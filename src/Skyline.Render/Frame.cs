// SPDX-License-Identifier: Apache-2.0

using Silk.NET.WebGPU;

namespace Skyline.Render;

/// <summary>
/// One frame handed to <see cref="FrameLoop.OnRender"/>. A read-only ref
/// struct: its pointers are valid only for the duration of the callback and
/// must not be captured or stored. Every raw handle the loop holds is here,
/// so an app can drop to raw wgpu mid-frame.
/// </summary>
public readonly unsafe ref struct Frame
{
    internal Frame(FrameInfo info, RenderPassEncoder* pass, TextureView* view, CommandEncoder* encoder)
    {
        Info = info;
        Pass = pass;
        View = view;
        Encoder = encoder;
    }

    /// <summary>Geometry and timing for this frame (pixel size, device pixel ratio, delta seconds).</summary>
    public FrameInfo Info { get; }

    /// <summary>
    /// The started, cleared render pass — set the pipeline and draw into it.
    /// The loop ends this pass for you, so do not end it yourself. Null when
    /// <see cref="FrameLoopOptions.BeginClearPass"/> is false, in which case
    /// you begin and end every pass on <see cref="Encoder"/> yourself.
    /// </summary>
    public RenderPassEncoder* Pass { get; }

    /// <summary>This frame's swapchain texture view — the same one <see cref="Pass"/> targets. Build your own attachments against it.</summary>
    public TextureView* View { get; }

    /// <summary>
    /// The live command encoder the loop will finish and submit after your
    /// callback returns. To add your own passes, copies, or compute that ride
    /// the same submission, set <see cref="FrameLoopOptions.BeginClearPass"/>
    /// to false so <see cref="Pass"/> is null and you own the whole encoder —
    /// wgpu allows only one pass open at a time, and with a clear pass active
    /// the loop owns it.
    /// </summary>
    public CommandEncoder* Encoder { get; }
}
