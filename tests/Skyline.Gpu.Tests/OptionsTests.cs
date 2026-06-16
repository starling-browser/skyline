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
        var o = new GpuContextOptions();
        Assert.AreEqual(PowerPreference.HighPerformance, o.PowerPreference);
        Assert.IsTrue(o.LogErrors);
    }

    [TestMethod]
    public unsafe void PipelineOptionsDefaults()
    {
        var o = new PipelineOptions { Shader = null, ColorFormat = TextureFormat.Rgba8Unorm };
        Assert.AreEqual("vs_main", o.VertexEntry);
        Assert.AreEqual("fs_main", o.FragmentEntry);
        Assert.AreEqual(0, o.VertexBuffers.Length);
        Assert.IsFalse(o.Blend.HasValue);
        Assert.AreEqual(PrimitiveTopology.TriangleList, o.Topology);
        Assert.AreEqual(FrontFace.Ccw, o.FrontFace);
        Assert.AreEqual(CullMode.None, o.CullMode);
        Assert.AreEqual(1u, o.SampleCount);
        Assert.AreEqual(ColorWriteMask.All, o.WriteMask);
        Assert.IsNull(o.Label);
        Assert.IsTrue(o.Layout == null);
    }
}
