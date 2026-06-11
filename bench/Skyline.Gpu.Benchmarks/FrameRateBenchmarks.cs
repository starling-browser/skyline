using BenchmarkDotNet.Attributes;
using Silk.NET.WebGPU;
using Skyline;

namespace Skyline.Gpu.Benchmarks;

/// <summary>
/// Two frame loops, same render work, paced at two frames in flight.
///
/// <c>Frame</c> is the full windowed path: pump events, pace, acquire,
/// render, submit, present. Even in Immediate mode, macOS hands out
/// drawables at the panel's refresh rate, so this measures the display
/// ceiling — on a 120 Hz panel it reads 8.3 ms no matter how light the
/// work is.
///
/// <c>OffscreenFrame</c> renders the same passes to an offscreen texture
/// with no present, so nothing display-side throttles it. This is the
/// stack's own sustainable rate — the number that answers "could this
/// keep up with a 240 Hz panel".
///
/// Program.cs converts both means to FPS against the standard tiers
/// (60/120/144/240) after the run.
/// </summary>
[MemoryDiagnoser]
public unsafe class FrameRateBenchmarks
{
    private AppWindow _win = null!;
    private GpuContext _gpu = null!;
    private WindowSurface _surface = null!;
    private FramePacer _pacer = null!;
    private Texture* _offscreen;
    private TextureView* _offscreenView;

    /// <summary>Render passes per frame: 1 is a minimal frame, 8 a busier one.</summary>
    [Params(1, 8)]
    public int PassesPerFrame;

    [GlobalSetup]
    public void Setup()
    {
        _win = new AppWindow(new AppWindowOptions
        {
            Title = "Skyline frame-rate bench",
            Width = 1280,
            Height = 720,
            Resizable = false,
        });
        _gpu = GpuContext.Create(_win.Surface);
        _surface = _gpu.Surface!;
        _surface.PresentMode = _surface.Capabilities.ChoosePresentMode(PresentMode.Immediate, PresentMode.Mailbox);
        var frame = _win.CurrentFrame;
        _surface.Configure(frame.PixelWidth, frame.PixelHeight);
        _pacer = new FramePacer(_gpu, maxFramesInFlight: 2);

        // Same size and format as the swapchain, so both benchmarks rasterize
        // the same pixel count.
        var desc = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Format = _surface.Format,
            Size = new Extent3D { Width = (uint)frame.PixelWidth, Height = (uint)frame.PixelHeight, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Usage = TextureUsage.RenderAttachment,
        };
        _offscreen = _gpu.Api.DeviceCreateTexture(_gpu.DeviceHandle, in desc);
        _offscreenView = _gpu.Api.TextureCreateView(_offscreen, (TextureViewDescriptor*)null);

        Console.WriteLine($"// frame-rate bench: present mode {_surface.PresentMode}, {frame.PixelWidth}x{frame.PixelHeight}");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pacer.Dispose();
        if (_offscreenView != null) _gpu.Api.TextureViewRelease(_offscreenView);
        if (_offscreen != null) _gpu.Api.TextureRelease(_offscreen);
        _gpu.Dispose();
        _win.Dispose();
    }

    [Benchmark]
    public void Frame()
    {
        _win.PumpEvents();
        _pacer.Wait();

        var acquired = false;
        for (var attempt = 0; attempt < 8 && !(acquired = _surface.TryAcquireFrame()); attempt++) { }
        if (!acquired)
            throw new InvalidOperationException("could not acquire a frame in 8 attempts");

        EncodeAndSubmit(_surface.CurrentView);
        _surface.Present();
    }

    [Benchmark]
    public void OffscreenFrame()
    {
        _pacer.Wait();
        EncodeAndSubmit(_offscreenView);
    }

    private void EncodeAndSubmit(TextureView* target)
    {
        var wgpu = _gpu.Api;
        var enc = wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        for (var i = 0; i < PassesPerFrame; i++)
        {
            var att = new RenderPassColorAttachment
            {
                View = target,
                LoadOp = i == 0 ? LoadOp.Clear : LoadOp.Load,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0.1, G = 0.2, B = 0.3, A = 1.0 },
            };
            var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
            var pass = wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
            wgpu.RenderPassEncoderEnd(pass);
            wgpu.RenderPassEncoderRelease(pass);
        }
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(_gpu.QueueHandle, 1, &cmd);
        _pacer.FrameSubmitted();
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);
    }
}
