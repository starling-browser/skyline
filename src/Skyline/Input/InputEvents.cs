namespace Skyline.Input;

public enum PointerEventKind
{
    Move,
    Down,
    Up,
    Wheel,
}

/// <summary>
/// One pointer event. Coordinates are logical pixels relative to the
/// window's content area. <see cref="Button"/> is 0 = left, 1 = right,
/// 2 = middle (meaningful for Down/Up only).
/// </summary>
public readonly record struct PointerEvent(
    PointerEventKind Kind,
    float X,
    float Y,
    int Button,
    float WheelDx,
    float WheelDy);

/// <summary>
/// One key transition. <see cref="Key"/> covers the common keys;
/// <see cref="Code"/> is the raw GLFW keycode for everything else.
/// Printable text arrives separately via <see cref="TextEvent"/>.
/// </summary>
public readonly record struct KeyEvent(bool IsDown, Key Key, int Code);

/// <summary>A committed text character (post layout/IME, unlike raw keycodes).</summary>
public readonly record struct TextEvent(char Character);
