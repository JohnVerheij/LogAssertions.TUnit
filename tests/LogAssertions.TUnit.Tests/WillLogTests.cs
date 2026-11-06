using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Core;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// <c>WillLog(timeout, cancellationToken)</c>: the event-driven form of <c>HasLogged()</c>. No test waits on the real
/// clock: records arrive after a <see cref="Task.Yield"/>, and the timeout runs on a <see cref="ManualTimeProvider"/>.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class WillLogTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    /// <summary>A record logged before the wait counts: the wait passes at once, also without a token.</summary>
    [Test]
    public async Task AlreadyLogged_PassesAtOnce(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        DirectLog.Write(new FakeLogger(collector, "Worker"), "worker ready");

        await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("ready", StringComparison.Ordinal).Once();
        await WaitWithoutTokenAsync(collector);
    }

    /// <summary>A record logged later on another thread completes the wait.</summary>
    [Test]
    public async Task LoggedLater_PassesOnArrival(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");
        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                DirectLog.Write(logger, "tick");
                DirectLog.Write(logger, "worker ready", LogLevel.Warning);
            },
            cancellationToken);

        var record = await Assert.That(collector).WillLog(Bound, cancellationToken)
            .AtLevel(LogLevel.Warning).Containing("ready", StringComparison.Ordinal).Once().GetMatch();

        await Assert.That(record.Category).IsEqualTo("Worker");
    }

    /// <summary><c>AtLeast</c>, <c>Exactly</c>, and <c>Between</c> pass once the count is in range, and
    /// <c>GetMatches</c> returns the matches.</summary>
    [Test]
    public async Task CountTerminators_PassInRange(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");
        DirectLog.Write(logger, "item 1");
        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                DirectLog.Write(logger, "item 2");
            },
            cancellationToken);

        var matches = await Assert.That(collector).WillLog(Bound, cancellationToken)
            .Containing("item", StringComparison.Ordinal).AtLeast(2).GetMatches();
        await Assert.That(matches.Count).IsEqualTo(2);
        await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("item", StringComparison.Ordinal).Exactly(2);
        await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("item", StringComparison.Ordinal).Between(1, 3);
    }

    /// <summary>A second matching record already captured before the wait runs fails <c>Once()</c> at once, with the
    /// standard dump: records captured by the time the minimum is reached count.</summary>
    [Test]
    public async Task OverTheMaximum_FailsAtOnce(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");
        DirectLog.Write(logger, "ready 1");
        DirectLog.Write(logger, "ready 2");

        var exception = await CatchAsync<AssertionException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("ready", StringComparison.Ordinal).Once());

        await Assert.That(exception!.Message).Contains("exactly 1 log record(s) to be logged within 00:00:05 matching: Message contains \"ready\" (Ordinal)", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("2 record(s) matched, more than the maximum of 1", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("Captured records (2 total):", StringComparison.Ordinal);
    }

    /// <summary>A second matching record logged after <c>Once()</c> has passed is not checked: the passed result
    /// stands, and the matches it returned are the ones captured when it passed.</summary>
    [Test]
    public async Task RecordAfterThePass_DoesNotChangeThePassedResult(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");
        DirectLog.Write(logger, "ready 1");

        var matches = await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("ready", StringComparison.Ordinal).Once().GetMatches();
        DirectLog.Write(logger, "ready 2");

        await Assert.That(matches).Count().IsEqualTo(1);
        await Assert.That(matches[0].Message).IsEqualTo("ready 1");
        await Assert.That(collector.Count).IsEqualTo(2);
    }

    /// <summary>The timeout ends the wait exactly when it has run out, not a tick before, and the wait fails with the
    /// expectation, the count, and the captured records.</summary>
    [Test]
    public async Task Timeout_FailsWithTheDiagnostics(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        DirectLog.Write(new FakeLogger(collector, "Worker"), "starting");
        var clock = new ManualTimeProvider();
        var context = new AssertionContext<FakeLogCollector>(collector, new StringBuilder("Assert.That(collector)"));

        var outcome = CatchAsync<AssertionException>(async () =>
            await new WillLogAssertion(context, Bound, clock, cancellationToken).Containing("ready", StringComparison.Ordinal).Once());
        await clock.WaitForTimerAsync(cancellationToken);
        clock.Advance(Bound - TimeSpan.FromTicks(1));
        await Task.Yield();
        await Assert.That(outcome.IsCompleted).IsFalse();

        clock.Advance(TimeSpan.FromTicks(1));
        var exception = await outcome;

        await Assert.That(exception!.Message).Contains("the wait timed out after 00:00:05 with 0 record(s) matched", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("[info] Worker: starting", StringComparison.Ordinal);
    }

    /// <summary>The timeout starts when the assertion runs, not when the chain is built: time that passes between the
    /// two does not count.</summary>
    [Test]
    public async Task Timeout_StartsWhenTheAssertionRuns(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var clock = new ManualTimeProvider();
        var context = new AssertionContext<FakeLogCollector>(collector, new StringBuilder("Assert.That(collector)"));
        var chain = new WillLogAssertion(context, Bound, clock, cancellationToken).Containing("ready", StringComparison.Ordinal).Once();

        clock.Advance(Bound * 2);
        var outcome = CatchAsync<AssertionException>(chain.AssertAsync);
        await clock.WaitForTimerAsync(cancellationToken);
        DirectLog.Write(new FakeLogger(collector, "Worker"), "worker ready");

        await Assert.That(await outcome).IsNull();
    }

    /// <summary>When the token ends the wait, it fails with the same diagnostics, not a bare cancellation.</summary>
    [Test]
    public async Task Canceled_FailsWithTheDiagnostics(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var outcome = CatchAsync<AssertionException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancel.Token).Containing("ready", StringComparison.Ordinal));
        await cancel.CancelAsync();
        var exception = await outcome;

        await Assert.That(exception!.Message).Contains("the wait was canceled with 0 record(s) matched", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("Captured records (0 total):", StringComparison.Ordinal);
    }

    /// <summary>A chain whose levels all sit below the capture floor fails at once instead of waiting.</summary>
    [Test]
    public async Task BelowTheCaptureFloor_FailsAtOnce(CancellationToken cancellationToken)
    {
        using var capture = LogCollectorBuilder.Create(LogLevel.Information);

        var exception = await CatchAsync<AssertionException>(async () =>
            await Assert.That(capture.Collector).WillLog(Bound, cancellationToken).AtLevel(LogLevel.Debug));

        await Assert.That(exception!.Message).Contains(
            "this assertion only matches records at level Debug or below, but the collector's capture floor is Information, so no such record was captured.",
            StringComparison.Ordinal);
    }

    /// <summary>When the collector is cleared after the minimum arrived but before the count was checked, the wait
    /// goes on until enough records arrive again.</summary>
    [Test]
    public async Task ClearedDuringTheWait_WaitsAgain(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");
        var cleared = 0;
        DirectLog.Write(logger, "item 1");

        // The filter clears the collector the first time it sees "item 1", so the count of one that ends the first
        // wait is gone when the assertion checks the collector.
        var outcome = CatchAsync<AssertionException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken)
                .Where(record =>
                {
                    if (record.Message is "item 1" && Interlocked.Exchange(ref cleared, 1) is 0)
                        collector.Clear();
                    return record.Message.StartsWith("item", StringComparison.Ordinal);
                })
                .Once());

        await Task.Yield();
        DirectLog.Write(logger, "item 2");

        await Assert.That(await outcome).IsNull();
        await Assert.That(cleared).IsEqualTo(1);
        await Assert.That(collector.Count).IsEqualTo(1);
    }

    /// <summary><c>GetMatch</c> needs a count of exactly one; the count terminators reject a non-positive count; the
    /// timeout must be positive and within what a <see cref="CancellationTokenSource"/> accepts.</summary>
    [Test]
    public async Task InvalidArguments_Throw(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();

        await Assert.That(await CatchAsync<InvalidOperationException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken).AtLeast(2).GetMatch())).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken).Exactly(0))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken).AtLeast(0))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken).Between(0, 1))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(Bound, cancellationToken).Between(2, 1))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(TimeSpan.Zero, cancellationToken))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(Timeout.InfiniteTimeSpan, cancellationToken))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).WillLog(TimeSpan.MaxValue, cancellationToken))).IsNotNull();
    }

    /// <summary>A source that threw, and a <see langword="null"/> collector, fail the assertion instead of waiting.</summary>
    [Test]
    public async Task DefensiveSources_Fail(CancellationToken cancellationToken)
    {
        await Assert.That(await CatchAsync<AssertionException>(async () =>
            await Assert.That(ThrowingSourceAsync(cancellationToken)).WillLog<FakeLogCollector>(Bound, cancellationToken))).IsNotNull();

        var exception = await CatchAsync<AssertionException>(async () =>
            await Assert.That(NullCollector()!).WillLog(Bound, cancellationToken));
        await Assert.That(exception!.Message).Contains("collector was null", StringComparison.Ordinal);
    }

    /// <summary>The documented call without a token: the timeout alone bounds the wait.</summary>
    /// <param name="collector">The collector, which already holds a matching record.</param>
    /// <returns>A task that completes when the assertion has passed.</returns>
    private static async Task WaitWithoutTokenAsync(FakeLogCollector collector)
        => await Assert.That(collector).WillLog(Bound).Containing("ready", StringComparison.Ordinal);

    /// <summary>Runs <paramref name="run"/> and returns the <typeparamref name="TException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <typeparam name="TException">The exception type to catch.</typeparam>
    /// <param name="run">The code to run.</param>
    /// <returns>The exception, or <see langword="null"/> when none was thrown.</returns>
    private static async Task<TException?> CatchAsync<TException>(Func<Task> run)
        where TException : Exception
    {
        try
        {
            await run();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }

    /// <summary>Returns a null collector without a constant expression the analyzer would reject.</summary>
    /// <returns>Always null.</returns>
    private static FakeLogCollector? NullCollector() => null;

    /// <summary>A source that throws.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Never returns; always throws.</returns>
    private static async Task<FakeLogCollector> ThrowingSourceAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("source threw");
    }
}
