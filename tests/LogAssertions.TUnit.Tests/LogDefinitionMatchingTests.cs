using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for typed log-definition matching (v0.11.0): <see cref="LogDefinition.Capture"/> plus
/// the <c>Matching(LogDefinition)</c> and <c>MatchingCall(Action&lt;ILogger&gt;)</c> chain
/// methods, across <c>HasLogged</c>, <c>HasNotLogged</c>, and <c>HasLoggedSequence</c>.
/// Pins the identity contract (event ID + name + template; never level or argument values),
/// the exact-call contract (identity + placeholder values + exception), the probe failure
/// modes, and the real-world definition shapes: private Core behind a wrapper,
/// generator-assigned implicit IDs, dynamic-level definitions, definitions in generic hosts,
/// and the documented same-name/same-template cross-class ambiguity.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogDefinitionMatchingTests
{
    /// <summary>Reusable definition for the parameterised order message (captured with throwaway args).</summary>
    private static readonly LogDefinition OrderForCustomer =
        LogDefinition.Capture(log => TestLogMessages.OrderForCustomer(log, 0, ""));

    /// <summary>Reusable definition for the started-processing message.</summary>
    private static readonly LogDefinition StartedProcessing =
        LogDefinition.Capture(TestLogMessages.StartedProcessing);

    /// <summary>Builds a collector and a logger backed by it.</summary>
    /// <returns>The collector and logger pair.</returns>
    private static (FakeLogCollector Collector, ILogger Logger) CreateCollectorAndLogger()
    {
        FakeLogCollector collector = new();
        FakeLogger logger = new(collector);
        return (collector, logger);
    }

    // --- Capture ---

    /// <summary>Verifies Capture populates identity, level, template, properties, and exception.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CapturePopulatesAllCapturedFieldsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var boom = new InvalidOperationException("boom");
        var definition = LogDefinition.Capture(log => TestLogMessages.OperationFailed(log, boom));

        await Assert.That(definition.Id.Id).IsEqualTo(5);
        await Assert.That(definition.Id.Name).IsEqualTo(nameof(TestLogMessages.OperationFailed));
        await Assert.That(definition.Level).IsEqualTo(LogLevel.Error);
        await Assert.That(definition.Template).IsEqualTo("Operation failed");
        await Assert.That(definition.Properties).IsEmpty();
        await Assert.That(definition.Exception).IsSameReferenceAs(boom);
    }

    /// <summary>Verifies a generator-assigned (omitted) event ID captures as a stable non-zero value with the method name.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureImplicitEventIdIsStableAndNamedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = LogDefinition.Capture(log => TestLogMessages.ImplicitIdSample(log, "a"));
        var second = LogDefinition.Capture(log => TestLogMessages.ImplicitIdSample(log, "b"));

        await Assert.That(first.Id.Id).IsNotEqualTo(0);
        await Assert.That(first.Id.Id).IsEqualTo(second.Id.Id);
        await Assert.That(first.Id.Name).IsEqualTo(nameof(TestLogMessages.ImplicitIdSample));
    }

    /// <summary>Verifies a lambda that logs nothing fails fast with the explanatory message.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureThrowsWhenLambdaLogsNothingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ex = Assert.Throws<ArgumentException>(() => LogDefinition.Capture(_ => { }));
        await Assert.That(ex!.Message).Contains("logged nothing", StringComparison.Ordinal);
    }

    /// <summary>Verifies a lambda that logs more than once fails fast naming the record count.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureThrowsWhenLambdaLogsMultipleRecordsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ex = Assert.Throws<ArgumentException>(() => LogDefinition.Capture(log =>
        {
            TestLogMessages.First(log);
            TestLogMessages.Second(log);
        }));
        await Assert.That(ex!.Message).Contains("2 records", StringComparison.Ordinal);
    }

    /// <summary>Verifies the null-lambda guard.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureThrowsOnNullInvocationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => LogDefinition.Capture(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>Verifies ToString renders the Name#Id "template" identity form.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task ToStringRendersIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(OrderForCustomer.ToString())
            .IsEqualTo("OrderForCustomer#70 \"Order {OrderId} for {Customer}\"");
    }

    // --- Matching (identity) ---

    /// <summary>Verifies identity matching ignores argument values: two calls with different args both match.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingIgnoresArgumentValuesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.OrderForCustomer(logger, 42, "alpha");
        TestLogMessages.OrderForCustomer(logger, 7, "beta");

        await Assert.That(collector).HasLogged().Matching(OrderForCustomer).Exactly(2);
    }

    /// <summary>Verifies identity matching does not match records from a different definition.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingRejectsOtherDefinitionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().Matching(StartedProcessing).Once();
        await Assert.That(collector).HasNotLogged().Matching(OrderForCustomer);
    }

    /// <summary>Verifies the workhorse composition: identity plus a pinned placeholder value.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingComposesWithWithPropertyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.OrderForCustomer(logger, 42, "alpha");
        TestLogMessages.OrderForCustomer(logger, 7, "beta");

        await Assert.That(collector).HasLogged()
            .Matching(OrderForCustomer).WithProperty("OrderId", 42).Once();
    }

    /// <summary>Verifies a dynamic-level definition matches by identity at every level, and
    /// that AtLevel narrows it (level is deliberately not part of identity).</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingDynamicLevelMatchesAcrossLevelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(
            log => TestLogMessages.DynamicLevelSample(log, LogLevel.Debug, "x"));
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.DynamicLevelSample(logger, LogLevel.Warning, "a");
        TestLogMessages.DynamicLevelSample(logger, LogLevel.Error, "b");

        await Assert.That(collector).HasLogged().Matching(definition).Exactly(2);
        await Assert.That(collector).HasLogged()
            .Matching(definition).AtLevel(LogLevel.Error).Once();
    }

    /// <summary>Verifies the private-Core-plus-wrapper shape: capturing via the wrapper matches
    /// records produced via the wrapper (both carry the Core's identity).</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingPrivateCoreViaWrapperAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => TestLogMessages.PrivateCoreViaWrapper(log, "probe"));
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.PrivateCoreViaWrapper(logger, "production");

        await Assert.That(definition.Id.Id).IsEqualTo(302);
        await Assert.That(collector).HasLogged().Matching(definition).Once();
    }

    /// <summary>Verifies a definition hosted in a generic class captures by closing the generic;
    /// identity is shared across closings.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingDefinitionInGenericHostAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => GenericHostMessages<int>.GenericSample(log, 0));
        var (collector, logger) = CreateCollectorAndLogger();
        GenericHostMessages<string>.GenericSample(logger, "production");

        await Assert.That(collector).HasLogged().Matching(definition).Once();
    }

    /// <summary>Pins the documented residual ambiguity: two definitions with the same method
    /// name and template (different classes, generator-assigned IDs) are indistinguishable
    /// because their records carry identical identity.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingSameNameSameTemplateCrossClassIsAmbiguousByDesignAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitionA = LogDefinition.Capture(log => CollisionMessagesA.CollisionSample(log, "x"));
        var (collector, logger) = CreateCollectorAndLogger();
        CollisionMessagesB.CollisionSample(logger, "produced-by-B");

        await Assert.That(collector).HasLogged().Matching(definitionA).Once();
    }

    /// <summary>Verifies a plain (non-generated) logger call also captures and matches, keyed
    /// on template with the default zero event ID.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingPlainLoggerCallDegradesToTemplateIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA1848, CA1873 // deliberate: pin the plain, non-source-generated call path
        var definition = LogDefinition.Capture(log => log.LogInformation("plain {Thing} call", "probe"));
        var (collector, logger) = CreateCollectorAndLogger();
        logger.LogInformation("plain {Thing} call", "production");
        logger.LogInformation("a different call {Number}", 1);
#pragma warning restore CA1848, CA1873

        await Assert.That(definition.Id.Id).IsEqualTo(0);
        await Assert.That(collector).HasLogged().Matching(definition).Once();
    }

    /// <summary>Verifies HasNotLogged().Matching(...) fails when the definition was logged, and
    /// that the failure message names the definition.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task HasNotLoggedMatchingFailsWhenDefinitionPresentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.StartedProcessing(logger);

        var ex = await Assert.ThrowsAsync<AssertionException>(async () =>
            await Assert.That(collector).HasNotLogged().Matching(StartedProcessing));
        await Assert.That(ex!.Message).Contains("Definition StartedProcessing#3", StringComparison.Ordinal);
    }

    /// <summary>Verifies definition matching works as a sequence-step filter.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingWorksInSequenceStepsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = LogDefinition.Capture(TestLogMessages.First);
        var second = LogDefinition.Capture(TestLogMessages.Second);
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.First(logger);
        TestLogMessages.Second(logger);

        await Assert.That(collector).HasLoggedSequence()
            .Matching(first)
            .Then().Matching(second);
    }

    // --- MatchingCall (exact) ---

    /// <summary>Verifies the exact-call form matches only the record with identical argument values.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingCallPinsArgumentValuesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.OrderForCustomer(logger, 42, "alpha");
        TestLogMessages.OrderForCustomer(logger, 7, "beta");

        await Assert.That(collector).HasLogged()
            .MatchingCall(log => TestLogMessages.OrderForCustomer(log, 42, "alpha")).Once();
        await Assert.That(collector).HasNotLogged()
            .MatchingCall(log => TestLogMessages.OrderForCustomer(log, 42, "beta"));
    }

    /// <summary>Verifies exact-call exception matching: same runtime type and message match;
    /// a different message does not.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingCallMatchesExceptionByTypeAndMessageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom"));

        await Assert.That(collector).HasLogged()
            .MatchingCall(log => TestLogMessages.OperationFailed(log, new InvalidOperationException("boom")))
            .Once();
        await Assert.That(collector).HasNotLogged()
            .MatchingCall(log => TestLogMessages.OperationFailed(log, new InvalidOperationException("other")));
    }

    /// <summary>Verifies a captured call without an exception matches only exception-free records.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingCallWithoutExceptionRejectsExceptionRecordsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var withoutException = LogDefinition.Capture(TestLogMessages.StartedProcessing);
        var (collector, logger) = CreateCollectorAndLogger();
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom"));
        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().WithFilter(LogFilter.MatchingCall(withoutException)).Once();
    }

    /// <summary>Verifies formatted-value comparison is culture-stable: capture and match agree
    /// under a comma-decimal culture because the collector formats invariantly.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingCallIsCultureInvariantAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            var (collector, logger) = CreateCollectorAndLogger();
            TestLogMessages.DecimalAmount(logger, 3.14m, 2.5);

            await Assert.That(collector).HasLogged()
                .MatchingCall(log => TestLogMessages.DecimalAmount(log, 3.14m, 2.5)).Once();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Verifies the failure message renders the definition identity description.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingFailureMessageNamesTheDefinitionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, _) = CreateCollectorAndLogger();

        var ex = await Assert.ThrowsAsync<AssertionException>(async () =>
            await Assert.That(collector).HasLogged().Matching(OrderForCustomer).Once());
        await Assert.That(ex!.Message)
            .Contains("Definition OrderForCustomer#70 \"Order {OrderId} for {Customer}\"", StringComparison.Ordinal);
    }

    /// <summary>Verifies template-less records (null structured state) capture with a null
    /// template, render the no-template ToString form, match by identity across state shapes,
    /// and are rejected by the exact-call form when the placeholder counts differ.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingNullStateRecordsAndCountMismatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nullState = LogDefinition.Capture(log => log.Log<object?>(
            LogLevel.Information, new EventId(77, "RawEvent"), null, null, (_, _) => "rendered"));

        await Assert.That(nullState.Template).IsNull();
        await Assert.That(nullState.Properties).IsEmpty();
        await Assert.That(nullState.ToString()).IsEqualTo("RawEvent#77 \"<no template>\"");

        var (collector, logger) = CreateCollectorAndLogger();
        List<KeyValuePair<string, object?>> propertyState = [new("K", "v")];
        logger.Log<object?>(LogLevel.Information, new EventId(77, "RawEvent"), null, null, (_, _) => "rendered");
        logger.Log(LogLevel.Information, new EventId(77, "RawEvent"), propertyState, null, (_, _) => "rendered2");

        // Identity ignores placeholder values, so both records match; the exact-call form
        // rejects the second record on the placeholder-count mismatch.
        await Assert.That(collector).HasLogged().Matching(nullState).Exactly(2);
        await Assert.That(collector).HasLogged().WithFilter(LogFilter.MatchingCall(nullState)).Once();
    }

    /// <summary>Verifies the exact-call exception asymmetries: a captured call WITH an exception
    /// rejects a same-identity record without one, and rejects a different exception type even
    /// with an equal message.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingCallExceptionAsymmetriesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CA1848, CA1873 // deliberate: plain calls keep identity equal across exception shapes
        var withException = LogDefinition.Capture(
            log => log.LogError(new TimeoutException("t"), "asym {A}", 1));
        var (collector, logger) = CreateCollectorAndLogger();
        logger.LogError("asym {A}", 1);
        logger.LogError(new InvalidOperationException("t"), "asym {A}", 1);
#pragma warning restore CA1848, CA1873

        await Assert.That(collector).HasLogged().Matching(withException).Exactly(2);
        await Assert.That(collector).HasNotLogged().WithFilter(LogFilter.MatchingCall(withException));
    }

    /// <summary>Verifies the null-definition guard on the chain method (the guard throws
    /// synchronously at chain-build time, before any await).</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingThrowsOnNullDefinitionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, _) = CreateCollectorAndLogger();

        var thrown = false;
        try
        {
#pragma warning disable TUnitAssertions0002 // the guard throws at chain-build time, before any await
            _ = Assert.That(collector).HasLogged().Matching((LogDefinition)null!);
#pragma warning restore TUnitAssertions0002
        }
        catch (ArgumentNullException)
        {
            thrown = true;
        }

        await Assert.That(thrown).IsTrue();
    }

    /// <summary>Verifies the null-call guard on MatchingCall attributes the error to the chain
    /// method's own parameter name, not the probe's.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task MatchingCallThrowsOnNullCallWithOwnParamNameAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, _) = CreateCollectorAndLogger();

        string? paramName = null;
        try
        {
#pragma warning disable TUnitAssertions0002 // the guard throws at chain-build time, before any await
            _ = Assert.That(collector).HasLogged().MatchingCall(null!);
#pragma warning restore TUnitAssertions0002
        }
        catch (ArgumentNullException ex)
        {
            paramName = ex.ParamName;
        }

        await Assert.That(paramName).IsEqualTo("call");
    }
}
