namespace Skyline.Interaction.Tests;

[TestClass]
public class ShellTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task DeniedByPolicy_ReturnsNull()
    {
        var shell = new InProcessApprovalShell(new StubApprovalUi(ApprovalDecision.Allow(Start)));
        var grant = await shell.AuthorizeAsync(Actors.RemoteHuman, InteractionCapability.Edit, "type");
        Assert.IsNull(grant);
    }

    [TestMethod]
    public async Task ImplicitGrant_MintsWithoutAskingTheUi()
    {
        var ui = new StubApprovalUi(ApprovalDecision.Deny(Start));
        var shell = new InProcessApprovalShell(ui);
        var grant = await shell.AuthorizeAsync(Actors.LocalHuman, InteractionCapability.Point, "point");
        Assert.IsNotNull(grant);
        Assert.AreSame(Actors.LocalHuman, grant!.Grantee);
        Assert.AreEqual(InteractionCapability.Point, grant.Capabilities);
        Assert.IsFalse(grant.RequiresVisibleUi);
        Assert.IsNull(ui.Last, "an implicit grant never reaches the UI");
    }

    [TestMethod]
    public async Task Ask_Allowed_MintsAVisibleGrant()
    {
        var ui = new StubApprovalUi(ApprovalDecision.Allow(Start));
        var time = new ManualTimeProvider(Start);
        var shell = new InProcessApprovalShell(ui, time);
        var grant = await shell.AuthorizeAsync(Actors.Planner, InteractionCapability.Edit, "Planner wants to type");
        Assert.IsNotNull(grant);
        Assert.IsTrue(grant!.RequiresVisibleUi);
        Assert.IsNotNull(ui.Last);
        Assert.AreEqual("Planner wants to type", ui.Last!.Prompt);
        Assert.AreEqual(Start + TimeSpan.FromSeconds(30), ui.Last.ExpiresAt);
        Assert.IsTrue(ui.Last.RequiresVisibleUi);
    }

    [TestMethod]
    public async Task Ask_Allowed_MintsAPersistentGrant()
    {
        var shell = new InProcessApprovalShell(new StubApprovalUi(ApprovalDecision.Allow(Start)));
        var grant = await shell.AuthorizeAsync(Actors.Planner, InteractionCapability.Edit, "type");
        Assert.IsNotNull(grant);
        Assert.IsNull(grant!.ExpiresAt, "a plain Allow persists with no expiry");
    }

    [TestMethod]
    public async Task Ask_AllowedOnce_MintsAnExpiringGrant()
    {
        var shell = new InProcessApprovalShell(new StubApprovalUi(ApprovalDecision.AllowOnce(Start)));
        var grant = await shell.AuthorizeAsync(Actors.Planner, InteractionCapability.Edit, "type");
        Assert.IsNotNull(grant);
        // "Allow once" is distinct from Allow: it expires at the grant instant,
        // so it is never retained as a live grant.
        Assert.AreEqual(Start, grant!.ExpiresAt);
    }

    [TestMethod]
    public async Task Ask_Denied_ReturnsNull()
    {
        var ui = new StubApprovalUi(ApprovalDecision.Deny(Start));
        var shell = new InProcessApprovalShell(ui);
        var grant = await shell.AuthorizeAsync(Actors.Planner, InteractionCapability.Edit, "type");
        Assert.IsNull(grant);
    }

    [TestMethod]
    public async Task BeforeGrant_Veto_RefusesTheGrant()
    {
        var shell = new InProcessApprovalShell(new StubApprovalUi(ApprovalDecision.Allow(Start)))
        {
            BeforeGrant = _ => false,
        };
        var grant = await shell.AuthorizeAsync(Actors.LocalHuman, InteractionCapability.Point, "point");
        Assert.IsNull(grant);
    }

    [TestMethod]
    public async Task BeforeGrant_Pass_AndGrantMinted_Fires()
    {
        CapabilityGrant? observed = null;
        var shell = new InProcessApprovalShell(new StubApprovalUi(ApprovalDecision.Allow(Start)))
        {
            BeforeGrant = _ => true,
        };
        shell.GrantMinted += g => observed = g;
        var grant = await shell.AuthorizeAsync(Actors.LocalHuman, InteractionCapability.Point, "point");
        Assert.IsNotNull(grant);
        Assert.AreSame(grant, observed);
    }

    [TestMethod]
    public async Task PromptTimeout_IsOverridable()
    {
        var ui = new StubApprovalUi(ApprovalDecision.Allow(Start));
        var time = new ManualTimeProvider(Start);
        var shell = new InProcessApprovalShell(ui, time) { PromptTimeout = TimeSpan.FromSeconds(5) };
        await shell.AuthorizeAsync(Actors.Planner, InteractionCapability.Edit, "type");
        Assert.AreEqual(Start + TimeSpan.FromSeconds(5), ui.Last!.ExpiresAt);
    }

    [TestMethod]
    public async Task Policy_IsSwappable()
    {
        var shell = new InProcessApprovalShell(new StubApprovalUi(ApprovalDecision.Deny(Start)))
        {
            Policy = new AlwaysGrantPolicy(),
        };
        // A remote human asking to type would be hard-denied by the default policy.
        var grant = await shell.AuthorizeAsync(Actors.RemoteHuman, InteractionCapability.Edit, "type");
        Assert.IsNotNull(grant);
    }

    private sealed class AlwaysGrantPolicy : IApprovalPolicy
    {
        public PolicyOutcome Evaluate(Actor actor, InteractionCapability requested) =>
            new ImplicitGrant(ConsentKind.PolicyAllowed);
    }
}
