using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Routing of the built-in test-output tee: the three rules of <see cref="TestLifetimeTable.ResolveTeeTarget"/> (with
/// the shared table the assembly's <see cref="LogAssertionsTestLifetimeAttribute"/> feeds, and with a table of their
/// own for the end check and for a test the attribute does not track), the skipped-record count, the logger members,
/// and the factories that register the tee.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class TeeRoutingTests
{
    /// <summary>Gets or sets the context of <see cref="A_StoresItsContext"/>, which has a result by the time a test
    /// that depends on it runs.</summary>
    private static TestContext? FinishedTest { get; set; }

    /// <summary>Rule 1: a record logged on the test's own flow goes to the test.</summary>
    [Test]
    public async Task Rule1_RecordOnTheTestsFlow_GoesToTheTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var provider = new TestOutputLoggerProvider(owner: null);

        DirectLog.Write(provider.CreateLogger("Rule1"), "rule-1 record");

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("[Information] Rule1: rule-1 record", StringComparison.Ordinal);
        await Assert.That(provider.SkippedRecordCount).IsEqualTo(0);
    }

    /// <summary>Rule 2: a record logged where no test is current goes to the owning test while it runs.</summary>
    [Test]
    public async Task Rule2_RecordWithNoTestContext_GoesToTheRunningOwner(CancellationToken cancellationToken)
    {
        using var provider = new TestOutputLoggerProvider(TestContext.Current);
        var logger = provider.CreateLogger("Rule2");

        await OffContext.RunAsync(() => DirectLog.Write(logger, "rule-2 record"), cancellationToken);

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("Rule2: rule-2 record", StringComparison.Ordinal);
        await Assert.That(provider.SkippedRecordCount).IsEqualTo(0);
    }

    /// <summary>Rule 3: a record logged where no test is current, by a tee without an owner, reaches no test and is
    /// counted.</summary>
    [Test]
    public async Task Rule3_RecordWithNoTestAndNoOwner_IsCountedNotWritten(CancellationToken cancellationToken)
    {
        using var provider = new TestOutputLoggerProvider(owner: null);
        var logger = provider.CreateLogger("Rule3");

        await OffContext.RunAsync(() => DirectLog.Write(logger, "rule-3 record"), cancellationToken);

        await Assert.That(provider.SkippedRecordCount).IsEqualTo(1);
        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain("rule-3 record", StringComparison.Ordinal);
    }

    /// <summary>With the attribute's start and end: an ended test gets no record, neither on its own flow (rule 1) nor
    /// as owner (rule 2), and a restarted test (the next attempt of a retry) gets them again.</summary>
    [Test]
    public async Task Tracked_EndedTest_GetsNoRecord_UntilItStartsAgain(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TestLifetimeTable();
        var test = TestContext.Current!;

        table.MarkStarted(test);
        await Assert.That(table.ResolveTeeTarget(test, owner: null)).IsSameReferenceAs(test);
        await Assert.That(table.ResolveTeeTarget(current: null, test)).IsSameReferenceAs(test);

        await table.MarkEndedAsync(test);
        await Assert.That(table.ResolveTeeTarget(test, owner: null)).IsNull();
        await Assert.That(table.ResolveTeeTarget(current: null, test)).IsNull();

        table.MarkStarted(test);
        await Assert.That(table.ResolveTeeTarget(test, owner: null)).IsSameReferenceAs(test);
        await Assert.That(table.ResolveTeeTarget(current: null, test)).IsSameReferenceAs(test);
    }

    /// <summary>Without the attribute (a table that never saw the test start): a record on the test's own flow always
    /// goes to that test, and ending a test the table never saw start changes nothing.</summary>
    [Test]
    public async Task Untracked_TestOnItsOwnFlow_AlwaysGetsTheRecord(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TestLifetimeTable();
        var test = TestContext.Current!;

        await table.MarkEndedAsync(test);

        await Assert.That(table.ResolveTeeTarget(test, owner: null)).IsSameReferenceAs(test);
        await Assert.That(table.ResolveTeeTarget(current: null, owner: null)).IsNull();
    }

    /// <summary>Stores this test's context for <see cref="Untracked_Owner_GetsRecordsUntilItHasAResult"/>.</summary>
    [Test]
    public async Task A_StoresItsContext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FinishedTest = TestContext.Current;
        await Assert.That(FinishedTest).IsNotNull();
    }

    /// <summary>Without the attribute, rule 2 ends when the owner has a result: the running test still gets records,
    /// a finished one does not.</summary>
    [Test]
    [DependsOn(nameof(A_StoresItsContext))]
    public async Task Untracked_Owner_GetsRecordsUntilItHasAResult(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TestLifetimeTable();
        var running = TestContext.Current!;

        await Assert.That(table.ResolveTeeTarget(current: null, running)).IsSameReferenceAs(running);
        await Assert.That(FinishedTest!.Execution.Result).IsNotNull();
        await Assert.That(table.ResolveTeeTarget(current: null, FinishedTest)).IsNull();
    }

    /// <summary>Disposal writes the count of records that reached no test once, to the test current then.</summary>
    [Test]
    public async Task Dispose_WithSkippedRecords_WritesTheCountOnce(CancellationToken cancellationToken)
    {
        var provider = new TestOutputLoggerProvider(owner: null);
        var logger = provider.CreateLogger("Skipped");
        await OffContext.RunAsync(
            () =>
            {
                DirectLog.Write(logger, "first");
                DirectLog.Write(logger, "second");
            },
            cancellationToken);

        provider.Dispose();
        provider.Dispose();

        var output = TestContext.Current!.GetStandardOutput();
        const string line = "2 log record(s) were not teed to a test report: each was logged after its test had ended, or where no test was current.";
        await Assert.That(output).Contains(line, StringComparison.Ordinal);
        await Assert.That(output.IndexOf(line, StringComparison.Ordinal)).IsEqualTo(output.LastIndexOf(line, StringComparison.Ordinal));
    }

    /// <summary>Disposal without skipped records writes nothing.</summary>
    [Test]
    public async Task Dispose_WithoutSkippedRecords_WritesNothing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = new TestOutputLoggerProvider(TestContext.Current);
        DirectLog.Write(provider.CreateLogger("NotSkipped"), "teed");

        provider.Dispose();

        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain("were not teed", StringComparison.Ordinal);
    }

    /// <summary><c>AddTeedFakeLogging</c> registers the tee so that the factory disposes it: a factory built where no
    /// test is current has no owner, its off-context record is skipped, and disposing the factory reports it.</summary>
    [Test]
    public async Task AddTeedFakeLogging_FactoryDisposal_ReportsTheSkippedRecords(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        ILoggerFactory? factory = null;
        await OffContext.RunAsync(
            () =>
            {
                factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector));
                DirectLog.Write(factory.CreateLogger("Unowned"), "unowned record");
            },
            cancellationToken);

        factory!.Dispose();

        await Assert.That(collector).HasLogged().Containing("unowned record", StringComparison.Ordinal).Once();
        await Assert.That(TestContext.Current!.GetStandardOutput())
            .Contains("1 log record(s) were not teed to a test report", StringComparison.Ordinal);
    }

    /// <summary>The tee's logger members: enabled for every level but <see cref="LogLevel.None"/>, a shared non-null
    /// scope, the exception text after the record, and a <see langword="null"/> formatter rejected.</summary>
    [Test]
    public async Task Logger_Members_Behave(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var provider = new TestOutputLoggerProvider(TestContext.Current);
        var logger = provider.CreateLogger("Direct");

        await Assert.That(logger.IsEnabled(LogLevel.Trace)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.None)).IsFalse();
        var scope = logger.BeginScope("a-scope");
        await Assert.That(scope).IsNotNull();
        await Assert.That(logger.BeginScope(42)).IsSameReferenceAs(scope);
        scope!.Dispose();

        logger.Log(LogLevel.Warning, default, "with exception", new InvalidOperationException("tee-exception-text"), static (s, _) => s);

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("tee-exception-text", StringComparison.Ordinal);
        await Assert.That(() => logger.Log(LogLevel.Warning, default, "state", null, null!)).Throws<ArgumentNullException>();
    }

    /// <summary>The routing decision runs for every record and allocates nothing (measured over 10,000 calls after a
    /// warm-up). TUnit's output writer itself allocates on access, so the tee fetches it only to write.</summary>
    [Test]
    public async Task RoutingDecision_AllocatesNothing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var provider = new TestOutputLoggerProvider(TestContext.Current);
        for (var i = 0; i < 1_000; i++)
            _ = provider.ResolveTarget();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
            _ = provider.ResolveTarget();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }

    /// <summary>A record that reaches no test allocates nothing on the tee path (10,000 records through
    /// <see cref="ILogger.Log{TState}"/> after a warm-up), and every one is counted.</summary>
    [Test]
    public async Task SkippedRecord_AllocatesNothing(CancellationToken cancellationToken)
    {
        using var provider = new TestOutputLoggerProvider(owner: null);
        var logger = provider.CreateLogger("Skipped");
        long allocated = -1;

        await OffContext.RunAsync(
            () =>
            {
                for (var i = 0; i < 1_000; i++)
                    DirectLog.Write(logger, "warm-up");

                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 10_000; i++)
                    DirectLog.Write(logger, "skipped");
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            },
            cancellationToken);

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(provider.SkippedRecordCount).IsEqualTo(11_000);
    }

    /// <summary><c>CreateTeedDiscarding</c>: enabled from the minimum level up, the record is teed, and a scope is a
    /// shared non-null instance.</summary>
    [Test]
    public async Task CreateTeedDiscarding_EnabledFromTheMinimum_Tees(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var factory = TestOutputLogCollectorBuilder.CreateTeedDiscarding(LogLevel.Information);
        var logger = factory.CreateLogger("Discarding");

        await Assert.That(logger.IsEnabled(LogLevel.Debug)).IsFalse();
        await Assert.That(logger.IsEnabled(LogLevel.Information)).IsTrue();
        using (logger.BeginScope("scope"))
            DirectLog.Write(logger, "discarded but teed");

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("Discarding: discarded but teed", StringComparison.Ordinal);
    }

    /// <summary><c>CreateTeedDiscarding</c> enables every level by default.</summary>
    [Test]
    public async Task CreateTeedDiscarding_DefaultEnablesTrace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var factory = TestOutputLogCollectorBuilder.CreateTeedDiscarding();

        await Assert.That(factory.CreateLogger("Discarding").IsEnabled(LogLevel.Trace)).IsTrue();
    }

    /// <summary>A record on the flow of a test the attribute marked ended goes to no test, even while the owning test
    /// runs: it does not fall back to the owner. With no test current, the running owner gets it.</summary>
    [Test]
    [DependsOn(nameof(A_StoresItsContext))]
    public async Task Tracked_EndedCurrentTest_DoesNotFallBackToTheRunningOwner(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var table = new TestLifetimeTable();
        var owner = TestContext.Current!;
        table.MarkStarted(FinishedTest!);
        await table.MarkEndedAsync(FinishedTest!);
        table.MarkStarted(owner);

        await Assert.That(table.ResolveTeeTarget(FinishedTest, owner)).IsNull();
        await Assert.That(table.ResolveTeeTarget(null, owner)).IsSameReferenceAs(owner);
    }

    /// <summary>The note for a project without the attribute: a table that never saw the test start, and is told the
    /// project does not apply the attribute, writes it once, to that test, and names the line to add; no test, a test
    /// the table started, or a test whose project applies the attribute (TUnit lists it on the test before the start)
    /// gets none. In this assembly the attribute started every test, so the shared table writes none.</summary>
    [Test]
    public async Task MissingAttributeNote_IsWrittenOnce_ToATestTheAttributeDidNotStart(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var test = TestContext.Current!;
        var untracked = new TestLifetimeTable(static _ => false);
        var notStartedYet = new TestLifetimeTable();
        var tracked = new TestLifetimeTable(static _ => false);
        tracked.MarkStarted(test);

        notStartedYet.NoteIfUntracked(test);
        untracked.NoteIfUntracked(null);
        untracked.NoteIfUntracked(test);
        untracked.NoteIfUntracked(test);
        tracked.NoteIfUntracked(test);
        TestLifetimeTable.Shared.NoteIfUntracked(test);
        using var teed = TestOutputLogCollectorBuilder.CreateTeed();

        var output = test.GetStandardOutput();
        await Assert.That(output.IndexOf(TestLifetimeTable.MissingAttributeNote, StringComparison.Ordinal))
            .IsEqualTo(output.LastIndexOf(TestLifetimeTable.MissingAttributeNote, StringComparison.Ordinal));
        await Assert.That(output).Contains(
            "this test project does not apply the LogAssertionsTestLifetime attribute, so the test-output tee cannot see when a test ends",
            StringComparison.Ordinal);
        await Assert.That(output).Contains("[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]", StringComparison.Ordinal);
    }
}
