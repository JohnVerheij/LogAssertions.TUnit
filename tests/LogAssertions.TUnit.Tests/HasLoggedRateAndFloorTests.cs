using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// <c>HasLogged()...AtMostOncePer(interval)</c> (spacing by record timestamp, on a <see cref="ManualTimeProvider"/>),
/// and the capture-floor guard on every <c>HasLogged()</c> count: an absence check (a count that admits zero) fails as
/// vacuous below the floor, and a chain that needs a match names the floor in its failure.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class HasLoggedRateAndFloorTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    /// <summary>The vacuous-floor message for a chain at Debug against an Information floor, word for word.</summary>
    private const string VacuousBelowInformation =
        "this assertion only matches records at level Debug or below, but the collector's capture floor is Information, "
        + "so records below Information were never captured and the check is vacuously true. "
        + "Lower the floor (LogCollectorBuilder.Create with a lower minimumLevel) or assert at a captured level.";

    // --- AtMostOncePer ---

    /// <summary>Gaps of exactly the interval and longer pass.</summary>
    [Test]
    public async Task AtMostOncePer_GapsAtLeastTheInterval_Pass(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, clock, logger) = NewCollector();
        Log(clock, logger, TimeSpan.Zero, "queue full");
        Log(clock, logger, Minute, "queue full");
        Log(clock, logger, TimeSpan.FromMinutes(3), "queue full");

        await Assert.That(collector).HasLogged().Containing("queue full", StringComparison.Ordinal).AtMostOncePer(Minute);
    }

    /// <summary>The boundary: a gap must be at least the interval. Exactly the interval and one tick more pass; one
    /// tick less fails.</summary>
    [Test]
    [Arguments(0L, true)]
    [Arguments(1L, true)]
    [Arguments(-1L, false)]
    public async Task AtMostOncePer_Boundary_GapMustBeAtLeastTheInterval(long ticksFromTheInterval, bool passes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, clock, logger) = NewCollector();
        Log(clock, logger, TimeSpan.Zero, "queue full");
        Log(clock, logger, Minute + TimeSpan.FromTicks(ticksFromTheInterval), "queue full");

        var exception = await CatchAsync(async () => await Assert.That(collector).HasLogged().AtMostOncePer(Minute));

        await Assert.That(exception is null).IsEqualTo(passes);
    }

    /// <summary>A gap shorter than the interval fails with the two records, their timestamps, the gap, and the number
    /// of short gaps; the expectation names the interval.</summary>
    [Test]
    public async Task AtMostOncePer_ShortGap_FailsWithThePair(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, clock, logger) = NewCollector();
        Log(clock, logger, TimeSpan.Zero, "queue full");
        Log(clock, logger, TimeSpan.FromSeconds(20), "queue full");
        Log(clock, logger, TimeSpan.FromSeconds(80), "queue full");

        var exception = await CatchAsync(async () =>
            await Assert.That(collector).HasLogged().Containing("queue full", StringComparison.Ordinal).AtMostOncePer(Minute));

        var message = exception!.Message;
        await Assert.That(message).Contains(
            "at least 1 log record(s) to have been logged, at most once per 00:01:00, matching: Message contains \"queue full\" (Ordinal)",
            StringComparison.Ordinal);
        await Assert.That(message).Contains("two matching records were 00:00:20 apart, less than 00:01:00 (1 of 2 gaps too short):", StringComparison.Ordinal);
        await Assert.That(message).Contains("  12:00:00.000 [warn] Queue: queue full", StringComparison.Ordinal);
        await Assert.That(message).Contains("  12:00:20.000 [warn] Queue: queue full", StringComparison.Ordinal);
        await Assert.That(message).Contains("Captured records (3 total):", StringComparison.Ordinal);
    }

    /// <summary>Records are ordered by timestamp, not by the order they were logged, and two records with the same
    /// timestamp are zero apart.</summary>
    [Test]
    public async Task AtMostOncePer_OrdersByTimestamp_AndTiesAreTooClose(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, clock, logger) = NewCollector();
        Log(clock, logger, TimeSpan.FromMinutes(2), "late");
        Log(clock, logger, TimeSpan.Zero, "early");

        await Assert.That(collector).HasLogged().AtMostOncePer(Minute);

        Log(clock, logger, TimeSpan.Zero, "early again");
        var exception = await CatchAsync(async () => await Assert.That(collector).HasLogged().AtMostOncePer(Minute));

        await Assert.That(exception!.Message).Contains("at least 1 log record(s) to have been logged, at most once per 00:01:00\nbut ", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("two matching records were 00:00:00 apart", StringComparison.Ordinal);
    }

    /// <summary>One record passes; zero records fail, since <c>HasLogged()</c> keeps its implicit "at least 1"; zero
    /// records pass with a count that admits zero.</summary>
    [Test]
    public async Task AtMostOncePer_ComposesWithTheCount(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, clock, logger) = NewCollector();

        var none = await CatchAsync(async () => await Assert.That(collector).HasLogged().AtMostOncePer(Minute));
        await Assert.That(none!.Message).Contains("0 record(s) matched", StringComparison.Ordinal);
        await Assert.That(collector).HasLogged().AtMost(5).AtMostOncePer(Minute);

        Log(clock, logger, TimeSpan.Zero, "one");
        await Assert.That(collector).HasLogged().AtMostOncePer(Minute);

        Log(clock, logger, TimeSpan.FromMinutes(2), "two");
        var tooFew = await CatchAsync(async () => await Assert.That(collector).HasLogged().AtMostOncePer(Minute).AtLeast(3));
        await Assert.That(tooFew!.Message).Contains("at least 3 log record(s) to have been logged, at most once per 00:01:00", StringComparison.Ordinal);
        var tooFewOtherOrder = await CatchAsync(async () => await Assert.That(collector).HasLogged().AtLeast(3).AtMostOncePer(Minute));
        await Assert.That(tooFewOtherOrder!.Message).Contains("2 record(s) matched", StringComparison.Ordinal);
    }

    /// <summary>The interval must be positive.</summary>
    [Test]
    public async Task AtMostOncePer_NonPositiveInterval_Throws(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();

        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).HasLogged().AtMostOncePer(TimeSpan.Zero))).IsNotNull();
        await Assert.That(await CatchAsync<ArgumentOutOfRangeException>(async () =>
            await Assert.That(collector).HasLogged().AtMostOncePer(TimeSpan.FromSeconds(-1)))).IsNotNull();
        cancellationToken.ThrowIfCancellationRequested();
    }

    // --- capture-floor guard on HasLogged() ---

    /// <summary>Every count that admits zero is an absence check and fails as vacuous below the floor, with the message
    /// <c>HasNotLogged()</c> uses, naming the asserted level and the floor.</summary>
    [Test]
    [Arguments("Never()")]
    [Arguments("AtMost(3)")]
    [Arguments("Exactly(0)")]
    [Arguments("Between(0, 2)")]
    public async Task AbsenceCount_BelowTheFloor_FailsAsVacuous(string count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var capture = LogCollectorBuilder.Create(LogLevel.Information);
        var collector = capture.Collector;
        Func<Task> run = count switch
        {
            "Never()" => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).Never(),
            "AtMost(3)" => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).AtMost(3),
            "Exactly(0)" => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).Exactly(0),
            _ => async () => await Assert.That(collector).HasLogged().AtLevelOrBelow(LogLevel.Debug).Between(0, 2),
        };

        var exception = await CatchAsync(run);

        await Assert.That(exception!.Message).Contains("but " + VacuousBelowInformation, StringComparison.Ordinal);
    }

    /// <summary>The full failure of <c>Never()</c> and <c>AtMost(n)</c> below the floor: the expectation, then the
    /// floor message, word for word.</summary>
    [Test]
    public async Task NeverAndAtMost_BelowTheFloor_FullMessage(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var capture = LogCollectorBuilder.Create(LogLevel.Information);

        var never = await CatchAsync(async () => await Assert.That(capture.Collector).HasLogged().AtLevel(LogLevel.Debug).Never());
        var atMost = await CatchAsync(async () => await Assert.That(capture.Collector).HasLogged().AtLevel(LogLevel.Debug).AtMost(3));

        await Assert.That(never!.Message).StartsWith(
            "Expected exactly 0 log record(s) to have been logged matching: Level = Debug\nbut " + VacuousBelowInformation,
            StringComparison.Ordinal);
        await Assert.That(atMost!.Message).StartsWith(
            "Expected at most 3 log record(s) to have been logged matching: Level = Debug\nbut " + VacuousBelowInformation,
            StringComparison.Ordinal);
    }

    /// <summary>An absence check at a captured level passes; a count that admits zero but sees too many records
    /// fails on the count, not on the floor.</summary>
    [Test]
    public async Task AbsenceCounts_AtACapturedLevel_AreNotVacuous(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var capture = LogCollectorBuilder.Create(LogLevel.Information);
        var logger = capture.CreateLogger("Floor");

        await Assert.That(capture.Collector).HasLogged().AtLevel(LogLevel.Warning).Never();

        DirectLog.Write(logger, "one", LogLevel.Warning);
        DirectLog.Write(logger, "two", LogLevel.Warning);
        var exception = await CatchAsync(async () => await Assert.That(capture.Collector).HasLogged().AtLevel(LogLevel.Warning).AtMost(1));

        await Assert.That(exception!.Message).Contains("2 record(s) matched" + Environment.NewLine, StringComparison.Ordinal);
        await Assert.That(exception.Message).DoesNotContain("capture floor", StringComparison.Ordinal);
    }

    /// <summary>Every count that needs a match fails below the floor and names the floor.</summary>
    [Test]
    [Arguments("Once()")]
    [Arguments("AtLeast(2)")]
    [Arguments("Exactly(2)")]
    [Arguments("Between(1, 3)")]
    public async Task NeededMatch_BelowTheFloor_NamesTheFloor(string count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var capture = LogCollectorBuilder.Create(LogLevel.Information);
        var collector = capture.Collector;
        Func<Task> run = count switch
        {
            "Once()" => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).Once(),
            "AtLeast(2)" => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).AtLeast(2),
            "Exactly(2)" => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).Exactly(2),
            _ => async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).Between(1, 3),
        };

        var exception = await CatchAsync(run);

        await Assert.That(exception!.Message).Contains(
            "0 record(s) matched: this assertion only matches records at level Debug or below, but the collector's capture floor is Information, so no such record was captured.",
            StringComparison.Ordinal);
    }

    /// <summary>At a captured level, a chain that needs a match fails without the floor note.</summary>
    [Test]
    public async Task NeededMatch_AtACapturedLevel_HasNoFloorNote(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var capture = LogCollectorBuilder.Create(LogLevel.Information);

        var captured = await CatchAsync(async () => await Assert.That(capture.Collector).HasLogged().AtLevel(LogLevel.Warning).Once());

        await Assert.That(captured!.Message).Contains("0 record(s) matched" + Environment.NewLine, StringComparison.Ordinal);
    }

    /// <summary>Creates a collector whose timestamps come from a <see cref="ManualTimeProvider"/>, and a logger on it.</summary>
    /// <returns>The collector, its clock, and a logger.</returns>
    private static (FakeLogCollector Collector, ManualTimeProvider Clock, ILogger Logger) NewCollector()
    {
        var clock = new ManualTimeProvider();
        var collector = FakeLogCollector.Create(new FakeLogCollectorOptions { TimeProvider = clock });
        return (collector, clock, new FakeLogger(collector, "Queue"));
    }

    /// <summary>Logs a Warning record at <paramref name="offset"/> after noon.</summary>
    /// <param name="clock">The collector's clock.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="offset">The time after 12:00:00 UTC.</param>
    /// <param name="message">The message.</param>
    private static void Log(ManualTimeProvider clock, ILogger logger, TimeSpan offset, string message)
    {
        clock.UtcNow = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero) + offset;
        DirectLog.Write(logger, message, LogLevel.Warning);
    }

    /// <summary>Runs <paramref name="run"/> and returns the <see cref="AssertionException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <param name="run">The assertion to run.</param>
    /// <returns>The exception, or <see langword="null"/>.</returns>
    private static Task<AssertionException?> CatchAsync(Func<Task> run) => CatchAsync<AssertionException>(run);

    /// <summary>Runs <paramref name="run"/> and returns the <typeparamref name="TException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <typeparam name="TException">The exception type to catch.</typeparam>
    /// <param name="run">The code to run.</param>
    /// <returns>The exception, or <see langword="null"/>.</returns>
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
}
