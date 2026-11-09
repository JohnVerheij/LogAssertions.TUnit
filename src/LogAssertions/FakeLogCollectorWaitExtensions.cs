using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// it is canceled, the collector decides: if it holds enough matching records (also ones captured just before the
/// cancellation, or before the call with a token that was already canceled), the wait returns them; otherwise it throws
/// <see cref="OperationCanceledException"/> with a message that says how many records the collector holds and how many
/// of them match.
/// </para>
/// <para>
/// A wait proves that a record arrived; it cannot prove that one did not. For "has not logged", let the work finish
/// first (an event, a barrier) and then assert on the collector.
/// </para>
/// </remarks>
public static class FakeLogCollectorWaitExtensions
{
    /// <summary>The largest initial capacity of the list of matches. A larger count grows the list as records
    /// match, so an unrealistic count fails on the token, not on the allocation.</summary>
    private const int MaxInitialCapacity = 16;

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
    /// <exception cref="InvalidOperationException">The collector's record stream ended before a matching record
    /// arrived (see <see cref="WaitForLogsAsync"/>).</exception>
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
    /// <exception cref="InvalidOperationException">The collector's record stream ended before a matching record
    /// arrived (see <see cref="WaitForLogsAsync"/>).</exception>
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
    /// <exception cref="InvalidOperationException">The collector's record stream ended before enough matching records
    /// arrived. <c>FakeLogCollector.GetLogsAsync</c> does not end today; the wait fails rather than return fewer
    /// records should a later version end it.</exception>
    public static async Task<IReadOnlyList<FakeLogRecord>> WaitForLogsAsync(
        this FakeLogCollector collector,
        ILogRecordFilter filter,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        try
        {
            return await ReadMatchesAsync(collector.GetLogsAsync(cancellationToken), filter, count, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // The stream checks its token before it reads the records it holds, so records captured before the
            // cancellation can be unread here: the collector, not the stream, decides.
            var collected = collector.GetSnapshot();
            var held = FirstMatches(collected, filter, count);
            if (held.Count == count)
            {
                return held;
            }

            throw new OperationCanceledException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Waiting for {count} log record(s) where {filter.Description}: {held.Count} matched among the {collected.Count} record(s) collected before the wait was canceled."),
                exception,
                cancellationToken);
        }
    }

    /// <summary>Reads <paramref name="records"/> until <paramref name="count"/> of them match <paramref name="filter"/>,
    /// and returns those in the order they came.</summary>
    /// <param name="records">The record stream: the collector's yields the records already collected and then waits for
    /// each new one until <paramref name="cancellationToken"/> is canceled.</param>
    /// <param name="filter">The filter the records must match.</param>
    /// <param name="count">The number of matching records to read, positive.</param>
    /// <param name="cancellationToken">Ends the reading.</param>
    /// <returns>The first <paramref name="count"/> matching records.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled first.</exception>
    /// <exception cref="InvalidOperationException">The stream ended first.</exception>
    internal static async Task<IReadOnlyList<FakeLogRecord>> ReadMatchesAsync(
        IAsyncEnumerable<FakeLogRecord> records,
        ILogRecordFilter filter,
        int count,
        CancellationToken cancellationToken)
    {
        var matches = new List<FakeLogRecord>(Math.Min(count, MaxInitialCapacity));
        var seen = 0;
        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
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

        // GetLogsAsync documents no completion: it waits for new records until it is canceled. Should a later version
        // end the sequence, the wait fails instead of returning fewer records than asked for.
        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"The collector's record stream ended after {seen} record(s), {matches.Count} of {count} matching."));
    }

    /// <summary>Returns the first <paramref name="count"/> records of <paramref name="records"/> that match
    /// <paramref name="filter"/>, or all the matching ones when there are fewer.</summary>
    /// <param name="records">The records, in the order they were logged.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="count">The most to return.</param>
    /// <returns>The matching records.</returns>
    private static List<FakeLogRecord> FirstMatches(IReadOnlyList<FakeLogRecord> records, ILogRecordFilter filter, int count)
    {
        var matches = new List<FakeLogRecord>(Math.Min(count, MaxInitialCapacity));
        foreach (var record in records.Where(filter.Matches))
        {
            matches.Add(record);
            if (matches.Count == count)
            {
                break;
            }
        }

        return matches;
    }
}
