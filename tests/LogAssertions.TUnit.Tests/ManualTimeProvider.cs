using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LogAssertions.TUnit.Tests;

/// <summary>A clock the test sets by hand, whose timers fire only when <see cref="Advance"/> moves the clock to or past
/// their due time. Used for record timestamps (through <c>FakeLogCollectorOptions.TimeProvider</c>) and for the
/// <c>WillLog</c> timeout, so no test waits on the real clock. Timers fire once; a period is ignored.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly TaskCompletionSource _timerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets or sets the current time.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Waits until the first timer is created.</summary>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when a timer exists.</returns>
    public Task WaitForTimerAsync(CancellationToken cancellationToken) => _timerCreated.Task.WaitAsync(cancellationToken);

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => UtcNow;

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        _ = timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        _timerCreated.TrySetResult();
        return timer;
    }

    /// <summary>Moves the clock forward by <paramref name="delta"/> and fires every timer that is due by then.</summary>
    /// <param name="delta">How far to move the clock.</param>
    public void Advance(TimeSpan delta)
    {
        ManualTimer[] timers;
        lock (_gate)
        {
            UtcNow += delta;
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.FireIfDue(UtcNow);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private readonly Lock _gate = new();
        private DateTimeOffset? _dueAt;
        private bool _disposed;

        public void FireIfDue(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_dueAt is not { } dueAt || dueAt > now)
                    return;
                _dueAt = null;
            }

            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                if (_disposed)
                    return false;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.UtcNow + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _dueAt = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
