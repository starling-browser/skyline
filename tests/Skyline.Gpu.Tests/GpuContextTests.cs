using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public unsafe class GpuContextTests
{
    [TestMethod]
    public void CreateHeadlessBuildsTheChain()
    {
        using var gpu = GpuContext.CreateHeadless();
        Assert.IsTrue(gpu.InstanceHandle != null);
        Assert.IsTrue(gpu.AdapterHandle != null);
        Assert.IsTrue(gpu.DeviceHandle != null);
        Assert.IsTrue(gpu.QueueHandle != null);
        Assert.IsNotNull(gpu.Api);
        Assert.IsNull(gpu.Surface);
    }

    [TestMethod]
    public void HeadlessSupportsPollOnWgpuNative()
    {
        using var gpu = GpuContext.CreateHeadless();
        Assert.IsTrue(gpu.SupportsPoll);
        Assert.IsTrue(gpu.Poll(wait: false));
    }

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        var gpu = GpuContext.CreateHeadless();
        gpu.Dispose();
        gpu.Dispose();
    }

    [TestMethod]
    public void UncapturedErrorFiresOnInvalidResource()
    {
        using var gpu = GpuContext.CreateHeadless();
        var fired = false;
        string? message = null;
        gpu.UncapturedError += (type, msg) => { fired = true; message = msg; };

        // SampleCount 3 is invalid in WebGPU (only 1 and 4 exist), which
        // raises a validation error no error scope captures.
        var desc = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Format = TextureFormat.Bgra8Unorm,
            Size = new Extent3D { Width = 4, Height = 4, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 3,
            Usage = TextureUsage.RenderAttachment,
        };
        var tex = gpu.Api.DeviceCreateTexture(gpu.DeviceHandle, in desc);
        if (tex != null) gpu.Api.TextureRelease(tex);
        gpu.Poll(wait: false);

        Assert.IsTrue(fired, "invalid texture creation should raise an uncaptured error");
        Assert.IsFalse(string.IsNullOrEmpty(message));
    }

    [TestMethod]
    public void DeviceLostHandlerCanBeRegistered()
    {
        // wgpu-native only fires the lost callback for real device loss,
        // not for an orderly Dispose — so this asserts registration and
        // teardown are safe, not that the event fires.
        var gpu = GpuContext.CreateHeadless();
        gpu.DeviceLost += (_, _) => { };
        gpu.Dispose();
    }
}
