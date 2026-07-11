using Microsoft.Extensions.Logging;

namespace LogAssertions.TUnit.Tests;

/// <summary>Generic host carrying a definition, mirroring definitions nested in generic
/// production classes; the capture lambda closes the generic parameter. The payload parameter
/// is typed by the class's type parameter so different closings format different values while
/// sharing one definition identity.</summary>
/// <typeparam name="T">The payload type formatted into the message.</typeparam>
internal static partial class GenericHostMessages<T>
{
    [LoggerMessage(EventId = 303, Level = LogLevel.Information, Message = "generic {Payload} ready")]
    public static partial void GenericSample(ILogger logger, T payload);
}
