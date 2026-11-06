using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Reads a record's structured state without throwing. <see cref="FakeLogRecord.StructuredState"/> and
/// <see cref="FakeLogRecord.GetStructuredStateValue"/> throw <see cref="InvalidCastException"/> when the logged state
/// is not a key/value list (a custom <c>TState</c> passed to <c>ILogger.Log</c>); such a record has no properties and no
/// template here.
/// </summary>
internal static class RecordState
{
    /// <summary>Returns the record's key/value pairs, or <see langword="null"/> when its state is not a key/value list.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The pairs (formatted strings), or <see langword="null"/>.</returns>
    internal static IReadOnlyList<KeyValuePair<string, string?>>? Pairs(FakeLogRecord record)
        => record.State as IReadOnlyList<KeyValuePair<string, string?>>;

    /// <summary>Returns the value of the first pair whose key equals <paramref name="key"/> (ordinal), or
    /// <see langword="null"/> when there is none or the state is not a key/value list.</summary>
    /// <param name="record">The record.</param>
    /// <param name="key">The key.</param>
    /// <returns>The formatted value, or <see langword="null"/>.</returns>
    internal static string? Value(FakeLogRecord record, string key)
    {
        if (Pairs(record) is not { } pairs)
            return null;

        for (var i = 0; i < pairs.Count; i++)
        {
            if (string.Equals(pairs[i].Key, key, StringComparison.Ordinal))
                return pairs[i].Value;
        }

        return null;
    }
}
