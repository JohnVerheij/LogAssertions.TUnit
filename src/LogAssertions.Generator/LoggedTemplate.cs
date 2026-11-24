using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LogAssertions.Generator;

/// <summary>
/// The text a record carries for a message or an event name the attribute sets. Microsoft's generators paste the event
/// name between quotes as it is, and the generator of Microsoft.Extensions.Logging.Abstractions before 11.0 copies the
/// message into a C# string literal escaping only quotes and line breaks; either way a backslash escape in it
/// (<c>\t</c>, <c>\\</c>, <c>\"</c> in a name) takes effect when the product compiles. Parsing the same literal with
/// the C# parser gives the text the product logs. Later versions of that generator, and the generator of
/// Microsoft.Extensions.Telemetry, log the message as written.
/// </summary>
internal static class LoggedTemplate
{
    /// <summary>Returns the template a record of a method with <paramref name="message"/> carries.</summary>
    /// <param name="message">The message as the attribute sets it, not empty.</param>
    /// <param name="product">How the product's logging generator writes records.</param>
    /// <returns>The logged template.</returns>
    internal static string Of(string message, ProductLogging product)
    {
        if (product.LogsMessageAsWritten || message.IndexOf('\\') < 0)
            return message;

        return Parsed(
            message
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\u0085", "\\u0085")
                .Replace("\u2028", "\\u2028")
                .Replace("\u2029", "\\u2029"),
            message);
    }

    /// <summary>Returns the event name a record carries for an event name the attribute sets: both generators, in every
    /// version, paste it between quotes as it is.</summary>
    /// <param name="eventName">The event name as the attribute sets it.</param>
    /// <returns>The logged event name.</returns>
    internal static string EventName(string eventName)
        => eventName.IndexOf('\\') < 0 ? eventName : Parsed(eventName, eventName);

    /// <summary>Returns the value of a C# string literal with the given content, as the C# compiler reads it.</summary>
    /// <param name="content">The literal's content, between its quotes.</param>
    /// <param name="fallback">The text to return when the literal does not parse: such a product cannot have
    /// compiled.</param>
    /// <returns>The value, or <paramref name="fallback"/>.</returns>
    private static string Parsed(string content, string fallback)
        => SyntaxFactory.ParseExpression("\"" + content + "\"") is LiteralExpressionSyntax parsed && !parsed.ContainsDiagnostics
            ? parsed.Token.ValueText
            : fallback;
}
