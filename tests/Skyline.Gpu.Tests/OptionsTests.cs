using Silk.NET.WebGPU;
using Skyline.Gpu;

namespace Skyline.Gpu.Tests;

[TestClass]
public class OptionsTests
{
    [TestMethod]
    public void WindowSurfaceOptionsDefaults()
    {
        var o = new WindowSurfaceOptions();
        Assert.AreEqual(TextureFormat.Bgra8Unorm, o.Format);
        Assert.AreEqual(TextureUsage.None, o.ExtraUsage);
        Assert.AreEqual(PresentMode.Fifo, o.PresentMode);
        Assert.AreEqual(CompositeAlphaMode.Auto, o.AlphaMode);
    }

    [TestMethod]
    public void GpuContextOptionsDefaults()
    {
        Assert.AreEqual(PowerPreference.HighPerformance, new GpuContextOptions().PowerPreference);
    }
}
