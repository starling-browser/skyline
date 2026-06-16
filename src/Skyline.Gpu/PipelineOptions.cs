// SPDX-License-Identifier: Apache-2.0

using Silk.NET.WebGPU;

namespace Skyline.Gpu;

/// <summary>
/// Inputs for <see cref="GpuContext.CreatePipeline"/>. Fills WebGPU's own
/// <see cref="RenderPipelineDescriptor"/> with overridable defaults: one
/// shader module for both stages, a single color target at
/// <see cref="ColorFormat"/>, opaque (no blend), a triangle list, and no
/// multisampling. Override any of it. Every field is a WebGPU value or a raw
/// Silk.NET handle, so the result mixes freely with raw wgpu.
/// </summary>
public sealed unsafe class PipelineOptions
{
    /// <summary>The compiled module holding both entry points. Build it with <see cref="GpuContext.CreateShaderModuleWgsl"/>.</summary>
    public required ShaderModule* Shader { get; init; }

    /// <summary>Vertex entry point. Defaults to the common <c>vs_main</c>.</summary>
    public string VertexEntry { get; init; } = "vs_main";

    /// <summary>Fragment entry point. Defaults to the common <c>fs_main</c>.</summary>
    public string FragmentEntry { get; init; } = "fs_main";

    /// <summary>The single color target's format. Pass your surface's <see cref="WindowSurface.Format"/> or a render target's format.</summary>
    public required TextureFormat ColorFormat { get; init; }

    /// <summary>
    /// The pipeline layout. Leave null only when the pipeline has no bind
    /// groups — wgpu then infers an empty layout. With bind groups, pass an
    /// explicit layout: an inferred layout can silently mismatch a hand-built
    /// bind group layout and fail far from the cause.
    /// </summary>
    public PipelineLayout* Layout { get; init; }

    /// <summary>
    /// Vertex buffer layouts. Empty (the default) draws from the shader
    /// alone — for example a full-screen triangle from the vertex index. The
    /// attribute storage each layout points at must stay alive across the
    /// <see cref="GpuContext.CreatePipeline"/> call.
    /// </summary>
    public VertexBufferLayout[] VertexBuffers { get; init; } = [];

    /// <summary>Blend state for the color target. Null (the default) is opaque — no blending.</summary>
    public BlendState? Blend { get; init; }

    /// <summary>Primitive topology. Defaults to <see cref="PrimitiveTopology.TriangleList"/>.</summary>
    public PrimitiveTopology Topology { get; init; } = PrimitiveTopology.TriangleList;

    /// <summary>Front-face winding. Defaults to counter-clockwise.</summary>
    public FrontFace FrontFace { get; init; } = FrontFace.Ccw;

    /// <summary>Face culling. Defaults to none.</summary>
    public CullMode CullMode { get; init; } = CullMode.None;

    /// <summary>Multisample count. Defaults to 1 (no multisampling).</summary>
    public uint SampleCount { get; init; } = 1;

    /// <summary>Color write mask. Defaults to all channels.</summary>
    public ColorWriteMask WriteMask { get; init; } = ColorWriteMask.All;

    /// <summary>Optional debug label.</summary>
    public string? Label { get; init; }
}
