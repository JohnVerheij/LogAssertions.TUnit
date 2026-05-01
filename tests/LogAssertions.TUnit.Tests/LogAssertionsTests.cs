using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for the TUnit-native fluent log-assertion DSL built on <c>[AssertionExtension]</c>.
/// Pins the five filter methods (<c>AtLevel</c>, <c>Containing</c>, <c>WithMessage</c>,
/// <c>WithException</c>, <c>WithProperty</c>), the five terminator methods (<c>Once</c>,
/// <c>Exactly</c>, <c>AtLeast</c>, <c>AtMost</c>, <c>Never</c>), the <c>HasNotLogged</c>
/// inverse entry point, and the failure-message shape (terminator description + actual count
/// + captured-records snapshot) that solves the historical Console.WriteLine-debugging friction.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogAssertionsTests
{
    /// <summary>
    /// Builds a <see cref="FakeLogCollector"/> seeded with one record at every level
    /// (Trace / Debug / Information / Warning / Error) plus an exception-bearing entry.
    /// Centralising the seed shape lets each test assert against a fixed, well-known set
    /// of records — counts and substrings throughout the file are anchored to this seed.
    /// </summary>
    /// <returns>A populated collector with five sample log records.</returns>
    private static FakeLogCollector CreateCollectorWithSampleRecords()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });

        ILogger logger = factory.CreateLogger("TestCategory");
        TestLogMessages.TraceSample(logger);
        TestLogMessages.DebugSample(logger);
        TestLogMessages.StartedProcessing(logger);
        TestLogMessages.ValidationFailed(logger);
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom"));
        return collector;
    }

    // --- HasLogged + Once ---

    /// <summary>
    /// Verifies the canonical happy path: <c>HasLogged().AtLevel(...).Containing(...).Once()</c>
    /// passes when exactly one record matches both filters. The seeded collector has exactly
    /// one Warning record containing "validation failed", so the assertion succeeds.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasLoggedOncePassesWhenExactlyOneMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Containing("validation failed", StringComparison.Ordinal).Once();
    }

    /// <summary>
    /// Verifies that <c>Once()</c> throws <see cref="AssertionException"/> when zero records
    /// match. The seeded collector has no Critical-level entries, so asserting one yields
    /// the failure path. Pins the throw shape — without it, downstream tests can't rely on
    /// failures being observable.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasLoggedOnceThrowsWhenNoMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical).Once())
            .Throws<AssertionException>();
    }

    // --- AtLevel filter ---

    /// <summary>
    /// Verifies <c>AtLevel</c> isolates records at the exact level requested. The seed has
    /// exactly one record at each of Trace / Debug / Information / Warning / Error and zero
    /// at Critical, so a per-level <c>Once()</c> sweep plus a <c>HasNotLogged</c> on Critical
    /// pins the level-equality semantics — a mutation that flips it to <c>&gt;=</c> or
    /// <c>&lt;=</c> would make the sweep fail.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtLevelFiltersCorrectlyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Trace).Once();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Debug).Once();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Information).Once();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Once();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Error).Once();
        await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Critical);
    }

    // --- Containing filter ---

    /// <summary>
    /// Verifies <c>Containing</c> matches records whose message includes the given substring
    /// (ordinal). Two scenarios pin the contract: the partial-word case
    /// (<c>"validation"</c> matches <c>"validation failed: ..."</c>) and the start-of-string
    /// case (<c>"Started"</c> matches <c>"Started processing"</c>). A mutation flipping the
    /// comparison to case-insensitive or to start-of-string would not change the result here,
    /// but combined with <see cref="ContainingNoMatchThrows"/> below, the contract is pinned.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ContainingMatchesSubstringAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().Containing("validation", StringComparison.Ordinal).Once();
        await Assert.That(collector).HasLogged().Containing("Started", StringComparison.Ordinal).Once();
    }

    /// <summary>
    /// Verifies <c>Containing</c> with a never-present substring fails the <c>Once()</c>
    /// terminator. Pins the no-match-throws shape so the positive matches in
    /// <see cref="ContainingMatchesSubstring"/> aren't false positives.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ContainingNoMatchThrowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().Containing("xyz-not-found", StringComparison.Ordinal).Once())
            .Throws<AssertionException>();
    }

    // --- WithMessage predicate ---

    /// <summary>
    /// Verifies <c>WithMessage</c> applies a caller-supplied predicate to each record's
    /// <see cref="FakeLogRecord.Message"/>. The predicate <c>StartsWith("Started", Ordinal)</c>
    /// matches exactly the seed's Information record, pinning the predicate-evaluation contract
    /// and the per-record visit pattern.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithMessageCustomPredicateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().WithMessage(m => m.StartsWith("Started", StringComparison.Ordinal)).Once();
    }

    // --- WithException filter ---

    /// <summary>
    /// Verifies <c>WithException&lt;T&gt;</c> matches records whose
    /// <see cref="FakeLogRecord.Exception"/> is assignable to <typeparamref>T</typeparamref>.
    /// The seed has one <see cref="InvalidOperationException"/>; both the exact-type query
    /// and the base-type query (<see cref="Exception"/>) match it, pinning the
    /// <c>is TException</c> assignability contract.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithExceptionMatchesTypeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().WithException<InvalidOperationException>().Once();
        await Assert.That(collector).HasLogged().WithException<Exception>().Once();
    }

    /// <summary>
    /// Verifies <c>WithException&lt;T&gt;</c> with an unrelated exception type
    /// (<see cref="ArgumentException"/> versus the seeded <see cref="InvalidOperationException"/>)
    /// fails the <c>Once()</c> terminator. Pins the assignability semantics — a mutation that
    /// matched on type-name string instead of <see langword="is"/> assignability would not catch this.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithExceptionNoMatchThrowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithException<ArgumentException>().Once())
            .Throws<AssertionException>();
    }

    // --- WithProperty filter ---

    /// <summary>
    /// Verifies <c>WithProperty</c> matches records whose structured-state contains the given
    /// key/value pair (ordinal). Uses a fresh collector seeded with a single
    /// <c>logger.LogWarning("Item {ItemId} failed", "42")</c> — the structured-state entry
    /// <c>ItemId = "42"</c> is what the assertion targets, pinning the
    /// <c>GetStructuredStateValue</c> + ordinal-equality contract.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithPropertyMatchesKeyValueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("PropTest");
        TestLogMessages.ItemFailed(logger, "42");

        await Assert.That(collector).HasLogged().WithProperty("ItemId", "42").Once();
    }

    /// <summary>
    /// Verifies <c>WithProperty</c> with a value that doesn't match (asks for "99" against a
    /// record bearing <c>ItemId = "42"</c>) fails the <c>Once()</c> terminator. Pins the
    /// value-equality contract — a mutation that compared keys only and ignored values would
    /// not catch this.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithPropertyWrongValueThrowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("PropTest");
        TestLogMessages.ItemFailed(logger, "42");

        await Assert.That(async () => await Assert.That(collector).HasLogged().WithProperty("ItemId", "99").Once())
            .Throws<AssertionException>();
    }

    // --- Exactly terminator ---

    /// <summary>
    /// Verifies <c>Exactly(n)</c> passes for both the no-filter case (5 records → expect 5)
    /// and the filtered case (1 Warning record → expect 1). Pins the terminator's count-
    /// matching contract on both ends of the filter chain.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ExactlyPassesOnCorrectCountAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().Exactly(5);
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Exactly(1);
    }

    /// <summary>
    /// Verifies <c>Exactly(n)</c> throws when the count differs (asks for 3 Warnings against
    /// a seed with 1). Pins the must-equal-not-just-be-bounded contract — a mutation that
    /// implemented <c>Exactly</c> as <c>AtLeast</c> would slip through this test.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ExactlyThrowsWhenCountDiffersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Exactly(3))
            .Throws<AssertionException>();
    }

    // --- AtLeast terminator ---

    /// <summary>
    /// Verifies <c>AtLeast(n)</c> passes when the actual count meets or exceeds the floor.
    /// Two rows: <c>AtLeast(1)</c> (5 records ≥ 1) and <c>AtLeast(5)</c> (5 records ≥ 5,
    /// the boundary). The boundary case prevents a mutation that uses <c>&gt;</c> instead
    /// of <c>&gt;=</c> from passing.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtLeastPassesWhenEnoughMatchesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().AtLeast(1);
        await Assert.That(collector).HasLogged().AtLeast(5);
    }

    /// <summary>
    /// Verifies <c>AtLeast(n)</c> throws when the actual count is below the floor (asks for
    /// 10 records against a seed with 5). Pins the under-the-floor failure shape.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtLeastThrowsWhenTooFewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtLeast(10))
            .Throws<AssertionException>();
    }

    // --- AtMost terminator ---

    /// <summary>
    /// Verifies <c>AtMost(n)</c> passes when the actual count is at or below the ceiling.
    /// Two rows: <c>AtMost(5)</c> (5 records ≤ 5, the boundary) and <c>AtMost(10)</c> (5 ≤ 10,
    /// well under). The boundary case prevents a mutation that uses <c>&lt;</c> instead of
    /// <c>&lt;=</c> from passing.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtMostPassesWhenFewEnoughAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().AtMost(5);
        await Assert.That(collector).HasLogged().AtMost(10);
    }

    /// <summary>
    /// Verifies <c>AtMost(n)</c> throws when the actual count exceeds the ceiling (asks for
    /// at most 1 against a seed with 5). Pins the over-the-ceiling failure shape.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtMostThrowsWhenTooManyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtMost(1))
            .Throws<AssertionException>();
    }

    // --- Never terminator ---

    /// <summary>
    /// Verifies <c>Never()</c> passes when zero records match. Filtering on Critical (none
    /// in the seed) followed by <c>Never()</c> succeeds, pinning the zero-match-passes shape.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NeverPassesWhenNoMatchesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical).Never();
    }

    /// <summary>
    /// Verifies <c>Never()</c> throws when at least one record matches. Filtering on Warning
    /// (1 in the seed) followed by <c>Never()</c> fails, pinning the asymmetry with
    /// <see cref="NeverPassesWhenNoMatches"/>.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NeverThrowsWhenRecordsMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Never())
            .Throws<AssertionException>();
    }

    // --- HasNotLogged ---

    /// <summary>
    /// Verifies the <c>HasNotLogged</c> entry point passes when zero records match the filter
    /// chain (no terminator required — the entry point itself implies "exactly 0 matches").
    /// Two rows: a level filter (Critical) and a substring filter (an absent token), each
    /// independently confirming the implicit-zero contract.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasNotLoggedPassesWhenNoMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Critical);
        await Assert.That(collector).HasNotLogged().Containing("xyz-absent", StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the <c>HasNotLogged</c> entry point throws when at least one record matches.
    /// Filtering on Warning (1 in the seed) makes <c>HasNotLogged</c> fail, pinning the
    /// asymmetry with <see cref="HasNotLoggedPassesWhenNoMatch"/>.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasNotLoggedThrowsWhenRecordsMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Warning))
            .Throws<AssertionException>();
    }

    // --- Filter combinations ---

    /// <summary>
    /// Verifies that multiple filters chain as a logical AND. The seed's exception-bearing
    /// Error record satisfies all three filters simultaneously: level Error, exception type
    /// <see cref="InvalidOperationException"/>, and message containing "failed". The
    /// <c>Once()</c> terminator confirms exactly one match — a mutation that turned the
    /// chain into OR semantics would yield more matches and fail.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task CombinedFiltersNarrowResultsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Error).WithException<InvalidOperationException>().Containing("failed", StringComparison.Ordinal).Once();
    }

    /// <summary>
    /// Verifies a filter combination with no satisfying record (asks for a Warning bearing
    /// any exception, but the seed's Warning has no exception) fails the <c>Once()</c>
    /// terminator. Pins the AND-semantics from
    /// <see cref="CombinedFiltersNarrowResults"/> in the negative direction.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task CombinedFiltersNoMatchThrowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).WithException<Exception>().Once())
            .Throws<AssertionException>();
    }

    // --- Empty collector ---

    /// <summary>
    /// Verifies the empty-collector boundary. <c>Never()</c> passes (zero records ≥ zero matches);
    /// <c>Once()</c> throws (zero records ≠ one match). Pins the no-records-yet boundary so a
    /// regression in collector-traversal logic surfaces here rather than confusing later tests
    /// that assume a populated seed.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task EmptyCollectorNeverPassesOnceThrowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        await Assert.That(collector).HasLogged().Never();
        await Assert.That(async () => await Assert.That(collector).HasLogged().Once())
            .Throws<AssertionException>();
    }

    // --- Exception message quality ---

    /// <summary>
    /// Pins the failure-message shape that solves the historical Console.WriteLine-debugging
    /// friction. On a no-match failure (<c>AtLevel(Critical).Once()</c> against the seed),
    /// the thrown <see cref="AssertionException"/>'s message contains four expected substrings:
    /// the terminator description (<c>"exactly 1 log record(s)"</c>), the actual count
    /// (<c>"0 record(s) matched"</c>), the captured-records header
    /// (<c>"Captured records (5 total):"</c>), and at least one record level marker
    /// (<c>"[warn]"</c>, the 4-character abbreviation matching the MEL console formatter).
    /// A mutation that drops any of these breaks dev-time diagnosis.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ExceptionMessageContainsAllPartsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        AssertionException? ex = await Assert
            .That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical).Once())
            .Throws<AssertionException>();

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("exactly 1 log record(s)");
        await Assert.That(ex.Message).Contains("0 record(s) matched");
        await Assert.That(ex.Message).Contains("Captured records (5 total):");
        await Assert.That(ex.Message).Contains("[warn]");
    }

    // --- No filters + terminators ---

    /// <summary>
    /// Verifies the no-filter case — every terminator counts the entire collector. Three rows
    /// pin the three boundary terminators on the seed of 5 records: <c>Exactly(5)</c>,
    /// <c>AtLeast(1)</c>, <c>AtMost(10)</c>. A mutation that introduced an implicit filter
    /// (e.g., always filter to Information+) would make at least one row fail.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NoFiltersTerminatorsMatchAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();
        await Assert.That(collector).HasLogged().Exactly(5);
        await Assert.That(collector).HasLogged().AtLeast(1);
        await Assert.That(collector).HasLogged().AtMost(10);
    }

    // --- WithCategory filter ---

    /// <summary>
    /// Verifies that <c>WithCategory</c> filters by exact logger-category-name match (ordinal).
    /// Seeds two loggers with different categories; pins that <c>WithCategory("CategoryA")</c>
    /// matches only records emitted via the "CategoryA" logger and ignores those from "CategoryB".
    /// A mutation that case-folded or substring-matched the comparison would break this test.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithCategoryMatchesExactCategoryNameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });

        ILogger loggerA = factory.CreateLogger("CategoryA");
        ILogger loggerB = factory.CreateLogger("CategoryB");
        TestLogMessages.First(loggerA);
        TestLogMessages.Second(loggerB);
        TestLogMessages.Third(loggerA);

        await Assert.That(collector).HasLogged().WithCategory("CategoryA").Exactly(2);
        await Assert.That(collector).HasLogged().WithCategory("CategoryB").Once();
        await Assert.That(collector).HasNotLogged().WithCategory("MissingCategory");
    }

    // --- Argument validation on filters ---

    /// <summary>
    /// Verifies that filter methods reject null arguments via <see cref="ArgumentNullException"/>.
    /// One assertion per public method that accepts a reference-type argument; pins the
    /// <c>ArgumentNullException.ThrowIfNull</c> guard added on each.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FilterMethodsRejectNullArgumentsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();

        // HasLogged path — null guards applied uniformly via the shared base class.
        await Assert.That(async () => await Assert.That(collector).HasLogged().Containing(null!, StringComparison.Ordinal)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithMessage(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithProperty(null!, "value")).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithCategory(null!)).Throws<ArgumentNullException>();

        // HasNotLogged path — same guards, separate code-path through the inverse entry point.
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().Containing(null!, StringComparison.Ordinal)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().WithMessage(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().WithProperty(null!, "value")).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().WithCategory(null!)).Throws<ArgumentNullException>();
    }

    // --- Argument validation on terminators ---

    /// <summary>
    /// Verifies that count-based terminators reject negative values via
    /// <see cref="ArgumentOutOfRangeException"/>. Pins the <c>ThrowIfNegative</c> guard on
    /// <c>Exactly</c>, <c>AtLeast</c>, and <c>AtMost</c>; <c>Once</c> and <c>Never</c> take
    /// no arguments and are excluded.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NegativeCountTerminatorsAreRejectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();

        await Assert.That(async () => await Assert.That(collector).HasLogged().Exactly(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtLeast(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().AtMost(-1)).Throws<ArgumentOutOfRangeException>();
    }

    // --- TUnit .And chaining ---

    /// <summary>
    /// Verifies that two log assertions can be combined via TUnit's <c>.And</c> operator
    /// (inherited from <c>Assertion&lt;T&gt;</c>). Pins both that the chain compiles AND
    /// that both sides are evaluated against the same collector.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AndChainingCombinesAssertionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Information).AtLeast(1)
            .And.HasNotLogged().AtLevel(LogLevel.Critical);
    }

    // --- WithEventId / WithEventName filters ---

    /// <summary>
    /// Verifies <c>WithEventId(int)</c> matches records by their numeric event ID and
    /// <c>WithEventName(string)</c> matches by event name. Seeds two records with distinct
    /// (id, name) pairs and pins both filters can isolate each.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithEventIdAndWithEventNameFilterCorrectlyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("EventTest");
        TestLogMessages.AppStarted(logger);   // EventId 100, Name "Bootstrap"
        TestLogMessages.AppStopped(logger);   // EventId 200, Name "Shutdown"

        await Assert.That(collector).HasLogged().WithEventId(100).Once();
        await Assert.That(collector).HasLogged().WithEventId(200).Once();
        await Assert.That(collector).HasNotLogged().WithEventId(999);

        await Assert.That(collector).HasLogged().WithEventName("Bootstrap").Once();
        await Assert.That(collector).HasLogged().WithEventName("Shutdown").Once();
        await Assert.That(collector).HasNotLogged().WithEventName("Missing");
    }

    // --- WithScope filter ---

    /// <summary>
    /// Marker scope state type used to verify <c>WithScope&lt;TScope&gt;</c> matches by type identity.
    /// Defined as a record so equality is structural; the assertion only checks type membership.
    /// </summary>
    /// <param name="OperationId">An identifier for the active operation; not asserted on.</param>
    private sealed record OperationScope(string OperationId);

    /// <summary>
    /// Verifies <c>WithScope&lt;TScope&gt;</c> matches records emitted while a scope of the
    /// specified type was active on the logger. Two log calls bracketed by a scope vs. one
    /// outside it pin the scope-presence semantics.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithScopeMatchesRecordsInsideScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("ScopeTest");

        TestLogMessages.AppStarted(logger);   // outside any scope
        using (logger.BeginScope(new OperationScope("op-42")))
        {
            TestLogMessages.CycleStarted(logger, 1);
            TestLogMessages.CycleFinished(logger, 1);
        }
        TestLogMessages.AppStopped(logger);   // outside any scope

        await Assert.That(collector).HasLogged().WithScope<OperationScope>().Exactly(2);
        await Assert.That(collector).HasNotLogged().WithScope<OperationScope>().WithEventId(100);
    }

    // --- Where escape-hatch filter ---

    /// <summary>
    /// Verifies <c>Where(Func&lt;FakeLogRecord, bool&gt;)</c> applies a caller-supplied
    /// predicate directly to each record. Pins the escape-hatch contract: anything not
    /// expressible as a built-in filter can still be matched without extending the API.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WhereAppliesCustomRecordPredicateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged()
            .Where(r => r.Level >= LogLevel.Warning && r.Message.Length > 0)
            .Exactly(2); // Warning + Error from the seed
    }

    // --- HasLoggedSequence with Then ---

    /// <summary>
    /// Verifies <c>HasLoggedSequence</c> matches records in order, with <c>Then()</c> committing
    /// each step. The seed simulates a typical cycle (Start → ValidationFail → Finished); the
    /// assertion pins that the order-preserving walk advances exactly once per matched record.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasLoggedSequenceMatchesOrderedRecordsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("SeqTest");
        TestLogMessages.CycleStarted(logger, 1);
        TestLogMessages.CycleValidationFailed(logger, 1);
        TestLogMessages.CycleFinished(logger, 1);

        await Assert.That(collector).HasLoggedSequence()
            .AtLevel(LogLevel.Information).Containing("started", StringComparison.Ordinal)
            .Then().AtLevel(LogLevel.Warning).Containing("validation failed", StringComparison.Ordinal)
            .Then().AtLevel(LogLevel.Information).Containing("finished", StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <c>HasLoggedSequence</c> fails when an expected step never matches. Pins the
    /// failure-path contract — without it, the positive sequence test could be a false positive
    /// against an implementation that always returns success.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasLoggedSequenceFailsWhenStepNotMatchedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("SeqTest");
        TestLogMessages.CycleStarted(logger, 1);
        TestLogMessages.CycleFinished(logger, 1); // No validation-failed record between

        await Assert.That(async () => await Assert.That(collector).HasLoggedSequence()
                .AtLevel(LogLevel.Information).Containing("started", StringComparison.Ordinal)
                .Then().AtLevel(LogLevel.Warning)
                .Then().AtLevel(LogLevel.Information).Containing("finished", StringComparison.Ordinal))
            .Throws<AssertionException>();
    }

    /// <summary>
    /// Verifies <c>HasLoggedSequence</c> respects strict order: a sequence that exists in the
    /// records but in the wrong order fails. The collector here has Information then Warning,
    /// but the assertion asks for Warning-then-Information — pins that the walk only advances
    /// forward through records.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task HasLoggedSequenceFailsWhenRecordsInWrongOrderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("SeqTest");
        TestLogMessages.CycleStarted(logger, 1);            // Information
        TestLogMessages.CycleValidationFailed(logger, 1);   // Warning

        await Assert.That(async () => await Assert.That(collector).HasLoggedSequence()
                .AtLevel(LogLevel.Warning)
                .Then().AtLevel(LogLevel.Information))
            .Throws<AssertionException>();
    }

    // --- AtLevelOrAbove / AtLevelOrBelow filters ---

    /// <summary>
    /// Verifies the level-range filters. The seed has one record per level (Trace through Error).
    /// <c>AtLevelOrAbove(Warning)</c> matches Warning + Error (2 records); <c>AtLevelOrBelow(Debug)</c>
    /// matches Trace + Debug (2 records). Pins the >= and &lt;= comparisons against the LogLevel enum
    /// ordinals, which is the right semantic for the typical "no error or warning logged" pattern.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtLevelOrAboveAndOrBelowFilterByOrdinalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged().AtLevelOrAbove(LogLevel.Warning).Exactly(2);
        await Assert.That(collector).HasLogged().AtLevelOrBelow(LogLevel.Debug).Exactly(2);
        await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Critical);
    }

    // --- Containing(string, StringComparison) overload ---

    /// <summary>
    /// Verifies the case-insensitive <c>Containing</c> overload. The seed has a Warning record
    /// "validation failed: TimeoutMs out of range" — the lowercase "validation" matches by ordinal,
    /// the uppercase "VALIDATION" only matches with <c>OrdinalIgnoreCase</c>. Pins both that the
    /// overload exists AND that it routes the comparison to the underlying string contains call.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ContainingHonoursStringComparisonOverloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged().Containing("VALIDATION", StringComparison.OrdinalIgnoreCase).Once();
        await Assert.That(collector).HasNotLogged().Containing("VALIDATION", StringComparison.Ordinal);
    }

    // --- Between terminator ---

    /// <summary>
    /// Verifies the <c>Between(min, max)</c> terminator on inclusive bounds. The seed has 5 records;
    /// asserting <c>Between(3, 7)</c> passes (5 falls in range). The boundary tests
    /// <c>Between(5, 5)</c> (equivalent to <c>Exactly(5)</c>) and <c>Between(0, 4)</c> (fails because
    /// 5 &gt; 4) pin the inclusivity semantics.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task BetweenTerminatorMatchesInclusiveRangeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged().Between(3, 7);
        await Assert.That(collector).HasLogged().Between(5, 5);
        await Assert.That(async () => await Assert.That(collector).HasLogged().Between(0, 4))
            .Throws<AssertionException>();
    }

    /// <summary>
    /// Verifies <c>Between</c> rejects invalid bounds: negative min, and max less than min.
    /// Pins the <c>ThrowIfNegative</c> + <c>ThrowIfLessThan</c> guards.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task BetweenRejectsInvalidBoundsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();

        await Assert.That(async () => await Assert.That(collector).HasLogged().Between(-1, 5)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().Between(5, 3)).Throws<ArgumentOutOfRangeException>();
    }

    // --- WithExceptionMessage filter ---

    /// <summary>
    /// Verifies <c>WithExceptionMessage</c> matches records whose exception's message contains
    /// the substring (ordinal). Composed with <c>WithException&lt;T&gt;</c> for the typical
    /// "specific exception type with specific message" pattern. The seed has one Error record
    /// bearing <c>InvalidOperationException("boom")</c> — both filters together match it.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithExceptionMessageMatchesExceptionMessageSubstringAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged().WithExceptionMessage("boom").Once();
        await Assert.That(collector).HasLogged().WithException<InvalidOperationException>().WithExceptionMessage("boom").Once();
        await Assert.That(collector).HasNotLogged().WithExceptionMessage("nope");
    }

    // --- WithMessageTemplate filter ---

    /// <summary>
    /// Verifies <c>WithMessageTemplate</c> matches the pre-substitution form recorded under the
    /// MEL <c>{OriginalFormat}</c> key, distinct from the formatted message. <c>ItemFailed</c>
    /// is logged with parameter <c>"X-9"</c>: the formatted message is <c>"Item X-9 failed"</c>
    /// but the template is <c>"Item {ItemId} failed"</c>. Asserting on the template lets tests
    /// pin the call-site without coupling to the substituted value.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithMessageTemplateMatchesOriginalFormatAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.ItemFailed(logger, "X-9");

        await Assert.That(collector).HasLogged().WithMessageTemplate("Item {ItemId} failed").Once();
        await Assert.That(collector).HasNotLogged().WithMessageTemplate("Item X-9 failed");
        await Assert.That(collector).HasNotLogged().WithMessageTemplate("Other template");
    }

    // --- WithProperty predicate overload ---

    /// <summary>
    /// Verifies the predicate overload of <c>WithProperty</c>: applies a <c>Func&lt;string?, bool&gt;</c>
    /// to the formatted property value (FakeLogRecord stores structured-state values as strings).
    /// Use case is range or pattern matching where exact equality is too strict — here we accept
    /// any cycle number that parses to an even integer.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithPropertyPredicateAppliesToFormattedValueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.CycleStarted(logger, 1);
        TestLogMessages.CycleStarted(logger, 2);
        TestLogMessages.CycleStarted(logger, 3);

        await Assert.That(collector).HasLogged()
            .WithProperty("CycleNumber", v => int.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var n) && n % 2 == 0).Once();
        await Assert.That(collector).HasLogged()
            .WithProperty("CycleNumber", v => v is not null).Exactly(3);
    }

    /// <summary>
    /// Verifies the predicate overload of <c>WithProperty</c> rejects null arguments. Pins the
    /// <c>ArgumentNullException.ThrowIfNull</c> guards on both <c>key</c> and <c>predicate</c>
    /// so an accidental null surfaces immediately rather than via a misleading non-match.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithPropertyPredicateRejectsNullArgsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();

        await Assert.That(async () => await Assert.That(collector).HasLogged().WithProperty(null!, _ => true))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithProperty("k", (Func<string?, bool>)null!))
            .Throws<ArgumentNullException>();
    }

    // --- WithScopeProperty filter ---

    /// <summary>
    /// Verifies <c>WithScopeProperty</c> matches records emitted within a dictionary-shaped
    /// scope (the most common AOT-friendly pattern). The collector is seeded with one record
    /// inside a scope of <c>OrderId=42, RequestId="abc"</c>, exercising both string and integer
    /// values. <see cref="object.Equals(object?, object?)"/> semantics apply, so boxed integers
    /// must match the boxed expected value's underlying type.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithScopePropertyMatchesDictionaryScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        var scope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["OrderId"] = 42,
            ["RequestId"] = "abc",
        };
        using (logger.BeginScope(scope))
            TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithScopeProperty("OrderId", 42).Once();
        await Assert.That(collector).HasLogged().WithScopeProperty("RequestId", "abc").Once();
        await Assert.That(collector).HasNotLogged().WithScopeProperty("OrderId", 99);
        await Assert.That(collector).HasNotLogged().WithScopeProperty("Missing", "anything");
    }

    /// <summary>
    /// Verifies <c>WithScopeProperty</c> matches records emitted within a message-template
    /// scope (<c>logger.BeginScope("X {Key}", value)</c>). Internally MEL produces a
    /// <c>FormattedLogValues</c> instance which exposes the key-value pairs we read.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithScopePropertyMatchesFormattedScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        using (TestLogMessages.OrderScope(logger, 42))
            TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithScopeProperty("OrderId", 42).Once();
    }

    /// <summary>
    /// Verifies records logged outside any scope are not matched by <c>WithScopeProperty</c>,
    /// and that the predicate overload accepts a custom value test. Ensures filtering does not
    /// accidentally match the absence of a scope.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithScopePropertyPredicateAndAbsentScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        TestLogMessages.StartedProcessing(logger);

        var scoped = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Count"] = 17 };
        using (logger.BeginScope(scoped))
            TestLogMessages.ValidationFailed(logger);

        await Assert.That(collector).HasLogged().WithScopeProperty("Count", v => v is int n && n > 10).Once();
        await Assert.That(collector).HasNotLogged().WithScopeProperty("Count", v => v is int n && n > 100);
        await Assert.That(collector).HasNotLogged().WithScopeProperty("AnyKey", _ => true).WithCategory("Test")
            .Containing("Started", StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies <c>WithScopeProperty</c> rejects null arguments on both overloads.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithScopePropertyRejectsNullArgsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();

        await Assert.That(async () => await Assert.That(collector).HasLogged().WithScopeProperty(null!, "x"))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithScopeProperty(null!, _ => true))
            .Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithScopeProperty("k", (Func<object?, bool>)null!))
            .Throws<ArgumentNullException>();
    }

    // --- Failure-message format ---

    /// <summary>
    /// Verifies the failure-message format pins three contracts: 4-character level abbreviations
    /// (matching the MEL console formatter — <c>info</c>, <c>warn</c>, <c>fail</c>), an indented
    /// <c>props:</c> line listing structured properties (excluding the magic
    /// <c>{OriginalFormat}</c> key), and an indented <c>scope:</c> line rendering scope content
    /// as <c>key=value</c> pairs. These are the user-visible debugging surfaces; changing them
    /// is a breaking-output change and must remain test-pinned.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FailureMessageRendersAbbreviatedLevelPropsAndScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        var scope = new Dictionary<string, object?>(StringComparer.Ordinal) { ["OrderId"] = 42 };
        using (logger.BeginScope(scope))
            TestLogMessages.ItemFailed(logger, "X-9");

        AssertionException? ex = await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical))
            .Throws<AssertionException>();
        await Assert.That(ex).IsNotNull();

        var msg = ex!.Message;
        await Assert.That(msg).Contains("[warn]");
        await Assert.That(msg).Contains("Item X-9 failed");
        await Assert.That(msg).Contains("props: ItemId=X-9");
        await Assert.That(msg).Contains("scope: OrderId=42");
        await Assert.That(msg).DoesNotContain("{OriginalFormat}");
    }

    // --- Edge cases: nested scopes ---

    /// <summary>
    /// Verifies that with multiple scopes simultaneously active (outer + inner),
    /// <c>WithScopeProperty</c> matches when the key is present in any of the active scopes.
    /// FakeLogRecord exposes all currently-active scopes on the calling logger, and the filter
    /// walks every scope; chaining two <c>WithScopeProperty</c> calls AND-combines them.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task NestedScopesAreAllInspectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        var outer = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Outer"] = "yes" };
        var inner = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Inner"] = 42 };
        using (logger.BeginScope(outer))
        using (logger.BeginScope(inner))
            TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithScopeProperty("Outer", "yes").Once();
        await Assert.That(collector).HasLogged().WithScopeProperty("Inner", 42).Once();
        await Assert.That(collector).HasLogged()
            .WithScopeProperty("Outer", "yes").WithScopeProperty("Inner", 42).Once();
    }

    /// <summary>
    /// Verifies that when outer and inner scopes carry the same key with different values,
    /// <c>WithScopeProperty(key, value)</c> matches against either value independently — the
    /// predicate walks every active scope. A consumer relying on this can be confident that
    /// pushing a context value in a nested scope shadows the outer for tests, not just for
    /// runtime structured logging.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task SameScopeKeyInOuterAndInnerScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        var outer = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Layer"] = "outer" };
        var inner = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Layer"] = "inner" };
        using (logger.BeginScope(outer))
        using (logger.BeginScope(inner))
            TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithScopeProperty("Layer", "outer").Once();
        await Assert.That(collector).HasLogged().WithScopeProperty("Layer", "inner").Once();
    }

    // --- Edge cases: unicode ---

    /// <summary>
    /// Verifies <c>Containing</c> handles unicode correctly under both ordinal and
    /// ordinal-ignore-case comparisons. Pins the contract that string-comparison behaviour
    /// is delegated to the BCL without our code interposing surprising normalisation.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task UnicodeMessagesMatchExpectedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.UnicodeMessage(logger, "abc");

        await Assert.That(collector).HasLogged().Containing("ünïcödé", StringComparison.Ordinal).Once();
        await Assert.That(collector).HasLogged().Containing("ÜNÏCÖDÉ", StringComparison.OrdinalIgnoreCase).Once();
        await Assert.That(collector).HasNotLogged().Containing("ÜNÏCÖDÉ", StringComparison.Ordinal);
    }

    // --- Edge cases: concurrency ---

    /// <summary>
    /// Verifies records emitted concurrently from multiple threads are all captured in the
    /// snapshot. <see cref="FakeLogCollector"/> is documented as thread-safe; this test pins
    /// our reliance on that contract — a regression in MEL that broke FakeLogCollector
    /// concurrency would surface here rather than in production-suspecting silence.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ConcurrentLoggerCallsCaptureAllRecordsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        const int threads = 4;
        const int iterations = 250;

        Task[] tasks = [.. Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
                TestLogMessages.StartedProcessing(logger);
        }, cancellationToken))];
        await Task.WhenAll(tasks).ConfigureAwait(false);

        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Information).Exactly(threads * iterations);
    }

    // --- Edge cases: large snapshot ---

    /// <summary>
    /// Verifies the assertion path scales to large captured-record sets without pathological
    /// slowdown. 1,000 records is large enough to expose O(n²) bugs in filter evaluation but
    /// small enough to stay well under the 10-second test timeout.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task LargeSnapshotIsHandledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        const int total = 1000;
        for (var i = 0; i < total; i++)
            TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Information).Exactly(total);
        await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Error);
    }

    // --- Edge cases: sequence corner cases ---

    /// <summary>
    /// Verifies a step with zero filters in <c>HasLoggedSequence</c> is silently skipped — the
    /// sequence walk advances to the next step without consuming a record. Pins the documented
    /// behaviour: empty step never affects matching.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task SequenceEmptyStepIsSkippedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);
        TestLogMessages.Third(logger);

        await Assert.That(collector).HasLoggedSequence()
            .AtLevel(LogLevel.Information)
            .Then()                              // empty step — skipped
            .Then().AtLevel(LogLevel.Warning);
    }

    /// <summary>
    /// Verifies <c>HasLoggedSequence</c> handles a long chain (5+ steps) without losing track
    /// of position. The walk is order-preserving and skips records between matches, so the test
    /// seeds noise records (Debug entries) between expected matches to confirm the skip logic
    /// holds across many steps.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task SequenceLongChainAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("Test");

        for (var i = 1; i <= 5; i++)
        {
            TestLogMessages.CycleStarted(logger, i);
            TestLogMessages.DebugSample(logger);
            TestLogMessages.CycleFinished(logger, i);
        }

        await Assert.That(collector).HasLoggedSequence()
            .AtLevel(LogLevel.Information).Containing("Cycle 1 started", StringComparison.Ordinal)
            .Then().AtLevel(LogLevel.Information).Containing("Cycle 2 started", StringComparison.Ordinal)
            .Then().AtLevel(LogLevel.Information).Containing("Cycle 3 started", StringComparison.Ordinal)
            .Then().AtLevel(LogLevel.Information).Containing("Cycle 4 started", StringComparison.Ordinal)
            .Then().AtLevel(LogLevel.Information).Containing("Cycle 5 finished", StringComparison.Ordinal);
    }

    // --- Edge cases: filter ordering invariance ---

    /// <summary>
    /// Verifies filter ordering does not change semantics — the same filter set in any order
    /// produces the same match. Pins the AND-combine contract documented for the filter chain;
    /// guards against a regression where one filter type's evaluation accidentally short-circuits
    /// another.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FilterOrderDoesNotChangeMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = CreateCollectorWithSampleRecords();

        await Assert.That(collector).HasLogged()
            .AtLevel(LogLevel.Warning).Containing("validation failed", StringComparison.Ordinal).Once();
        await Assert.That(collector).HasLogged()
            .Containing("validation failed", StringComparison.Ordinal).AtLevel(LogLevel.Warning).Once();
    }

    // --- Edge cases: case sensitivity ---

    /// <summary>
    /// Verifies <c>WithCategory</c> uses ordinal (case-sensitive) comparison. A category named
    /// "MyCategory" does not match "mycategory" or "MYCATEGORY". Pins the documented
    /// no-implicit-culture rule.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithCategoryIsCaseSensitiveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("MyCategory");
        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithCategory("MyCategory").Once();
        await Assert.That(collector).HasNotLogged().WithCategory("mycategory");
        await Assert.That(collector).HasNotLogged().WithCategory("MYCATEGORY");
    }

    // --- Edge cases: WithProperty for absent key ---

    /// <summary>
    /// Verifies <c>WithProperty</c> against an absent key returns no match for non-null
    /// expected values. Pins the typical case: assertions about a structured property value
    /// must not pass on records that don't carry that property at all.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithPropertyAbsentKeyDoesNotMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.ItemFailed(logger, "X");

        await Assert.That(collector).HasNotLogged().WithProperty("MissingKey", "X");
        await Assert.That(collector).HasNotLogged()
            .WithProperty("MissingKey", v => string.Equals(v, "X", StringComparison.Ordinal));
    }

    // --- Edge cases: WithMessageTemplate against parameterless log ---

    /// <summary>
    /// Verifies <c>WithMessageTemplate</c> matches a parameterless log entry's template (which
    /// equals the formatted message). MEL's source generator populates <c>{OriginalFormat}</c>
    /// regardless of parameter count, so the filter is meaningful even for parameterless calls.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithMessageTemplateMatchesParameterlessLogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithMessageTemplate("Started processing").Once();
        await Assert.That(collector).HasNotLogged().WithMessageTemplate("Started");
    }

    // --- Edge cases: failure-snapshot rendering corner cases ---

    /// <summary>
    /// Verifies the failure-snapshot renders multiple structured properties on a single record
    /// joined by a comma — not as separate <c>props:</c> lines, not as a single concatenated
    /// blob. Pins the comma-separator branch in the props rendering.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FailureSnapshotRendersMultiplePropertiesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.OrderForCustomer(logger, 42, "alice");

        AssertionException? ex = await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical))
            .Throws<AssertionException>();
        await Assert.That(ex).IsNotNull();

        var msg = ex!.Message;
        // Both properties present on a single comma-separated props line.
        // The MEL source generator's ordering of StructuredState entries is not stable
        // across versions, so this test does not pin which property appears first.
        await Assert.That(msg).Contains("props: ");
        await Assert.That(msg).Contains("OrderId=42");
        await Assert.That(msg).Contains("Customer=alice");
        await Assert.That(msg).Contains(", ");
    }

    /// <summary>
    /// Verifies the failure-snapshot renders multiple active scopes joined by <c>" | "</c>
    /// on a single <c>scope:</c> line, in push order (outer → inner). Pins the pipe-separator
    /// branch in the scopes rendering.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FailureSnapshotRendersMultipleScopesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        var outer = new Dictionary<string, object?>(StringComparer.Ordinal) { ["RequestId"] = "abc" };
        var inner = new Dictionary<string, object?>(StringComparer.Ordinal) { ["OrderId"] = 42 };
        using (logger.BeginScope(outer))
        using (logger.BeginScope(inner))
            TestLogMessages.StartedProcessing(logger);

        AssertionException? ex = await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical))
            .Throws<AssertionException>();
        await Assert.That(ex).IsNotNull();

        var msg = ex!.Message;
        await Assert.That(msg).Contains("scope: ");
        await Assert.That(msg).Contains("RequestId=abc");
        await Assert.That(msg).Contains("OrderId=42");
        await Assert.That(msg).Contains(" | ");
    }

    /// <summary>
    /// Verifies the failure-snapshot renders an opaque scope (one that does not implement the
    /// key-value-pair shape) via its <see cref="object.ToString"/> representation. Pins the
    /// fallthrough branch in <c>AppendScope</c> after both <c>TryAppendKeyValuePairs</c>
    /// overloads return false.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FailureSnapshotRendersOpaqueScopeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(new FakeLoggerProvider(collector)));
        ILogger logger = factory.CreateLogger("Test");

        using (logger.BeginScope(new OpaqueScope()))
            TestLogMessages.StartedProcessing(logger);

        AssertionException? ex = await Assert.That(async () => await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical))
            .Throws<AssertionException>();
        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Message).Contains("scope: opaque-scope-token");
    }

    /// <summary>
    /// Verifies the failure-snapshot renders 4-character abbreviations for every standard
    /// <see cref="LogLevel"/>. Pins the <c>LevelAbbreviation</c> switch — a regression that
    /// dropped one of the level arms would surface here. The pre-existing
    /// <see cref="ExceptionMessageContainsAllPartsAsync"/> only covers <c>warn</c>; this
    /// covers the rest.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FailureSnapshotAbbreviatesEveryLogLevelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("Test");
        TestLogMessages.TraceSample(logger);
        TestLogMessages.DebugSample(logger);
        TestLogMessages.StartedProcessing(logger);    // info
        TestLogMessages.ValidationFailed(logger);     // warn
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("x")); // fail
        TestLogMessages.CriticalSample(logger);

        AssertionException? ex = await Assert.That(async () => await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Information))
            .Throws<AssertionException>();
        await Assert.That(ex).IsNotNull();

        var msg = ex!.Message;
        await Assert.That(msg).Contains("[trce]");
        await Assert.That(msg).Contains("[dbug]");
        await Assert.That(msg).Contains("[info]");
        await Assert.That(msg).Contains("[warn]");
        await Assert.That(msg).Contains("[fail]");
        await Assert.That(msg).Contains("[crit]");
    }

    /// <summary>
    /// Stand-in for an opaque scope object. Doesn't implement
    /// <see cref="IEnumerable{T}"/> over <see cref="KeyValuePair{TKey, TValue}"/>, so the
    /// failure-snapshot must render it through <see cref="object.ToString"/>.
    /// </summary>
    private sealed class OpaqueScope
    {
        public override string ToString() => "opaque-scope-token";
    }
}
