// SPDX-License-Identifier: Apache-2.0

using System.Runtime.ExceptionServices;
using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Render;

/// <summary>The per-frame callback. Receives a <see cref="Frame"/> by readonly reference — a ref struct cannot ride a generic delegate.</summary>
public delegate void FrameCallback(in Frame frame);

/// <summary>
/// Owns the per-frame ritual every windowed wgpu app repeats: pace, acquire
/// (with stale-swapchain recovery), begin a clear pass, run your draw, end,
/// submit, count, present. It also drives the window's resize, idle, and
/// render callbacks. What it owns, it owns completely — but every raw handle
/// (<see cref="Gpu"/>, <see cref="Surface"/>, and the live encoder/view/pass
/// on each <see cref="Frame"/>) stays one property away, so an app drops to
/// raw wgpu without leaving the loop.
/// </summary>
public sealed unsafe class FrameLoop : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly WindowSurface? _surface;
    private readonly FramePacer _pacer;
    private readonly bool _ownsResources;
    private readonly bool _beginClearPass;
    private readonly bool _continuous;
    private AppWindow? _window;
    // Cached once in Wire: a method-group conversion of an instance method
    // (_surface.Present) allocates a fresh delegate every time, so binding it
    // per frame in OnRenderFrame would churn two allocations a frame.
    private Action? _present;
    private Action? _cancel;
    private ExceptionDispatchInfo? _fault;
    // 1 when a redraw is pending. Consumed atomically in the IsDirty gate so a
    // RequestRedraw from another thread can never be lost between the gate's
    // check and the frame.
    private int _dirty;
    private bool _disposed;

    private FrameLoop(GpuContext gpu, WindowSurface? surface, FramePacer pacer, FrameLoopOptions options, bool ownsResources)
    {
        _gpu = gpu;
        _surface = surface;
        _pacer = pacer;
        _ownsResources = ownsResources;
        _beginClearPass = options.BeginClearPass;
        _continuous = options.Continuous;
        ClearColor = options.ClearColor;
    }

    /// <summary>
    /// Build a loop over a context, surface, and pacer you already own, and
    /// wire <paramref name="window"/>'s frame events to it. The loop borrows
    /// all three and disposes none of them — this is the multi-window path,
    /// where one <see cref="GpuContext"/> is shared across windows and each
    /// window has its own surface and pacer.
    /// </summary>
    public static FrameLoop Over(AppWindow window, GpuContext gpu, WindowSurface surface, FramePacer pacer,
        FrameLoopOptions? options = null)
    {
        options ??= new FrameLoopOptions();
        var loop = new FrameLoop(gpu, surface, pacer, options, ownsResources: false);
        loop.Wire(window);
        return loop;
    }

    /// <summary>
    /// Build the whole stack for one window — a <see cref="GpuContext"/>, a
    /// <see cref="FramePacer"/>, and the surface — and wire its frame events.
    /// The single-window getting-started path. The loop owns and disposes the
    /// context (and its surface) and the pacer. Refuses a window already
    /// adopted by an <see cref="AppHost"/>; those share one context, so use
    /// <see cref="Over"/> instead. Do not adopt the window into an AppHost
    /// after attaching — one window gets one presenter.
    /// </summary>
    public static FrameLoop Attach(AppWindow window, FrameLoopOptions? options = null)
    {
        if (window.IsHosted)
        {
            throw new InvalidOperationException(
                "this window belongs to an AppHost. Use FrameLoop.Over with a shared GpuContext instead.");
        }

        options ??= new FrameLoopOptions();
        var gpu = WindowGpu.CreateContext(window, options.Gpu, options.Surface);
        var pacer = new FramePacer(gpu, options.MaxFramesInFlight);
        var loop = new FrameLoop(gpu, gpu.Surface!, pacer, options, ownsResources: true);
        loop.Wire(window);
        return loop;
    }

    // Surface-less construction so DriveFrame can be driven headlessly with a
    // fabricated view and a present delegate. Not wired to any window, and it
    // borrows the context and pacer (the test owns their lifetime).
    internal static FrameLoop CreateForTest(GpuContext gpu, FramePacer pacer, FrameLoopOptions options) =>
        new(gpu, null, pacer, options, ownsResources: false);

    /// <summary>The color the started clear pass clears to. Change it from your render callback (the loop's thread).</summary>
    public Color ClearColor { get; set; }

    /// <summary>Your per-frame draw. Runs after the clear pass begins (when enabled) and before submit/present. Set it before the loop starts.</summary>
    public FrameCallback? OnRender { get; set; }

    /// <summary>
    /// Optional handler for the window's input, resize, and focus callbacks. The
    /// loop owns the window's <see cref="AppWindow.Handler"/> while attached and
    /// forwards everything except the frame draw here, so set this instead of the
    /// window's handler. Draw through <see cref="OnRender"/>, not the handler's
    /// render callback. Resize forwards after the loop reconfigures the surface.
    /// </summary>
    public AppWindowHandler? Handler { get; set; }

    /// <summary>The GPU context — raw <c>Api</c>, device, and queue handles for the eject.</summary>
    public GpuContext Gpu => _gpu;

    /// <summary>The window surface this loop presents to.</summary>
    public WindowSurface Surface => _surface ?? throw new InvalidOperationException("this FrameLoop has no surface");

    /// <summary>The frame pacer capping frames in flight.</summary>
    public FramePacer Pacer => _pacer;

    /// <summary>
    /// How the run ended. <see cref="Err"/> carries the exception a render
    /// callback threw — when <see cref="OnRender"/> throws, the loop cancels
    /// the frame, captures it, and closes the window so the run loop exits
    /// cleanly. <see cref="Ok"/> carries the presented frame count when nothing
    /// faulted. Read it after the run loop returns and branch on the two cases.
    /// </summary>
    public FrameOutcome Outcome =>
        _fault is { } fault
            ? new Err(fault.SourceException)
            : new Ok(_surface?.PresentCount ?? 0);

    private void Wire(AppWindow window)
    {
        _window = window;
        _present = _surface!.Present;
        _cancel = _surface.CancelFrame;
        var frame = window.CurrentFrame;
        _surface.Configure(frame.PixelWidth, frame.PixelHeight);
        // The loop is the window's single handler. It drives the frame itself
        // and forwards input, focus, and post-reconfigure resize to the app's
        // Handler.
        window.Handler = new CallbackAppWindowHandler
        {
            RenderFrame = (_, info) => OnRenderFrame(info),
            Resized = (w, f) =>
            {
                OnResized(f);
                Handler?.OnResized(w, f);
            },
            PointerInput = (w, e) => Handler?.OnPointerInput(w, e),
            KeyInput = (w, e) => Handler?.OnKeyInput(w, e),
            TextInput = (w, e) => Handler?.OnTextInput(w, e),
            FocusChanged = (w, e) => Handler?.OnFocusChanged(w, e),
        };
        // Continuous loops render every frame (IsDirty left null). Event-driven
        // loops idle until a redraw is pending. Consuming the flag here — at the
        // gate that decides whether to render — makes the check-and-consume one
        // atomic act, so a cross-thread RequestRedraw is never lost.
        if (!_continuous)
        {
            window.IsDirty = () => Interlocked.Exchange(ref _dirty, 0) == 1;
        }
    }

    private void OnResized(FrameInfo f) => _surface!.Configure(f.PixelWidth, f.PixelHeight);

    private void OnRenderFrame(FrameInfo info)
    {
        _pacer.Wait();
        var ok = _surface!.TryAcquireFrame();
        try
        {
            DriveFrame(info, ok, ok ? _surface.CurrentView : null, _present!, _cancel!);
        }
        catch (Exception ex)
        {
            // DriveFrame already tore the frame down. Capture the draw exception
            // and close the window so Run exits through its normal path — an
            // exception thrown out of Run leaves the window unable to dispose.
            // The app reads it back from Outcome after Run returns.
            _fault = ExceptionDispatchInfo.Capture(ex);
            _window!.RequestClose();
        }
    }

    /// <summary>
    /// Mark the next frame dirty. For an event-driven loop, call this when
    /// something changed. Callable from any thread — it also wakes a hosted
    /// render thread.
    /// </summary>
    public void RequestRedraw()
    {
        Volatile.Write(ref _dirty, 1);
        _window?.RequestRedraw();
    }

    // The testable core, decoupled from WindowSurface: given whether a frame was
    // acquired, its view, a present action, and a cancel action, run the whole
    // ritual. If OnRender (or a wgpu call) throws, the frame is torn down cleanly
    // — pass ended, encoder released, acquired frame cancelled, nothing presented
    // — and the exception surfaces, because a draw bug should be loud.
    internal bool DriveFrame(FrameInfo info, bool acquired, TextureView* view, Action present, Action cancel)
    {
        if (!acquired)
        {
            return false;
        }

        var api = _gpu.Api;
        var encoder = api.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        RenderPassEncoder* pass = null;
        var completed = false;
        try
        {
            if (_beginClearPass)
            {
                var attachment = new RenderPassColorAttachment
                {
                    View = view,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = ClearColor,
                };
                var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
                pass = api.CommandEncoderBeginRenderPass(encoder, in passDesc);
            }

            OnRender?.Invoke(new Frame(info, pass, view, encoder));

            // The loop owns the clear pass start to end — apps draw into it but do
            // not end it. Apps that need extra passes use BeginClearPass = false.
            if (pass != null)
            {
                api.RenderPassEncoderEnd(pass);
                api.RenderPassEncoderRelease(pass);
                pass = null;
            }

            var cmd = api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
            api.QueueSubmit(_gpu.QueueHandle, 1, &cmd);
            _pacer.FrameSubmitted();
            api.CommandBufferRelease(cmd);
            present();
            completed = true;
            return true;
        }
        finally
        {
            // On the success path pass is already null. On a throw it may still be
            // open, so end and release it before dropping the encoder.
            if (pass != null)
            {
                api.RenderPassEncoderEnd(pass);
                api.RenderPassEncoderRelease(pass);
            }
            api.CommandEncoderRelease(encoder);
            if (!completed)
            {
                cancel();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_window is not null)
        {
            _window.Handler = null;
        }
        // Attach built the context and pacer, so Attach disposes them (and the
        // context disposes its surface). Over and the test seam borrow them, so
        // they leave them alone.
        if (_ownsResources)
        {
            _pacer.Dispose();
            _gpu.Dispose();
        }
    }
}
