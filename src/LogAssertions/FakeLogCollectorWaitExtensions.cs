using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Event-driven waits for log records on a <see cref="FakeLogCollector"/>: wait until background work has logged a
/// matching record, instead of polling the collector or sleeping. Built on
/// <see cref="FakeLogCollector.GetLogsAsync(CancellationToken)"/>, which yields the records already collected and then
/// each new one as it is written, so a wait returns the moment the record arrives.
/// </summary>
/// <remarks>
/// <para>
/// Records already collected count: a wait for a record that was logged before the call returns at once. To wait only
/// for records logged from now on, clear the collector first (<see cref="FakeLogCollector.Clear"/>), or use the count
/// form and wait for one more than are already there.
/// </para>
/// <para>
/// The token is the only bound, so it is required: pass the test's own token, or a linked source with a timeout. When
/// it is canceled, the wait throws <see cref="OperationCanceledException"/> with a message that says how many records
/// arrived and how many matched.
/// </para>
/// <para>
/// A wait proves that a record arrived; it cannot prove that one did not. For "has not logged", let the work finish
/// first (an event, a barrier) and then assert on the collector.
/// </para>
/// </remarks>
public static class FakeLogCollectorWaitExtensions
{
    /// <summary>Waits until <paramref name="collector"/> holds a record that matches <paramref name="filter"/>, and
    /// returns the first such record.</summary>
    /// <param name="collector">The collector to wait on.</param>
    /// <param name="filter">The filter the record must match, for example
    /// <c>LogFilter.All(LogFilter.AtLevel(LogLevel.Warning), LogFilter.Containing("retry", StringComparison.Ordinal))</c>.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>The first matching record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> or <paramref name="filter"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before a matching
    /// record arrived.</exception>
    public static async Task<FakeLogRecord> WaitForLogAsync(
        this FakeLogCollector collector,
        ILogRecordFilter filter,
        CancellationToken cancellationToken)
    {
        var records = await collector.WaitForLogsAsync(filter, 1, cancellationToken).ConfigureAwait(false);
        return records[0];
    }

    /// <summary>Waits until <paramref name="collector"/> holds a record that satisfies <paramref name="predicate"/>,
    /// and returns the first such record.</summary>
    /// <param name="collector">The collector to wait on.</param>
    /// <param name="predicate">The condition the record must satisfy.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>The first matching record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> or <paramref name="predicate"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before a matching
    /// record arrived.</exception>
    public static Task<FakeLogRecord> WaitForLogAsync(
        this FakeLogCollector collector,
        Func<FakeLogRecord, bool> predicate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return collector.WaitForLogAsync(new PredicateFilter(predicate, "the record satisfies the predicate"), cancellationToken);
    }

    /// <summary>Waits until <paramref name="collector"/> holds <paramref name="count"/> records that match
    /// <paramref name="filter"/>, and returns the first <paramref name="count"/> of them in the order they were
    /// logged.</summary>
    /// <param name="collector">The collector to wait on.</param>
    /// <param name="filter">The filter the records must match.</param>
    /// <param name="count">The number of matching records to wait for. Must be positive.</param>
    /// <param name="cancellationToken">The only bound of the wait, typically the test's token.</param>
    /// <returns>The first <paramref name="count"/> matching records.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> or <paramref name="filter"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled before enough
    /// matching records arrived.</exception>
    public static async Task<IReadOnlyList<FakeLogRecord>> WaitForLogsAsync(
        this FakeLogCollector collector,
        ILogRecordFilter filter,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var matches = new List<FakeLogRecord>(count);
        var seen = 0;
        try
        {
            await foreach (var record in collector.GetLogsAsync(cancellationToken).ConfigureAwait(false))
            {
                seen++;
                if (filter.Matches(record))
                {
                    matches.Add(record);
                    if (matches.Count == count)
                    {
                        return matches;
                    }
                }
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waiting for {count} log record(s) where {filter.Description}: {matches.Count} matched among the {seen} record(s) collected before the wait was canceled."),
                exception,
                cancellationToken);
        }

        // GetLogsAsync documents no completion: it waits for new records until it is canceled. Should a later version
        // end the sequence, the wait fails instead of returning fewer records than asked for.
        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"The collector's record stream ended after {seen} record(s), {matches.Count} of {count} matching."));
    }
}
