namespace Skyline.Interaction.Tests;

[TestClass]
public class TargetTests
{
    [TestMethod]
    public void EveryTargetKindCarriesItsFields()
    {
        var point = new PointTarget(3f, 4f);
        Assert.AreEqual(3f, point.X);
        Assert.AreEqual(4f, point.Y);

        var obj = new ObjectTarget("node-1");
        Assert.AreEqual("node-1", obj.ObjectId);

        var range = new RangeTarget("node-1", 2, 7);
        Assert.AreEqual("node-1", range.ObjectId);
        Assert.AreEqual(2, range.Start);
        Assert.AreEqual(7, range.End);

        var ray = new RayTarget(1f, 2f, 3f, 0f, 0f, -1f);
        Assert.AreEqual(1f, ray.OriginX);
        Assert.AreEqual(2f, ray.OriginY);
        Assert.AreEqual(3f, ray.OriginZ);
        Assert.AreEqual(0f, ray.DirectionX);
        Assert.AreEqual(0f, ray.DirectionY);
        Assert.AreEqual(-1f, ray.DirectionZ);

        var volume = new VolumeTarget(1f, 2f, 3f, 4f, 5f, 6f);
        Assert.AreEqual(1f, volume.X);
        Assert.AreEqual(2f, volume.Y);
        Assert.AreEqual(3f, volume.Z);
        Assert.AreEqual(4f, volume.Width);
        Assert.AreEqual(5f, volume.Height);
        Assert.AreEqual(6f, volume.Depth);

        var semantic = new SemanticTarget("skyline://address-bar");
        Assert.AreEqual("skyline://address-bar", semantic.Uri);
    }

    [TestMethod]
    public void TargetRef_PairsATargetWithItsSurface()
    {
        var reference = new TargetRef("surface-7", new PointTarget(1f, 1f));
        Assert.AreEqual("surface-7", reference.SurfaceId);
        Assert.IsTrue(reference.Target is PointTarget);
    }

    [TestMethod]
    public void Target_IsAClosedSetOfSixCases()
    {
        Assert.AreEqual("point", Kind(new PointTarget(0f, 0f)));
        Assert.AreEqual("object", Kind(new ObjectTarget("o")));
        Assert.AreEqual("range", Kind(new RangeTarget("o", 0, 1)));
        Assert.AreEqual("ray", Kind(new RayTarget(0f, 0f, 0f, 0f, 0f, 0f)));
        Assert.AreEqual("volume", Kind(new VolumeTarget(0f, 0f, 0f, 0f, 0f, 0f)));
        Assert.AreEqual("semantic", Kind(new SemanticTarget("u")));
    }

    private static string Kind(Target target) => target switch
    {
        PointTarget => "point",
        ObjectTarget => "object",
        RangeTarget => "range",
        RayTarget => "ray",
        VolumeTarget => "volume",
        SemanticTarget => "semantic",
        _ => "?",
    };
}
