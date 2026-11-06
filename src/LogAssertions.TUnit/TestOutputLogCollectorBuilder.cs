using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit-specific counterpart to <see cref="LogCollectorBuilder"/> whose factories also mirror each record live to the
/// TUnit test's output, so the test's logs appear inline in the per-test report. Use the core
/// <see cref="LogCollectorBuilder.Create(LogLevel)"/> (no tee) for log-heavy soak tests where writing every record to
/// the per-test output is undesirable.
/// </summary>
/// <remarks>
/// The tee picks the test for each record as <see cref="FakeLoggingBuilderExtensions.AddTeedFakeLogging"/> describes;
/// with <see cref="LogAssertionsTestLifetimeAttribute"/> applied to the test project it never writes into a test that
/// has ended.
/// </remarks>
public static class TestOutputLogCollectorBuilder
{
    /// <summary>
    /// Creates a <see cref="LogCapture"/> (a logger factory wired to a new <see cref="FakeLogCollector"/>) that also
    /// mirrors each record to the test's output as it is logged. The test current now owns the tee, so a record logged
    /// on a thread where no test is current still reaches it while it runs. The caller owns the capture: dispose it
    /// when the test completes.
    /// </summary>
    /// <remarks>Clock: each record's <see cref="FakeLogRecord.Timestamp"/> comes from the system clock. For a fake
    /// clock, use <see cref="CreateTeed(TimeProvider, LogLevel)"/>.</remarks>
    /// <param name="minimumLevel">The minimum level to capture (and tee), also registered as the capture floor.
    /// Default is <see cref="LogLevel.Trace"/> (everything).</param>
    /// <returns>The capture: use it as the factory, and its <see cref="LogCapture.Collector"/> for assertions.</returns>
    public static LogCapture CreateTeed(LogLevel minimumLevel = LogLevel.Trace)
        => CreateTeedFor(new FakeLogCollector(), minimumLevel);

    /// <summary>
    /// Creates a teed <see cref="LogCapture"/> as <see cref="CreateTeed(LogLevel)"/> does, whose collector stamps each
    /// record with <paramref name="timeProvider"/>'s time.
    /// </summary>
    /// <remarks>Clock: each record's <see cref="FakeLogRecord.Timestamp"/> comes from <paramref name="timeProvider"/>,
    /// so <c>AtMostOncePer</c> and the times in a failure dump follow a fake clock.</remarks>
    /// <param name="timeProvider">The clock of the record timestamps, for example a fake clock.</param>
    /// <param name="minimumLevel">The minimum level to capture (and tee), also registered as the capture floor.
    /// Default is <see cref="LogLevel.Trace"/> (everything).</param>
    /// <returns>The capture: use it as the factory, and its <see cref="LogCapture.Collector"/> for assertions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public static LogCapture CreateTeed(TimeProvider timeProvider, LogLevel minimumLevel = LogLevel.Trace)
        => CreateTeedFor(LogCollectorBuilder.CollectorOn(timeProvider), minimumLevel);

    /// <summary>Wraps <paramref name="collector"/> in a teed capture at <paramref name="minimumLevel"/>.</summary>
    /// <param name="collector">The collector of the capture.</param>
    /// <param name="minimumLevel">The minimum level to capture and tee.</param>
    /// <returns>The capture.</returns>
    private static LogCapture CreateTeedFor(FakeLogCollector collector, LogLevel minimumLevel)
        // Delegates to AddTeedFakeLogging so the capture-shaped and builder-shaped helpers share one wiring
        // definition: capture provider, capture-floor registration, and the built-in test-output tee.
        => LogCapture.Create(collector, b => b.AddTeedFakeLogging(collector, minimumLevel));

    /// <summary>
    /// Creates an <see cref="ILoggerFactory"/> whose loggers are enabled at <paramref name="minimumLevel"/> and above,
    /// keep no records, and mirror each record to the test's output. Use it where code needs a real, enabled logger
    /// (so <c>[LoggerMessage]</c> and <c>IsEnabled</c>-guarded code runs) and the test does not assert on logs, but
    /// you still want the logs in the report: a fixture or a long-lived host keeps no growing collector.
    /// </summary>
    /// <remarks>
    /// Each record is teed as <see cref="FakeLoggingBuilderExtensions.AddTeedFakeLogging"/> describes. <c>BeginScope</c>
    /// returns a shared scope whose disposal does nothing, never <see langword="null"/>, as with
    /// <see cref="LogCollectorBuilder.CreateDiscarding"/>.
    /// </remarks>
    /// <param name="minimumLevel">The lowest level reported as enabled and teed. Default is
    /// <see cref="LogLevel.Trace"/> (every level).</param>
    /// <returns>The factory. The caller owns it and disposes it when the test or fixture completes.</returns>
    public static ILoggerFactory CreateTeedDiscarding(LogLevel minimumLevel = LogLevel.Trace)
        => LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(minimumLevel);
            FakeLoggingBuilderExtensions.AddTestOutputTee(b);
        });
}
