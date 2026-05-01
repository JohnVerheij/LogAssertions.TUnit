using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit assertion that verifies a <see cref="FakeLogCollector"/> does <em>not</em> contain
/// matching log records. Inherits filter chaining from <see cref="LogAssertionBase{TSelf}"/>;
/// the expectation is fixed at zero matches (no terminators).
/// </summary>
[AssertionExtension("HasNotLogged")]
public sealed class HasNotLoggedAssertion : LogAssertionBase<HasNotLoggedAssertion>
{
    /// <summary>Initialises a negative log assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    public HasNotLoggedAssertion(AssertionContext<FakeLogCollector> context) : base(context) { }

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

        if (matchCount == 0)
            return Task.FromResult(AssertionResult.Passed);

        return Task.FromResult(AssertionResult.Failed(BuildFailureMessage(matchCount, snapshot)));
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        StringBuilder sb = new();
        sb.Append("no log record(s) to have been logged");
        AppendFilterSummary(sb);
        return sb.ToString();
    }
}
