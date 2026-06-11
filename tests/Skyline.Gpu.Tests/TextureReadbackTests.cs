using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public unsafe class TextureReadbackTests
{
    [TestMethod]
    public void ClearColorRoundTripsThroughReadback()
    {
        using var gpu = GpuContext.CreateHeadless();
        var wgpu = gpu.Api;

        // 100 px wide so each row (400 bytes) needs padding to wgpu's
        // 256-byte alignment — exercising the row-repack path.
        const int w = 100;
        const int h = 50;
        var desc = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Format = TextureFormat.Bgra8Unorm,
            Size = new Extent3D { Width = w, Height = h, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
        };
        var texture = wgpu.DeviceCreateTexture(gpu.DeviceHandle, in desc);
        var view = wgpu.TextureCreateView(texture, (TextureViewDescriptor*)null);

        using var readback = new TextureReadback(gpu, w, h);
        var att = new RenderPassColorAttachment
        {
            View = view,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0.25, G = 0.5, B = 0.75, A = 1.0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
        var enc = wgpu.DeviceCreateCommandEncoder(gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var pass = wgpu.CommandEncoderBeginRenderPass(enc, in passDesc);
        wgpu.RenderPassEncoderEnd(pass);
        wgpu.RenderPassEncoderRelease(pass);
        readback.Encode(enc, texture);
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(gpu.QueueHandle, 1, &cmd);
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);

        var pixels = readback.Resolve();

        Assert.AreEqual(w * h * 4, pixels.Length);
        // BGRA byte order: B=0.75, G=0.5, R=0.25.
        AssertPixel(pixels, x: 0, y: 0, w);
        AssertPixel(pixels, x: w - 1, y: h - 1, w);
        AssertPixel(pixels, x: w / 2, y: h / 2, w);

        wgpu.TextureViewRelease(view);
        wgpu.TextureRelease(texture);
    }

    private static void AssertPixel(byte[] pixels, int x, int y, int w)
    {
        var o = (y * w + x) * 4;
        AssertChannel(191, pixels[o]);     // B
        AssertChannel(128, pixels[o + 1]); // G
        AssertChannel(64, pixels[o + 2]);  // R
        Assert.AreEqual(255, pixels[o + 3]);
    }

    private static void AssertChannel(int expected, byte actual) =>
        Assert.IsTrue(Math.Abs(expected - actual) <= 1, $"expected ~{expected}, got {actual}");

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        using var gpu = GpuContext.CreateHeadless();
        var readback = new TextureReadback(gpu, 4, 4);
        readback.Dispose();
        readback.Dispose();
    }
}
