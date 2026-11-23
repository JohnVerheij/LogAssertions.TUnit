using System.Collections.Immutable;

namespace LogAssertions.Generator;

/// <summary>The definition of one <c>[LoggerMessage]</c> method, read from its attribute.</summary>
internal sealed class MethodDefinition
{
    /// <summary>Initializes the definition.</summary>
    /// <param name="methodName">The method name.</param>
    /// <param name="eventId">The event ID: the attribute's, or the one Microsoft's generator derives from the name.</param>
    /// <param name="eventName">The event name: the attribute's, or the method name.</param>
    /// <param name="level">The level, or <see langword="null"/> when the attribute sets none (a level parameter).</param>
    /// <param name="template">The message template, or <see langword="null"/> when the method has no message.</param>
    /// <param name="placeholderNames">The names of the placeholder values its records carry.</param>
    internal MethodDefinition(string methodName, int eventId, string eventName, int? level, string? template, ImmutableArray<string> placeholderNames)
    {
        MethodName = methodName;
        EventId = eventId;
        EventName = eventName;
        Level = level;
        Template = template;
        PlaceholderNames = placeholderNames;
    }

    /// <summary>The method name.</summary>
    internal string MethodName { get; }

    /// <summary>The event ID: the attribute's, or the one Microsoft's generator derives from the name.</summary>
    internal int EventId { get; }

    /// <summary>The event name: the attribute's, or the method name.</summary>
    internal string EventName { get; }

    /// <summary>The level, or <see langword="null"/> when the attribute sets none (a level parameter).</summary>
    internal int? Level { get; }

    /// <summary>The message template, or <see langword="null"/> when the method has no message.</summary>
    internal string? Template { get; }

    /// <summary>The names of the placeholder values its records carry.</summary>
    internal ImmutableArray<string> PlaceholderNames { get; }
}
