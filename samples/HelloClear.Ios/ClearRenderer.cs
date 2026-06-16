// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace HelloClear;

/// <summary>
/// The Skyline.Gpu chain on an iOS CAMetalLayer, plus a per-frame animated
/// clear. wgpu-native is statically linked into the app executable, so the
/// api object resolves symbols from the executable instead of a dylib —
/// <c>WebGPU.GetApi()</c> has no library to load here.
/// </summary>
internal sealed unsafe class ClearRenderer : IDisposable
{
    private readonly GpuContext _gpu;
    private double _time;

    public ClearRenderer(nint metalLayer)
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
    }

    public void Resize(int pixelWidth, int pixelHeight)
    {
        var surface = _gpu.Surface!;
        if (surface.PixelSize != (pixelWidth, pixelHeight))
        {
            surface.Configure(pixelWidth, pixelHeight);
        }
    }

    public void RenderFrame()
    {
        var surface = _gpu.Surface!;
        if (!surface.TryAcquireFrame())
        {
            return;
        }
        _time += 1.0 / 60.0;
        var wgpu = _gpu.Api;
        var attachment = new RenderPassColorAttachment
        {
            View = surface.CurrentView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color
            {
                R = 0.5 + 0.5 * Math.Sin(_time * 0.7),
                G = 0.5 + 0.5 * Math.Sin(_time * 0.7 + 2.1),
                B = 0.5 + 0.5 * Math.Sin(_time * 0.7 + 4.2),
                A = 1.0,
            },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
        var encoder = wgpu.DeviceCreateCommandEncoder(_gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = wgpu.CommandEncoderBeginRenderPass(encoder, in passDesc);
        wgpu.RenderPassEncoderEnd(pass);
        wgpu.RenderPassEncoderRelease(pass);
        var command = wgpu.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(_gpu.QueueHandle, 1, &command);
        wgpu.CommandBufferRelease(command);
        wgpu.CommandEncoderRelease(encoder);
        surface.Present();
    }

    public void Dispose() => _gpu.Dispose();
}
