// SPDX-License-Identifier: Apache-2.0
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Skyline.Input;

namespace Skyline.Apple;

/// <summary>
/// A layer-hosting <see cref="NSView"/> whose backing layer is a
/// <see cref="CAMetalLayer"/> — the surface wgpu renders into. It also turns
/// AppKit mouse and key events into Skyline's plain input structs, with
/// coordinates flipped to a top-left origin to match the GLFW backend.
/// </summary>
internal sealed class MetalView : NSView
{
    private NSTrackingArea? _tracking;

    internal event Action<PointerEvent>? Pointer;
    internal event Action<KeyEvent>? Key;
    internal event Action<TextEvent>? Text;
    internal event Action? Resized;

    internal MetalView(CGRect frame) : base(frame)
    {
        WantsLayer = true;
    }

    internal CAMetalLayer MetalLayer => (CAMetalLayer)Layer!;

    public override CALayer MakeBackingLayer() => new CAMetalLayer();

    public override bool AcceptsFirstResponder() => true;

    public override bool IsFlipped => true;

    public override void SetFrameSize(CGSize newSize)
    {
        base.SetFrameSize(newSize);
        Resized?.Invoke();
    }

    public override void UpdateTrackingAreas()
    {
        if (_tracking is not null)
        {
            RemoveTrackingArea(_tracking);
            _tracking.Dispose();
        }
        _tracking = new NSTrackingArea(
            Bounds,
            NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.MouseMoved |
            NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect,
            this,
            null);
        AddTrackingArea(_tracking);
        base.UpdateTrackingAreas();
    }

    private (float X, float Y) Local(NSEvent e)
    {
        // IsFlipped is true, so view coordinates already use a top-left origin.
        var p = ConvertPointFromView(e.LocationInWindow, null);
        return ((float)p.X, (float)p.Y);
    }

    private void Move(NSEvent e)
    {
        var (x, y) = Local(e);
        Pointer?.Invoke(new PointerEvent(PointerEventKind.Move, x, y, -1, 0, 0, AppleModifierMap.Map(e.ModifierFlags)));
    }

    private void Down(NSEvent e, int button)
    {
        var (x, y) = Local(e);
        Pointer?.Invoke(new PointerEvent(PointerEventKind.Down, x, y, button, 0, 0, AppleModifierMap.Map(e.ModifierFlags)));
    }

    private void Up(NSEvent e, int button)
    {
        var (x, y) = Local(e);
        Pointer?.Invoke(new PointerEvent(PointerEventKind.Up, x, y, button, 0, 0, AppleModifierMap.Map(e.ModifierFlags)));
    }

    public override void MouseMoved(NSEvent theEvent) => Move(theEvent);
    public override void MouseDragged(NSEvent theEvent) => Move(theEvent);
    public override void RightMouseDragged(NSEvent theEvent) => Move(theEvent);
    public override void OtherMouseDragged(NSEvent theEvent) => Move(theEvent);

    public override void MouseDown(NSEvent theEvent) => Down(theEvent, 0);
    public override void MouseUp(NSEvent theEvent) => Up(theEvent, 0);
    public override void RightMouseDown(NSEvent theEvent) => Down(theEvent, 1);
    public override void RightMouseUp(NSEvent theEvent) => Up(theEvent, 1);
    public override void OtherMouseDown(NSEvent theEvent) => Down(theEvent, (int)theEvent.ButtonNumber);
    public override void OtherMouseUp(NSEvent theEvent) => Up(theEvent, (int)theEvent.ButtonNumber);

    public override void ScrollWheel(NSEvent theEvent)
    {
        var (x, y) = Local(theEvent);
        Pointer?.Invoke(new PointerEvent(
            PointerEventKind.Wheel, x, y, -1, (float)theEvent.ScrollingDeltaX, (float)theEvent.ScrollingDeltaY,
            AppleModifierMap.Map(theEvent.ModifierFlags)));
    }

    // KeyEvent.Code is the GLFW-space keycode by contract; Skyline's Key enum
    // mirrors it, so a known key reports (int)key to match the GLFW backend. An
    // unmapped key keeps its distinct raw macOS code instead of collapsing every
    // unmapped key onto Key.Unknown's -1.
    private static KeyEvent ToKeyEvent(bool isDown, ushort macKeyCode, ModifierKeys modifiers)
    {
        // Qualify the enum: the instance event is also named Key.
        var key = AppleKeyMap.Map(macKeyCode);
        var code = key != Input.Key.Unknown ? (int)key : macKeyCode;
        return new KeyEvent(isDown, key, code, modifiers);
    }

    public override void KeyDown(NSEvent theEvent)
    {
        Key?.Invoke(ToKeyEvent(true, theEvent.KeyCode, AppleModifierMap.Map(theEvent.ModifierFlags)));
        var chars = theEvent.Characters;
        if (!string.IsNullOrEmpty(chars))
        {
            foreach (var ch in chars)
            {
                // Skip control characters so only typed text reaches TextInput,
                // matching the GLFW KeyChar path.
                if (!char.IsControl(ch))
                {
                    Text?.Invoke(new TextEvent(ch));
                }
            }
        }
    }

    public override void KeyUp(NSEvent theEvent) => Key?.Invoke(ToKeyEvent(false, theEvent.KeyCode, AppleModifierMap.Map(theEvent.ModifierFlags)));
}
