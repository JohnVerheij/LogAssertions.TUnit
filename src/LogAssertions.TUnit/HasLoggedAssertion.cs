using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit assertion that verifies a <see cref="FakeLogCollector"/> contains matching log records.
/// Inherits filter chaining from <see cref="LogAssertionBase{TSelf}"/>; adds count-expectation
/// terminators (<c>Once</c>, <c>Exactly</c>, <c>AtLeast</c>, <c>AtMost</c>, <c>Between</c>, <c>Never</c>), the
/// spacing constraint <c>AtMostOncePer</c>, and the value-returning terminators <c>GetMatch</c> and
/// <c>GetMatches</c> for handing the matched records to follow-up assertions.
/// </summary>
/// <remarks>
/// A chain whose count admits zero (<c>Never()</c>, <c>AtMost(n)</c>, <c>Exactly(0)</c>, <c>Between(0, n)</c>) is an
/// absence check: when every level it could match sits below the collector's capture floor, it fails as vacuous, as
/// <c>HasNotLogged()</c> does. A chain that needs a match names the floor in its failure for the same reason.
/// </remarks>
[AssertionExtension("HasLogged")]
public sealed class HasLoggedAssertion : LogAssertionBase<HasLoggedAssertion>
{
    private readonly LogCountExpectation _count = new();
    private TimeSpan? _minimumGap;
    private IReadOnlyList<FakeLogRecord>? _capturedMatches;

    /// <summary>Initializes a positive log assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    public HasLoggedAssertion(AssertionContext<FakeLogCollector> context) : base(context) { }

    /// <summary>Expects exactly one matching record.</summary>
    /// <returns>This assertion for chaining.</returns>
    public HasLoggedAssertion Once()
    {
        _count.SetOnce();
        Context.ExpressionBuilder.Append(".Once()");
        return this;
    }

    /// <summary>Expects exactly <paramref name="count"/> matching records.</summary>
    /// <param name="count">The required match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public HasLoggedAssertion Exactly(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count.SetExactly(count);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Exactly({count})");
        return this;
    }

    /// <summary>Expects at least <paramref name="count"/> matching records.</summary>
    /// <param name="count">The minimum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public HasLoggedAssertion AtLeast(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count.SetAtLeast(count);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtLeast({count})");
        return this;
    }

    /// <summary>Expects at most <paramref name="count"/> matching records.</summary>
    /// <param name="count">The maximum match count. Must be non-negative.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public HasLoggedAssertion AtMost(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _count.SetAtMost(count);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtMost({count})");
        return this;
    }

    /// <summary>Expects zero matching records. Like <c>HasNotLogged()</c>, it fails as vacuous when every level the
    /// chain could match sits below the collector's capture floor.</summary>
    /// <returns>This assertion for chaining.</returns>
    public HasLoggedAssertion Never()
    {
        _count.SetNever();
        Context.ExpressionBuilder.Append(".Never()");
        return this;
    }

    /// <summary>Expects the matching record count to fall in the inclusive range [<paramref name="min"/>, <paramref name="max"/>].</summary>
    /// <param name="min">The minimum match count (inclusive). Must be non-negative.</param>
    /// <param name="max">The maximum match count (inclusive). Must be greater than or equal to <paramref name="min"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative, or <paramref name="max"/> is less than <paramref name="min"/>.</exception>
    public HasLoggedAssertion Between(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        _count.SetBetween(min, max);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Between({min}, {max})");
        return this;
    }

    /// <summary>
    /// Expects every two consecutive matching records, ordered by <see cref="FakeLogRecord.Timestamp"/>, to be at least
    /// <paramref name="interval"/> apart: a rate-limited record is logged at most once per interval. Two records
    /// exactly <paramref name="interval"/> apart pass; one tick less fails. It constrains the spacing only; the count
    /// expectation still applies, so the chain keeps needing at least one match unless a count terminator that admits
    /// zero is set (<c>.AtMost(n)</c>, <c>.Between(0, n)</c>).
    /// </summary>
    /// <remarks>
    /// Clock: the records' <see cref="FakeLogRecord.Timestamp"/>, that is, the collector's clock
    /// (<c>FakeLogCollectorOptions.TimeProvider</c>). <c>LogCollectorBuilder.Create(timeProvider)</c> and
    /// <c>TestOutputLogCollectorBuilder.CreateTeed(timeProvider)</c> set it; without one it is the system clock.
    /// </remarks>
    /// <param name="interval">The shortest allowed gap. Must be positive.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> is zero or negative.</exception>
    public HasLoggedAssertion AtMostOncePer(TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        _minimumGap = interval;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtMostOncePer({interval})");
        return this;
    }

    /// <summary>
    /// Returns the matched records once the assertion passes. Awaits the chain (which evaluates
    /// the count expectation and throws on mismatch); on success, returns the snapshot of matches
    /// captured at evaluation time. Useful for handing the matched records to follow-up
    /// assertions without a duplicate <c>collector.Filter(...)</c> call.
    /// </summary>
    /// <returns>The matched records in original order; the list is a snapshot, not bound to the live collector.</returns>
    public Task<IReadOnlyList<FakeLogRecord>> GetMatches()
    {
        Context.ExpressionBuilder.Append(".GetMatches()");
        return ExecuteAndReturnMatchesAsync();
    }

    /// <summary>
    /// Returns the single matched record once the assertion passes. Requires the chain's count
    /// expectation to be exactly one: typically expressed via <see cref="Once"/> or
    /// <see cref="Exactly"/> with count <c>1</c>, but any terminator that constrains the count
    /// to exactly one (including <see cref="Between"/> with both bounds equal to <c>1</c>) is
    /// also accepted. Throws <see cref="InvalidOperationException"/> for any other expectation
    /// so the call site fails fast on a nonsensical "give me the single match" against a chain
    /// that allows N matches.
    /// </summary>
    /// <returns>The single matched record.</returns>
    /// <exception cref="InvalidOperationException">The chain's count expectation does not constrain the match count to exactly one.</exception>
    public Task<FakeLogRecord> GetMatch()
    {
        if (!_count.IsExactlyOne)
        {
            throw new InvalidOperationException(
                "GetMatch() requires the chain to constrain the match count to exactly one: " +
                "use .Once() or .Exactly(1) (or any terminator with both bounds equal to 1) " +
                "before .GetMatch(). Use .GetMatches() to retrieve any number of matches.");
        }

        Context.ExpressionBuilder.Append(".GetMatch()");
        return ExecuteAndReturnSingleMatchAsync();
    }

    private async Task<IReadOnlyList<FakeLogRecord>> ExecuteAndReturnMatchesAsync()
    {
        await AssertAsync();
        return _capturedMatches ?? [];
    }

    private async Task<FakeLogRecord> ExecuteAndReturnSingleMatchAsync()
    {
        await AssertAsync();
        return _capturedMatches![0];
    }

    /// <inheritdoc/>
    protected override Task<AssertionResult> CheckAsync(EvaluationMetadata<FakeLogCollector> metadata)
    {
        if (metadata.Exception is not null)
        {
            return Task.FromResult(AssertionResult.Failed(
                $"threw {metadata.Exception.GetType().Name}", metadata.Exception));
        }

        var collector = metadata.Value;
        if (collector is null)
            return Task.FromResult(AssertionResult.Failed("collector was null"));

        return Task.FromResult(Evaluate(collector));
    }

    /// <summary>Evaluates the count, the capture floor, and the spacing against the collector's records.</summary>
    /// <param name="collector">The collector under assertion.</param>
    /// <returns>The outcome.</returns>
    private AssertionResult Evaluate(FakeLogCollector collector)
    {
        var snapshot = collector.GetSnapshot();
        var matches = GetMatches(snapshot);
        _capturedMatches = matches;

        if (!_count.IsSatisfiedBy(matches.Count))
        {
            var headline = string.Create(CultureInfo.InvariantCulture, $"{matches.Count} record(s) matched");
            if (matches.Count < _count.Min && TryDescribeUncapturedFloor(collector, out var floorNote))
                headline += ": " + floorNote;
            return AssertionResult.Failed(BuildFailureMessage(headline, snapshot));
        }

        // A count that admits zero is an absence check, vacuous when the floor filtered out every level it targets.
        if (matches.Count is 0 && TryDescribeVacuousFloor(collector, out var vacuous))
            return AssertionResult.Failed(vacuous);

        return _minimumGap is { } interval && DescribeTooClose(matches, interval) is { } tooClose
            ? AssertionResult.Failed(BuildFailureMessage(tooClose, snapshot))
            : AssertionResult.Passed;
    }

    /// <summary>Finds the first two consecutive matching records (by timestamp) that are less than
    /// <paramref name="interval"/> apart, and describes them.</summary>
    /// <param name="matches">The matching records.</param>
    /// <param name="interval">The shortest allowed gap.</param>
    /// <returns>The description, or <see langword="null"/> when every gap is long enough.</returns>
    private static string? DescribeTooClose(IReadOnlyList<FakeLogRecord> matches, TimeSpan interval)
    {
        var ordered = matches.OrderBy(static record => record.Timestamp).ToArray();
        var first = -1;
        var tooShort = 0;
        for (var i = 1; i < ordered.Length; i++)
        {
            if (ordered[i].Timestamp - ordered[i - 1].Timestamp < interval)
            {
                tooShort++;
                if (first < 0)
                    first = i;
            }
        }

        if (tooShort is 0)
            return null;

        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture,
            $"two matching records were {ordered[first].Timestamp - ordered[first - 1].Timestamp:c} apart, less than {interval:c} ({tooShort} of {ordered.Length - 1} gaps too short):")
            .AppendLine();
        LogAssertionRendering.AppendRecords(sb, [ordered[first - 1], ordered[first]], 0, DumpVerbosity.Compact, withTimestamps: true);
        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        StringBuilder filters = new();
        AppendFilterSummary(filters);

        StringBuilder sb = new();
        sb.Append(_count.Description)
            .Append(" log record(s) to have been logged");
        if (_minimumGap is { } interval)
        {
            sb.Append(CultureInfo.InvariantCulture, $", at most once per {interval:c}");
            if (filters.Length > 0)
                sb.Append(',');
        }

        return sb.Append(filters).ToString();
    }
}
