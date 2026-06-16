// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>A request still waiting on an answer.</summary>
public sealed record Pending(ApprovalRequest Request);

/// <summary>An allowed request: the grant it minted and the consent behind it.</summary>
public sealed record Granted(CapabilityGrant Grant, ConsentRecord Consent);

/// <summary>A refused request and the consent record of the refusal.</summary>
public sealed record Denied(ConsentRecord Consent);

/// <summary>A request that hit its deadline before anyone answered.</summary>
public sealed record Expired(string RequestId);

/// <summary>Where one approval stands, as a closed set of four cases. Branch on it.</summary>
public union ApprovalState(Pending, Granted, Denied, Expired);
