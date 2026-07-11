using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Attributes;
using TUnit.Assertions.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit assertion that gates a test run on its log output: every captured record at or above
/// <c>floor</c> must have been produced by one of the allowed definitions. Records below the floor
/// are always permitted and are never enumerated, which is what makes the gate usable on a real
/// service: the interesting band (warnings and errors) is small and can be listed, while the
/// Debug/Trace volume (heartbeats, request bodies, request tracing) is not.
/// </summary>
/// <example>
/// <code>
/// // "nothing at Warning-or-above escaped, except these two known-good events"
/// await Assert.That(collector).HasLoggedOnly(LogLevel.Warning)
///     .Allowing(
///         UpstreamContractViolated,   // deliberately emitted; flags an upstream bug
///         StaleSessionDropped);      // benign post-restart close
/// </code>
/// With no allowed definitions the gate asserts that nothing was logged at or above the floor at
/// all, which is the "clean run" check:
/// <code>
/// await Assert.That(collector).HasLoggedOnly(LogLevel.Warning);
/// </code>
/// </example>
/// <remarks>
/// This is the complement of the rest of the DSL. <c>HasLogged()</c> asserts an expected record is
/// present; the gate asserts no <em>unexpected</em> record is. Assert it once per test class or
/// fixture (a TUnit <c>[After]</c> hook over the fixture's collector is the natural home) rather
/// than per test.
/// </remarks>
[AssertionExtension("HasLoggedOnly")]
public sealed class HasLoggedOnlyAssertion : Assertion<FakeLogCollector>
{
    private readonly LogLevel _floor;
    private readonly List<LogDefinition> _allowed = [];

    /// <summary>Initialises the gate at <paramref name="floor"/>. Called by the TUnit source
    /// generator. With no <see cref="Allowing"/> call the gate asserts nothing was logged at or
    /// above the floor at all.</summary>
    /// <param name="context">The assertion context supplied by TUnit.</param>
    /// <param name="floor">The lowest level the gate inspects. Records below it are always allowed.</param>
    public HasLoggedOnlyAssertion(AssertionContext<FakeLogCollector> context, LogLevel floor)
        : base(context)
    {
        _floor = floor;
        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".HasLoggedOnly({floor})");
    }

    /// <summary>
    /// Permits the given definitions at or above the floor. Call it once with every known-good event,
    /// or repeatedly to accumulate:
    /// <c>.HasLoggedOnly(LogLevel.Warning).Allowing(UpstreamContractViolated, StaleSessionDropped)</c>.
    /// </summary>
    /// <param name="definitions">The definitions permitted at or above the floor. Must be non-null
    /// and contain no nulls.</param>
    /// <returns>This assertion for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="definitions"/> contains a <see langword="null"/> definition.</exception>
    public HasLoggedOnlyAssertion Allowing(params LogDefinition[] definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var definition in definitions)
        {
            if (definition is null)
            {
                throw new ArgumentException("The allowed definitions must not contain null.", nameof(definitions));
            }

            _allowed.Add(definition);
        }

        Context.ExpressionBuilder.Append(CultureInfo.InvariantCulture, $".Allowing({definitions.Length} definitions)");
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

        // Guard a vacuous gate: when the collector's capture floor sits above the asserted floor, the
        // records in between were never captured, so an empty offender list would mean "nothing was
        // recorded", not "nothing was logged". Same class of false green the HasNotLogged floor guard
        // catches.
        if (LogCaptureFloorRegistry.TryGetFloor(collector, out var captureFloor) && captureFloor > _floor)
        {
            return Task.FromResult(AssertionResult.Failed(string.Format(
                CultureInfo.InvariantCulture,
                "this gate inspects records at level {0} or above, but the collector's capture floor is {1}, "
                + "so records between {0} and {1} were never captured and the gate would pass vacuously. "
                + "Lower the floor (LogCollectorBuilder.Create with a lower minimumLevel) or raise the gate's floor to {1}.",
                _floor,
                captureFloor)));
        }

        var allowedFilter = _allowed.Count is 0
            ? null
            : LogFilter.MatchingAny(_allowed[0], [.. _allowed.GetRange(1, _allowed.Count - 1)]);

        var snapshot = collector.GetSnapshot();
        var offenders = new List<FakeLogRecord>();
        foreach (var record in snapshot)
        {
            if (record.Level >= _floor && !(allowedFilter?.Matches(record) ?? false))
            {
                offenders.Add(record);
            }
        }

        return Task.FromResult(offenders.Count is 0
            ? AssertionResult.Passed
            : AssertionResult.Failed(BuildFailureMessage(offenders)));
    }

    /// <summary>Renders the gate failure: the records that escaped the allowlist, then the allowlist
    /// itself so the reader can see what <em>was</em> permitted.</summary>
    /// <param name="offenders">The records at or above the floor that matched no allowed definition.</param>
    /// <returns>The multi-line failure body.</returns>
    private string BuildFailureMessage(List<FakeLogRecord> offenders)
    {
        StringBuilder sb = new();
        sb.Append(CultureInfo.InvariantCulture, $"{offenders.Count} record(s) at level {_floor} or above were not in the allowlist")
            .AppendLine()
            .AppendLine()
            .Append("Unexpected records:")
            .AppendLine();

        LogAssertionRendering.AppendCapturedRecords(sb, offenders);

        sb.AppendLine();
        if (_allowed.Count is 0)
        {
            sb.Append("Allowed definitions: (none: the gate asserts nothing was logged at or above the floor)").AppendLine();
        }
        else
        {
            sb.Append("Allowed definitions:").AppendLine();
            foreach (var definition in _allowed)
            {
                sb.Append("  ").Append(definition.ToString()).AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    protected override string GetExpectation()
        => string.Format(
            CultureInfo.InvariantCulture,
            "no record at level {0} or above other than the {1} allowed definition(s)",
            _floor,
            _allowed.Count);
}
