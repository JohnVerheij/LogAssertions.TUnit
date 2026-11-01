using System;
using Microsoft.Extensions.Logging;

namespace LogAssertions;

/// <summary>
/// A logger provider whose loggers are enabled for every level but keep nothing: code guarded by
/// <c>logger.IsEnabled(...)</c> runs its logging branch, and no record is retained. Behind
/// <see cref="LogCollectorBuilder.CreateDiscarding"/>.
/// </summary>
internal sealed class DiscardingLoggerProvider : ILoggerProvider
{
    /// <summary>Gets the shared instance; the provider holds no state.</summary>
    public static DiscardingLoggerProvider Instance { get; } = new();

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

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Discarded on purpose: the logging branch runs, nothing is kept.
        }
    }
}
