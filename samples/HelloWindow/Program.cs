using Silk.NET.WebGPU;
using Skyline;
using Skyline.Input;

// The proof: Skyline owns the window, loop, and input; this sample owns
// rendering end to end with raw wgpu. Skyline never sees a pixel.
//
// Move the pointer to steer the clear color, press Space to toggle a slow
// hue cycle, press Escape to quit. Pass --frames N to auto-close after N
// presented frames (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
    _ = int.TryParse(args[argIdx + 1], out maxFrames);

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline — hello window", Width = 800, Height = 600 });

using var gpu = new WgpuClearRenderer(win.Surface, win.CurrentFrame);

var pointerX = 0.5f;
var pointerY = 0.5f;
var animate = false;
var hue = 0f;
var inputDirty = true;
var presented = 0;

win.Resized += f => gpu.Configure(f.PixelWidth, f.PixelHeight);

win.PointerInput += e =>
{
    if (e.Kind != PointerEventKind.Move) return;
    var f = win.CurrentFrame;
    pointerX = Math.Clamp(e.X / Math.Max(1f, f.LogicalWidth), 0f, 1f);
    pointerY = Math.Clamp(e.Y / Math.Max(1f, f.LogicalHeight), 0f, 1f);
    inputDirty = true;
};

win.KeyInput += e =>
{
    if (!e.IsDown) return;
    if (e.Key == Key.Escape) win.RequestClose();
    if (e.Key == Key.Space) { animate = !animate; inputDirty = true; }
};

// Render only when something changed: a smoke run (--frames) free-runs so it
// finishes, otherwise idle frames sleep inside Skyline's loop.
win.IsDirty = () => maxFrames > 0 || animate || inputDirty;

win.RenderFrame += f =>
{
    inputDirty = false;
    if (animate) hue = (hue + (float)f.DeltaSeconds * 0.2f) % 1f;
    if (!gpu.RenderClear(pointerX, hue + pointerY * 0.5f, 0.45f)) return;
    presented++;
    if (maxFrames > 0 && presented >= maxFrames) win.RequestClose();
};

var code = win.Run();
Console.WriteLine($"HELLO OK: presented {presented} frames via Skyline window + raw wgpu");
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
            Usage = TextureUsage.RenderAttachment,
            Width = (uint)Math.Max(1, pixelWidth),
            Height = (uint)Math.Max(1, pixelHeight),
            PresentMode = PresentMode.Fifo,
            AlphaMode = CompositeAlphaMode.Auto,
        };
        _wgpu.SurfaceConfigure(_surface, in config);
    }

    public bool RenderClear(float r, float g, float b)
    {
        SurfaceTexture st = default;
        _wgpu.SurfaceGetCurrentTexture(_surface, ref st);
        if (st.Status != SurfaceGetCurrentTextureStatus.Success)
            return false; // surface mid-reconfigure; next frame will succeed

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
        var cmd = _wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        _wgpu.QueueSubmit(_queue, 1, &cmd);
        _wgpu.CommandBufferRelease(cmd);
        _wgpu.CommandEncoderRelease(enc);
        _wgpu.SurfacePresent(_surface);
        _wgpu.TextureViewRelease(view);
        _wgpu.TextureRelease(st.Texture);
        return true;
    }

    public void Dispose()
    {
        if (_surface != null) _wgpu.SurfaceRelease(_surface);
        if (_queue != null) _wgpu.QueueRelease(_queue);
        if (_device != null) _wgpu.DeviceRelease(_device);
        if (_adapter != null) _wgpu.AdapterRelease(_adapter);
        if (_instance != null) _wgpu.InstanceRelease(_instance);
    }
}
