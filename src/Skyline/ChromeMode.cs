// SPDX-License-Identifier: Apache-2.0
namespace Skyline;

/// <summary>
/// The kind of OS chrome a window wears. The window host maps this to what
/// the active backend can express: GLFW honors the portable subset (border,
/// transparent framebuffer), while a native backend can render real platform
/// chrome — a macOS transparent titlebar, for one.
/// </summary>
public enum ChromeMode
{
    /// <summary>A normal decorated, resizable window. The default.</summary>
    Standard,

    /// <summary>Decorated, but not resizable.</summary>
    Fixed,

    /// <summary>No border, title bar, or buttons. The app draws its own.</summary>
    Borderless,

    /// <summary>
    /// Frameless with a see-through backing, so content can show what is
    /// behind the window. On a native macOS backend this is a real
    /// transparent title bar with content extending under it.
    /// </summary>
    Transparent,

    /// <summary>
    /// An opaque window whose title bar merges into the content: the traffic
    /// lights float over the app and there is no separate top bar. On a native
    /// macOS backend this is a transparent title bar with a hidden title and
    /// content drawn full height under the buttons. Other backends fall back to
    /// a standard decorated window.
    /// </summary>
    UnifiedTitlebar,
}
