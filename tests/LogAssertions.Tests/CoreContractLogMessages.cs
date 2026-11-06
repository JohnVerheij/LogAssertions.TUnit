using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>A definition that shares its event ID with <see cref="CoreTestLogMessages.OrderProcessed"/>, for the
/// contract renderer's ordering by name (the generator rejects two methods with one ID in a type).</summary>
internal static partial class CoreContractLogMessages
{
    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug, Message = "{B} then {A}")]
    public static partial void Reversed(ILogger logger, string a, string b);
}
