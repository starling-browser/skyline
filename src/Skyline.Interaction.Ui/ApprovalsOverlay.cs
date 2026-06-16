// SPDX-License-Identifier: Apache-2.0
using Silk.NET.WebGPU;
using Skyline.Gpu;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Skyline.Interaction.Ui;

/// <summary>
/// The composited desktop approvals UI. <see cref="Encode"/> opens a
/// <see cref="LoadOp.Load"/> pass against the view it is given and draws the
/// prompt and indicator on top, never wrapping or hiding the view. All decision
/// logic lives in the GPU-free <see cref="State"/>. This type only adds the
/// wgpu encode.
/// </summary>
public sealed unsafe class ApprovalsOverlay : IApprovalUi, IPointerApprovalSink, IDisposable
{
    private const int FloatsPerVertex = 6;
    // The panel chrome plus one quad per lit pixel of every label: the badge,
    // name, capability headline, detail line, countdown, three buttons, the
    // live pill, and the decision toasts. A 5x7 glyph is at most 35 lit quads,
    // so a few hundred characters reach a few thousand quads. This caps it well
    // above the realistic worst case, and Encode clamps to the buffer
    // regardless, so an over-long build degrades to clipped output rather than
    // a GPU out-of-bounds write.
    private const int MaxVertices = 49152;

    private static readonly string ShaderWgsl =
        """
        struct VertexIn {
            @location(0) position: vec2f,
            @location(1) color: vec4f,
        };
        struct VertexOut {
            @builtin(position) position: vec4f,
            @location(0) color: vec4f,
        };

        @vertex
        fn vs_main(input: VertexIn) -> VertexOut {
            var out: VertexOut;
            out.position = vec4f(input.position, 0.0, 1.0);
            out.color = input.color;
            return out;
        }

        @fragment
        fn fs_main(input: VertexOut) -> @location(0) vec4f {
            return input.color;
        }
        """;

    private readonly GpuContext _gpu;
    private readonly WebGPU _wgpu;
    private readonly TextureFormat _format;
    private readonly ApprovalSurfaceState _state;

    private ShaderModule* _shader;
    private RenderPipeline* _pipeline;
    private WgpuBuffer* _vertexBuffer;
    private float[]? _uploaded;
    private bool _disposed;

    /// <summary>
    /// Build an overlay over a GPU context. <paramref name="format"/> is the
    /// surface format the overlay draws into. <paramref name="requestRedraw"/>
    /// is called when a request arrives or is answered, so the host can
    /// schedule a redraw.
    /// </summary>
    public ApprovalsOverlay(GpuContext gpu, TextureFormat format, TimeProvider? time = null, Action? requestRedraw = null)
    {
        _gpu = gpu;
        _wgpu = gpu.Api;
        _format = format;
        _state = new ApprovalSurfaceState(time ?? TimeProvider.System, requestRedraw ?? (static () => { }));
    }

    /// <summary>The GPU-free decision core. Add live grants, hit-test, and resolve through it.</summary>
    public ApprovalSurfaceState State => _state;

    /// <inheritdoc/>
    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct = default) =>
        _state.RequestAsync(request, ct);

    /// <inheritdoc/>
    public void OnPointerDown(float x, float y) => _state.OnPointerDown(x, y);

    /// <summary>
    /// Draw the overlay into <paramref name="view"/> through a load pass on
    /// <paramref name="encoder"/>. Returns at once when nothing is showing.
    /// Call it with no other pass open on the encoder.
    /// </summary>
    public void Encode(TextureView* view, CommandEncoder* encoder, FrameInfo info)
    {
        var snapshot = _state.Snapshot;
        if (!snapshot.HasModal && !snapshot.HasToasts)
        {
            return;
        }

        EnsureResources();
        var vertices = _state.BuildVertices(info.LogicalWidth, info.LogicalHeight);
        // Never write or draw past the fixed buffer: clamp to its capacity in
        // whole vertices so an over-large build clips instead of corrupting.
        var floatCount = Math.Min(vertices.Length, MaxVertices * FloatsPerVertex);
        // BuildVertices returns the same array instance while nothing changes,
        // so skip re-uploading identical bytes — the buffer still holds them.
        if (!ReferenceEquals(vertices, _uploaded))
        {
            fixed (float* data = vertices)
            {
                _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _vertexBuffer, 0, data, (nuint)(floatCount * sizeof(float)));
            }
            _uploaded = vertices;
        }

        var attachment = new RenderPassColorAttachment
        {
            View = view,
            LoadOp = LoadOp.Load,
            StoreOp = StoreOp.Store,
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
        var pass = _wgpu.CommandEncoderBeginRenderPass(encoder, in passDesc);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, (ulong)(MaxVertices * FloatsPerVertex * sizeof(float)));
        _wgpu.RenderPassEncoderDraw(pass, (uint)(floatCount / FloatsPerVertex), 1, 0, 0);
        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);
    }

    private void EnsureResources()
    {
        if (_pipeline != null)
        {
            return;
        }

        _shader = _gpu.CreateShaderModuleWgsl(ShaderWgsl, "skyline-approvals-overlay");

        var attributes = stackalloc VertexAttribute[2];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 8, ShaderLocation = 1 };
        var layout = new VertexBufferLayout
        {
            ArrayStride = (ulong)(FloatsPerVertex * sizeof(float)),
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 2,
            Attributes = attributes,
        };

        // Straight-alpha blending so the overlay composites over the app's pixels.
        var blend = new BlendState
        {
            Color = new BlendComponent { SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
        };

        _pipeline = _gpu.CreatePipeline(new PipelineOptions
        {
            Shader = _shader,
            ColorFormat = _format,
            VertexBuffers = [layout],
            Blend = blend,
            Label = "skyline-approvals-overlay",
        });

        var bufferDesc = new BufferDescriptor
        {
            Size = (ulong)(MaxVertices * FloatsPerVertex * sizeof(float)),
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        };
        _vertexBuffer = _wgpu.DeviceCreateBuffer(_gpu.DeviceHandle, in bufferDesc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_pipeline != null)
        {
            _wgpu.RenderPipelineRelease(_pipeline);
        }
        if (_shader != null)
        {
            _wgpu.ShaderModuleRelease(_shader);
        }
        if (_vertexBuffer != null)
        {
            _wgpu.BufferRelease(_vertexBuffer);
        }
    }
}
