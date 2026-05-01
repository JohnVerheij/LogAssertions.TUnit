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

    /// <summary>Filters to records at the specified <paramref name="level"/>.</summary>
    /// <param name="level">The exact log level to match.</param>
    /// <returns>This assertion for chaining.</returns>
    public TSelf AtLevel(LogLevel level)
    {
        _predicates.Add(r => r.Level == level);
        _filterDescriptions.Add(string.Format(CultureInfo.InvariantCulture, "Level = {0}", level));
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
        _predicates.Add(r => r.Message.Contains(substring, StringComparison.Ordinal));
        _filterDescriptions.Add($"Message contains \"{substring}\"");
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
        _predicates.Add(r => predicate(r.Message));
        _filterDescriptions.Add("Message matches predicate");
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
        _predicates.Add(r => r.Exception is TException);
        _filterDescriptions.Add($"Exception is {typeof(TException).Name}");
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
        _predicates.Add(r =>
            string.Equals(r.GetStructuredStateValue(key), value, StringComparison.Ordinal));
        _filterDescriptions.Add($"{key} = \"{value}\"");
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
        _predicates.Add(r => string.Equals(r.Category, category, StringComparison.Ordinal));
        _filterDescriptions.Add($"Category = \"{category}\"");
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".WithCategory(\"{category}\")");
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
