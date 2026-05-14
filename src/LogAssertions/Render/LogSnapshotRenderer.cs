using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Render;

/// <summary>
/// Pure renderer that converts a <see cref="FakeLogCollector"/>'s captured records into
/// deterministic, snapshot-friendly multi-line text. Each record renders as a header line
/// (<c>[NN] level category "message"</c>) followed by optional indented detail lines for
/// structured state, active scopes, and an attached exception. Records are separated by a
/// single blank line.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stability contract.</b> Unlike <see cref="LogAssertionRendering"/> (whose output is
/// explicitly documented as not stable and is meant only for failure messages), the output
/// of this renderer <i>is</i> a stable, pin-able contract. Snapshot baselines produced by
/// <see cref="Render(FakeLogCollector, LogSnapshotOptions?)"/> are intended to be committed
/// and diffed. A change to the rendered shape is a versioned, documented change.
/// </para>
/// <para>
/// <b>Pairs with snapshot assertions.</b> Render once, then pin the result against a
/// baseline:
/// </para>
/// <code>
/// var rendered = LogSnapshotRenderer.Render(collector);
/// await Assert.That(rendered).MatchesSnapshot();
/// </code>
/// <para>
/// The <c>MatchesSnapshot()</c> extension lives in the sibling <c>SnapshotAssertions.TUnit</c>
/// package; this package does not depend on it. The two-line composition is deliberate: it
/// lets consumers reach for the renderer without committing to a specific snapshot framework,
/// and keeps the snapshot package an opt-in pairing rather than a transitive dependency.
/// </para>
/// <para>
/// <b>No baked-in scrubbing.</b> This renderer emits the captured text verbatim; it does not
/// scrub GUIDs, timestamps, or durations out of message bodies. Volatile values are the
/// concern of the snapshot layer: compose <c>SnapshotAssertions.Scrubbers</c> at the
/// <c>MatchesSnapshot()</c> call site. Keeping rendering and scrubbing separate means the
/// renderer has one job, the scrubber set stays in one place, and neither has to know about
/// the other.
/// </para>
/// <para>
/// <b>Deterministic line endings.</b> Lines are terminated with the literal LF byte
/// (<c>'\n'</c>), never <see cref="Environment.NewLine"/>. The CRLF / LF split between Windows
/// and Unix would otherwise serialise the same records differently per OS, breaking snapshot
/// baselines on cross-platform CI. Hardcoding LF keeps a baseline committed on one platform
/// valid for test runs on every other.
/// </para>
/// <para>
/// <b>Capture-order preserving.</b> Records render in the order <see cref="FakeLogCollector"/>
/// captured them; the renderer never sorts. A regression that keeps per-record predicate
/// assertions passing while reordering, dropping, or inserting records still surfaces as a
/// snapshot diff.
/// </para>
/// <para>
/// <b>Defensive structured-state handling.</b> The renderer reads <see cref="FakeLogRecord"/>
/// state through the <see cref="FakeLogRecord.StructuredState"/> property inside a
/// <see langword="try"/>/<see langword="catch"/> for <see cref="InvalidCastException"/>. That
/// property hard-casts the captured state to a key-value-pair list and throws when it is not
/// one (e.g. <c>ILogger.Log&lt;TState&gt;</c> with a custom typed state); a record with
/// non-KVP state simply renders without a <c>state:</c> line instead of throwing.
/// </para>
/// </remarks>
public static class LogSnapshotRenderer
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>
    /// Renders every captured record from <paramref name="collector"/> into a deterministic,
    /// snapshot-friendly multi-line string.
    /// </summary>
    /// <param name="collector">The fake collector whose captured records to render.</param>
    /// <param name="options">Rendering options, or <see langword="null"/> for
    /// <see cref="LogSnapshotOptions.Default"/>.</param>
    /// <returns>A multi-line string with one block per captured record, or
    /// <see cref="string.Empty"/> when the collector has captured nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> is
    /// <see langword="null"/>.</exception>
    public static string Render(FakeLogCollector collector, LogSnapshotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(collector);
        options ??= LogSnapshotOptions.Default;

        IReadOnlyList<FakeLogRecord> records = collector.GetSnapshot();
        if (records.Count is 0)
        {
            return string.Empty;
        }

        // Header + optional state / scope / exception lines: ~128 bytes is a conservative
        // per-record upper bound that avoids the StringBuilder resize cascade on large logs.
        var sb = new StringBuilder(capacity: records.Count * 128);
        for (var i = 0; i < records.Count; i++)
        {
            FakeLogRecord record = records[i];
            AppendHeader(sb, i, record, options);
            AppendStructuredState(sb, record);

            if (options.ScopeStyle is ScopeStyle.Include)
            {
                AppendScopes(sb, record);
            }

            if (record.Exception is not null)
            {
                AppendException(sb, record.Exception, options.ExceptionStyle);
            }

            // Blank line between records (the previous line already ends in '\n').
            if (i < records.Count - 1)
            {
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, int index, FakeLogRecord record, LogSnapshotOptions options)
    {
        sb.Append('[')
          .Append(index.ToString("D2", CultureInfo.InvariantCulture))
          .Append("] ")
          .Append(RenderLevel(record.Level, options.LevelStyle))
          .Append(' ')
          .Append(RenderCategory(record.Category ?? string.Empty, options.CategoryStyle))
          .Append(" \"")
          .Append(record.Message)
          .Append("\"\n");
    }

    private static string RenderLevel(LogLevel level, LogLevelStyle style) => style switch
    {
        LogLevelStyle.Abbreviation => LogAssertionRendering.LevelAbbreviation(level),
        _ => level.ToString(),
    };

    private static string RenderCategory(string category, CategoryStyle style)
    {
        if (style is CategoryStyle.Full)
        {
            return category;
        }

        var lastDot = category.LastIndexOf('.');
        return lastDot < 0 ? category : category[(lastDot + 1)..];
    }

    private static void AppendStructuredState(StringBuilder sb, FakeLogRecord record)
    {
        IReadOnlyList<KeyValuePair<string, string?>>? state;
        try
        {
            state = record.StructuredState;
        }
        catch (InvalidCastException)
        {
            // FakeLogRecord.StructuredState hard-casts the captured state to a key-value-pair
            // list and throws InvalidCastException when it is a custom typed object instead
            // (e.g. ILogger.Log<TState> with a custom TState). Such a record renders without
            // a state line instead of throwing.
            return;
        }

        if (state is null || state.Count is 0)
        {
            return;
        }

        var first = true;
        foreach (var kvp in state)
        {
            if (string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append(first ? "    state: " : "; ")
              .Append(kvp.Key)
              .Append('=')
              .Append(kvp.Value ?? "null");
            first = false;
        }

        if (!first)
        {
            sb.Append('\n');
        }
    }

    private static void AppendScopes(StringBuilder sb, FakeLogRecord record)
    {
        if (record.Scopes.Count is 0)
        {
            return;
        }

        var first = true;
        foreach (var scope in record.Scopes)
        {
            sb.Append(first ? "    scope: " : " | ");
            AppendScope(sb, scope);
            first = false;
        }

        if (!first)
        {
            sb.Append('\n');
        }
    }

    private static void AppendScope(StringBuilder sb, object? scope)
    {
        if (scope is null)
        {
            sb.Append("null");
            return;
        }

        if (scope is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            var any = false;
            foreach (var kvp in kvps)
            {
                if (string.Equals(kvp.Key, OriginalFormatKey, StringComparison.Ordinal))
                {
                    continue;
                }

                if (any)
                {
                    sb.Append(", ");
                }

                sb.Append(kvp.Key)
                  .Append('=')
                  .Append(Convert.ToString(kvp.Value, CultureInfo.InvariantCulture) ?? "null");
                any = true;
            }

            if (any)
            {
                return;
            }
        }

        sb.Append(Convert.ToString(scope, CultureInfo.InvariantCulture) ?? "null");
    }

    private static void AppendException(StringBuilder sb, Exception exception, ExceptionStyle style)
    {
        if (style is ExceptionStyle.Full)
        {
            sb.Append("    exception:\n");
            foreach (var line in exception.ToString().Split('\n'))
            {
                sb.Append("      ").Append(line.TrimEnd('\r')).Append('\n');
            }

            return;
        }

        sb.Append("    exception: ")
          .Append(exception.GetType().Name)
          .Append(": ")
          .Append(exception.Message);

        if (style is ExceptionStyle.StackTracePlaceholder)
        {
            sb.Append(" {STACKTRACE}");
        }

        sb.Append('\n');
    }
}
