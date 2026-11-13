using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// One test's log capture: a logger factory whose records go to <see cref="Collector"/>. The capture is the factory
/// itself, so it can be passed wherever an <see cref="ILoggerFactory"/> is expected, and one <see langword="using"/>
/// disposes everything. Created by <see cref="LogCollectorBuilder.Create(LogLevel)"/> (and, in the TUnit adapter, by
/// <c>TestOutputLogCollectorBuilder.CreateTeed</c>).
/// </summary>
/// <example>
/// <code>
/// using var logs = LogCollectorBuilder.Create();
/// var service = new OrderService(logs.CreateLogger&lt;OrderService&gt;());
/// service.Ship(42);
/// await Assert.That(logs.Collector).HasLogged().Matching(OrderShipped).Once();
/// </code>
/// </example>
/// <remarks>
/// Disposing the capture disposes the factory and its providers; it is safe to call more than once. The collector
/// keeps the records it already holds, so assertions can still run after disposal.
/// </remarks>
public sealed class LogCapture : ILoggerFactory, IAsyncDisposable
{
    private readonly ILoggerFactory _factory;

    /// <summary>Initializes the capture over a factory that writes into <paramref name="collector"/>. Only the
    /// builders create instances.</summary>
    /// <param name="factory">The factory, wired to <paramref name="collector"/>; the capture owns it.</param>
    /// <param name="collector">The collector that receives the factory's records.</param>
    internal LogCapture(ILoggerFactory factory, FakeLogCollector collector)
    {
        _factory = factory;
        Collector = collector;
    }

    /// <summary>Builds a factory with <paramref name="configure"/> and wraps it with <paramref name="collector"/>.</summary>
    /// <param name="collector">The collector the configured providers write into.</param>
    /// <param name="configure">Configures the logging builder of the factory.</param>
    /// <returns>The capture, which owns the factory.</returns>
    internal static LogCapture Create(FakeLogCollector collector, Action<ILoggingBuilder> configure)
        => new(LoggerFactory.Create(configure), collector);

    /// <summary>Gets the collector that receives every record logged through this capture's loggers.</summary>
    public FakeLogCollector Collector { get; }

    /// <summary>Creates a logger whose records go to <see cref="Collector"/>.</summary>
    /// <param name="categoryName">The category name for the logger's records.</param>
    /// <returns>The logger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="categoryName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The capture was disposed.</exception>
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        return _factory.CreateLogger(categoryName);
    }

    /// <summary>Adds a provider to the capture's factory. Disposing the capture also disposes a provider added this
    /// way, as <see cref="LoggerFactory.AddProvider"/> does.</summary>
    /// <param name="provider">The provider to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The capture was disposed.</exception>
    public void AddProvider(ILoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _factory.AddProvider(provider);
    }

    /// <summary>Disposes the factory and the providers it owns. The collector keeps its records.</summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>Disposes the factory and the providers it owns, as <see cref="Dispose"/> does; for
    /// <see langword="await"/> <see langword="using"/> and fixtures that are disposed asynchronously.</summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }
}
