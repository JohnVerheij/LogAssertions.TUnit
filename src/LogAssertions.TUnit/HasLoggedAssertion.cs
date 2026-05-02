using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit assertion that verifies a <see cref="FakeLogCollector"/> contains matching log records.
/// Inherits filter chaining from <see cref="LogAssertionBase{TSelf}"/>; adds count-expectation
/// terminators (<c>Once</c>, <c>Exactly</c>, <c>AtLeast</c>, <c>AtMost</c>, <c>Never</c>) and the
/// value-returning terminators <c>GetMatch</c> and <c>GetMatches</c> for handing the matched
/// records to follow-up assertions.
/// </summary>
[AssertionExtension("HasLogged")]
public sealed class HasLoggedAssertion : LogAssertionBase<HasLoggedAssertion>
{
    private int _minCount = 1;
    private int _maxCount = int.MaxValue;
    private string _terminatorDescription = "at least 1";
    private IReadOnlyList<FakeLogRecord>? _capturedMatches;

    /// <summary>Initialises a positive log assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    public HasLoggedAssertion(AssertionContext<FakeLogCollector> context) : base(context) { }

    /// <summary>Expects exactly one matching record.</summary>
    /// <returns>This assertion for chaining.</returns>
    public HasLoggedAssertion Once()
    {
        _minCount = 1;
        _maxCount = 1;
        _terminatorDescription = "exactly 1";
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
        _minCount = count;
        _maxCount = count;
        _terminatorDescription = string.Format(CultureInfo.InvariantCulture, "exactly {0}", count);
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
        _minCount = count;
        _maxCount = int.MaxValue;
        _terminatorDescription = string.Format(CultureInfo.InvariantCulture, "at least {0}", count);
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
        _minCount = 0;
        _maxCount = count;
        _terminatorDescription = string.Format(CultureInfo.InvariantCulture, "at most {0}", count);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".AtMost({count})");
        return this;
    }

    /// <summary>Expects zero matching records.</summary>
    /// <returns>This assertion for chaining.</returns>
    public HasLoggedAssertion Never()
    {
        _minCount = 0;
        _maxCount = 0;
        _terminatorDescription = "exactly 0";
        Context.ExpressionBuilder.Append(".Never()");
        return this;
    }

    /// <summary>
    /// Expects the matching record count to fall in the inclusive range [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    /// <param name="min">The minimum match count (inclusive). Must be non-negative.</param>
    /// <param name="max">The maximum match count (inclusive). Must be greater than or equal to <paramref name="min"/>.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="min"/> is negative, or <paramref name="max"/> is less than <paramref name="min"/>.
    /// </exception>
    public HasLoggedAssertion Between(int min, int max)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(min);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, min);
        _minCount = min;
        _maxCount = max;
        _terminatorDescription = string.Format(CultureInfo.InvariantCulture, "between {0} and {1}", min, max);
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Between({min}, {max})");
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
    /// expectation to be exactly one — typically expressed via <see cref="Once"/> or
    /// <see cref="Exactly"/> with count <c>1</c>, but any terminator that constrains the count
    /// to exactly one (including <see cref="Between"/> with both bounds equal to <c>1</c>) is
    /// also accepted. Throws <see cref="InvalidOperationException"/> for any other expectation
    /// so the call site fails fast on a nonsensical "give me the single match" against a chain
    /// that allows N matches.
    /// </summary>
    /// <returns>The single matched record.</returns>
    /// <exception cref="InvalidOperationException">
    /// The chain's count expectation does not constrain the match count to exactly one.
    /// </exception>
    public Task<FakeLogRecord> GetMatch()
    {
        if (_minCount != 1 || _maxCount != 1)
        {
            throw new InvalidOperationException(
                "GetMatch() requires the chain to constrain the match count to exactly one — " +
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

        var snapshot = collector.GetSnapshot();
        var matches = GetMatches(snapshot);
        _capturedMatches = matches;
        var matchCount = matches.Count;

        if (matchCount >= _minCount && matchCount <= _maxCount)
            return Task.FromResult(AssertionResult.Passed);

        return Task.FromResult(AssertionResult.Failed(BuildFailureMessage(matchCount, snapshot)));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        StringBuilder sb = new();
        sb.Append(_terminatorDescription)
            .Append(" log record(s) to have been logged");
        AppendFilterSummary(sb);
        return sb.ToString();
    }
}
