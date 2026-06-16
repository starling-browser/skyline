// SPDX-License-Identifier: Apache-2.0

using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>How a <see cref="WindowSurface"/> is configured. Applied on every <see cref="WindowSurface.Configure"/>.</summary>
public sealed class WindowSurfaceOptions
{
    /// <summary>Swapchain pixel format. Bgra8Unorm is universally supported by wgpu window surfaces.</summary>
    public TextureFormat Format { get; init; } = TextureFormat.Bgra8Unorm;

    /// <summary>
    /// Usages beyond RenderAttachment (always included). Add CopyDst to blit
    /// into the frame, CopySrc to read it back.
    /// </summary>
    public TextureUsage ExtraUsage { get; init; } = TextureUsage.None;

    /// <summary>Fifo is vsync and works everywhere. Mailbox/Immediate trade tearing rules for latency where supported.</summary>
    public PresentMode PresentMode { get; init; } = PresentMode.Fifo;

    public CompositeAlphaMode AlphaMode { get; init; } = CompositeAlphaMode.Auto;
}
