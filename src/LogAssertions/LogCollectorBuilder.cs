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
        return (factory, collector);
    }
}
