// SPDX-License-Identifier: Apache-2.0
namespace Skyline;

/// <summary>
/// The AppKit window settings a <see cref="ChromeMode"/> resolves to, as plain
/// values with no AppKit types. The native macOS backend applies these to a
/// real <c>NSWindow</c>; keeping the table here lets it be tested without a Mac.
/// </summary>
/// <param name="StyleMask">An <c>NSWindowStyleMask</c> bit set.</param>
/// <param name="TitlebarTransparent">Sets <c>titlebarAppearsTransparent</c>.</param>
/// <param name="Opaque">The window's <c>opaque</c> flag.</param>
/// <param name="HideTitle">Hides the title text so the bar merges with content.</param>
internal readonly record struct AppKitChrome(uint StyleMask, bool TitlebarTransparent, bool Opaque, bool HideTitle);

/// <summary>Maps a <see cref="ChromeMode"/> to <see cref="AppKitChrome"/>.</summary>
internal static class AppKitChromeMap
{
    // NSWindowStyleMask bits.
    private const uint Titled = 1 << 0;
    private const uint Closable = 1 << 1;
    private const uint Miniaturizable = 1 << 2;
    private const uint Resizable = 1 << 3;
    private const uint FullSizeContentView = 1 << 15;

    internal static AppKitChrome Map(ChromeMode chrome) => chrome switch
    {
        ChromeMode.Standard => new(Titled | Closable | Miniaturizable | Resizable, false, true, false),
        ChromeMode.Fixed => new(Titled | Closable | Miniaturizable, false, true, false),
        ChromeMode.Borderless => new(0, false, true, false),
        ChromeMode.Transparent => new(Titled | Closable | Miniaturizable | Resizable | FullSizeContentView, true, false, false),
        ChromeMode.UnifiedTitlebar => new(Titled | Closable | Miniaturizable | Resizable | FullSizeContentView, true, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(chrome)),
    };
}
