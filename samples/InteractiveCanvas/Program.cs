using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using Skyline.Render;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

// Dynamic geometry on top of Skyline's FrameLoop. Drag the pointer to paint.
// Press C to clear, Escape to quit. This file builds the dot geometry and asks
// for a redraw on input. Pass --frames N to auto-close after N presented frames
// (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
{
    _ = int.TryParse(args[argIdx + 1], out maxFrames);
}

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline - interactive canvas", Width = 900, Height = 640 });
using var loop = FrameLoop.Attach(win, new FrameLoopOptions
{
    ClearColor = new Color { R = 0.96, G = 0.96, B = 0.93, A = 1.0 },
    Continuous = maxFrames > 0, // the smoke test renders continuously; interactive idles
});
using var renderer = new CanvasRenderer(loop, win.CurrentFrame);

var drawing = false;
var presented = 0;

var handler = loop.Handler;

handler.Resized = (_, _) =>
{
    renderer.MarkDirty(); // logical size changed — geometry must recompute
    loop.RequestRedraw();
};

handler.PointerInput = (_, e) =>
{
    if (e.Kind == PointerEventKind.Down)
    {
        drawing = true;
    }

    if (e.Kind == PointerEventKind.Up)
    {
        drawing = false;
    }

    if (e.Kind is PointerEventKind.Down or PointerEventKind.Move && drawing)
    {
        renderer.AddDot(e.X, e.Y);
        loop.RequestRedraw();
    }
};

handler.KeyInput = (_, e) =>
{
    if (!e.IsDown)
    {
        return;
    }

    if (e.Key == Key.Escape)
    {
        win.RequestClose();
    }

    if (e.Key == Key.C) { renderer.Clear(); loop.RequestRedraw(); }
};

loop.RequestRedraw(); // draw the seeded canvas once at startup

loop.OnRender = (in Frame frame) =>
{
    renderer.Draw(frame);
    presented++;
    if (maxFrames > 0 && presented >= maxFrames)
    {
        win.RequestClose();
    }
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
    private readonly WebGPU _wgpu;
    private readonly TextureFormat _colorFormat;
    private readonly List<Dot> _dots = new();
    private WgpuBuffer* _vertexBuffer;
    private ShaderModule* _shader;
    private RenderPipeline* _pipeline;
    private bool _geometryDirty = true;
    private int _vertexCount;

    private const string ShaderWgsl = """
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
        """;

    public CanvasRenderer(FrameLoop loop, FrameInfo frame)
    {
        _gpu = loop.Gpu;
        _wgpu = _gpu.Api;
        _colorFormat = loop.Surface.Format;

        CreateResources();
        Seed(frame);
    }

    public int DotCount => _dots.Count;

    public void MarkDirty() => _geometryDirty = true;

    public void AddDot(float logicalX, float logicalY)
    {
        if (_dots.Count >= MaxDots)
        {
            _dots.RemoveAt(0);
        }

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

    // The loop already cleared the frame to the canvas color. We just upload
    // any changed geometry and draw the dots into the started pass.
    public void Draw(in Frame frame)
    {
        if (_geometryDirty)
        {
            UploadGeometry(frame.Info);
        }

        _wgpu.RenderPassEncoderSetPipeline(frame.Pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(frame.Pass, 0, _vertexBuffer, 0, (ulong)(MaxDots * VerticesPerDot * sizeof(CanvasVertex)));
        if (_vertexCount > 0)
        {
            _wgpu.RenderPassEncoderDraw(frame.Pass, (uint)_vertexCount, 1, 0, 0);
        }
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
        _shader = _gpu.CreateShaderModuleWgsl(ShaderWgsl);

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

        // No bind groups here, so the layout stays null and wgpu infers an
        // empty one. Only the shader, format, and vertices differ from default.
        _pipeline = _gpu.CreatePipeline(new PipelineOptions
        {
            Shader = _shader,
            ColorFormat = _colorFormat,
            VertexBuffers = [vertexBuffer],
        });
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
        {
            _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _vertexBuffer, 0, p, (nuint)(vertices.Length * sizeof(CanvasVertex)));
        }

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
            AddDot(x, y);
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

    // Release only the pipeline, shader, and vertex buffer this renderer
    // created.
    public void Dispose()
    {
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

    private readonly record struct Dot(float X, float Y, float Radius, float R, float G, float B);
}
