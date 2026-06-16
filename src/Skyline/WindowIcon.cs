// SPDX-License-Identifier: Apache-2.0

namespace Skyline;

/// <summary>
/// A window icon as tightly-packed RGBA8 pixels, row-major, four bytes per
/// pixel. Shown in the taskbar (Windows), the dock (macOS), and the window
/// switcher. <see cref="Rgba"/> must hold <c>Width * Height * 4</c> bytes.
/// </summary>
public sealed record WindowIcon(int Width, int Height, byte[] Rgba);
