namespace Skyline.Interaction.Tests;

[TestClass]
public class PolicyTests
{
    private readonly DefaultApprovalPolicy _policy = new();

    [TestMethod]
    public void System_IsAlwaysImplicitlyGranted()
    {
        var outcome = _policy.Evaluate(Actors.Os, InteractionCapability.Administer);
        Assert.IsTrue(outcome is ImplicitGrant { Kind: ConsentKind.SystemAllowed });
    }

    [TestMethod]
    public void LocalHuman_OrdinaryCapability_IsImplicitlyGranted()
    {
        var outcome = _policy.Evaluate(Actors.LocalHuman, InteractionCapability.Point | InteractionCapability.Select);
        Assert.IsTrue(outcome is ImplicitGrant { Kind: ConsentKind.UserGesture });
    }

    [TestMethod]
    public void LocalHuman_SensitiveCapability_Asks()
    {
        Assert.IsTrue(_policy.Evaluate(Actors.LocalHuman, InteractionCapability.Edit) is Ask);
        Assert.IsTrue(_policy.Evaluate(Actors.LocalHuman, InteractionCapability.LowLevelInput) is Ask);
        Assert.IsTrue(_policy.Evaluate(Actors.LocalHuman, InteractionCapability.Administer) is Ask);
    }

    [TestMethod]
    public void RemoteHuman_EditOrKeyboard_IsHardDenied()
    {
        Assert.IsTrue(_policy.Evaluate(Actors.RemoteHuman, InteractionCapability.Edit) is Deny);
        Assert.IsTrue(_policy.Evaluate(Actors.RemoteHuman, InteractionCapability.LowLevelInput) is Deny);
    }

    [TestMethod]
    public void RemoteHuman_AllowedSet_IsImplicitlyGranted()
    {
        var outcome = _policy.Evaluate(Actors.RemoteHuman,
            InteractionCapability.Observe | InteractionCapability.Point |
            InteractionCapability.Select | InteractionCapability.Collaborate);
        Assert.IsTrue(outcome is ImplicitGrant { Kind: ConsentKind.PolicyAllowed });
    }

    [TestMethod]
    public void RemoteHuman_OtherCapability_Asks()
    {
        // Manipulate is neither hard-denied nor in the allowed set.
        Assert.IsTrue(_policy.Evaluate(Actors.RemoteHuman, InteractionCapability.Manipulate) is Ask);
    }

    [TestMethod]
    public void Ai_Keyboard_IsHardDenied()
    {
        Assert.IsTrue(_policy.Evaluate(Actors.Planner, InteractionCapability.LowLevelInput) is Deny);
    }

    [TestMethod]
    public void RemoteNonHuman_EditOrKeyboard_IsHardDenied()
    {
        // The remote hard-deny gates every actor kind, not just humans: a
        // remote AI or automation can't be prompted into Edit or the keyboard.
        var remoteAi = new Actor("rai", "Remote AI", ActorKind.Ai, ActorLocality.Remote);
        var remoteRobot = new Actor("rauto", "Remote Robot", ActorKind.Automation, ActorLocality.Remote);
        Assert.IsTrue(_policy.Evaluate(remoteAi, InteractionCapability.Edit) is Deny);
        Assert.IsTrue(_policy.Evaluate(remoteAi, InteractionCapability.LowLevelInput) is Deny);
        Assert.IsTrue(_policy.Evaluate(remoteRobot, InteractionCapability.Edit) is Deny);
        Assert.IsTrue(_policy.Evaluate(remoteRobot, InteractionCapability.LowLevelInput) is Deny);
    }

    [TestMethod]
    public void RemoteNonHuman_OrdinaryCapability_StillAsks()
    {
        // Outside the hard-denied set, a remote AI still falls through to Ask.
        var remoteAi = new Actor("rai", "Remote AI", ActorKind.Ai, ActorLocality.Remote);
        Assert.IsTrue(_policy.Evaluate(remoteAi, InteractionCapability.Observe) is Ask);
    }

    [TestMethod]
    public void AiOrAutomation_OtherwiseAsks()
    {
        Assert.IsTrue(_policy.Evaluate(Actors.Planner, InteractionCapability.Edit) is Ask);
        Assert.IsTrue(_policy.Evaluate(Actors.Robot, InteractionCapability.LowLevelInput) is Ask);
    }
}
