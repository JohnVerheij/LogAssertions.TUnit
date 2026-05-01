using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Non-asserting inspection helpers on <see cref="FakeLogCollector"/>. Use these when a test
/// needs to read the captured records as data — for downstream calculations, debugging output,
/// or cross-checking — rather than to assert. None of these methods throw on mismatch.
/// </summary>
public static class FakeLogCollectorInspectionExtensions
{
    /// <summary>
    /// Returns the records that satisfy every supplied filter, in original order. Useful when
    /// the test needs the matched records for further inspection rather than just an
    /// assertion. The returned list is a defensive copy not bound to the live collector.
    /// </summary>
    /// <param name="collector">The collector to query.</param>
    /// <param name="filters">The filters; AND-combined. Empty filter list returns every record.</param>
    /// <returns>The matched records.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<FakeLogRecord> Filter(this FakeLogCollector collector, params ILogRecordFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(filters);

        var snapshot = collector.GetSnapshot();
        if (filters.Length == 0)
            return [.. snapshot];

        return [.. snapshot.Where(r => filters.All(f => f.Matches(r)))];
    }

    /// <summary>
    /// Counts the records that satisfy every supplied filter. Convenience over
    /// <c>collector.Filter(...).Count</c> that avoids materialising the intermediate list.
    /// </summary>
    /// <param name="collector">The collector to query.</param>
    /// <param name="filters">The filters; AND-combined. Empty filter list counts every record.</param>
    /// <returns>The number of matching records.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static int CountMatching(this FakeLogCollector collector, params ILogRecordFilter[] filters)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(filters);

        var snapshot = collector.GetSnapshot();
        if (filters.Length == 0)
            return snapshot.Count;

        return snapshot.Count(r => filters.All(f => f.Matches(r)));
    }

    /// <summary>
    /// Renders every captured record to <paramref name="writer"/> using the same formatter
    /// the failure-message snapshot uses (4-character level abbreviation, props line, scopes
    /// line, exception line). Useful during test development to see what was actually logged
    /// before writing the assertion.
    /// </summary>
    /// <param name="collector">The collector to dump.</param>
    /// <param name="writer">The text destination. Must be non-null.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static void DumpTo(this FakeLogCollector collector, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(writer);

        var snapshot = collector.GetSnapshot();
        StringBuilder sb = new();
        sb.Append("Captured records (").Append(snapshot.Count).AppendLine(" total):");
        LogAssertionRendering.AppendCapturedRecords(sb, snapshot);
        writer.Write(sb.ToString());
    }
}
