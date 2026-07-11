using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for the v0.12.0 log gate (<c>HasLoggedOnly</c>): every record at or above a floor must have
/// been produced by an allowed definition, records below the floor are always permitted, and a
/// capture floor above the gate's floor is reported as a vacuous gate rather than passing silently.
/// Also covers the definition-level <c>MatchingAny</c> chain method and the <c>Step</c> sequence sugar.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class HasLoggedOnlyTests
{
    private static readonly LogDefinition ValidationFailed =
        LogDefinition.Capture(TestLogMessages.ValidationFailed);

    private static readonly LogDefinition OperationFailed =
        LogDefinition.Capture(log => TestLogMessages.OperationFailed(log, new InvalidOperationException("x")));

    private static readonly LogDefinition Third = LogDefinition.Capture(TestLogMessages.Third);

    /// <summary>Builds a collector and a logger backed by it.</summary>
    /// <returns>The collector and logger pair.</returns>
    private static (FakeLogCollector Collector, ILogger Logger) CreateCollectorAndLogger()
    {
        FakeLogCollector collector = new();
        FakeLogger logger = new(collector);
        return (collector, logger);
    }

    // --- HasLoggedOnly: the gate ---

    /// <summary>Records below the floor are always allowed and never need listing.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateIgnoresEverythingBelowTheFloorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.TraceSample(logger);
        TestLogMessages.DebugSample(logger);
        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLoggedOnly(LogLevel.Warning);
    }

    /// <summary>An allowed definition at or above the floor passes the gate.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GatePassesWhenOnlyAllowedDefinitionsAtOrAboveFloorAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.DebugSample(logger);
        TestLogMessages.ValidationFailed(logger);
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom"));

        await Assert.That(collector).HasLoggedOnly(LogLevel.Warning).Allowing(ValidationFailed, OperationFailed);
    }

    /// <summary>A record at or above the floor that matches no allowed definition fails the gate,
    /// and the failure names it plus the allowlist.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateFailsOnUnexpectedRecordAndReportsItAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.ValidationFailed(logger);
        TestLogMessages.Third(logger); // Warning, not in the allowlist

        var ex = await Assert.That(async () =>
            await Assert.That(collector).HasLoggedOnly(LogLevel.Warning).Allowing(ValidationFailed))
            .Throws<AssertionException>();

        await Assert.That(ex!.Message).Contains("not in the allowlist", StringComparison.Ordinal);
        await Assert.That(ex.Message).Contains("third", StringComparison.Ordinal);
        await Assert.That(ex.Message).Contains("Allowed definitions:", StringComparison.Ordinal);
    }

    /// <summary>With no allowed definitions the gate is the clean-run check: anything at or above
    /// the floor fails it.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateWithEmptyAllowlistIsACleanRunCheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.ValidationFailed(logger);

        var ex = await Assert.That(async () =>
            await Assert.That(collector).HasLoggedOnly(LogLevel.Warning))
            .Throws<AssertionException>();
        await Assert.That(ex!.Message).Contains("(none:", StringComparison.Ordinal);
    }

    /// <summary>The gate keys on identity, not wording: a record from a different definition that
    /// happens to render similar text still fails.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateKeysOnIdentityNotWordingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.CycleValidationFailed(logger, 3); // Warning; different definition

        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedOnly(LogLevel.Warning).Allowing(ValidationFailed))
            .Throws<AssertionException>();
    }

    /// <summary>A capture floor above the gate's floor makes the gate vacuous: the records it claims
    /// to inspect were never captured, so it must fail loudly rather than pass.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateAboveCaptureFloorIsReportedAsVacuousAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Error);
        using (factory)
        {
            var logger = factory.CreateLogger("Test");
            TestLogMessages.StartedProcessing(logger);

            // The collector never captured Warning, so a Warning-floor gate cannot see a Warning record.
            var ex = await Assert.That(async () =>
                await Assert.That(collector).HasLoggedOnly(LogLevel.Warning))
                .Throws<AssertionException>();
            await Assert.That(ex!.Message).Contains("vacuously", StringComparison.Ordinal);
        }
    }

    /// <summary>A capture floor at or below the gate's floor is fine.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateAtOrAboveCaptureFloorIsNotVacuousAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Warning);
        using (factory)
        {
            var logger = factory.CreateLogger("Test");
            TestLogMessages.StartedProcessing(logger); // Information: below the capture floor, dropped

            await Assert.That(collector).HasLoggedOnly(LogLevel.Warning);
        }
    }

    /// <summary>The null-allowlist guard on Allowing.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateThrowsOnNullAllowlistAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, _) = CreateCollectorAndLogger();

        var thrown = false;
        try
        {
#pragma warning disable TUnitAssertions0002 // the guard throws at chain-build time, before any await
            _ = Assert.That(collector).HasLoggedOnly(LogLevel.Warning).Allowing(null!);
#pragma warning restore TUnitAssertions0002
        }
        catch (ArgumentNullException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
    }

    /// <summary>The null-element guard on Allowing.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateThrowsOnNullDefinitionInAllowlistAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, _) = CreateCollectorAndLogger();

        var thrown = false;
        try
        {
#pragma warning disable TUnitAssertions0002 // the guard throws at chain-build time, before any await
            _ = Assert.That(collector).HasLoggedOnly(LogLevel.Warning).Allowing(ValidationFailed, null!);
#pragma warning restore TUnitAssertions0002
        }
        catch (ArgumentException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
    }

    /// <summary>The defensive branch for a source that threw: TUnit routes the exception to the
    /// assertion via metadata.Exception, and the gate reports it rather than dereferencing a value.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateSourceThrewPropagatesAsAssertionExceptionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(ThrowingSourceAsync(cancellationToken)).HasLoggedOnly<FakeLogCollector>(LogLevel.Warning);
        }).Throws<AssertionException>();
    }

    /// <summary>The defensive branch for a null collector.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GateNullCollectorFailsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ex = await Assert.That(async () =>
            await Assert.That(NullCollector()!).HasLoggedOnly(LogLevel.Warning))
            .Throws<AssertionException>();
        await Assert.That(ex!.Message).Contains("collector was null", StringComparison.Ordinal);
    }

    /// <summary>Returns a null collector without a constant expression the analyzer would reject.</summary>
    /// <returns>Always null.</returns>
    private static FakeLogCollector? NullCollector() => null;

    /// <summary>A source that throws, for the defensive metadata.Exception branch.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Never returns; always throws.</returns>
    private static async Task<FakeLogCollector> ThrowingSourceAsync(CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("source threw");
    }

    // --- MatchingAny over definitions ---

    /// <summary>One logical event emitted by either of two definitions (verbose vs terse) matches
    /// via a single MatchingAny call.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingAnyMatchesEitherDefinitionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.Third(logger); // the "terse" one of the pair

        await Assert.That(collector).HasLogged().MatchingAny(ValidationFailed, Third).Once();
        await Assert.That(collector).HasNotLogged().MatchingAny(ValidationFailed, OperationFailed);
    }

    /// <summary>MatchingAny composes with the rest of the chain.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingAnyComposesWithLevelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.ValidationFailed(logger); // Warning
        TestLogMessages.Third(logger);            // Warning

        await Assert.That(collector).HasLogged()
            .MatchingAny(ValidationFailed, Third).AtLevel(LogLevel.Warning).Exactly(2);
    }

    /// <summary>The zero-argument MatchingAny() still resolves to the filter overload rather than
    /// becoming ambiguous with the definition overload.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingAnyWithNoArgumentsStillResolvesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.ValidationFailed(logger);

        // An empty disjunction matches no record (documented behavior of the filter overload).
        await Assert.That(collector).HasNotLogged().MatchingAny();
    }

    // --- Step sugar ---

    /// <summary>Step(def) reads one call per event and matches the same flow as
    /// Matching(def).Then().Matching(def).</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task StepBuildsATypedSequenceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = LogDefinition.Capture(TestLogMessages.First);
        var second = LogDefinition.Capture(TestLogMessages.Second);
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);
        TestLogMessages.Third(logger);

        await Assert.That(collector).HasLoggedSequence()
            .Step(first).Step(second).Step(Third);
    }

    /// <summary>Filters chain onto the step just opened by Step.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task StepAcceptsTrailingFiltersOnTheOpenedStepAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = LogDefinition.Capture(TestLogMessages.First);
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.First(logger);
        TestLogMessages.Third(logger);

        await Assert.That(collector).HasLoggedSequence()
            .Step(first).AtLevel(LogLevel.Information)
            .Step(Third).AtLevel(LogLevel.Warning);
    }

    /// <summary>An out-of-order flow fails the typed sequence.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task StepFailsWhenOrderIsWrongAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = LogDefinition.Capture(TestLogMessages.First);
        var second = LogDefinition.Capture(TestLogMessages.Second);
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.Second(logger);
        TestLogMessages.First(logger);

        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedSequence().Step(first).Step(second))
            .Throws<AssertionException>();
    }

    /// <summary>The null-definition guard on Step.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task StepThrowsOnNullDefinitionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, _) = CreateCollectorAndLogger();

        var thrown = false;
        try
        {
#pragma warning disable TUnitAssertions0002 // the guard throws at chain-build time, before any await
            _ = Assert.That(collector).HasLoggedSequence().Step(null!);
#pragma warning restore TUnitAssertions0002
        }
        catch (ArgumentNullException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
    }
}
