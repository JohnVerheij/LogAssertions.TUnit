using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Pins the interop contract between LogAssertions.TUnit's [AssertionExtension] chains and
/// TUnit's general-purpose assertion features (WithMessage() justification, Assert.Multiple()
/// failure aggregation). The README documents that these features compose with our chains
/// "for free" because the chains derive from TUnit's assertion types — these tests guarantee
/// that the documentation stays true across TUnit upstream releases. A failure here is either
/// a TUnit upstream regression (file upstream) or a LogAssertions design break (fix here and
/// update the README). The Should() interop is intentionally not pinned here: the upstream
/// TUnit.Assertions.Should package is currently beta-only, and this repo's dpfa Proj1101
/// rule forbids beta dependencies. The Should() interop will be added to this file once
/// the upstream package goes stable.
/// </summary>
[Category("TUnitInterop")]
[Timeout(10_000)]
internal sealed class TUnitInteropTests
{
    private static FakeLogCollector CreateCollectorWithSeededRecords()
    {
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });

        ILogger logger = factory.CreateLogger("TestCategory");
        TestLogMessages.StartedProcessing(logger);
        TestLogMessages.ValidationFailed(logger);
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom"));
        return collector;
    }

    /// <summary>
    /// Pins that TUnit core's .Because("reason") chains cleanly on our assertion types and
    /// surfaces the justification in the failure message. (Note: TUnit's .WithMessage(...)
    /// is a predicate-based assertion *on* a failure message — used for negative-testing
    /// patterns — and does NOT add a reason annotation. The reason-annotation API is
    /// .Because, mirroring FluentAssertions.) If this regresses, the README's documented
    /// "use Because() to explain why a HasNotLogged assertion matters" example would mislead
    /// consumers.
    /// </summary>
    [Test]
    public async Task BecauseAddsJustificationToFailureMessageAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        AssertionException? caught = null;
        try
        {
            await Assert.That(collector)
                .HasNotLogged()
                .AtLevelOrAbove(LogLevel.Error)
                .Because("happy-path workflow must not log errors");
        }
        catch (AssertionException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("happy-path workflow must not log errors");
    }

    /// <summary>
    /// Pins that TUnit's Assert.Multiple() aggregates failures from multiple LogAssertions
    /// chains rather than fast-failing on the first. The aggregated exception must reference
    /// both failures — if only the first surfaces, Assert.Multiple is fast-failing on our
    /// chains and the documented "Assert.Multiple works with our chains" guidance would be
    /// wrong. AssertAllAsync remains the log-specific batch terminator; Assert.Multiple is
    /// the general-purpose alternative for batches that mix log and non-log assertions.
    /// </summary>
    [Test]
    public async Task AssertMultipleAggregatesFailuresFromOurChainsAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = CreateCollectorWithSeededRecords();

        AssertionException? caught = null;
        try
        {
            using (Assert.Multiple())
            {
                // First failure: collector has 0 Critical records, asserting AtLeast 1 fails.
                await Assert.That(collector).HasLogged().AtLevel(LogLevel.Critical).AtLeast(1);

                // Second failure: collector has 1 Error record, asserting Never on Error fails.
                await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Error);
            }
        }
        catch (AssertionException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();

        await Assert.That(caught!.Message).Contains("Critical");
        await Assert.That(caught.Message).Contains("Error");
    }
}
