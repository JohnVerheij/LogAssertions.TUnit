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
/// TUnit assertion that verifies a <see cref="FakeLogCollector"/> does <em>not</em> contain
/// matching log records. Inherits filter chaining from <see cref="LogAssertionBase{TSelf}"/>;
/// the expectation is fixed at zero matches (no terminators).
/// </summary>
[AssertionExtension("HasNotLogged")]
public sealed class HasNotLoggedAssertion : LogAssertionBase<HasNotLoggedAssertion>
{
    /// <summary>Initializes a negative log assertion. Called by the TUnit source generator.</summary>
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

        if (matchCount is 0)
        {
            // Guard against a false green: when the chain's level filters restrict it to records
            // below the collector's capture floor, nothing at that level was ever captured, so a
            // zero match count is vacuous rather than a genuine absence.
            return Task.FromResult(
                TryDescribeVacuousFloor(collector, out var vacuousMessage)
                    ? AssertionResult.Failed(vacuousMessage)
                    : AssertionResult.Passed);
        }

        return Task.FromResult(AssertionResult.Failed(DescribeMatches(matchCount, snapshot)));
    }

    /// <summary>Renders the failure. When the failure message's captured records (the last 50) already show every
    /// matching record, that is the usual message; otherwise (an older match would be hidden) it lists the matching
    /// records themselves, capped as the captured records are, so the offending record is always visible.</summary>
    /// <param name="matchCount">The number of matching records.</param>
    /// <param name="snapshot">All captured records.</param>
    /// <returns>The failure message body.</returns>
    private string DescribeMatches(int matchCount, IReadOnlyList<FakeLogRecord> snapshot)
    {
        var matches = GetMatches(snapshot);
        var shownFrom = Math.Max(0, snapshot.Count - LogAssertionRendering.FailureRecordLimit);
        var firstShown = false;
        for (var i = shownFrom; i < snapshot.Count && !firstShown; i++)
            firstShown = ReferenceEquals(snapshot[i], matches[0]);

        if (firstShown)
            return BuildFailureMessage(matchCount, snapshot);

        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture, $"{matchCount} record(s) matched")
            .AppendLine()
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"Matching records ({matchCount} of {snapshot.Count} captured):")
            .AppendLine();
        AppendCapturedRecords(sb, matches);
        return sb.ToString();
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
