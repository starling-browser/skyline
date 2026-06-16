// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// A live grant of capabilities to one actor. <see cref="RequiresVisibleUi"/>
/// is set when the grant came from a visible prompt, so the host can keep a
/// persistent indicator lit while the grant lives.
/// </summary>
public sealed record CapabilityGrant(
    string Id,
    Actor Grantee,
    Actor GrantedBy,
    InteractionCapability Capabilities,
    DateTimeOffset? ExpiresAt,
    bool RequiresVisibleUi);
