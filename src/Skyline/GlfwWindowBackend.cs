// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Skyline.Input;

namespace Skyline;

/// <summary>
/// The portable backend: a Silk.NET GLFW window with no GL context and no
/// automatic swap, so a presenter owns the surface and presents explicitly.
/// This is the code that used to live inline in <see cref="AppWindow"/>.
/// </summary>
internal sealed class GlfwWindowBackend : IWindowBackend
{
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly Glfw _glfw;
    private readonly unsafe WindowHandle* _glfwHandle;
    private readonly WindowSurfaceSource _surfaceSource;
    private readonly Func<Silk.NET.Input.Key, bool> _pressed;

    public event Action<(int Width, int Height)>? FramebufferResized;
    public event Action<bool>? MinimizedChanged;
    public event Action<double>? Render;
    public event Action<PointerEvent>? Pointer;
    public event Action<KeyEvent>? Key;
    public event Action<TextEvent>? Text;

    public unsafe GlfwWindowBackend(AppWindowOptions options)
    {
        Silk.NET.Windowing.Glfw.GlfwWindowing.Use();

        var (border, transparent) = GlfwChrome.Map(options.EffectiveChrome);
        _window = Window.Create(WindowOptions.Default with
        {
            Title = options.Title,
            Size = new Vector2D<int>(options.Width, options.Height),
            WindowBorder = border,
            TransparentFramebuffer = transparent,
            API = GraphicsAPI.None,
            VSync = false,
            ShouldSwapAutomatically = false,
        });
        _window.Initialize();
        _surfaceSource = new WindowSurfaceSource.Native(_window);

        _glfw = Glfw.GetApi();
        _glfwHandle = (WindowHandle*)(_window.Native?.Glfw
            ?? throw new InvalidOperationException("Skyline requires the GLFW windowing backend."));

        _window.FramebufferResize += sz => FramebufferResized?.Invoke((sz.X, sz.Y));
        _window.StateChanged += state => MinimizedChanged?.Invoke(state == WindowState.Minimized);
        _window.Render += delta => Render?.Invoke(delta);

        _input = _window.CreateInput();
        // GLFW callbacks carry no modifier field, so sample the live keyboard
        // state at event time. ModifierKeysMap maps it to the platform-neutral set.
        _pressed = k => _input.Keyboards.Any(kb => kb.IsKeyPressed(k));
        foreach (var mouse in _input.Mice)
        {
            mouse.MouseMove += (_, pos) => Pointer?.Invoke(new PointerEvent(PointerEventKind.Move, pos.X, pos.Y, -1, 0, 0, ModifierKeysMap.FromPressed(_pressed)));
            mouse.MouseDown += (m, btn) => Pointer?.Invoke(new PointerEvent(PointerEventKind.Down, m.Position.X, m.Position.Y, (int)btn, 0, 0, ModifierKeysMap.FromPressed(_pressed)));
            mouse.MouseUp += (m, btn) => Pointer?.Invoke(new PointerEvent(PointerEventKind.Up, m.Position.X, m.Position.Y, (int)btn, 0, 0, ModifierKeysMap.FromPressed(_pressed)));
            mouse.Scroll += (m, wheel) => Pointer?.Invoke(new PointerEvent(PointerEventKind.Wheel, m.Position.X, m.Position.Y, -1, wheel.X, wheel.Y, ModifierKeysMap.FromPressed(_pressed)));
        }
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += (_, k, _) => Key?.Invoke(new KeyEvent(true, AppWindow.MapKey(k), (int)k, ModifierKeysMap.FromPressed(_pressed)));
            keyboard.KeyUp += (_, k, _) => Key?.Invoke(new KeyEvent(false, AppWindow.MapKey(k), (int)k, ModifierKeysMap.FromPressed(_pressed)));
            keyboard.KeyChar += (_, ch) => Text?.Invoke(new TextEvent(ch));
        }
    }

    public (int Width, int Height) FramebufferSize => (_window.FramebufferSize.X, _window.FramebufferSize.Y);
    public (int Width, int Height) LogicalSize => (_window.Size.X, _window.Size.Y);

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

    // GLFW's clipboard is text only, so it reports and stores text/plain alone.
    private static readonly string[] TextFormats = ["text/plain"];

    public unsafe IReadOnlyList<string> ClipboardFormats =>
        string.IsNullOrEmpty(_glfw.GetClipboardString(_glfwHandle)) ? [] : TextFormats;

    public unsafe byte[]? GetClipboardData(string mimeType)
    {
        if (mimeType == "text/plain" && _glfw.GetClipboardString(_glfwHandle) is { Length: > 0 } s)
        {
            return Encoding.UTF8.GetBytes(s);
        }
        return null;
    }

    public unsafe void SetClipboardData(string mimeType, byte[] data)
    {
        // Other MIME types are dropped: GLFW has nowhere to put them.
        if (mimeType == "text/plain")
        {
            _glfw.SetClipboardString(_glfwHandle, Encoding.UTF8.GetString(data));
        }
    }

    public bool IsClosing => _window.IsClosing;

    public WindowSurfaceSource SurfaceSource => _surfaceSource;

    public void Run() => _window.Run();

    public void PumpEventsOnce() => _window.DoEvents();

    public void RequestClose() => _window.Close();

    public void Resize(int width, int height) => _window.Size = new Vector2D<int>(width, height);

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void Restore() => _window.WindowState = WindowState.Normal;

    public void Dispose()
    {
        _input.Dispose();
        _window.Dispose();
    }
}

/// <summary>The GLFW process pump: <c>PollEvents</c> and <c>PostEmptyEvent</c>.</summary>
internal sealed class GlfwEventPump : IWindowEventPump
{
    private readonly Glfw _glfw = Glfw.GetApi();

    public void PollEvents() => _glfw.PollEvents();

    public void PostEmptyEvent() => _glfw.PostEmptyEvent();
}
