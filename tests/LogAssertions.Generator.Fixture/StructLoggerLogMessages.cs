using Microsoft.Extensions.Logging;

#if EXTENSIONS_FIXTURE
namespace LogAssertions.Generator.Fixture.Extensions;
#else
namespace LogAssertions.Generator.Fixture;
#endif

/// <summary>A struct logger before the logger parameter.</summary>
internal static partial class StructLoggerLogMessages
{
    // SYSLIB1015: the struct parameter is not in the template on purpose (see ConstrainedLogMessages).
#pragma warning disable SYSLIB1015

    /// <summary>The struct is a value; the second parameter is the logger.</summary>
    [LoggerMessage(EventId = 1404, Level = LogLevel.Information, Message = "Struct first {X}")]
    internal static partial void StructFirst(StructLogger value, ILogger logger, int x);
#pragma warning restore SYSLIB1015
}
