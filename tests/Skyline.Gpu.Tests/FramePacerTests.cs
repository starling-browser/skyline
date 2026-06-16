using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public unsafe class FramePacerTests
{
    private static void SubmitEmpty(GpuContext gpu)
    {
        var wgpu = gpu.Api;
        var enc = wgpu.DeviceCreateCommandEncoder(gpu.DeviceHandle, (CommandEncoderDescriptor*)null);
        var cmd = wgpu.CommandEncoderFinish(enc, (CommandBufferDescriptor*)null);
        wgpu.QueueSubmit(gpu.QueueHandle, 1, &cmd);
        wgpu.CommandBufferRelease(cmd);
        wgpu.CommandEncoderRelease(enc);
    }

    [TestMethod]
    public void RejectsNonPositiveCap()
    {
        using var gpu = GpuContext.CreateHeadless();
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new FramePacer(gpu, 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new FramePacer(gpu, -1));
    }

    [TestMethod]
    public void ExposesCapAndStartsIdle()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, 3);
        Assert.AreEqual(3, pacer.MaxFramesInFlight);
        Assert.AreEqual(0, pacer.FramesInFlight);
        Assert.IsTrue(pacer.TryWait());
    }

    [TestMethod]
    public void WaitBlocksUntilTheGpuCatchesUp()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, maxFramesInFlight: 1);

        SubmitEmpty(gpu);
        pacer.FrameSubmitted();
        Assert.IsTrue(pacer.FramesInFlight is 0 or 1);

        // With the cap at 1, this can only return once the submitted
        // frame's completion callback has fired.
        pacer.Wait();
        Assert.AreEqual(0, pacer.FramesInFlight);
    }

    [TestMethod]
    public void TryWaitPumpsCompletions()
    {
        using var gpu = GpuContext.CreateHeadless();
        using var pacer = new FramePacer(gpu, maxFramesInFlight: 1);

        SubmitEmpty(gpu);
        pacer.FrameSubmitted();

        // Empty work completes quickly but not instantly. Retry against a
        // generous deadline instead of assuming GPU timing.
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var free = false;
        while (!free && deadline.ElapsedMilliseconds < 5000)
        {
            free = pacer.TryWait();
            if (!free)
            {
                Thread.Sleep(1);
            }
        }
        Assert.IsTrue(free);
    }

    [TestMethod]
    public void DisposeDrainsInFlightFrames()
    {
        using var gpu = GpuContext.CreateHeadless();
        var pacer = new FramePacer(gpu, maxFramesInFlight: 2);
        SubmitEmpty(gpu);
        pacer.FrameSubmitted();
        SubmitEmpty(gpu);
        pacer.FrameSubmitted();
        pacer.Dispose(); // must not hang or crash
        pacer.Dispose(); // idempotent
    }
}
