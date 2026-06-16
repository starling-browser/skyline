// SPDX-License-Identifier: Apache-2.0

using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Render;

/// <summary>
/// How a <see cref="FrameLoop"/> behaves. Every field has a default that
/// serves the getting-started case, and every field is a WebGPU value or a
/// pass-through to <see cref="GpuContext"/> / <see cref="WindowSurface"/>
/// options — no invented vocabulary.
/// </summary>
public sealed class FrameLoopOptions
{
    /// <summary>The color the started pass clears to. Defaults to opaque black. Ignored when <see cref="BeginClearPass"/> is false.</summary>
    public Color ClearColor { get; init; } = new() { A = 1 };

    /// <summary>
    /// When true (the default), each frame begins a render pass cleared to
    /// <see cref="ClearColor"/> and hands it to you as <see cref="Frame.Pass"/>.
    /// Set false to own every pass yourself — <see cref="Frame.Pass"/> is then
    /// null and you encode against <see cref="Frame.Encoder"/> and
    /// <see cref="Frame.View"/>. This is the compositing path.
    /// </summary>
    public bool BeginClearPass { get; init; } = true;

    /// <summary>
    /// When false (the default), the loop renders only after
    /// <see cref="FrameLoop.RequestRedraw"/> and otherwise idles. When true,
    /// it renders every frame.
    /// </summary>
    public bool Continuous { get; init; }

    /// <summary>Frames the CPU may queue ahead of the GPU. Passed to the <see cref="FramePacer"/> when <see cref="FrameLoop.Attach"/> builds one.</summary>
    public int MaxFramesInFlight { get; init; } = 2;

    /// <summary>Options for the <see cref="GpuContext"/> that <see cref="FrameLoop.Attach"/> builds. Ignored by <see cref="FrameLoop.Over"/>.</summary>
    public GpuContextOptions? Gpu { get; init; }

    /// <summary>Options for the <see cref="WindowSurface"/> that <see cref="FrameLoop.Attach"/> builds. Ignored by <see cref="FrameLoop.Over"/>.</summary>
    public WindowSurfaceOptions? Surface { get; init; }
}
