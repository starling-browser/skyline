using Skyline;

namespace Skyline.Tests;

[TestClass]
public class FrameInfoTests
{
    [TestMethod]
    public void LogicalSizeDividesByDpr()
    {
        var f = new FrameInfo(2560, 1440, 2f, 0.016);
        Assert.AreEqual(1280f, f.LogicalWidth);
        Assert.AreEqual(720f, f.LogicalHeight);
    }

    [TestMethod]
    public void DprOfOneKeepsPixelSize()
    {
        var f = new FrameInfo(800, 600, 1f, 0);
        Assert.AreEqual(800f, f.LogicalWidth);
        Assert.AreEqual(600f, f.LogicalHeight);
    }

    [TestMethod]
    public void ExposesDeltaAndDeconstructs()
    {
        var f = new FrameInfo(1, 2, 1f, 0.25);
        Assert.AreEqual(0.25, f.DeltaSeconds);
        var (w, h, dpr, delta) = f;
        Assert.AreEqual(1, w);
        Assert.AreEqual(2, h);
        Assert.AreEqual(1f, dpr);
        Assert.AreEqual(0.25, delta);
    }

    [TestMethod]
    public void IsValueEquatable()
    {
        var a = new FrameInfo(10, 20, 1.5f, 0.5);
        var b = new FrameInfo(10, 20, 1.5f, 0.5);
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, b with { PixelWidth = 11 });
    }
}
