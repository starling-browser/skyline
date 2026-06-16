// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction;

/// <summary>The shell a runtime exposes: the live <see cref="FocusGraph"/> and the approvals entry.</summary>
public interface IInteractionShell
{
    FocusGraph Focus { get; }

    Task<CapabilityGrant?> AuthorizeAsync(
        Actor actor, InteractionCapability capability, string prompt, CancellationToken ct = default);
}

/// <summary>
/// A surface that understands semantic commands. The shell routes an
/// authorized <see cref="CommandRequest"/> here, and the surface reports
/// whether it ran.
/// </summary>
public interface IInteractiveSurface
{
    string SurfaceId { get; }

    CommandResult Dispatch(CommandRequest request);
}
