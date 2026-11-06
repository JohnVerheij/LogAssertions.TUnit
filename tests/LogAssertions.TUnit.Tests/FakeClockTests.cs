using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The two clocks. Record timestamps come from the collector's clock, a fake one through the <c>TimeProvider</c>
/// overloads of the builders, and <c>AtMostOncePer</c> and the failure dump read them. <c>WillLog</c> waits in real
/// time, so with a fake clock a test starts the assertion, advances the clock, then awaits the assertion.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class FakeClockTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary><c>CreateTeed(TimeProvider)</c>: records carry the fake time, are teed, and the minimum level is the
    /// capture floor; a <see langword="null"/> clock is rejected.</summary>
    [Test]
    public async Task CreateTeed_WithAFakeClock_StampsTeesAndRegistersTheFloor(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = new FakeTimeProvider(Noon);
        using var logs = TestOutputLogCollectorBuilder.CreateTeed(clock, LogLevel.Information);
        clock.Advance(TimeSpan.FromMinutes(1));

        DirectLog.Write(logs.CreateLogger("Clock"), "stamped by the fake clock");

        await Assert.That(logs.Collector.LatestRecord.Timestamp).IsEqualTo(Noon.AddMinutes(1));
        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("Clock: stamped by the fake clock", StringComparison.Ordinal);
        var vacuous = await CatchAsync(async () => await Assert.That(logs.Collector).HasNotLogged().AtLevel(LogLevel.Debug));
        await Assert.That(vacuous!.Message).Contains("the collector's capture floor is Information", StringComparison.Ordinal);
        await Assert.That(() => TestOutputLogCollectorBuilder.CreateTeed(null!)).Throws<ArgumentNullException>();
    }

    /// <summary><c>Create(TimeProvider)</c>: <c>AtMostOncePer</c> reads the fake clock, so two records a fake minute
    /// apart, logged within real milliseconds, are spaced a minute; the minimum level is the capture floor.</summary>
    [Test]
    public async Task Create_WithAFakeClock_AtMostOncePerReadsIt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = new FakeTimeProvider(Noon);
        using var logs = LogCollectorBuilder.Create(clock, LogLevel.Information);
        var logger = logs.CreateLogger("Rate");

        DirectLog.Write(logger, "hourly line");
        clock.Advance(TimeSpan.FromMinutes(1));
        DirectLog.Write(logger, "hourly line");

        await Assert.That(logs.Collector).HasLogged().Containing("hourly", StringComparison.Ordinal).Exactly(2).AtMostOncePer(TimeSpan.FromMinutes(1));
        var vacuous = await CatchAsync(async () => await Assert.That(logs.Collector).HasLogged().AtLevel(LogLevel.Debug).Never());
        await Assert.That(vacuous!.Message).Contains("the collector's capture floor is Information", StringComparison.Ordinal);
    }

    /// <summary>The on-failure dump writes each record's time from the collector's clock (through a table of this
    /// test's own, where the running test counts as not passed).</summary>
    [Test]
    public async Task Dump_TimesComeFromTheCollectorsClock(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clock = new FakeTimeProvider(Noon.AddHours(3).AddMilliseconds(250));
        using var logs = LogCollectorBuilder.Create(clock);
        var test = TestContext.Current!;
        var table = new TestLifetimeTable();

        table.MarkStarted(test);
        table.Arm(test, logs.Collector, DumpVerbosity.Default);
        DirectLog.Write(logs.CreateLogger("Dumped"), "dumped record");
        await table.MarkEndedAsync(test);

        await Assert.That(test.GetStandardOutput()).Contains("  15:00:00.250 [info] Dumped: dumped record", StringComparison.Ordinal);
    }

    /// <summary>The recipe: start the assertion (a local function that awaits it, as TUnit's analyzer requires), advance
    /// the fake clock, then await the assertion. The work logs after a fake minute, and the wait passes.</summary>
    [Test]
    public async Task WillLog_StartAdvanceAwait_Passes(CancellationToken cancellationToken)
    {
        var clock = new FakeTimeProvider(Noon);
        using var logs = LogCollectorBuilder.Create(clock);
        var work = LogAfterAMinuteAsync(logs.CreateLogger("Worker"), clock, "worker ready", cancellationToken);

        var logged = WaitForReadyAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await work;
        await logged;

        await Assert.That(logs.Collector.LatestRecord.Timestamp).IsEqualTo(Noon.AddMinutes(1));

        async Task WaitForReadyAsync() =>
            await Assert.That(logs.Collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken)
                .Containing("ready", StringComparison.Ordinal).Once();
    }

    /// <summary>The failing case: the work logs something else. Once the advancing is done, the test ends the wait
    /// (here through its token), and the final <see langword="await"/> throws <c>WillLog</c>'s message with the
    /// captured records; a helper that advances without observing the task would drop it.</summary>
    [Test]
    public async Task WillLog_StartAdvanceAwait_FinalAwaitThrowsTheMessage(CancellationToken cancellationToken)
    {
        var clock = new FakeTimeProvider(Noon);
        using var logs = LogCollectorBuilder.Create(clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var work = LogAfterAMinuteAsync(logs.CreateLogger("Worker"), clock, "worker failed", cancellationToken);

        var logged = WaitForReadyAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await work;
        await stop.CancelAsync();

        AssertionException? exception = null;
        try
        {
            await logged;
        }
        catch (AssertionException thrown)
        {
            exception = thrown;
        }

        await Assert.That(exception!.Message).Contains("the wait was canceled with 0 record(s) matched", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("[info] Worker: worker failed", StringComparison.Ordinal);

        async Task WaitForReadyAsync() =>
            await Assert.That(logs.Collector).WillLog(TimeSpan.FromSeconds(5), stop.Token)
                .Containing("ready", StringComparison.Ordinal).Once();
    }

    /// <summary>Waits a minute on <paramref name="clock"/>, then logs <paramref name="message"/>. The delay's timer is
    /// created before this returns, so an <c>Advance</c> after the call fires it.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="clock">The clock of the delay.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the record is logged.</returns>
    private static async Task LogAfterAMinuteAsync(ILogger logger, TimeProvider clock, string message, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), clock, cancellationToken);
        DirectLog.Write(logger, message);
    }

    /// <summary>Runs <paramref name="run"/> and returns the <see cref="AssertionException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <param name="run">The assertion to run.</param>
    /// <returns>The exception, or <see langword="null"/>.</returns>
    private static async Task<AssertionException?> CatchAsync(Func<Task> run)
    {
        try
        {
            await run();
            return null;
        }
        catch (AssertionException exception)
        {
            return exception;
        }
    }
}
