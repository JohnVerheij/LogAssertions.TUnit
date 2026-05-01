using System;
using Microsoft.Extensions.Logging;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// LoggerMessage source-generator definitions for all test seed logging.
/// Centralising the message templates here satisfies CA1848 (no
/// allocating ILogger extension calls) and keeps test files readable.
/// </summary>
internal static partial class TestLogMessages
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Trace, Message = "Trace message")]
    public static partial void TraceSample(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Debug message")]
    public static partial void DebugSample(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Started processing")]
    public static partial void StartedProcessing(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "validation failed: TimeoutMs out of range")]
    public static partial void ValidationFailed(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Operation failed")]
    public static partial void OperationFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "first")]
    public static partial void First(ILogger logger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "second")]
    public static partial void Second(ILogger logger);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "third")]
    public static partial void Third(ILogger logger);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Item {ItemId} failed")]
    public static partial void ItemFailed(ILogger logger, string itemId);

    [LoggerMessage(EventId = 100, EventName = "Bootstrap", Level = LogLevel.Information, Message = "App started")]
    public static partial void AppStarted(ILogger logger);

    [LoggerMessage(EventId = 200, EventName = "Shutdown", Level = LogLevel.Information, Message = "App stopped")]
    public static partial void AppStopped(ILogger logger);

    [LoggerMessage(EventId = 50, Level = LogLevel.Information, Message = "Cycle {CycleNumber} started")]
    public static partial void CycleStarted(ILogger logger, int cycleNumber);

    [LoggerMessage(EventId = 51, Level = LogLevel.Warning, Message = "Cycle {CycleNumber} validation failed")]
    public static partial void CycleValidationFailed(ILogger logger, int cycleNumber);

    [LoggerMessage(EventId = 52, Level = LogLevel.Information, Message = "Cycle {CycleNumber} finished")]
    public static partial void CycleFinished(ILogger logger, int cycleNumber);

    /// <summary>
    /// Allocation-free factory for a formatted-template scope of shape <c>"Order {OrderId}"</c>.
    /// Used by tests that need to exercise the message-template scope idiom without tripping
    /// CA1848 (which fires on the allocating <see cref="LoggerExtensions.BeginScope(ILogger, string, object?[])"/>
    /// extension).
    /// </summary>
    public static readonly Func<ILogger, int, IDisposable?> OrderScope =
        LoggerMessage.DefineScope<int>("Order {OrderId}");
}

