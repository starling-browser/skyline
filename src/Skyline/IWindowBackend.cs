// SPDX-License-Identifier: Apache-2.0
using Skyline.Input;

namespace Skyline;

/// <summary>
/// The windowing seam <see cref="AppWindow"/> sits on. One backend per window,
/// chosen at creation: GLFW on Windows and Linux, a native backend on Apple.
/// Backends raise raw events; <see cref="AppWindow"/> owns the frame geometry,
/// dirty pacing, and resize parking on top of them.
/// </summary>
internal interface IWindowBackend : IDisposable
{
    /// <summary>Framebuffer size in pixels.</summary>
    (int Width, int Height) FramebufferSize { get; }

    /// <summary>Window size in logical points.</summary>
    (int Width, int Height) LogicalSize { get; }

    string Title { get; set; }
    string? ClipboardText { get; set; }

    bool IsClosing { get; }

    /// <summary>How a presenter builds a surface for this window.</summary>
    WindowSurfaceSource SurfaceSource { get; }

    /// <summary>Framebuffer size changed, in pixels.</summary>
    event Action<(int Width, int Height)>? FramebufferResized;

    /// <summary>Minimized state changed; true when minimized.</summary>
    event Action<bool>? MinimizedChanged;

    /// <summary>A frame tick from the backend's own loop, with the delta seconds.</summary>
    event Action<double>? Render;

    event Action<PointerEvent>? Pointer;
    event Action<KeyEvent>? Key;
    event Action<TextEvent>? Text;

    /// <summary>Run the backend's own event loop until the window closes.</summary>
    void Run();

    /// <summary>Process pending OS events once, without the built-in loop.</summary>
    void PumpEventsOnce();

    void RequestClose();
    void Resize(int width, int height);
    void Minimize();
    void Restore();

    /// <summary>
    /// Set the OS cursor shown over the window's content. The caller drives this
    /// from whatever sits under the pointer as it moves — for example a hand
    /// over a link or an I-beam over text. Each backend maps the shape to its
    /// own platform cursor.
    /// </summary>
    void SetCursor(CursorShape shape);
}

/// <summary>
/// The process-wide event pump. GLFW and a native backend each have exactly
/// one. <see cref="AppHost"/> drives it from the main thread.
/// </summary>
internal interface IWindowEventPump
{
    /// <summary>Drain pending events without blocking.</summary>
    void PollEvents();

    /// <summary>Wake a sleeping pump from any thread.</summary>
    void PostEmptyEvent();
}
