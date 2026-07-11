using System;
using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>LoggerMessage definitions for the core-suite definition tests.</summary>
internal static partial class CoreTestLogMessages
{
    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Order {OrderId} placed by {Customer}")]
    public static partial void OrderPlaced(ILogger logger, int orderId, string customer);

    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "plain")]
    public static partial void Plain(ILogger logger);

    [LoggerMessage(EventId = 402, Level = LogLevel.Error, Message = "failed hard")]
    public static partial void Failed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 403, Level = LogLevel.Information, Message = "same id first {Value}")]
    public static partial void SameIdFirst(ILogger logger, string value);
}
