using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Shared helpers for rendering captured log records into human-readable text. Used both by
/// the assertion classes (failure-message snapshot rendering) and by the
/// <c>FakeLogCollectorInspectionExtensions.DumpTo</c> extension.
/// </summary>
/// <remarks>
/// The rendering format is documented as <b>not stable</b>; see the README "Stability
/// intent (pre-1.0)" section. Tests should not pin exact failure-message text: pin filter
/// match counts and broad markers (e.g. <c>"[warn]"</c>) only.
/// </remarks>
public static class LogAssertionRendering
{
    /// <summary>
    /// Magic key used by Microsoft.Extensions.Logging to surface the original (pre-substitution)
    /// message template in the structured-state list (e.g. <c>"Order {OrderId} processed"</c>).
    /// </summary>
    internal const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>The most records a failure message shows: the latest ones, after a line that counts the earlier ones.
    /// Explicit dumps are not capped.</summary>
    internal const int FailureRecordLimit = 50;

    /// <summary>
    /// Appends the captured-records section to <paramref name="sb"/>: one summary line per
    /// record (<c>[lvl] category: message</c>) followed by indented detail lines for any
    /// structured properties, active scopes, and exception (when present). Equivalent to
    /// <see cref="AppendCapturedRecords(StringBuilder, IReadOnlyList{FakeLogRecord}, DumpVerbosity)"/>
    /// with <see cref="DumpVerbosity.Default"/>.
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void AppendCapturedRecords(StringBuilder sb, IReadOnlyList<FakeLogRecord> snapshot)
        => AppendCapturedRecords(sb, snapshot, DumpVerbosity.Default);

    /// <summary>Verbosity-controlled overload of <see cref="AppendCapturedRecords(StringBuilder, IReadOnlyList{FakeLogRecord})"/>.</summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <param name="verbosity">How much detail to render per record. See
    /// <see cref="DumpVerbosity"/> for the contract of each level.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void AppendCapturedRecords(StringBuilder sb, IReadOnlyList<FakeLogRecord> snapshot, DumpVerbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(snapshot);
        AppendRecords(sb, snapshot, 0, verbosity, withTimestamps: false);
    }

    /// <summary>
    /// Appends the records section of a failure message: the last <see cref="FailureRecordLimit"/> records of
    /// <paramref name="records"/>, in the format of
    /// <see cref="AppendCapturedRecords(StringBuilder, IReadOnlyList{FakeLogRecord}, DumpVerbosity)"/>, after one line
    /// <c>(N earlier record(s) not shown)</c> when there are more. A long run's collector can hold thousands of
    /// records; the latest ones are the context of a failure.
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="records">The records, oldest first.</param>
    internal static void AppendFailureRecords(StringBuilder sb, IReadOnlyList<FakeLogRecord> records)
    {
        var start = Math.Max(0, records.Count - FailureRecordLimit);
        if (start > 0)
        {
            sb.Append("  (").Append(start).AppendLine(" earlier record(s) not shown)");
        }

        AppendRecords(sb, records, start, DumpVerbosity.Default, withTimestamps: false);
    }

    /// <summary>
    /// Appends the records of <paramref name="records"/> from index <paramref name="start"/> on, in the format of
    /// <see cref="AppendCapturedRecords(StringBuilder, IReadOnlyList{FakeLogRecord}, DumpVerbosity)"/>, optionally
    /// with each record's timestamp (UTC, <c>HH:mm:ss.fff</c>) before its level. Writes <c>(no records)</c> when
    /// there are none from <paramref name="start"/> on.
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="records">The captured records.</param>
    /// <param name="start">The index of the first record to append.</param>
    /// <param name="verbosity">How much detail to render per record.</param>
    /// <param name="withTimestamps">Whether each record's line starts with its timestamp.</param>
    internal static void AppendRecords(
        StringBuilder sb, IReadOnlyList<FakeLogRecord> records, int start, DumpVerbosity verbosity, bool withTimestamps)
    {
        if (records.Count <= start)
        {
            sb.AppendLine("  (no records)");
            return;
        }

        for (var i = start; i < records.Count; i++)
        {
            AppendRecord(sb, records[i], verbosity, withTimestamps);
        }
    }

    /// <summary>Appends one record: its headline, then, unless <paramref name="verbosity"/> is
    /// <see cref="DumpVerbosity.Compact"/>, its properties and scopes (not at
    /// <see cref="DumpVerbosity.CompactWithException"/>) and its exception.</summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="record">The record.</param>
    /// <param name="verbosity">How much detail to render.</param>
    /// <param name="withTimestamp">Whether the headline starts with the record's timestamp.</param>
    private static void AppendRecord(StringBuilder sb, FakeLogRecord record, DumpVerbosity verbosity, bool withTimestamp)
    {
        sb.Append("  ");
        if (withTimestamp)
        {
            sb.Append(record.Timestamp.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ');
        }

        sb.Append('[').Append(LevelAbbreviation(record.Level)).Append("] ");
        if (!string.IsNullOrEmpty(record.Category))
            sb.Append(record.Category).Append(": ");
        sb.Append(record.Message).AppendLine();

        if (verbosity is DumpVerbosity.Compact)
            return;

        if (verbosity is not DumpVerbosity.CompactWithException)
        {
            AppendStructuredState(sb, record);
            AppendScopes(sb, record);
        }

        if (record.Exception is not null)
            AppendException(sb, record.Exception, verbosity);
    }

    /// <summary>Appends the exception line and, at <see cref="DumpVerbosity.Verbose"/>, the full exception text.</summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="exception">The record's exception.</param>
    /// <param name="verbosity">How much detail to render.</param>
    private static void AppendException(StringBuilder sb, Exception exception, DumpVerbosity verbosity)
    {
        sb.Append("    exception: ")
            .Append(exception.GetType().Name)
            .Append(": ")
            .Append(exception.Message)
            .AppendLine();

        if (verbosity is not DumpVerbosity.Verbose)
            return;

        // Full ToString() carries the stack trace and inner-exception chain. Indented
        // by 6 spaces so it visually nests under the "exception:" line and survives
        // log-aggregator rendering.
        foreach (var line in exception.ToString().Split('\n'))
        {
            sb.Append("      ").Append(line.TrimEnd('\r')).AppendLine();
        }
    }

    /// <summary>
    /// 4-character abbreviation of a log level, matching the conventional
    /// Microsoft.Extensions.Logging console formatter (<c>trce</c>, <c>dbug</c>, <c>info</c>,
    /// <c>warn</c>, <c>fail</c>, <c>crit</c>, <c>none</c>). Anything outside the standard
    /// range falls back to the level's invariant <c>ToString()</c>.
    /// </summary>
    /// <param name="level">The log level to abbreviate.</param>
    /// <returns>The 4-character abbreviation, or the level's <c>ToString()</c> for a level outside the standard
    /// range.</returns>
    public static string LevelAbbreviation(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        LogLevel.Critical => "crit",
        LogLevel.None => "none",
        _ => level.ToString(),
    };

    private static void AppendStructuredState(StringBuilder sb, FakeLogRecord record)
    {
        // FakeLogRecord.StructuredState throws when State is not a key-value-pair list
        // (e.g. ILogger.Log<TState> with custom typed state). Defensive cast on State
        // directly avoids the throwing getter.
        if (record.State is not IReadOnlyList<KeyValuePair<string, string?>> kvps || kvps.Count is 0)
            return;

        var first = true;
        foreach (var kvp in kvps)
        {
            if (string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))
                continue;

            sb.Append(first ? "    props: " : ", ")
                .Append(kvp.Key).Append('=').Append(kvp.Value ?? "null");
            first = false;
        }

        if (!first)
            sb.AppendLine();
    }

    private static void AppendScopes(StringBuilder sb, FakeLogRecord record)
    {
        if (record.Scopes.Count is 0)
            return;

        var first = true;
        foreach (var scope in record.Scopes)
        {
            sb.Append(first ? "    scope: " : " | ");
            AppendScope(sb, scope);
            first = false;
        }

        if (!first)
            sb.AppendLine();
    }

    private static void AppendScope(StringBuilder sb, object? scope)
    {
        if (scope is null)
        {
            sb.Append("null");
            return;
        }

        if (TryAppendKeyValuePairs<object?>(sb, scope) || TryAppendKeyValuePairs<object>(sb, scope))
            return;

        sb.Append(scope);
    }

    private static bool TryAppendKeyValuePairs<TValue>(StringBuilder sb, object scope)
    {
        if (scope is not IEnumerable<KeyValuePair<string, TValue>> kvps)
            return false;

        var any = false;
        foreach (var kvp in kvps)
        {
            if (string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))
                continue;

            if (any)
                sb.Append(", ");
            sb.Append(kvp.Key).Append('=').Append(kvp.Value is null ? "null" : kvp.Value.ToString());
            any = true;
        }

        return any;
    }
}
