using Silk.NET.WebGPU;

namespace Skyline.Interaction.Gpu.Tests;

[TestClass]
public class GpuTransferTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Actor Local = new("u", "User", ActorKind.Human, ActorLocality.Local);
    private static readonly Actor Remote = new("r", "Remote", ActorKind.Human, ActorLocality.Remote);
    private static readonly GpuSurfaceHandle Surface = new(0xABCD, TextureFormat.Rgba8Unorm, 64, 48);

    private static Provenance Prov() => new(Local, T0);

    [TestMethod]
    public void SurfaceHandleCarriesFields()
    {
        Assert.AreEqual((nint)0xABCD, Surface.Texture);
        Assert.AreEqual(TextureFormat.Rgba8Unorm, Surface.Format);
        Assert.AreEqual(64u, Surface.Width);
        Assert.AreEqual(48u, Surface.Height);
    }

    [TestMethod]
    public void OfferIsListedAndTakeable()
    {
        var broker = new InProcessGpuTransferBroker(new FixedTime(T0));
        var offer = broker.Offer(Surface, Prov());

        Assert.AreEqual(TransferPolicy.Default, offer.Policy);
        Assert.AreEqual(Surface, offer.Surface);
        CollectionAssert.AreEqual(new[] { offer }, broker.List().ToArray());

        var taken = broker.Take(offer.Id, Local);
        Assert.IsNotNull(taken);
        Assert.AreEqual(offer.Id, taken!.Id);
    }

    [TestMethod]
    public void TakeUnknownIdReturnsNull()
    {
        var broker = new InProcessGpuTransferBroker(new FixedTime(T0));
        Assert.IsNull(broker.Take("missing", Local));
    }

    [TestMethod]
    public void RemoteTakerIsDeniedALocalOnlyOffer()
    {
        var broker = new InProcessGpuTransferBroker(new FixedTime(T0));
        var offer = broker.Offer(Surface, Prov()); // Default policy: local only
        Assert.IsNull(broker.Take(offer.Id, Remote));
    }

    [TestMethod]
    public void RemoteTakerIsAllowedWhenThePolicyAllowsIt()
    {
        var broker = new InProcessGpuTransferBroker(new FixedTime(T0));
        var policy = new TransferPolicy(TransferScope.Session, AllowRemote: true);
        var offer = broker.Offer(Surface, Prov(), policy);
        var taken = broker.Take(offer.Id, Remote);
        Assert.IsNotNull(taken);
        Assert.AreEqual(offer.Id, taken!.Id);
    }

    [TestMethod]
    public void RevokeRemovesAnOffer()
    {
        var broker = new InProcessGpuTransferBroker(new FixedTime(T0));
        var offer = broker.Offer(Surface, Prov());
        Assert.IsTrue(broker.Revoke(offer.Id));
        Assert.IsFalse(broker.Revoke(offer.Id)); // already gone
        Assert.AreEqual(0, broker.List().Count);
    }

    [TestMethod]
    public void ExpiredOffersArePrunedAndLiveOnesSurvive()
    {
        var clock = new FixedTime(T0);
        var broker = new InProcessGpuTransferBroker(clock);
        var expiring = broker.Offer(Surface, Prov(),
            new TransferPolicy(TransferScope.Session, AllowRemote: false, ExpiresAt: T0.AddSeconds(10)));
        var permanent = broker.Offer(Surface, Prov()); // no expiry

        clock.Now = T0.AddSeconds(11);

        Assert.IsNull(broker.Take(expiring.Id, Local), "an expired offer is pruned before a take");
        var live = broker.List();
        Assert.AreEqual(1, live.Count);
        Assert.AreEqual(permanent.Id, live[0].Id);
    }
}
