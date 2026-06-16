// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// Pure data handed to an <see cref="IApprovalUi"/>. No pixels and no threads:
/// the question to put in front of a person, plus a deadline after which an
/// unanswered request denies itself.
/// </summary>
public sealed record ApprovalRequest(
    string Id,
    Actor Requester,
    InteractionCapability Requested,
    string Prompt,
    DateTimeOffset? ExpiresAt,
    bool RequiresVisibleUi);

/// <summary>The three answers a person can give a prompt.</summary>
public enum ApprovalVerb { Allow, AllowOnce, Deny }

/// <summary>
/// One answer to an <see cref="ApprovalRequest"/>: the verb, the consent kind
/// it implies, and when it was given.
/// </summary>
public readonly record struct ApprovalDecision(ApprovalVerb Verb, ConsentKind Kind, DateTimeOffset At)
{
    public static ApprovalDecision Allow(DateTimeOffset at) => new(ApprovalVerb.Allow, ConsentKind.PromptAccepted, at);
    public static ApprovalDecision AllowOnce(DateTimeOffset at) => new(ApprovalVerb.AllowOnce, ConsentKind.UserGesture, at);
    public static ApprovalDecision Deny(DateTimeOffset at) => new(ApprovalVerb.Deny, ConsentKind.UserGesture, at);
}
