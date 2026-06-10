using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Skyline;
using Skyline.Input;

// The proof: Skyline owns the window, loop, and input; this sample owns
// rendering end to end with raw wgpu. Skyline never sees a pixel.
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
Console.WriteLine($"HELLO OK: presented {presented} frames via Skyline window + raw wgpu");
if (verifyHud)
{
    Console.WriteLine(verifyReport ?? "HUD VERIFY FAILED: no readback ran");
    if (verifyReport?.StartsWith("HUD VERIFY OK") != true) return 1;
}
return presented > 0 ? code : 1;

/// <summary>
/// The sample's renderer: wgpu instance/device/swapchain built from
/// Skyline's surface handle, presenting a solid clear color. This class is
/// the part a real app would replace with its own engine.
/// </summary>
internal sealed unsafe class WgpuClearRenderer : IDisposable
{
    private readonly WebGPU _wgpu;
    private readonly Instance* _instance;
    private readonly Adapter* _adapter;
    private readonly Device* _device;
    private readonly Queue* _queue;
    private readonly Surface* _surface;
    private const TextureFormat Format = TextureFormat.Bgra8Unorm;

    public WgpuClearRenderer(Silk.NET.Core.Contexts.INativeWindowSource surfaceSource, FrameInfo frame)
    {
        _wgpu = WebGPU.GetApi();

        var instDesc = default(InstanceDescriptor);
        _instance = _wgpu.CreateInstance(in instDesc);
        if (_instance == null) throw new InvalidOperationException("wgpu CreateInstance failed");

        _surface = surfaceSource.CreateWebGPUSurface(_wgpu, _instance);
        if (_surface == null) throw new InvalidOperationException("wgpu surface creation failed");

        Adapter* adapter = null;
        var adapterOpts = new RequestAdapterOptions { CompatibleSurface = _surface, PowerPreference = PowerPreference.HighPerformance };
        var aCb = PfnRequestAdapterCallback.From((status, a, _, _) => { if (status == RequestAdapterStatus.Success) adapter = a; });
        _wgpu.InstanceRequestAdapter(_instance, in adapterOpts, aCb, null);
        _adapter = adapter;
        if (_adapter == null) throw new InvalidOperationException("no wgpu adapter");

        Device* device = null;
        var devDesc = default(DeviceDescriptor);
        var dCb = PfnRequestDeviceCallback.From((status, d, _, _) => { if (status == RequestDeviceStatus.Success) device = d; });
        _wgpu.AdapterRequestDevice(_adapter, in devDesc, dCb, null);
        _device = device;
        if (_device == null) throw new InvalidOperationException("no wgpu device");

        _queue = _wgpu.DeviceGetQueue(_device);
        Configure(frame.PixelWidth, frame.PixelHeight);
    }

    public void Configure(int pixelWidth, int pixelHeight)
    {
        var config = new SurfaceConfiguration
        {
            Device = _device,
            Format = Format,
            // CopyDst: the HUD panel is copied into the acquired swapchain
            // texture (no shader pipeline in this sample). CopySrc: lets
            // --verify-hud read the final frame back for pixel assertions.
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopyDst | TextureUsage.CopySrc,
            Width = (uint)Math.Max(1, pixelWidth),
            Height = (uint)Math.Max(1, pixelHeight),
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto,
        };
        _wgpu.SurfaceConfigure(_surface, in config);
        _surfaceSize = (Math.Max(1, pixelWidth), Math.Max(1, pixelHeight));
    }

    private (int W, int H) _surfaceSize;

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
        SurfaceTexture st = default;
        _wgpu.SurfaceGetCurrentTexture(_surface, ref st);
        if (st.Status != SurfaceGetCurrentTextureStatus.Success)
            return false; // surface mid-reconfigure; next frame will succeed

        EnsureHudTexture(hudLines, dpr);

        var view = _wgpu.TextureCreateView(st.Texture, (TextureViewDescriptor*)null);
        var att = new RenderPassColorAttachment
        {
            View = view,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color { R = r, G = g % 1f, B = b, A = 1.0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };

        var enc = _wgpu.DeviceCreateCommandEncoder(_device, (CommandEncoderDescriptor*)null);
        var pass = _wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);
        EncodeHudCopy(enc, st.Texture, dpr);

        Silk.NET.WebGPU.Buffer* readback = null;
        var rowPitch = 0;
        if (readbackHud)
            readback = EncodeReadback(enc, st.Texture, out rowPitch);

        var cmd = _wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        _wgpu.QueueSubmit(_queue, 1, &cmd);
        _wgpu.CommandBufferRelease(cmd);
        _wgpu.CommandEncoderRelease(enc);

        if (readback != null)
        {
            LastVerifyReport = CheckHudPixels(readback, rowPitch, dpr);
            _wgpu.BufferRelease(readback);
        }

        _wgpu.SurfacePresent(_surface);
        _wgpu.TextureViewRelease(view);
        _wgpu.TextureRelease(st.Texture);
        return true;
    }

    private Silk.NET.WebGPU.Buffer* EncodeReadback(CommandEncoder* enc, Texture* surfaceTexture, out int rowPitch)
    {
        // Copy the composed frame (clear + HUD) to a mappable buffer inside
        // the same submission, so what we assert on is exactly what presents.
        rowPitch = (_surfaceSize.W * 4 + 255) & ~255; // wgpu requires 256-byte row alignment
        var desc = new BufferDescriptor
        {
            Size = (ulong)(rowPitch * _surfaceSize.H),
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
        };
        var buffer = _wgpu.DeviceCreateBuffer(_device, in desc);
        var src = new ImageCopyTexture { Texture = surfaceTexture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var dst = new ImageCopyBuffer
        {
            Buffer = buffer,
            Layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)rowPitch, RowsPerImage = (uint)_surfaceSize.H },
        };
        var extent = new Extent3D { Width = (uint)_surfaceSize.W, Height = (uint)_surfaceSize.H, DepthOrArrayLayers = 1 };
        _wgpu.CommandEncoderCopyTextureToBuffer(enc, in src, in dst, in extent);
        return buffer;
    }

    private string CheckHudPixels(Silk.NET.WebGPU.Buffer* buffer, int rowPitch, float dpr)
    {
        var size = (nuint)(rowPitch * _surfaceSize.H);
        var mapped = false;
        var failed = false;
        var cb = PfnBufferMapCallback.From((status, _) =>
        {
            if (status == BufferMapAsyncStatus.Success) mapped = true; else failed = true;
        });
        _wgpu.BufferMapAsync(buffer, MapMode.Read, 0, size, cb, null);

        if (!_wgpu.TryGetDeviceExtension(null, out Wgpu native))
            return "HUD VERIFY FAILED: wgpu-native poll extension unavailable";
        while (!mapped && !failed)
            native.DevicePoll(_device, true, null);
        if (failed)
            return "HUD VERIFY FAILED: buffer map failed";

        var data = (byte*)_wgpu.BufferGetConstMappedRange(buffer, 0, size);
        var margin = (int)MathF.Round(16 * dpr);

        // Panel background probe: a point inside the panel's padding, which
        // TextOverlay fills with BGRA (24, 22, 20, 255).
        var bx = margin + 6;
        var by = margin + 6;
        var bo = by * rowPitch + bx * 4;
        var bgOk = data[bo] == 24 && data[bo + 1] == 22 && data[bo + 2] == 20;

        // Text probe: count near-white pixels across the panel area.
        var textPixels = 0;
        for (var y = margin; y < Math.Min(margin + _hudSize.H, _surfaceSize.H); y++)
        for (var x = margin; x < Math.Min(margin + _hudSize.W, _surfaceSize.W); x++)
        {
            var o = y * rowPitch + x * 4;
            if (data[o] > 200 && data[o + 1] > 200 && data[o + 2] > 200) textPixels++;
        }

        _wgpu.BufferUnmap(buffer);

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

        var (w, h, px) = HelloWindow.TextOverlay.Render(lines, (int)MathF.Round(2 * dpr));
        if (_hudTexture == null || _hudSize != (w, h))
        {
            if (_hudTexture != null) _wgpu.TextureRelease(_hudTexture);
            var desc = new TextureDescriptor
            {
                Dimension = TextureDimension.Dimension2D,
                Format = Format,
                Size = new Extent3D { Width = (uint)w, Height = (uint)h, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = 1,
                Usage = TextureUsage.CopyDst | TextureUsage.CopySrc,
            };
            _hudTexture = _wgpu.DeviceCreateTexture(_device, in desc);
            _hudSize = (w, h);
        }

        var dst = new ImageCopyTexture { Texture = _hudTexture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)(w * 4), RowsPerImage = (uint)h };
        var extent = new Extent3D { Width = (uint)w, Height = (uint)h, DepthOrArrayLayers = 1 };
        // This write is ordered before any later QueueSubmit, so the copy
        // encoded below always reads the up-to-date panel.
        fixed (byte* p = px)
            _wgpu.QueueWriteTexture(_queue, in dst, p, (nuint)px.Length, in layout, in extent);
    }

    private void EncodeHudCopy(CommandEncoder* enc, Texture* surfaceTexture, float dpr)
    {
        if (_hudTexture == null)
            return;
        var margin = (int)MathF.Round(16 * dpr);
        if (margin + _hudSize.W > _surfaceSize.W || margin + _hudSize.H > _surfaceSize.H)
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
        _wgpu.CommandEncoderCopyTextureToTexture(enc, in src, in dst, in extent);
    }

    public void Dispose()
    {
        if (_hudTexture != null) _wgpu.TextureRelease(_hudTexture);
        if (_surface != null) _wgpu.SurfaceRelease(_surface);
        if (_queue != null) _wgpu.QueueRelease(_queue);
        if (_device != null) _wgpu.DeviceRelease(_device);
        if (_adapter != null) _wgpu.AdapterRelease(_adapter);
        if (_instance != null) _wgpu.InstanceRelease(_instance);
    }
}
