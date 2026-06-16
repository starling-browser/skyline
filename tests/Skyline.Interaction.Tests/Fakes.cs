namespace Skyline.Interaction.Tests;

/// <summary>An approvals UI that returns a preset decision and remembers the last request.</summary>
internal sealed class StubApprovalUi(ApprovalDecision decision) : IApprovalUi
{
    public ApprovalRequest? Last { get; private set; }

    public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        Last = request;
        return Task.FromResult(decision);
    }
}

/// <summary>A pointer sink that counts and records the last down position.</summary>
internal sealed class CountingSink : IPointerApprovalSink
{
    public int Count { get; private set; }
    public float X { get; private set; }
    public float Y { get; private set; }

    public void OnPointerDown(float x, float y)
    {
        Count++;
        X = x;
        Y = y;
    }
}

/// <summary>A clipboard that drops every write, standing in for an empty or unavailable OS clipboard.</summary>
internal sealed class EmptyClipboard : IClipboard
{
    public string? Text
    {
        get => null;
        set { }
    }
}

internal static class Actors
{
    public static readonly Actor LocalHuman = new("h", "Human", ActorKind.Human, ActorLocality.Local);
    public static readonly Actor RemoteHuman = new("r", "Remote", ActorKind.Human, ActorLocality.Remote);
    public static readonly Actor Planner = new("ai", "Planner", ActorKind.Ai, ActorLocality.Local);
    public static readonly Actor Robot = new("auto", "Robot", ActorKind.Automation, ActorLocality.Local);
    public static readonly Actor Os = new("sys", "System", ActorKind.System, ActorLocality.Local);
}
