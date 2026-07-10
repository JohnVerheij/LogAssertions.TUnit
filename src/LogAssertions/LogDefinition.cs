using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// The captured shape of one log call: event ID, level, message template, formatted placeholder
/// values, and exception. Created via <see cref="Capture"/>, which invokes a logging delegate once
/// against a private probe logger and records what it emitted, so a <c>[LoggerMessage]</c>
/// source-generated definition (or any other log call) becomes a reusable, string-free assertion
/// value. Store instances in <c>static readonly</c> fields and match them on the assertion chain
/// via <c>Matching(definition)</c>, or build filters directly with
/// <see cref="LogFilter.Matching(LogDefinition)"/>.
/// </summary>
/// <example>
/// <code>
/// private static readonly LogDefinition OrderShipped =
///     LogDefinition.Capture(log => LogMessages.OrderShipped(log, 0, ""));
///
/// await Assert.That(collector).HasLogged().Matching(OrderShipped).Once();
/// </code>
/// The argument values inside the <see cref="Capture"/> lambda are throwaway: identity matching
/// keys on the event ID (numeric ID plus name) and the message template, never on the argument
/// values or the level. To additionally pin argument values, chain <c>WithProperty</c>, or use
/// the exact-call form (<c>MatchingCall</c> on the chain / <see cref="LogFilter.MatchingCall"/>).
/// </example>
/// <remarks>
/// <para>
/// Identity requires the message template to match; two definitions that share a numeric event ID
/// are still distinguished by their templates. The residual ambiguity: two <c>[LoggerMessage]</c>
/// methods with the same method name (in different classes, both with generator-assigned IDs) and
/// the same template: produces log records that are indistinguishable by design, because the
/// records themselves carry identical identity.
/// </para>
/// <para>
/// The probe invocation is side-effect free: a <c>[LoggerMessage]</c> method only formats and
/// logs to the logger it is handed, and <see cref="Capture"/> hands it a throwaway in-memory
/// logger. Production code never references this package; the capture lambda executes the
/// production logging method in the test process only.
/// </para>
/// </remarks>
public sealed class LogDefinition
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>The captured placeholder values, sorted by key then value (ordinal) for order-insensitive comparison.</summary>
    private readonly KeyValuePair<string, string?>[] _sortedProperties;

    /// <summary>Initialises the captured shape. Only <see cref="Capture"/> creates instances.</summary>
    /// <param name="id">The captured event ID (numeric ID plus name).</param>
    /// <param name="level">The captured log level.</param>
    /// <param name="template">The captured message template (the <c>{OriginalFormat}</c> value), when present.</param>
    /// <param name="properties">The captured placeholder key/value pairs, excluding <c>{OriginalFormat}</c>.</param>
    /// <param name="exception">The captured exception, when the call supplied one.</param>
    private LogDefinition(
        EventId id,
        LogLevel level,
        string? template,
        KeyValuePair<string, string?>[] properties,
        Exception? exception)
    {
        Id = id;
        Level = level;
        Template = template;
        Properties = properties;
        Exception = exception;
        _sortedProperties = SortPairs(properties);
    }

    /// <summary>The captured event ID: numeric ID plus event name. For a <c>[LoggerMessage]</c>
    /// definition the name is the method name and the numeric ID is the attribute's
    /// <c>EventId</c> (or a stable generator-assigned value when omitted).</summary>
    public EventId Id { get; }

    /// <summary>The level the captured call logged at. Not part of identity matching: a
    /// definition taking <see cref="LogLevel"/> as a runtime parameter would capture the probe
    /// call's level, so level constraints belong on the chain (<c>AtLevel</c> etc.).</summary>
    public LogLevel Level { get; }

    /// <summary>The captured message template (the pre-substitution <c>{OriginalFormat}</c>
    /// value), or <see langword="null"/> when the call carried none.</summary>
    public string? Template { get; }

    /// <summary>The captured placeholder key/value pairs (formatted strings, invariant culture;
    /// <c>{OriginalFormat}</c> excluded), in capture order.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Properties { get; }

    /// <summary>The exception instance the captured call supplied, or <see langword="null"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Invokes <paramref name="invocation"/> once against a private probe logger and returns the
    /// captured call shape. The probe collects every level (including disabled-level records), so
    /// an <c>IsEnabled</c> gate inside generated logging code cannot suppress the capture.
    /// </summary>
    /// <param name="invocation">A delegate that performs exactly one log call on the supplied
    /// logger: typically a <c>[LoggerMessage]</c> method invocation with throwaway argument
    /// values. Must be non-null.</param>
    /// <returns>The captured shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="invocation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="invocation"/> logged zero records
    /// (nothing to capture) or more than one record (ambiguous: pass a lambda that emits exactly
    /// one log call).</exception>
    public static LogDefinition Capture(Action<ILogger> invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var collector = FakeLogCollector.Create(
            new FakeLogCollectorOptions { CollectRecordsForDisabledLogLevels = true });
        invocation(new FakeLogger(collector));

        var records = collector.GetSnapshot();
        if (records.Count is 0)
        {
            throw new ArgumentException(
                "The definition invocation logged nothing. Pass a lambda that performs exactly one "
                + "log call on the supplied logger, e.g. log => LogMessages.OrderShipped(log, 0, \"\").",
                nameof(invocation));
        }

        if (records.Count > 1)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The definition invocation logged {0} records. Pass a lambda that performs "
                    + "exactly one log call so the captured shape is unambiguous.",
                    records.Count),
                nameof(invocation));
        }

        var record = records[0];
        KeyValuePair<string, string?>[] properties =
            [.. (record.StructuredState ?? []).Where(kvp => !string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))];

        return new LogDefinition(
            record.Id,
            record.Level,
            record.GetStructuredStateValue(OriginalFormatKey),
            properties,
            record.Exception);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="record"/> carries this definition's
    /// identity: equal numeric event ID, equal event name (ordinal), and equal message template
    /// (ordinal). Argument values, level, exception, category, and scopes are not compared.
    /// </summary>
    /// <param name="record">The record to test.</param>
    /// <returns><see langword="true"/> when the record was produced by this definition.</returns>
    internal bool MatchesIdentity(FakeLogRecord record)
        => record.Id.Id == Id.Id
        && string.Equals(record.Id.Name, Id.Name, StringComparison.Ordinal)
        && string.Equals(record.GetStructuredStateValue(OriginalFormatKey), Template, StringComparison.Ordinal);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="record"/> matches this captured call
    /// exactly: the identity (<see cref="MatchesIdentity"/>) plus every placeholder value (as
    /// formatted strings, order-insensitive) plus the exception (both absent, or same runtime
    /// type and equal message). Level is still not compared; chain <c>AtLevel</c> when it matters.
    /// </summary>
    /// <param name="record">The record to test.</param>
    /// <returns><see langword="true"/> when the record matches the captured call exactly.</returns>
    internal bool MatchesCall(FakeLogRecord record)
        => MatchesIdentity(record)
        && ExceptionMatches(record.Exception)
        && PropertiesMatch(record);

    /// <summary>Compares the record's exception against the captured one: both absent, or same
    /// runtime type with an ordinally equal message.</summary>
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

    /// <summary>Compares the record's placeholder values (excluding <c>{OriginalFormat}</c>)
    /// against the captured ones as an order-insensitive multiset of key/value pairs.</summary>
    /// <param name="record">The record to compare.</param>
    /// <returns><see langword="true"/> when the pairs are equal.</returns>
    private bool PropertiesMatch(FakeLogRecord record)
    {
        KeyValuePair<string, string?>[] actual =
            [.. (record.StructuredState ?? []).Where(kvp => !string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))];
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

    /// <summary>Sorts key/value pairs by key then value (ordinal) so two captures of the same
    /// call compare equal regardless of state-entry order (which is not guaranteed).</summary>
    /// <param name="pairs">The pairs to sort. Not mutated; a sorted copy is returned.</param>
    /// <returns>The sorted copy.</returns>
    private static KeyValuePair<string, string?>[] SortPairs(KeyValuePair<string, string?>[] pairs)
        => [.. pairs
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ThenBy(kvp => kvp.Value, StringComparer.Ordinal)];

    /// <summary>Renders the identity as <c>Name#Id "template"</c> (name falls back to <c>?</c>
    /// when the record carried none), for failure-message descriptions and diagnostics.</summary>
    /// <returns>The rendered identity.</returns>
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}#{1} \"{2}\"",
            Id.Name ?? "?",
            Id.Id,
            Template ?? "<no template>");
}
