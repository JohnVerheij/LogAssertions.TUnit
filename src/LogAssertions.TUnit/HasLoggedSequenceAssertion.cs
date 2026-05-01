using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit assertion that verifies a <see cref="FakeLogCollector"/> contains a sequence of
/// matching log records, in order. Each step is built from the same filter chain as
/// <see cref="HasLoggedAssertion"/> (<c>AtLevel</c>, <c>Containing</c>, etc.); call
/// <see cref="Then"/> to commit the current step and start the next one.
/// </summary>
/// <example>
/// <code>
/// await Assert.That(collector).HasLoggedSequence()
///     .AtLevel(LogLevel.Information).Containing("Started")
///     .Then().AtLevel(LogLevel.Warning).Containing("validation failed")
///     .Then().AtLevel(LogLevel.Information).Containing("Stopped");
/// </code>
/// Each step matches the first subsequent record satisfying its filters; records between
/// matches are skipped (sequence is order-preserving but not contiguous).
/// </example>
[AssertionExtension("HasLoggedSequence")]
public sealed class HasLoggedSequenceAssertion : LogAssertionBase<HasLoggedSequenceAssertion>
{
    private readonly List<List<System.Func<FakeLogRecord, bool>>> _stepPredicates = [];
    private readonly List<List<string>> _stepDescriptions = [];
    private List<System.Func<FakeLogRecord, bool>> _currentPredicates;
    private List<string> _currentDescriptions;

    /// <summary>Initialises a sequence assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    public HasLoggedSequenceAssertion(AssertionContext<FakeLogCollector> context) : base(context)
    {
        _currentPredicates = [];
        _currentDescriptions = [];
        _stepPredicates.Add(_currentPredicates);
        _stepDescriptions.Add(_currentDescriptions);
    }

    /// <summary>
    /// Commits the current step's filters and starts a new step. The next filter call adds
    /// to the new step. Calling <see cref="Then"/> with no preceding filters in the current
    /// step results in an empty step which always matches the next available record.
    /// </summary>
    /// <returns>This assertion for chaining.</returns>
    public HasLoggedSequenceAssertion Then()
    {
        _currentPredicates = [];
        _currentDescriptions = [];
        _stepPredicates.Add(_currentPredicates);
        _stepDescriptions.Add(_currentDescriptions);
        Context.ExpressionBuilder.Append(".Then()");
        return this;
    }

    /// <inheritdoc/>
    protected override void AddPredicate(System.Func<FakeLogRecord, bool> predicate, string description)
    {
        System.ArgumentNullException.ThrowIfNull(predicate);
        System.ArgumentNullException.ThrowIfNull(description);
        _currentPredicates.Add(predicate);
        _currentDescriptions.Add(description);
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
        var recordIndex = 0;

        for (var stepIndex = 0; stepIndex < _stepPredicates.Count; stepIndex++)
        {
            var stepPreds = _stepPredicates[stepIndex];
            if (stepPreds.Count == 0)
                continue;

            var matched = false;
            while (recordIndex < snapshot.Count)
            {
                var record = snapshot[recordIndex];
                recordIndex++;
                if (stepPreds.TrueForAll(p => p(record)))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
                return Task.FromResult(AssertionResult.Failed(BuildSequenceFailureMessage(stepIndex, snapshot)));
        }

        return Task.FromResult(AssertionResult.Passed);
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        StringBuilder sb = new();
        sb.Append("log records to occur in order");

        var hasContent = false;
        for (var i = 0; i < _stepDescriptions.Count; i++)
        {
            if (_stepDescriptions[i].Count == 0)
                continue;

            sb.Append(hasContent ? " then " : ": ");
            sb.AppendJoin(" + ", _stepDescriptions[i]);
            hasContent = true;
        }

        return sb.ToString();
    }

    private string BuildSequenceFailureMessage(int failedStepIndex, IReadOnlyList<FakeLogRecord> snapshot)
    {
        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture, $"Step {failedStepIndex + 1} did not match any remaining record")
            .AppendLine()
            .Append("Step filters: ")
            .AppendJoin(" + ", _stepDescriptions[failedStepIndex])
            .AppendLine()
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"Captured records ({snapshot.Count} total):")
            .AppendLine();

        if (snapshot.Count == 0)
        {
            sb.AppendLine("  (no records)");
        }
        else
        {
            foreach (var record in snapshot)
            {
                sb.Append("  [").Append(record.Level).Append("] ");
                if (!string.IsNullOrEmpty(record.Category))
                    sb.Append(record.Category).Append(": ");
                sb.Append(record.Message);
                if (record.Exception is not null)
                {
                    sb.Append(" | ").Append(record.Exception.GetType().Name)
                        .Append(": ").Append(record.Exception.Message);
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
