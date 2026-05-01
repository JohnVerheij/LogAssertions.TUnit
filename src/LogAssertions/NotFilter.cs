using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Logical negation of an inner filter. A record matches when the inner filter does not.
/// </summary>
internal sealed class NotFilter(ILogRecordFilter inner) : ILogRecordFilter
{
    /// <inheritdoc/>
    public bool Matches(FakeLogRecord record) => !inner.Matches(record);

    /// <inheritdoc/>
    public string Description { get; } = "NOT " + inner.Description;
}
