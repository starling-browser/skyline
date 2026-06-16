using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public unsafe class ErrorLoggingTests
{
    [TestMethod]
    public void UnsubscribedErrorLogsToStderr()
    {
        using var gpu = GpuContext.CreateHeadless();
        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);
            gpu.RaiseUncapturedError(ErrorType.Validation, "boom");
        }
        finally
        {
            Console.SetError(original);
        }
        StringAssert.Contains(sw.ToString(), "wgpu error (Validation): boom");
    }

    [TestMethod]
    public void SubscribedErrorDoesNotLog()
    {
        using var gpu = GpuContext.CreateHeadless();
        ErrorType? receivedType = null;
        string? receivedMessage = null;
        gpu.UncapturedError += (type, msg) => { receivedType = type; receivedMessage = msg; };

        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);
            gpu.RaiseUncapturedError(ErrorType.Validation, "handled");
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.AreEqual(ErrorType.Validation, receivedType);
        Assert.AreEqual("handled", receivedMessage);
        Assert.AreEqual(string.Empty, sw.ToString());
    }

    [TestMethod]
    public void LogErrorsFalseSilencesErrors()
    {
        using var gpu = GpuContext.CreateHeadless(new GpuContextOptions { LogErrors = false });
        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);
            gpu.RaiseUncapturedError(ErrorType.Validation, "silent");
        }
        finally
        {
            Console.SetError(original);
        }
        Assert.AreEqual(string.Empty, sw.ToString());
    }

    [TestMethod]
    public void UnsubscribedDeviceLostLogs()
    {
        using var gpu = GpuContext.CreateHeadless();
        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);
            gpu.RaiseDeviceLost(DeviceLostReason.Unknown, "gone");
        }
        finally
        {
            Console.SetError(original);
        }
        StringAssert.Contains(sw.ToString(), "wgpu device lost (Unknown): gone");
    }

    [TestMethod]
    public void DestroyedDeviceLostIsNeverLogged()
    {
        using var gpu = GpuContext.CreateHeadless();
        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);
            gpu.RaiseDeviceLost(DeviceLostReason.Destroyed, "teardown");
        }
        finally
        {
            Console.SetError(original);
        }
        Assert.AreEqual(string.Empty, sw.ToString());
    }

    [TestMethod]
    public void SubscribedDeviceLostDoesNotLog()
    {
        using var gpu = GpuContext.CreateHeadless();
        DeviceLostReason? receivedReason = null;
        string? receivedMessage = null;
        gpu.DeviceLost += (reason, msg) => { receivedReason = reason; receivedMessage = msg; };

        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);
            gpu.RaiseDeviceLost(DeviceLostReason.Unknown, "gone");
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.AreEqual(DeviceLostReason.Unknown, receivedReason);
        Assert.AreEqual("gone", receivedMessage);
        Assert.AreEqual(string.Empty, sw.ToString());
    }

    [TestMethod]
    public void NativeValidationErrorLogsWhenUnsubscribed()
    {
        using var gpu = GpuContext.CreateHeadless();
        var sw = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(sw);

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
            if (tex != null)
            {
                gpu.Api.TextureRelease(tex);
            }

            gpu.Poll(wait: false);
        }
        finally
        {
            Console.SetError(original);
        }

        // Require the Validation error specifically, so the assertion can only
        // pass when the invalid SampleCount actually drove the native
        // uncaptured-error callback to stderr — not from unrelated noise.
        StringAssert.Contains(sw.ToString(), "wgpu error (Validation)");
    }
}
