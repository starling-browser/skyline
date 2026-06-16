// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>Grant the request outright, with this consent kind. No prompt.</summary>
public sealed record ImplicitGrant(ConsentKind Kind);

/// <summary>Put the request in front of a person.</summary>
public sealed record Ask;

/// <summary>Refuse the request outright. No prompt.</summary>
public sealed record Deny;

/// <summary>
/// What a policy decided for one request, as a closed set of three cases.
/// Branch on it like any union.
/// </summary>
public union PolicyOutcome(ImplicitGrant, Ask, Deny);

/// <summary>The rule that turns an actor plus a capability ask into an outcome.</summary>
public interface IApprovalPolicy
{
    PolicyOutcome Evaluate(Actor actor, InteractionCapability requested);
}

/// <summary>
/// The default policy, keyed on actor kind and locality. Overridable whole —
/// set <see cref="InProcessApprovalShell.Policy"/> to your own. The structural
/// guarantees: a remote actor can never be granted <see cref="InteractionCapability.Edit"/>
/// or <see cref="InteractionCapability.LowLevelInput"/>, and an AI can never be
/// granted <see cref="InteractionCapability.LowLevelInput"/>.
/// </summary>
public sealed class DefaultApprovalPolicy : IApprovalPolicy
{
    private const InteractionCapability Sensitive =
        InteractionCapability.Edit | InteractionCapability.LowLevelInput | InteractionCapability.Administer;

    private const InteractionCapability RemoteAllowed =
        InteractionCapability.Observe | InteractionCapability.Point |
        InteractionCapability.Select | InteractionCapability.Collaborate;

    public PolicyOutcome Evaluate(Actor actor, InteractionCapability requested)
    {
        if (actor.Kind == ActorKind.System)
        {
            return new ImplicitGrant(ConsentKind.SystemAllowed);
        }

        // A remote actor can never reach Edit or the keyboard, whatever its
        // kind. This gates every non-system actor, not just remote humans, so a
        // remote AI or automation can't be prompted into it either.
        if (actor.Locality == ActorLocality.Remote
            && (requested & (InteractionCapability.Edit | InteractionCapability.LowLevelInput)) != 0)
        {
            return new Deny();
        }

        if (actor.Kind == ActorKind.Human)
        {
            if (actor.Locality == ActorLocality.Remote)
            {
                if ((requested & ~RemoteAllowed) != 0)
                {
                    return new Ask();
                }
                return new ImplicitGrant(ConsentKind.PolicyAllowed);
            }

            return (requested & Sensitive) != 0
                ? new Ask()
                : new ImplicitGrant(ConsentKind.UserGesture);
        }

        // AI or automation always asks, and an AI is hard-denied the keyboard.
        if (actor.Kind == ActorKind.Ai && (requested & InteractionCapability.LowLevelInput) != 0)
        {
            return new Deny();
        }
        return new Ask();
    }
}
