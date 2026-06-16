using Silk.NET.WebGPU;
using Skyline.Gpu;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Skyline.Gpu.Tests;

/// <summary>
/// Covers the descriptor-fillers (<see cref="GpuContext.CreatePipeline"/>,
/// <see cref="GpuContext.CreateBindGroup"/>, <see cref="GpuContext.CreateColorTarget"/>)
/// by rendering through them into an offscreen target and reading the pixels
/// back — the same path Starling composites on. Each filler is exercised with
/// and without a label so both branches run.
/// </summary>
[TestClass]
public unsafe class DescriptorFillerTests
{
    private const int Size = 32;

    private const string TriangleShader = """
        @vertex fn vs_main(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
            var p = array(vec2f(-0.8, -0.8), vec2f(0.8, -0.8), vec2f(0.0, 0.8));
            return vec4f(p[i], 0.0, 1.0);
        }
        @fragment fn fs_main() -> @location(0) vec4f { return vec4f(0.2, 0.9, 0.4, 1.0); }
        """;

    private const string TexturedShader = """
        struct VOut { @builtin(position) pos: vec4f, @location(0) uv: vec2f };
        @vertex fn vs_main(@location(0) position: vec2f, @location(1) uv: vec2f) -> VOut {
            var o: VOut;
            o.pos = vec4f(position, 0.0, 1.0);
            o.uv = uv;
            return o;
        }
        @group(0) @binding(0) var samp: sampler;
        @group(0) @binding(1) var tex: texture_2d<f32>;
        @fragment fn fs_main(in: VOut) -> @location(0) vec4f { return textureSample(tex, samp, in.uv); }
        """;

    [TestMethod]
    public void CreatePipeline_NoVertexBufferNoLabel_RendersTriangle()
    {
        using var gpu = GpuContext.CreateHeadless();
        var wgpu = gpu.Api;
        gpu.UncapturedError += (type, msg) => Assert.Fail($"wgpu error ({type}): {msg}");

        var shader = gpu.CreateShaderModuleWgsl(TriangleShader);
        // No label, no blend, empty vertex buffers, null layout — every default.
        var pipeline = gpu.CreatePipeline(new PipelineOptions
        {
            Shader = shader,
            ColorFormat = TextureFormat.Rgba8Unorm,
        });

        // No label on the target either, exercising that branch.
        var target = gpu.CreateColorTarget(Size, Size, TextureFormat.Rgba8Unorm, extraUsage: TextureUsage.CopySrc);
        var view = wgpu.TextureCreateView(target, (TextureViewDescriptor*)null);

        using var readback = new TextureReadback(gpu, Size, Size);
        var pixels = RenderAndRead(gpu, view, target, readback,
            new Color { R = 0, G = 0, B = 0, A = 1 }, pipeline, vertexCount: 3, null, 0, null);

        // Center is inside the triangle (green); the top-left corner is the clear (black).
        AssertChannel(pixels, Size / 2, Size / 2, channel: 1, atLeast: 200);  // G high
        AssertChannel(pixels, Size / 2, Size / 2, channel: 0, atMost: 90);    // R low
        AssertChannel(pixels, 0, 0, channel: 1, atMost: 20);                  // clear: G ~ 0

        wgpu.TextureViewRelease(view);
        wgpu.TextureRelease(target);
        wgpu.RenderPipelineRelease(pipeline);
        wgpu.ShaderModuleRelease(shader);
    }

    [TestMethod]
    public void CreatePipeline_WithBindGroupBlendVertexBufferAndLabels_RendersTexture()
    {
        using var gpu = GpuContext.CreateHeadless();
        var wgpu = gpu.Api;
        gpu.UncapturedError += (type, msg) => Assert.Fail($"wgpu error ({type}): {msg}");

        // A 1x1 solid-red source texture to sample.
        var srcDesc = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Format = TextureFormat.Rgba8Unorm,
            Size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
        };
        var srcTex = wgpu.DeviceCreateTexture(gpu.DeviceHandle, in srcDesc);
        var srcView = wgpu.TextureCreateView(srcTex, (TextureViewDescriptor*)null);
        var red = new byte[] { 255, 0, 0, 255 };
        var copyDst = new ImageCopyTexture { Texture = srcTex, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var copyLayout = new TextureDataLayout { Offset = 0, BytesPerRow = 4, RowsPerImage = 1 };
        var copyExtent = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 };
        fixed (byte* p = red)
        {
            wgpu.QueueWriteTexture(gpu.QueueHandle, in copyDst, p, 4, in copyLayout, in copyExtent);
        }

        var samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Nearest,
            MinFilter = FilterMode.Nearest,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        };
        var sampler = wgpu.DeviceCreateSampler(gpu.DeviceHandle, in samplerDesc);

        var bglEntries = stackalloc BindGroupLayoutEntry[2];
        bglEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        bglEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false,
            },
        };
        var bglDesc = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = bglEntries };
        var bgl = wgpu.DeviceCreateBindGroupLayout(gpu.DeviceHandle, in bglDesc);

        var layouts = stackalloc BindGroupLayout*[1];
        layouts[0] = bgl;
        var plDesc = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = layouts };
        var pl = wgpu.DeviceCreatePipelineLayout(gpu.DeviceHandle, in plDesc);

        // Full-screen quad: x, y, u, v interleaved (6 vertices).
        var verts = new float[]
        {
            -1f, -1f, 0f, 1f,   1f, -1f, 1f, 1f,   1f, 1f, 1f, 0f,
            -1f, -1f, 0f, 1f,   1f, 1f, 1f, 0f,   -1f, 1f, 0f, 0f,
        };
        var vbDesc = new BufferDescriptor
        {
            Size = (ulong)(verts.Length * sizeof(float)),
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        };
        var vb = wgpu.DeviceCreateBuffer(gpu.DeviceHandle, in vbDesc);
        fixed (float* p = verts)
        {
            wgpu.QueueWriteBuffer(gpu.QueueHandle, vb, 0, p, (nuint)(verts.Length * sizeof(float)));
        }

        var attrs = stackalloc VertexAttribute[2];
        attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
        attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
        var vbLayout = new VertexBufferLayout
        {
            ArrayStride = 16,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 2,
            Attributes = attrs,
        };

        var blend = new BlendState
        {
            Color = new BlendComponent { SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
        };

        var shader = gpu.CreateShaderModuleWgsl(TexturedShader);
        // Label, blend, explicit layout, and a vertex buffer — the other branches.
        var pipeline = gpu.CreatePipeline(new PipelineOptions
        {
            Shader = shader,
            ColorFormat = TextureFormat.Rgba8Unorm,
            Layout = pl,
            VertexBuffers = [vbLayout],
            Blend = blend,
            Label = "textured-quad",
        });

        Span<BindGroupEntry> entries =
        [
            new BindGroupEntry { Binding = 0, Sampler = sampler },
            new BindGroupEntry { Binding = 1, TextureView = srcView },
        ];
        var bindGroup = gpu.CreateBindGroup(bgl, entries, label: "quad-bindings");

        var target = gpu.CreateColorTarget(Size, Size, TextureFormat.Rgba8Unorm, TextureUsage.CopySrc, label: "composite-target");
        var view = wgpu.TextureCreateView(target, (TextureViewDescriptor*)null);

        using var readback = new TextureReadback(gpu, Size, Size);
        var pixels = RenderAndRead(gpu, view, target, readback,
            new Color { R = 0, G = 0, B = 0, A = 1 }, pipeline,
            vertexCount: 6, vb, (ulong)(verts.Length * sizeof(float)), bindGroup);

        // The whole target samples the red texel.
        AssertChannel(pixels, Size / 2, Size / 2, channel: 0, atLeast: 200); // R high
        AssertChannel(pixels, Size / 2, Size / 2, channel: 1, atMost: 60);   // G low
        AssertChannel(pixels, Size / 2, Size / 2, channel: 2, atMost: 60);   // B low

        wgpu.TextureViewRelease(view);
        wgpu.TextureRelease(target);
        wgpu.BindGroupRelease(bindGroup);
        wgpu.RenderPipelineRelease(pipeline);
        wgpu.ShaderModuleRelease(shader);
        wgpu.BufferRelease(vb);
        wgpu.PipelineLayoutRelease(pl);
        wgpu.BindGroupLayoutRelease(bgl);
        wgpu.SamplerRelease(sampler);
        wgpu.TextureViewRelease(srcView);
        wgpu.TextureRelease(srcTex);
    }

    [TestMethod]
    public void CreateBindGroup_WithoutLabel_BuildsValidGroup()
    {
        using var gpu = GpuContext.CreateHeadless();
        var wgpu = gpu.Api;
        gpu.UncapturedError += (type, msg) => Assert.Fail($"wgpu error ({type}): {msg}");

        var bufDesc = new BufferDescriptor { Size = 16, Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
        var buffer = wgpu.DeviceCreateBuffer(gpu.DeviceHandle, in bufDesc);

        var entry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform },
        };
        var bglDesc = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &entry };
        var bgl = wgpu.DeviceCreateBindGroupLayout(gpu.DeviceHandle, in bglDesc);

        Span<BindGroupEntry> entries =
        [
            new BindGroupEntry { Binding = 0, Buffer = buffer, Offset = 0, Size = 16 },
        ];
        // No label — the other branch of CreateBindGroup.
        var bindGroup = gpu.CreateBindGroup(bgl, entries);
        gpu.Poll(wait: false);

        Assert.IsTrue(bindGroup != null);

        wgpu.BindGroupRelease(bindGroup);
        wgpu.BindGroupLayoutRelease(bgl);
        wgpu.BufferRelease(buffer);
    }

    private static byte[] RenderAndRead(GpuContext gpu, TextureView* view, Texture* target, TextureReadback readback,
        Color clear, RenderPipeline* pipeline, uint vertexCount, WgpuBuffer* vertexBuffer, ulong vertexBytes, BindGroup* bindGroup)
    {
        var wgpu = gpu.Api;
        var att = new RenderPassColorAttachment { View = view, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = clear };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
        var enc = wgpu.DeviceCreateCommandEncoder(gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        wgpu.RenderPassEncoderSetPipeline(pass, pipeline);
        if (vertexBuffer != null)
        {
            wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, vertexBuffer, 0, vertexBytes);
        }

        if (bindGroup != null)
        {
            wgpu.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, (uint*)null);
        }

        wgpu.RenderPassEncoderDraw(pass, vertexCount, 1, 0, 0);
        wgpu.RenderPassEncoderEnd(pass);
        wgpu.RenderPassEncoderRelease(pass);
        readback.Encode(enc, target);
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(gpu.QueueHandle, 1, &cmd);
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);
        return readback.Resolve();
    }

    private static void AssertChannel(byte[] pixels, int x, int y, int channel, int atLeast = 0, int atMost = 255)
    {
        var v = pixels[(y * Size + x) * 4 + channel];
        Assert.IsTrue(v >= atLeast && v <= atMost, $"channel {channel} at ({x},{y}) = {v}, expected [{atLeast},{atMost}]");
    }
}
