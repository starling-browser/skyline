using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;

// Windowed tests as a console app, not MSTest: GLFW (via Cocoa) requires
// the main thread for window creation and event processing, and test
// runners execute on worker threads. Each Check is an assertion; any
// failure prints and flips the exit code.

var failures = 0;
var checks = 0;

void Check(bool condition, string what)
{
    checks++;
    if (condition) return;
    failures++;
    Console.Error.WriteLine($"FAIL: {what}");
}

// --- AppWindow basics -------------------------------------------------

var win = new AppWindow(new AppWindowOptions { Title = "windowed tests", Width = 320, Height = 240 });
Check(win.Title == "windowed tests", "Title reflects options");
win.Title = "renamed";
Check(win.Title == "renamed", "Title set round-trips");

var frame = win.CurrentFrame;
Check(frame.PixelWidth > 0 && frame.PixelHeight > 0, "CurrentFrame has pixel size");
Check(frame.Dpr > 0, "CurrentFrame has a device pixel ratio");
Check(Math.Abs(frame.LogicalWidth - frame.PixelWidth / frame.Dpr) < 0.01, "logical width derives from Dpr");
Check(win.Surface is not null, "native surface source is exposed");

win.ClipboardText = "skyline-test";
Check(win.ClipboardText == "skyline-test", "clipboard round-trips");

win.PumpEvents(); // must not throw without a host

var soloResized = false;
win.Resized += _ => soloResized = true;
win.Resize(360, 270);
win.PumpEvents();
Check(soloResized, "Resize fires Resized in single-window mode");
frame = win.CurrentFrame;

// --- Input raising (the seam below GLFW callbacks) ---------------------

PointerEvent? pointer = null;
KeyEvent? key = null;
TextEvent? text = null;
win.PointerInput += e => pointer = e;
win.KeyInput += e => key = e;
win.TextInput += e => text = e;

win.RaisePointer(PointerEventKind.Down, 10f, 20f, 0, 0, 0);
Check(pointer is { Kind: PointerEventKind.Down, X: 10f, Y: 20f, Button: 0 }, "RaisePointer reaches PointerInput");

win.RaiseKey(true, Silk.NET.Input.Key.Escape);
Check(key is { IsDown: true, Key: Key.Escape, Code: 256 }, "RaiseKey maps and reaches KeyInput");

win.RaiseKey(false, (Silk.NET.Input.Key)12345);
Check(key is { IsDown: false, Key: Key.Unknown, Code: 12345 }, "unmapped key reports Unknown but keeps the code");

win.RaiseText('é');
Check(text is { Character: 'é' }, "RaiseText reaches TextInput");

// --- GPU on a real window surface --------------------------------------

using (var gpu = GpuContext.Create(win.Surface!, surfaceOptions: new WindowSurfaceOptions
{
    ExtraUsage = TextureUsage.CopySrc,
}))
{
    var surface = gpu.Surface;
    Check(surface is not null, "windowed context exposes a surface");

    var caps = surface!.Capabilities;
    Check(caps.PresentModes.Length > 0, "capabilities report present modes");
    Check(caps.Supports(PresentMode.Fifo), "Fifo is supported everywhere");
    Check(ReferenceEquals(caps, surface.Capabilities), "capabilities are cached");

    surface.PresentMode = caps.ChoosePresentMode(PresentMode.Fifo);
    Check(surface.PresentMode == PresentMode.Fifo, "PresentMode property holds the choice");
    Check(surface.Format == TextureFormat.Bgra8Unorm, "Format reflects options");

    surface.Configure(frame.PixelWidth, frame.PixelHeight);
    Check(surface.PixelSize == (frame.PixelWidth, frame.PixelHeight), "Configure records pixel size");

    var presentWithoutAcquire = false;
    try { surface.Present(); } catch (InvalidOperationException) { presentWithoutAcquire = true; }
    Check(presentWithoutAcquire, "Present without an acquired frame throws");

    unsafe
    {
        Check(surface.SurfaceHandle != null, "raw surface handle is reachable");
        Check(!surface.HandleAcquireResult(new SurfaceTexture { Status = SurfaceGetCurrentTextureStatus.Timeout }),
            "stale acquire reconfigures and reports false");
    }
    var fatalAcquire = false;
    try { surface.HandleAcquireResult(new SurfaceTexture { Status = SurfaceGetCurrentTextureStatus.OutOfMemory }); }
    catch (InvalidOperationException) { fatalAcquire = true; }
    Check(fatalAcquire, "fatal acquire status throws");

    if (surface.TryAcquireFrame())
    {
        unsafe
        {
            Check(surface.CurrentTexture != null, "acquired frame exposes the texture");
            Check(surface.CurrentView != null, "acquired frame exposes the view");
        }

        var doubleAcquire = false;
        try { surface.TryAcquireFrame(); } catch (InvalidOperationException) { doubleAcquire = true; }
        Check(doubleAcquire, "double acquire throws");

        surface.CancelFrame(); // release without presenting
        Check(surface.TryAcquireFrame(), "acquire works again after CancelFrame");

        // Clear the swapchain texture and read it back: end-to-end proof
        // the windowed path renders what we asked.
        unsafe
        {
            var wgpu = gpu.Api;
            using var readback = new TextureReadback(gpu, surface.PixelSize.Width, surface.PixelSize.Height);
            var att = new RenderPassColorAttachment
            {
                View = surface.CurrentView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Silk.NET.WebGPU.Color { R = 1.0, G = 0.0, B = 0.0, A = 1.0 },
            };
            var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
            var enc = wgpu.DeviceCreateCommandEncoder(gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
            var pass = wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
            wgpu.RenderPassEncoderEnd(pass);
            wgpu.RenderPassEncoderRelease(pass);
            readback.Encode(enc, surface.CurrentTexture);
            var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
            wgpu.QueueSubmit(gpu.QueueHandle, 1, &cmd);
            wgpu.CommandBufferRelease(cmd);
            wgpu.CommandEncoderRelease(enc);
            var px = readback.Resolve();
            Check(px[2] >= 254 && px[0] <= 1, "swapchain clear is red in readback (BGRA)");
            surface.Present();
        }
    }
    else
    {
        Check(false, "could not acquire a swapchain frame");
    }

    // A second window on the same device.
    using var second = new AppWindow(new AppWindowOptions { Title = "second", Width = 160, Height = 120 });
    using var secondSurface = gpu.CreateSurface(second.Surface!);
    var sf = second.CurrentFrame;
    secondSurface.Configure(sf.PixelWidth, sf.PixelHeight);
    Check(secondSurface.TryAcquireFrame(), "second surface on the shared device acquires");
    secondSurface.CancelFrame();
}

win.Dispose();

// --- Single-window Run loop ---------------------------------------------

using (var solo = new AppWindow(new AppWindowOptions { Title = "solo", Width = 160, Height = 120 }))
{
    var frames = 0;
    var idleFrames = 0;
    solo.IsDirty = () => idleFrames++ >= 3; // exercise the idle-sleep path first
    solo.RenderFrame += f =>
    {
        frames++;
        if (frames >= 3) solo.RequestClose();
    };
    Check(solo.Run() == 0, "Run returns 0 after RequestClose");
    Check(frames >= 3, "RenderFrame fired through the built-in loop");
    Check(idleFrames > 3, "IsDirty=false frames skipped rendering");
}

// --- AppHost: two windows, render threads, invoke, close ----------------

using (var host = new AppHost())
{
    var winA = new AppWindow(new AppWindowOptions { Title = "host A", Width = 160, Height = 120 });
    var winB = new AppWindow(new AppWindowOptions { Title = "host B", Width = 160, Height = 120 });
    host.AddWindow(winA);
    host.AddWindow(winB);

    var doubleAdd = false;
    try { host.AddWindow(winA); } catch (InvalidOperationException) { doubleAdd = true; }
    Check(doubleAdd, "adding a window to a host twice throws");

    var hostedRun = false;
    try { winA.Run(); } catch (InvalidOperationException) { hostedRun = true; }
    Check(hostedRun, "Run on a hosted window throws");

    var mainThread = Environment.CurrentManagedThreadId;
    int renderThreadA = 0, renderThreadB = 0, framesA = 0, framesB = 0;
    var invokeRanOnMain = 0;
    var closedBeforeDispose = 0;
    var framesAAtRestore = int.MaxValue;
    var restoredA = false;
    var idledOnceB = false;
    var hostedResizeThread = 0;

    winB.Resized += _ => hostedResizeThread = Environment.CurrentManagedThreadId;
    winB.IsDirty = () =>
    {
        // Return false exactly once to walk the host's idle-wait path.
        if (framesB == 3 && !idledOnceB)
        {
            idledOnceB = true;
            winB.RequestRedraw();
            return false;
        }
        return true;
    };

    winA.RenderFrame += _ =>
    {
        renderThreadA = Environment.CurrentManagedThreadId;
        framesA++;
        Thread.Sleep(5); // pace so main-thread invokes interleave deterministically
        if (framesA == 2) host.Invoke(() => invokeRanOnMain = Environment.CurrentManagedThreadId);
        if (framesA == 3) host.Invoke(winA.Minimize);
        if (framesA >= 10 && restoredA) winA.RequestClose();
    };
    winB.RenderFrame += _ =>
    {
        renderThreadB = Environment.CurrentManagedThreadId;
        framesB++;
        Thread.Sleep(5);
        if (framesB == 10) host.Invoke(() => winB.Resize(220, 160));
        if (framesB == 30)
        {
            // Window A has been minimized for ~135 ms of 5 ms frames: its
            // render thread must have parked. Restore it and record how
            // far it got.
            host.Invoke(() =>
            {
                framesAAtRestore = framesA;
                winA.Restore();
                restoredA = true;
            });
        }
        // Close once the resize round-trip has been observed (Cocoa can
        // defer the resize callback through the window-open animation),
        // with a generous cap so a real failure fails instead of hanging.
        if ((framesB >= 40 && hostedResizeThread != 0) || framesB >= 600) winB.RequestClose();
    };
    host.WindowClosed += w =>
    {
        // The window must still be alive here: Title is a native call.
        _ = w.Title;
        closedBeforeDispose++;
    };

    winA.RequestRedraw(); // any-thread wake is callable before Run too
    Check(host.Run() == 0, "AppHost.Run returns when all windows close");

    Check(framesA >= 10 && framesB >= 40 && framesB < 600, "both windows rendered on the host");
    Check(renderThreadA != mainThread, "window A renders off the main thread");
    Check(renderThreadB != mainThread, "window B renders off the main thread");
    Check(renderThreadA != renderThreadB, "windows render on separate threads");
    Check(invokeRanOnMain == mainThread, "Invoke runs on the main thread");
    Check(framesAAtRestore < 10, "a minimized window stops rendering");
    Check(idledOnceB, "the idle-wait path ran");
    Check(hostedResizeThread == renderThreadB, "hosted Resized fires on the render thread");
    Check(closedBeforeDispose == 2, "WindowClosed fired for both windows before dispose");
}

// --- AppHost edge paths --------------------------------------------------

{
    // A throwing Invoke action must still clean up.
    var hostX = new AppHost();
    var winX = new AppWindow(new AppWindowOptions { Title = "boom", Width = 160, Height = 120 });
    hostX.AddWindow(winX);
    var closedX = false;
    hostX.WindowClosed += _ => closedX = true;
    hostX.Invoke(() => throw new InvalidOperationException("boom"));
    var threw = false;
    try { hostX.Run(); } catch (InvalidOperationException) { threw = true; }
    Check(threw, "an Invoke exception propagates out of Run");
    Check(closedX, "windows are retired despite the exception");
    hostX.Dispose();
}

{
    // Dispose without ever running retires cleanly.
    var hostY = new AppHost();
    var winY = new AppWindow(new AppWindowOptions { Title = "never run", Width = 160, Height = 120 });
    hostY.AddWindow(winY);
    var closedY = false;
    hostY.WindowClosed += _ => closedY = true;
    hostY.Dispose();
    Check(closedY, "Dispose retires windows that never ran");
}

Console.WriteLine(failures == 0
    ? $"WINDOWED TESTS OK: {checks} checks passed"
    : $"WINDOWED TESTS FAILED: {failures} of {checks} checks");
return failures == 0 ? 0 : 1;
