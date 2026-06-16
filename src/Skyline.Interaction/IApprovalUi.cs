// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>
/// The seam between the decision model and however a person answers, so the
/// shell never knows which UI is in front of the user.
/// </summary>
public interface IApprovalUi
{
    Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct = default);
}
