using System;
using System.Threading;
using System.Threading.Tasks;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Two tests run at the same time, each with its own teed capture, a tee they share (built with no owning test), and
/// an armed dump: each test's output holds only its own records.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class ParallelRoutingTests
{
    private static readonly TestOutputLoggerProvider SharedTee = new(owner: null);
    private static readonly TaskCompletionSource BothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int s_started;

    /// <summary>Both cases meet, then log interleaved, then end; neither output holds a record of the other.</summary>
    /// <param name="name">The case's marker.</param>
    /// <param name="other">The other case's marker.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the test has run.</returns>
    [Test]
    [Arguments("ALPHA", "BRAVO")]
    [Arguments("BRAVO", "ALPHA")]
    public async Task ParallelTests_EachOutputHoldsOnlyItsOwnRecords(string name, string other, CancellationToken cancellationToken)
    {
        var test = TestContext.Current!;
        using var own = TestOutputLogCollectorBuilder.CreateTeed();
        var ownLogger = own.CreateLogger("Own");
        var sharedLogger = SharedTee.CreateLogger("Shared");
        var table = new TestLifetimeTable();
        table.MarkStarted(test);
        table.Arm(test, own.Collector, DumpVerbosity.Compact);

        if (Interlocked.Increment(ref s_started) is 2)
            BothStarted.SetResult();
        await BothStarted.Task.WaitAsync(cancellationToken);

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
}
