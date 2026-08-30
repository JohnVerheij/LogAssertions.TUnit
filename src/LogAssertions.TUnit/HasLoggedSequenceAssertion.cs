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
/// TUnit assertion that verifies a <see cref="FakeLogCollector"/> contains a sequence of
/// matching log records, in order. Each step is built from the same filter chain as
/// <see cref="HasLoggedAssertion"/> (<c>AtLevel</c>, <c>Containing</c>, etc.); call
/// <see cref="Then"/> to commit the current step and start the next strictly-ordered step,
/// or <see cref="ThenAnyOrder(System.Action{HasLoggedSequenceAssertion}[])"/> to commit a
/// concurrent group whose sub-steps must all occur but in any order.
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
    private readonly List<SequenceStep> _steps = [];
    private List<ILogRecordFilter> _currentFilters;
    private bool _isCapturingSubStep;

    /// <summary>Initialises a sequence assertion. Called by the TUnit source generator.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    public HasLoggedSequenceAssertion(AssertionContext<FakeLogCollector> context) : base(context)
    {
        _currentFilters = [];
        _steps.Add(new SequenceStep(SequenceStepKind.Simple, _currentFilters, AnyOrderSubSteps: null));
    }

    /// <summary>
    /// Commits the current step's filters and starts a new strictly-ordered step. The next
    /// filter call adds to the new step. An empty step (a <see cref="Then"/> call with no
    /// subsequent filter calls before the next <see cref="Then"/> or terminator) is a no-op
    /// during evaluation: it neither matches nor consumes a record. This keeps multi-step
    /// chains like <c>.AtLevel(Info).Then().Then().AtLevel(Warning)</c> matching exactly the
    /// records the filters describe, with the empty steps acting as inert separators.
    /// </summary>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="InvalidOperationException">Called inside a <see cref="ThenAnyOrder"/>
    /// sub-step configurator. Sub-step configurators describe filters for one concurrent group;
    /// structure the outer sequence at the top level instead.</exception>
    public HasLoggedSequenceAssertion Then()
    {
        if (_isCapturingSubStep)
            throw new InvalidOperationException(
                "Then() cannot be called inside a ThenAnyOrder sub-step configurator. " +
                "Sub-step configurators describe filters for one concurrent group; structure " +
                "the outer sequence at the top level after ThenAnyOrder(...) returns.");
        _currentFilters = [];
        _steps.Add(new SequenceStep(SequenceStepKind.Simple, _currentFilters, AnyOrderSubSteps: null));
        Context.ExpressionBuilder.Append(".Then()");
        return this;
    }

    /// <summary>
    /// Commits the current step and adds a concurrent group: all <paramref name="subSteps"/>
    /// must match somewhere in the remaining records, but the order among them does not matter.
    /// Records that match no sub-step are skipped. Sub-steps are matched via backtracking, so
    /// any order-independent valid assignment is found if one exists: even when sub-step
    /// filters overlap (a broad filter never starves a more specific filter).
    /// </summary>
    /// <param name="subSteps">Configurators for each concurrent sub-step. Each receives the
    /// assertion and adds filter calls; the filters added during the configurator's invocation
    /// are captured as that sub-step's filters.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Called inside another sub-step configurator,
    /// or a sub-step configurator calls <see cref="Then"/> or <see cref="ThenAnyOrder"/>.
    /// Sub-step configurators must add filters only: outer sequence structure must be expressed
    /// at the top level, not inside a configurator.</exception>
    public HasLoggedSequenceAssertion ThenAnyOrder(params Action<HasLoggedSequenceAssertion>[] subSteps)
    {
        if (_isCapturingSubStep)
            throw new InvalidOperationException(
                "ThenAnyOrder() cannot be called inside another ThenAnyOrder sub-step configurator. " +
                "Concurrent groups must be siblings in the outer sequence.");
        ArgumentNullException.ThrowIfNull(subSteps);

        var subStepFilters = new List<List<ILogRecordFilter>>(subSteps.Length);

        // _isCapturingSubStep guards against a configurator calling Then() / ThenAnyOrder() on
        // the live assertion instance: those would mutate _steps mid-capture and silently
        // produce a sequence shape different from what the fluent chain implies. The guard
        // throws InvalidOperationException if it happens; try/finally ensures the flag clears
        // even if a configurator throws.
        _isCapturingSubStep = true;
        try
        {
            foreach (var configurator in subSteps)
            {
                if (configurator is null)
                    throw new ArgumentNullException(nameof(subSteps), "Sub-step configurator must not be null.");
                var thisSub = new List<ILogRecordFilter>();
                _currentFilters = thisSub;
                configurator(this);
                subStepFilters.Add(thisSub);
            }
        }
        finally
        {
            _isCapturingSubStep = false;
        }

        // The AnyOrder step's Filters field is unused at evaluation time: only AnyOrderSubSteps
        // is read by MatchAnyOrderStep. Pass an empty (sentinel) list rather than redirecting
        // _currentFilters to it: any subsequent .AtLevel/.Containing calls must land in a NEW
        // strictly-ordered Simple step, not in the AnyOrder step's dead-code Filters list.
        _steps.Add(new SequenceStep(SequenceStepKind.AnyOrder, [], subStepFilters));
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".ThenAnyOrder({subSteps.Length} sub-steps)");
        // Open a fresh Simple step so post-AnyOrder filters land somewhere observable.
        // Without this seam step, .ThenAnyOrder(...).Containing("tail") would silently drop
        // "tail" because _currentFilters would point at the AnyOrder step's unread Filters list.
        // The seam step is empty unless a filter call follows ThenAnyOrder; per MatchSimpleStep
        // semantics, an empty step is a no-op.
        _currentFilters = [];
        _steps.Add(new SequenceStep(SequenceStepKind.Simple, _currentFilters, AnyOrderSubSteps: null));
        return this;
    }

    /// <summary>
    /// Adds a strictly-ordered step matching the given <paramref name="definition"/>. Sugar for
    /// <c>Then().Matching(definition)</c> that starts a new step only when the current one already
    /// carries filters, so a whole flow reads as one call per event:
    /// <c>.HasLoggedSequence().Step(OrderReceived).Step(PaymentCaptured).Step(OrderShipped)</c>.
    /// Further filters chain onto the step just opened
    /// (<c>.Step(Retry).AtLevel(LogLevel.Warning).Step(Succeeded)</c>).
    /// </summary>
    /// <param name="definition">The definition the step matches. Must be non-null.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Called inside a <see cref="ThenAnyOrder"/> sub-step
    /// configurator, which describes filters for one concurrent group rather than outer sequence structure.</exception>
    public HasLoggedSequenceAssertion Step(LogDefinition definition)
    {
        // Step() is outer-sequence structure, so it carries the same guard as Then() and
        // ThenAnyOrder(). It cannot delegate the guard to Then(): a configurator is always handed
        // a fresh empty filter list, so its first Step() would take the Count == 0 path below and
        // silently behave as Matching(), while a second Step() in the same configurator would
        // throw. Guarding up front keeps the call's meaning independent of its position.
        if (_isCapturingSubStep)
            throw new InvalidOperationException(
                "Step() cannot be called inside a ThenAnyOrder sub-step configurator. " +
                "Sub-step configurators describe filters for one concurrent group: use " +
                "Matching(definition) to match a definition inside a sub-step, and structure the " +
                "outer sequence at the top level after ThenAnyOrder(...) returns.");
        ArgumentNullException.ThrowIfNull(definition);

        // The constructor opens the first step, so the first Step() call fills it rather than
        // opening an empty one ahead of itself.
        if (_currentFilters.Count > 0)
        {
            _ = Then();
        }

        return Matching(definition);
    }

    /// <inheritdoc/>
    protected override void AddFilter(ILogRecordFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _currentFilters.Add(filter);
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

        for (var stepIndex = 0; stepIndex < _steps.Count; stepIndex++)
        {
            var step = _steps[stepIndex];
            var ok = step.Kind is SequenceStepKind.Simple
                ? MatchSimpleStep(step, snapshot, ref recordIndex)
                : MatchAnyOrderStep(step, snapshot, ref recordIndex);

            if (!ok)
            {
                var label = step.Kind is SequenceStepKind.Simple ? "Step" : "Any-order group";
                return Task.FromResult(AssertionResult.Failed(BuildSequenceFailureMessage(stepIndex, snapshot, label)));
            }
        }

        return Task.FromResult(AssertionResult.Passed);
    }

    private static bool MatchSimpleStep(SequenceStep step, IReadOnlyList<FakeLogRecord> snapshot, ref int recordIndex)
    {
        // Empty step is a no-op (no filter constraint, no record consumed). This pins the
        // documented behavior verified by SequenceEmptyStepIsSkippedAsync: chains like
        // .AtLevel(Info).Then().Then().AtLevel(Warning) match exactly the records the named
        // filters describe; the empty .Then() acts as an inert separator.
        if (step.Filters.Count is 0)
            return true;

        while (recordIndex < snapshot.Count)
        {
            var record = snapshot[recordIndex];
            recordIndex++;
            if (step.Filters.TrueForAll(f => f.Matches(record)))
                return true;
        }
        return false;
    }

    private static bool MatchAnyOrderStep(SequenceStep step, IReadOnlyList<FakeLogRecord> snapshot, ref int recordIndex)
    {
        var subSteps = step.AnyOrderSubSteps!;
        if (subSteps.Count is 0)
            return true;

        // Backtracking matcher: explore record-to-sub-step assignments rather than greedy
        // first-match. The greedy version was order-dependent: a broad filter could consume
        // a record needed by a later specific filter even when a valid assignment existed.
        // Worst case is O(N^k) where N=remaining records, k=sub-step count; fine in practice
        // because k is typically 2-5.
        var assigned = new int[subSteps.Count];
        Array.Fill(assigned, -1);
        if (!TryAssignAnyOrder(0, subSteps, snapshot, recordIndex, assigned))
            return false;

        // Advance recordIndex past the highest assigned record so subsequent steps in the
        // outer sequence resume from there. assigned[] is fully populated when TryAssignAnyOrder
        // returns true, so .Max() is safe (and S3267-clean vs the equivalent foreach).
        recordIndex = Math.Max(recordIndex - 1, assigned.Max()) + 1;
        return true;
    }

    private static bool TryAssignAnyOrder(
        int subStepIdx,
        IReadOnlyList<List<ILogRecordFilter>> subSteps,
        IReadOnlyList<FakeLogRecord> snapshot,
        int startIdx,
        int[] assigned)
    {
        if (subStepIdx == subSteps.Count)
            return true;

        var filters = subSteps[subStepIdx];
        for (var rIdx = startIdx; rIdx < snapshot.Count; rIdx++)
        {
            // Skip records already claimed by earlier sub-steps in this assignment path.
            var claimed = false;
            for (var j = 0; j < subStepIdx; j++)
            {
                if (assigned[j] == rIdx)
                {
                    claimed = true;
                    break;
                }
            }
            if (claimed)
                continue;

            if (filters.TrueForAll(f => f.Matches(snapshot[rIdx])))
            {
                assigned[subStepIdx] = rIdx;
                if (TryAssignAnyOrder(subStepIdx + 1, subSteps, snapshot, startIdx, assigned))
                    return true;
                // Backtracking: reset this sub-step's claim so the next iteration of the outer
                // for-loop can try a different record. The double-write to assigned[subStepIdx]
                // is intentional (assign-then-reset): Sonar S4143 is suppressed because the
                // analyzer can't see that the first write was conditional on the recursive call
                // succeeding.
#pragma warning disable S4143
                assigned[subStepIdx] = -1;
#pragma warning restore S4143
            }
        }
        return false;
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
    {
        StringBuilder sb = new();
        sb.Append("log records to occur in order");

        var hasContent = false;
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            if (step.Kind is SequenceStepKind.Simple && step.Filters.Count is 0)
                continue;

            sb.Append(hasContent ? " then " : ": ");

            if (step.Kind is SequenceStepKind.Simple)
            {
                sb.AppendJoin(" + ", step.Filters.Select(static f => f.Description));
            }
            else
            {
                sb.Append("(any-order: ");
                var first = true;
                foreach (var sub in step.AnyOrderSubSteps!)
                {
                    if (!first)
                        sb.Append(" / ");
                    sb.AppendJoin(" + ", sub.Select(static f => f.Description));
                    first = false;
                }
                sb.Append(')');
            }

            hasContent = true;
        }

        return sb.ToString();
    }

    private string BuildSequenceFailureMessage(int failedStepIndex, IReadOnlyList<FakeLogRecord> snapshot, string stepLabel)
    {
        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture, $"{stepLabel} {failedStepIndex + 1} did not match")
            .AppendLine();

        var step = _steps[failedStepIndex];
        if (step.Kind is SequenceStepKind.Simple)
        {
            sb.Append("Step filters: ")
                .AppendJoin(" + ", step.Filters.Select(static f => f.Description));
        }
        else
        {
            sb.Append("Any-order sub-steps:");
            for (var i = 0; i < step.AnyOrderSubSteps!.Count; i++)
            {
                sb.AppendLine().Append(CultureInfo.InvariantCulture, $"  [{i + 1}] ")
                    .AppendJoin(" + ", step.AnyOrderSubSteps[i].Select(static f => f.Description));
            }
        }

        sb.AppendLine().AppendLine()
            .Append(CultureInfo.InvariantCulture, $"Captured records ({snapshot.Count} total):")
            .AppendLine();

        AppendCapturedRecords(sb, snapshot);
        return sb.ToString();
    }

    private enum SequenceStepKind { Simple, AnyOrder }

    private sealed record SequenceStep(
        SequenceStepKind Kind,
        List<ILogRecordFilter> Filters,
        List<List<ILogRecordFilter>>? AnyOrderSubSteps);
}
