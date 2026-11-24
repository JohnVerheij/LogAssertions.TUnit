using Microsoft.Extensions.Logging;

#if EXTENSIONS_FIXTURE
namespace LogAssertions.Generator.Fixture.Extensions;
#else
namespace LogAssertions.Generator.Fixture;
#endif

/// <summary>A generic type.</summary>
/// <typeparam name="T">Any type.</typeparam>
internal static partial class GenericLogMessages<T>
{
    /// <summary>A method of a generic type.</summary>
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Generic {Value}")]
    internal static partial void InGenericType(ILogger logger, T value);
}
