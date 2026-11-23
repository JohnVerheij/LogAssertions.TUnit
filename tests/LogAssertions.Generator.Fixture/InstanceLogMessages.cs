using Microsoft.Extensions.Logging;

#if EXTENSIONS_FIXTURE
namespace LogAssertions.Generator.Fixture.Extensions;
#else
namespace LogAssertions.Generator.Fixture;
#endif

/// <summary>An instance logging class with the logger from its primary constructor.</summary>
/// <param name="logger">The logger.</param>
internal sealed partial class InstanceLogMessages(ILogger logger)
{
    /// <summary>An instance method.</summary>
    /// <param name="count">The count.</param>
    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Started {Count}")]
    internal partial void Started(int count);
}
