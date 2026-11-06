using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LogAssertions.Render;

/// <summary>
/// Pure renderer that writes a service's log contract (the event IDs, names, levels, message templates, and
/// placeholder names of its log definitions) as deterministic text, one line per definition:
/// <code>
/// 1201 OrderProcessed Information "Order {OrderId} processed in {DurationMs} ms" OrderId,DurationMs
/// </code>
/// Dashboards, log parsers, and analysis scripts read exactly these parts; composed with <c>MatchesSnapshot()</c>, the
/// contract becomes a reviewed baseline, and a renamed placeholder or a reworded template shows as a diff.
/// </summary>
/// <example>
/// <code>
/// var contract = LogContractRenderer.Render([OrderProcessed, OrderShipped, PaymentFailed]);
/// await Assert.That(contract).MatchesSnapshot();
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Format.</b> Each line is the numeric event ID, the event name (<c>?</c> when the definition has none), the level,
/// the message template in double quotes (<c>&lt;no template&gt;</c> when the call carried none), and the placeholder
/// names in template order, separated by commas, with <c>{OriginalFormat}</c> left out. A definition without
/// placeholders ends after the template. In the template, <c>\</c>, <c>"</c>, carriage return, line feed, and tab are
/// written as <c>\\</c>, <c>\"</c>, <c>\r</c>, <c>\n</c>, and <c>\t</c>, so every definition stays on one line.
/// </para>
/// <para>
/// <b>Deterministic.</b> Lines are ordered by event ID, then event name, then template (both ordinal), then level,
/// whatever the input order. Every line ends with the literal LF byte (<c>'\n'</c>), never
/// <see cref="Environment.NewLine"/>, so a baseline committed on one operating system stays valid on every other. There
/// is no header line, as with <see cref="LogSnapshotRenderer"/>; a change to the format is a listed breaking change.
/// </para>
/// <para>
/// <b>Sources.</b> The renderer reads only <see cref="LogDefinition.Id"/>, <see cref="LogDefinition.Level"/>,
/// <see cref="LogDefinition.Template"/>, and <see cref="LogDefinition.Properties"/>, with no reflection. Capture each
/// definition with <see cref="LogDefinition.Capture"/>; the argument values of the capture are not rendered.
/// </para>
/// </remarks>
public static class LogContractRenderer
{
    /// <summary>Renders one line per definition, ordered by event ID, name, template, and level.</summary>
    /// <param name="definitions">The definitions of the contract. Must not contain <see langword="null"/>.</param>
    /// <returns>The contract: one LF-terminated line per definition, or <see cref="string.Empty"/> when there are none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definitions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="definitions"/> contains a <see langword="null"/>
    /// definition.</exception>
    public static string Render(IEnumerable<LogDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        LogDefinition[] items = [.. definitions];
        if (Array.Exists(items, static definition => definition is null))
        {
            throw new ArgumentException("The definitions must not contain null.", nameof(definitions));
        }

        StringBuilder sb = new();
        foreach (var definition in items
            .OrderBy(static d => d.Id.Id)
            .ThenBy(static d => d.Id.Name, StringComparer.Ordinal)
            .ThenBy(static d => d.Template, StringComparer.Ordinal)
            .ThenBy(static d => d.Level))
        {
            AppendLine(sb, definition);
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, LogDefinition definition)
    {
        sb.Append(definition.Id.Id.ToString(CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(definition.Id.Name ?? "?")
            .Append(' ')
            .Append(definition.Level.ToString())
            .Append(' ');

        if (definition.Template is null)
        {
            sb.Append("<no template>");
        }
        else
        {
            sb.Append('"');
            AppendEscaped(sb, definition.Template);
            sb.Append('"');
        }

        var first = true;
        foreach (var name in PlaceholderNames(definition))
        {
            sb.Append(first ? ' ' : ',').Append(name);
            first = false;
        }

        sb.Append('\n');
    }

    /// <summary>The definition's placeholder names (its <see cref="LogDefinition.Properties"/> keys) in template
    /// order. The source generator stores them in another order, so they are ordered by where each first appears in
    /// the template; a name the template does not contain keeps its captured position after those.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The names in template order.</returns>
    private static IEnumerable<string> PlaceholderNames(LogDefinition definition)
    {
        var positions = TemplatePositions(definition.Template ?? string.Empty);
        return definition.Properties
            .Select(static property => property.Key)
            .OrderBy(name => positions.TryGetValue(name, out var position) ? position : int.MaxValue);
    }

    /// <summary>Finds the position of the first occurrence of each placeholder in a message template: <c>{Name}</c>,
    /// <c>{Name:format}</c>, or <c>{Name,alignment}</c>; an escaped brace (<c>{{</c>) starts no placeholder.</summary>
    /// <param name="template">The message template.</param>
    /// <returns>The placeholder names mapped to their order of first appearance.</returns>
    private static Dictionary<string, int> TemplatePositions(string template)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        while (template.IndexOf('{', index) is var open and >= 0)
        {
            if (open + 1 < template.Length && template[open + 1] is '{')
            {
                index = open + 2;
                continue;
            }

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
                break;

            var nameEnd = template.IndexOfAny([',', ':'], open + 1, close - open - 1);
            positions.TryAdd(template[(open + 1)..(nameEnd < 0 ? close : nameEnd)], positions.Count);
            index = close + 1;
        }

        return positions;
    }

    private static void AppendEscaped(StringBuilder sb, string template)
    {
        foreach (var c in template)
        {
            _ = c switch
            {
                '\\' => sb.Append(@"\\"),
                '"' => sb.Append("\\\""),
                '\r' => sb.Append(@"\r"),
                '\n' => sb.Append(@"\n"),
                '\t' => sb.Append(@"\t"),
                _ => sb.Append(c),
            };
        }
    }
}
