namespace SchoolCollab.Assignments.Tests.Unit;

/// <summary>
/// Hand-rolled <see cref="TimeProvider"/> test double for the authoring page's 500 ms trailing
/// recipient-preview debounce (TGT-16 / D-4). Hand-rolled on purpose: the
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> package would require a
/// <c>Directory.Packages.props</c> edit and is therefore out of scope.
///
/// <para>Timers created through <see cref="CreateTimer"/> stay pending until
/// <see cref="Advance"/> moves the clock past their due time — so a debounce that has been
/// cancelled can never fire, and the number of preview reads a burst of changes produces is
/// observable with no wall-clock wait.</para>
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    /// <summary>The authoring page's trailing preview debounce (<c>AssignmentAuthoring.razor</c>
    /// <c>PreviewDebounce</c>, which is private), restated here so a test never re-spells the
    /// 500 ms and cannot silently drift from the component.</summary>
    public static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _utcNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The timers waiting for the clock — the authoring page's armed trailing
    /// debounce(s): a coalescing surface leaves exactly one armed after a burst of changes.</summary>
    internal int PendingTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count;
            }
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    /// <summary>Moves the clock forward by <paramref name="by"/> and fires every pending timer
    /// whose due time has been reached (the callbacks run outside the lock, after the timers are
    /// unregistered, so a callback may safely register a new timer).</summary>
    public void Advance(TimeSpan by)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _utcNow += by;
            due = _timers.Where(t => t.DueAt <= _utcNow).ToList();
            _timers.RemoveAll(due.Contains);
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new FakeTimer(this, callback, state);
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            Change(timer, dueTime);
        }

        return timer;
    }

    /// <summary>Registers (or re-registers) <paramref name="timer"/> for its new due time; an
    /// infinite due time leaves the timer live but never-firing, per the <see cref="ITimer"/>
    /// contract. Periods are ignored — the only timer under test is the one-shot
    /// <c>Task.Delay</c> debounce.</summary>
    internal void Change(FakeTimer timer, TimeSpan dueTime)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            timer.DueAt = _utcNow + (dueTime < TimeSpan.Zero ? TimeSpan.Zero : dueTime);
            _timers.Add(timer);
        }
    }

    internal void Remove(FakeTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    internal sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;

        internal DateTimeOffset DueAt { get; set; }

        /// <summary>Runs the registered callback; a timer cancelled (disposed) before the clock
        /// reached it never calls back — which is exactly how the debounce's stale cancellation
        /// is observable.</summary>
        internal void Fire()
        {
            if (_disposed)
            {
                return;
            }

            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed)
            {
                return false;
            }

            owner.Change(this, dueTime);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
