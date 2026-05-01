using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
/// <remarks>
/// <para>
/// <b>Not for external derivation.</b> This type is public only because the curiously-recurring
/// template pattern (CRTP) used here requires public visibility wherever the public sealed
/// derived classes (<see cref="HasLoggedAssertion"/> etc.) appear. The shape of this base
/// class — protected members, virtual hooks, internal helpers — is implementation detail
/// and may change in any release. Do not derive from it; do not reference its protected
/// members from outside this assembly. The supported public surface is the entry-point
/// extension methods on <c>FakeLogCollector</c> plus the fluent chain methods returning
/// <typeparamref name="TSelf"/>.
/// </para>
/// <para>
/// See the README "Stability promise" section for the full surface-stability contract.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class LogAssertionBase<TSelf> : Assertion<FakeLogCollector>
    where TSelf : LogAssertionBase<TSelf>
{
    /// <summary>
    /// Magic key used by Microsoft.Extensions.Logging to surface the original (pre-substitution)
    /// message template in the structured-state list (e.g. <c>"Order {OrderId} processed"</c>).
    /// </summary>
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly List<ILogRecordFilter> _filters = [];

    /// <summary>Initialises the base assertion with the supplied TUnit context.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    protected LogAssertionBase(AssertionContext<FakeLogCollector> context) : base(context) { }

    /// <summary>
    /// Records a filter. Default implementation appends to the shared filter chain used by
    /// single-match assertions; <see cref="HasLoggedSequenceAssertion"/> overrides this to
    /// route filters into the current sequence step.
    /// </summary>
    /// <param name="filter">The filter to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    protected virtual void AddFilter(ILogRecordFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _filters.Add(filter);
    }

    /// <summary>Filters to records at the specified <paramref name="level"/>.</summary>
    /// <param name="level">The exact log level to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevel(LogLevel level)
    {
        AddFilter(LogFilter.AtLevel(level));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevel({level})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is greater than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The minimum log level to match (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevelOrAbove(LogLevel level)
    {
        AddFilter(LogFilter.AtLevelOrAbove(level));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevelOrAbove({level})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is less than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The maximum log level to match (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevelOrBelow(LogLevel level)
    {
        AddFilter(LogFilter.AtLevelOrBelow(level));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLevelOrBelow({level})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is one of <paramref name="levels"/>.</summary>
    /// <param name="levels">The set of log levels to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is <see langword="null"/>.</exception>
    public TSelf AtAnyLevel(params LogLevel[] levels)
    {
        AddFilter(LogFilter.AtLevel(levels));
        Context.ExpressionBuilder.Append(".AtAnyLevel(...)");
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
        AddFilter(LogFilter.Containing(substring, comparison));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Containing(\"{substring}\", {comparison})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose message contains every one of <paramref name="substrings"/>.</summary>
    /// <param name="comparison">The string comparison to apply.</param>
    /// <param name="substrings">The substrings; the message must contain all of them.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substrings"/> is <see langword="null"/>.</exception>
    public TSelf ContainingAll(StringComparison comparison, params string[] substrings)
    {
        AddFilter(LogFilter.ContainingAll(comparison, substrings));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ContainingAll({comparison}, ...)");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose message contains at least one of <paramref name="substrings"/>.</summary>
    /// <param name="comparison">The string comparison to apply.</param>
    /// <param name="substrings">The substrings; the message must contain at least one.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substrings"/> is <see langword="null"/>.</exception>
    public TSelf ContainingAny(StringComparison comparison, params string[] substrings)
    {
        AddFilter(LogFilter.ContainingAny(comparison, substrings));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ContainingAny({comparison}, ...)");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose message matches the regular expression <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The compiled regex. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public TSelf Matching(Regex pattern)
    {
        AddFilter(LogFilter.Matching(pattern));
        Context.ExpressionBuilder.Append(".Matching(/regex/)");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose message satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">A predicate applied to the log message. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf WithMessage(Func<string, bool> predicate)
    {
        AddFilter(LogFilter.WithMessage(predicate));
        Context.ExpressionBuilder.Append(".WithMessage(predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose original message template (the pre-substitution form, e.g.
    /// <c>"Order {OrderId} processed"</c>) equals <paramref name="template"/> exactly (ordinal).
    /// </summary>
    /// <param name="template">The exact message template to match. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
    public TSelf WithMessageTemplate(string template)
    {
        AddFilter(LogFilter.WithMessageTemplate(template));
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
        AddFilter(LogFilter.WithException<TException>());
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithException<{typeof(TException).Name}>()");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> is non-null (any type).
    /// </summary>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithException()
    {
        AddFilter(LogFilter.WithException());
        Context.ExpressionBuilder.Append(".WithException()");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Exception"/> satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="predicate">A predicate over the (non-null) exception. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public TSelf WithException(Func<Exception, bool> predicate)
    {
        AddFilter(LogFilter.WithException(predicate));
        Context.ExpressionBuilder.Append(".WithException(predicate)");
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
        AddFilter(LogFilter.WithExceptionMessage(substring));
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
        AddFilter(LogFilter.WithProperty(key, value));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", \"{value}\")");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records containing a structured-state entry with the specified <paramref name="key"/>
    /// whose formatted string value satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="predicate">A predicate applied to the formatted string value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithProperty(string key, Func<string?, bool> predicate)
    {
        AddFilter(LogFilter.WithProperty(key, predicate));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithProperty(\"{key}\", predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property
    /// with the specified <paramref name="key"/> and <paramref name="value"/> (compared via
    /// <see cref="object.Equals(object?, object?)"/>).
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="value">The expected scope-property value; may be <see langword="null"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key, object? value)
    {
        AddFilter(LogFilter.WithScopeProperty(key, value));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithScopeProperty(\"{key}\", {value ?? "null"})");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records emitted while a scope on the calling logger contained a property
    /// with the specified <paramref name="key"/> whose value satisfies <paramref name="predicate"/>.
    /// </summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="predicate">A predicate applied to the scope-property value. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public TSelf WithScopeProperty(string key, Func<object?, bool> predicate)
    {
        AddFilter(LogFilter.WithScopeProperty(key, predicate));
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
        AddFilter(LogFilter.WithCategory(category));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithCategory(\"{category}\")");
        return (TSelf)this;
    }

    /// <summary>Alias for <see cref="WithCategory(string)"/> using the more colloquial name.</summary>
    /// <param name="loggerName">The full logger name (the category). Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loggerName"/> is <see langword="null"/>.</exception>
    public TSelf WithLoggerName(string loggerName)
    {
        AddFilter(LogFilter.WithCategory(loggerName));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithLoggerName(\"{loggerName}\")");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose <see cref="FakeLogRecord.Id"/> ID equals <paramref name="eventId"/>.</summary>
    /// <param name="eventId">The numeric event ID to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf WithEventId(int eventId)
    {
        AddFilter(LogFilter.WithEventId(eventId));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventId({eventId})");
        return (TSelf)this;
    }

    /// <summary>
    /// Filters to records whose <see cref="FakeLogRecord.Id"/> ID is within the inclusive range
    /// <paramref name="min"/>..<paramref name="max"/>.
    /// </summary>
    /// <param name="min">The minimum event ID (inclusive).</param>
    /// <param name="max">The maximum event ID (inclusive).</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public TSelf WithEventIdInRange(int min, int max)
    {
        AddFilter(LogFilter.WithEventIdInRange(min, max));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithEventIdInRange({min}, {max})");
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
        AddFilter(LogFilter.WithEventName(eventName));
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
        AddFilter(LogFilter.WithScope<TScope>());
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
        AddFilter(LogFilter.Where(predicate));
        Context.ExpressionBuilder.Append(".Where(predicate)");
        return (TSelf)this;
    }

    /// <summary>
    /// Adds a user-supplied <see cref="ILogRecordFilter"/> to the chain. Use this to plug in
    /// composable filter objects built via <see cref="LogFilter"/> factory methods, or
    /// implementations of <see cref="ILogRecordFilter"/> shared across many tests.
    /// </summary>
    /// <param name="filter">The filter. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    public TSelf WithFilter(ILogRecordFilter filter)
    {
        AddFilter(filter);
        Context.ExpressionBuilder.Append(".WithFilter(...)");
        return (TSelf)this;
    }

    /// <summary>
    /// Adds a disjunction (OR) of <paramref name="filters"/> as a single composite filter on
    /// the chain. The chain itself is AND-combined; this method composes a sub-disjunction
    /// inside that AND, enabling expressions such as
    /// <c>.AtLevel(Warning).MatchingAny(LogFilter.Containing("a", Ordinal), LogFilter.Containing("b", Ordinal))</c>
    /// = <c>level == Warning AND (msg contains "a" OR msg contains "b")</c>.
    /// </summary>
    /// <param name="filters">The disjunction's children. May be empty (matches no record).</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    public TSelf MatchingAny(params ILogRecordFilter[] filters)
    {
        AddFilter(LogFilter.Any(filters));
        Context.ExpressionBuilder.Append(".MatchingAny(...)");
        return (TSelf)this;
    }

    /// <summary>
    /// Adds a conjunction (AND) of <paramref name="filters"/> as a single composite filter
    /// on the chain. Equivalent to chaining the filters individually but useful when composing
    /// pre-built reusable filters.
    /// </summary>
    /// <param name="filters">The conjunction's children. May be empty (matches every record).</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    public TSelf MatchingAll(params ILogRecordFilter[] filters)
    {
        AddFilter(LogFilter.All(filters));
        Context.ExpressionBuilder.Append(".MatchingAll(...)");
        return (TSelf)this;
    }

    /// <summary>
    /// Adds the negation of <paramref name="filter"/> to the chain. A record matches when the
    /// inner filter does <em>not</em>.
    /// </summary>
    /// <param name="filter">The filter to negate. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    public TSelf Not(ILogRecordFilter filter)
    {
        AddFilter(LogFilter.Not(filter));
        Context.ExpressionBuilder.Append(".Not(...)");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose message does not contain <paramref name="substring"/>.</summary>
    /// <param name="substring">The substring that must not appear. Must be non-null.</param>
    /// <param name="comparison">The string comparison.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf NotContaining(string substring, StringComparison comparison)
    {
        AddFilter(LogFilter.Not(LogFilter.Containing(substring, comparison)));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".NotContaining(\"{substring}\", {comparison})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is not <paramref name="level"/>.</summary>
    /// <param name="level">The log level to exclude.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf NotAtLevel(LogLevel level)
    {
        AddFilter(LogFilter.Not(LogFilter.AtLevel(level)));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".NotAtLevel({level})");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose category is not <paramref name="category"/>.</summary>
    /// <param name="category">The category name to exclude. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public TSelf ExcludingCategory(string category)
    {
        AddFilter(LogFilter.Not(LogFilter.WithCategory(category)));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ExcludingCategory(\"{category}\")");
        return (TSelf)this;
    }

    /// <summary>Filters to records whose level is not <paramref name="level"/> (alias for <see cref="NotAtLevel"/>).</summary>
    /// <param name="level">The log level to exclude.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf ExcludingLevel(LogLevel level) => NotAtLevel(level);

    /// <summary>
    /// Counts records in <paramref name="snapshot"/> that satisfy every filter in the chain.
    /// </summary>
    /// <param name="snapshot">The captured records to evaluate.</param>
    /// <returns>The number of matching records.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected int CountMatches(IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Count(r => _filters.Count == 0 || _filters.TrueForAll(f => f.Matches(r)));
    }

    /// <summary>
    /// Returns the matching records from <paramref name="snapshot"/> as a snapshot list (a defensive
    /// copy not bound to the live collector).
    /// </summary>
    /// <param name="snapshot">The captured records to evaluate.</param>
    /// <returns>The matched records in original order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    protected IReadOnlyList<FakeLogRecord> GetMatches(IReadOnlyList<FakeLogRecord> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return [.. snapshot.Where(r => _filters.Count == 0 || _filters.TrueForAll(f => f.Matches(r)))];
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
        if (_filters.Count > 0)
        {
            sb.Append(" matching: ")
                .AppendJoin(", ", _filters.Select(f => f.Description));
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
}
