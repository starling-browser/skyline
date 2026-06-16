// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction.Ui;

/// <summary>
/// Where a pointer-down lands for the approvals UI. The GPU-free
/// <see cref="ApprovalSurfaceState"/> and the <see cref="ApprovalsOverlay"/>
/// that wraps it both implement it, so an input source can route gestures to
/// either without referencing the GPU.
/// </summary>
public interface IPointerApprovalSink
{
    void OnPointerDown(float x, float y);
}
