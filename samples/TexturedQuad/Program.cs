using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using Skyline.Render;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

// A full-screen GPU visualization on top of Skyline's FrameLoop: the Milky Way,
// seen as a tilted spiral disk. One fragment shader draws a glowing core bulge,
// two logarithmic spiral arms studded with blue star clusters and pink nebulae,
// dark dust lanes, and a field of background stars, all turning slowly. No vertex
// buffer and no texture — a full-screen triangle from the vertex index feeds the
// shader, and a small uniform buffer carries resolution, time, and the mouse.
//
// Everything moves at a crawl on purpose: a slow turn reads as calm and never
// flashes. Move the mouse to tilt the view, Space pauses, Escape quits. Pass
// --frames N to auto-close after N presented frames (smoke test).

var maxFrames = 0;
var argIdx = Array.IndexOf(args, "--frames");
if (argIdx >= 0 && argIdx + 1 < args.Length)
{
    _ = int.TryParse(args[argIdx + 1], out maxFrames);
}

using var win = new AppWindow(new AppWindowOptions { Title = "Skyline - Milky Way", Width = 960, Height = 600 });
using var loop = FrameLoop.Attach(win, new FrameLoopOptions
{
    ClearColor = new Color { R = 0.0, G = 0.0, B = 0.0, A = 1.0 },
    Continuous = true,
});
using var renderer = new GalaxyRenderer(loop);

var animate = true;
var presented = 0;

win.KeyInput += e =>
{
    if (!e.IsDown)
    {
        return;
    }

    if (e.Key == Key.Escape)
    {
        win.RequestClose();
    }

    if (e.Key == Key.Space)
    {
        animate = !animate;
    }
};

win.PointerInput += e =>
{
    if (e.Kind == PointerEventKind.Move)
    {
        renderer.SetMouse(e.X, e.Y);
    }
};

loop.OnRender = (in Frame frame) =>
{
    renderer.Draw(frame, animate);
    presented++;
    if (maxFrames > 0 && presented >= maxFrames)
    {
        win.RequestClose();
    }
};

var code = win.Run();
Console.WriteLine($"MILKY WAY OK: presented {presented} frames with an app-owned WebGPU pipeline");
return presented > 0 ? code : 1;

// resolution (xy), time (z), then the mouse (xy) — laid out to match the WGSL
// uniform's 16-byte alignment, so the struct copies straight across.
[StructLayout(LayoutKind.Sequential)]
internal struct Uniforms
{
    public float ResX;
    public float ResY;
    public float Time;
    public float Pad0;
    public float MouseX;
    public float MouseY;
    public float Pad1;
    public float Pad2;
}

internal sealed unsafe class GalaxyRenderer : IDisposable
{
    private readonly GpuContext _gpu;
    private readonly WebGPU _wgpu;
    private readonly TextureFormat _colorFormat;
    private WgpuBuffer* _uniformBuffer;
    private BindGroupLayout* _bindGroupLayout;
    private PipelineLayout* _pipelineLayout;
    private BindGroup* _bindGroup;
    private ShaderModule* _shader;
    private RenderPipeline* _pipeline;

    private float _time;
    private float _mouseX = -1f;
    private float _mouseY = -1f;

    private const string ShaderWgsl = """
        struct Uniforms {
            resolution: vec2f,
            time: f32,
            _pad0: f32,
            mouse: vec2f,
            _pad1: vec2f,
        };
        @group(0) @binding(0) var<uniform> u: Uniforms;

        @vertex
        fn vs_main(@builtin(vertex_index) vid: u32) -> @builtin(position) vec4f {
            // One oversized triangle covers the whole surface.
            var corners = array<vec2f, 3>(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            return vec4f(corners[vid], 0.0, 1.0);
        }

        const TAU: f32 = 6.2831853;

        fn rot(a: f32) -> mat2x2<f32> {
            let c = cos(a);
            let s = sin(a);
            return mat2x2<f32>(c, s, -s, c);
        }

        fn hash21(p: vec2f) -> f32 {
            var p3 = fract(vec3f(p.x, p.y, p.x) * 0.1031);
            p3 = p3 + dot(p3, vec3f(p3.y, p3.z, p3.x) + vec3f(33.33));
            return fract((p3.x + p3.y) * p3.z);
        }

        fn vnoise(p: vec2f) -> f32 {
            let i = floor(p);
            let f = fract(p);
            let w = f * f * (3.0 - 2.0 * f);
            let a = hash21(i);
            let b = hash21(i + vec2f(1.0, 0.0));
            let c = hash21(i + vec2f(0.0, 1.0));
            let e = hash21(i + vec2f(1.0, 1.0));
            return mix(mix(a, b, w.x), mix(c, e, w.x), w.y);
        }

        // Stacked octaves of value noise: smooth, cloudy fields for the nebulae.
        fn fbm(p: vec2f) -> f32 {
            var v = 0.0;
            var amp = 0.5;
            var q = p;
            for (var i = 0; i < 5; i = i + 1) {
                v = v + amp * vnoise(q);
                q = q * 2.03;
                amp = amp * 0.5;
            }
            return v;
        }

        // A field of round stars on a hashed grid: varied brightness, warm-to-blue
        // tint, slow twinkle. The threshold sets how many cells hold a star.
        fn starField(p: vec2f, t: f32, threshold: f32, radius: f32) -> vec3f {
            let cell = floor(p);
            let f = fract(p);
            var c = vec3f(0.0);
            for (var y = -1; y <= 1; y = y + 1) {
                for (var x = -1; x <= 1; x = x + 1) {
                    let nb = vec2f(f32(x), f32(y));
                    let h = hash21(cell + nb);
                    if (h < threshold) {
                        continue;
                    }
                    let off = vec2f(hash21(cell + nb + vec2f(7.1, 1.3)), hash21(cell + nb + vec2f(0.7, 13.7)));
                    let d = length(nb + off - f);
                    let mag = pow(hash21(cell + nb + vec2f(3.3, 9.1)), 4.0);
                    let twinkle = 0.65 + 0.35 * sin(t + h * TAU);
                    let tint = mix(vec3f(1.0, 0.82, 0.6), vec3f(0.72, 0.85, 1.0), hash21(cell + nb + vec2f(5.7, 2.1)));
                    c = c + tint * smoothstep(radius, 0.0, d) * (0.25 + mag) * twinkle;
                }
            }
            return c;
        }

        @fragment
        fn fs_main(@builtin(position) fragCoord: vec4f) -> @location(0) vec4f {
            let res = u.resolution;
            var uv = (fragCoord.xy - 0.5 * res) / res.y;
            let t = u.time;

            // The mouse tilts the view; the default is a pleasing angle.
            let m = (u.mouse - vec2f(0.5)) * 2.0;
            uv = rot(0.32 + m.x * 0.4) * uv;

            var col = vec3f(0.006, 0.008, 0.018);

            // Deep background star fields at three depths, holding still as the disk turns.
            col = col + starField(uv * 7.0 + vec2f(31.0), t, 0.86, 0.09) * 0.9;
            col = col + starField(uv * 15.0 + vec2f(7.0), t, 0.85, 0.06) * 0.7;
            col = col + starField(uv * 30.0 + vec2f(53.0), t, 0.86, 0.045) * 0.5;

            // Galaxy plane: incline the disk, then turn slowly clockwise.
            let incl = 0.42;
            var d = vec2f(uv.x, uv.y / incl);
            d = rot(-t * 0.05) * d;
            let r = length(d);
            let a = atan2(d.y, d.x);

            // Two arms from a log spiral, as smooth glowing bands with a dust lane.
            let phase = 2.0 * (a + 2.4 * log(r + 0.05));
            let arm = pow(0.5 + 0.5 * cos(phase), 2.0);
            let dust = pow(0.5 + 0.5 * cos(phase + 0.8), 2.5);
            let disk = exp(-r * 2.0);

            let warm = vec3f(1.0, 0.80, 0.48);
            let gold = vec3f(1.0, 0.93, 0.78);
            let blue = vec3f(0.52, 0.70, 1.0);

            // The core shines: a white-hot center fades through gold into a warm
            // glow that floods the whole bulge.
            let core = exp(-r * 36.0) * 5.0 + exp(-r * 12.0) * 2.6 + exp(-r * 5.5) * 1.3;
            col = col + mix(warm, gold, smoothstep(0.0, 1.5, core)) * core;
            col = col + warm * exp(-r * 2.6) * 0.35;

            // Soft glowing nebulae: smooth colored gas, brightest along the arms.
            // A wide smoothstep (not a hard cut) keeps the clouds soft, not blotchy.
            let cloud = fbm(d * 3.0 + vec2f(t * 0.02, 0.0)) * fbm(d * 6.5 + vec2f(5.0, -t * 0.015));
            let neb = smoothstep(0.04, 0.4, cloud) * disk * (0.3 + 0.8 * arm) * (1.0 - 0.5 * dust);
            let hue = fbm(d * 1.7 + vec2f(40.0));
            var nebCol = mix(vec3f(0.95, 0.28, 0.5), vec3f(0.3, 0.5, 1.0), hue);
            nebCol = mix(nebCol, vec3f(0.2, 0.85, 0.7), smoothstep(0.55, 0.95, fbm(d * 2.3 + vec2f(7.0))) * 0.6);
            col = col + nebCol * neb * 0.7;

            // Smooth disk starlight from the arms, cooling outward, carved by dust.
            let diskCol = mix(gold, blue, smoothstep(0.05, 0.6, r));
            col = col + diskCol * disk * (0.06 + 0.7 * arm) * (1.0 - 0.6 * dust) * 0.9;

            // Millions of star systems: several dense, fine star layers packed into
            // the disk and arms. Round points, so they read as stars, never as camo.
            let stars =
                  starField(d * 42.0, t, 0.42, 0.05)
                + starField(d * 80.0, t, 0.42, 0.034) * 0.85
                + starField(d * 150.0, t, 0.46, 0.022) * 0.65;
            col = col + stars * disk * (0.5 + 0.7 * arm) * (1.0 - 0.5 * dust) * 1.7;

            // Tone map, a light saturation lift, gamma, and a vignette.
            col = col / (col + vec3f(0.72));
            let luma = dot(col, vec3f(0.299, 0.587, 0.114));
            col = clamp(mix(vec3f(luma), col, 1.18), vec3f(0.0), vec3f(1.0));
            col = pow(col, vec3f(0.88));
            let g = fragCoord.xy / res;
            col = col * pow(16.0 * g.x * g.y * (1.0 - g.x) * (1.0 - g.y), 0.13);

            return vec4f(col, 1.0);
        }
        """;

    public GalaxyRenderer(FrameLoop loop)
    {
        _gpu = loop.Gpu;
        _wgpu = _gpu.Api;
        _colorFormat = loop.Surface.Format;
        CreateResources();
    }

    /// <summary>Tilt the view from a pointer position, in logical pixels.</summary>
    public void SetMouse(float x, float y)
    {
        _mouseX = x;
        _mouseY = y;
    }

    // Draw into the pass, which arrives already started and cleared.
    public void Draw(in Frame frame, bool animate)
    {
        if (animate)
        {
            _time += (float)frame.Info.DeltaSeconds;
        }

        var w = frame.Info.LogicalWidth <= 0f ? 1f : frame.Info.LogicalWidth;
        var h = frame.Info.LogicalHeight <= 0f ? 1f : frame.Info.LogicalHeight;
        var uniforms = new Uniforms
        {
            ResX = frame.Info.PixelWidth,
            ResY = frame.Info.PixelHeight,
            Time = _time,
            MouseX = _mouseX < 0f ? 0.5f : Math.Clamp(_mouseX / w, 0f, 1f),
            MouseY = _mouseY < 0f ? 0.5f : Math.Clamp(_mouseY / h, 0f, 1f),
        };
        _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _uniformBuffer, 0, &uniforms, (nuint)sizeof(Uniforms));

        _wgpu.RenderPassEncoderSetPipeline(frame.Pass, _pipeline);
        _wgpu.RenderPassEncoderSetBindGroup(frame.Pass, 0, _bindGroup, 0, (uint*)null);
        _wgpu.RenderPassEncoderDraw(frame.Pass, 3, 1, 0, 0);
    }

    private void CreateResources()
    {
        var uniformDesc = new BufferDescriptor
        {
            Size = (ulong)sizeof(Uniforms),
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
        };
        _uniformBuffer = _wgpu.DeviceCreateBuffer(_gpu.DeviceHandle, in uniformDesc);

        CreatePipeline();
        CreateBindGroup();
    }

    private void CreatePipeline()
    {
        _shader = _gpu.CreateShaderModuleWgsl(ShaderWgsl);

        var layoutEntries = stackalloc BindGroupLayoutEntry[1];
        layoutEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform },
        };
        var bglDesc = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = layoutEntries };
        _bindGroupLayout = _wgpu.DeviceCreateBindGroupLayout(_gpu.DeviceHandle, in bglDesc);

        var layouts = stackalloc BindGroupLayout*[1];
        layouts[0] = _bindGroupLayout;
        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = layouts,
        };
        _pipelineLayout = _wgpu.DeviceCreatePipelineLayout(_gpu.DeviceHandle, in pipelineLayoutDesc);

        // No vertex buffers: the shader builds a full-screen triangle from the
        // vertex index. The filler supplies the color target and primitive.
        _pipeline = _gpu.CreatePipeline(new PipelineOptions
        {
            Shader = _shader,
            ColorFormat = _colorFormat,
            Layout = _pipelineLayout,
        });
    }

    private void CreateBindGroup()
    {
        Span<BindGroupEntry> entries =
        [
            new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Offset = 0, Size = (ulong)sizeof(Uniforms) },
        ];
        _bindGroup = _gpu.CreateBindGroup(_bindGroupLayout, entries);
    }

    // Release only the resources this renderer created.
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

        if (_bindGroup != null)
        {
            _wgpu.BindGroupRelease(_bindGroup);
        }

        if (_pipelineLayout != null)
        {
            _wgpu.PipelineLayoutRelease(_pipelineLayout);
        }

        if (_bindGroupLayout != null)
        {
            _wgpu.BindGroupLayoutRelease(_bindGroupLayout);
        }

        if (_uniformBuffer != null)
        {
            _wgpu.BufferRelease(_uniformBuffer);
        }
    }
}
