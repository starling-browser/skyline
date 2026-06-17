// SPDX-License-Identifier: Apache-2.0

using Silk.NET.Core.Contexts;
using Skyline.Input;

namespace Skyline;

/// <summary>
/// A native OS window with chrome, an event loop, and input — and no
/// rendering opinion. The window's native handle is exposed through
/// <see cref="Surface"/> (Silk.NET's <see cref="INativeWindowSource"/>),
/// so any presenter that can build a surface from a native window
/// (wgpu, Vulkan, Metal, GL) plugs in. This class never touches a pixel.
///
/// The platform window lives behind an <see cref="IWindowBackend"/>: GLFW on
/// Windows and Linux, a native backend on Apple. This class owns the frame
/// geometry, dirty pacing, and resize parking on top of whichever backend the
/// window's <see cref="AppWindowOptions"/> selected.
/// </summary>
public sealed class AppWindow : IDisposable
{
    private readonly IWindowBackend _backend;

    // Frame geometry as one immutable object: the main thread writes it on
    // resize, a render thread reads it mid-frame. Swapping a reference is
    // atomic; updating two plain fields is not.
    private sealed record FrameGeom(int PixelWidth, int PixelHeight, float Dpr);
    private volatile FrameGeom _geom;
    private volatile FrameGeom? _pendingResize;
    private volatile bool _minimized;
    private readonly AutoResetEvent _redraw = new(false);
    internal AppHost? Host;

    public AppWindow(AppWindowOptions? options = null)
    {
        options ??= new AppWindowOptions();
        _backend = WindowBackendFactory.Create(options);

        var fb = _backend.FramebufferSize;
        var logical = _backend.LogicalSize;
        var dpr = logical.Width > 0 ? (float)fb.Width / logical.Width : 1f;
        _geom = new FrameGeom(fb.Width, fb.Height, dpr);

        _backend.FramebufferResized += sz =>
        {
            if (sz.Width <= 0 || sz.Height <= 0)
            {
                return;
            }

            var logicalNow = _backend.LogicalSize;
            var g = new FrameGeom(sz.Width, sz.Height, logicalNow.Width > 0 ? (float)sz.Width / logicalNow.Width : _geom.Dpr);
            if (Host is null)
            {
                _geom = g;
                Resized?.Invoke(Frame(0));
            }
            else
            {
                // Hosted windows reconfigure their swapchain on their render
                // thread, never mid-frame: park the resize until the next
                // frame starts. _geom advances only when the resize is
                // consumed, so a frame never reports dimensions its
                // swapchain doesn't have yet.
                _pendingResize = g;
                RequestRedraw();
            }
        };

        _backend.MinimizedChanged += minimized =>
        {
            _minimized = minimized;
            // Wake the render thread so a restore resumes immediately
            // instead of after the idle wait.
            RequestRedraw();
        };

        _backend.Render += delta =>
        {
            var f = _backend.FramebufferSize;
            if (f.Width <= 0 || f.Height <= 0)
            {
                return;
            }

            if (IsDirty is { } dirty && !dirty())
            {
                // Manual present is the only throttle in this stack. An idle
                // frame that neither draws nor presents must sleep or the
                // loop free-runs a core.
                Thread.Sleep(8);
                return;
            }
            RenderFrame?.Invoke(Frame(delta));
        };

        _backend.Pointer += e => PointerInput?.Invoke(e);
        _backend.Key += e => KeyInput?.Invoke(e);
        _backend.Text += e => TextInput?.Invoke(e);
    }

    internal void RaisePointer(PointerEventKind kind, float x, float y, int button, float wheelDx, float wheelDy, ModifierKeys modifiers = ModifierKeys.None) =>
        PointerInput?.Invoke(new PointerEvent(kind, x, y, button, wheelDx, wheelDy, modifiers));

    internal void RaiseKey(bool isDown, Silk.NET.Input.Key key, ModifierKeys modifiers = ModifierKeys.None) =>
        KeyInput?.Invoke(new KeyEvent(isDown, MapKey(key), (int)key, modifiers));

    internal void RaiseText(char character) =>
        TextInput?.Invoke(new TextEvent(character));

    /// <summary>
    /// The native window as a surface source. Hand this to your renderer
    /// to create its swapchain (e.g. wgpu's CreateWebGPUSurface). Throws on
    /// the native macOS backend, which presents through <see cref="MetalLayer"/>
    /// instead.
    /// </summary>
    public INativeWindowSource Surface => _backend.SurfaceSource is WindowSurfaceSource.Native n
        ? n.Source
        : throw new InvalidOperationException(
            "this window uses the native macOS backend; build the surface with " +
            "Skyline.Render's WindowGpu.CreateContext/CreateSurface (or FrameLoop), not window.Surface.");

    /// <summary>How this window's backend hands its drawing surface to a presenter.</summary>
    internal WindowSurfaceSource BackendSurfaceSource => _backend.SurfaceSource;

    /// <summary>
    /// The window's <c>CAMetalLayer</c> pointer on a native macOS backend, or
    /// null on GLFW.
    /// </summary>
    public nint? MetalLayer => _backend.SurfaceSource is WindowSurfaceSource.MetalLayer m ? m.Layer : null;

    /// <summary>Draw and present a frame. Skyline never presents for you.</summary>
    public event Action<FrameInfo>? RenderFrame;

    /// <summary>Framebuffer size changed. Reconfigure your swapchain.</summary>
    public event Action<FrameInfo>? Resized;

    public event Action<PointerEvent>? PointerInput;
    public event Action<KeyEvent>? KeyInput;
    public event Action<TextEvent>? TextInput;

    /// <summary>
    /// When set and returning false, the frame is skipped and the loop
    /// sleeps briefly instead of rendering. Continuous-render apps leave
    /// this null (or return true).
    /// </summary>
    public Func<bool>? IsDirty { get; set; }

    public string Title
    {
        get => _backend.Title;
        set => _backend.Title = value;
    }

    public string? ClipboardText
    {
        get => _backend.ClipboardText;
        set => _backend.ClipboardText = value;
    }

    /// <summary>The MIME types currently on the clipboard.</summary>
    public IReadOnlyList<string> ClipboardFormats => _backend.ClipboardFormats;

    /// <summary>
    /// Read clipboard data for a MIME type (for example "image/png" or
    /// "text/html"), or null if the clipboard holds no such type.
    /// </summary>
    public byte[]? GetClipboardData(string mimeType) => _backend.GetClipboardData(mimeType);

    /// <summary>
    /// Write clipboard data under a MIME type, replacing the clipboard. The
    /// GLFW backend stores text only; other types are dropped there.
    /// </summary>
    public void SetClipboardData(string mimeType, byte[] data) => _backend.SetClipboardData(mimeType, data);

    public FrameInfo CurrentFrame => Frame(0);

    /// <summary>True when this window has been adopted by an <see cref="AppHost"/>. Hosted windows render on the host's thread, so use <c>AppHost.Run</c>, not <see cref="Run"/>.</summary>
    public bool IsHosted => Host is not null;

    public void RequestClose() => _backend.RequestClose();

    /// <summary>Run the event loop. Blocks until the window closes.</summary>
    public int Run()
    {
        if (Host is not null)
        {
            throw new InvalidOperationException("this window belongs to an AppHost. Call AppHost.Run instead.");
        }

        _backend.Run();
        return 0;
    }

    /// <summary>
    /// Ask the host's render thread to draw a frame soon. Callable from any
    /// thread. For windows driven by <see cref="Run"/>, use <see cref="IsDirty"/>.
    /// </summary>
    public void RequestRedraw() => _redraw.Set();

    /// <summary>Resize to a logical size. Main thread only; <see cref="Resized"/> follows.</summary>
    public void Resize(int width, int height) => _backend.Resize(width, height);

    /// <summary>Minimize the window. Main thread only.</summary>
    public void Minimize()
    {
        // Record the state now: macOS reports the change only after the
        // minimize animation, and a hosted render thread must stop touching
        // the swapchain before that.
        _minimized = true;
        _backend.Minimize();
        RequestRedraw();
    }

    /// <summary>Restore the window from minimized. Main thread only.</summary>
    public void Restore()
    {
        _minimized = false;
        _backend.Restore();
        RequestRedraw();
    }

    /// <summary>
    /// Process pending OS events once, without the built-in loop. For
    /// consumers that drive their own frame loop — engines, benchmarks —
    /// instead of subscribing to <see cref="RenderFrame"/>.
    /// </summary>
    public void PumpEvents() => _backend.PumpEventsOnce();

    public void Dispose()
    {
        _backend.Dispose();
        _redraw.Dispose();
    }

    private FrameInfo Frame(double delta)
    {
        var g = _geom;
        return new FrameInfo(g.PixelWidth, g.PixelHeight, g.Dpr, delta);
    }

    // The host-facing seam. The render thread calls these; everything else
    // on this class stays main-thread.
    internal bool IsClosing => _backend.IsClosing;
    internal bool IsMinimized => _minimized;
    internal bool ShouldRenderNow => IsDirty?.Invoke() != false;
    internal void RaiseRenderFrame(double delta) => RenderFrame?.Invoke(Frame(delta));
    internal bool WaitForRedraw(int milliseconds) => _redraw.WaitOne(milliseconds);

    internal bool TryConsumePendingResize(out FrameInfo frame)
    {
        var g = Interlocked.Exchange(ref _pendingResize, null);
        if (g is null)
        {
            frame = default;
            return false;
        }
        _geom = g;
        frame = new FrameInfo(g.PixelWidth, g.PixelHeight, g.Dpr, 0);
        return true;
    }

    internal void RaiseResized(FrameInfo frame) => Resized?.Invoke(frame);

    internal static Input.Key MapKey(Silk.NET.Input.Key k)
    {
        // Silk.NET.Input.Key values are GLFW keycodes, which Skyline's enum
        // mirrors. Anything outside the defined set reports Unknown but keeps
        // the raw code on the event.
        var code = (int)k;
        return Enum.IsDefined(typeof(Input.Key), code) ? (Input.Key)code : Input.Key.Unknown;
    }
}
