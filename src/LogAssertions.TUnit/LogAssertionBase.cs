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
/// Shared base class for <see cref="HasLoggedAssertion"/> and <see cref="HasNotLoggedAssertion"/>.
/// Implements the filter chain (<c>AtLevel</c>, <c>Containing</c>, <c>WithMessage</c>,
/// <c>WithException</c>, <c>WithProperty</c>, <c>WithCategory</c>) and the failure-message
/// snapshot rendering. Derived classes own the count-expectation semantics and the
/// <c>[AssertionExtension]</c> attribute that registers the entry-point name.
/// </summary>
/// <typeparam name="TSelf">The derived assertion type, returned from filter methods to enable fluent chaining.</typeparam>
public abstract class LogAssertionBase<TSelf> : Assertion<FakeLogCollector>
    where TSelf : LogAssertionBase<TSelf>
{
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

    /// <summary>Filters to records whose message contains <paramref name="substring"/> (ordinal).</summary>
    /// <param name="substring">The substring to search for. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="substring"/> is <see langword="null"/>.</exception>
    public TSelf Containing(string substring)
    {
        ArgumentNullException.ThrowIfNull(substring);
        AddPredicate(r => r.Message.Contains(substring, StringComparison.Ordinal), $"Message contains \"{substring}\"");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Containing(\"{substring}\")");
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
    /// Filters to records containing a structured-state entry with the specified
    /// <paramref name="key"/> and <paramref name="value"/>.
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
    /// message, exception) for use in failure messages.
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

        if (snapshot.Count == 0)
        {
            sb.AppendLine("  (no records)");
        }
        else
        {
            foreach (FakeLogRecord record in snapshot)
            {
                sb.Append("  [").Append(record.Level).Append("] ");
                if (!string.IsNullOrEmpty(record.Category))
                    sb.Append(record.Category).Append(": ");
                sb.Append(record.Message);
                if (record.Exception is not null)
                {
                    sb.Append(" | ").Append(record.Exception.GetType().Name)
                        .Append(": ").Append(record.Exception.Message);
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
