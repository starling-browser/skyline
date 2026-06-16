// SPDX-License-Identifier: Apache-2.0
using Silk.NET.Input;

namespace Skyline.Input;

/// <summary>
/// Maps a <see cref="CursorShape"/> to a GLFW standard cursor. Kept pure so the
/// table is tested without a window. Shapes GLFW has no standard cursor for map
/// to the closest match: a four-way move uses the all-resize cursor, a grab
/// uses the hand, and the busy shapes fall back to the arrow (GLFW has no wait
/// cursor — the window manager shows its own busy state).
/// </summary>
internal static class CursorShapeMap
{
    internal static StandardCursor ToStandardCursor(CursorShape shape) => shape switch
    {
        CursorShape.Pointer => StandardCursor.Hand,
        CursorShape.Text => StandardCursor.IBeam,
        CursorShape.Crosshair => StandardCursor.Crosshair,
        CursorShape.Move => StandardCursor.ResizeAll,
        CursorShape.Wait => StandardCursor.Default,
        CursorShape.Progress => StandardCursor.Default,
        CursorShape.NotAllowed => StandardCursor.NotAllowed,
        CursorShape.Grab => StandardCursor.Hand,
        CursorShape.Grabbing => StandardCursor.Hand,
        CursorShape.ResizeEw => StandardCursor.HResize,
        CursorShape.ResizeNs => StandardCursor.VResize,
        CursorShape.ResizeNwse => StandardCursor.NwseResize,
        CursorShape.ResizeNesw => StandardCursor.NeswResize,
        CursorShape.ResizeAll => StandardCursor.ResizeAll,
        _ => StandardCursor.Default,
    };
}
