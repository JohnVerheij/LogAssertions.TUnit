using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Two tests, each with its own teed capture, a tee they share (built with no owning test), and an armed dump: each
/// test's output holds only its own records. When the scheduler runs the two cases at the same time they meet and log
/// interleaved; a case that runs alone (one test at a time, one case filtered in, a retry) waits a moment for the
/// other and then logs anyway, and the check means the same. Nothing can deadlock: the meeting has a bound, and its
/// state belongs to the class's run, not to the process.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class ParallelRoutingTests
{
    /// <summary>How long a case waits for the other one before it logs on its own.</summary>
    private static readonly TimeSpan MeetingBound = TimeSpan.FromSeconds(2);

    /// <summary>The meeting point of the two cases, one per run of the class.</summary>
    private static readonly ConditionalWeakTable<ClassHookContext, Meeting> Meetings = new();

    /// <summary>Both cases meet if they run together, then log interleaved, then end; neither output holds a record of
    /// the other.</summary>
    /// <param name="name">The case's marker.</param>
    /// <param name="other">The other case's marker.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the test has run.</returns>
    [Test]
    [Arguments("ALPHA", "BRAVO", Categories = ["ParallelRouting.Alpha"])]
    [Arguments("BRAVO", "ALPHA", Categories = ["ParallelRouting.Bravo"])]
    public async Task ParallelTests_EachOutputHoldsOnlyItsOwnRecords(string name, string other, CancellationToken cancellationToken)
    {
        var test = TestContext.Current!;
        var meeting = Meetings.GetValue(test.ClassContext, static _ => new Meeting());
        using var own = TestOutputLogCollectorBuilder.CreateTeed();
        var ownLogger = own.CreateLogger("Own");
        var sharedLogger = meeting.SharedTee.CreateLogger("Shared");
        var table = new TestLifetimeTable();
        table.MarkStarted(test);
        table.Arm(test, own.Collector, DumpVerbosity.Compact);

        await meeting.ArriveAsync(MeetingBound, cancellationToken);
        for (var i = 0; i < 50; i++)
        {
            DirectLog.Write(ownLogger, $"{name}-own-{i}");
            DirectLog.Write(sharedLogger, $"{name}-shared-{i}");
            await Task.Yield();
        }

        await table.MarkEndedAsync(test);

        var output = test.GetStandardOutput();
        await Assert.That(output).Contains($"Own: {name}-own-49", StringComparison.Ordinal);
        await Assert.That(output).Contains($"Shared: {name}-shared-49", StringComparison.Ordinal);
        await Assert.That(output).Contains("(50 of 50 total, times in UTC)", StringComparison.Ordinal);
        await Assert.That(output).DoesNotContain(other, StringComparison.Ordinal);
    }

    /// <summary>Disposes the meeting of this run of the class, and with it the tee the two cases shared.</summary>
    /// <param name="context">The run of the class.</param>
    /// <param name="cancellationToken">The hook's token.</param>
    [After(Class)]
    public static void DisposeMeeting(ClassHookContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Meetings.TryGetValue(context, out var meeting))
            meeting.Dispose();
    }

    /// <summary>Where the two cases of one run meet, and the tee they share.</summary>
    private sealed class Meeting : IDisposable
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        /// <summary>Gets the tee both cases log through, built with no owning test.</summary>
        public TestOutputLoggerProvider SharedTee { get; } = new(owner: null);

        /// <summary>Marks a case as arrived and waits, at most <paramref name="bound"/>, until the other one has.</summary>
        /// <param name="bound">The longest wait.</param>
        /// <param name="cancellationToken">The test's token.</param>
        /// <returns>A task that completes when the other case has arrived, or when the bound has passed.</returns>
        public async Task ArriveAsync(TimeSpan bound, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) >= 2)
                _both.TrySetResult();
            try
            {
                await _both.Task.WaitAsync(bound, cancellationToken);
            }
            catch (TimeoutException)
            {
                // The other case is not running at the same time: log on alone.
            }
        }

        /// <inheritdoc/>
        public void Dispose() => SharedTee.Dispose();
    }
}
