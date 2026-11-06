using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit;

/// <summary>A failure dump armed by <c>DumpToTestOutputOnFailure</c>: the collector, and the record that was its
/// latest when the dump was armed. The dump writes the records captured after that record.</summary>
/// <param name="Collector">The collector to dump.</param>
/// <param name="Bookmark">The collector's latest record when the dump was armed, or <see langword="null"/> when it
/// held none.</param>
/// <param name="Verbosity">How much detail to write per record.</param>
internal sealed record ArmedFailureDump(FakeLogCollector Collector, FakeLogRecord? Bookmark, DumpVerbosity Verbosity)
{
    /// <summary>Writes the heading and the records captured since the dump was armed, one timestamp (UTC) per record.
    /// When the bookmark is no longer in the collector (it was cleared), every record is written.</summary>
    /// <param name="writer">The test's output.</param>
    /// <param name="attempt">The attempt number to name in the heading, or <see langword="null"/> for a test without
    /// retries.</param>
    /// <returns>A task that completes when the dump is written.</returns>
    internal async Task WriteToAsync(TextWriter writer, int? attempt)
    {
        var snapshot = Collector.GetSnapshot();
        var start = FindStart(snapshot, out var cleared);

        StringBuilder sb = new();
        sb.Append("Captured records");
        if (attempt is { } number)
        {
            sb.Append(" of attempt ").Append(number);
        }

        sb.Append(" since the dump was armed (").Append(snapshot.Count - start).Append(" of ").Append(snapshot.Count)
            .Append(" total, times in UTC");
        if (cleared)
        {
            sb.Append("; the collector was cleared after the dump was armed");
        }

        sb.AppendLine("):");
        LogAssertionRendering.AppendRecords(sb, snapshot, start, Verbosity, withTimestamps: true);
        await writer.WriteAsync(sb.ToString()).ConfigureAwait(false);
    }

    /// <summary>Finds the index of the first record after the bookmark.</summary>
    /// <param name="snapshot">The collector's records.</param>
    /// <param name="cleared"><see langword="true"/> when the bookmark is no longer in the collector.</param>
    /// <returns>The index of the first record to write.</returns>
    private int FindStart(IReadOnlyList<FakeLogRecord> snapshot, out bool cleared)
    {
        cleared = false;
        if (Bookmark is null)
        {
            return 0;
        }

        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(snapshot[i], Bookmark))
            {
                return i + 1;
            }
        }

        cleared = true;
        return 0;
    }
}
