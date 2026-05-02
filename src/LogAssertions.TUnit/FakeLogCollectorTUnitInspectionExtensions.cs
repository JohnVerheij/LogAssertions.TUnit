using System;
using LogAssertions;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit-specific non-asserting inspection helpers on <see cref="FakeLogCollector"/>. The
/// framework-agnostic counterparts live in <see cref="FakeLogCollectorInspectionExtensions"/>
/// in the LogAssertions core package; helpers here add bindings to TUnit's per-test output
/// writer so the rendered records appear inline in the test report.
/// </summary>
public static class FakeLogCollectorTUnitInspectionExtensions
{
    /// <summary>
    /// Renders every captured record to the current TUnit test's standard output writer using
    /// the same formatter the failure-message snapshot uses. Equivalent to
    /// <c>collector.DumpTo(TestContext.Current.Output.StandardOutput)</c>, but skips the boilerplate.
    /// Use during test development to see what was actually logged before writing the assertion.
    /// </summary>
    /// <param name="collector">The collector to dump.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called outside a TUnit test execution (no <see cref="TestContext.Current"/>). The method
    /// is only meaningful inside a <c>[Test]</c> method; the failure mode here is a clear
    /// diagnostic rather than a silent no-op.
    /// </exception>
    public static void DumpToTestOutput(this FakeLogCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        var context = TestContext.Current
            ?? throw new InvalidOperationException(
                "DumpToTestOutput() requires an active TUnit test context (TestContext.Current was null). " +
                "Call this from inside a [Test] method, or use DumpTo(TextWriter) for non-TUnit contexts.");
        collector.DumpTo(context.Output.StandardOutput);
    }
}
