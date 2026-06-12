using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

// A first real WebGPU pipeline on top of Skyline: shader module, vertex
// buffer, texture upload, sampler, bind group, render pipeline, and draw.
//
// Space pauses the rotation, Escape quits. Pass --frames N to auto-close
// after N presented frames (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
    _ = int.TryParse(args[argIdx + 1], out maxFrames);

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline - textured quad", Width = 800, Height = 600 });
using var renderer = new TexturedQuadRenderer(win.Surface, win.CurrentFrame);

var animate = true;
var presented = 0;

win.Resized += f => renderer.Configure(f.PixelWidth, f.PixelHeight);
win.KeyInput += e =>
{
    if (!e.IsDown) return;
    if (e.Key == Key.Escape) win.RequestClose();
    if (e.Key == Key.Space) animate = !animate;
};

win.IsDirty = () => maxFrames > 0 || animate;
win.RenderFrame += f =>
{
    if (!renderer.Render(f, animate)) return;
    presented++;
    if (maxFrames > 0 && presented >= maxFrames) win.RequestClose();
};

var code = win.Run();
Console.WriteLine($"TEXTURED QUAD OK: presented {presented} frames with an app-owned WebGPU pipeline");
return presented > 0 ? code : 1;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct Vertex(float x, float y, float u, float v)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float U = u;
    public readonly float V = v;
}

internal sealed unsafe class TexturedQuadRenderer : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly WindowSurface _surface;
    private readonly FramePacer _pacer;
    private readonly WebGPU _wgpu;
    private WgpuBuffer* _vertexBuffer;
    private Texture* _texture;
    private TextureView* _textureView;
    private Sampler* _sampler;
    private BindGroupLayout* _bindGroupLayout;
    private PipelineLayout* _pipelineLayout;
    private BindGroup* _bindGroup;
    private ShaderModule* _shader;
    private RenderPipeline* _pipeline;
    private float _angle;

    private static readonly byte[] ShaderSource = Encoding.UTF8.GetBytes("""
        struct VertexIn {
            @location(0) position: vec2f,
            @location(1) uv: vec2f,
        };

        struct VertexOut {
            @builtin(position) position: vec4f,
            @location(0) uv: vec2f,
        };

        @vertex
        fn vs_main(input: VertexIn) -> VertexOut {
            var out: VertexOut;
            out.position = vec4f(input.position, 0.0, 1.0);
            out.uv = input.uv;
            return out;
        }

        @group(0) @binding(0) var quadSampler: sampler;
        @group(0) @binding(1) var quadTexture: texture_2d<f32>;

        @fragment
        fn fs_main(input: VertexOut) -> @location(0) vec4f {
            return textureSample(quadTexture, quadSampler, input.uv);
        }
        """ + "\0");

    private static ReadOnlySpan<byte> VertexEntry => "vs_main\0"u8;
    private static ReadOnlySpan<byte> FragmentEntry => "fs_main\0"u8;

    public TexturedQuadRenderer(Silk.NET.Core.Contexts.INativeWindowSource surfaceSource, FrameInfo frame)
    {
        _gpu = GpuContext.Create(surfaceSource);
        _gpu.UncapturedError += (type, msg) => Console.Error.WriteLine($"wgpu error ({type}): {msg}");
        _surface = _gpu.Surface!;
        _wgpu = _gpu.Api;
        _pacer = new FramePacer(_gpu, maxFramesInFlight: 2);

        Configure(frame.PixelWidth, frame.PixelHeight);
        CreateResources();
    }

    public void Configure(int pixelWidth, int pixelHeight) =>
        _surface.Configure(pixelWidth, pixelHeight);

    public bool Render(FrameInfo frame, bool animate)
    {
        _pacer.Wait();
        if (!_surface.TryAcquireFrame())
            return false;

        if (animate) _angle = (_angle + (float)frame.DeltaSeconds * 0.8f) % (MathF.PI * 2f);
        UpdateVertices(frame);

        var color = new RenderPassColorAttachment
        {
            View = _surface.CurrentView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.055, G = 0.065, B = 0.075, A = 1.0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color };
        var enc = _wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = _wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, (ulong)(6 * sizeof(Vertex)));
        _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _bindGroup, 0, (uint*)null);
        _wgpu.RenderPassEncoderDraw(pass, 6, 1, 0, 0);
        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);

        var cmd = _wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        _wgpu.QueueSubmit(_gpu.QueueHandle, 1, &cmd);
        _pacer.FrameSubmitted();
        _wgpu.CommandBufferRelease(cmd);
        _wgpu.CommandEncoderRelease(enc);
        _surface.Present();
        return true;
    }

    private void CreateResources()
    {
        var vertexDesc = new BufferDescriptor
        {
            Size = (ulong)(6 * sizeof(Vertex)),
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        };
        _vertexBuffer = _wgpu.DeviceCreateBuffer(_gpu.DeviceHandle, in vertexDesc);

        CreateTexture();

        var samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        };
        _sampler = _wgpu.DeviceCreateSampler(_gpu.DeviceHandle, in samplerDesc);

        var viewDesc = new TextureViewDescriptor
        {
            Format = TextureFormat.Rgba8Unorm,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };
        _textureView = _wgpu.TextureCreateView(_texture, in viewDesc);

        CreatePipeline();
        CreateBindGroup();
    }

    private void CreateTexture()
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var cell = ((x / 16) + (y / 16)) % 2 == 0;
            var dx = x - size / 2f;
            var dy = y - size / 2f;
            var ring = MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - 38f) < 4f;
            var o = (y * size + x) * 4;
            pixels[o + 0] = (byte)(ring ? 255 : cell ? 235 : 30);
            pixels[o + 1] = (byte)(ring ? 210 : cell ? 245 : 70);
            pixels[o + 2] = (byte)(ring ? 80 : cell ? 255 : 120);
            pixels[o + 3] = 255;
        }

        var desc = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Format = TextureFormat.Rgba8Unorm,
            Size = new Extent3D { Width = size, Height = size, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding,
        };
        _texture = _wgpu.DeviceCreateTexture(_gpu.DeviceHandle, in desc);

        var dst = new ImageCopyTexture { Texture = _texture, MipLevel = 0, Origin = default, Aspect = TextureAspect.All };
        var layout = new TextureDataLayout { Offset = 0, BytesPerRow = size * 4, RowsPerImage = size };
        var extent = new Extent3D { Width = size, Height = size, DepthOrArrayLayers = 1 };
        fixed (byte* p = pixels)
            _wgpu.QueueWriteTexture(_gpu.QueueHandle, in dst, p, (nuint)pixels.Length, in layout, in extent);
    }

    private void CreatePipeline()
    {
        fixed (byte* shaderBytes = ShaderSource)
        fixed (byte* vsEntry = VertexEntry)
        fixed (byte* fsEntry = FragmentEntry)
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct(null, SType.ShaderModuleWgslDescriptor),
                Code = shaderBytes,
            };
            var shaderDesc = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            _shader = _wgpu.DeviceCreateShaderModule(_gpu.DeviceHandle, in shaderDesc);

            var attributes = stackalloc VertexAttribute[2];
            attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
            attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 8, ShaderLocation = 1 };
            var vertexBuffer = new VertexBufferLayout
            {
                ArrayStride = (ulong)sizeof(Vertex),
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 2,
                Attributes = attributes,
            };

            var layoutEntries = stackalloc BindGroupLayoutEntry[2];
            layoutEntries[0] = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
            };
            layoutEntries[1] = new BindGroupLayoutEntry
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
            var bglDesc = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = layoutEntries };
            _bindGroupLayout = _wgpu.DeviceCreateBindGroupLayout(_gpu.DeviceHandle, in bglDesc);

            var layouts = stackalloc BindGroupLayout*[1];
            layouts[0] = _bindGroupLayout;
            var pipelineLayoutDesc = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = layouts,
            };
            _pipelineLayout = _wgpu.DeviceCreatePipelineLayout(_gpu.DeviceHandle, in pipelineLayoutDesc);

            var colorTarget = new ColorTargetState { Format = _surface.Format, WriteMask = ColorWriteMask.All };
            var vertexState = new VertexState
            {
                Module = _shader,
                EntryPoint = vsEntry,
                BufferCount = 1,
                Buffers = &vertexBuffer,
            };
            var fragmentState = new FragmentState
            {
                Module = _shader,
                EntryPoint = fsEntry,
                TargetCount = 1,
                Targets = &colorTarget,
            };
            var pipelineDesc = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = vertexState,
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    FrontFace = FrontFace.Ccw,
                    CullMode = CullMode.None,
                },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue, AlphaToCoverageEnabled = false },
                Fragment = &fragmentState,
            };
            _pipeline = _wgpu.DeviceCreateRenderPipeline(_gpu.DeviceHandle, in pipelineDesc);
        }
    }

    private void CreateBindGroup()
    {
        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, Sampler = _sampler };
        entries[1] = new BindGroupEntry { Binding = 1, TextureView = _textureView };
        var desc = new BindGroupDescriptor
        {
            Layout = _bindGroupLayout,
            EntryCount = 2,
            Entries = entries,
        };
        _bindGroup = _wgpu.DeviceCreateBindGroup(_gpu.DeviceHandle, in desc);
    }

    private void UpdateVertices(FrameInfo frame)
    {
        var scale = 0.62f + MathF.Sin(_angle * 1.7f) * 0.05f;
        var aspect = frame.PixelHeight <= 0 ? 1f : frame.PixelWidth / (float)frame.PixelHeight;
        var sx = aspect >= 1f ? scale / aspect : scale;
        var sy = aspect >= 1f ? scale : scale * aspect;
        var c = MathF.Cos(_angle);
        var s = MathF.Sin(_angle);

        Span<Vertex> vertices =
        [
            Transform(-sx, -sy, 0, 1, c, s),
            Transform( sx, -sy, 1, 1, c, s),
            Transform( sx,  sy, 1, 0, c, s),
            Transform(-sx, -sy, 0, 1, c, s),
            Transform( sx,  sy, 1, 0, c, s),
            Transform(-sx,  sy, 0, 0, c, s),
        ];

        fixed (Vertex* p = vertices)
            _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _vertexBuffer, 0, p, (nuint)(vertices.Length * sizeof(Vertex)));
    }

    private static Vertex Transform(float x, float y, float u, float v, float c, float s) =>
        new(x * c - y * s, x * s + y * c, u, v);

    public void Dispose()
    {
        _pacer.Dispose();
        if (_pipeline != null) _wgpu.RenderPipelineRelease(_pipeline);
        if (_shader != null) _wgpu.ShaderModuleRelease(_shader);
        if (_bindGroup != null) _wgpu.BindGroupRelease(_bindGroup);
        if (_pipelineLayout != null) _wgpu.PipelineLayoutRelease(_pipelineLayout);
        if (_bindGroupLayout != null) _wgpu.BindGroupLayoutRelease(_bindGroupLayout);
        if (_sampler != null) _wgpu.SamplerRelease(_sampler);
        if (_textureView != null) _wgpu.TextureViewRelease(_textureView);
        if (_texture != null) _wgpu.TextureRelease(_texture);
        if (_vertexBuffer != null) _wgpu.BufferRelease(_vertexBuffer);
        _gpu.Dispose();
    }
}
