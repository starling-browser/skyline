// SPDX-License-Identifier: Apache-2.0
using AppKit;
using Skyline.Input;

namespace Skyline.Apple;

/// <summary>
/// Maps a <see cref="CursorShape"/> to an <see cref="NSCursor"/>. macOS has no
/// public cursor for a few shapes (a busy wait, a four-way move, the diagonal
/// resizes), so those fall back to the arrow — the system draws its own busy
/// cursor regardless.
/// </summary>
internal static class AppleCursorMap
{
    internal static NSCursor ToNsCursor(CursorShape shape) => shape switch
    {
        CursorShape.Pointer => NSCursor.PointingHandCursor,
        CursorShape.Text => NSCursor.IBeamCursor,
        CursorShape.Crosshair => NSCursor.CrosshairCursor,
        CursorShape.NotAllowed => NSCursor.OperationNotAllowedCursor,
        CursorShape.Grab => NSCursor.OpenHandCursor,
        CursorShape.Grabbing => NSCursor.ClosedHandCursor,
        CursorShape.ResizeEw => NSCursor.ResizeLeftRightCursor,
        CursorShape.ResizeNs => NSCursor.ResizeUpDownCursor,
        _ => NSCursor.ArrowCursor,
    };
}
