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
        => DumpToTestOutput(collector, DumpVerbosity.Default);

    /// <summary>
    /// Verbosity-controlled overload of <see cref="DumpToTestOutput(FakeLogCollector)"/>. The
    /// no-arg overload uses <see cref="DumpVerbosity.Default"/>; pass an explicit verbosity to
    /// surface less (Compact: headlines only) or more (Verbose: full exception stack trace).
    /// </summary>
    /// <param name="collector">The collector to dump.</param>
    /// <param name="verbosity">How much detail to render per record. See
    /// <see cref="DumpVerbosity"/> for the contract of each level.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">There is no active TUnit test context.</exception>
    public static void DumpToTestOutput(this FakeLogCollector collector, DumpVerbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(collector);
        var context = TestContext.Current
            ?? throw new InvalidOperationException(
                "DumpToTestOutput() requires an active TUnit test context (TestContext.Current was null). " +
                "Call this from inside a [Test] method, or use DumpTo(TextWriter) for non-TUnit contexts.");
        collector.DumpTo(context.Output.StandardOutput, verbosity);
    }

    /// <summary>
    /// Arms a dump of <paramref name="collector"/> for the current test: when the test ends without passing (it
    /// failed, timed out, or was canceled; a skipped test does not count), the records captured since this call are
    /// written to the test's output, one timestamp (UTC) per record. A test that passes writes nothing. Call it in the
    /// test or in a <c>[Before(Test)]</c> hook; it needs
    /// <c>[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]</c> in the test project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The start point is the collector's latest record at the time of the call, not a clock reading, so it is right
    /// whatever clock the collector uses. When the collector is cleared after the call, every record is written. The
    /// dump writes every record since the call (only the dump inside an assertion's failure message is capped), and its
    /// last line repeats the summary: TUnit keeps only the latest part of a very large test output, so the report of
    /// such a dump loses its start but still ends with the summary.
    /// </para>
    /// <para>
    /// Clock: the time on each line is the record's <see cref="FakeLogRecord.Timestamp"/>, that is, the collector's
    /// clock (<c>FakeLogCollectorOptions.TimeProvider</c>, set by <c>LogCollectorBuilder.Create(timeProvider)</c>);
    /// without one it is the system clock.
    /// </para>
    /// <para>
    /// A retried test arms again in each attempt (the test body and its hooks run again). After an attempt that did
    /// not pass, that attempt's records are written, and the heading and the last line name the attempt. A second call
    /// for the same collector in the same attempt changes nothing.
    /// </para>
    /// </remarks>
    /// <param name="collector">The collector to dump when the test does not pass.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">There is no current test; the
    /// <see cref="LogAssertionsTestLifetimeAttribute"/> is not applied to the test project (the message names the line
    /// to add); TUnit has not started the current test yet (a <c>[Before(Class)]</c> hook, the test class's
    /// constructor, or a fixture's initialization); or the current test has ended.</exception>
    public static void DumpToTestOutputOnFailure(this FakeLogCollector collector)
        => DumpToTestOutputOnFailure(collector, DumpVerbosity.Default);

    /// <summary>Verbosity-controlled overload of <see cref="DumpToTestOutputOnFailure(FakeLogCollector)"/>.</summary>
    /// <remarks>Clock: the time on each line is the record's <see cref="FakeLogRecord.Timestamp"/>, the collector's
    /// clock.</remarks>
    /// <param name="collector">The collector to dump when the test does not pass.</param>
    /// <param name="verbosity">How much detail to write per record. See <see cref="DumpVerbosity"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="collector"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">There is no current test; the
    /// <see cref="LogAssertionsTestLifetimeAttribute"/> is not applied to the test project; TUnit has not started the
    /// current test yet; or the current test has ended.</exception>
    public static void DumpToTestOutputOnFailure(this FakeLogCollector collector, DumpVerbosity verbosity)
    {
        ArgumentNullException.ThrowIfNull(collector);
        var context = TestContext.Current
            ?? throw new InvalidOperationException(
                "DumpToTestOutputOnFailure() requires an active TUnit test context (TestContext.Current was null). " +
                "Call it in a [Test] method or a [Before(Test)] hook.");
        TestLifetimeTable.Shared.Arm(context, collector, verbosity);
    }
}
