using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Static factory for composable <see cref="ILogRecordFilter"/> instances. The fluent
/// chain on <c>HasLogged()</c>/<c>HasNotLogged()</c>/<c>HasLoggedSequence()</c> creates
/// filters internally; this class exposes the same primitives for users who want to
/// build reusable filter objects, share them across tests, or compose them via
/// <see cref="All(ILogRecordFilter[])"/> / <see cref="Any(ILogRecordFilter[])"/> /
/// <see cref="Not(ILogRecordFilter)"/>.
/// </summary>
/// <example>
/// <code>
/// // Reusable filter shared across many tests:
/// static readonly ILogRecordFilter CriticalDbError = LogFilter.All(
///     LogFilter.AtLevel(LogLevel.Critical),
///     LogFilter.WithException&lt;DbException&gt;());
///
/// // Use via WithFilter on the assertion chain:
/// await Assert.That(collector).HasLogged().WithFilter(CriticalDbError).AtLeast(1);
/// </code>
/// </example>
public static class LogFilter
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>Records at exactly <paramref name="level"/>.</summary>
    /// <param name="level">The log level to match.</param>
    /// <returns>A filter accepting records whose level equals <paramref name="level"/>.</returns>
    public static ILogRecordFilter AtLevel(LogLevel level)
        => new PredicateFilter(r => r.Level == level,
            string.Format(CultureInfo.InvariantCulture, "Level = {0}", level));

    /// <summary>Records whose level is any of <paramref name="levels"/>.</summary>
    /// <param name="levels">The set of log levels to match. Must be non-null.</param>
    /// <returns>A filter accepting records whose level appears in the set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter AtLevel(params LogLevel[] levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var snapshot = (LogLevel[])levels.Clone();
        var description = "Level in [" + string.Join(", ", snapshot) + "]";
        return new PredicateFilter(r => Array.IndexOf(snapshot, r.Level) >= 0, description);
    }

    /// <summary>Records whose level is greater than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The minimum log level (inclusive).</param>
    /// <returns>A filter accepting records at or above the specified level.</returns>
    public static ILogRecordFilter AtLevelOrAbove(LogLevel level)
        => new PredicateFilter(r => r.Level >= level,
            string.Format(CultureInfo.InvariantCulture, "Level >= {0}", level));

    /// <summary>Records whose level is less than or equal to <paramref name="level"/>.</summary>
    /// <param name="level">The maximum log level (inclusive).</param>
    /// <returns>A filter accepting records at or below the specified level.</returns>
    public static ILogRecordFilter AtLevelOrBelow(LogLevel level)
        => new PredicateFilter(r => r.Level <= level,
            string.Format(CultureInfo.InvariantCulture, "Level <= {0}", level));

    /// <summary>Records whose formatted message contains <paramref name="substring"/>.</summary>
    /// <param name="substring">The substring to look for. Must be non-null.</param>
    /// <param name="comparison">The string comparison.</param>
    /// <returns>A filter accepting records whose message contains the substring.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter Containing(string substring, StringComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(substring);
        return new PredicateFilter(
            r => r.Message.Contains(substring, comparison),
            string.Format(CultureInfo.InvariantCulture, "Message contains \"{0}\" ({1})", substring, comparison));
    }

    /// <summary>Records whose formatted message contains every one of <paramref name="substrings"/>.</summary>
    /// <param name="comparison">The string comparison.</param>
    /// <param name="substrings">The substrings; the message must contain all of them.</param>
    /// <returns>A filter accepting records whose message contains every substring.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substrings"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter ContainingAll(StringComparison comparison, params string[] substrings)
    {
        ArgumentNullException.ThrowIfNull(substrings);
        var snapshot = (string[])substrings.Clone();
        var description = "Message contains all [" + string.Join(", ", snapshot.Select(s => "\"" + s + "\"")) + "] (" + comparison + ")";
        return new PredicateFilter(r => snapshot.All(s => r.Message.Contains(s, comparison)), description);
    }

    /// <summary>Records whose formatted message contains any one of <paramref name="substrings"/>.</summary>
    /// <param name="comparison">The string comparison.</param>
    /// <param name="substrings">The substrings; the message must contain at least one.</param>
    /// <returns>A filter accepting records whose message contains at least one substring.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substrings"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter ContainingAny(StringComparison comparison, params string[] substrings)
    {
        ArgumentNullException.ThrowIfNull(substrings);
        var snapshot = (string[])substrings.Clone();
        var description = "Message contains any [" + string.Join(", ", snapshot.Select(s => "\"" + s + "\"")) + "] (" + comparison + ")";
        return new PredicateFilter(r => snapshot.Any(s => r.Message.Contains(s, comparison)), description);
    }

    /// <summary>Records whose formatted message matches <paramref name="pattern"/>.</summary>
    /// <param name="pattern">The regular expression. Must be non-null.</param>
    /// <returns>A filter accepting records whose message matches the regex.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter Matching(Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new PredicateFilter(r => pattern.IsMatch(r.Message),
            "Message matches /" + pattern + "/");
    }

    /// <summary>Records whose formatted message satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The message predicate. Must be non-null.</param>
    /// <returns>A filter accepting records whose message satisfies the predicate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithMessage(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateFilter(r => predicate(r.Message), "Message matches predicate");
    }

    /// <summary>Records whose pre-substitution message template equals <paramref name="template"/>.</summary>
    /// <param name="template">The exact template (the value MEL stores under <c>{OriginalFormat}</c>). Must be non-null.</param>
    /// <returns>A filter accepting records with the given template.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithMessageTemplate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new PredicateFilter(
            r => string.Equals(r.GetStructuredStateValue(OriginalFormatKey), template, StringComparison.Ordinal),
            string.Format(CultureInfo.InvariantCulture, "Template = \"{0}\"", template));
    }

    /// <summary>Records whose <see cref="FakeLogRecord.Exception"/> is assignable to <typeparamref name="TException"/>.</summary>
    /// <typeparam name="TException">The exception type.</typeparam>
    /// <returns>A filter accepting records whose exception is the specified type or a subclass.</returns>
    public static ILogRecordFilter WithException<TException>() where TException : Exception
        => new PredicateFilter(r => r.Exception is TException,
            "Exception is " + typeof(TException).Name);

    /// <summary>Records whose <see cref="FakeLogRecord.Exception"/> is non-null (any type).</summary>
    /// <returns>A filter accepting records that carry any exception.</returns>
    public static ILogRecordFilter WithException()
        => new PredicateFilter(r => r.Exception is not null, "Exception is non-null");

    /// <summary>Records whose <see cref="FakeLogRecord.Exception"/> satisfies <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The predicate. Receives the non-null exception or returns <see langword="false"/> when the record has no exception.</param>
    /// <returns>A filter accepting records whose exception satisfies the predicate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithException(Func<Exception, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateFilter(r => r.Exception is not null && predicate(r.Exception),
            "Exception matches predicate");
    }

    /// <summary>Records whose exception's message contains <paramref name="substring"/> (ordinal).</summary>
    /// <param name="substring">The substring to find in the exception's message. Must be non-null.</param>
    /// <returns>A filter accepting records whose exception message contains the substring.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithExceptionMessage(string substring)
    {
        ArgumentNullException.ThrowIfNull(substring);
        return new PredicateFilter(
            r => r.Exception?.Message.Contains(substring, StringComparison.Ordinal) ?? false,
            "Exception message contains \"" + substring + "\"");
    }

    /// <summary>Records containing a structured-state entry with the specified key and value.</summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="value">The expected formatted-string value (ordinal); may be <see langword="null"/>.</param>
    /// <returns>A filter accepting records whose structured-state entry matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithProperty(string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new PredicateFilter(
            r => string.Equals(r.GetStructuredStateValue(key), value, StringComparison.Ordinal),
            key + " = \"" + value + "\"");
    }

    /// <summary>Records whose structured-state value at <paramref name="key"/> satisfies <paramref name="predicate"/>.</summary>
    /// <param name="key">The structured-state key. Must be non-null.</param>
    /// <param name="predicate">A predicate over the formatted value (string-typed; FakeLogRecord stores values as strings).</param>
    /// <returns>A filter accepting records whose structured-state value satisfies the predicate.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithProperty(string key, Func<string?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateFilter(
            r => predicate(r.GetStructuredStateValue(key)),
            key + " matches predicate");
    }

    /// <summary>Records whose category equals <paramref name="category"/> (ordinal).</summary>
    /// <param name="category">The full category name. Must be non-null.</param>
    /// <returns>A filter accepting records emitted by the specified category.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="category"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return new PredicateFilter(
            r => string.Equals(r.Category, category, StringComparison.Ordinal),
            "Category = \"" + category + "\"");
    }

    /// <summary>Records whose <see cref="EventId.Id"/> equals <paramref name="eventId"/>.</summary>
    /// <param name="eventId">The numeric event ID.</param>
    /// <returns>A filter accepting records with the matching event ID.</returns>
    public static ILogRecordFilter WithEventId(int eventId)
        => new PredicateFilter(r => r.Id.Id == eventId,
            string.Format(CultureInfo.InvariantCulture, "EventId = {0}", eventId));

    /// <summary>Records whose <see cref="EventId.Id"/> is within the inclusive range <paramref name="min"/>..<paramref name="max"/>.</summary>
    /// <param name="min">The minimum event ID (inclusive).</param>
    /// <param name="max">The maximum event ID (inclusive). Must be greater than or equal to <paramref name="min"/>.</param>
    /// <returns>A filter accepting records with event IDs in the range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public static ILogRecordFilter WithEventIdInRange(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        return new PredicateFilter(r => r.Id.Id >= min && r.Id.Id <= max,
            string.Format(CultureInfo.InvariantCulture, "EventId in [{0}, {1}]", min, max));
    }

    /// <summary>Records whose <see cref="EventId.Name"/> equals <paramref name="eventName"/> (ordinal).</summary>
    /// <param name="eventName">The event name. Must be non-null.</param>
    /// <returns>A filter accepting records with the matching event name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eventName"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithEventName(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        return new PredicateFilter(
            r => string.Equals(r.Id.Name, eventName, StringComparison.Ordinal),
            "EventName = \"" + eventName + "\"");
    }

    /// <summary>Records emitted while a scope of type <typeparamref name="TScope"/> was active.</summary>
    /// <typeparam name="TScope">The scope state type to match.</typeparam>
    /// <returns>A filter accepting records with an active scope of the specified type.</returns>
    public static ILogRecordFilter WithScope<TScope>()
        => new PredicateFilter(r => r.Scopes.OfType<TScope>().Any(),
            "Scope of type " + typeof(TScope).Name + " active");

    /// <summary>Records whose active scopes contain a property with the specified key and value.</summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="value">The expected scope-property value; compared via <see cref="object.Equals(object?, object?)"/>.</param>
    /// <returns>A filter accepting records with a matching scope property.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithScopeProperty(string key, object? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new PredicateFilter(
            r => ScopePropertyMatches(r, key, v => Equals(v, value)),
            "Scope " + key + " = " + (value ?? "null"));
    }

    /// <summary>Records whose active scopes contain a property whose value satisfies <paramref name="predicate"/>.</summary>
    /// <param name="key">The scope-property key. Must be non-null.</param>
    /// <param name="predicate">A predicate over the scope-property value.</param>
    /// <returns>A filter accepting records whose scope-property value satisfies the predicate.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ILogRecordFilter WithScopeProperty(string key, Func<object?, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateFilter(
            r => ScopePropertyMatches(r, key, predicate),
            "Scope " + key + " matches predicate");
    }

    /// <summary>An arbitrary predicate over the full record.</summary>
    /// <param name="predicate">The predicate. Must be non-null.</param>
    /// <returns>A filter accepting records satisfying the predicate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter Where(Func<FakeLogRecord, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new PredicateFilter(predicate, "Custom predicate");
    }

    /// <summary>Conjunction: records matching every one of <paramref name="filters"/>.</summary>
    /// <param name="filters">The filters to AND together. May be empty (matches every record).</param>
    /// <returns>A filter accepting records that match every child filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter All(params ILogRecordFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return new AndFilter((ILogRecordFilter[])filters.Clone());
    }

    /// <summary>Disjunction: records matching any one of <paramref name="filters"/>.</summary>
    /// <param name="filters">The filters to OR together. May be empty (matches no record).</param>
    /// <returns>A filter accepting records that match at least one child filter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filters"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter Any(params ILogRecordFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return new OrFilter((ILogRecordFilter[])filters.Clone());
    }

    /// <summary>Logical negation: records that do not match <paramref name="filter"/>.</summary>
    /// <param name="filter">The filter to negate. Must be non-null.</param>
    /// <returns>A filter accepting records the inner filter rejects.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    public static ILogRecordFilter Not(ILogRecordFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return new NotFilter(filter);
    }

    /// <summary>
    /// Walks every active scope on <paramref name="record"/>, looking for a key-value pair whose
    /// key equals <paramref name="key"/> (ordinal) and whose value satisfies
    /// <paramref name="predicate"/>. Recognises both <c>object</c> and <c>object?</c> value-type
    /// variants of the <see cref="IEnumerable{T}"/>-of-<see cref="KeyValuePair{TKey, TValue}"/>
    /// shape used by dictionary scopes and <see cref="LoggerMessage.DefineScope{T1}(string)"/>.
    /// </summary>
    /// <param name="record">The record to inspect.</param>
    /// <param name="key">The scope-property key.</param>
    /// <param name="predicate">The predicate applied to the matched value.</param>
    /// <returns><see langword="true"/> when at least one active scope matched.</returns>
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
