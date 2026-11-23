using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// The identity of one log definition: event ID (numeric ID plus name), message template, placeholder names, and
/// level. A definition is a reusable, string-free assertion value for every record a logging method writes, whatever
/// its argument values. Take it from the generated definitions (<see cref="GenerateLogDefinitionsAttribute"/>), or
/// capture it with <see cref="Capture"/>, and match it on the assertion chain via <c>Matching(definition)</c> or with
/// <see cref="LogFilter.Matching(LogDefinition)"/>.
/// </summary>
/// <example>
/// <code>
/// [assembly: GenerateLogDefinitions(typeof(OrderService))]
///
/// await Assert.That(collector).HasLogged().Matching(OrderServiceLogDefinitions.LogOrderShipped).Once();
/// </code>
/// Without the generator, capture the definition once with throwaway argument values:
/// <code>
/// private static readonly LogDefinition OrderShipped =
///     LogDefinition.Capture(log => LogMessages.OrderShipped(log, 0, ""));
/// </code>
/// Identity matching keys on the event ID (numeric ID plus name) and the message template, never on the argument
/// values or the level. To also pin argument values, chain <c>WithProperty</c>, or use the exact-call form
/// (<c>MatchingCall</c> on the chain, or <see cref="LogFilter.MatchingCall"/> with a <see cref="LogCall"/>).
/// </example>
/// <remarks>
/// <para>
/// Identity requires the message template to match; two definitions that share a numeric event ID are still
/// distinguished by their templates. The residual ambiguity: two <c>[LoggerMessage]</c> methods with the same method
/// name (in different classes, both with generator-assigned IDs) and the same template produce log records that are
/// indistinguishable by design, because the records themselves carry identical identity.
/// </para>
/// <para>
/// A generated and a captured definition of the same <c>[LoggerMessage]</c> method carry the same identity: the event ID
/// (the attribute's <c>EventId</c>, or the number Microsoft's logging generator derives from the event name when it is
/// omitted), the event name, and the template, so they match the same records. Their placeholder names agree too, apart
/// from the limit the README lists for Microsoft.Extensions.Telemetry's tag attributes.
/// </para>
/// <para>
/// An empty template is no template: Microsoft's two logging generators write a method without a message as an empty
/// template and as none, and a definition matches both.
/// </para>
/// </remarks>
public sealed class LogDefinition
{
    /// <summary>The state key that carries the message template.</summary>
    internal const string OriginalFormatKey = "{OriginalFormat}";

    /// <summary>The placeholder names, in the order the definition was built with.</summary>
    private readonly string[] _placeholderNames;

    /// <summary>Initializes the identity. Only the <c>Create</c> methods and <see cref="LogCall.Capture"/> create instances.</summary>
    /// <param name="id">The event ID (numeric ID plus name).</param>
    /// <param name="level">The level, or <see langword="null"/> when the logging method takes it as a parameter.</param>
    /// <param name="template">The message template, or <see langword="null"/> when the call carries none.</param>
    /// <param name="placeholderNames">The placeholder names.</param>
    private LogDefinition(EventId id, LogLevel? level, string? template, string[] placeholderNames)
    {
        Id = id;
        Level = level;
        Template = template;
        _placeholderNames = placeholderNames;
    }

    /// <summary>The event ID: numeric ID plus event name. For a <c>[LoggerMessage]</c> definition the name is the
    /// attribute's <c>EventName</c> or else the method name, and the numeric ID is the attribute's <c>EventId</c> or else
    /// the value Microsoft's logging generator derives from that name.</summary>
    public EventId Id { get; }

    /// <summary>The level the definition logs at, or <see langword="null"/> when a generated definition's logging
    /// method takes the level as a parameter. A captured definition holds the level of the capture call. Not part of
    /// identity matching, so level constraints belong on the chain (<c>AtLevel</c> etc.).</summary>
    public LogLevel? Level { get; }

    /// <summary>The message template (the pre-substitution <c>{OriginalFormat}</c> value), or
    /// <see langword="null"/> when the call carries none or an empty one.</summary>
    public string? Template { get; }

    /// <summary>The names of the placeholder values a record of this definition carries, <c>{OriginalFormat}</c>
    /// excluded, in the record's order: a captured definition lists the keys the logging code stored; a generated one the
    /// names Microsoft's logging generator gives the method's values, mostly the spelling of the template placeholder
    /// that uses a value, else the parameter's own name (the README gives the rules); one made with
    /// <see cref="Create(EventId, LogLevel?, string?)"/> the template's placeholders in order of first
    /// appearance.</summary>
    public IReadOnlyList<string> PlaceholderNames => _placeholderNames;

    /// <summary>
    /// Creates a definition from its parts, for a record whose logging code a test cannot call. The placeholder names are
    /// read from <paramref name="template"/>; the generated definitions (<see cref="GenerateLogDefinitionsAttribute"/>)
    /// use the overload that takes them.
    /// </summary>
    /// <param name="id">The event ID (numeric ID plus name) the records carry.</param>
    /// <param name="level">The level the records carry, or <see langword="null"/> when it varies per call.</param>
    /// <param name="template">The message template the records carry, or <see langword="null"/> for none.</param>
    /// <returns>The definition.</returns>
    public static LogDefinition Create(EventId id, LogLevel? level, string? template)
        => new(id, level, NoneIfEmpty(template), MessageTemplate.PlaceholderNames(template));

    /// <summary>
    /// Creates a definition from its parts, with the placeholder names its records carry. The generated definitions
    /// use it, since a <c>[LoggerMessage]</c> record also carries the parameters its template does not use.
    /// </summary>
    /// <param name="id">The event ID (numeric ID plus name) the records carry.</param>
    /// <param name="level">The level the records carry, or <see langword="null"/> when it varies per call.</param>
    /// <param name="template">The message template the records carry, or <see langword="null"/> for none.</param>
    /// <param name="placeholderNames">The names of the placeholder values the records carry, in order. Must be non-null
    /// and contain no <see langword="null"/>.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="placeholderNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="placeholderNames"/> contains a <see langword="null"/>.</exception>
    public static LogDefinition Create(EventId id, LogLevel? level, string? template, IEnumerable<string> placeholderNames)
    {
        ArgumentNullException.ThrowIfNull(placeholderNames);
        string[] names = [.. placeholderNames];
        if (Array.Exists(names, static name => name is null))
            throw new ArgumentException("The placeholder names must not contain null.", nameof(placeholderNames));

        return new(id, level, NoneIfEmpty(template), names);
    }

    /// <summary>
    /// Invokes <paramref name="invocation"/> once against a private probe logger and returns the identity of the call.
    /// The argument values of the call are throwaway; keep them with <see cref="LogCall.Capture"/> instead.
    /// </summary>
    /// <param name="invocation">A delegate that performs exactly one log call on the supplied logger: typically a
    /// <c>[LoggerMessage]</c> method invocation with throwaway argument values. Must be non-null.</param>
    /// <returns>The captured identity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="invocation"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="invocation"/> logged zero records (nothing to capture) or
    /// more than one record (ambiguous: pass a lambda that emits exactly one log call).</exception>
    public static LogDefinition Capture(Action<ILogger> invocation)
        => LogCall.Capture(invocation).Definition;

    /// <summary>Creates the identity of a captured record.</summary>
    /// <param name="record">The captured record.</param>
    /// <param name="properties">The record's placeholder key/value pairs, <c>{OriginalFormat}</c> excluded.</param>
    /// <returns>The identity.</returns>
    internal static LogDefinition FromRecord(FakeLogRecord record, KeyValuePair<string, string?>[] properties)
    {
        var names = new string[properties.Length];
        for (var i = 0; i < properties.Length; i++)
        {
            names[i] = properties[i].Key;
        }

        return new(record.Id, record.Level, TemplateOf(record), names);
    }

    /// <summary>Returns the template a record carries, or <see langword="null"/> for none or an empty one.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The template.</returns>
    internal static string? TemplateOf(FakeLogRecord record) => NoneIfEmpty(RecordState.Value(record, OriginalFormatKey));

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="record"/> carries this definition's identity: equal numeric
    /// event ID, equal event name (ordinal), and equal message template (ordinal). Argument values, level, exception,
    /// category, and scopes are not compared.
    /// </summary>
    /// <param name="record">The record to test.</param>
    /// <returns><see langword="true"/> when the record was produced by this definition.</returns>
    internal bool MatchesIdentity(FakeLogRecord record)
        => record.Id.Id == Id.Id
        && string.Equals(record.Id.Name, Id.Name, StringComparison.Ordinal)
        && string.Equals(TemplateOf(record), Template, StringComparison.Ordinal);

    /// <summary>Returns <see langword="null"/> for an empty template: no template.</summary>
    /// <param name="template">The template.</param>
    /// <returns>The template, or <see langword="null"/>.</returns>
    private static string? NoneIfEmpty(string? template) => string.IsNullOrEmpty(template) ? null : template;

    /// <summary>Renders the identity as <c>Name#Id "template"</c> (name falls back to <c>?</c> when the record carried
    /// none), for failure-message descriptions and diagnostics.</summary>
    /// <returns>The rendered identity.</returns>
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}#{1} \"{2}\"",
            Id.Name ?? "?",
            Id.Id,
            Template ?? "<no template>");
}
