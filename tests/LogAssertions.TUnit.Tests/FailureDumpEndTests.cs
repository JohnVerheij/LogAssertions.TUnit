using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;
using TUnit.Core.Enums;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The end of an attempt with a dump armed: a timed-out attempt writes its dump (through the assembly's
/// <see cref="LogAssertionsTestLifetimeAttribute"/>, retried so the next attempt can read it), the result states that
/// write it, an arm that races the end is either written by that end or refused, and a dump larger than TUnit keeps of
/// a test's output still ends with its summary in the report.
/// </summary>
[Timeout(15_000)]
internal sealed class FailureDumpEndTests
{
    private const string Heading = "Captured records since the dump was armed";

    /// <summary>Attempt 1 arms a dump, logs, and runs into its timeout; the attribute writes the dump. Attempt 2 finds
    /// it in the test's output.</summary>
    [Test]
    [Retry(1)]
    [Timeout(1_000)]
    public async Task TimedOutAttempt_WritesItsDump(CancellationToken cancellationToken)
    {
        var attempt = TestContext.Current!.Execution.CurrentRetryAttempt + 1;
        var collector = new FakeLogCollector();
        collector.DumpToTestOutputOnFailure();
        DirectLog.Write(new FakeLogger(collector, "Timeout"), $"record of attempt {attempt}");
        if (attempt == 1)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        var output = TestContext.Current.GetStandardOutput();
        await Assert.That(output).Contains("Captured records of attempt 1 since the dump was armed (1 of 1 total, times in UTC):", StringComparison.Ordinal);
        await Assert.That(output).Contains("Timeout: record of attempt 1", StringComparison.Ordinal);
    }

    /// <summary>500 rounds, each on a table of its own: one thread arms a dump while another ends the attempt. Every
    /// arm that returns is written by that end, exactly once, and every other arm is refused because the test had
    /// ended. The running test has no result yet, so the table treats it as not passed and writes the dumps.</summary>
    [Test]
    public async Task ArmRacingTheEnd_IsWrittenOnceOrRefused(CancellationToken cancellationToken)
    {
        const int Rounds = 500;
        var test = TestContext.Current!;
        var headingsBefore = CountOf(test.GetStandardOutput(), Heading);
        var armed = 0;
        var refused = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var table = new TestLifetimeTable();
            table.MarkStarted(test);
            using var go = new ManualResetEventSlim();
            var arming = Task.Run(() => ArmAfter(go, table, test, cancellationToken), cancellationToken);
            var ending = Task.Run(
                async () =>
                {
                    go.Wait(cancellationToken);
                    await table.MarkEndedAsync(test);
                },
                cancellationToken);
            go.Set();
            if (await arming)
                armed++;
            else
                refused++;
            await ending;
        }

        test.Output.WriteLine($"Arm racing the end: {armed} armed and written, {refused} refused.");
        await Assert.That(armed + refused).IsEqualTo(Rounds);
        await Assert.That(CountOf(test.GetStandardOutput(), Heading) - headingsBefore).IsEqualTo(armed);
    }

    /// <summary>A dump of 14,000 records (about 1.1 million characters) exceeds what TUnit keeps of a test's output:
    /// past 1,048,576 characters it keeps the latest 786,432, so the report loses the dump's start (its heading and
    /// first records). The dump still writes every record, and its footer repeats the summary, so the report still
    /// says what the dump holds.</summary>
    [Test]
    public async Task DumpLargerThanTUnitKeeps_ReportStillEndsWithItsSummary(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        const int Records = 14_000;
        var test = TestContext.Current!;
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Soak");
        var table = new TestLifetimeTable();
        table.MarkStarted(test);
        table.Arm(test, collector, DumpVerbosity.Compact);
        for (var i = 0; i < Records; i++)
            DirectLog.Write(logger, $"soak record {i:D5} with a payload of about the length of a real log line");

        await table.MarkEndedAsync(test);

        var output = test.GetStandardOutput();
        await Assert.That(output).DoesNotContain("soak record 00000", StringComparison.Ordinal);
        await Assert.That(output).EndsWith(
            $"soak record {Records - 1:D5} with a payload of about the length of a real log line"
                + Environment.NewLine + $"End of dump: {Records} of {Records} records." + Environment.NewLine,
            StringComparison.Ordinal);
    }

    /// <summary>The end of an attempt writes its armed dump for every result state except passed and skipped, and when
    /// the attempt has no result: failed, timed out, canceled, and the states before a result.</summary>
    [Test]
    public async Task EndOfAttempt_WritesTheDump_UnlessItPassedOrWasSkipped(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var test = TestContext.Current!;
        TestState?[] states = [null, .. Enum.GetValues<TestState>().Select(static state => (TestState?)state)];

        using (Assert.Multiple())
        {
            foreach (var state in states)
            {
                var marker = $"record of an attempt ended as {state?.ToString() ?? "no result"}";
                var collector = new FakeLogCollector();
                var entry = new TestLifetimeEntry();
                entry.Start();
                await Assert.That(entry.TryArm(collector, DumpVerbosity.Compact)).IsTrue();
                DirectLog.Write(new FakeLogger(collector, "State"), marker);

                await entry.EndAsync(test, state);

                var written = test.GetStandardOutput().Contains(marker, StringComparison.Ordinal);
                await Assert.That(written).IsEqualTo(state is not (TestState.Passed or TestState.Skipped)).Because(marker);
            }
        }
    }

    /// <summary>Waits for <paramref name="go"/>, then arms a dump in <paramref name="table"/>.</summary>
    /// <param name="go">Released when both sides of the race are ready.</param>
    /// <param name="table">The table.</param>
    /// <param name="test">The test.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns><see langword="true"/> when the dump was armed; <see langword="false"/> when it was refused because
    /// the test had ended.</returns>
    private static bool ArmAfter(ManualResetEventSlim go, TestLifetimeTable table, TestContext test, CancellationToken cancellationToken)
    {
        go.Wait(cancellationToken);
        try
        {
            table.Arm(test, new FakeLogCollector(), DumpVerbosity.Compact);
            return true;
        }
        catch (InvalidOperationException exception) when (string.Equals(exception.Message, TestLifetimeTable.EndedTestMessage, StringComparison.Ordinal))
        {
            return false;
        }
    }

    /// <summary>Counts the occurrences of <paramref name="value"/> in <paramref name="text"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The value to count.</param>
    /// <returns>The count.</returns>
    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
