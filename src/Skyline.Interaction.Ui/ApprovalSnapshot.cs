// SPDX-License-Identifier: Apache-2.0
namespace Skyline.Interaction.Ui;

/// <summary>
/// An immutable picture of what the overlay should show: the pending requests
/// in first-in-first-out order, the live grants that keep the persistent
/// indicator lit, and the decision toasts that fade out after an answer. The
/// render thread reads one of these off a volatile field and never mutates it.
/// </summary>
public sealed record ApprovalSnapshot(
    IReadOnlyList<PendingApproval> Pending,
    IReadOnlyList<CapabilityGrant> LiveVisibleGrants,
    IReadOnlyList<DecisionToast> Toasts)
{
    /// <summary>True while any visible grant lives, for a host that wants to surface held capabilities.</summary>
    public bool IndicatorActive => LiveVisibleGrants.Count > 0;

    /// <summary>True while a request waits on an answer — the modal is up.</summary>
    public bool HasModal => Pending.Count > 0;

    /// <summary>True while a decision is still fading out — the overlay keeps drawing for it.</summary>
    public bool HasToasts => Toasts.Count > 0;

    public static readonly ApprovalSnapshot Empty = new([], [], []);
}

/// <summary>
/// A pending request paired with the moment it was raised. The overlay needs
/// the start time to draw a countdown meter that runs the full window down,
/// not just the seconds left.
/// </summary>
public sealed record PendingApproval(ApprovalRequest Request, DateTimeOffset CreatedAt);

/// <summary>Which decision a toast reports, so it can color and word itself.</summary>
public enum ToastKind { Allowed, AllowedOnce, Denied }

/// <summary>
/// A short-lived banner that confirms an answer — "ALLOWED", "DENIED" — and
/// who it was for. <see cref="StartedAt"/> drives its slide-in and fade-out.
/// </summary>
public sealed record DecisionToast(ToastKind Kind, string Title, string Subtitle, DateTimeOffset StartedAt);
