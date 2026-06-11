using Silk.NET.Core.Contexts;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Skyline.Input;

namespace Skyline;

/// <summary>
/// A native OS window with chrome, an event loop, and input — and no
/// rendering opinion. The window's native handle is exposed through
/// <see cref="Surface"/> (Silk.NET's <see cref="INativeWindowSource"/>),
/// so any presenter that can build a surface from a native window
/// (wgpu, Vulkan, Metal, GL) plugs in. This class never touches a pixel.
/// </summary>
public sealed class AppWindow : IDisposable
{
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly Glfw _glfw;
    private readonly unsafe WindowHandle* _glfwHandle;

    // Frame geometry as one immutable object: the main thread writes it on
    // resize, a render thread reads it mid-frame. Swapping a reference is
    // atomic; updating two plain fields is not.
    private sealed record FrameGeom(int PixelWidth, int PixelHeight, float Dpr);
    private volatile FrameGeom _geom = new(1, 1, 1f);
    private volatile FrameGeom? _pendingResize;
    private volatile bool _minimized;
    private readonly AutoResetEvent _redraw = new(false);
    internal AppHost? Host;

    public unsafe AppWindow(AppWindowOptions? options = null)
    {
        options ??= new AppWindowOptions();
        Silk.NET.Windowing.Glfw.GlfwWindowing.Use();

        _window = Window.Create(WindowOptions.Default with
        {
            Title = options.Title,
            Size = new Vector2D<int>(options.Width, options.Height),
            WindowBorder = options.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed,
            // No GL context, no automatic swap: the consumer's presenter owns
            // the surface and presents explicitly. This is what keeps Skyline
            // unopinionated about rendering.
            API = GraphicsAPI.None,
            VSync = false,
            ShouldSwapAutomatically = false,
        });
        _window.Initialize();

        var dpr = _window.Size.X > 0 ? (float)_window.FramebufferSize.X / _window.Size.X : 1f;
        _geom = new FrameGeom(_window.FramebufferSize.X, _window.FramebufferSize.Y, dpr);

        _glfw = Glfw.GetApi();
        _glfwHandle = (WindowHandle*)(_window.Native?.Glfw
            ?? throw new InvalidOperationException("Skyline requires the GLFW windowing backend."));

        _window.FramebufferResize += sz =>
        {
            if (sz.X <= 0 || sz.Y <= 0) return;
            var g = new FrameGeom(sz.X, sz.Y, _window.Size.X > 0 ? (float)sz.X / _window.Size.X : _geom.Dpr);
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

        _window.StateChanged += state =>
        {
            _minimized = state == WindowState.Minimized;
            // Wake the render thread so a restore resumes immediately
            // instead of after the idle wait.
            RequestRedraw();
        };

        _window.Render += delta =>
        {
            var fb = _window.FramebufferSize;
            if (fb.X <= 0 || fb.Y <= 0) return;
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

        _input = _window.CreateInput();
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, pos) => RaisePointer(PointerEventKind.Move, pos.X, pos.Y, -1, 0, 0);
            mouse.MouseDown += (m, btn) => RaisePointer(PointerEventKind.Down, m.Position.X, m.Position.Y, (int)btn, 0, 0);
            mouse.MouseUp += (m, btn) => RaisePointer(PointerEventKind.Up, m.Position.X, m.Position.Y, (int)btn, 0, 0);
            mouse.Scroll += (m, wheel) => RaisePointer(PointerEventKind.Wheel, m.Position.X, m.Position.Y, -1, wheel.X, wheel.Y);
        }
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, k, _) => RaiseKey(true, k);
            keyboard.KeyUp += (_, k, _) => RaiseKey(false, k);
            keyboard.KeyChar += (_, ch) => RaiseText(ch);
        }
    }

    internal void RaisePointer(PointerEventKind kind, float x, float y, int button, float wheelDx, float wheelDy) =>
        PointerInput?.Invoke(new PointerEvent(kind, x, y, button, wheelDx, wheelDy));

    internal void RaiseKey(bool isDown, Silk.NET.Input.Key key) =>
        KeyInput?.Invoke(new KeyEvent(isDown, MapKey(key), (int)key));

    internal void RaiseText(char character) =>
        TextInput?.Invoke(new TextEvent(character));

    /// <summary>
    /// The native window as a surface source. Hand this to your renderer
    /// to create its swapchain (e.g. wgpu's CreateWebGPUSurface).
    /// </summary>
    public INativeWindowSource Surface => _window;

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
        get => _window.Title;
        set => _window.Title = value;
    }

    public unsafe string? ClipboardText
    {
        get => _glfw.GetClipboardString(_glfwHandle);
        set => _glfw.SetClipboardString(_glfwHandle, value ?? string.Empty);
    }

    public FrameInfo CurrentFrame => Frame(0);

    public void RequestClose() => _window.Close();

    /// <summary>Run the event loop. Blocks until the window closes.</summary>
    public int Run()
    {
        if (Host is not null)
            throw new InvalidOperationException("this window belongs to an AppHost. Call AppHost.Run instead.");
        _window.Run();
        return 0;
    }

    /// <summary>
    /// Ask the host's render thread to draw a frame soon. Callable from any
    /// thread. For windows driven by <see cref="Run"/>, use <see cref="IsDirty"/>.
    /// </summary>
    public void RequestRedraw() => _redraw.Set();

    /// <summary>Resize to a logical size. Main thread only; <see cref="Resized"/> follows.</summary>
    public void Resize(int width, int height) => _window.Size = new Vector2D<int>(width, height);

    /// <summary>Minimize the window. Main thread only.</summary>
    public void Minimize()
    {
        // Record the state now: macOS reports the change only after the
        // minimize animation, and a hosted render thread must stop touching
        // the swapchain before that.
        _minimized = true;
        _window.WindowState = WindowState.Minimized;
        RequestRedraw();
    }

    /// <summary>Restore the window from minimized. Main thread only.</summary>
    public void Restore()
    {
        _minimized = false;
        _window.WindowState = WindowState.Normal;
        RequestRedraw();
    }

    /// <summary>
    /// Process pending OS events once, without the built-in loop. For
    /// consumers that drive their own frame loop — engines, benchmarks —
    /// instead of subscribing to <see cref="RenderFrame"/>.
    /// </summary>
    public void PumpEvents() => _window.DoEvents();

    public void Dispose()
    {
        _input.Dispose();
        _window.Dispose();
        _redraw.Dispose();
    }

    private FrameInfo Frame(double delta)
    {
        var g = _geom;
        return new FrameInfo(g.PixelWidth, g.PixelHeight, g.Dpr, delta);
    }

    // The host-facing seam. The render thread calls these; everything else
    // on this class stays main-thread.
    internal bool IsClosing => _window.IsClosing;
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
