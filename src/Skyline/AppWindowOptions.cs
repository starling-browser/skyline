// SPDX-License-Identifier: Apache-2.0

namespace Skyline;

/// <summary>Creation options for an <see cref="AppWindow"/>. Sizes are logical pixels.</summary>
public sealed class AppWindowOptions
{
    public string Title { get; init; } = "Skyline";
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 600;

    /// <summary>
    /// Whether the window can be resized. Shorthand for the common case:
    /// leaving <see cref="Chrome"/> at <see cref="ChromeMode.Standard"/> and
    /// setting this to <c>false</c> is the same as <see cref="ChromeMode.Fixed"/>.
    /// Prefer <see cref="Chrome"/> for anything beyond resizable-or-not.
    /// </summary>
    public bool Resizable { get; init; } = true;

    /// <summary>The kind of OS chrome the window wears. Defaults to <see cref="ChromeMode.Standard"/>.</summary>
    public ChromeMode Chrome { get; init; } = ChromeMode.Standard;

    /// <summary>The window/app icon, applied at creation. Null leaves the platform default.</summary>
    public WindowIcon? Icon { get; init; }

    /// <summary>
    /// Use the portable GLFW backend even on platforms with a native one
    /// (macOS). An escape hatch for falling back when a native backend
    /// misbehaves; off by default.
    /// </summary>
    public bool ForceGlfw { get; init; }

    /// <summary>
    /// The chrome mode after folding in <see cref="Resizable"/>: a Standard
    /// window asked to be non-resizable becomes <see cref="ChromeMode.Fixed"/>.
    /// </summary>
    internal ChromeMode EffectiveChrome =>
        Chrome == ChromeMode.Standard && !Resizable ? ChromeMode.Fixed : Chrome;
}
