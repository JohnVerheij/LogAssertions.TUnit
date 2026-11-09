using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit assertion that waits until a <see cref="FakeLogCollector"/> holds matching log records: the event-driven
/// form of <c>HasLogged()</c> for records that background work logs after the call that started it returns. Entry
/// point: <c>Assert.That(collector).WillLog(timeout, cancellationToken)</c>, then the same filters as
/// <c>HasLogged()</c> and a count terminator.
/// </summary>
/// <example>
/// <code>
/// await Assert.That(collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken)
///     .AtLevel(LogLevel.Warning).Containing("would collide", StringComparison.Ordinal).Once();
/// </code>
/// </example>
/// <remarks>
/// <para>
/// It waits on the collector's stream of records, the one
/// <see cref="FakeLogCollectorWaitExtensions.WaitForLogAsync(FakeLogCollector, ILogRecordFilter, CancellationToken)"/>
/// reads. The count is read from the collector right after the minimum number of matching records has been captured.
/// Records captured by then count, including ones logged before the assertion ran. A record that arrives after the
/// assertion has passed isn't checked: to pin "no more than one", assert <c>HasLogged()...Once()</c> after the work
/// has settled. The assertion fails when that count is above the maximum. When the <c>timeout</c> or the token ends
/// the wait, the records the collector holds at that moment decide: enough matching records pass (also a record
/// captured just before the timeout fired, or records already captured when the token was canceled before the
/// assertion ran); otherwise it fails with the expectation, the count, and the captured records, the same diagnostics
/// as <c>HasLogged()</c>, not a bare cancellation. A chain whose levels all sit below the collector's capture floor
/// fails at once, since such a record can never be captured.
/// </para>
/// <para>
/// Clock: the <c>timeout</c> runs in real time (<see cref="TimeProvider.System"/>), whatever clock the collector uses,
/// and starts when the assertion runs (at <see langword="await"/>), not when the chain is built. It bounds how long the
/// test waits for real background work. When that work waits on a fake clock, start the assertion first, advance the
/// clock, then await the assertion: put the chain in a local function that awaits it (TUnit's analyzer requires every
/// <c>Assert.That</c> to be awaited), call it, advance, and await the task it returned. That
/// <see langword="await"/> is what surfaces the failure message: a helper that advances until the task completes
/// surfaces it only if the helper awaits the task too.
/// </para>
/// <para>
/// A wait proves that records arrived, not that no more follow, so there is no <c>Never()</c> or <c>AtMost(n)</c>: to
/// pin "no more than one", let the work finish and assert <c>HasLogged()...Once()</c>; for absence, use
/// <c>HasNotLogged()</c> after the work has finished. The wait is built on
/// <see cref="FakeLogCollectorWaitExtensions.WaitForLogsAsync"/>: no polling and no real-time delay; the timeout is a
/// bound, not a poll interval.
/// </para>
/// </remarks>
[AssertionExtension("WillLog")]
public sealed class WillLogAssertion : LogAssertionBase<WillLogAssertion>
{
    /// <summary>The longest timeout a <see cref="CancellationTokenSource"/> accepts.</summary>
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly TimeSpan _timeout;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationToken _cancellationToken;
    private readonly LogCountExpectation _count = new();
    private IReadOnlyList<FakeLogRecord>? _capturedMatches;

    /// <summary>Initializes a waiting log assertion bounded by <paramref name="timeout"/> (real time) and
    /// <paramref name="cancellationToken"/>. Called by the TUnit source generator.</summary>
    /// <remarks>
    /// <para>
    /// The count is read from the collector right after the minimum number of matching records has been captured.
    /// Records captured by then count, including ones logged before the assertion ran. A record that arrives after the
    /// assertion has passed isn't checked: to pin "no more than one", assert <c>HasLogged()...Once()</c> after the work
    /// has settled.
    /// </para>
    /// <para>
    /// Clock: <paramref name="timeout"/> runs on <see cref="TimeProvider.System"/>, from the moment the assertion runs.
    /// </para>
    /// </remarks>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="timeout">The longest the wait may take, in real time, counted from when the assertion runs. Must be
    /// positive and at most about 49 days. Choose it below the test's own timeout, so the wait fails with its
    /// diagnostics first.</param>
    /// <param name="cancellationToken">Ends the wait early, for example the test's token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is zero, negative (including
    /// <see cref="Timeout.InfiniteTimeSpan"/>), or longer than a <see cref="CancellationTokenSource"/> accepts.</exception>
    public WillLogAssertion(AssertionContext<FakeLogCollector> context, TimeSpan timeout, CancellationToken cancellationToken = default)
        : this(context, timeout, TimeProvider.System, cancellationToken)
    {
    }

    /// <summary>Initializes a waiting log assertion whose timeout runs on <paramref name="timeProvider"/>.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="timeout">The longest the wait may take on <paramref name="timeProvider"/>'s clock.</param>
    /// <param name="timeProvider">The clock of the timeout.</param>
    /// <param name="cancellationToken">Ends the wait early.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is out of range.</exception>
    internal WillLogAssertion(
        AssertionContext<FakeLogCollector> context, TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
        : base(context)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxTimeout);
        _timeout = timeout;
        _timeProvider = timeProvider;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Waits for exactly one matching record: passes when the first arrives, and fails at once if the wait
    /// first sees two.</summary>
    /// <returns>This assertion for chaining.</returns>
    public WillLogAssertion Once()
    {
        _count.SetOnce();
        Context.ExpressionBuilder.Append(".Once()");
        return this;
    }

    /// <summary>Waits for exactly <paramref name="count"/> matching records: passes when the count reaches it, and
    /// fails at once if the wait first sees more.</summary>
    /// <param name="count">The count. Must be positive.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    public WillLogAssertion Exactly(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _count.SetExactly(count);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Exactly({count})");
        return this;
    }

    /// <summary>Waits for at least <paramref name="count"/> matching records. Without a count terminator the chain
    /// waits for at least one.</summary>
    /// <param name="count">The minimum. Must be positive.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    public WillLogAssertion AtLeast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        _count.SetAtLeast(count);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLeast({count})");
        return this;
    }

    /// <summary>Waits until the count of matching records is in the inclusive range [<paramref name="min"/>,
    /// <paramref name="max"/>], and fails at once if the wait first sees more than <paramref name="max"/>.</summary>
    /// <param name="min">The minimum (inclusive). Must be positive.</param>
    /// <param name="max">The maximum (inclusive). Must be at least <paramref name="min"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is not positive, or <paramref name="max"/>
    /// is less than <paramref name="min"/>.</exception>
    public WillLogAssertion Between(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        _count.SetBetween(min, max);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Between({min}, {max})");
        return this;
    }

    /// <summary>Runs the assertion and returns the matched records once the wait passes, as
    /// <c>HasLogged().GetMatches()</c> does.</summary>
    /// <returns>The records that matched when the wait passed, in the order they were logged; a snapshot.</returns>
    /// <exception cref="InvalidOperationException">The assertion failed inside <c>Assert.Multiple()</c>, which records
    /// the failure instead of throwing it, so there are no matches to return; the message names the failure, and the
    /// scope still reports it.</exception>
    public Task<IReadOnlyList<FakeLogRecord>> GetMatches()
    {
        Context.ExpressionBuilder.Append(".GetMatches()");
        return ExecuteAndReturnMatchesAsync();
    }

    /// <summary>Runs the assertion and returns the single matched record once the wait passes, as
    /// <c>HasLogged().GetMatch()</c> does. Requires the count to be exactly one (<see cref="Once"/>, <c>Exactly(1)</c>,
    /// or <c>Between(1, 1)</c>).</summary>
    /// <returns>The single matched record.</returns>
    /// <exception cref="InvalidOperationException">The chain's count expectation is not exactly one; or the assertion
    /// failed inside <c>Assert.Multiple()</c>, which records the failure instead of throwing it, so there is no match to
    /// return (the message names the failure, and the scope still reports it).</exception>
    public Task<FakeLogRecord> GetMatch()
    {
        if (!_count.IsExactlyOne)
        {
            throw new InvalidOperationException(
                "GetMatch() requires the chain to constrain the match count to exactly one: " +
                "use .Once() or .Exactly(1) before .GetMatch(). Use .GetMatches() to retrieve any number of matches.");
        }

        Context.ExpressionBuilder.Append(".GetMatch()");
        return ExecuteAndReturnSingleMatchAsync();
    }

    private async Task<IReadOnlyList<FakeLogRecord>> ExecuteAndReturnMatchesAsync()
    {
        await AssertAsync();
        return _capturedMatches ?? throw NoMatchAfterRecordedFailure("GetMatches()");
    }

    private async Task<FakeLogRecord> ExecuteAndReturnSingleMatchAsync()
    {
        await AssertAsync();
        return _capturedMatches is { } matches ? matches[0] : throw NoMatchAfterRecordedFailure("GetMatch()");
    }

    /// <inheritdoc/>
    protected override async Task<AssertionResult> CheckAsync(EvaluationMetadata<FakeLogCollector> metadata)
    {
        // The same instance can be evaluated again (each GetMatch() or await runs the check), so an earlier check's
        // matches must never answer for this one.
        _capturedMatches = null;
        return RememberFailure(await CheckSourceAndWaitAsync(metadata).ConfigureAwait(false));
    }

    /// <summary>Checks the source and the capture floor, then waits for the matching records.</summary>
    /// <param name="metadata">The evaluation of the assertion source.</param>
    /// <returns>The outcome.</returns>
    private async Task<AssertionResult> CheckSourceAndWaitAsync(EvaluationMetadata<FakeLogCollector> metadata)
    {
        if (metadata.Exception is not null)
        {
            return AssertionResult.Failed($"threw {metadata.Exception.GetType().Name}", metadata.Exception);
        }

        var collector = metadata.Value;
        if (collector is null)
            return AssertionResult.Failed("collector was null");

        if (TryDescribeUncapturedFloor(collector, out var floorMessage))
            return AssertionResult.Failed(floorMessage);

        using var timeout = new CancellationTokenSource(_timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _cancellationToken);
        try
        {
            return await WaitAsync(collector, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            var headline = timeout.IsCancellationRequested
                ? $"the wait timed out after {_timeout:c}"
                : "the wait was canceled";
            var snapshot = collector.GetSnapshot();
            return AssertionResult.Failed(BuildFailureMessage($"{headline} with {GetMatches(snapshot).Count} record(s) matched", snapshot));
        }
    }

    /// <summary>Waits until the minimum count of matching records has arrived, then decides on the collector's
    /// records; waits again when they are fewer (the collector was cleared meanwhile).</summary>
    /// <param name="collector">The collector under assertion.</param>
    /// <param name="cancellationToken">The bound of the wait.</param>
    /// <returns>The outcome.</returns>
    private async Task<AssertionResult> WaitAsync(FakeLogCollector collector, CancellationToken cancellationToken)
    {
        var filter = CombinedFilter();
        while (true)
        {
            _ = await collector.WaitForLogsAsync(filter, _count.Min, cancellationToken).ConfigureAwait(false);

            var snapshot = collector.GetSnapshot();
            var matches = GetMatches(snapshot);
            if (matches.Count > _count.Max)
            {
                return AssertionResult.Failed(BuildFailureMessage(
                    string.Create(CultureInfo.InvariantCulture, $"{matches.Count} record(s) matched, more than the maximum of {_count.Max}"),
                    snapshot));
            }

            if (matches.Count >= _count.Min)
            {
                _capturedMatches = matches;
                return AssertionResult.Passed;
            }
        }
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        StringBuilder sb = new();
        sb.Append(_count.Description)
            .Append(CultureInfo.InvariantCulture, $" log record(s) to be logged within {_timeout:c}");
        AppendFilterSummary(sb);
        return sb.ToString();
    }
}
