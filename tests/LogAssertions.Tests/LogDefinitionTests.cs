using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for <see cref="LogDefinition"/> and the definition-based
/// <see cref="LogFilter"/> factories. Exercises capture population, both probe failure modes,
/// identity semantics (event ID + name + template; never level or argument values), exact-call
/// semantics (placeholder values order-insensitively, exception by type and message), and the
/// filter descriptions, all through the core package only.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogDefinitionTests
{
    /// <summary>Builds a collector and a logger backed by it.</summary>
    /// <returns>The collector and logger pair.</returns>
    private static (FakeLogCollector Collector, ILogger Logger) CreateCollectorAndLogger()
    {
        FakeLogCollector collector = new();
        FakeLogger logger = new(collector);
        return (collector, logger);
    }

    /// <summary>Verifies capture of a template-bearing call populates every field.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CapturePopulatesFieldsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => CoreTestLogMessages.OrderPlaced(log, 5, "x"));

        await Assert.That(definition.Id.Id).IsEqualTo(400);
        await Assert.That(definition.Id.Name).IsEqualTo(nameof(CoreTestLogMessages.OrderPlaced));
        await Assert.That(definition.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(definition.Template).IsEqualTo("Order {OrderId} placed by {Customer}");
        await Assert.That(definition.Properties.Count).IsEqualTo(2);
        await Assert.That(definition.Exception).IsNull();
    }

    /// <summary>Verifies the captured placeholder values are the formatted probe-argument strings.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureRecordsFormattedPlaceholderValuesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => CoreTestLogMessages.OrderPlaced(log, 5, "x"));
        var byKey = definition.Properties.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        await Assert.That(byKey["OrderId"]).IsEqualTo("5");
        await Assert.That(byKey["Customer"]).IsEqualTo("x");
    }

    /// <summary>Verifies the zero-record probe failure.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureThrowsOnSilentLambdaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ex = Assert.Throws<ArgumentException>(() => LogDefinition.Capture(_ => { }));
        await Assert.That(ex.ParamName).IsEqualTo("invocation");
    }

    /// <summary>Verifies the multi-record probe failure.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task CaptureThrowsOnMultiRecordLambdaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ex = Assert.Throws<ArgumentException>(() => LogDefinition.Capture(log =>
        {
            CoreTestLogMessages.Plain(log);
            CoreTestLogMessages.Plain(log);
        }));
        await Assert.That(ex.ParamName).IsEqualTo("invocation");
    }

    /// <summary>Verifies LogFilter.Matching accepts identity across argument values and
    /// rejects a different definition.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task FilterMatchingIsIdentityOnlyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => CoreTestLogMessages.OrderPlaced(log, 0, ""));
        var (collector, logger) = CreateCollectorAndLogger();
        CoreTestLogMessages.OrderPlaced(logger, 42, "alpha");
        CoreTestLogMessages.OrderPlaced(logger, 7, "beta");
        CoreTestLogMessages.Plain(logger);

        await Assert.That(collector.CountMatching(LogFilter.Matching(definition))).IsEqualTo(2);
    }

    /// <summary>Verifies identity requires the template to match even when the numeric event ID
    /// collides (the no-explicit-EventId consumer constraint: ID equality alone is never enough).</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task FilterMatchingRequiresTemplateNotJustEventIdAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => CoreTestLogMessages.SameIdFirst(log, "x"));
        var (collector, logger) = CreateCollectorAndLogger();
        CoreCollidingIdLogMessages.SameIdSecond(logger, "y");

        await Assert.That(collector.CountMatching(LogFilter.Matching(definition))).IsEqualTo(0);
    }

    /// <summary>Verifies exact-call matching pins placeholder values and the exception.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task FilterMatchingCallPinsValuesAndExceptionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (collector, logger) = CreateCollectorAndLogger();
        CoreTestLogMessages.OrderPlaced(logger, 42, "alpha");
        CoreTestLogMessages.Failed(logger, new TimeoutException("late"));

        var exactValues = LogDefinition.Capture(log => CoreTestLogMessages.OrderPlaced(log, 42, "alpha"));
        var otherValues = LogDefinition.Capture(log => CoreTestLogMessages.OrderPlaced(log, 42, "beta"));
        var exactException = LogDefinition.Capture(log => CoreTestLogMessages.Failed(log, new TimeoutException("late")));
        var otherExceptionType = LogDefinition.Capture(log => CoreTestLogMessages.Failed(log, new InvalidOperationException("late")));

        await Assert.That(collector.CountMatching(LogFilter.MatchingCall(exactValues))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.MatchingCall(otherValues))).IsEqualTo(0);
        await Assert.That(collector.CountMatching(LogFilter.MatchingCall(exactException))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.MatchingCall(otherExceptionType))).IsEqualTo(0);
    }

    /// <summary>Verifies the filter descriptions used in failure-message expectation lines.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task FilterDescriptionsRenderIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(log => CoreTestLogMessages.OrderPlaced(log, 0, ""));

        await Assert.That(LogFilter.Matching(definition).Description)
            .IsEqualTo("Definition OrderPlaced#400 \"Order {OrderId} placed by {Customer}\"");
        await Assert.That(LogFilter.MatchingCall(definition).Description)
            .Contains("with supplied argument values", StringComparison.Ordinal);
    }

    /// <summary>Verifies the null guards on the filter factories.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task FilterFactoriesThrowOnNullAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => LogFilter.Matching((LogDefinition)null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.MatchingCall(null!)).Throws<ArgumentNullException>();
    }
}
