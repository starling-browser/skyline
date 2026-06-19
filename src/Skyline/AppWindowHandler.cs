// SPDX-License-Identifier: Apache-2.0
using Skyline.Input;

namespace Skyline;

/// <summary>The window gained or lost focus.</summary>
public readonly record struct WindowFocusEvent(bool IsFocused);

/// <summary>
/// Receives a window's input, render, resize, and focus callbacks. A window has
/// exactly one owner, so it has one handler — set <see cref="AppWindow.Handler"/>.
/// Subclass and override the callbacks you need; the rest are no-ops.
///
/// All callbacks run synchronously: input and focus on the main thread, render
/// and resize on the window's render thread (see <see cref="AppHost"/>). For
/// quick hookups that prefer lambdas over a subclass, use
/// <see cref="CallbackAppWindowHandler"/>.
/// </summary>
public abstract class AppWindowHandler
{
    /// <summary>Draw and present a frame. Skyline never presents for you. Runs on the render thread.</summary>
    public virtual void OnRenderFrame(AppWindow window, FrameInfo frame) { }

    /// <summary>Framebuffer size changed. Reconfigure your swapchain. Runs on the render thread.</summary>
    public virtual void OnResized(AppWindow window, FrameInfo frame) { }

    public virtual void OnPointerInput(AppWindow window, PointerEvent e) { }

    public virtual void OnKeyInput(AppWindow window, KeyEvent e) { }

    public virtual void OnTextInput(AppWindow window, TextEvent e) { }

    /// <summary>
    /// The window gained or lost focus. Pause animation, timers, and media
    /// while blurred; resume on focus.
    /// </summary>
    public virtual void OnFocusChanged(AppWindow window, WindowFocusEvent e) { }
}

/// <summary>
/// An <see cref="AppWindowHandler"/> whose callbacks are plain delegates, set one
/// per callback. Each is a single delegate, not a multicast event: assigning one
/// replaces it. Convenient for samples and quick hookups.
/// </summary>
public sealed class CallbackAppWindowHandler : AppWindowHandler
{
    public Action<AppWindow, FrameInfo>? RenderFrame { get; set; }
    public Action<AppWindow, FrameInfo>? Resized { get; set; }
    public Action<AppWindow, PointerEvent>? PointerInput { get; set; }
    public Action<AppWindow, KeyEvent>? KeyInput { get; set; }
    public Action<AppWindow, TextEvent>? TextInput { get; set; }
    public Action<AppWindow, WindowFocusEvent>? FocusChanged { get; set; }

    public override void OnRenderFrame(AppWindow window, FrameInfo frame) => RenderFrame?.Invoke(window, frame);
    public override void OnResized(AppWindow window, FrameInfo frame) => Resized?.Invoke(window, frame);
    public override void OnPointerInput(AppWindow window, PointerEvent e) => PointerInput?.Invoke(window, e);
    public override void OnKeyInput(AppWindow window, KeyEvent e) => KeyInput?.Invoke(window, e);
    public override void OnTextInput(AppWindow window, TextEvent e) => TextInput?.Invoke(window, e);
    public override void OnFocusChanged(AppWindow window, WindowFocusEvent e) => FocusChanged?.Invoke(window, e);
}
