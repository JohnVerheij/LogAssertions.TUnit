using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using LogAssertions.TUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for the value-returning terminators on <see cref="HasLoggedAssertion"/>:
/// <see cref="HasLoggedAssertion.GetMatch"/> (returns the single match when the chain is
/// terminated with <c>Once</c> or <c>Exactly(1)</c>) and <see cref="HasLoggedAssertion.GetMatches"/>
/// (returns the matched-records snapshot for any terminator). These methods kill the duplicate
/// <c>collector.Filter(...)</c> pattern by handing the matched records to follow-up
/// assertions in the same chain.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class HasLoggedAssertionGetMatchTests
{
    private static FakeLogCollector CreateCollectorWithSeededRecords()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });

        ILogger logger = factory.CreateLogger("GetMatchCategory");
        TestLogMessages.StartedProcessing(logger);     // Information
        TestLogMessages.ValidationFailed(logger);      // Warning
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("first boom"));  // Error
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("second boom")); // Error
        return collector;
    }

    /// <summary>
    /// Pins the canonical <c>GetMatch()</c> happy path: a chain terminated with <c>Once()</c>
    /// returns the single matching <see cref="FakeLogRecord"/>, which can then drive a
    /// follow-up TUnit assertion (here: that the record's exception message contains the
    /// expected text).
    /// </summary>
    [Test]
    public async Task GetMatchReturnsSingleMatchedRecordWhenChainExpectsOneAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        FakeLogRecord match = await Assert.That(collector)
            .HasLogged()
            .AtLevel(LogLevel.Warning)
            .Once()
            .GetMatch();

        await Assert.That(match.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(match.Message).Contains("validation failed");
    }

    /// <summary>
    /// Pins that <c>GetMatch()</c> works equivalently after <c>Exactly(1)</c> as it does
    /// after <c>Once()</c> — both terminators express "exactly one match", and both should
    /// satisfy <c>GetMatch</c>'s precondition.
    /// </summary>
    [Test]
    public async Task GetMatchReturnsSingleMatchedRecordWhenChainUsesExactlyOneAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        FakeLogRecord match = await Assert.That(collector)
            .HasLogged()
            .AtLevel(LogLevel.Warning)
            .Exactly(1)
            .GetMatch();

        await Assert.That(match.Level).IsEqualTo(LogLevel.Warning);
    }

    /// <summary>
    /// Pins that <c>GetMatch()</c> throws <see cref="InvalidOperationException"/> when the
    /// chain's count expectation isn't <c>1</c>. Avoids silent surprise where the user calls
    /// <c>GetMatch</c> after <c>AtLeast(1)</c> and gets only the first of N matches.
    /// </summary>
    [Test]
    public async Task GetMatchThrowsInvalidOperationWhenCountExpectationIsNotOneAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        InvalidOperationException? caught = null;
        try
        {
            // Two Error records, AtLeast(1) is satisfied — but GetMatch should reject the
            // expectation upfront because "at least 1" is not "exactly 1".
            await Assert.That(collector)
                .HasLogged()
                .AtLevel(LogLevel.Error)
                .AtLeast(1)
                .GetMatch();
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("GetMatch()");
        await Assert.That(caught.Message).Contains("Once()");
    }

    /// <summary>
    /// Pins that <c>GetMatch()</c> propagates the assertion failure (via
    /// <see cref="AssertionException"/>) when the count expectation is correct (Once)
    /// but the actual match count is wrong (here: zero matches).
    /// </summary>
    [Test]
    public async Task GetMatchPropagatesAssertionFailureWhenCountIsWrongAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        AssertionException? caught = null;
        try
        {
            await Assert.That(collector)
                .HasLogged()
                .AtLevel(LogLevel.Critical)  // No Critical records seeded
                .Once()
                .GetMatch();
        }
        catch (AssertionException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    /// <summary>
    /// Pins the canonical <c>GetMatches()</c> happy path: a chain terminated with
    /// <c>AtLeast(N)</c> returns the matched records as an ordered snapshot, which can drive
    /// follow-up assertions about the collection.
    /// </summary>
    [Test]
    public async Task GetMatchesReturnsAllMatchedRecordsForAtLeastTerminatorAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        IReadOnlyList<FakeLogRecord> matches = await Assert.That(collector)
            .HasLogged()
            .AtLevel(LogLevel.Error)
            .AtLeast(1)
            .GetMatches();

        await Assert.That(matches).Count().IsEqualTo(2);
        await Assert.That(matches[0].Exception!.Message).Contains("first boom");
        await Assert.That(matches[1].Exception!.Message).Contains("second boom");
    }

    /// <summary>
    /// Pins that <c>GetMatches()</c> works equally with <c>Exactly(N)</c> as the count
    /// terminator and returns the same snapshot regardless of which terminator was chosen
    /// (the matches are determined by the filter chain, not the terminator).
    /// </summary>
    [Test]
    public async Task GetMatchesReturnsAllMatchedRecordsForExactlyTerminatorAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        IReadOnlyList<FakeLogRecord> matches = await Assert.That(collector)
            .HasLogged()
            .AtLevel(LogLevel.Error)
            .Exactly(2)
            .GetMatches();

        await Assert.That(matches).Count().IsEqualTo(2);
    }

    /// <summary>
    /// Pins that <c>GetMatches()</c> propagates the assertion failure when the count
    /// expectation isn't met. The thrown <see cref="AssertionException"/> includes the
    /// captured-records snapshot from the failure-message renderer.
    /// </summary>
    [Test]
    public async Task GetMatchesPropagatesAssertionFailureWhenCountIsWrongAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        AssertionException? caught = null;
        try
        {
            await Assert.That(collector)
                .HasLogged()
                .AtLevel(LogLevel.Error)
                .Exactly(99)  // Asking for 99, only 2 present
                .GetMatches();
        }
        catch (AssertionException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("2 record(s) matched");
    }
}
