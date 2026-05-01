using System;
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
/// terminators (<c>Once</c>, <c>Exactly</c>, <c>AtLeast</c>, <c>AtMost</c>, <c>Never</c>).
/// </summary>
[AssertionExtension("HasLogged")]
public sealed class HasLoggedAssertion : LogAssertionBase<HasLoggedAssertion>
{
    private int _minCount = 1;
    private int _maxCount = int.MaxValue;
    private string _terminatorDescription = "at least 1";

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
        var matchCount = CountMatches(snapshot);

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
