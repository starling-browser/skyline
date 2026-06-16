namespace Skyline.Interaction.Tests;

[TestClass]
public class TransferTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    private static Provenance From(Actor actor) => new(actor, Start, "test");

    [TestMethod]
    public void Offer_RecordsThePayloadAndDefaultsThePolicy()
    {
        var broker = new InProcessTransferBroker(new ManualTimeProvider(Start));
        var offer = broker.Offer("text/plain", "hello", From(Actors.LocalHuman));
        Assert.AreEqual("text/plain", offer.MimeType);
        Assert.AreEqual("hello", offer.Payload);
        Assert.AreSame(TransferPolicy.Default, offer.Policy);
        Assert.AreEqual(Actors.LocalHuman, offer.Provenance.Source);
        Assert.AreEqual("test", offer.Provenance.Origin);
        Assert.AreEqual(Start, offer.Provenance.At);
    }

    [TestMethod]
    public void Take_ReturnsTheOfferToALocalActor()
    {
        var broker = new InProcessTransferBroker(new ManualTimeProvider(Start));
        var offer = broker.Offer("text/plain", "hi", From(Actors.LocalHuman));
        var taken = broker.Take(offer.Id, Actors.LocalHuman);
        Assert.AreSame(offer, taken);
    }

    [TestMethod]
    public void Take_UnknownId_ReturnsNull()
    {
        var broker = new InProcessTransferBroker(new ManualTimeProvider(Start));
        Assert.IsNull(broker.Take("missing", Actors.LocalHuman));
    }

    [TestMethod]
    public void Take_RemoteActor_IsDeniedALocalOnlyOffer()
    {
        var broker = new InProcessTransferBroker(new ManualTimeProvider(Start));
        var offer = broker.Offer("text/plain", "secret", From(Actors.LocalHuman));
        Assert.IsNull(broker.Take(offer.Id, Actors.RemoteHuman));
    }

    [TestMethod]
    public void Take_RemoteActor_IsAllowedWhenThePolicyPermits()
    {
        var broker = new InProcessTransferBroker(new ManualTimeProvider(Start));
        var policy = new TransferPolicy(TransferScope.System, AllowRemote: true);
        var offer = broker.Offer("text/plain", "shared", From(Actors.LocalHuman), policy);
        Assert.AreSame(offer, broker.Take(offer.Id, Actors.RemoteHuman));
    }

    [TestMethod]
    public void ExpiredOffers_ArePrunedFromTakeAndList()
    {
        var time = new ManualTimeProvider(Start);
        var broker = new InProcessTransferBroker(time);
        var policy = new TransferPolicy(TransferScope.Session, AllowRemote: false, ExpiresAt: Start + TimeSpan.FromSeconds(10));
        var offer = broker.Offer("text/plain", "fleeting", From(Actors.LocalHuman), policy);

        Assert.AreEqual(1, broker.List().Count);
        time.Advance(TimeSpan.FromSeconds(11));
        Assert.IsNull(broker.Take(offer.Id, Actors.LocalHuman), "an expired offer is gone");
        Assert.AreEqual(0, broker.List().Count);
    }

    [TestMethod]
    public void List_KeepsLiveOffers()
    {
        var time = new ManualTimeProvider(Start);
        var broker = new InProcessTransferBroker(time);
        broker.Offer("text/plain", "kept", From(Actors.LocalHuman)); // no expiry
        broker.Offer("text/plain", "also", From(Actors.LocalHuman),
            new TransferPolicy(TransferScope.Session, false, ExpiresAt: Start + TimeSpan.FromHours(1)));
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.AreEqual(2, broker.List().Count);
    }

    [TestMethod]
    public void Revoke_RemovesAnOffer()
    {
        var broker = new InProcessTransferBroker(new ManualTimeProvider(Start));
        var offer = broker.Offer("text/plain", "gone", From(Actors.LocalHuman));
        Assert.IsTrue(broker.Revoke(offer.Id));
        Assert.IsFalse(broker.Revoke(offer.Id), "revoking twice is a no-op");
        Assert.IsNull(broker.Take(offer.Id, Actors.LocalHuman));
    }

    [TestMethod]
    public void DefaultClock_Works()
    {
        var broker = new InProcessTransferBroker();
        var offer = broker.Offer("text/plain", "x", From(Actors.LocalHuman));
        Assert.AreEqual(1, broker.List().Count);
        Assert.AreSame(offer, broker.Take(offer.Id, Actors.LocalHuman));
    }

    [TestMethod]
    public void TransferPolicy_Default_IsLocalSession()
    {
        Assert.AreEqual(TransferScope.Session, TransferPolicy.Default.Scope);
        Assert.IsFalse(TransferPolicy.Default.AllowRemote);
        Assert.IsNull(TransferPolicy.Default.ExpiresAt);
    }
}
