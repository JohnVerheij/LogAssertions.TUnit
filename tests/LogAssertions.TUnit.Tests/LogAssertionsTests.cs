using System;
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
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Warning).Containing("validation failed").Once();
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
        await Assert.That(collector).HasLogged().Containing("validation").Once();
        await Assert.That(collector).HasLogged().Containing("Started").Once();
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
        await Assert.That(async () => await Assert.That(collector).HasLogged().Containing("xyz-not-found").Once())
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
        await Assert.That(collector).HasNotLogged().Containing("xyz-absent");
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
        await Assert.That(collector).HasLogged().AtLevel(LogLevel.Error).WithException<InvalidOperationException>().Containing("failed").Once();
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
    /// (<c>"[Warning]"</c>). A mutation that drops any of these breaks dev-time diagnosis.
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
        await Assert.That(ex.Message).Contains("[Warning]");
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
        await Assert.That(async () => await Assert.That(collector).HasLogged().Containing(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithMessage(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithProperty(null!, "value")).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithCategory(null!)).Throws<ArgumentNullException>();

        // HasNotLogged path — same guards, separate code-path through the inverse entry point.
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().Containing(null!)).Throws<ArgumentNullException>();
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
            .AtLevel(LogLevel.Information).Containing("started")
            .Then().AtLevel(LogLevel.Warning).Containing("validation failed")
            .Then().AtLevel(LogLevel.Information).Containing("finished");
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
                .AtLevel(LogLevel.Information).Containing("started")
                .Then().AtLevel(LogLevel.Warning)
                .Then().AtLevel(LogLevel.Information).Containing("finished"))
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
}
