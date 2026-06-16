// SPDX-License-Identifier: Apache-2.0
using Silk.NET.Core.Contexts;

namespace Skyline;

/// <summary>
/// How a window hands its drawing surface to a presenter. GLFW exposes a
/// Silk.NET <see cref="INativeWindowSource"/>; a native macOS backend exposes a
/// <c>CAMetalLayer</c> pointer instead, built through wgpu's surface factory.
/// </summary>
internal abstract class WindowSurfaceSource
{
    /// <summary>A GLFW window, usable with <c>GpuContext.Create(INativeWindowSource)</c>.</summary>
    internal sealed class Native(INativeWindowSource source) : WindowSurfaceSource
    {
        public INativeWindowSource Source { get; } = source;
    }

    /// <summary>A <c>CAMetalLayer</c> pointer, usable with wgpu's surface factory.</summary>
    internal sealed class MetalLayer(nint layer) : WindowSurfaceSource
    {
        public nint Layer { get; } = layer;
    }
}
