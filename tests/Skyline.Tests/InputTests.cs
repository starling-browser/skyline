using Skyline;
using Skyline.Input;

namespace Skyline.Tests;

[TestClass]
public class InputTests
{
    [TestMethod]
    public void PointerEventCarriesFields()
    {
        var e = new PointerEvent(PointerEventKind.Wheel, 3f, 4f, -1, 0.5f, -1.5f);
        Assert.AreEqual(PointerEventKind.Wheel, e.Kind);
        Assert.AreEqual(3f, e.X);
        Assert.AreEqual(4f, e.Y);
        Assert.AreEqual(-1, e.Button);
        Assert.AreEqual(0.5f, e.WheelDx);
        Assert.AreEqual(-1.5f, e.WheelDy);
    }

    [TestMethod]
    public void KeyEventCarriesFields()
    {
        var e = new KeyEvent(true, Key.Escape, 256);
        Assert.IsTrue(e.IsDown);
        Assert.AreEqual(Key.Escape, e.Key);
        Assert.AreEqual(256, e.Code);
    }

    [TestMethod]
    public void TextEventCarriesCharacter()
    {
        Assert.AreEqual('é', new TextEvent('é').Character);
    }

    [TestMethod]
    public void KeyValuesMatchGlfwKeycodes()
    {
        Assert.AreEqual(-1, (int)Key.Unknown);
        Assert.AreEqual(32, (int)Key.Space);
        Assert.AreEqual(65, (int)Key.A);
        Assert.AreEqual(90, (int)Key.Z);
        Assert.AreEqual(48, (int)Key.D0);
        Assert.AreEqual(57, (int)Key.D9);
        Assert.AreEqual(256, (int)Key.Escape);
    }

    [DataTestMethod]
    [DataRow(Silk.NET.Input.Key.Escape, Key.Escape)]
    [DataRow(Silk.NET.Input.Key.Space, Key.Space)]
    [DataRow(Silk.NET.Input.Key.A, Key.A)]
    [DataRow(Silk.NET.Input.Key.Number0, Key.D0)]
    public void MapKeyMapsDefinedKeys(Silk.NET.Input.Key silk, Key expected)
    {
        Assert.AreEqual(expected, AppWindow.MapKey(silk));
    }

    [TestMethod]
    public void MapKeyReportsUnknownForUnmappedCodes()
    {
        // Silk's enum has entries Skyline's does not. They report Unknown
        // and round-trip through KeyEvent.Code instead.
        Assert.AreEqual(Key.Unknown, AppWindow.MapKey((Silk.NET.Input.Key)12345));
    }
}
