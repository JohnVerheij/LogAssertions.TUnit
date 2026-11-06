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

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Order {OrderId} processed in {DurationMs} ms")]
    public static partial void OrderProcessed(ILogger logger, int orderId, long durationMs);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Warning, Message = "Say \"hi\" at C:\\path\r\n\tnow")]
    public static partial void Escaped(ILogger logger);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Paid {Amount:N2} {{not a placeholder}} to {Payee,10}, {Amount:N0} in total")]
    public static partial void Paid(ILogger logger, string payee, decimal amount);
}
