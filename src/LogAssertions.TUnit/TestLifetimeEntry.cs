using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;
using TUnit.Core.Enums;

namespace LogAssertions.TUnit;

/// <summary>
/// The lifetime of one test as <see cref="LogAssertionsTestLifetimeAttribute"/> reports it: whether an attempt is
/// running, which attempt it is, and the failure dumps armed for that attempt.
/// </summary>
internal sealed class TestLifetimeEntry
{
    private readonly Lock _gate = new();
    private readonly List<ArmedFailureDump> _armed = [];
    private volatile bool _running;
    private int _attempt;

    /// <summary>Gets a value indicating whether an attempt of the test is running.</summary>
    internal bool IsRunning => _running;

    /// <summary>Gets the lock that the start, the end, arming a dump, and a teed write take.</summary>
    internal Lock Gate => _gate;

    /// <summary>Starts an attempt: it runs, and the dumps armed in an earlier attempt are dropped.</summary>
    internal void Start()
    {
        lock (_gate)
        {
            _attempt++;
            _armed.Clear();
            _running = true;
        }
    }

    /// <summary>Arms a dump of <paramref name="collector"/> for the running attempt, bookmarked at its latest record.
    /// A second call for the same collector in the same attempt changes nothing. The check that an attempt runs and
    /// the arming are one step, so a dump armed while the attempt ends is either written by that end or refused.</summary>
    /// <param name="collector">The collector to dump.</param>
    /// <param name="verbosity">How much detail to write per record.</param>
    /// <returns><see langword="false"/> when no attempt is running.</returns>
    internal bool TryArm(FakeLogCollector collector, DumpVerbosity verbosity)
    {
        var snapshot = collector.GetSnapshot();
        var bookmark = snapshot.Count > 0 ? snapshot[^1] : null;
        lock (_gate)
        {
            if (!_running)
            {
                return false;
            }

            if (!_armed.Exists(dump => ReferenceEquals(dump.Collector, collector)))
            {
                _armed.Add(new ArmedFailureDump(collector, bookmark, verbosity));
            }

            return true;
        }
    }

    /// <summary>Writes <paramref name="text"/> as one line to the output of <paramref name="context"/> while an attempt
    /// runs. The check and the write are one step under the lock <see cref="EndAsync"/> takes to mark the test ended,
    /// so a record whose test ends while it is being formatted is refused, never written into the ended test.</summary>
    /// <param name="context">The test.</param>
    /// <param name="text">The formatted record.</param>
    /// <returns><see langword="false"/> when no attempt is running.</returns>
    internal bool TryWriteLine(TestContext context, string text)
    {
        lock (_gate)
        {
            if (!_running)
            {
                return false;
            }

            // The writer is fetched only when a record is written: TUnit allocates on each access.
            context.Output.StandardOutput.WriteLine(text);
            return true;
        }
    }

    /// <summary>Ends the running attempt: marks the test ended, then writes the attempt's armed dumps to the test's
    /// output when it did not pass (it failed, timed out, or was canceled; a skipped test does not count).</summary>
    /// <param name="context">The test.</param>
    /// <param name="state">The state of the attempt's result, or <see langword="null"/> when it has none.</param>
    /// <returns>A task that completes when the dumps are written.</returns>
    internal async ValueTask EndAsync(TestContext context, TestState? state)
    {
        ArmedFailureDump[] armed;
        int attempt;
        lock (_gate)
        {
            _running = false;
            armed = [.. _armed];
            _armed.Clear();
            attempt = _attempt;
        }

        if (armed.Length > 0 && state is not (TestState.Passed or TestState.Skipped))
        {
            var withAttempt = context.Metadata.TestDetails.RetryLimit > 0;
            var writer = context.Output.StandardOutput;
            foreach (var dump in armed)
            {
                await dump.WriteToAsync(writer, withAttempt ? attempt : null).ConfigureAwait(false);
            }
        }
    }
}
