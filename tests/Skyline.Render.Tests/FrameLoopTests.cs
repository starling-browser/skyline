using Silk.NET.WebGPU;
using Skyline;
using Skyline.Gpu;

namespace Skyline.Render.Tests;

/// <summary>
/// Drives <see cref="FrameLoop.DriveFrame"/> headlessly through the
/// surface-less test seam: a fabricated render-target view stands in for a
/// swapchain, a counting delegate stands in for present, and
/// <see cref="TextureReadback"/> proves what landed. This covers the frame
/// ritual without a window. Attach, Over, and the window wiring are covered
/// by the windowed harness.
/// </summary>
[TestClass]
public unsafe class FrameLoopTests
{
    private const int W = 32;
    private const int H = 32;

    [TestMethod]
    public void DriveFrame_NotAcquired_SkipsWithoutPresenting()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());

        var presented = 0;
        var rendered = loop.DriveFrame(default, acquired: false, view: null, present: () => presented++, cancel: () => { });

        Assert.IsFalse(rendered);
        Assert.AreEqual(0, presented);
    }

    [TestMethod]
    public void DriveFrame_ClearPass_ClearsTargetToColor()
    {
        using var gpu = GpuContext.CreateHeadless();
        gpu.UncapturedError += (t, m) => Assert.Fail($"wgpu error ({t}): {m}");
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions
        {
            ClearColor = new Color { R = 0.25, G = 0.5, B = 0.75, A = 1.0 },
        });

        var target = gpu.CreateColorTarget(W, H, TextureFormat.Rgba8Unorm, TextureUsage.CopySrc);
        var view = gpu.Api.TextureCreateView(target, (TextureViewDescriptor*)null);

        var presented = 0;
        Assert.IsTrue(loop.DriveFrame(Info(), acquired: true, view, () => presented++, () => { }));
        Assert.AreEqual(1, presented);

        var px = ReadBack(gpu, target);
        AssertCenter(px, 0, 64);   // R 0.25
        AssertCenter(px, 1, 128);  // G 0.5
        AssertCenter(px, 2, 191);  // B 0.75

        gpu.Api.TextureViewRelease(view);
        gpu.Api.TextureRelease(target);
    }

    [TestMethod]
    public void DriveFrame_NoClearPass_OnRenderOwnsThePass()
    {
        using var gpu = GpuContext.CreateHeadless();
        gpu.UncapturedError += (t, m) => Assert.Fail($"wgpu error ({t}): {m}");
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions { BeginClearPass = false });

        var target = gpu.CreateColorTarget(W, H, TextureFormat.Rgba8Unorm, TextureUsage.CopySrc);
        var view = gpu.Api.TextureCreateView(target, (TextureViewDescriptor*)null);

        var sawNullPass = false;
        var sawHandles = false;
        var sawInfo = false;
        loop.OnRender = (in Frame f) =>
        {
            sawNullPass = f.Pass == null;
            sawHandles = f.Encoder != null && f.View != null;
            sawInfo = f.Info.PixelWidth == W && f.Info.PixelHeight == H;
            // Own the pass on the loop's live encoder — the compositing path.
            var att = new RenderPassColorAttachment
            {
                View = f.View,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0, G = 1, B = 0, A = 1 },
            };
            var pd = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &att };
            var p = gpu.Api.CommandEncoderBeginRenderPass(f.Encoder, in pd);
            gpu.Api.RenderPassEncoderEnd(p);
            gpu.Api.RenderPassEncoderRelease(p);
        };

        var presented = 0;
        Assert.IsTrue(loop.DriveFrame(Info(), acquired: true, view, () => presented++, () => { }));
        Assert.AreEqual(1, presented);
        Assert.IsTrue(sawNullPass, "Pass is null when BeginClearPass is false");
        Assert.IsTrue(sawHandles, "Encoder and View are exposed");
        Assert.IsTrue(sawInfo, "Info carries the frame geometry");

        var px = ReadBack(gpu, target);
        AssertCenter(px, 1, 255); // green from the app's own pass
        AssertCenter(px, 0, 0);

        gpu.Api.TextureViewRelease(view);
        gpu.Api.TextureRelease(target);
    }

    [TestMethod]
    public void DriveFrame_ClearPassWithOnRender_HandsOverAStartedPass()
    {
        using var gpu = GpuContext.CreateHeadless();
        gpu.UncapturedError += (t, m) => Assert.Fail($"wgpu error ({t}): {m}");
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions
        {
            ClearColor = new Color { R = 1, G = 0, B = 0, A = 1 },
        });

        var target = gpu.CreateColorTarget(W, H, TextureFormat.Rgba8Unorm, TextureUsage.CopySrc);
        var view = gpu.Api.TextureCreateView(target, (TextureViewDescriptor*)null);

        var sawStartedPass = false;
        loop.OnRender = (in Frame f) => sawStartedPass = f.Pass != null;

        Assert.IsTrue(loop.DriveFrame(Info(), acquired: true, view, () => { }, () => { }));
        Assert.IsTrue(sawStartedPass, "the clear pass is started and handed to OnRender");

        var px = ReadBack(gpu, target);
        AssertCenter(px, 0, 255); // red clear
        AssertCenter(px, 1, 0);

        gpu.Api.TextureViewRelease(view);
        gpu.Api.TextureRelease(target);
    }

    [TestMethod]
    public void DriveFrame_OnRenderThrows_CancelsTheFrameAndRethrows()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());

        var target = gpu.CreateColorTarget(W, H, TextureFormat.Rgba8Unorm, TextureUsage.CopySrc);
        var view = gpu.Api.TextureCreateView(target, (TextureViewDescriptor*)null);

        var presented = 0;
        var cancelled = 0;
        loop.OnRender = (in Frame f) => throw new InvalidOperationException("boom in draw");

        Assert.ThrowsException<InvalidOperationException>(() =>
            loop.DriveFrame(Info(), acquired: true, view, () => presented++, () => cancelled++));

        Assert.AreEqual(0, presented, "a thrown frame is never presented");
        Assert.AreEqual(1, cancelled, "a thrown frame is cancelled so the swapchain acquire does not leak");

        gpu.Api.TextureViewRelease(view);
        gpu.Api.TextureRelease(target);
    }

    [TestMethod]
    public void Surface_ThrowsWhenLoopHasNoSurface()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());
        Assert.ThrowsException<InvalidOperationException>(() => { _ = loop.Surface; });
    }

    [TestMethod]
    public void Outcome_WithoutFault_IsOkWithPresentedCount()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());
        // No draw faulted and this seam has no surface, so the run is Ok(0).
        Assert.IsTrue(loop.Outcome is Ok { Frames: 0 });
    }

    [TestMethod]
    public void GpuAndPacerAreExposed()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());
        Assert.AreSame(gpu, loop.Gpu);
        Assert.AreSame(pacer, loop.Pacer);
    }

    [TestMethod]
    public void RequestRedraw_WithoutWindow_DoesNotThrow()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());
        loop.RequestRedraw();
    }

    [TestMethod]
    public void ClearColor_IsLive()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 2);
        using var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions { ClearColor = new Color { R = 1 } });
        Assert.AreEqual(1.0, loop.ClearColor.R);
        loop.ClearColor = new Color { G = 1 };
        Assert.AreEqual(1.0, loop.ClearColor.G);
    }

    [TestMethod]
    public void Dispose_IsIdempotent_AndLeavesABorrowedContextAlive()
    {
        var gpu = GpuContext.CreateHeadless();
        var pacer = new FramePacer(gpu, 2);
        var loop = FrameLoop.CreateForTest(gpu, pacer, new FrameLoopOptions());
        loop.Dispose();
        loop.Dispose(); // idempotent
        // The loop borrowed the context and pacer, so both still work after it disposes.
        Assert.IsTrue(gpu.Poll(wait: false));
        pacer.Dispose();
        gpu.Dispose();
    }

    [TestMethod]
    public void OptionsDefaults()
    {
        var o = new FrameLoopOptions();
        Assert.AreEqual(0.0, o.ClearColor.R);
        Assert.AreEqual(1.0, o.ClearColor.A);
        Assert.IsTrue(o.BeginClearPass);
        Assert.IsFalse(o.Continuous);
        Assert.AreEqual(2, o.MaxFramesInFlight);
        Assert.IsNull(o.Gpu);
        Assert.IsNull(o.Surface);
    }

    private static FrameInfo Info() => new(W, H, 1f, 0.016);

    private static byte[] ReadBack(GpuContext gpu, Texture* target)
    {
        var wgpu = gpu.Api;
        using var readback = new TextureReadback(gpu, W, H);
        var enc = wgpu.DeviceCreateCommandEncoder(gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        readback.Encode(enc, target);
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(gpu.QueueHandle, 1, &cmd);
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);
        return readback.Resolve();
    }

    private static void AssertCenter(byte[] px, int channel, int expected)
    {
        var o = ((H / 2) * W + (W / 2)) * 4 + channel;
        Assert.IsTrue(Math.Abs(px[o] - expected) <= 2, $"channel {channel} = {px[o]}, expected ~{expected}");
    }
}
