using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;

// The proof: Skyline owns the window, loop, and input; Skyline.Gpu owns the
// device chain, swapchain, and present mechanics; this sample owns what it
// draws — a clear pass and a HUD copy encoded with raw wgpu through
// Skyline.Gpu's escape hatches.
//
// Move the pointer to steer the clear color, press Space to toggle a slow
// hue cycle, press Escape to quit. Pass --frames N to auto-close after N
// presented frames (smoke test).

if (Array.IndexOf(args, "--dump-hud") >= 0)
    return HelloWindow.DumpFont.Run();

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
    _ = int.TryParse(args[argIdx + 1], out maxFrames);

// --verify-hud: on the final frame, read the rendered pixels back from the
// GPU and assert the HUD panel is actually in the presented image.
var verifyHud = Array.IndexOf(args, "--verify-hud") >= 0;
if (verifyHud && maxFrames <= 0) maxFrames = 30;
string? verifyReport = null;

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline — hello window", Width = 800, Height = 600 });

using var gpu = new WgpuClearRenderer(win.Surface, win.CurrentFrame);

var pointerX = 0.5f;
var pointerY = 0.5f;
var animate = false;
var hue = 0f;
// After any change, present a short burst of frames rather than exactly
// one: early presents can land while the window is still being mapped /
// composited by the OS, and a single frame rendered then never becomes
// visible. ~30 frames (~0.5s) comfortably outlives window setup.
var dirtyFrames = 30;
var presented = 0;

win.Resized += f =>
{
    gpu.Configure(f.PixelWidth, f.PixelHeight);
    dirtyFrames = 30; // reconfigure discards surface contents; redraw
};

win.PointerInput += e =>
{
    if (e.Kind != PointerEventKind.Move) return;
    var f = win.CurrentFrame;
    pointerX = Math.Clamp(e.X / Math.Max(1f, f.LogicalWidth), 0f, 1f);
    pointerY = Math.Clamp(e.Y / Math.Max(1f, f.LogicalHeight), 0f, 1f);
    dirtyFrames = Math.Max(dirtyFrames, 3);
};

win.KeyInput += e =>
{
    if (!e.IsDown) return;
    if (e.Key == Key.Escape) win.RequestClose();
    if (e.Key == Key.Space) { animate = !animate; dirtyFrames = Math.Max(dirtyFrames, 3); }
};

// Render only when something changed: a smoke run (--frames) free-runs so it
// finishes, otherwise idle frames sleep inside Skyline's loop.
win.IsDirty = () => maxFrames > 0 || animate || dirtyFrames > 0;

win.RenderFrame += f =>
{
    if (animate) hue = (hue + (float)f.DeltaSeconds * 0.2f) % 1f;
    var r = pointerX;
    var g = (hue + pointerY * 0.5f) % 1f;
    var b = 0.45f;
    string[] hud =
    [
        "MOVE POINTER: COLOR   SPACE: HUE CYCLE " + (animate ? "ON " : "OFF") + "   ESC: QUIT",
        $"R {r:0.00}   G {g:0.00}   B {b:0.00}   HUE {hue:0.00}",
    ];
    var isLast = maxFrames > 0 && presented == maxFrames - 1;
    if (!gpu.RenderClear(r, g, b, hud, f.Dpr, readbackHud: verifyHud && isLast)) return; // stay dirty; retry next frame
    if (verifyHud && isLast) verifyReport = gpu.LastVerifyReport;
    if (dirtyFrames > 0) dirtyFrames--;
    presented++;
    if (maxFrames > 0 && presented >= maxFrames) win.RequestClose();
};

var code = win.Run();
Console.WriteLine($"HELLO OK: presented {presented} frames via Skyline window + Skyline.Gpu");
if (verifyHud)
{
    Console.WriteLine(verifyReport ?? "HUD VERIFY FAILED: no readback ran");
    if (verifyReport?.StartsWith("HUD VERIFY OK") != true) return 1;
}
return presented > 0 ? code : 1;

/// <summary>
/// The sample's renderer. Skyline.Gpu provides the device, swapchain, and
/// present; this class encodes its own work — a clear pass plus the HUD
/// texture copy — with raw wgpu via the escape hatches. This is the part a
/// real app would replace with its own engine.
/// </summary>
internal sealed unsafe class WgpuClearRenderer : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly WindowSurface _surface;
    private const TextureFormat Format = TextureFormat.Bgra8Unorm;

    public WgpuClearRenderer(Silk.NET.Core.Contexts.INativeWindowSource surfaceSource, FrameInfo frame)
    {
        _gpu = GpuContext.Create(surfaceSource, surfaceOptions: new WindowSurfaceOptions
        {
            Format = Format,
            // CopyDst: the HUD panel is copied into the acquired swapchain
            // texture (no shader pipeline in this sample). CopySrc: lets
            // --verify-hud read the final frame back for pixel assertions.
            ExtraUsage = TextureUsage.CopyDst | TextureUsage.CopySrc,
        });
        _gpu.UncapturedError += (type, msg) => Console.Error.WriteLine($"wgpu error ({type}): {msg}");
        _surface = _gpu.Surface!;
        Configure(frame.PixelWidth, frame.PixelHeight);
    }

    public void Configure(int pixelWidth, int pixelHeight) =>
        _surface.Configure(pixelWidth, pixelHeight);

    // HUD panel kept in a persistent GPU texture. The pixels are uploaded
    // only when the text changes; every frame then copies texture→texture
    // inside the same command submission as the clear pass. Folding the
    // copy into the submitted encoder is what guarantees the HUD lands on
    // the frame being presented — a bare QueueWriteTexture is allowed to
    // execute with the NEXT submit, which made the last frame before the
    // app went idle (the one that stays on screen) lose its overlay.
    private string? _hudKey;
    private Texture* _hudTexture;
    private (int W, int H) _hudSize;

    /// <summary>Report from the last readback-enabled frame (see --verify-hud).</summary>
    public string? LastVerifyReport { get; private set; }

    public bool RenderClear(float r, float g, float b, string[] hudLines, float dpr, bool readbackHud = false)
    {
        if (!_surface.TryAcquireFrame())
            return false; // swapchain stale; Skyline.Gpu reconfigured, retry next frame

        EnsureHudTexture(hudLines, dpr);

        var wgpu = _gpu.Api;
        var att = new RenderPassColorAttachment
        {
            View = _surface.CurrentView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color { R = r, G = g % 1f, B = b, A = 1.0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };

        var enc = wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        wgpu.RenderPassEncoderEnd(pass);
        wgpu.RenderPassEncoderRelease(pass);
        EncodeHudCopy(enc, _surface.CurrentTexture, dpr);

        TextureReadback? readback = null;
        if (readbackHud)
        {
            readback = new TextureReadback(_gpu, _surface.PixelSize.Width, _surface.PixelSize.Height);
            readback.Encode(enc, _surface.CurrentTexture);
        }

        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(_gpu.QueueHandle, 1, &cmd);
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);

        if (readback != null)
        {
            LastVerifyReport = CheckHudPixels(readback.Resolve(), dpr);
            readback.Dispose();
        }

        _surface.Present();
        return true;
    }

    private string CheckHudPixels(byte[] pixels, float dpr)
    {
        var (w, h) = _surface.PixelSize;
        var margin = (int)MathF.Round(16 * dpr);

        // Panel background probe: a point inside the panel's padding, which
        // TextOverlay fills with BGRA (24, 22, 20, 255).
        var bo = ((margin + 6) * w + margin + 6) * 4;
        var bgOk = pixels[bo] == 24 && pixels[bo + 1] == 22 && pixels[bo + 2] == 20;

        // Text probe: count near-white pixels across the panel area.
        var textPixels = 0;
        for (var y = margin; y < Math.Min(margin + _hudSize.H, h); y++)
        for (var x = margin; x < Math.Min(margin + _hudSize.W, w); x++)
        {
            var o = (y * w + x) * 4;
            if (pixels[o] > 200 && pixels[o + 1] > 200 && pixels[o + 2] > 200) textPixels++;
        }

        return bgOk && textPixels > 100
            ? $"HUD VERIFY OK: panel background present, {textPixels} text pixels in frame"
            : $"HUD VERIFY FAILED: bgOk={bgOk}, textPixels={textPixels}";
    }

    private void EnsureHudTexture(string[] lines, float dpr)
    {
        var key = string.Join('\n', lines) + "@" + dpr;
        if (key == _hudKey && _hudTexture != null)
            return;
        _hudKey = key;

        var wgpu = _gpu.Api;
        var (w, h, px) = HelloWindow.TextOverlay.Render(lines, (int)MathF.Round(2 * dpr));
        if (_hudTexture == null || _hudSize != (w, h))
        {
            if (_hudTexture != null) wgpu.TextureRelease(_hudTexture);
            var desc = new TextureDescriptor
            {
                Dimension = TextureDimension.Dimension2D,
                Format = Format,
                Size = new Extent3D { Width = (uint)w, Height = (uint)h, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = 1,
                Usage = TextureUsage.CopyDst | TextureUsage.CopySrc,
            };
            _hudTexture = wgpu.DeviceCreateTexture(_gpu.DeviceHandle, in desc);
            _hudSize = (w, h);
        }

        var dst = new ImageCopyTexture { Texture = _hudTexture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)(w * 4), RowsPerImage = (uint)h };
        var extent = new Extent3D { Width = (uint)w, Height = (uint)h, DepthOrArrayLayers = 1 };
        // This write is ordered before any later QueueSubmit, so the copy
        // encoded below always reads the up-to-date panel.
        fixed (byte* p = px)
            wgpu.QueueWriteTexture(_gpu.QueueHandle, in dst, p, (nuint)px.Length, in layout, in extent);
    }

    private void EncodeHudCopy(CommandEncoder* enc, Texture* surfaceTexture, float dpr)
    {
        if (_hudTexture == null)
            return;
        var (sw, sh) = _surface.PixelSize;
        var margin = (int)MathF.Round(16 * dpr);
        if (margin + _hudSize.W > sw || margin + _hudSize.H > sh)
            return; // window too small for the panel; skip rather than clip

        var src = new ImageCopyTexture { Texture = _hudTexture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var dst = new ImageCopyTexture
        {
            Texture = surfaceTexture,
            MipLevel = 0,
            Origin = new Origin3D { X = (uint)margin, Y = (uint)margin, Z = 0 },
            Aspect = TextureAspect.All,
        };
        var extent = new Extent3D { Width = (uint)_hudSize.W, Height = (uint)_hudSize.H, DepthOrArrayLayers = 1 };
        _gpu.Api.CommandEncoderCopyTextureToTexture(enc, in src, in dst, in extent);
    }

    public void Dispose()
    {
        if (_hudTexture != null) _gpu.Api.TextureRelease(_hudTexture);
        _gpu.Dispose(); // disposes the surface too
    }
}
