using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit;

/// <summary>A failure dump armed by <c>DumpToTestOutputOnFailure</c>: the collector, and the record that was its
/// latest when the dump was armed. The dump writes the records captured after that record.</summary>
/// <param name="collector">The collector to dump.</param>
/// <param name="bookmark">The collector's latest record when the dump was armed, or <see langword="null"/> when it
/// held none.</param>
/// <param name="verbosity">How much detail to write per record.</param>
internal sealed class ArmedFailureDump(FakeLogCollector collector, FakeLogRecord? bookmark, DumpVerbosity verbosity)
{
    /// <summary>Gets the collector to dump.</summary>
    internal FakeLogCollector Collector { get; } = collector;

    /// <summary>Gets the collector's latest record when the dump was armed, or <see langword="null"/> when it held
    /// none.</summary>
    internal FakeLogRecord? Bookmark { get; } = bookmark;

    /// <summary>Gets how much detail to write per record.</summary>
    internal DumpVerbosity Verbosity { get; } = verbosity;

    /// <summary>Writes the heading, the records captured since the dump was armed (one timestamp, UTC, per record), and
    /// a footer that repeats the counts. TUnit keeps only the latest part of a very large test output, so a trimmed dump
    /// still ends with its summary. When the bookmark is no longer in the collector (it was cleared), every record is
    /// written.</summary>
    /// <param name="writer">The test's output.</param>
    /// <param name="attempt">The attempt number to name in the heading, or <see langword="null"/> for a test without
    /// retries.</param>
    /// <returns>A task that completes when the dump is written.</returns>
    internal async Task WriteToAsync(TextWriter writer, int? attempt)
    {
        var snapshot = Collector.GetSnapshot();
        var start = FindStart(snapshot, out var cleared);
        var written = snapshot.Count - start;

        StringBuilder sb = new();
        sb.Append("Captured records");
        if (attempt is { } number)
        {
            sb.Append(" of attempt ").Append(number);
        }

        sb.Append(" since the dump was armed (").Append(written).Append(" of ").Append(snapshot.Count)
            .Append(" total, times in UTC");
        if (cleared)
        {
            sb.Append("; the collector was cleared after the dump was armed");
        }

        sb.AppendLine("):");
        LogAssertionRendering.AppendRecords(sb, snapshot, start, Verbosity, withTimestamps: true);
        sb.Append("End of dump");
        if (attempt is { } ended)
        {
            sb.Append(" of attempt ").Append(ended);
        }

        sb.Append(": ").Append(written).Append(" of ").Append(snapshot.Count).AppendLine(" records.");

        // The dump is written after the test, also when the test was canceled, so no token is passed on. .NET 11 added a
        // token overload that the analyzers ask for; the older frameworks have only the plain one.
#if NET11_0_OR_GREATER
        await writer.WriteAsync(sb.ToString(), CancellationToken.None).ConfigureAwait(false);
#else
        await writer.WriteAsync(sb.ToString()).ConfigureAwait(false);
#endif
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
