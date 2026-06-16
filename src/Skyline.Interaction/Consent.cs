// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>How an actor came to be allowed to do something.</summary>
public enum ConsentKind
{
    UserGesture,
    PromptAccepted,
    DelegatedGrant,
    PolicyAllowed,
    SystemAllowed,
}

/// <summary>A durable note of one consent: who, how, when, and an optional reason.</summary>
public readonly record struct ConsentRecord(
    string ActorId,
    ConsentKind Kind,
    DateTimeOffset At,
    string? Reason = null);
