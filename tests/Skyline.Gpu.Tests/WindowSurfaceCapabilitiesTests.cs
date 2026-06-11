using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public class WindowSurfaceCapabilitiesTests
{
    private static WindowSurfaceCapabilities MacLike() => new(
        [TextureFormat.Bgra8Unorm, TextureFormat.Bgra8UnormSrgb],
        [PresentMode.Fifo, PresentMode.Immediate],
        [CompositeAlphaMode.Opaque]);

    [TestMethod]
    public void SupportsReportsHit()
    {
        Assert.IsTrue(MacLike().Supports(PresentMode.Fifo));
        Assert.IsTrue(MacLike().Supports(PresentMode.Immediate));
    }

    [TestMethod]
    public void SupportsReportsMiss()
    {
        Assert.IsFalse(MacLike().Supports(PresentMode.Mailbox));
        Assert.IsFalse(MacLike().Supports(PresentMode.FifoRelaxed));
    }

    [TestMethod]
    public void ChoosePresentModeTakesFirstSupported()
    {
        Assert.AreEqual(PresentMode.Immediate, MacLike().ChoosePresentMode(PresentMode.Mailbox, PresentMode.Immediate));
        Assert.AreEqual(PresentMode.Fifo, MacLike().ChoosePresentMode(PresentMode.Fifo, PresentMode.Immediate));
    }

    [TestMethod]
    public void ChoosePresentModeFallsBackToFifo()
    {
        Assert.AreEqual(PresentMode.Fifo, MacLike().ChoosePresentMode(PresentMode.Mailbox));
        Assert.AreEqual(PresentMode.Fifo, MacLike().ChoosePresentMode());
    }

    [TestMethod]
    public void SpansExposeEverything()
    {
        var caps = MacLike();
        Assert.AreEqual(2, caps.Formats.Length);
        Assert.AreEqual(2, caps.PresentModes.Length);
        Assert.AreEqual(1, caps.AlphaModes.Length);
        Assert.AreEqual(TextureFormat.Bgra8Unorm, caps.Formats[0]);
        Assert.AreEqual(CompositeAlphaMode.Opaque, caps.AlphaModes[0]);
    }
}
