using Microsoft.Extensions.Logging;

namespace LogAssertions.TUnit.Tests;

/// <summary>First half of the cross-class identity-collision pair: same method name and same
/// template as <c>CollisionMessagesB</c>, both with generator-assigned event IDs, so the two
/// definitions produce records with identical identity (documented residual ambiguity).</summary>
internal static partial class CollisionMessagesA
{
    [LoggerMessage(Level = LogLevel.Information, Message = "collision {Value}")]
    public static partial void CollisionSample(ILogger logger, string value);
}
