// SPDX-License-Identifier: Apache-2.0

namespace Skyline.Input;

/// <summary>
/// A cursor shape, named for its CSS role. The window host maps it to the
/// platform cursor (a GLFW standard cursor or an <c>NSCursor</c>). A browser
/// shell sets this from the hit-tested element's <c>cursor</c> property.
/// Shapes a platform has no cursor for fall back to the default arrow.
/// </summary>
public enum CursorShape
{
    Default,
    Pointer,
    Text,
    Crosshair,
    Move,
    Wait,
    Progress,
    NotAllowed,
    Grab,
    Grabbing,
    ResizeEw,
    ResizeNs,
    ResizeNwse,
    ResizeNesw,
    ResizeAll,
}
