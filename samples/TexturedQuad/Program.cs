using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;
using Skyline.Input;
using Skyline.Render;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

// A full-screen GPU visualization on top of Skyline's FrameLoop: a spiral galaxy,
// seen as a tilted disk. One fragment shader draws a blazing white core with a
// vertical light shaft, purple nebulosity, and spiral arms swirled out of dense
// white-lavender stars over a black, star-flecked sky, all turning slowly. No
// vertex buffer and no texture — a full-screen triangle from the vertex index
// feeds the shader, and a small uniform buffer carries resolution and time.
//
// Everything moves at a crawl on purpose: a slow turn reads as calm and never
// flashes. Space pauses, Escape quits. Pass --frames N to auto-close after N
// presented frames (smoke test).

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

var handler = new CallbackAppWindowHandler();
loop.Handler = handler;
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

    if (e.Key == Key.Space)
    {
        animate = !animate;
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

        // A field of round stars on a hashed grid: varied brightness, white-to-
        // lavender tint, slow twinkle. The threshold sets how many cells hold a star.
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
                    let twinkle = 0.88 + 0.12 * sin(t + h * TAU);
                    let tint = mix(vec3f(1.0, 1.0, 1.0), vec3f(0.74, 0.66, 1.0), hash21(cell + nb + vec2f(5.7, 2.1)));
                    c = c + tint * smoothstep(radius, 0.0, d) * (0.25 + mag) * twinkle;
                }
            }
            return c;
        }

        @fragment
        fn fs_main(@builtin(position) fragCoord: vec4f) -> @location(0) vec4f {
            let res = u.resolution;
            let uvc = (fragCoord.xy - 0.5 * res) / res.y;   // screen-centered
            let t = u.time;

            var col = vec3f(0.0);

            // Sparse background stars on black.
            col = col + starField(uvc * 7.0 + vec2f(31.0), t, 0.90, 0.075) * 0.9;
            col = col + starField(uvc * 16.0 + vec2f(7.0), t, 0.92, 0.05) * 0.7;

            // Galaxy disk: a fixed tilt opens it into an ellipse (no mouse), then it
            // spins slowly clockwise.
            let incl = 0.52;
            // Pull back so the whole disk and its faint outer arms sit framed in
            // dark space (a larger factor sees more of the galaxy).
            let oriented = rot(0.26) * uvc * 1.3;
            var d0 = vec2f(oriented.x, oriented.y / incl);
            d0 = rot(-t * 0.05) * d0;

            // Domain warp: nudge the coordinate by smooth noise so the arms swirl in
            // wispy filaments of stars instead of clean bands.
            let warp = vec2f(fbm(d0 * 2.2 + vec2f(0.0, t * 0.03)), fbm(d0 * 2.2 + vec2f(19.0, -t * 0.02))) - vec2f(0.5);
            let d = d0 + warp * 0.10;
            let r = length(d);
            let a = atan2(d.y, d.x);

            let phase = 2.0 * (a + 2.4 * log(r + 0.05));
            let arm = pow(0.5 + 0.5 * cos(phase), 1.6);
            let dust = pow(0.5 + 0.5 * cos(phase + 0.8), 2.5);
            let disk = exp(-r * 1.3);

            let lav = vec3f(0.62, 0.50, 0.90);   // lavender
            let pale = vec3f(0.86, 0.80, 1.0);   // pale violet-white

            // The core blazes white from a tight, bright, round center.
            let core = exp(-r * 34.0) * 6.0 + exp(-r * 13.0) * 2.2;
            col = col + mix(pale, vec3f(1.0), smoothstep(0.0, 1.2, core)) * core;
            col = col + lav * exp(-r * 3.2) * 0.16;   // small inner halo

            // Faint purple nebulosity, only along the arms so it never fogs the disk.
            let cloud = fbm(d * 3.5 + vec2f(t * 0.02, 0.0)) * fbm(d * 7.0 + vec2f(5.0, -t * 0.015));
            let neb = smoothstep(0.04, 0.45, cloud) * disk * arm * (1.0 - 0.4 * dust);
            col = col + mix(lav * 0.8, pale, arm) * neb * 0.6;
            // A soft luminous purple body under the arms, so the stars sit in glow.
            col = col + lav * disk * arm * 0.18;

            // The arms ARE the stars: dense, bright white-lavender points packed hard
            // into the spiral, thinning to almost nothing between the arms.
            let armDensity = 0.08 + 1.6 * pow(arm, 1.3);
            let stars =
                  starField(d * 100.0, t, 0.25, 0.12)
                + starField(d * 175.0, t, 0.30, 0.18) * 0.90
                + starField(d * 300.0, t, 0.38, 0.28) * 0.75
                + starField(d * 500.0, t, 0.46, 0.42) * 0.60;
            col = col + stars * disk * armDensity * (1.0 - 0.3 * dust) * 3.6;

            // Tone map (lets the core blow to white), gamma, gentle vignette.
            col = col / (col + vec3f(0.62));
            col = pow(col, vec3f(0.88));
            let g = fragCoord.xy / res;
            col = col * pow(16.0 * g.x * g.y * (1.0 - g.x) * (1.0 - g.y), 0.12);

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

    // Draw into the pass, which arrives already started and cleared.
    public void Draw(in Frame frame, bool animate)
    {
        if (animate)
        {
            _time += (float)frame.Info.DeltaSeconds;
        }

        var uniforms = new Uniforms
        {
            ResX = frame.Info.PixelWidth,
            ResY = frame.Info.PixelHeight,
            Time = _time,
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
