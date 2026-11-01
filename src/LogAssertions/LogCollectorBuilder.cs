using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Convenience factory that constructs a <see cref="FakeLogCollector"/> together with an
/// <see cref="ILoggerFactory"/> that writes into it. Replaces the 3-4 lines of boilerplate
/// every test would otherwise need.
/// </summary>
/// <example>
/// <code>
/// var (factory, collector) = LogCollectorBuilder.Create();
/// using (factory)
/// {
///     var logger = factory.CreateLogger("MyCategory");
///     logger.LogInformation("hello");
///     await Assert.That(collector).HasLogged().Containing("hello", StringComparison.Ordinal).Once();
/// }
/// </code>
/// </example>
public static class LogCollectorBuilder
{
    /// <summary>
    /// Creates a new <see cref="FakeLogCollector"/> and an <see cref="ILoggerFactory"/> wired
    /// to it. The caller owns both: dispose the factory when the test completes; the collector
    /// is GC-managed and has no unmanaged resources.
    /// </summary>
    /// <param name="minimumLevel">The minimum level to capture. Default is <see cref="LogLevel.Trace"/> (capture everything).</param>
    /// <returns>The wired pair: <c>Factory</c> for creating loggers, <c>Collector</c> for assertions.</returns>
    public static (ILoggerFactory Factory, FakeLogCollector Collector) Create(LogLevel minimumLevel = LogLevel.Trace)
    {
        FakeLogCollector collector = new();
        ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(minimumLevel);
            b.AddProvider(new FakeLoggerProvider(collector));
        });

        // Record the capture floor so an assertion can detect a vacuous check at a level this
        // collector never captured (for example HasNotLogged().AtLevel(Trace) under a higher floor).
        LogCaptureFloorRegistry.Register(collector, minimumLevel);
        return (factory, collector);
    }

    /// <summary>
    /// Creates an <see cref="ILoggerFactory"/> whose loggers are enabled at <paramref name="minimumLevel"/> and above
    /// but keep nothing. Use it where a test needs a real, enabled logger and does not assert on logs: code guarded by
    /// <c>logger.IsEnabled(...)</c> takes its logging branch (so coverage includes it), and a long-lived host does not
    /// accumulate records the way a <see cref="FakeLogCollector"/> would.
    /// </summary>
    /// <remarks>
    /// A <c>NullLogger</c> or a factory without providers reports every level as disabled, so the guarded branch never
    /// runs under test. To assert on what was logged, use <see cref="Create"/> instead.
    /// </remarks>
    /// <param name="minimumLevel">The lowest level reported as enabled. Default is <see cref="LogLevel.Trace"/>
    /// (every level).</param>
    /// <returns>The factory. The caller owns it and disposes it when the test completes.</returns>
    public static ILoggerFactory CreateDiscarding(LogLevel minimumLevel = LogLevel.Trace)
        => LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(minimumLevel);
            b.AddProvider(DiscardingLoggerProvider.Instance);
        });
}
