// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Skyline.Gpu;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace StarlingMock;

/// <summary>
/// The mock Starling page: a column of offscreen strip textures rendered by
/// a generative shader, composited onto the swapchain at the scroll offset.
/// This mirrors the Starling plan's shape — content rendered to tiles,
/// tiles composited, pixels never leaving the GPU. "Navigation" reseeds
/// the generator.
/// </summary>
internal sealed unsafe class PageRenderer : IDisposable
{
    private const int StripPixels = 512;     // strip height; width follows the view
    private const int StripCount = 24;       // page length: StripCount * StripPixels
    private const float LinesPerStrip = 8f;

    private const string PageShaderWgsl = """
        struct PageParams {
            hue: f32,
            strip: f32,
            time: f32,
            lines_per_strip: f32,
            size: vec2f,
        };
        @group(0) @binding(0) var<uniform> page: PageParams;

        @vertex
        fn vs_main(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
            var tri = array<vec2f, 3>(vec2f(-1.0, -3.0), vec2f(-1.0, 1.0), vec2f(3.0, 1.0));
            return vec4f(tri[i], 0.0, 1.0);
        }

        fn hash(n: f32) -> f32 {
            return fract(sin(n * 12.9898 + 78.233) * 43758.5453);
        }

        fn rect(p: vec2f, x0: f32, x1: f32, y0: f32, y1: f32) -> f32 {
            return step(x0, p.x) * step(p.x, x1) * step(y0, p.y) * step(p.y, y1);
        }

        @fragment
        fn fs_main(@builtin(position) frag: vec4f) -> @location(0) vec4f {
            let uv = frag.xy / page.size;
            let angle = page.hue * 6.2832;
            let tint = 0.5 + 0.5 * vec3f(sin(angle), sin(angle + 2.094), sin(angle + 4.189));
            var color = mix(vec3f(0.98), tint, 0.07);

            // One line index that continues across strips, so the page reads
            // as one document.
            let line = floor(uv.y * page.lines_per_strip) + page.strip * page.lines_per_strip;
            let fy = fract(uv.y * page.lines_per_strip);

            let kind = hash(line * 7.31 + page.hue * 113.0);
            let isImage = step(0.86, kind);
            let isHeading = step(0.72, kind) * (1.0 - isImage);
            let width = 0.30 + 0.62 * hash(line * 3.7 + page.hue * 57.0);

            let body = rect(vec2f(uv.x, fy), 0.07, 0.07 + width * 0.86, 0.38, 0.58);
            color = mix(color, vec3f(0.58), body * (1.0 - isHeading) * (1.0 - isImage));

            let heading = rect(vec2f(uv.x, fy), 0.07, 0.07 + width * 0.55, 0.30, 0.64);
            color = mix(color, vec3f(0.20), heading * isHeading);

            let image = rect(vec2f(uv.x, fy), 0.07, 0.93, 0.10, 0.90);
            color = mix(color, mix(tint, vec3f(0.80), 0.35), image * isImage);

            // A slow glow so the surface visibly re-renders every frame.
            color += vec3f(0.03 * sin(page.time * 1.5 + (uv.y + page.strip) * 2.0));
            return vec4f(color, 1.0);
        }
        """;

    private const string CompositeShaderWgsl = """
        struct StripRect {
            origin: vec2f,   // top-left in clip space
            size: vec2f,     // extent in clip space
        };
        @group(0) @binding(0) var<uniform> strip: StripRect;
        @group(0) @binding(1) var page_tex: texture_2d<f32>;
        @group(0) @binding(2) var page_samp: sampler;

        struct VsOut {
            @builtin(position) pos: vec4f,
            @location(0) uv: vec2f,
        };

        @vertex
        fn vs_main(@builtin(vertex_index) i: u32) -> VsOut {
            var corners = array<vec2f, 6>(
                vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(0.0, 1.0),
                vec2f(0.0, 1.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0));
            let c = corners[i];
            var outv: VsOut;
            outv.pos = vec4f(
                strip.origin.x + c.x * strip.size.x,
                strip.origin.y - c.y * strip.size.y,
                0.0, 1.0);
            outv.uv = c;
            return outv;
        }

        @fragment
        fn fs_main(in: VsOut) -> @location(0) vec4f {
            return textureSample(page_tex, page_samp, in.uv);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct PageParams
    {
        public float Hue, Strip, Time, LinesPerStrip;
        public float SizeX, SizeY, Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StripRect
    {
        public float X, Y, W, H;
    }

    private readonly GpuContext _gpu;
    private readonly WebGPU _wgpu;
    private readonly ShaderModule* _pageShader;
    private readonly ShaderModule* _compositeShader;
    private readonly BindGroupLayout* _pageGroupLayout;
    private readonly BindGroupLayout* _compositeGroupLayout;
    private readonly PipelineLayout* _pageLayout;
    private readonly PipelineLayout* _compositeLayout;
    private readonly RenderPipeline* _pagePipeline;
    private readonly RenderPipeline* _compositePipeline;
    private readonly Sampler* _sampler;

    private readonly WgpuBuffer*[] _paramBuffers = new WgpuBuffer*[StripCount];
    private readonly WgpuBuffer*[] _rectBuffers = new WgpuBuffer*[StripCount];
    private readonly BindGroup*[] _pageGroups = new BindGroup*[StripCount];
    private readonly Texture*[] _textures = new Texture*[StripCount];
    private readonly TextureView*[] _views = new TextureView*[StripCount];
    private readonly BindGroup*[] _compositeGroups = new BindGroup*[StripCount];

    private int _width;
    private int _height;
    private float _hue;
    private float _scroll;
    private double _time;

    public float PageHeight => StripCount * StripPixels;
    public float ViewportHeight => _height;

    public PageRenderer(nint metalLayer)
    {
        var main = NativeLibrary.GetMainProgramHandle();
        var wgpu = new WebGPU(new LamdaNativeContext(
            (string proc, out nint pfn) => NativeLibrary.TryGetExport(main, proc, out pfn)));
        _gpu = GpuContext.Create(wgpu, (api, instance) =>
        {
            var metal = new SurfaceDescriptorFromMetalLayer
            {
                Chain = new ChainedStruct(null, SType.SurfaceDescriptorFromMetalLayer),
                Layer = (void*)metalLayer,
            };
            var desc = new SurfaceDescriptor { NextInChain = &metal.Chain };
            return api.InstanceCreateSurface(instance, in desc);
        });
        _wgpu = _gpu.Api;
        var device = _gpu.DeviceHandle;
        var format = _gpu.Surface!.Format;

        _pageShader = _gpu.CreateShaderModuleWgsl(PageShaderWgsl, "page");
        _compositeShader = _gpu.CreateShaderModuleWgsl(CompositeShaderWgsl, "composite");

        var pageEntry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform },
        };
        var pageGroupDesc = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &pageEntry };
        _pageGroupLayout = _wgpu.DeviceCreateBindGroupLayout(device, in pageGroupDesc);

        var compositeEntries = stackalloc BindGroupLayoutEntry[3];
        compositeEntries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform },
        };
        compositeEntries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
            },
        };
        compositeEntries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        var compositeGroupDesc = new BindGroupLayoutDescriptor { EntryCount = 3, Entries = compositeEntries };
        _compositeGroupLayout = _wgpu.DeviceCreateBindGroupLayout(device, in compositeGroupDesc);

        var pageGroupLayout = _pageGroupLayout;
        var pageLayoutDesc = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &pageGroupLayout };
        _pageLayout = _wgpu.DeviceCreatePipelineLayout(device, in pageLayoutDesc);
        var compositeGroupLayout = _compositeGroupLayout;
        var compositeLayoutDesc = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &compositeGroupLayout };
        _compositeLayout = _wgpu.DeviceCreatePipelineLayout(device, in compositeLayoutDesc);

        _pagePipeline = _gpu.CreatePipeline(new PipelineOptions
        {
            Shader = _pageShader,
            ColorFormat = format,
            Layout = _pageLayout,
            Label = "page",
        });
        _compositePipeline = _gpu.CreatePipeline(new PipelineOptions
        {
            Shader = _compositeShader,
            ColorFormat = format,
            Layout = _compositeLayout,
            Label = "composite",
        });

        var samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0f,
            LodMaxClamp = 32f,
            MaxAnisotropy = 1,
        };
        _sampler = _wgpu.DeviceCreateSampler(device, in samplerDesc);

        var bufferDesc = new BufferDescriptor
        {
            Size = 32,
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
        };
        Span<BindGroupEntry> pageGroupEntries = stackalloc BindGroupEntry[1];
        for (var i = 0; i < StripCount; i++)
        {
            _paramBuffers[i] = _wgpu.DeviceCreateBuffer(device, in bufferDesc);
            _rectBuffers[i] = _wgpu.DeviceCreateBuffer(device, in bufferDesc);
            pageGroupEntries[0] = new BindGroupEntry { Binding = 0, Buffer = _paramBuffers[i], Size = 32 };
            _pageGroups[i] = _gpu.CreateBindGroup(_pageGroupLayout, pageGroupEntries);
        }
    }

    public void Navigate(string url)
    {
        // FNV-1a, folded to a hue. Same address, same page.
        var h = 2166136261u;
        foreach (var c in url)
        {
            h = (h ^ c) * 16777619u;
        }
        _hue = (h % 1024u) / 1024f;
        _scroll = 0f;
    }

    public void ScrollBy(float pixels)
    {
        _scroll = Math.Clamp(_scroll + pixels, 0f, Math.Max(0f, PageHeight - _height));
    }

    public void Resize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }
        if (_gpu.Surface!.PixelSize != (pixelWidth, pixelHeight))
        {
            _gpu.Surface.Configure(pixelWidth, pixelHeight);
        }
        _height = pixelHeight;
        if (_width == pixelWidth)
        {
            return;
        }
        _width = pixelWidth;
        Span<BindGroupEntry> compositeGroupEntries = stackalloc BindGroupEntry[3];
        for (var i = 0; i < StripCount; i++)
        {
            ReleaseStrip(i);
            _textures[i] = _gpu.CreateColorTarget(_width, StripPixels, _gpu.Surface.Format, label: "strip");
            _views[i] = _wgpu.TextureCreateView(_textures[i], null);
            compositeGroupEntries[0] = new BindGroupEntry { Binding = 0, Buffer = _rectBuffers[i], Size = 32 };
            compositeGroupEntries[1] = new BindGroupEntry { Binding = 1, TextureView = _views[i] };
            compositeGroupEntries[2] = new BindGroupEntry { Binding = 2, Sampler = _sampler };
            _compositeGroups[i] = _gpu.CreateBindGroup(_compositeGroupLayout, compositeGroupEntries);
        }
    }

    public void RenderFrame(double deltaSeconds)
    {
        var surface = _gpu.Surface!;
        if (_width == 0 || !surface.TryAcquireFrame())
        {
            return;
        }
        _time += deltaSeconds;

        var firstStrip = Math.Clamp((int)(_scroll / StripPixels), 0, StripCount - 1);
        var lastStrip = Math.Clamp((int)((_scroll + _height) / StripPixels), 0, StripCount - 1);

        for (var i = firstStrip; i <= lastStrip; i++)
        {
            var p = new PageParams
            {
                Hue = _hue,
                Strip = i,
                Time = (float)_time,
                LinesPerStrip = LinesPerStrip,
                SizeX = _width,
                SizeY = StripPixels,
            };
            _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _paramBuffers[i], 0, &p, (nuint)sizeof(PageParams));

            var top = 1f - 2f * (i * StripPixels - _scroll) / _height;
            var r = new StripRect { X = -1f, Y = top, W = 2f, H = 2f * StripPixels / _height };
            _wgpu.QueueWriteBuffer(_gpu.QueueHandle, _rectBuffers[i], 0, &r, (nuint)sizeof(StripRect));
        }

        var encoder = _wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);

        for (var i = firstStrip; i <= lastStrip; i++)
        {
            var stripAttachment = new RenderPassColorAttachment
            {
                View = _views[i],
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 1, G = 1, B = 1, A = 1 },
            };
            var stripPassDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &stripAttachment };
            var stripPass = _wgpu.CommandEncoderBeginRenderPass(encoder, in stripPassDesc);
            _wgpu.RenderPassEncoderSetPipeline(stripPass, _pagePipeline);
            _wgpu.RenderPassEncoderSetBindGroup(stripPass, 0, _pageGroups[i], 0, null);
            _wgpu.RenderPassEncoderDraw(stripPass, 3, 1, 0, 0);
            _wgpu.RenderPassEncoderEnd(stripPass);
            _wgpu.RenderPassEncoderRelease(stripPass);
        }

        var attachment = new RenderPassColorAttachment
        {
            View = surface.CurrentView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.07, G = 0.07, B = 0.09, A = 1 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
        var pass = _wgpu.CommandEncoderBeginRenderPass(encoder, in passDesc);
        _wgpu.RenderPassEncoderSetPipeline(pass, _compositePipeline);
        for (var i = firstStrip; i <= lastStrip; i++)
        {
            _wgpu.RenderPassEncoderSetBindGroup(pass, 0, _compositeGroups[i], 0, null);
            _wgpu.RenderPassEncoderDraw(pass, 6, 1, 0, 0);
        }
        _wgpu.RenderPassEncoderEnd(pass);
        _wgpu.RenderPassEncoderRelease(pass);

        var command = _wgpu.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
        _wgpu.QueueSubmit(_gpu.QueueHandle, 1, &command);
        _wgpu.CommandBufferRelease(command);
        _wgpu.CommandEncoderRelease(encoder);
        surface.Present();
    }

    private void ReleaseStrip(int i)
    {
        if (_compositeGroups[i] != null)
        {
            _wgpu.BindGroupRelease(_compositeGroups[i]);
            _compositeGroups[i] = null;
        }
        if (_views[i] != null)
        {
            _wgpu.TextureViewRelease(_views[i]);
            _views[i] = null;
        }
        if (_textures[i] != null)
        {
            _wgpu.TextureRelease(_textures[i]);
            _textures[i] = null;
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < StripCount; i++)
        {
            ReleaseStrip(i);
            if (_pageGroups[i] != null)
            {
                _wgpu.BindGroupRelease(_pageGroups[i]);
            }
            if (_paramBuffers[i] != null)
            {
                _wgpu.BufferRelease(_paramBuffers[i]);
            }
            if (_rectBuffers[i] != null)
            {
                _wgpu.BufferRelease(_rectBuffers[i]);
            }
        }
        _wgpu.SamplerRelease(_sampler);
        _wgpu.RenderPipelineRelease(_compositePipeline);
        _wgpu.RenderPipelineRelease(_pagePipeline);
        _wgpu.PipelineLayoutRelease(_compositeLayout);
        _wgpu.PipelineLayoutRelease(_pageLayout);
        _wgpu.BindGroupLayoutRelease(_compositeGroupLayout);
        _wgpu.BindGroupLayoutRelease(_pageGroupLayout);
        _wgpu.ShaderModuleRelease(_compositeShader);
        _wgpu.ShaderModuleRelease(_pageShader);
        _gpu.Dispose();
    }
}
