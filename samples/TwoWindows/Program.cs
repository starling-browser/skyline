using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;

// Two windows, one GPU device, one render thread each. The AppHost pumps
// events on the main thread while each window draws and presents on its
// own thread — so neither window's vsync wait can stall the other.
//
// Escape closes a window. Pass --frames N to auto-close both after N
// presented frames each (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
    _ = int.TryParse(args[argIdx + 1], out maxFrames);

using var host = new AppHost();

var winA = new AppWindow(new AppWindowOptions { Title = "Skyline two windows — A", Width = 480, Height = 360 });
var winB = new AppWindow(new AppWindowOptions { Title = "Skyline two windows — B", Width = 480, Height = 360 });

// One device serves both windows: the chain is built once against window
// A, and window B gets a second surface on the same context.
using var gpu = GpuContext.Create(winA.Surface, surfaceOptions: new WindowSurfaceOptions());
var surfaceB = gpu.CreateSurface(winB.Surface);

var a = new WindowRenderer(gpu, gpu.Surface!, winA, tint: (0.85f, 0.30f, 0.20f), maxFrames);
var b = new WindowRenderer(gpu, surfaceB, winB, tint: (0.20f, 0.45f, 0.85f), maxFrames);

host.AddWindow(winA);
host.AddWindow(winB);

// Release each window's GPU surface when it closes — its render thread
// has already retired, and the surface must go before the window does.
host.WindowClosed += w =>
{
    if (w == winA) { a.Dispose(); gpu.Surface!.Dispose(); }
    if (w == winB) { b.Dispose(); surfaceB.Dispose(); }
};

host.Run();

Console.WriteLine($"TWO WINDOWS OK: presented A={a.Presented} B={b.Presented} on separate render threads");
return a.Presented > 0 && b.Presented > 0 ? 0 : 1;

/// <summary>
/// Per-window renderer. Everything here runs on that window's render
/// thread: pacing, acquire, a clear pass, present. The device and queue
/// are shared with the other window — wgpu allows that across threads.
/// </summary>
internal sealed unsafe class WindowRenderer : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly WindowSurface _surface;
    private readonly FramePacer _pacer;
    private readonly (float R, float G, float B) _tint;
    private readonly int _maxFrames;
    private int _presented;

    public int Presented => _presented;

    public WindowRenderer(GpuContext gpu, WindowSurface surface, AppWindow window, (float, float, float) tint, int maxFrames)
    {
        _gpu = gpu;
        _surface = surface;
        _tint = tint;
        _maxFrames = maxFrames;
        _pacer = new FramePacer(gpu, maxFramesInFlight: 2);

        var frame = window.CurrentFrame;
        _surface.Configure(frame.PixelWidth, frame.PixelHeight);

        window.Resized += f => _surface.Configure(f.PixelWidth, f.PixelHeight);
        window.KeyInput += e =>
        {
            if (e.IsDown && e.Key == Skyline.Input.Key.Escape) window.RequestClose();
        };
        window.RenderFrame += f =>
        {
            if (!RenderFrame(f)) return;
            if (_maxFrames > 0 && _presented >= _maxFrames) window.RequestClose();
        };
    }

    private bool RenderFrame(FrameInfo frame)
    {
        _pacer.Wait();
        if (!_surface.TryAcquireFrame())
            return false;

        // Pulse the clear color so motion is visible in both windows.
        var pulse = 0.5f + 0.5f * MathF.Sin(_presented * 0.05f);
        var wgpu = _gpu.Api;
        var att = new RenderPassColorAttachment
        {
            View = _surface.CurrentView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = _tint.R * pulse, G = _tint.G * pulse, B = _tint.B * pulse, A = 1.0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
        var enc = wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        wgpu.RenderPassEncoderEnd(pass);
        wgpu.RenderPassEncoderRelease(pass);
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(_gpu.QueueHandle, 1, &cmd);
        _pacer.FrameSubmitted();
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);
        _surface.Present();
        Interlocked.Increment(ref _presented);
        return true;
    }

    public void Dispose() => _pacer.Dispose();
}
