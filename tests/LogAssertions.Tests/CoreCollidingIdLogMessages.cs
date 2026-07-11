using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>Carries a definition whose numeric event ID collides with
/// <see cref="CoreTestLogMessages.SameIdFirst"/> but whose template differs, pinning that
/// identity matching requires the template and never accepts ID equality alone (the
/// no-explicit-EventId consumer constraint). Lives in its own type because the generator
/// rejects duplicate IDs within one type (LOGGEN002).</summary>
internal static partial class CoreCollidingIdLogMessages
{
    [LoggerMessage(EventId = 403, Level = LogLevel.Information, Message = "same id second {Value}")]
    public static partial void SameIdSecond(ILogger logger, string value);
}
