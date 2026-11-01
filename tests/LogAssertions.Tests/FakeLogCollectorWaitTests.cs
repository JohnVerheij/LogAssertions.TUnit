using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Tests;

/// <summary>
/// Tests for the event-driven waits (<see cref="FakeLogCollectorWaitExtensions"/>) and for
/// <see cref="LogCollectorBuilder.CreateDiscarding"/>. No test waits on the real clock: background work runs after a
/// <see cref="Task.Yield"/>, and a wait reacts to the record whether it arrives before, during, or after the call.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class FakeLogCollectorWaitTests
{
    private static readonly ILogRecordFilter Ready = LogFilter.Containing("ready", StringComparison.Ordinal);

    // --- WaitForLogAsync ---

    /// <summary>A record logged before the wait counts: the wait returns at once with it.</summary>
    [Test]
    public async Task WaitForLogAsync_RecordAlreadyLogged_ReturnsAtOnce(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");
        Write(logger, "service ready");

        var record = await collector.WaitForLogAsync(Ready, cancellationToken);

        await Assert.That(record.Message).IsEqualTo("service ready");
    }

    /// <summary>A record logged after the wait started completes it, and earlier non-matching records do not.</summary>
    [Test]
    public async Task WaitForLogAsync_RecordLoggedLater_CompletesOnArrival(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");
        Write(logger, "starting");

        var wait = StartWait(collector, Ready, cancellationToken);
        await Assert.That(wait.IsCompleted).IsFalse();

        Write(logger, "service ready");
        var record = await wait;

        await Assert.That(record.Message).IsEqualTo("service ready");
    }

    /// <summary>A record logged on another thread while the wait runs is seen.</summary>
    [Test]
    public async Task WaitForLogAsync_RecordLoggedOnAnotherThread_IsSeen(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                Write(logger, "tick");
                Write(logger, "worker ready", LogLevel.Warning);
            },
            cancellationToken);

        var record = await collector.WaitForLogAsync(
            LogFilter.All(LogFilter.AtLevel(LogLevel.Warning), Ready),
            cancellationToken);

        await Assert.That(record.Category).IsEqualTo("Worker");
    }

    /// <summary>The predicate overload matches like a filter.</summary>
    [Test]
    public async Task WaitForLogAsync_Predicate_ReturnsTheFirstMatch(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");
        Write(logger, "a", LogLevel.Information);
        Write(logger, "b", LogLevel.Error);
        Write(logger, "c", LogLevel.Error);

        var record = await collector.WaitForLogAsync(static r => r.Level == LogLevel.Error, cancellationToken);

        await Assert.That(record.Message).IsEqualTo("b");
    }

    /// <summary>A record logged after <see cref="FakeLogCollector.Clear"/> during a wait is still seen.</summary>
    [Test]
    public async Task WaitForLogAsync_ClearDuringTheWait_LaterRecordIsSeen(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");
        Write(logger, "one");
        Write(logger, "two");

        var wait = StartWait(collector, Ready, cancellationToken);
        collector.Clear();
        Write(logger, "ready after clear");

        await Assert.That((await wait).Message).IsEqualTo("ready after clear");
    }

    /// <summary>Several waits on one collector run independently.</summary>
    [Test]
    public async Task WaitForLogAsync_ConcurrentWaits_EachCompletes(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");

        var first = StartWait(collector, LogFilter.Containing("first", StringComparison.Ordinal), cancellationToken);
        var second = StartWait(collector, LogFilter.Containing("second", StringComparison.Ordinal), cancellationToken);
        Write(logger, "second");
        Write(logger, "first");

        await Assert.That((await first).Message).IsEqualTo("first");
        await Assert.That((await second).Message).IsEqualTo("second");
    }

    /// <summary>Canceling the wait throws <see cref="OperationCanceledException"/> naming the filter and counts. The
    /// filter cancels while it evaluates the first record, so the wait has seen exactly one record, whatever the
    /// scheduling.</summary>
    [Test]
    public async Task WaitForLogAsync_Canceled_ThrowsWithDiagnostics(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");
        Write(logger, "starting");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var exception = await Assert.That(async () => await collector.WaitForLogAsync(new CancelingFilter(cts), cts.Token))
            .Throws<OperationCanceledException>();

        await Assert.That(exception!.Message).Contains("Waiting for 1 log record(s) where cancels on the first record");
        await Assert.That(exception.Message).Contains("0 matched among the 1 record(s) collected");
        await Assert.That(exception.CancellationToken).IsEqualTo(cts.Token);
    }

    /// <summary>A canceled wait from another thread also throws, whatever it had read.</summary>
    [Test]
    public async Task WaitForLogAsync_CanceledFromAnotherThread_Throws(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                await cts.CancelAsync();
            },
            cancellationToken);

        await Assert.That(async () => await collector.WaitForLogAsync(Ready, cts.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>A canceled token throws before anything is read.</summary>
    [Test]
    public async Task WaitForLogAsync_PreCanceledToken_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await collector.WaitForLogAsync(Ready, cts.Token))
            .Throws<OperationCanceledException>();
    }

    /// <summary>A count far beyond any real test allocates nothing up front: the wait starts and ends on its
    /// token.</summary>
    [Test]
    public async Task WaitForLogsAsync_HugeCount_EndsOnTheToken(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var exception = await Assert.That(async () => await collector.WaitForLogsAsync(Ready, int.MaxValue, cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(exception!.Message).Contains("Waiting for 2147483647 log record(s)");
    }

    /// <summary>Null arguments and a non-positive count are rejected.</summary>
    [Test]
    public async Task Waits_InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();

        await Assert.That(async () => await ((FakeLogCollector)null!).WaitForLogAsync(Ready, cancellationToken)).Throws<ArgumentNullException>();
        await Assert.That(async () => await collector.WaitForLogAsync((ILogRecordFilter)null!, cancellationToken)).Throws<ArgumentNullException>();
        await Assert.That(async () => await collector.WaitForLogAsync((Func<FakeLogRecord, bool>)null!, cancellationToken)).Throws<ArgumentNullException>();
        await Assert.That(async () => await collector.WaitForLogsAsync(Ready, 0, cancellationToken)).Throws<ArgumentOutOfRangeException>();
    }

    // --- WaitForLogsAsync ---

    /// <summary>The count form returns the first <c>count</c> matching records in the order they were logged.</summary>
    [Test]
    public async Task WaitForLogsAsync_ReturnsTheFirstCountMatches(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Test");
        Write(logger, "ready 1");

        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                Write(logger, "noise");
                Write(logger, "ready 2");
                Write(logger, "ready 3");
                Write(logger, "ready 4");
            },
            cancellationToken);

        var records = await collector.WaitForLogsAsync(Ready, 3, cancellationToken);

        await Assert.That(records.Count).IsEqualTo(3);
        await Assert.That(records[0].Message).IsEqualTo("ready 1");
        await Assert.That(records[2].Message).IsEqualTo("ready 3");
    }

    // --- CreateDiscarding ---

    /// <summary>The discarding factory's loggers are enabled at and above the minimum level, and logging through them
    /// (with a scope) keeps nothing and does not throw.</summary>
    [Test]
    public async Task CreateDiscarding_EnabledAtAndAboveTheMinimum(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var factory = LogCollectorBuilder.CreateDiscarding(LogLevel.Information);
        var logger = factory.CreateLogger("Test");

        await Assert.That(logger.IsEnabled(LogLevel.Debug)).IsFalse();
        await Assert.That(logger.IsEnabled(LogLevel.Information)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.Critical)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.None)).IsFalse();

        using (logger.BeginScope("scope"))
        {
            Write(logger, "discarded", LogLevel.Error);
        }
    }

    /// <summary>By default every level is enabled.</summary>
    [Test]
    public async Task CreateDiscarding_DefaultEnablesTrace(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var factory = LogCollectorBuilder.CreateDiscarding();

        await Assert.That(factory.CreateLogger("Test").IsEnabled(LogLevel.Trace)).IsTrue();
    }

    /// <summary>A filter that cancels the wait while it evaluates a record, and matches nothing.</summary>
    private sealed class CancelingFilter(CancellationTokenSource source) : ILogRecordFilter
    {
        /// <inheritdoc/>
        public string Description => "cancels on the first record";

        /// <inheritdoc/>
        public bool Matches(FakeLogRecord record)
        {
            source.Cancel();
            return false;
        }
    }

    /// <summary>Starts a wait as a task the test can inspect before awaiting it.</summary>
    private static Task<FakeLogRecord> StartWait(FakeLogCollector collector, ILogRecordFilter filter, CancellationToken cancellationToken)
        => collector.WaitForLogAsync(filter, cancellationToken);

    /// <summary>Writes one record through <see cref="ILogger.Log{TState}"/> directly.</summary>
    private static void Write(ILogger logger, string message, LogLevel level = LogLevel.Information)
        => logger.Log(level, new EventId(1), message, exception: null, static (state, _) => state);
}
