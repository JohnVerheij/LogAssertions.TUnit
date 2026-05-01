using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Disjunction (logical OR) of zero or more child filters. A record matches when at least one
/// child filter matches; an empty child list matches no record.
/// </summary>
internal sealed class OrFilter(IReadOnlyList<ILogRecordFilter> children) : ILogRecordFilter
{
    /// <inheritdoc/>
    public bool Matches(FakeLogRecord record) => children.Any(c => c.Matches(record));

    /// <inheritdoc/>
    public string Description { get; } = children.Count == 0
        ? "(none)"
        : "(" + string.Join(" OR ", children.Select(c => c.Description)) + ")";
}
