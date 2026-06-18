using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using Skyline.Interaction;
using Skyline.Interaction.Ui;
using Skyline.Render;

// Windowed tests as a console app, not MSTest: GLFW (via Cocoa) requires
// the main thread for window creation and event processing, and test
// runners execute on worker threads. Each Check is an assertion; any
// failure prints and flips the exit code.

var failures = 0;
var checks = 0;

void Check(bool condition, string what)
{
    checks++;
    if (condition)
    {
        return;
    }

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
Check(win.RefreshRate > 0, "RefreshRate reports a positive display rate");

win.ClipboardText = "skyline-test";
Check(win.ClipboardText == "skyline-test", "clipboard round-trips");

win.PumpEvents(); // must not throw without a host

// --- Chrome modes construct on the GLFW backend ------------------------

foreach (var chrome in new[] { ChromeMode.Fixed, ChromeMode.Borderless, ChromeMode.Transparent })
{
    using var chromed = new AppWindow(new AppWindowOptions
    {
        Title = $"chrome {chrome}",
        Width = 160,
        Height = 120,
        Chrome = chrome,
        ForceGlfw = true,
    });
    var cf = chromed.CurrentFrame;
    Check(cf.PixelWidth > 0 && cf.PixelHeight > 0, $"{chrome} window constructs with a real surface");
    Check(chromed.RefreshRate > 0, $"{chrome} GLFW window reports a refresh rate");
}

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
Check(pointer is { Kind: PointerEventKind.Down, X: 10f, Y: 20f, Button: 0, Modifiers: ModifierKeys.None }, "RaisePointer reaches PointerInput");

win.RaisePointer(PointerEventKind.Down, 10f, 20f, 0, 0, 0, ModifierKeys.Cmd | ModifierKeys.Shift);
Check(pointer is { Modifiers: ModifierKeys.Cmd | ModifierKeys.Shift }, "RaisePointer carries modifiers to PointerInput");

win.RaiseKey(true, Silk.NET.Input.Key.Escape);
Check(key is { IsDown: true, Key: Key.Escape, Code: 256, Modifiers: ModifierKeys.None }, "RaiseKey maps and reaches KeyInput");

win.RaiseKey(true, Silk.NET.Input.Key.Escape, ModifierKeys.Ctrl);
Check(key is { Modifiers: ModifierKeys.Ctrl }, "RaiseKey carries modifiers to KeyInput");

win.RaiseKey(false, (Silk.NET.Input.Key)12345);
Check(key is { IsDown: false, Key: Key.Unknown, Code: 12345 }, "unmapped key reports Unknown but keeps the code");

win.RaiseText('é');
Check(text is { Character: 'é' }, "RaiseText reaches TextInput");

// --- Cursor shapes on the GLFW backend ---------------------------------

using (var cursorWin = new AppWindow(new AppWindowOptions { Title = "cursor", Width = 160, Height = 120, ForceGlfw = true }))
{
    var cursorOk = true;
    try
    {
        // Two passes: the second reuses each cached cursor instead of recreating it.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var shape in Enum.GetValues<CursorShape>())
            {
                cursorWin.SetCursor(shape);
            }
        }
    }
    catch
    {
        cursorOk = false;
    }
    Check(cursorOk, "SetCursor applies every shape on the GLFW backend without throwing");
}

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
    Check(caps.AlphaModes.Length > 0, "capabilities report alpha modes");
    Check(caps.Supports(caps.AlphaModes[0]), "the first reported alpha mode is supported");
    Check(ReferenceEquals(caps, surface.Capabilities), "capabilities are cached");

    // An unsupported alpha mode must throw a managed error, not abort the
    // process.
    var unsupportedAlpha = CompositeAlphaMode.Opaque;
    var foundUnsupportedAlpha = false;
    foreach (var m in new[] { CompositeAlphaMode.Opaque, CompositeAlphaMode.Premultiplied, CompositeAlphaMode.Unpremultiplied, CompositeAlphaMode.Inherit })
    {
        if (!caps.Supports(m)) { unsupportedAlpha = m; foundUnsupportedAlpha = true; break; }
    }
    Check(foundUnsupportedAlpha && !caps.Supports(unsupportedAlpha), "the surface rejects at least one alpha mode");
    using (var alphaGpu = GpuContext.Create(win.Surface!, surfaceOptions: new WindowSurfaceOptions { AlphaMode = unsupportedAlpha }))
    {
        var alphaThrew = false;
        try { alphaGpu.Surface!.Configure(frame.PixelWidth, frame.PixelHeight); }
        catch (InvalidOperationException) { alphaThrew = true; }
        Check(alphaThrew, "Configure throws on an unsupported alpha mode");
    }
    using (var alphaOkGpu = GpuContext.Create(win.Surface!, surfaceOptions: new WindowSurfaceOptions { AlphaMode = caps.AlphaModes[0] }))
    {
        alphaOkGpu.Surface!.Configure(frame.PixelWidth, frame.PixelHeight);
        Check(alphaOkGpu.Surface.PixelSize.Width > 0, "Configure accepts a supported alpha mode");
    }

    surface.PresentMode = caps.ChoosePresentMode(PresentMode.Fifo);
    Check(surface.PresentMode == PresentMode.Fifo, "PresentMode property holds the choice");
    Check(surface.Format == TextureFormat.Bgra8Unorm, "Format reflects options");

    surface.Configure(frame.PixelWidth, frame.PixelHeight);
    Check(surface.PixelSize == (frame.PixelWidth, frame.PixelHeight), "Configure records pixel size");
    Check(surface.PresentCount == 0, "PresentCount starts at zero");

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
        Check(surface.PresentCount == 0, "CancelFrame does not count a present");
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
            Check(surface.PresentCount == 1, "Present increments PresentCount");
        }
    }
    else
    {
        Check(false, "could not acquire a swapchain frame");
    }

    // A second window on the same device.
    using var second = new AppWindow(new AppWindowOptions { Title = "second", Width = 160, Height = 120 });
    var sf = second.CurrentFrame;
    using (var secondSurface = gpu.CreateSurface(second.Surface!))
    {
        secondSurface.Configure(sf.PixelWidth, sf.PixelHeight);
        Check(secondSurface.TryAcquireFrame(), "second surface on the shared device acquires");
        secondSurface.CancelFrame();
        Check(secondSurface.PresentCount == 0, "second surface CancelFrame does not count a present");
    }

    // The SurfaceFactory overload of CreateSurface — the native-macOS /
    // multi-window eject. Exercised here with a GLFW window's factory, after the
    // first surface is disposed so only one swapchain configures the window.
    unsafe
    {
        using var factorySurface = gpu.CreateSurface(second.Surface!.CreateWebGPUSurface);
        factorySurface.Configure(sf.PixelWidth, sf.PixelHeight);
        Check(factorySurface.TryAcquireFrame(), "factory-built surface on the shared device acquires");
        factorySurface.CancelFrame();
    }
}

// --- GPU via the caller-supplied surface factory -------------------------

// The eject seam: a caller-built WebGPU api object and an explicit surface
// factory, no window helper inside Create.
unsafe
{
    using var ejected = GpuContext.Create(WebGPU.GetApi(), win.Surface!.CreateWebGPUSurface);
    Check(ejected.Surface is not null, "factory-created context exposes a surface");
    ejected.Surface!.Configure(frame.PixelWidth, frame.PixelHeight);
    Check(ejected.Surface.TryAcquireFrame(), "factory-created surface acquires");
    ejected.Surface.CancelFrame();
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
        if (frames >= 3)
        {
            solo.RequestClose();
        }
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
        if (framesA == 2)
        {
            host.Invoke(() => invokeRanOnMain = Environment.CurrentManagedThreadId);
        }

        if (framesA == 3)
        {
            host.Invoke(winA.Minimize);
        }

        if (framesA >= 10 && restoredA)
        {
            winA.RequestClose();
        }
    };
    winB.RenderFrame += _ =>
    {
        renderThreadB = Environment.CurrentManagedThreadId;
        framesB++;
        Thread.Sleep(5);
        if (framesB == 10)
        {
            host.Invoke(() => winB.Resize(220, 160));
        }

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
        // Close once the resize round-trip has been observed, with a generous
        // cap so a real failure fails instead of hanging.
        if ((framesB >= 40 && hostedResizeThread != 0) || framesB >= 600)
        {
            winB.RequestClose();
        }
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

// --- AppHost: adopting a window after Run has started -------------------

using (var host = new AppHost())
{
    var first = new AppWindow(new AppWindowOptions { Title = "first", Width = 160, Height = 120 });
    host.AddWindow(first);

    var mainThread = Environment.CurrentManagedThreadId;
    AppWindow? late = null;
    var addedLate = false;
    var lateRendered = false;
    var lateThread = 0;
    var lateFrames = 0;
    var firstFrames = 0;

    first.RenderFrame += _ =>
    {
        firstFrames++;
        Thread.Sleep(2); // pace so the late window's thread has time to spin up
        // Adopt a second window mid-run, from the main thread. AddWindow must
        // start its render thread immediately because the host is running.
        if (firstFrames == 2 && !addedLate)
        {
            addedLate = true;
            host.Invoke(() =>
            {
                late = new AppWindow(new AppWindowOptions { Title = "late", Width = 160, Height = 120 });
                late.RenderFrame += _ =>
                {
                    lateThread = Environment.CurrentManagedThreadId;
                    lateRendered = true;
                    if (++lateFrames >= 3)
                    {
                        late!.RequestClose();
                    }
                };
                host.AddWindow(late);
            });
        }
        // Hold first open until the late window has proved it renders, with a
        // generous cap so a real failure fails instead of hanging.
        if (lateRendered || firstFrames >= 600)
        {
            first.RequestClose();
        }
    };

    Check(host.Run() == 0, "Run returns after a late-added window also closes");
    Check(lateRendered, "a window added to a running host starts rendering");
    Check(lateThread != mainThread && lateThread != 0, "the late window renders off the main thread");
}

// --- AppHost edge paths --------------------------------------------------

{
    // Retiring a window with no WindowClosed subscriber must not throw: the
    // null-conditional invoke in Retire is the branch under test.
    var hostZ = new AppHost();
    hostZ.AddWindow(new AppWindow(new AppWindowOptions { Title = "no-subscriber", Width = 160, Height = 120 }));
    var clean = true;
    try { hostZ.Dispose(); } catch { clean = false; }
    Check(clean, "Dispose with no WindowClosed subscriber retires cleanly");
}

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

// --- Skyline.Render: FrameLoop on real windows --------------------------

// Attach builds its own device, drives a single-window Run loop, idles until
// RequestRedraw, reconfigures on resize, and exposes every raw handle.
using (var rwin = new AppWindow(new AppWindowOptions { Title = "frameloop attach", Width = 200, Height = 150 }))
{
    Check(!rwin.IsHosted, "a standalone window reports IsHosted false");
    var rframes = 0;
    var ejectSeen = false;
    var rloopResized = false;
    rwin.Resized += _ => rloopResized = true;

    using var loop = FrameLoop.Attach(rwin, new FrameLoopOptions
    {
        ClearColor = new Silk.NET.WebGPU.Color { R = 0.0, G = 0.4, B = 0.8, A = 1.0 },
    });
    Check(ReferenceEquals(loop.Surface, loop.Gpu.Surface), "FrameLoop.Surface is the context surface");
    Check(loop.Pacer.MaxFramesInFlight == 2, "FrameLoop exposes its pacer");

    // Resize between frames (before Run), so FrameLoop.OnResized reconfigures
    // with no frame in flight — the order the render loop guarantees.
    rwin.Resize(220, 165);
    rwin.PumpEvents();
    Check(rloopResized, "FrameLoop reconfigures on resize");

    loop.OnRender = (in Frame f) =>
    {
        rframes++;
        unsafe
        {
            if (f.Encoder != null && f.View != null && f.Pass != null)
            {
                ejectSeen = true;
            }
        }
        loop.RequestRedraw();                     // self-perpetuate (Continuous=false)
        if (rframes >= 4)
        {
            rwin.RequestClose();
        }
    };
    loop.RequestRedraw();                          // kick the first frame
    Check(rwin.Run() == 0, "FrameLoop.Attach drives a window Run loop to close");
    Check(rframes >= 4, "FrameLoop OnRender fired each frame");
    Check(ejectSeen, "Frame exposes a live encoder, view, and started pass");
}

// Over borrows a caller-owned device, renders continuously, and leaves the
// context alone on dispose.
using (var owin = new AppWindow(new AppWindowOptions { Title = "frameloop over", Width = 160, Height = 120 }))
using (var ogpu = GpuContext.Create(owin.Surface))
{
    using var opacer = new FramePacer(ogpu, 2); // Over borrows the pacer; the test owns it
    var oframes = 0;
    using (var loop = FrameLoop.Over(owin, ogpu, ogpu.Surface!, opacer, new FrameLoopOptions { Continuous = true }))
    {
        loop.OnRender = (in Frame f) => { if (++oframes >= 3) { owin.RequestClose(); } };
        Check(owin.Run() == 0, "FrameLoop.Over drives a Run loop with a borrowed device");
        Check(oframes >= 3, "Over OnRender fired");
    }
    Check(ogpu.Poll(wait: false), "Over leaves the borrowed context alive after its loop disposes");
}

// Attach refuses a window adopted by an AppHost.
using (var ahost = new AppHost())
{
    var hwin = new AppWindow(new AppWindowOptions { Title = "hosted", Width = 160, Height = 120 });
    ahost.AddWindow(hwin);
    Check(hwin.IsHosted, "AddWindow marks the window hosted");
    var refused = false;
    try { FrameLoop.Attach(hwin); } catch (InvalidOperationException) { refused = true; }
    Check(refused, "FrameLoop.Attach refuses a hosted window");
}

// A throwing OnRender is captured, the loop closes the window, and the error
// surfaces as Outcome's Err case — never out of Run, which would break dispose.
using (var fwin = new AppWindow(new AppWindowOptions { Title = "frameloop fault", Width = 160, Height = 120 }))
{
    using var loop = FrameLoop.Attach(fwin, new FrameLoopOptions { Continuous = true });
    var ff = 0;
    loop.OnRender = (in Frame f) => { if (++ff >= 2) { throw new InvalidOperationException("boom in draw"); } };
    fwin.Run(); // returns normally — the loop closed itself on the fault
    Check(loop.Outcome is Err { Error: InvalidOperationException }, "a throwing OnRender surfaces as Outcome's Err case");
}

// --- Skyline.Interaction.Ui: the composited approvals overlay -----------

// The overlay draws its panel and buttons on top of an app frame through a
// LoadOp.Load pass. Here we only prove the wgpu encode lands pixels.
using (var iwin = new AppWindow(new AppWindowOptions { Title = "approvals overlay", Width = 200, Height = 150 }))
using (var igpu = GpuContext.Create(iwin.Surface!, surfaceOptions: new WindowSurfaceOptions { ExtraUsage = TextureUsage.CopySrc }))
{
    igpu.UncapturedError += (t, m) => Check(false, $"overlay wgpu error ({t}): {m}");
    var isurface = igpu.Surface!;
    var iframe = iwin.CurrentFrame;
    isurface.Configure(iframe.PixelWidth, iframe.PixelHeight);

    var clip = new AppWindowClipboard(iwin);
    clip.Text = "via-overlay-clipboard";
    Check(clip.Text == "via-overlay-clipboard", "AppWindowClipboard round-trips through the window");

    var redraws = 0;
    using var overlay = new ApprovalsOverlay(igpu, isurface.Format, requestRedraw: () => redraws++);
    Check(!overlay.State.Snapshot.HasModal, "a fresh overlay starts with no modal");

    // An idle Encode draws nothing and never builds a pipeline.
    if (isurface.TryAcquireFrame())
    {
        unsafe
        {
            var idleEnc = igpu.Api.DeviceCreateCommandEncoder(igpu.DeviceHandle, (CommandEncoderDescriptor*)null);
            overlay.Encode(isurface.CurrentView, idleEnc, iframe);
            var idleCmd = igpu.Api.CommandEncoderFinish(idleEnc, (CommandBufferDescriptor*)null);
            igpu.Api.QueueSubmit(igpu.QueueHandle, 1, &idleCmd);
            igpu.Api.CommandBufferRelease(idleCmd);
            igpu.Api.CommandEncoderRelease(idleEnc);
        }
        isurface.CancelFrame();
    }

    // Raise a request through the IApprovalUi seam, draw it, read the Allow
    // button back, then answer it through the pointer sink.
    var planner = new Actor("planner", "Planner", ActorKind.Ai, ActorLocality.Local);
    var decision = overlay.RequestAsync(
        new ApprovalRequest("w1", planner, InteractionCapability.Edit, "Planner wants to type", null, true));
    Check(redraws >= 1, "RequestAsync asks for a redraw");
    var pendingSnapshot = overlay.State.Snapshot;
    Check(pendingSnapshot.HasModal, "the request is pending");

    var layout = overlay.State.Layout(iframe.LogicalWidth, iframe.LogicalHeight);
    var allowX = layout.Allow.X + layout.Allow.Width / 2f;
    var allowY = layout.Allow.Y + layout.Allow.Height / 2f;

    if (isurface.TryAcquireFrame())
    {
        unsafe
        {
            using var readback = new TextureReadback(igpu, isurface.PixelSize.Width, isurface.PixelSize.Height);
            var enc = igpu.Api.DeviceCreateCommandEncoder(igpu.DeviceHandle, (CommandEncoderDescriptor*)null);

            // Clear to black so the overlay composites over a known background.
            var att = new RenderPassColorAttachment
            {
                View = isurface.CurrentView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Silk.NET.WebGPU.Color { R = 0, G = 0, B = 0, A = 1 },
            };
            var pd = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
            var clearPass = igpu.Api.CommandEncoderBeginRenderPass(enc, in pd);
            igpu.Api.RenderPassEncoderEnd(clearPass);
            igpu.Api.RenderPassEncoderRelease(clearPass);

            overlay.Encode(isurface.CurrentView, enc, iframe);  // builds the pipeline and draws
            overlay.Encode(isurface.CurrentView, enc, iframe);  // reuses the cached pipeline

            readback.Encode(enc, isurface.CurrentTexture);
            var cmd = igpu.Api.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
            igpu.Api.QueueSubmit(igpu.QueueHandle, 1, &cmd);
            igpu.Api.CommandBufferRelease(cmd);
            igpu.Api.CommandEncoderRelease(enc);

            var px = readback.Resolve();
            var width = isurface.PixelSize.Width;

            // Above the centered label the button is solid green fill.
            var fillCx = (int)(allowX * iframe.Dpr);
            var fillCy = (int)((layout.Allow.Y + 2f) * iframe.Dpr);
            var fill = (fillCy * width + fillCx) * 4;
            Check(px[fill + 1] > 120 && px[fill + 2] < 90, "Allow button draws green in readback (BGRA)");

            // Across the button's middle row, the white label leaves bright pixels.
            var labelRow = (int)(allowY * iframe.Dpr);
            var labelFound = false;
            for (var sx = (int)(layout.Allow.X * iframe.Dpr); sx < (int)((layout.Allow.X + layout.Allow.Width) * iframe.Dpr); sx++)
            {
                var o = (labelRow * width + sx) * 4;
                if (px[o] > 200 && px[o + 1] > 200 && px[o + 2] > 200)
                {
                    labelFound = true;
                    break;
                }
            }
            Check(labelFound, "the Allow label draws white pixels over the button");
            isurface.Present();
        }
    }

    overlay.OnPointerDown(allowX, allowY);
    Check(decision.IsCompleted && decision.Result.Verb == ApprovalVerb.Allow, "clicking Allow resolves the request");

    overlay.Dispose();
    overlay.Dispose(); // idempotent
}

// The default TimeProvider and redraw constructor arguments.
using (var dwin = new AppWindow(new AppWindowOptions { Title = "overlay defaults", Width = 160, Height = 120 }))
using (var dgpu = GpuContext.Create(dwin.Surface!))
{
    using var defaults = new ApprovalsOverlay(dgpu, dgpu.Surface!.Format);
    Check(!defaults.State.Snapshot.HasModal, "a fresh overlay has no modal");
}

Console.WriteLine(failures == 0
    ? $"WINDOWED TESTS OK: {checks} checks passed"
    : $"WINDOWED TESTS FAILED: {failures} of {checks} checks");
return failures == 0 ? 0 : 1;
