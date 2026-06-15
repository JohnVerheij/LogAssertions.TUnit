using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit;

/// <summary>
/// TUnit-specific counterpart to <see cref="LogCollectorBuilder"/> that, in addition to capturing
/// records into a <see cref="FakeLogCollector"/> for assertions, mirrors each record live to the
/// active TUnit test's output writer. Use this when you want the test's logs to appear inline in the
/// per-test report; use the core <see cref="LogCollectorBuilder.Create(LogLevel)"/> (no tee) for
/// log-heavy soak tests where buffering every record in the per-test output is undesirable.
/// </summary>
public static class TestOutputLogCollectorBuilder
{
    /// <summary>
    /// Creates a <see cref="FakeLogCollector"/> and an <see cref="ILoggerFactory"/> wired to it, plus
    /// a provider that writes each record to <c>TestContext.Current.Output</c> as it is logged. The
    /// caller owns both: dispose the factory when the test completes.
    /// </summary>
    /// <param name="minimumLevel">The minimum level to capture (and tee). Default is
    /// <see cref="LogLevel.Trace"/> (everything).</param>
    /// <returns>The wired pair: <c>Factory</c> for creating loggers, <c>Collector</c> for assertions.</returns>
    public static (ILoggerFactory Factory, FakeLogCollector Collector) CreateTeed(LogLevel minimumLevel = LogLevel.Trace)
    {
        FakeLogCollector collector = new();

        // Delegates to AddTeedFakeLogging so the tuple-shaped and builder-shaped helpers share one wiring
        // definition: capture provider, capture-floor registration, and the built-in test-output tee.
        ILoggerFactory factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector, minimumLevel));
        return (factory, collector);
    }
}
