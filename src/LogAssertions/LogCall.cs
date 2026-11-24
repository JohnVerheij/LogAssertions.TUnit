using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// One captured log call: the identity of its definition plus its formatted placeholder values and its exception.
/// Created via <see cref="Capture"/>, which invokes a logging delegate once against a private probe logger, and matched
/// with <see cref="LogFilter.MatchingCall"/> (the chain's <c>MatchingCall(log => ...)</c> captures one for you).
/// </summary>
/// <example>
/// <code>
/// var shipped = LogCall.Capture(log => LogMessages.OrderShipped(log, 42, "NYC"));
///
/// await Assert.That(collector).HasLogged().WithFilter(LogFilter.MatchingCall(shipped)).Once();
/// </code>
/// </example>
/// <remarks>
/// The probe invocation is side-effect free: a <c>[LoggerMessage]</c> method only formats and logs to the logger it is
/// handed, and <see cref="Capture"/> hands it a throwaway in-memory logger that collects every level, so an
/// <c>IsEnabled</c> gate inside generated logging code cannot suppress the capture. Production code never references
/// this package; the capture lambda executes the production logging method in the test process only.
/// </remarks>
public sealed class LogCall
{
    /// <summary>The placeholder values, sorted by key then value (ordinal) for order-insensitive comparison.</summary>
    private readonly KeyValuePair<string, string?>[] _sortedProperties;

    /// <summary>Initializes the captured call. Only <see cref="Capture"/> creates instances.</summary>
    /// <param name="definition">The identity of the call.</param>
    /// <param name="properties">The placeholder key/value pairs, excluding <c>{OriginalFormat}</c>.</param>
    /// <param name="exception">The exception, when the call supplied one.</param>
    private LogCall(LogDefinition definition, KeyValuePair<string, string?>[] properties, Exception? exception)
    {
        Definition = definition;
        Properties = properties;
        Exception = exception;
        _sortedProperties = SortPairs(properties);
    }

    /// <summary>The identity of the call: event ID, level, message template, and placeholder names.</summary>
    public LogDefinition Definition { get; }

    /// <summary>The placeholder key/value pairs (formatted strings, invariant culture; <c>{OriginalFormat}</c>
    /// excluded), in capture order.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Properties { get; }

    /// <summary>The exception instance the call supplied, or <see langword="null"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>Invokes <paramref name="invocation"/> once against a private probe logger and returns the captured call.</summary>
    /// <param name="invocation">A delegate that performs exactly one log call on the supplied logger: typically a
    /// <c>[LoggerMessage]</c> method invocation with the exact argument values to assert. Must be non-null.</param>
    /// <returns>The captured call.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="invocation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="invocation"/> logged zero records (nothing to capture) or
    /// more than one record (ambiguous: pass a lambda that emits exactly one log call).</exception>
    public static LogCall Capture(Action<ILogger> invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var collector = FakeLogCollector.Create(
            new FakeLogCollectorOptions { CollectRecordsForDisabledLogLevels = true });
        invocation(new FakeLogger(collector));

        var records = collector.GetSnapshot();
        if (records.Count is 0)
        {
            throw new ArgumentException(
                "The invocation logged nothing. Pass a lambda that performs exactly one "
                + "log call on the supplied logger, e.g. log => LogMessages.OrderShipped(log, 0, \"\").",
                nameof(invocation));
        }

        if (records.Count > 1)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The invocation logged {0} records. Pass a lambda that performs "
                    + "exactly one log call so the captured shape is unambiguous.",
                    records.Count),
                nameof(invocation));
        }

        var record = records[0];
        var properties = PropertiesOf(record);
        return new LogCall(LogDefinition.FromRecord(record, properties), properties, record.Exception);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="record"/> matches this call exactly: the identity of
    /// <see cref="Definition"/> plus every placeholder value (as formatted strings, order-insensitive) plus the
    /// exception (both absent, or same runtime type and equal message). Level is not compared; chain <c>AtLevel</c>
    /// when it matters.
    /// </summary>
    /// <param name="record">The record to test.</param>
    /// <returns><see langword="true"/> when the record matches the call exactly.</returns>
    internal bool MatchesCall(FakeLogRecord record)
        => Definition.MatchesIdentity(record)
        && ExceptionMatches(record.Exception)
        && PropertiesMatch(record);

    /// <summary>Renders the call's identity as <c>Name#Id "template"</c>, like <see cref="LogDefinition.ToString"/>.</summary>
    /// <returns>The rendered identity.</returns>
    public override string ToString() => Definition.ToString();

    /// <summary>Returns a record's placeholder key/value pairs, <c>{OriginalFormat}</c> excluded.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The pairs, in the record's order.</returns>
    private static KeyValuePair<string, string?>[] PropertiesOf(FakeLogRecord record)
        => [.. (RecordState.Pairs(record) ?? []).Where(static kvp => !string.Equals(kvp.Key, LogDefinition.OriginalFormatKey, StringComparison.Ordinal))];

    /// <summary>Compares the record's exception against the captured one: both absent, or same runtime type with an
    /// ordinally equal message.</summary>
    /// <param name="actual">The record's exception.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private bool ExceptionMatches(Exception? actual)
    {
        if (Exception is null)
            return actual is null;

        return actual is not null
            && actual.GetType() == Exception.GetType()
            && string.Equals(actual.Message, Exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Compares the record's placeholder values (excluding <c>{OriginalFormat}</c>) against the captured ones
    /// as an order-insensitive multiset of key/value pairs.</summary>
    /// <param name="record">The record to compare.</param>
    /// <returns><see langword="true"/> when the pairs are equal.</returns>
    private bool PropertiesMatch(FakeLogRecord record)
    {
        var actual = PropertiesOf(record);
        if (actual.Length != _sortedProperties.Length)
            return false;

        var sortedActual = SortPairs(actual);
        for (var i = 0; i < sortedActual.Length; i++)
        {
            if (!string.Equals(sortedActual[i].Key, _sortedProperties[i].Key, StringComparison.Ordinal)
                || !string.Equals(sortedActual[i].Value, _sortedProperties[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Sorts key/value pairs by key then value (ordinal) so two captures of the same call compare equal
    /// regardless of state-entry order (which is not guaranteed).</summary>
    /// <param name="pairs">The pairs to sort. Not mutated; a sorted copy is returned.</param>
    /// <returns>The sorted copy.</returns>
    private static KeyValuePair<string, string?>[] SortPairs(KeyValuePair<string, string?>[] pairs)
        => [.. pairs
            .OrderBy(static kvp => kvp.Key, StringComparer.Ordinal)
            .ThenBy(static kvp => kvp.Value, StringComparer.Ordinal)];
}
