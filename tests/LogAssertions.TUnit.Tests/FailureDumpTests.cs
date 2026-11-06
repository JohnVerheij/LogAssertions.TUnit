using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// <c>DumpToTestOutputOnFailure()</c>: what an armed dump writes (driven through a table of the test's own, where the
/// running test has no result yet and so counts as not passed), when it throws, and the real end of a test through the
/// assembly's <see cref="LogAssertionsTestLifetimeAttribute"/> (a retried test, a test that skips itself, a test that
/// passes).
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class FailureDumpTests
{
    private const string Heading = "Captured records since the dump was armed";

    private static int s_retryAttempts;

    /// <summary>Gets or sets the context of <see cref="A_PassingTest_ArmsADump"/>.</summary>
    private static TestContext? PassingTest { get; set; }

    /// <summary>Gets or sets the collector armed in <see cref="ArmInBeforeTest"/>.</summary>
    private FakeLogCollector? ArmedInBefore { get; set; }

    /// <summary>Arms a dump in a <c>[Before(Test)]</c> hook, as a class with a shared collector would; this must not
    /// throw (the attribute marks the test started before the hooks run).</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    [Before(Test)]
    public void ArmInBeforeTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArmedInBefore = new FakeLogCollector();
        ArmedInBefore.DumpToTestOutputOnFailure();
    }

    /// <summary>The dump writes only the records captured after the call, each with its UTC timestamp, under a
    /// heading with the counts.</summary>
    [Test]
    public async Task Dump_WritesTheRecordsSinceTheCall(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = FakeLogCollector.Create(new FakeLogCollectorOptions { TimeProvider = new FixedTimeProvider() });
        var logger = new FakeLogger(collector, "Dump");
        DirectLog.Write(logger, "before the call");

        await EndWithDumpAsync(table => table.Arm(TestContext.Current!, collector, DumpVerbosity.Default), () =>
        {
            DirectLog.Write(logger, "after the call 1", LogLevel.Warning);
            DirectLog.Write(logger, "after the call 2");
        });

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(output).Contains(Heading + " (2 of 3 total, times in UTC):", StringComparison.Ordinal);
        await Assert.That(output).Contains("  12:34:56.789 [warn] Dump: after the call 1", StringComparison.Ordinal);
        await Assert.That(output).Contains("  12:34:56.789 [info] Dump: after the call 2", StringComparison.Ordinal);
        await Assert.That(output).DoesNotContain("before the call", StringComparison.Ordinal);
    }

    /// <summary>A collector that held no record at the call is written in full; with nothing logged after the call,
    /// the dump says so.</summary>
    [Test]
    public async Task Dump_EmptyAtTheCall_WritesEverything_OrNoRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var withRecords = new FakeLogCollector();
        var withoutRecords = new FakeLogCollector();

        await EndWithDumpAsync(
            table =>
            {
                table.Arm(TestContext.Current!, withRecords, DumpVerbosity.Default);
                table.Arm(TestContext.Current!, withoutRecords, DumpVerbosity.Default);
            },
            () => DirectLog.Write(new FakeLogger(withRecords, "Empty"), "the only record"));

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(output).Contains(Heading + " (1 of 1 total, times in UTC):", StringComparison.Ordinal);
        await Assert.That(output).Contains(Heading + " (0 of 0 total, times in UTC):" + Environment.NewLine + "  (no records)", StringComparison.Ordinal);
    }

    /// <summary>When the collector is cleared after the call, the bookmark is gone and every record is written, and
    /// the heading says why.</summary>
    [Test]
    public async Task Dump_CollectorClearedAfterTheCall_WritesEverything(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Cleared");
        DirectLog.Write(logger, "before the clear");

        await EndWithDumpAsync(table => table.Arm(TestContext.Current!, collector, DumpVerbosity.Default), () =>
        {
            collector.Clear();
            DirectLog.Write(logger, "after the clear");
        });

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains(
            Heading + " (1 of 1 total, times in UTC; the collector was cleared after the dump was armed):",
            StringComparison.Ordinal);
    }

    /// <summary>The verbosity applies per record, and a second call for the same collector in the same attempt writes
    /// one dump, not two.</summary>
    [Test]
    public async Task Dump_CompactVerbosity_AndASecondCall_WriteOneHeadlineOnlyDump(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Compact");

        await EndWithDumpAsync(
            table =>
            {
                table.Arm(TestContext.Current!, collector, DumpVerbosity.Compact);
                table.Arm(TestContext.Current!, collector, DumpVerbosity.Verbose);
            },
            () => TestLogMessages.OrderForCustomer(logger, 7, "compact-customer"));

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(output).Contains("Compact: Order 7 for compact-customer", StringComparison.Ordinal);
        await Assert.That(output).DoesNotContain("props:", StringComparison.Ordinal);
        await Assert.That(output.IndexOf(Heading, StringComparison.Ordinal)).IsEqualTo(output.LastIndexOf(Heading, StringComparison.Ordinal));
    }

    /// <summary>A test that passed writes no dump.</summary>
    [Test]
    [DependsOn(nameof(A_PassingTest_ArmsADump))]
    public async Task Dump_TestThatPassed_WritesNothing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(PassingTest!.Execution.Result?.State).IsEqualTo(TestState.Passed);
        await Assert.That(PassingTest.GetStandardOutput()).DoesNotContain(Heading, StringComparison.Ordinal);

        // The same through a table of this test's own, with the passed test's context.
        var table = new TestLifetimeTable();
        var collector = new FakeLogCollector();
        table.MarkStarted(PassingTest);
        table.Arm(PassingTest, collector, DumpVerbosity.Default);
        DirectLog.Write(new FakeLogger(collector, "Passed"), "logged by a passed test");
        await table.MarkEndedAsync(PassingTest);

        await Assert.That(PassingTest.GetStandardOutput()).DoesNotContain("logged by a passed test", StringComparison.Ordinal);
    }

    /// <summary>Arms a dump through the public method and passes; <see cref="Dump_TestThatPassed_WritesNothing"/>
    /// checks its output afterwards.</summary>
    [Test]
    public async Task A_PassingTest_ArmsADump(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PassingTest = TestContext.Current;
        var collector = new FakeLogCollector();
        collector.DumpToTestOutputOnFailure(DumpVerbosity.Verbose);
        DirectLog.Write(new FakeLogger(collector, "Passing"), "a record of a passing test");

        await Assert.That(collector.Count).IsEqualTo(1);
    }

    /// <summary>A retried test arms again in each attempt. Attempt 1 fails, so the attribute writes its records with
    /// the attempt in the heading; attempt 2 sees that dump in the test's output and passes.</summary>
    [Test]
    [Retry(2)]
    [NotInParallel(nameof(FailureDumpTests) + ".Retry")]
    public async Task Dump_RetriedTest_WritesTheFailedAttempt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = Interlocked.Increment(ref s_retryAttempts);
        var collector = new FakeLogCollector();
        collector.DumpToTestOutputOnFailure();
        DirectLog.Write(new FakeLogger(collector, "Retry"), $"record of attempt {attempt}");

        await Assert.That(attempt).IsGreaterThan(1);

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(output).Contains("Captured records of attempt 1 since the dump was armed (1 of 1 total, times in UTC):", StringComparison.Ordinal);
        await Assert.That(output).Contains("Retry: record of attempt 1", StringComparison.Ordinal);
    }

    /// <summary>A test that skips itself after arming a dump writes no dump: skipping is not a failure.</summary>
    [Test]
    /// <param name="cancellationToken">The test's token.</param>
    public void Dump_TestThatSkipsItself_WritesNothing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        collector.DumpToTestOutputOnFailure();
        DirectLog.Write(new FakeLogger(collector, "Skip"), "record of a skipped test");
        Skip.Test("Pins that a dump armed in a test that skips itself is not written.");
    }

    /// <summary>Checks the output of <see cref="Dump_TestThatSkipsItself_WritesNothing"/> after it was skipped.</summary>
    [Test]
    [DependsOn(nameof(Dump_TestThatSkipsItself_WritesNothing), ProceedOnFailure = true)]
    public async Task Dump_SkippedTestOutput_HasNoDump(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var skipped = TestContext.Current!.Dependencies.GetTests(nameof(Dump_TestThatSkipsItself_WritesNothing), typeof(FailureDumpTests));

        await Assert.That(skipped).Count().IsEqualTo(1);
        await Assert.That(skipped[0].Execution.Result?.State).IsEqualTo(TestState.Skipped);
        await Assert.That(skipped[0].GetStandardOutput()).DoesNotContain(Heading, StringComparison.Ordinal);
    }

    /// <summary>Without the attribute (a table that never saw the test start) the call throws with the line to add;
    /// after the test has ended it throws too.</summary>
    [Test]
    public async Task Arm_WithoutTheAttribute_OrAfterTheEnd_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TestLifetimeTable();
        var test = TestContext.Current!;
        var collector = new FakeLogCollector();

        var missing = await Assert.That(() => table.Arm(test, collector, DumpVerbosity.Default)).Throws<InvalidOperationException>();
        await Assert.That(missing!.Message).Contains("[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]", StringComparison.Ordinal);

        table.MarkStarted(test);
        await table.MarkEndedAsync(test);
        var ended = await Assert.That(() => table.Arm(test, collector, DumpVerbosity.Default)).Throws<InvalidOperationException>();
        await Assert.That(ended!.Message).Contains("after the current test had ended", StringComparison.Ordinal);
    }

    /// <summary>A new attempt starts with no dump armed: a dump armed in an attempt that never reported its end is
    /// dropped, not written at the end of the next one.</summary>
    [Test]
    public async Task Dump_NewAttempt_DropsTheDumpsOfTheEarlierOne(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TestLifetimeTable();
        var test = TestContext.Current!;
        var earlier = new FakeLogCollector();

        table.MarkStarted(test);
        table.Arm(test, earlier, DumpVerbosity.Default);
        table.MarkStarted(test);
        DirectLog.Write(new FakeLogger(earlier, "Earlier"), "record of the earlier attempt");
        await table.MarkEndedAsync(test);

        await Assert.That(test.GetStandardOutput()).DoesNotContain("record of the earlier attempt", StringComparison.Ordinal);
    }

    /// <summary>The public method rejects a <see langword="null"/> collector and a call where no test is current.</summary>
    [Test]
    public async Task DumpToTestOutputOnFailure_InvalidCalls_Throw(CancellationToken cancellationToken)
    {
        await Assert.That(() => ((FakeLogCollector)null!).DumpToTestOutputOnFailure()).Throws<ArgumentNullException>();

        Exception? offContext = null;
        await OffContext.RunAsync(
            () =>
            {
                try
                {
                    new FakeLogCollector().DumpToTestOutputOnFailure();
                }
                catch (InvalidOperationException ex)
                {
                    offContext = ex;
                }
            },
            cancellationToken);

        await Assert.That(offContext).IsNotNull();
        await Assert.That(offContext!.Message).Contains("requires an active TUnit test context", StringComparison.Ordinal);
    }

    /// <summary>The dump armed in <see cref="ArmInBeforeTest"/> is part of this test: a record logged here would be
    /// written if the test failed. Here the test passes, so the attribute writes nothing for it.</summary>
    [Test]
    public async Task ArmedInBeforeTest_DoesNotThrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectLog.Write(new FakeLogger(ArmedInBefore!, "Before"), "record after the hook");

        await Assert.That(ArmedInBefore!.Count).IsEqualTo(1);
    }

    /// <summary>Starts a table of this test's own, runs <paramref name="arm"/> and <paramref name="log"/>, and ends the
    /// test in the table. The running test has no result yet, so the table treats it as not passed and writes the
    /// armed dumps to this test's output.</summary>
    /// <param name="arm">Arms the dumps.</param>
    /// <param name="log">Logs the records after the dumps were armed.</param>
    /// <returns>A task that completes when the dumps are written.</returns>
    private static async Task EndWithDumpAsync(Action<TestLifetimeTable> arm, Action log)
    {
        var table = new TestLifetimeTable();
        table.MarkStarted(TestContext.Current!);
        arm(table);
        log();
        await table.MarkEndedAsync(TestContext.Current!);
    }

    /// <summary>A clock fixed at 12:34:56.789 UTC, for timestamps in the dump.</summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 6, 12, 34, 56, 789, TimeSpan.Zero);
    }
}
