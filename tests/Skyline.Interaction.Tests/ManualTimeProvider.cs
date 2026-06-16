namespace Skyline.Interaction.Tests;

/// <summary>
/// A clock the test drives by hand. <see cref="Advance"/> moves time forward
/// and fires every timer that came due, so deadline and expiry paths run by
/// direct call instead of by racing a wall clock.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by)
    {
        ManualTimer[] due;
        lock (_gate)
        {
            _now += by;
            due = _timers.FindAll(t => t.DueAt <= _now).ToArray();
            foreach (var t in due)
            {
                _timers.Remove(t);
            }
        }
        foreach (var t in due)
        {
            t.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, _now + dueTime);
        lock (_gate)
        {
            _timers.Add(timer);
        }
        return timer;
    }

    internal void Forget(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    internal sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset dueAt) : ITimer
    {
        public DateTimeOffset DueAt { get; } = dueAt;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => owner.Forget(this);

        public ValueTask DisposeAsync()
        {
            owner.Forget(this);
            return ValueTask.CompletedTask;
        }
    }
}
