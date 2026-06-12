using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

// Dynamic geometry on top of Skyline input. Drag the pointer to paint.
// Press C to clear, Escape to quit. Pass --frames N to auto-close after N
// presented frames (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
    _ = int.TryParse(args[argIdx + 1], out maxFrames);

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline - interactive canvas", Width = 900, Height = 640 });
using var renderer = new CanvasRenderer(win.Surface, win.CurrentFrame);

var drawing = false;
var dirtyFrames = 30;
var presented = 0;

win.Resized += f =>
{
    renderer.Configure(f.PixelWidth, f.PixelHeight);
    dirtyFrames = 30;
};

win.PointerInput += e =>
{
    if (e.Kind == PointerEventKind.Down) drawing = true;
    if (e.Kind == PointerEventKind.Up) drawing = false;
    if (e.Kind is PointerEventKind.Down or PointerEventKind.Move && drawing)
    {
        renderer.AddDot(e.X, e.Y, win.CurrentFrame);
        dirtyFrames = Math.Max(dirtyFrames, 4);
    }
};

win.KeyInput += e =>
{
    if (!e.IsDown) return;
    if (e.Key == Key.Escape) win.RequestClose();
    if (e.Key == Key.C)
    {
        renderer.Clear();
        dirtyFrames = 30;
    }
};

win.IsDirty = () => maxFrames > 0 || dirtyFrames > 0;
win.RenderFrame += f =>
{
    if (!renderer.Render(f)) return;
    if (dirtyFrames > 0) dirtyFrames--;
    presented++;
    if (maxFrames > 0 && presented >= maxFrames) win.RequestClose();
};

var code = win.Run();
Console.WriteLine($"INTERACTIVE CANVAS OK: presented {presented} frames, dots={renderer.DotCount}");
return presented > 0 ? code : 1;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CanvasVertex(float x, float y, float r, float g, float b)
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float R = r;
    public readonly float G = g;
    public readonly float B = b;
}

internal sealed unsafe class CanvasRenderer : IDisposable
{
    private const int MaxDots = 4096;
    private const int VerticesPerDot = 6;
    private readonly GpuContext _gpu;
    private readonly WindowSurface _surface;
    private readonly FramePacer _pacer;
    private readonly WebGPU _wgpu;
    private readonly List<Dot> _dots = new();
    private WgpuBuffer* _vertexBuffer;
    private ShaderModule* _shader;
    private RenderPipeline* _pipeline;
    private bool _geometryDirty = true;
    private int _vertexCount;

    private static readonly byte[] ShaderSource = Encoding.UTF8.GetBytes("""
        struct VertexIn {
            @location(0) position: vec2f,
            @location(1) color: vec3f,
        };

        struct VertexOut {
            @builtin(position) position: vec4f,
            @location(0) color: vec3f,
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
            return vec4f(input.color, 1.0);
        }
        """ + "\0");

    private static ReadOnlySpan<byte> VertexEntry => "vs_main\0"u8;
    private static ReadOnlySpan<byte> FragmentEntry => "fs_main\0"u8;

    public CanvasRenderer(Silk.NET.Core.Contexts.INativeWindowSource surfaceSource, FrameInfo frame)
    {
        _gpu = GpuContext.Create(surfaceSource);
        _gpu.UncapturedError += (type, msg) => Console.Error.WriteLine($"wgpu error ({type}): {msg}");
        _surface = _gpu.Surface!;
        _wgpu = _gpu.Api;
        _pacer = new FramePacer(_gpu, maxFramesInFlight: 2);

        Configure(frame.PixelWidth, frame.PixelHeight);
        CreateResources();
        Seed(frame);
    }

    public int DotCount => _dots.Count;

    public void Configure(int pixelWidth, int pixelHeight)
    {
        _surface.Configure(pixelWidth, pixelHeight);
        _geometryDirty = true;
    }

    public void AddDot(float logicalX, float logicalY, FrameInfo frame)
    {
        if (_dots.Count >= MaxDots) _dots.RemoveAt(0);
        var hue = (_dots.Count * 0.011f) % 1f;
        var (r, g, b) = Hsv(hue, 0.72f, 0.96f);
        _dots.Add(new Dot(logicalX, logicalY, 8f + MathF.Sin(_dots.Count * 0.21f) * 3f, r, g, b));
        _geometryDirty = true;
    }

    public void Clear()
    {
        _dots.Clear();
        _vertexCount = 0;
        _geometryDirty = true;
    }

    public bool Render(FrameInfo frame)
    {
        _pacer.Wait();
        if (!_surface.TryAcquireFrame())
            return false;

        if (_geometryDirty)
            UploadGeometry(frame);

        var color = new RenderPassColorAttachment
        {
            View = _surface.CurrentView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.96, G = 0.96, B = 0.93, A = 1.0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &color };
        var enc = _wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = _wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _vertexBuffer, 0, (ulong)(MaxDots * VerticesPerDot * sizeof(CanvasVertex)));
        if (_vertexCount > 0)
            _wgpu.RenderPassEncoderDraw(pass, (uint)_vertexCount, 1, 0, 0);
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
            Size = (ulong)(MaxDots * VerticesPerDot * sizeof(CanvasVertex)),
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        };
        _vertexBuffer = _wgpu.DeviceCreateBuffer(_gpu.DeviceHandle, in vertexDesc);

        CreatePipeline();
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
            attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 8, ShaderLocation = 1 };
            var vertexBuffer = new VertexBufferLayout
            {
                ArrayStride = (ulong)sizeof(CanvasVertex),
                StepMode = VertexStepMode.Vertex,
                AttributeCount = 2,
                Attributes = attributes,
            };

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

    private void UploadGeometry(FrameInfo frame)
    {
        if (_dots.Count == 0)
        {
            _vertexCount = 0;
            _geometryDirty = false;
            return;
        }

        var vertices = new CanvasVertex[_dots.Count * VerticesPerDot];
        var i = 0;
        foreach (var dot in _dots)
        {
            var cx = dot.X / Math.Max(1f, frame.LogicalWidth) * 2f - 1f;
            var cy = 1f - dot.Y / Math.Max(1f, frame.LogicalHeight) * 2f;
            var rx = dot.Radius / Math.Max(1f, frame.LogicalWidth) * 2f;
            var ry = dot.Radius / Math.Max(1f, frame.LogicalHeight) * 2f;
            var left = cx - rx;
            var right = cx + rx;
            var top = cy + ry;
            var bottom = cy - ry;

            vertices[i++] = new CanvasVertex(left, bottom, dot.R, dot.G, dot.B);
            vertices[i++] = new CanvasVertex(right, bottom, dot.R, dot.G, dot.B);
            vertices[i++] = new CanvasVertex(right, top, dot.R, dot.G, dot.B);
            vertices[i++] = new CanvasVertex(left, bottom, dot.R, dot.G, dot.B);
            vertices[i++] = new CanvasVertex(right, top, dot.R, dot.G, dot.B);
            vertices[i++] = new CanvasVertex(left, top, dot.R, dot.G, dot.B);
        }

        fixed (CanvasVertex* p = vertices)
            _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _vertexBuffer, 0, p, (nuint)(vertices.Length * sizeof(CanvasVertex)));
        _vertexCount = vertices.Length;
        _geometryDirty = false;
    }

    private void Seed(FrameInfo frame)
    {
        for (var i = 0; i < 42; i++)
        {
            var t = i / 41f;
            var x = frame.LogicalWidth * (0.15f + t * 0.7f);
            var y = frame.LogicalHeight * (0.5f + MathF.Sin(t * MathF.PI * 4f) * 0.22f);
            AddDot(x, y, frame);
        }
    }

    private static (float R, float G, float B) Hsv(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1f - MathF.Abs(h * 6f % 2f - 1f));
        var m = v - c;
        var (r, g, b) = h switch
        {
            < 1f / 6f => (c, x, 0f),
            < 2f / 6f => (x, c, 0f),
            < 3f / 6f => (0f, c, x),
            < 4f / 6f => (0f, x, c),
            < 5f / 6f => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return (r + m, g + m, b + m);
    }

    public void Dispose()
    {
        _pacer.Dispose();
        if (_pipeline != null) _wgpu.RenderPipelineRelease(_pipeline);
        if (_shader != null) _wgpu.ShaderModuleRelease(_shader);
        if (_vertexBuffer != null) _wgpu.BufferRelease(_vertexBuffer);
        _gpu.Dispose();
    }

    private readonly record struct Dot(float X, float Y, float Radius, float R, float G, float B);
}
