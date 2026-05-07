using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using LogAssertions.TUnit;
using Microsoft.Extensions.Logging;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Pins the v0.4.0 <c>ThenAnyOrder</c> sequence step: a concurrent group of sub-steps that
/// must all match in the remaining records but whose relative order is unconstrained.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class HasLoggedSequenceAnyOrderTests
{
    [Test]
    public async Task ThenAnyOrder_TwoSubsteps_MatchInDeclaredOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);
        TestLogMessages.Third(logger);

        // ThenAnyOrder of (second, third) — declared in order. Pass.
        await Assert.That(collector).HasLoggedSequence()
            .Containing("first", StringComparison.Ordinal)
            .ThenAnyOrder(
                s => s.Containing("second", StringComparison.Ordinal),
                s => s.Containing("third", StringComparison.Ordinal));
    }

    [Test]
    public async Task ThenAnyOrder_TwoSubsteps_MatchInReversedOrder(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);
        TestLogMessages.Third(logger);

        // ThenAnyOrder of (third, second) — declared in REVERSE; should still match.
        await Assert.That(collector).HasLoggedSequence()
            .Containing("first", StringComparison.Ordinal)
            .ThenAnyOrder(
                s => s.Containing("third", StringComparison.Ordinal),
                s => s.Containing("second", StringComparison.Ordinal));
    }

    [Test]
    public async Task ThenAnyOrder_OneSubstepUnmatched_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);

        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedSequence()
                .Containing("first", StringComparison.Ordinal)
                .ThenAnyOrder(
                    s => s.Containing("second", StringComparison.Ordinal),
                    s => s.Containing("never-occurs", StringComparison.Ordinal)))
            .Throws<AssertionException>();
    }

    [Test]
    public async Task ThenAnyOrder_InterveningUnrelatedRecords_StillMatches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);
        TestLogMessages.StartedProcessing(logger); // unrelated, between the two we want
        TestLogMessages.Third(logger);

        await Assert.That(collector).HasLoggedSequence()
            .Containing("first", StringComparison.Ordinal)
            .ThenAnyOrder(
                s => s.Containing("third", StringComparison.Ordinal),
                s => s.Containing("second", StringComparison.Ordinal));
    }

    [Test]
    public async Task ThenAnyOrder_FollowedByThen_Composes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);
        TestLogMessages.Third(logger);
        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLoggedSequence()
            .Containing("first", StringComparison.Ordinal)
            .ThenAnyOrder(
                s => s.Containing("third", StringComparison.Ordinal),
                s => s.Containing("second", StringComparison.Ordinal))
            .Then().Containing("Started", StringComparison.Ordinal);
    }

    [Test]
    public async Task ThenAnyOrder_ThreeSubsteps_ReversedDeclarationOrder_StillMatches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        // Three records that come in declared order: first, second, third.
        // Three sub-steps declared in the OPPOSITE order: third, second, first.
        // Greedy matching consumes them in record-order, so each record finds its sub-step.
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);
        TestLogMessages.Third(logger);

        await Assert.That(collector).HasLoggedSequence()
            .ThenAnyOrder(
                s => s.Containing("third", StringComparison.Ordinal),
                s => s.Containing("second", StringComparison.Ordinal),
                s => s.Containing("first", StringComparison.Ordinal));
    }

    [Test]
    public async Task ThenAnyOrder_NullSubStep_ThrowsArgumentNull(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;

        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedSequence()
                .ThenAnyOrder(null!))
            .Throws<ArgumentNullException>();
    }

    /// <summary>
    /// Sub-step configurators must not call <c>Then()</c> on the live assertion: that would
    /// mutate the outer step list during sub-step capture. The capture guard surfaces that as
    /// <see cref="InvalidOperationException"/> so the bug fails fast instead of silently
    /// producing a sequence different from what the fluent chain implies.
    /// </summary>
    [Test]
    public async Task ThenAnyOrder_ConfiguratorCallsThen_ThrowsInvalidOperation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);

        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedSequence()
                .ThenAnyOrder(s => { _ = s.Containing("first", StringComparison.Ordinal).Then(); }))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// Same guard as above for nested <c>ThenAnyOrder()</c>: a sub-step configurator cannot
    /// open another concurrent group on the same assertion. The capture guard surfaces it as
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    [Test]
    public async Task ThenAnyOrder_ConfiguratorCallsThenAnyOrder_ThrowsInvalidOperation(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);

#pragma warning disable MA0134 // Inner ThenAnyOrder returns an awaitable assertion; here we expect it to throw synchronously, so the discard is intentional and there is no Task to await.
        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedSequence()
                .ThenAnyOrder(s => { _ = s.ThenAnyOrder(inner => inner.Containing("first", StringComparison.Ordinal)); }))
            .Throws<InvalidOperationException>();
#pragma warning restore MA0134
    }

    /// <summary>
    /// Backtracking matcher must reject the assignment when no order-independent valid mapping
    /// exists. Two sub-steps that both require the only matching record cannot both be
    /// satisfied, so the assertion fails (no spurious greedy success).
    /// </summary>
    [Test]
    public async Task ThenAnyOrder_BacktrackingFailure_NoValidAssignment(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        TestLogMessages.First(logger);  // single record — both sub-steps target it

        await Assert.That(async () =>
            await Assert.That(collector).HasLoggedSequence()
                .ThenAnyOrder(
                    s => s.Containing("first", StringComparison.Ordinal),
                    s => s.Containing("first", StringComparison.Ordinal)))
            .Throws<AssertionException>();
    }

    /// <summary>
    /// Backtracking is order-independent across overlapping filters. Setup: two records, the
    /// first matches both sub-step filters and the second matches only the broad one. The old
    /// greedy matcher would assign the first record to the first-declared (broad) sub-step
    /// and then fail because the specific sub-step has nothing left to match. Backtracking
    /// reassigns the first record to the specific sub-step and the second to the broad one.
    /// </summary>
    [Test]
    public async Task ThenAnyOrder_BacktrackingReassigns_OverlappingFilters(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        // Record 0 matches both sub-step filters (contains "info" and "specific").
        // Record 1 matches only the broad sub-step (contains "info" but not "specific").
#pragma warning disable CA1848
        logger.LogInformation("info specific payload");
        logger.LogInformation("info plain payload");
#pragma warning restore CA1848

        // Sub-step order: broad first, specific second. Greedy would consume record 0 with the
        // broad sub-step and then fail on the specific. Backtracking finds the reassignment.
        await Assert.That(collector).HasLoggedSequence()
            .ThenAnyOrder(
                s => s.Containing("info", StringComparison.Ordinal),
                s => s.Containing("specific", StringComparison.Ordinal));
    }

    /// <summary>
    /// Coverage for the legacy <c>WithExceptionMessage(string)</c> obsolete overload: it
    /// delegates to the explicit-comparison overload with <see cref="StringComparison.Ordinal"/>.
    /// Suppressing CS0618 here is intentional — the test exists to keep the alias body
    /// covered by tests until v0.6.0 removes it.
    /// </summary>
    [Test]
    public async Task WithExceptionMessage_LegacyOverload_DelegatesToOrdinal(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        try { throw new InvalidOperationException("boom"); }
        catch (InvalidOperationException ex)
        {
#pragma warning disable CA1848
            logger.LogError(ex, "operation failed");
#pragma warning restore CA1848
        }

#pragma warning disable CS0618 // Legacy overload: tested intentionally to cover the [Obsolete] alias body.
#pragma warning disable CA1307, MA0074 // Same — analyzers correctly suggest the explicit-comparison overload, but here we exercise the obsolete one on purpose.
        await Assert.That(collector).HasLogged().WithExceptionMessage("boom").Once();
#pragma warning restore CA1307, MA0074
#pragma warning restore CS0618
    }
}
