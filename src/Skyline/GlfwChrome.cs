// SPDX-License-Identifier: Apache-2.0
using Silk.NET.Windowing;

namespace Skyline;

/// <summary>
/// Maps a <see cref="ChromeMode"/> to the GLFW window properties that express
/// it. GLFW only reaches the portable subset: a border style and a
/// see-through framebuffer. Real native chrome lives in a native backend.
/// </summary>
internal static class GlfwChrome
{
    internal static (WindowBorder Border, bool TransparentFramebuffer) Map(ChromeMode chrome) => chrome switch
    {
        ChromeMode.Standard => (WindowBorder.Resizable, false),
        ChromeMode.Fixed => (WindowBorder.Fixed, false),
        ChromeMode.Borderless => (WindowBorder.Hidden, false),
        ChromeMode.Transparent => (WindowBorder.Hidden, true),
        // GLFW can't float the buttons over content, so fall back to a normal
        // decorated window.
        ChromeMode.UnifiedTitlebar => (WindowBorder.Resizable, false),
        _ => throw new ArgumentOutOfRangeException(nameof(chrome)),
    };
}
