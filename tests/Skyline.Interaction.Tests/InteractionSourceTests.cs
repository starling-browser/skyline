using Skyline.Input;

namespace Skyline.Interaction.Tests;

[TestClass]
public class InteractionSourceTests
{
    [TestMethod]
    public void PointerDown_RoutesToTheSink()
    {
        var sink = new CountingSink();
        var source = new AppWindowInteractionSource(sink);
        source.OnPointerEvent(new PointerEvent(PointerEventKind.Down, 12f, 34f, 0, 0, 0));
        Assert.AreEqual(1, sink.Count);
        Assert.AreEqual(12f, sink.X);
        Assert.AreEqual(34f, sink.Y);
    }

    [TestMethod]
    public void NonDownPointerEvents_AreIgnored()
    {
        var sink = new CountingSink();
        var source = new AppWindowInteractionSource(sink);
        source.OnPointerEvent(new PointerEvent(PointerEventKind.Move, 1f, 1f, 0, 0, 0));
        source.OnPointerEvent(new PointerEvent(PointerEventKind.Up, 1f, 1f, 0, 0, 0));
        Assert.AreEqual(0, sink.Count);
    }

    [TestMethod]
    public void LocalHuman_IsALocalHumanActor()
    {
        var source = new AppWindowInteractionSource(new CountingSink());
        Assert.AreEqual(ActorKind.Human, source.LocalHuman.Kind);
        Assert.AreEqual(ActorLocality.Local, source.LocalHuman.Locality);
    }

    [TestMethod]
    public void Copy_WritesThroughToTheClipboard()
    {
        var clipboard = new MemoryClipboard();
        var source = new AppWindowInteractionSource(new CountingSink(), clipboard);
        source.Copy("hello");
        Assert.AreEqual("hello", clipboard.Text);
        Assert.AreEqual("hello", source.Paste());
    }

    [TestMethod]
    public void Paste_FallsBackToLastCopiedWhenClipboardIsEmpty()
    {
        var source = new AppWindowInteractionSource(new CountingSink(), new EmptyClipboard());
        Assert.IsNull(source.Paste());
        source.Copy("remembered");
        Assert.AreEqual("remembered", source.Paste());
    }

    [TestMethod]
    public void DefaultClipboard_IsInMemory()
    {
        var source = new AppWindowInteractionSource(new CountingSink());
        source.Copy("x");
        Assert.AreEqual("x", source.Paste());
    }
}
