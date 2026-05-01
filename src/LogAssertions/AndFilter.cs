using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Conjunction (logical AND) of zero or more child filters. A record matches when every child
/// filter matches; an empty child list matches every record.
/// </summary>
internal sealed class AndFilter(IReadOnlyList<ILogRecordFilter> children) : ILogRecordFilter
{
    /// <inheritdoc/>
    public bool Matches(FakeLogRecord record) => children.All(c => c.Matches(record));

    /// <inheritdoc/>
    public string Description { get; } = children.Count == 0
        ? "(any)"
        : "(" + string.Join(" AND ", children.Select(c => c.Description)) + ")";
}
