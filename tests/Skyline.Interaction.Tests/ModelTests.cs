namespace Skyline.Interaction.Tests;

[TestClass]
public class ModelTests
{
    private static readonly DateTimeOffset At = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ApprovalDecision_Factories_CarryVerbAndConsent()
    {
        var allow = ApprovalDecision.Allow(At);
        Assert.AreEqual(ApprovalVerb.Allow, allow.Verb);
        Assert.AreEqual(ConsentKind.PromptAccepted, allow.Kind);
        Assert.AreEqual(At, allow.At);

        var once = ApprovalDecision.AllowOnce(At);
        Assert.AreEqual(ApprovalVerb.AllowOnce, once.Verb);
        Assert.AreEqual(ConsentKind.UserGesture, once.Kind);

        var deny = ApprovalDecision.Deny(At);
        Assert.AreEqual(ApprovalVerb.Deny, deny.Verb);
        Assert.AreEqual(ConsentKind.UserGesture, deny.Kind);
    }

    [TestMethod]
    public void ApprovalState_IsAClosedSetOfFourCases()
    {
        var actor = Actors.LocalHuman;
        var request = new ApprovalRequest("r1", actor, InteractionCapability.Edit, "type", At, true);
        var grant = new CapabilityGrant("g1", actor, Actors.Os, InteractionCapability.Edit, null, true);
        var consent = new ConsentRecord(actor.Id, ConsentKind.PromptAccepted, At, "ok");

        Assert.AreEqual("pending", Name(new Pending(request)));
        Assert.AreEqual("granted", Name(new Granted(grant, consent)));
        Assert.AreEqual("denied", Name(new Denied(consent)));
        Assert.AreEqual("expired", Name(new Expired("r1")));
    }

    private static string Name(ApprovalState state) => state switch
    {
        Pending p => p.Request.Id == "r1" ? "pending" : "?",
        Granted g => g.Grant.Id == "g1" && g.Consent.Reason == "ok" ? "granted" : "?",
        Denied d => d.Consent.Kind == ConsentKind.PromptAccepted ? "denied" : "?",
        Expired e => e.RequestId == "r1" ? "expired" : "?",
        _ => "?",
    };

    [TestMethod]
    public void Snapshot_Empty_HasNoModalIndicatorOrToasts()
    {
        Assert.IsFalse(ApprovalSnapshot.Empty.HasModal);
        Assert.IsFalse(ApprovalSnapshot.Empty.IndicatorActive);
        Assert.IsFalse(ApprovalSnapshot.Empty.HasToasts);
    }

    [TestMethod]
    public void Actor_CarriesItsFields()
    {
        var operatorActor = Actors.LocalHuman;
        var ai = new Actor("ai2", "Agent", ActorKind.Ai, ActorLocality.Local, DelegatedBy: operatorActor);
        Assert.AreEqual("ai2", ai.Id);
        Assert.AreEqual("Agent", ai.DisplayName);
        Assert.AreEqual(ActorKind.Ai, ai.Kind);
        Assert.AreEqual(ActorLocality.Local, ai.Locality);
        Assert.AreSame(operatorActor, ai.DelegatedBy);
    }

    [TestMethod]
    public void ApprovalRequest_CarriesItsFields()
    {
        var actor = Actors.Planner;
        var request = new ApprovalRequest("r1", actor, InteractionCapability.Edit, "type", At, true);
        Assert.AreEqual("r1", request.Id);
        Assert.AreSame(actor, request.Requester);
        Assert.AreEqual(InteractionCapability.Edit, request.Requested);
        Assert.AreEqual("type", request.Prompt);
        Assert.AreEqual(At, request.ExpiresAt);
        Assert.IsTrue(request.RequiresVisibleUi);
    }

    [TestMethod]
    public void CapabilityGrant_CarriesItsFields()
    {
        var grantee = Actors.Planner;
        var granter = Actors.Os;
        var grant = new CapabilityGrant("g1", grantee, granter, InteractionCapability.Edit, At, true);
        Assert.AreEqual("g1", grant.Id);
        Assert.AreSame(grantee, grant.Grantee);
        Assert.AreSame(granter, grant.GrantedBy);
        Assert.AreEqual(InteractionCapability.Edit, grant.Capabilities);
        Assert.AreEqual(At, grant.ExpiresAt);
        Assert.IsTrue(grant.RequiresVisibleUi);
    }

    [TestMethod]
    public void ConsentRecord_CarriesItsFields()
    {
        var consent = new ConsentRecord("h", ConsentKind.PromptAccepted, At, "because");
        Assert.AreEqual("h", consent.ActorId);
        Assert.AreEqual(ConsentKind.PromptAccepted, consent.Kind);
        Assert.AreEqual(At, consent.At);
        Assert.AreEqual("because", consent.Reason);
    }
}
