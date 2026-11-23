using System;
using System.Collections.Generic;

namespace LogAssertions;

/// <summary>
/// Reads the placeholders of a message template: <c>{Name}</c>, <c>{Name:format}</c>, or <c>{Name,alignment}</c>. An
/// escaped brace (<c>{{</c>) starts no placeholder, and an unclosed brace ends the scan.
/// </summary>
internal static class MessageTemplate
{
    /// <summary>Finds the position of the first occurrence of each placeholder in a message template.</summary>
    /// <param name="template">The message template.</param>
    /// <returns>The placeholder names mapped to their order of first appearance.</returns>
    internal static Dictionary<string, int> Positions(string template)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        while (IndexOf(template, '{', index) is var open and >= 0)
        {
            if (open + 1 < template.Length && template[open + 1] is '{')
            {
                index = open + 2;
                continue;
            }

            var close = IndexOf(template, '}', open + 1);
            if (close < 0)
                break;

            var nameEnd = template.IndexOfAny([',', ':'], open + 1, close - open - 1);
            positions.TryAdd(template[(open + 1)..(nameEnd < 0 ? close : nameEnd)], positions.Count);
            index = close + 1;
        }

        return positions;
    }

    /// <summary>Returns the placeholder names of a message template in order of first appearance, each once.</summary>
    /// <param name="template">The message template, or <see langword="null"/> for none.</param>
    /// <returns>The names; empty when the template is <see langword="null"/> or has no placeholders.</returns>
    internal static string[] PlaceholderNames(string? template)
    {
        if (template is null)
            return [];

        var positions = Positions(template);
        var names = new string[positions.Count];
        foreach (var (name, position) in positions)
        {
            names[position] = name;
        }

        return names;
    }

    /// <summary>Finds a character from a start index, ordinally. The span form compiles on every target framework: the
    /// string overload that takes a start index gained a comparison parameter in .NET 11, and the analyzers then ask for it.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="value">The character to find.</param>
    /// <param name="startIndex">The index to start at.</param>
    /// <returns>The index of the character in <paramref name="text"/>, or -1.</returns>
    private static int IndexOf(string text, char value, int startIndex)
    {
        var found = text.AsSpan(startIndex).IndexOf(value);
        return found < 0 ? -1 : startIndex + found;
    }
}
