namespace Smoke.Consumer;

/// <summary>A consumer-style log definition for the contract renderer smoke test.</summary>
internal static partial class SmokeLogMessages
{
    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "worker {Name} ready")]
    public static partial void Ready(ILogger logger, string name);
}
