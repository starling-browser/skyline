using Skyline;

namespace Skyline.Tests;

[TestClass]
public class AppWindowOptionsTests
{
    [TestMethod]
    public void Defaults()
    {
        var o = new AppWindowOptions();
        Assert.AreEqual("Skyline", o.Title);
        Assert.AreEqual(800, o.Width);
        Assert.AreEqual(600, o.Height);
        Assert.IsTrue(o.Resizable);
    }

    [TestMethod]
    public void InitOverrides()
    {
        var o = new AppWindowOptions { Title = "t", Width = 1, Height = 2, Resizable = false };
        Assert.AreEqual("t", o.Title);
        Assert.AreEqual(1, o.Width);
        Assert.AreEqual(2, o.Height);
        Assert.IsFalse(o.Resizable);
    }
}
