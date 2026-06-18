// SPDX-License-Identifier: Apache-2.0
using GlfwCursorShape = Silk.NET.GLFW.CursorShape;

namespace Skyline.Input;

/// <summary>
/// Maps a <see cref="CursorShape"/> to a GLFW standard cursor, or null when GLFW
/// has no cursor for it and the default arrow should show. Kept pure so the
/// table is tested without a window. Shapes GLFW has no exact cursor for map to
/// the closest match: a four-way move uses the all-resize cursor, a grab uses
/// the hand, and the busy shapes fall back to null (GLFW has no wait cursor —
/// the window manager shows its own busy state).
/// </summary>
internal static class CursorShapeMap
{
    internal static GlfwCursorShape? ToGlfwCursor(CursorShape shape) => shape switch
    {
        CursorShape.Pointer => GlfwCursorShape.Hand,
        CursorShape.Text => GlfwCursorShape.IBeam,
        CursorShape.Crosshair => GlfwCursorShape.Crosshair,
        CursorShape.Move => GlfwCursorShape.AllResize,
        CursorShape.NotAllowed => GlfwCursorShape.NotAllowed,
        CursorShape.Grab => GlfwCursorShape.Hand,
        CursorShape.Grabbing => GlfwCursorShape.Hand,
        CursorShape.ResizeEw => GlfwCursorShape.HResize,
        CursorShape.ResizeNs => GlfwCursorShape.VResize,
        CursorShape.ResizeNwse => GlfwCursorShape.NwseResize,
        CursorShape.ResizeNesw => GlfwCursorShape.NeswResize,
        CursorShape.ResizeAll => GlfwCursorShape.AllResize,
        _ => null,
    };
}
