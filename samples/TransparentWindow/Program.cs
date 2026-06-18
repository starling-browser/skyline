// SPDX-License-Identifier: Apache-2.0
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using Skyline.Render;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

// A see-through macOS window: a transparent title bar with content drawn under
// it, and a clear color whose alpha lets the desktop show through. Drag the
// slider near the bottom to change that alpha at runtime. On Windows and Linux
// the same code falls back to a GLFW transparent-framebuffer window.
var window = new AppWindow(new AppWindowOptions
{
    Title = "Transparent — Skyline",
    Width = 720,
    Height = 480,
    Chrome = ChromeMode.Transparent,
});

// --frames N closes after N presented frames, for a headless smoke test.
var maxFrames = ReadFrameCap(args);
var frames = 0;

// The window body is a translucent blue. macOS composites the surface with
// premultiplied alpha, so the tint's RGB is scaled by the alpha (here and in
// each frame's clear below). Unpremultiplied is the only see-through mode macOS
// offers — it supports Opaque and Unpremultiplied, never Premultiplied.
const double tintR = 0.10, tintG = 0.12, tintB = 0.45;
const double startAlpha = 0.6;
using var loop = FrameLoop.Attach(window, new FrameLoopOptions
{
    Continuous = true,
    ClearColor = new Color { R = tintR * startAlpha, G = tintG * startAlpha, B = tintB * startAlpha, A = startAlpha },
    Surface = new WindowSurfaceOptions { AlphaMode = CompositeAlphaMode.Unpremultiplied },
});

using var quads = new QuadRenderer(loop.Gpu, loop.Surface.Format);
var slider = new Slider();
slider.Layout(window.CurrentFrame.LogicalWidth, window.CurrentFrame.LogicalHeight);
loop.Handler = new CallbackAppWindowHandler { PointerInput = (_, e) => slider.OnPointer(e) };

Console.WriteLine("Drag the slider to change the window transparency.");

loop.OnRender = (in Frame frame) =>
{
    slider.Layout(frame.Info.LogicalWidth, frame.Info.LogicalHeight);
    // The slider's value is the window alpha. Premultiply the tint by it so the
    // clear composites correctly; the clear pass next frame uses this.
    var alpha = slider.Value;
    loop.ClearColor = new Color { R = tintR * alpha, G = tintG * alpha, B = tintB * alpha, A = alpha };

    // The opaque slider draws on top of the translucent clear, into the pass
    // the loop already started.
    unsafe
    {
        quads.Draw(frame.Pass, slider.Quads(), frame.Info.LogicalWidth, frame.Info.LogicalHeight);
    }

    if (maxFrames > 0 && ++frames >= maxFrames)
    {
        window.RequestClose();
    }
};

return window.Run();

static int ReadFrameCap(string[] args)
{
    var i = Array.IndexOf(args, "--frames");
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) ? n : 0;
}

/// <summary>An axis-aligned rectangle in logical pixels.</summary>
readonly record struct Rect(float X, float Y, float W, float H)
{
    public bool Contains(float px, float py) => px >= X && px <= X + W && py >= Y && py <= Y + H;
}

/// <summary>One solid-color rectangle to draw.</summary>
readonly record struct Quad(Rect Rect, float R, float G, float B, float A);

/// <summary>A horizontal slider the pointer drags. Its value is 0..1.</summary>
sealed class Slider
{
    public float Value { get; private set; } = 0.6f;

    private bool _dragging;
    private Rect _track;
    private Rect _knob;
    private Rect _hit;

    public void Layout(float width, float height)
    {
        const float margin = 56f;
        const float trackHeight = 6f;
        const float knob = 22f;
        var centerY = height - 48f;
        _track = new Rect(margin, centerY - trackHeight / 2f, MathF.Max(1f, width - 2f * margin), trackHeight);
        var knobX = _track.X + Value * _track.W;
        _knob = new Rect(knobX - knob / 2f, centerY - knob / 2f, knob, knob);
        _hit = new Rect(_track.X - knob, centerY - knob, _track.W + 2f * knob, 2f * knob);
    }

    public void OnPointer(PointerEvent e)
    {
        switch (e.Kind)
        {
            case PointerEventKind.Down when _hit.Contains(e.X, e.Y):
                _dragging = true;
                SetFromX(e.X);
                break;
            case PointerEventKind.Move when _dragging:
                SetFromX(e.X);
                break;
            case PointerEventKind.Up:
                _dragging = false;
                break;
        }
    }

    private void SetFromX(float x) => Value = Math.Clamp((x - _track.X) / _track.W, 0f, 1f);

    public Quad[] Quads()
    {
        var fill = new Rect(_track.X, _track.Y, Value * _track.W, _track.H);
        return
        [
            new Quad(_track, 0.28f, 0.30f, 0.36f, 1f),
            new Quad(fill, 0.40f, 0.72f, 0.95f, 1f),
            new Quad(_knob, 0.96f, 0.97f, 0.99f, 1f),
        ];
    }
}

/// <summary>Draws solid-color rectangles into a render pass with raw wgpu.</summary>
unsafe sealed class QuadRenderer : IDisposable
{
    private const int MaxQuads = 16;
    private const int VertsPerQuad = 6;
    private const int FloatsPerVertex = 6;

    private static readonly string ShaderWgsl =
        """
        struct VertexIn { @location(0) position: vec2f, @location(1) color: vec4f };
        struct VertexOut { @builtin(position) position: vec4f, @location(0) color: vec4f };

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
    private readonly float[] _scratch = new float[MaxQuads * VertsPerQuad * FloatsPerVertex];
    private readonly ShaderModule* _shader;
    private readonly RenderPipeline* _pipeline;
    private readonly WgpuBuffer* _buffer;

    public QuadRenderer(GpuContext gpu, TextureFormat format)
    {
        _gpu = gpu;
        _wgpu = gpu.Api;
        _shader = gpu.CreateShaderModuleWgsl(ShaderWgsl);

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
        _pipeline = gpu.CreatePipeline(new PipelineOptions
        {
            Shader = _shader,
            ColorFormat = format,
            VertexBuffers = [layout],
        });

        var desc = new BufferDescriptor
        {
            Size = (ulong)(_scratch.Length * sizeof(float)),
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        };
        _buffer = _wgpu.DeviceCreateBuffer(gpu.DeviceHandle, in desc);
    }

    public void Draw(RenderPassEncoder* pass, ReadOnlySpan<Quad> quads, float width, float height)
    {
        var count = Math.Min(quads.Length, MaxQuads);
        var o = 0;
        for (var i = 0; i < count; i++)
        {
            var q = quads[i];
            var left = q.Rect.X / width * 2f - 1f;
            var right = (q.Rect.X + q.Rect.W) / width * 2f - 1f;
            var top = 1f - q.Rect.Y / height * 2f;
            var bottom = 1f - (q.Rect.Y + q.Rect.H) / height * 2f;
            o = Vertex(o, left, bottom, q);
            o = Vertex(o, right, bottom, q);
            o = Vertex(o, right, top, q);
            o = Vertex(o, left, bottom, q);
            o = Vertex(o, right, top, q);
            o = Vertex(o, left, top, q);
        }

        var vertexCount = count * VertsPerQuad;
        fixed (float* p = _scratch)
        {
            _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _buffer, 0, p, (nuint)(vertexCount * FloatsPerVertex * sizeof(float)));
        }
        _wgpu.RenderPassEncoderSetPipeline(pass, _pipeline);
        _wgpu.RenderPassEncoderSetVertexBuffer(pass, 0, _buffer, 0, (ulong)(_scratch.Length * sizeof(float)));
        _wgpu.RenderPassEncoderDraw(pass, (uint)vertexCount, 1, 0, 0);
    }

    private int Vertex(int o, float x, float y, Quad q)
    {
        _scratch[o++] = x;
        _scratch[o++] = y;
        _scratch[o++] = q.R;
        _scratch[o++] = q.G;
        _scratch[o++] = q.B;
        _scratch[o++] = q.A;
        return o;
    }

    public void Dispose()
    {
        _wgpu.RenderPipelineRelease(_pipeline);
        _wgpu.ShaderModuleRelease(_shader);
        _wgpu.BufferRelease(_buffer);
    }
}
