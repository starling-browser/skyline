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
        // An unmapped key code maps to Unknown.
        Assert.AreEqual(Key.Unknown, AppWindow.MapKey((Silk.NET.Input.Key)12345));
    }

    [TestMethod]
    public void EventsDefaultToNoModifiers()
    {
        Assert.AreEqual(ModifierKeys.None, new PointerEvent(PointerEventKind.Move, 0, 0, -1, 0, 0).Modifiers);
        Assert.AreEqual(ModifierKeys.None, new KeyEvent(true, Key.A, 65).Modifiers);
    }

    [TestMethod]
    public void EventsCarryModifiers()
    {
        var mods = ModifierKeys.Cmd | ModifierKeys.Shift;
        Assert.AreEqual(mods, new PointerEvent(PointerEventKind.Down, 1, 2, 0, 0, 0, mods).Modifiers);
        Assert.AreEqual(mods, new KeyEvent(true, Key.A, 65, mods).Modifiers);
    }

    [TestMethod]
    public void ModifierKeysMapReportsNoneWhenNothingPressed()
    {
        Assert.AreEqual(ModifierKeys.None, ModifierKeysMap.FromPressed(_ => false));
    }

    [TestMethod]
    public void ModifierKeysMapReportsEveryFlagWhenAllPressed()
    {
        Assert.AreEqual(
            ModifierKeys.Shift | ModifierKeys.Ctrl | ModifierKeys.Alt | ModifierKeys.Cmd | ModifierKeys.CapsLock,
            ModifierKeysMap.FromPressed(_ => true));
    }

    [DataTestMethod]
    [DataRow(Silk.NET.Input.Key.ShiftLeft, ModifierKeys.Shift)]
    [DataRow(Silk.NET.Input.Key.ShiftRight, ModifierKeys.Shift)]
    [DataRow(Silk.NET.Input.Key.ControlLeft, ModifierKeys.Ctrl)]
    [DataRow(Silk.NET.Input.Key.ControlRight, ModifierKeys.Ctrl)]
    [DataRow(Silk.NET.Input.Key.AltLeft, ModifierKeys.Alt)]
    [DataRow(Silk.NET.Input.Key.AltRight, ModifierKeys.Alt)]
    [DataRow(Silk.NET.Input.Key.SuperLeft, ModifierKeys.Cmd)]
    [DataRow(Silk.NET.Input.Key.SuperRight, ModifierKeys.Cmd)]
    [DataRow(Silk.NET.Input.Key.CapsLock, ModifierKeys.CapsLock)]
    public void ModifierKeysMapMapsEachKeyToItsFlag(Silk.NET.Input.Key pressed, ModifierKeys expected)
    {
        Assert.AreEqual(expected, ModifierKeysMap.FromPressed(k => k == pressed));
    }
}
