// SPDX-License-Identifier: Apache-2.0

namespace Skyline.Input;

public enum PointerEventKind
{
    Move,
    Down,
    Up,
    Wheel,

    /// <summary>The pointer entered the window's content area.</summary>
    Enter,

    /// <summary>The pointer left the window. Clear hover and reset the cursor.</summary>
    Leave,
}

/// <summary>
/// One pointer event. Coordinates are logical pixels relative to the
/// window's content area. <see cref="Button"/> is 0 = left, 1 = right,
/// 2 = middle (meaningful for Down/Up only). <see cref="Modifiers"/> are the
/// keyboard modifiers held when the event fired.
/// </summary>
public readonly record struct PointerEvent(
    PointerEventKind Kind,
    float X,
    float Y,
    int Button,
    float WheelDx,
    float WheelDy,
    ModifierKeys Modifiers = ModifierKeys.None);

/// <summary>
/// One key transition. <see cref="Key"/> covers the common keys.
/// <see cref="Code"/> is the raw GLFW keycode for everything else.
/// <see cref="Modifiers"/> are the keyboard modifiers held when the event
/// fired. Printable text arrives separately via <see cref="TextEvent"/>.
/// </summary>
public readonly record struct KeyEvent(bool IsDown, Key Key, int Code, ModifierKeys Modifiers = ModifierKeys.None);

/// <summary>
/// A committed text character — what the user typed, after keyboard layout
/// and input-method composition. Use this for text entry, not raw keycodes.
/// </summary>
public readonly record struct TextEvent(char Character);
