using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Tests;

/// <summary>
/// A record whose state is not a key/value list (a custom <c>TState</c> passed to <c>ILogger.Log</c>) has no
/// properties and no template: the filters that read structured state do not match it, and do not throw
/// (<c>FakeLogRecord.StructuredState</c> throws <see cref="InvalidCastException"/> for such a record).
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class CustomStateRecordTests
{
    /// <summary>The property and template filters return <see langword="false"/> instead of throwing; a
    /// <see langword="null"/> expected value matches the absent property.</summary>
    [Test]
    public async Task StructuredStateFilters_DoNotThrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = LogCustomState();

        await Assert.That(LogFilter.WithProperty("key", "value").Matches(record)).IsFalse();
        await Assert.That(LogFilter.WithProperty("key", (string?)null).Matches(record)).IsTrue();
        await Assert.That(LogFilter.WithProperty("key", static value => value is not null).Matches(record)).IsFalse();
        await Assert.That(LogFilter.WithProperty("key", 1).Matches(record)).IsFalse();
        await Assert.That(LogFilter.WithMessageTemplate("raw state").Matches(record)).IsFalse();
    }

    /// <summary>A custom-state call can be captured as a definition (no template, no properties), and the definition
    /// matches the records of that call by identity.</summary>
    [Test]
    public async Task Capture_OfACustomStateCall_MatchesItsRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = LogDefinition.Capture(Write);
        var record = LogCustomState();

        await Assert.That(definition.Template).IsNull();
        await Assert.That(definition.Properties.Count).IsEqualTo(0);
        await Assert.That(LogFilter.Matching(definition).Matches(record)).IsTrue();
        await Assert.That(LogFilter.MatchingCall(definition).Matches(record)).IsTrue();
    }

    /// <summary>Logs one record with a string as its state, and returns it.</summary>
    /// <returns>The record.</returns>
    private static FakeLogRecord LogCustomState()
    {
        var collector = new FakeLogCollector();
        Write(new FakeLogger(collector, "Custom"));
        return collector.LatestRecord;
    }

    /// <summary>Writes a record with a string (not a key/value list) as its state.</summary>
    /// <param name="logger">The logger.</param>
    private static void Write(ILogger logger)
        => logger.Log(LogLevel.Information, new EventId(6, "Raw"), "raw state", null, static (state, _) => state);
}
