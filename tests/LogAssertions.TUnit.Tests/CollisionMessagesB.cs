using Microsoft.Extensions.Logging;

namespace LogAssertions.TUnit.Tests;

/// <summary>Second half of the cross-class identity-collision pair. See <c>CollisionMessagesA</c>.</summary>
internal static partial class CollisionMessagesB
{
    [LoggerMessage(Level = LogLevel.Information, Message = "collision {Value}")]
    public static partial void CollisionSample(ILogger logger, string value);
}
