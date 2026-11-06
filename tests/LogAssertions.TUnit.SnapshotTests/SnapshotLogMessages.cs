using Microsoft.Extensions.Logging;

namespace LogAssertions.TUnit.SnapshotTests;

/// <summary>The log definitions of the contract pinned by <see cref="LogContractRendererSnapshotTests"/>.</summary>
internal static partial class SnapshotLogMessages
{
    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Order {OrderId} processed in {DurationMs} ms")]
    public static partial void OrderProcessed(ILogger logger, int orderId, long durationMs);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Order {OrderId} shipped to {City}")]
    public static partial void OrderShipped(ILogger logger, int orderId, string city);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Payment for order {OrderId} failed")]
    public static partial void PaymentFailed(ILogger logger, int orderId);
}
