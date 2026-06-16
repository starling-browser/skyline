// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// The default shell: plain and in-process. It runs one capability ask through
/// the <see cref="Policy"/> and, when the policy says to ask, through the
/// <see cref="IApprovalUi"/>. Inject a <see cref="TimeProvider"/> so deadlines
/// run against a fake clock in tests.
/// </summary>
public sealed class InProcessApprovalShell
{
    private static readonly Actor SystemActor =
        new("system", "System", ActorKind.System, ActorLocality.Local);

    private readonly IApprovalUi _ui;
    private readonly TimeProvider _time;

    public InProcessApprovalShell(IApprovalUi ui, TimeProvider? time = null)
    {
        _ui = ui;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The decision rule. Swap the whole thing to change behavior.</summary>
    public IApprovalPolicy Policy { get; set; } = new DefaultApprovalPolicy();

    /// <summary>How long a prompt waits before it denies itself.</summary>
    public TimeSpan PromptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A veto called with the grant about to be minted. Return false to refuse
    /// it without forking the policy or the UI. The ask returns null.
    /// </summary>
    public Func<CapabilityGrant, bool>? BeforeGrant { get; set; }

    /// <summary>Raised after a grant is minted, so a consumer can observe it (for example to light an indicator).</summary>
    public event Action<CapabilityGrant>? GrantMinted;

    /// <summary>
    /// Authorize <paramref name="requester"/> for <paramref name="requested"/>.
    /// Returns the grant, or null when the policy or a person denied it, or a
    /// <see cref="BeforeGrant"/> veto refused it.
    /// </summary>
    public async Task<CapabilityGrant?> AuthorizeAsync(
        Actor requester, InteractionCapability requested, string prompt, CancellationToken ct = default)
    {
        var outcome = Policy.Evaluate(requester, requested);
        if (outcome is Deny)
        {
            return null;
        }
        if (outcome is ImplicitGrant)
        {
            return Mint(requester, requested, requiresVisibleUi: false, expiresAt: null);
        }

        // The remaining case is Ask.
        var request = new ApprovalRequest(
            NewId(), requester, requested, prompt,
            _time.GetUtcNow() + PromptTimeout, RequiresVisibleUi: true);
        var decision = await _ui.RequestAsync(request, ct).ConfigureAwait(false);
        if (decision.Verb == ApprovalVerb.Deny)
        {
            return null;
        }

        // "Allow once" authorizes only this operation, so the grant expires at
        // the instant it is given — a host that keeps live grants lit will not
        // retain it. A plain "Allow" persists with no expiry.
        var expiresAt = decision.Verb == ApprovalVerb.AllowOnce ? decision.At : (DateTimeOffset?)null;
        return Mint(requester, requested, requiresVisibleUi: true, expiresAt);
    }

    private CapabilityGrant? Mint(
        Actor grantee, InteractionCapability requested, bool requiresVisibleUi, DateTimeOffset? expiresAt)
    {
        var grant = new CapabilityGrant(
            NewId(), grantee, SystemActor, requested, expiresAt, requiresVisibleUi);
        if (BeforeGrant is { } veto && !veto(grant))
        {
            return null;
        }
        GrantMinted?.Invoke(grant);
        return grant;
    }

    private static string NewId() => Guid.NewGuid().ToString("n");
}
