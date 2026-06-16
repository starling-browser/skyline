namespace Skyline.Interaction.Tests;

[TestClass]
public class FocusTests
{
    private static TargetRef Ref(string id) => new("surface", new ObjectTarget(id));

    [TestMethod]
    public void Empty_HasNoSlots()
    {
        var graph = FocusGraph.Empty;
        Assert.IsNull(graph.Attention);
        Assert.IsNull(graph.Pointer);
        Assert.IsNull(graph.Navigation);
        Assert.IsNull(graph.Text);
        Assert.IsNull(graph.Command);
        Assert.IsNull(graph.Capture);
        Assert.IsNull(graph.BestCommandTarget);
    }

    [TestMethod]
    public void BestCommandTarget_PrefersTheMostSpecificSlot()
    {
        Assert.AreEqual("cmd", Id((FocusGraph.Empty with { Command = Ref("cmd"), Text = Ref("txt") }).BestCommandTarget));
        Assert.AreEqual("txt", Id((FocusGraph.Empty with { Text = Ref("txt"), Pointer = Ref("ptr") }).BestCommandTarget));
        Assert.AreEqual("ptr", Id((FocusGraph.Empty with { Pointer = Ref("ptr"), Attention = Ref("att") }).BestCommandTarget));
        Assert.AreEqual("att", Id((FocusGraph.Empty with { Attention = Ref("att"), Navigation = Ref("nav") }).BestCommandTarget));
        Assert.AreEqual("nav", Id((FocusGraph.Empty with { Navigation = Ref("nav") }).BestCommandTarget));
    }

    [TestMethod]
    public void Capture_IsAnIndependentSlot()
    {
        var graph = FocusGraph.Empty with { Capture = Ref("cap") };
        Assert.AreEqual("cap", Id(graph.Capture));
        Assert.IsNull(graph.BestCommandTarget, "capture does not feed command routing");
    }

    [TestMethod]
    public void ObserveFrom_RoutesEachModalityToItsSlot()
    {
        var target = Ref("t");
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Gaze, target).Attention));
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Pointer, target).Pointer));
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Touch, target).Pointer));
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Keyboard, target).Text));
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Voice, target).Command));
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Ai, target).Command));
        Assert.AreEqual("t", Id(FocusGraph.Empty.ObserveFrom(InputModality.Clipboard, target).Command));
    }

    [TestMethod]
    public void ObserveFrom_GazeTouchesAttentionOnly()
    {
        var graph = FocusGraph.Empty.ObserveFrom(InputModality.Gaze, Ref("eyes"));
        Assert.IsNull(graph.Pointer);
        Assert.IsNull(graph.Text);
        Assert.IsNull(graph.Command);
    }

    [TestMethod]
    public void ObserveFrom_UnmappedModality_LeavesTheGraphUnchanged()
    {
        var graph = FocusGraph.Empty with { Pointer = Ref("ptr") };
        Assert.AreSame(graph, graph.ObserveFrom(InputModality.Other, Ref("x")));
    }

    private static string? Id(TargetRef? reference) =>
        reference?.Target is ObjectTarget o ? o.ObjectId : null;
}
