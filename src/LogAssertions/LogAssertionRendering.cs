using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Shared helpers for rendering captured log records into human-readable text. Used both by
/// the assertion classes (failure-message snapshot rendering) and by the
/// <see cref="FakeLogCollectorInspectionExtensions.DumpTo"/> extension.
/// </summary>
/// <remarks>
/// The rendering format is documented as <b>not stable</b>; see the README "Stability
/// promise" section. Tests should not pin exact failure-message text — pin filter
/// match counts and broad markers (e.g. <c>"[warn]"</c>) only.
/// </remarks>
public static class LogAssertionRendering
{
    /// <summary>
    /// Magic key used by Microsoft.Extensions.Logging to surface the original (pre-substitution)
    /// message template in the structured-state list (e.g. <c>"Order {OrderId} processed"</c>).
    /// </summary>
    internal const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>
    /// Appends the captured-records section to <paramref name="sb"/>: one summary line per
    /// record (<c>[lvl] category: message</c>) followed by indented detail lines for any
    /// structured properties, active scopes, and exception (when present).
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void AppendCapturedRecords(StringBuilder sb, IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(sb);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Count == 0)
        {
            sb.AppendLine("  (no records)");
            return;
        }

        foreach (FakeLogRecord record in snapshot)
        {
            sb.Append("  [").Append(LevelAbbreviation(record.Level)).Append("] ");
            if (!string.IsNullOrEmpty(record.Category))
                sb.Append(record.Category).Append(": ");
            sb.Append(record.Message).AppendLine();

            AppendStructuredState(sb, record);
            AppendScopes(sb, record);

            if (record.Exception is not null)
            {
                sb.Append("    exception: ")
                    .Append(record.Exception.GetType().Name)
                    .Append(": ")
                    .Append(record.Exception.Message)
                    .AppendLine();
            }
        }
    }

    /// <summary>
    /// 4-character abbreviation of a log level, matching the conventional
    /// Microsoft.Extensions.Logging console formatter (<c>trce</c>, <c>dbug</c>, <c>info</c>,
    /// <c>warn</c>, <c>fail</c>, <c>crit</c>, <c>none</c>). Anything outside the standard
    /// range falls back to the level's invariant <c>ToString()</c>.
    /// </summary>
    /// <param name="level">The log level to abbreviate.</param>
    /// <returns>The 4-character abbreviation.</returns>
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
        if (record.State is not IReadOnlyList<KeyValuePair<string, string?>> kvps || kvps.Count == 0)
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
        if (record.Scopes.Count == 0)
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
