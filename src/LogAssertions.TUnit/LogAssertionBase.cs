using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// Shared base class for <see cref="HasLoggedAssertion"/>, <see cref="HasNotLoggedAssertion"/>,
/// and <see cref="HasLoggedSequenceAssertion"/>. Implements the filter chain (level, message,
/// exception, structured-state, scope, event, and arbitrary-predicate filters) and the
/// failure-message snapshot rendering. Derived classes own count-expectation semantics and
/// the <c>[AssertionExtension]</c> attribute that registers the entry-point name.
/// </summary>
/// <typeparam name="TSelf">The derived assertion type, returned from filter methods to enable fluent chaining.</typeparam>
public abstract class LogAssertionBase<TSelf> : Assertion<FakeLogCollector>
    where TSelf : LogAssertionBase<TSelf>
{
    /// <summary>
    /// Magic key used by Microsoft.Extensions.Logging to surface the original (pre-substitution)
    /// message template in the structured-state list (e.g. <c>"Order {OrderId} processed"</c>).
    /// </summary>
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly List<Func<FakeLogRecord, bool>> _predicates = [];
    private readonly List<string> _filterDescriptions = [];

    /// <summary>Initialises the base assertion with the supplied TUnit context.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    protected LogAssertionBase(AssertionContext<FakeLogCollector> context) : base(context) { }

    /// <summary>
    /// Records a predicate and its description. Default implementation appends to the shared
    /// filter chain used by single-match assertions; <see cref="HasLoggedSequenceAssertion"/>
    /// overrides this to route predicates into the current sequence step.
    /// </summary>
    /// <param name="predicate">The predicate to add.</param>
    /// <param name="description">Human-readable description for the expectation message.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    protected virtual void AddPredicate(Func<FakeLogRecord, bool> predicate, string description)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(description);
        _predicates.Add(predicate);
        _filterDescriptions.Add(description);
    }

    /// <summary>Filters to records at the specified <paramref name="level"/>.</summary>
    /// <param name="level">The exact log level to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevel(LogLevel level)
    {
        AddPredicate(r => r.Level == level, string.Format(CultureInfo.InvariantCulture, "Level = {0}", level));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevel({level})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is greater than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The minimum log level to match (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevelOrAbove(LogLevel level)
    {
        AddPredicate(r => r.Level >= level, string.Format(CultureInfo.InvariantCulture, "Level >= {0}", level));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevelOrAbove({level})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is less than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The maximum log level to match (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevelOrBelow(LogLevel level)
    {
        AddPredicate(r => r.Level <= level, string.Format(CultureInfo.InvariantCulture, "Level <= {0}", level));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevelOrBelow({level})");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose message contains <paramref name="substring"/> using the specified
    /// <paramref name="comparison"/>. The comparison is explicit by design — pass
    /// <see cref="StringComparison.Ordinal"/> for the most common case.
    /// </summary>
    /// <param name="substring">The substring to search for. Must be non-null.</param>
    /// <param name="comparison">The string comparison to apply.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf Containing(string substring, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(substring);
        AddPredicate(
            r => r.Message.Contains(substring, comparison),
            string.Format(CultureInfo.InvariantCulture, "Message contains \"{0}\" ({1})", substring, comparison));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Containing(\"{substring}\", {comparison})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose message satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">A predicate applied to the log message. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf WithMessage(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        AddPredicate(r => predicate(r.Message), "Message matches predicate");
        Context.ExpressionBuilder.Append(".WithMessage(predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose original message template (the pre-substitution form, e.g.
    /// <c>"Order {OrderId} processed"</c>) equals <paramref name="template"/> exactly (ordinal).
    /// Resolved from the structured-state entry under the <c>{OriginalFormat}</c> key that
    /// Microsoft.Extensions.Logging populates automatically.
    /// </summary>
    /// <param name="template">The exact message template to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
    public TSelf WithMessageTemplate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        AddPredicate(
            r => string.Equals(r.GetStructuredStateValue(OriginalFormatKey), template, StringComparison.Ordinal),
            string.Format(CultureInfo.InvariantCulture, "Template = \"{0}\"", template));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithMessageTemplate(\"{template}\")");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is assignable to
    /// <typeparamref name="TException"/>.
    /// </summary>
    /// <typeparam name="TException">The exception type to match.</typeparam>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithException<TException>() where TException : Exception
    {
        AddPredicate(r => r.Exception is TException, $"Exception is {typeof(TException).Name}");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithException<{typeof(TException).Name}>()");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is non-null and whose
    /// <see cref="Exception.Message"/> contains <paramref name="substring"/> (ordinal).
    /// </summary>
    /// <param name="substring">The substring to search for in the exception's message. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf WithExceptionMessage(string substring)
    {
        ArgumentNullException.ThrowIfNull(substring);
        AddPredicate(
            r => r.Exception?.Message.Contains(substring, StringComparison.Ordinal) ?? false,
            $"Exception message contains \"{substring}\"");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithExceptionMessage(\"{substring}\")");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records containing a structured-state entry with the specified
    /// <paramref name="key"/> and <paramref name="value"/> (ordinal string comparison on the
    /// formatted value).
    /// </summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="value">The expected string value (ordinal comparison); may be <see langword="null"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithProperty(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        AddPredicate(
            r => string.Equals(r.GetStructuredStateValue(key), value, StringComparison.Ordinal),
            $"{key} = \"{value}\"");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", \"{value}\")");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records containing a structured-state entry with the specified <paramref name="key"/>
    /// whose formatted string value satisfies <paramref name="predicate"/>. Use for ranges or
    /// pattern-based matches; for exact equality prefer the string-value overload.
    /// </summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="predicate">A predicate applied to the formatted string value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithProperty(string key, Func<string?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(predicate);
        AddPredicate(
            r => predicate(r.GetStructuredStateValue(key)),
            $"{key} matches predicate");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property with
    /// the specified <paramref name="key"/> and <paramref name="value"/> (compared via
    /// <see cref="object.Equals(object?, object?)"/>). Recognises scopes that implement
    /// <see cref="IEnumerable{T}"/> over <see cref="KeyValuePair{TKey, TValue}"/> with string
    /// keys, which covers the two AOT-friendly idioms: dictionary scopes and the
    /// <see cref="LoggerExtensions.BeginScope(ILogger, string, object?[])"/> message-template form.
    /// Anonymous-object scopes are not inspected (they require reflection to read).
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="value">The expected scope-property value; may be <see langword="null"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        AddPredicate(
            r => ScopePropertyMatches(r, key, v => Equals(v, value)),
            $"Scope {key} = {value ?? "null"}");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\", {value ?? "null"})");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property with
    /// the specified <paramref name="key"/> whose value satisfies <paramref name="predicate"/>.
    /// See <see cref="WithScopeProperty(string, object?)"/> for the recognised scope shapes.
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="predicate">A predicate applied to the scope-property value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key, Func<object?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(predicate);
        AddPredicate(
            r => ScopePropertyMatches(r, key, predicate),
            $"Scope {key} matches predicate");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\", predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records emitted by a logger whose category name equals <paramref name="category"/>
    /// (ordinal comparison).
    /// </summary>
    /// <param name="category">The full category name (typically the logger name) to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public TSelf WithCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        AddPredicate(r => string.Equals(r.Category, category, StringComparison.Ordinal), $"Category = \"{category}\"");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithCategory(\"{category}\")");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose <see cref="FakeLogRecord.Id"/> ID equals <paramref name="eventId"/>.</summary>
    /// <param name="eventId">The numeric event ID to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithEventId(int eventId)
    {
        AddPredicate(
            r => r.Id.Id == eventId,
            string.Format(CultureInfo.InvariantCulture, "EventId = {0}", eventId));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventId({eventId})");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Id"/> name equals <paramref name="eventName"/> (ordinal).
    /// </summary>
    /// <param name="eventName">The event name (the second argument of <see cref="EventId"/>) to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is <see langword="null"/>.</exception>
    public TSelf WithEventName(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        AddPredicate(
            r => string.Equals(r.Id.Name, eventName, StringComparison.Ordinal),
            $"EventName = \"{eventName}\"");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventName(\"{eventName}\")");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records emitted while a scope of type <typeparamref name="TScope"/> was active
    /// on the calling logger (matched against <see cref="FakeLogRecord.Scopes"/>).
    /// </summary>
    /// <typeparam name="TScope">The scope state type to match.</typeparam>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithScope<TScope>()
    {
        AddPredicate(
            r => r.Scopes.OfType<TScope>().Any(),
            $"Scope of type {typeof(TScope).Name} active");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScope<{typeof(TScope).Name}>()");
        return (TSelf)this;
    }

    /// <summary>
    /// Escape-hatch filter that applies an arbitrary <paramref name="predicate"/> to each record.
    /// Use only when no other filter expresses the constraint cleanly.
    /// </summary>
    /// <param name="predicate">A predicate applied to each <see cref="FakeLogRecord"/>. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf Where(Func<FakeLogRecord, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        AddPredicate(predicate, "Custom predicate");
        Context.ExpressionBuilder.Append(".Where(predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Counts records in <paramref name="snapshot"/> that satisfy every filter predicate.
    /// </summary>
    /// <param name="snapshot">The captured records to evaluate.</param>
    /// <returns>The number of matching records.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected int CountMatches(IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Count(r => _predicates.Count == 0 || _predicates.TrueForAll(p => p(r)));
    }

    /// <summary>
    /// Appends the human-readable filter chain (e.g. <c> matching: Level = Warning, Message contains "x"</c>)
    /// to <paramref name="sb"/>. Emits nothing if no filters have been added.
    /// </summary>
    /// <param name="sb">The target string builder for the expectation message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sb"/> is <see langword="null"/>.</exception>
    protected void AppendFilterSummary(StringBuilder sb)
    {
        ArgumentNullException.ThrowIfNull(sb);
        if (_filterDescriptions.Count > 0)
        {
            sb.Append(" matching: ")
                .AppendJoin(", ", _filterDescriptions);
        }
    }

    /// <summary>
    /// Renders the matching summary plus a snapshot of every captured record (level, category,
    /// message, structured properties, scopes, exception) for use in failure messages.
    /// </summary>
    /// <param name="matchCount">The number of matching records.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <returns>The multi-line failure message body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected static string BuildFailureMessage(int matchCount, IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture, $"{matchCount} record(s) matched")
            .AppendLine()
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"Captured records ({snapshot.Count} total):")
            .AppendLine();

        AppendCapturedRecords(sb, snapshot);
        return sb.ToString();
    }

    /// <summary>
    /// Appends the captured-records section of the failure message: one summary line per
    /// record (<c>[lvl] category: message</c>) followed by indented detail lines for any
    /// structured properties, active scopes, and exception (when present).
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    protected static void AppendCapturedRecords(StringBuilder sb, IReadOnlyList<FakeLogRecord> snapshot)
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
    /// <c>warn</c>, <c>fail</c>, <c>crit</c>). Anything outside the standard range falls back
    /// to the level's invariant <c>ToString()</c>.
    /// </summary>
    /// <param name="level">The log level to abbreviate.</param>
    /// <returns>The 4-character abbreviation.</returns>
    private static string LevelAbbreviation(LogLevel level) => level switch
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

    /// <summary>
    /// Appends an indented <c>props:</c> line listing the record's structured-state entries,
    /// excluding the magic <c>{OriginalFormat}</c> entry (the raw template, already implied
    /// by the message line). Emits nothing if no structured state is present.
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="record">The record to render.</param>
    private static void AppendStructuredState(StringBuilder sb, FakeLogRecord record)
    {
        if (record.StructuredState is null || record.StructuredState.Count == 0)
            return;

        var first = true;
        foreach (var kvp in record.StructuredState)
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

    /// <summary>
    /// Appends an indented <c>scope:</c> line listing each active scope. Scopes that expose
    /// key-value pairs are rendered as <c>key=value</c> pairs; opaque scope objects are rendered
    /// via <see cref="object.ToString"/>. Emits nothing if no scopes are present.
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="record">The record to render.</param>
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

    /// <summary>
    /// Appends a single scope's representation: as <c>key=value</c> pairs when the scope is
    /// enumerable as <see cref="KeyValuePair{TKey, TValue}"/>, otherwise as the scope's
    /// <see cref="object.ToString"/> (or <c>"null"</c> when the scope is null).
    /// </summary>
    /// <param name="sb">The target string builder.</param>
    /// <param name="scope">The scope object.</param>
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

    /// <summary>
    /// Attempts to render <paramref name="scope"/> as comma-separated <c>key=value</c> pairs by
    /// casting to <see cref="IEnumerable{T}"/> over <see cref="KeyValuePair{TKey, TValue}"/>
    /// with string keys and a generic value type. Skips the magic <c>{OriginalFormat}</c> entry.
    /// </summary>
    /// <typeparam name="TValue">The value type of the scope's key-value pairs.</typeparam>
    /// <param name="sb">The target string builder.</param>
    /// <param name="scope">The scope object.</param>
    /// <returns><see langword="true"/> when at least one pair was rendered.</returns>
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

    /// <summary>
    /// Tests whether any of the record's scopes contains a key-value pair where the key equals
    /// <paramref name="key"/> (ordinal) and the value satisfies <paramref name="predicate"/>.
    /// Recognises both <c>object</c> and <c>object?</c> value-type variants of the
    /// <see cref="IEnumerable{T}"/>-of-<see cref="KeyValuePair{TKey, TValue}"/> shape.
    /// </summary>
    /// <param name="record">The record to inspect.</param>
    /// <param name="key">The scope-property key.</param>
    /// <param name="predicate">The predicate applied to the matched value.</param>
    /// <returns><see langword="true"/> when at least one scope matched.</returns>
    private static bool ScopePropertyMatches(FakeLogRecord record, string key, Func<object?, bool> predicate)
    {
        foreach (var scope in record.Scopes)
        {
            if (scope is null)
                continue;

            if (TryMatchScope<object?>(scope, key, predicate) || TryMatchScope<object>(scope, key, predicate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Inspects a single scope cast to <see cref="IEnumerable{T}"/> over
    /// <see cref="KeyValuePair{TKey, TValue}"/> with string keys, returning <see langword="true"/>
    /// when the scope contains an entry whose key equals <paramref name="key"/> (ordinal) and
    /// whose value satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <typeparam name="TValue">The value type of the scope's key-value pairs.</typeparam>
    /// <param name="scope">The scope object.</param>
    /// <param name="key">The scope-property key.</param>
    /// <param name="predicate">The predicate applied to the matched value.</param>
    /// <returns><see langword="true"/> when the scope matches.</returns>
    private static bool TryMatchScope<TValue>(object scope, string key, Func<object?, bool> predicate)
    {
        if (scope is not IEnumerable<KeyValuePair<string, TValue>> kvps)
            return false;

        return kvps
            .Where(kvp => string.Equals(kvp.Key, key, StringComparison.Ordinal))
            .Any(kvp => predicate(kvp.Value));
    }
}
