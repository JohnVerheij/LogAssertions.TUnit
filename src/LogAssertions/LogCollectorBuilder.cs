using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Convenience factories for test logging: <see cref="Create(LogLevel)"/> returns a <see cref="LogCapture"/> (a logger factory
/// that writes into its own <see cref="FakeLogCollector"/>), and <see cref="CreateDiscarding"/> returns an enabled
/// factory that keeps nothing.
/// </summary>
/// <example>
/// <code>
/// using var logs = LogCollectorBuilder.Create();
/// var logger = logs.CreateLogger("MyCategory");
/// logger.LogInformation("hello");
/// await Assert.That(logs.Collector).HasLogged().Containing("hello", StringComparison.Ordinal).Once();
/// </code>
/// </example>
public static class LogCollectorBuilder
{
    /// <summary>
    /// Creates a <see cref="LogCapture"/>: a logger factory wired to a new <see cref="FakeLogCollector"/>, captured at
    /// <paramref name="minimumLevel"/> and above. The caller owns it: dispose it when the test completes, which
    /// disposes the factory; the collector keeps its records.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="minimumLevel"/> is also registered as the collector's capture floor, so an absence assertion
    /// that targets only levels below it fails as vacuous instead of passing because nothing was captured.
    /// </para>
    /// <para>
    /// Clock: each record's <see cref="FakeLogRecord.Timestamp"/> comes from the system clock. For a fake clock, use
    /// <see cref="Create(TimeProvider, LogLevel)"/>.
    /// </para>
    /// </remarks>
    /// <param name="minimumLevel">The minimum level to capture. Default is <see cref="LogLevel.Trace"/> (capture everything).</param>
    /// <returns>The capture: use it as the factory, and its <see cref="LogCapture.Collector"/> for assertions.</returns>
    public static LogCapture Create(LogLevel minimumLevel = LogLevel.Trace)
        => CreateFor(new FakeLogCollector(), minimumLevel);

    /// <summary>
    /// Creates a <see cref="LogCapture"/> as <see cref="Create(LogLevel)"/> does, whose collector stamps each record
    /// with <paramref name="timeProvider"/>'s time.
    /// </summary>
    /// <remarks>
    /// Clock: each record's <see cref="FakeLogRecord.Timestamp"/> comes from <paramref name="timeProvider"/>
    /// (<c>FakeLogCollectorOptions.TimeProvider</c>), so timestamp-based checks such as <c>AtMostOncePer</c> and the
    /// times in a failure dump follow a fake clock.
    /// </remarks>
    /// <param name="timeProvider">The clock of the record timestamps, for example a fake clock.</param>
    /// <param name="minimumLevel">The minimum level to capture. Default is <see cref="LogLevel.Trace"/> (capture everything).</param>
    /// <returns>The capture: use it as the factory, and its <see cref="LogCapture.Collector"/> for assertions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public static LogCapture Create(TimeProvider timeProvider, LogLevel minimumLevel = LogLevel.Trace)
        => CreateFor(CollectorOn(timeProvider), minimumLevel);

    /// <summary>Creates a collector that stamps records with <paramref name="timeProvider"/>'s time.</summary>
    /// <param name="timeProvider">The clock of the record timestamps.</param>
    /// <returns>The collector.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    internal static FakeLogCollector CollectorOn(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        return FakeLogCollector.Create(new FakeLogCollectorOptions { TimeProvider = timeProvider });
    }

    /// <summary>Wraps <paramref name="collector"/> in a capture at <paramref name="minimumLevel"/> and registers the
    /// floor.</summary>
    /// <param name="collector">The collector of the capture.</param>
    /// <param name="minimumLevel">The minimum level to capture.</param>
    /// <returns>The capture.</returns>
    private static LogCapture CreateFor(FakeLogCollector collector, LogLevel minimumLevel)
    {
        var capture = LogCapture.Create(collector, b =>
        {
            b.SetMinimumLevel(minimumLevel);
            b.AddProvider(new FakeLoggerProvider(collector));
        });

        // Record the capture floor so an assertion can detect a vacuous check at a level this
        // collector never captured (for example HasNotLogged().AtLevel(Trace) under a higher floor).
        LogCaptureFloorRegistry.Register(collector, minimumLevel);
        return capture;
    }

    /// <summary>
    /// Creates an <see cref="ILoggerFactory"/> whose loggers are enabled at <paramref name="minimumLevel"/> and above
    /// but keep nothing. Use it where a test needs a real, enabled logger and does not assert on logs: code guarded by
    /// <c>logger.IsEnabled(...)</c> takes its logging branch (so coverage includes it), and a long-lived host does not
    /// accumulate records the way a <see cref="FakeLogCollector"/> would.
    /// </summary>
    /// <remarks>
    /// A <c>NullLogger</c> or a factory without providers reports every level as disabled, so the guarded branch never
    /// runs under test. To assert on what was logged, use <see cref="Create(LogLevel)"/> instead.
    /// <para>
    /// <c>BeginScope</c> returns a shared scope whose disposal does nothing, never <see langword="null"/>, as
    /// <c>NullLogger</c> does: code that requires a scope instance runs as it does under a production logger.
    /// </para>
    /// </remarks>
    /// <param name="minimumLevel">The lowest level reported as enabled. Default is <see cref="LogLevel.Trace"/>
    /// (every level).</param>
    /// <returns>The factory. The caller owns it and disposes it when the test completes.</returns>
    public static ILoggerFactory CreateDiscarding(LogLevel minimumLevel = LogLevel.Trace)
        => LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(minimumLevel);
            // Registered through a factory, so the logging container owns the provider and disposes it with the
            // returned factory, like the providers it creates itself.
            b.Services.AddSingleton<ILoggerProvider>(static _ => new DiscardingLoggerProvider());
        });
}
