using System;
using Microsoft.Extensions.Logging;

namespace LogAssertions;

/// <summary>
/// A logger provider whose loggers are enabled for every level but keep nothing: code guarded by
/// <c>logger.IsEnabled(...)</c> runs its logging branch, no record is retained, and a scope is a shared instance
/// whose disposal does nothing. Behind <see cref="LogCollectorBuilder.CreateDiscarding"/>, which creates one per
/// factory; the factory disposes it.
/// </summary>
internal sealed class DiscardingLoggerProvider : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => DiscardingLogger.Shared;

    /// <inheritdoc/>
    public void Dispose()
    {
        // Stateless: nothing to release.
    }

    /// <summary>A logger that reports every level as enabled and discards every record.</summary>
    private sealed class DiscardingLogger : ILogger
    {
        /// <summary>Gets the shared instance; the logger holds no state.</summary>
        public static DiscardingLogger Shared { get; } = new();

        /// <summary>Begins a scope that keeps nothing.</summary>
        /// <typeparam name="TState">The type of the scope state.</typeparam>
        /// <param name="state">The scope state; discarded.</param>
        /// <returns>The shared <see cref="NoOpScope"/>, never <see langword="null"/>: the same as <c>NullLogger</c>,
        /// so code that requires a scope instance runs as it does under a production logger.</returns>
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoOpScope.Instance;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Discarded on purpose: the logging branch runs, nothing is kept.
        }
    }

    /// <summary>A logging scope that holds nothing, so disposing it releases nothing.</summary>
    private sealed class NoOpScope : IDisposable
    {
        /// <summary>Gets the shared instance; the scope holds no state.</summary>
        public static NoOpScope Instance { get; } = new();

        /// <inheritdoc/>
        public void Dispose()
        {
            // The scope state was discarded with the records; nothing to release.
        }
    }
}
