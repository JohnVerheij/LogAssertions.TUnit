using System;
using Microsoft.Extensions.Logging;

#if EXTENSIONS_FIXTURE
namespace LogAssertions.Generator.Fixture.Extensions;
#else
namespace LogAssertions.Generator.Fixture;
#endif

/// <summary>Every shape of <c>[LoggerMessage]</c> attribute the generator reads, one method each.</summary>
internal static partial class FixtureLogMessages
{
    /// <summary>Event ID, level, and message through the constructor.</summary>
    [LoggerMessage(1001, LogLevel.Information, "Order {OrderId} shipped to {City}")]
    internal static partial void ConstructorArguments(ILogger logger, int orderId, string city);

    /// <summary>Event ID, level, and message as named arguments.</summary>
    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Retry {Attempt} of {Max}")]
    internal static partial void NamedArguments(ILogger logger, int attempt, int max);

    /// <summary>No event ID and no event name: Microsoft's generator derives the ID from the method name.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Derived from the method name {Value}")]
    internal static partial void DerivedFromMethodName(ILogger logger, string value);

    /// <summary>An event name and no event ID: the ID is derived from the event name.</summary>
    [LoggerMessage(EventName = "CustomEventName", Level = LogLevel.Error, Message = "Derived from the event name {Value}")]
    internal static partial void DerivedFromEventName(ILogger logger, string value);

    /// <summary>An event ID and an event name.</summary>
    [LoggerMessage(EventId = 1003, EventName = "BothSet", Level = LogLevel.Debug, Message = "Both {Value}")]
    internal static partial void EventIdAndEventName(ILogger logger, string value);

    /// <summary>The level as a method parameter.</summary>
    [LoggerMessage(EventId = 1004, Message = "The level is a parameter {Value}")]
    internal static partial void LevelParameter(ILogger logger, LogLevel level, string value);

    /// <summary>Level and message through the constructor, no event ID.</summary>
    [LoggerMessage(LogLevel.Critical, "Level and message {Value}")]
    internal static partial void LevelAndMessageConstructor(ILogger logger, string value);

    /// <summary>No message.</summary>
    [LoggerMessage(EventId = 1005, Level = LogLevel.Information)]
    internal static partial void NoMessage(ILogger logger);

    /// <summary>An empty message.</summary>
    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "")]
    internal static partial void EmptyMessage(ILogger logger);

    /// <summary>No placeholders.</summary>
    [LoggerMessage(EventId = 1006, Level = LogLevel.Trace, Message = "Plain text")]
    internal static partial void NoPlaceholders(ILogger logger);

    /// <summary>A format, an alignment, and escaped braces.</summary>
    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "{{literal}} {Amount:N2} {Name,10}")]
    internal static partial void FormatAlignmentAndEscapes(ILogger logger, decimal amount, string name);

    /// <summary>A placeholder whose case differs from its parameter, and characters a C# literal escapes. No backslash:
    /// the generator of Microsoft.Extensions.Logging.Abstractions copies it into its own literal unescaped.</summary>
    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Path \"{FilePath}\"\tnot found \u00e9")]
    internal static partial void CaseAndEscapedCharacters(ILogger logger, string filePath);

    /// <summary>An exception parameter.</summary>
    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "Operation {Name} failed")]
    internal static partial void WithException(ILogger logger, Exception exception, string name);

    /// <summary>More than six placeholders: Microsoft's generator writes its own state type.</summary>
    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "{A} {B} {C} {D} {E} {F} {G}")]
    internal static partial void SevenPlaceholders(ILogger logger, int a, int b, int c, int d, int e, int f, int g);

    /// <summary>A method whose name is a contextual keyword.</summary>
    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Contextual {Value}")]
    internal static partial void @async(ILogger logger, string value);

    /// <summary>An empty event name: the method name is used.</summary>
    [LoggerMessage(EventName = "", Level = LogLevel.Information, Message = "Empty event name")]
    internal static partial void EmptyEventName(ILogger logger);

    /// <summary>A whitespace event name: the method name is used.</summary>
    [LoggerMessage(EventName = " ", Level = LogLevel.Information, Message = "Whitespace event name")]
    internal static partial void WhitespaceEventName(ILogger logger);

    // SYSLIB1015 (a parameter the message does not use) is the shape under test in the next two methods: the generator of
    // Microsoft.Extensions.Logging.Abstractions still logs such a parameter, under its own name.
#pragma warning disable SYSLIB1015

    /// <summary>A parameter the template does not use, next to one it uses.</summary>
    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Extra {Used}")]
    internal static partial void UnusedParameter(ILogger logger, int used, int unused);

    /// <summary>No message, with a parameter.</summary>
    [LoggerMessage(EventId = 1014, Level = LogLevel.Information)]
    internal static partial void NoMessageWithParameter(ILogger logger, int value);

    /// <summary>Placeholders in another order than their parameters, and two unused parameters around them.</summary>
    [LoggerMessage(EventId = 1016, Level = LogLevel.Information, Message = "Order {B} then {A}")]
    internal static partial void TwoUnusedParameters(ILogger logger, int z, int a, int b, int y);

    /// <summary>A level parameter next to a level in the attribute: the parameter is a value.</summary>
    [LoggerMessage(EventId = 1017, Level = LogLevel.Information, Message = "Level value {X}")]
    internal static partial void LevelValueParameter(ILogger logger, int x, LogLevel other);
#pragma warning restore SYSLIB1015

    /// <summary>A backslash escape and an escaped backslash in the message: the generator of
    /// Microsoft.Extensions.Logging.Abstractions copies the message into its own literal without escaping a backslash,
    /// so its record carries a tab and one backslash.</summary>
    [LoggerMessage(EventId = 1018, Level = LogLevel.Information, Message = "Tab\\tand backslash\\\\ {X}")]
    internal static partial void Backslashes(ILogger logger, int x);

    /// <summary>An <c>@</c> placeholder that <c>LoggerMessage.Define</c> writes (level in the attribute, values in
    /// template order): the record keeps the <c>@</c>.</summary>
    [LoggerMessage(EventId = 1020, Level = LogLevel.Information, Message = "Destructured {@Item} and {Count}")]
    internal static partial void AtDefine(ILogger logger, string item, int count);

    /// <summary>An <c>@</c> placeholder written through the generator's own state type (the level is a parameter): the
    /// record names the value after the parameter.</summary>
    [LoggerMessage(EventId = 1021, Message = "Dynamic {@Request}")]
    internal static partial void AtDynamic(ILogger logger, LogLevel level, string request);

    // The next two name the level and the exception in the template, which both Microsoft generators warn against
    // (SYSLIB1002, SYSLIB1013, LOGGEN000, LOGGEN009); products still compile with it, and the records then carry them as
    // values, apart from the exception with Microsoft.Extensions.Telemetry.
#pragma warning disable SYSLIB1002, SYSLIB1013, LOGGEN000, LOGGEN009

    /// <summary>The level parameter named in the template.</summary>
    [LoggerMessage(EventId = 1022, Message = "At {level} for {A}")]
    internal static partial void LevelInTemplate(ILogger logger, LogLevel level, int a);

    /// <summary>The exception parameter named in the template.</summary>
    [LoggerMessage(EventId = 1023, Level = LogLevel.Error, Message = "Failed {ex} for {Id}")]
    internal static partial void ExceptionInTemplate(ILogger logger, int id, Exception ex);
#pragma warning restore SYSLIB1002, SYSLIB1013, LOGGEN000, LOGGEN009

    /// <summary>A backslash escape in the event name: both generators copy the event name into their own literal without
    /// escaping a backslash, so the record's name carries a tab.</summary>
    [LoggerMessage(EventId = 1024, EventName = "Back\\tslash", Level = LogLevel.Information, Message = "Backslash name")]
    internal static partial void BackslashEventName(ILogger logger);

    /// <summary>An escaped backslash in the event name, with a derived event ID: the ID is the hash of the name as
    /// written, the record's name has one backslash.</summary>
    [LoggerMessage(EventName = "Back\\\\two", Level = LogLevel.Information, Message = "Backslash name two")]
    internal static partial void BackslashEventNameDerived(ILogger logger);

    /// <summary>An escaped quote in the event name: both generators paste the name between quotes as it is, so the
    /// record's name carries plain quotes.</summary>
    [LoggerMessage(EventId = 1027, EventName = "Say \\\"hi\\\"", Level = LogLevel.Information, Message = "Quoted name")]
    internal static partial void QuotedEventName(ILogger logger);

#if EXTENSIONS_FIXTURE
    /// <summary>Tag names of Microsoft.Extensions.Telemetry: one the template uses, one it does not.</summary>
    [LoggerMessage(EventId = 1019, Level = LogLevel.Information, Message = "Tagged {custom}")]
    internal static partial void TagNames(ILogger logger, [TagName("custom")] string value, [TagName("other.name")] string second);

    /// <summary>Spaces inside placeholders: Microsoft.Extensions.Telemetry trims them (the generator of
    /// Microsoft.Extensions.Logging.Abstractions rejects such a template).</summary>
    [LoggerMessage(EventId = 1025, Level = LogLevel.Information, Message = "Spaces { X } {Y }")]
    internal static partial void SpacedPlaceholders(ILogger logger, int x, int y);

    /// <summary>A data-classified parameter: Microsoft.Extensions.Telemetry logs it as a classified tag for redaction,
    /// which the record's key/value state does not carry.</summary>
    [LoggerMessage(EventId = 1026, Level = LogLevel.Information, Message = "User {UserId} from {City}")]
    internal static partial void ClassifiedParameter(ILogger logger, [PrivateData] string userId, string city);
#endif

    /// <summary>A nested type.</summary>
    internal static partial class Nested
    {
        /// <summary>A method of a nested type.</summary>
        [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Nested {Value}")]
        internal static partial void InNestedType(ILogger logger, string value);
    }
}
