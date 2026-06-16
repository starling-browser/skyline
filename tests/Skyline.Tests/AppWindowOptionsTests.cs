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
        Assert.AreEqual(ChromeMode.Standard, o.Chrome);
        Assert.IsFalse(o.ForceGlfw);
        Assert.AreEqual(ChromeMode.Standard, o.EffectiveChrome);
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

    [TestMethod]
    public void ChromeAndForceGlfwOverrides()
    {
        var o = new AppWindowOptions { Chrome = ChromeMode.Transparent, ForceGlfw = true };
        Assert.AreEqual(ChromeMode.Transparent, o.Chrome);
        Assert.IsTrue(o.ForceGlfw);
    }

    [TestMethod]
    public void NonResizableStandardFoldsToFixed()
    {
        var o = new AppWindowOptions { Resizable = false };
        Assert.AreEqual(ChromeMode.Fixed, o.EffectiveChrome);
    }

    [TestMethod]
    public void ResizableDoesNotOverrideExplicitChrome()
    {
        // A non-Standard mode is honored as-is; Resizable only folds Standard.
        var o = new AppWindowOptions { Chrome = ChromeMode.Borderless, Resizable = false };
        Assert.AreEqual(ChromeMode.Borderless, o.EffectiveChrome);
    }
}
