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
}
