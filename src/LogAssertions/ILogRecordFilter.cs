using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// A composable filter over <see cref="FakeLogRecord"/>. Built-in filter methods on the
/// assertion fluent chain (<c>AtLevel</c>, <c>Containing</c>, etc.) each create one of these
/// internally; <c>WithFilter(ILogRecordFilter)</c> on the chain accepts arbitrary user
/// implementations, and the <see cref="LogFilter"/> static factory composes them via
/// <c>All</c>, <c>Any</c>, and <c>Not</c>.
/// </summary>
/// <remarks>
/// Implementations should be inexpensive to evaluate — they are invoked once per captured
/// record per assertion. The <see cref="Description"/> is rendered into the expectation line
/// of failure messages and should be terse and human-readable (e.g. <c>"Level = Warning"</c>,
/// not a full sentence).
/// </remarks>
public interface ILogRecordFilter
{
    /// <summary>Returns <see langword="true"/> when <paramref name="record"/> satisfies this filter.</summary>
    /// <param name="record">The record to test.</param>
    /// <returns><see langword="true"/> when the record matches; otherwise <see langword="false"/>.</returns>
    bool Matches(FakeLogRecord record);

    /// <summary>
    /// A short human-readable description used in the expectation summary on failure
    /// (for example <c>"Level = Warning"</c> or <c>"Message contains \"timeout\""</c>).
    /// </summary>
    string Description { get; }
}
