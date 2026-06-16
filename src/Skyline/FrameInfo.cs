// SPDX-License-Identifier: Apache-2.0

namespace Skyline;

/// <summary>
/// Geometry and timing for one frame. Sizes are framebuffer pixels.
/// Divide by <see cref="Dpr"/> — the device pixel ratio — for logical
/// (CSS-style) coordinates.
/// </summary>
public readonly record struct FrameInfo(
    int PixelWidth,
    int PixelHeight,
    float Dpr,
    double DeltaSeconds)
{
    public float LogicalWidth => PixelWidth / Dpr;
    public float LogicalHeight => PixelHeight / Dpr;
}
