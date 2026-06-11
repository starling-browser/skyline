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
    private float _dpr;
    private Vector2D<int> _lastFb;

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

        _dpr = _window.Size.X > 0 ? (float)_window.FramebufferSize.X / _window.Size.X : 1f;
        _lastFb = _window.FramebufferSize;

        _glfw = Glfw.GetApi();
        _glfwHandle = (WindowHandle*)(_window.Native?.Glfw
            ?? throw new InvalidOperationException("Skyline requires the GLFW windowing backend."));

        _window.FramebufferResize += sz =>
        {
            if (sz.X <= 0 || sz.Y <= 0) return;
            _dpr = _window.Size.X > 0 ? (float)sz.X / _window.Size.X : _dpr;
            _lastFb = sz;
            Resized?.Invoke(Frame(0));
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
            mouse.MouseMove += (_, pos) =>
                PointerInput?.Invoke(new PointerEvent(PointerEventKind.Move, pos.X, pos.Y, -1, 0, 0));
            mouse.MouseDown += (m, btn) =>
                PointerInput?.Invoke(new PointerEvent(PointerEventKind.Down, m.Position.X, m.Position.Y, (int)btn, 0, 0));
            mouse.MouseUp += (m, btn) =>
                PointerInput?.Invoke(new PointerEvent(PointerEventKind.Up, m.Position.X, m.Position.Y, (int)btn, 0, 0));
            mouse.Scroll += (m, wheel) =>
                PointerInput?.Invoke(new PointerEvent(PointerEventKind.Wheel, m.Position.X, m.Position.Y, -1, wheel.X, wheel.Y));
        }
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, k, code) =>
                KeyInput?.Invoke(new KeyEvent(true, MapKey(k), (int)k));
            keyboard.KeyUp += (_, k, code) =>
                KeyInput?.Invoke(new KeyEvent(false, MapKey(k), (int)k));
            keyboard.KeyChar += (_, ch) =>
                TextInput?.Invoke(new TextEvent(ch));
        }
    }

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
        _window.Run();
        return 0;
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
    }

    private FrameInfo Frame(double delta) =>
        new(_lastFb.X, _lastFb.Y, _dpr, delta);

    private static Input.Key MapKey(Silk.NET.Input.Key k)
    {
        // Silk.NET.Input.Key values are GLFW keycodes, which Skyline's enum
        // mirrors. Anything outside the defined set reports Unknown but keeps
        // the raw code on the event.
        var code = (int)k;
        return Enum.IsDefined(typeof(Input.Key), code) ? (Input.Key)code : Input.Key.Unknown;
    }
}
