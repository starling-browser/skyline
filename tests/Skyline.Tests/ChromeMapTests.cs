using Silk.NET.Windowing;
using Skyline;

namespace Skyline.Tests;

[TestClass]
public class ChromeMapTests
{
    [TestMethod]
    public void GlfwStandard()
    {
        Assert.AreEqual((WindowBorder.Resizable, false), GlfwChrome.Map(ChromeMode.Standard));
    }

    [TestMethod]
    public void GlfwFixed()
    {
        Assert.AreEqual((WindowBorder.Fixed, false), GlfwChrome.Map(ChromeMode.Fixed));
    }

    [TestMethod]
    public void GlfwBorderless()
    {
        Assert.AreEqual((WindowBorder.Hidden, false), GlfwChrome.Map(ChromeMode.Borderless));
    }

    [TestMethod]
    public void GlfwTransparent()
    {
        Assert.AreEqual((WindowBorder.Hidden, true), GlfwChrome.Map(ChromeMode.Transparent));
    }

    [TestMethod]
    public void GlfwUnifiedTitlebarFallsBackToDecorated()
    {
        Assert.AreEqual((WindowBorder.Resizable, false), GlfwChrome.Map(ChromeMode.UnifiedTitlebar));
    }

    [TestMethod]
    public void GlfwUnknownThrows()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => GlfwChrome.Map((ChromeMode)999));
    }

    [TestMethod]
    public void AppKitStandardIsTitledResizableOpaque()
    {
        var c = AppKitChromeMap.Map(ChromeMode.Standard);
        // Titled|Closable|Miniaturizable|Resizable = 1|2|4|8.
        Assert.AreEqual(15u, c.StyleMask);
        Assert.IsFalse(c.TitlebarTransparent);
        Assert.IsTrue(c.Opaque);
    }

    [TestMethod]
    public void AppKitFixedDropsResizable()
    {
        var c = AppKitChromeMap.Map(ChromeMode.Fixed);
        // Titled|Closable|Miniaturizable = 1|2|4, no Resizable bit.
        Assert.AreEqual(7u, c.StyleMask);
        Assert.AreEqual(0u, c.StyleMask & 8u);
    }

    [TestMethod]
    public void AppKitBorderlessIsZeroMask()
    {
        var c = AppKitChromeMap.Map(ChromeMode.Borderless);
        Assert.AreEqual(0u, c.StyleMask);
        Assert.IsTrue(c.Opaque);
    }

    [TestMethod]
    public void AppKitTransparentExtendsAndIsNotOpaque()
    {
        var c = AppKitChromeMap.Map(ChromeMode.Transparent);
        // FullSizeContentView = 1 << 15 set on top of the standard mask.
        Assert.AreNotEqual(0u, c.StyleMask & (1u << 15));
        Assert.IsTrue(c.TitlebarTransparent);
        Assert.IsFalse(c.Opaque);
        Assert.IsFalse(c.HideTitle);
    }

    [TestMethod]
    public void AppKitUnifiedTitlebarIsOpaqueWithHiddenTitleUnderContent()
    {
        var c = AppKitChromeMap.Map(ChromeMode.UnifiedTitlebar);
        // Full standard mask plus FullSizeContentView = 1 << 15.
        Assert.AreEqual(15u | (1u << 15), c.StyleMask);
        Assert.IsTrue(c.TitlebarTransparent);
        Assert.IsTrue(c.Opaque);
        Assert.IsTrue(c.HideTitle);
    }

    [TestMethod]
    public void AppKitUnknownThrows()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => AppKitChromeMap.Map((ChromeMode)999));
    }
}
