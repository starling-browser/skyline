// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>What kind of thing caused an action.</summary>
public enum ActorKind { Human, Ai, Automation, System }

/// <summary>Where the actor sits relative to this process.</summary>
public enum ActorLocality { Local, Remote }

/// <summary>
/// Who or what caused an action. Every approval, grant, and consent record
/// names one. <see cref="DelegatedBy"/> links an actor back to the one that
/// handed it authority — for example an AI an operator delegated to.
/// </summary>
public sealed record Actor(
    string Id,
    string DisplayName,
    ActorKind Kind,
    ActorLocality Locality,
    Actor? DelegatedBy = null);
