using System;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit;

/// <summary>
/// Closed-over wrapper that adapts a delegate plus a description into an
/// <see cref="ILogRecordFilter"/>. Used by <see cref="LogFilter"/> factory methods and by
/// <c>Where(Func&lt;FakeLogRecord, bool&gt;)</c> to avoid a separate concrete class per
/// filter shape.
/// </summary>
/// <param name="predicate">The predicate to evaluate against each record.</param>
/// <param name="description">The text rendered into failure-message expectation lines.</param>
internal sealed class PredicateFilter(Func<FakeLogRecord, bool> predicate, string description) : ILogRecordFilter
{
    /// <inheritdoc/>
    public bool Matches(FakeLogRecord record) => predicate(record);

    /// <inheritdoc/>
    public string Description { get; } = description;
}
