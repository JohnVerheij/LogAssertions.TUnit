using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LogAssertions.Generator;

/// <summary>
/// The names of the placeholder values a record of a <c>[LoggerMessage]</c> method carries, in the order the record
/// carries them: one per value parameter, under the spelling of the template placeholder that uses it, else its own
/// name. A placeholder uses a parameter when their names are equal ignoring case and a leading <c>@</c> (with
/// Microsoft.Extensions.Telemetry also ignoring the spaces around a placeholder's name). The logger, the level and the
/// exception parameters are values only where the product's generator logs them: when the template names them, the
/// exception not with Microsoft.Extensions.Telemetry (see <see cref="ProductLogging.Telemetry"/>), and a level parameter
/// always with Microsoft.Extensions.Logging.Abstractions when the attribute sets the level. The generator of
/// Microsoft.Extensions.Logging.Abstractions writes a method through <c>LoggerMessage.Define</c> when the attribute sets
/// the level, there are at most six values, and the template's placeholders, every occurrence counted, name them in
/// parameter order; otherwise through a state type of its own, which names a value after the placeholder spelled like
/// the parameter's code name ignoring case, else after that code name (see <see cref="CodeName"/>).
/// </summary>
internal static class MethodPlaceholders
{
    /// <summary>The attribute a data classification derives from.</summary>
    private const string ClassificationAttribute = "global::Microsoft.Extensions.Compliance.Classification.DataClassificationAttribute";

    /// <summary>The most values <c>LoggerMessage.Define</c> takes.</summary>
    private const int MaxDefineValues = 6;

    /// <summary>Returns the placeholder names of a method's records.</summary>
    /// <param name="method">The method.</param>
    /// <param name="template">Its message template as written, or <see langword="null"/> for none.</param>
    /// <param name="attributeSetsLevel">Whether the attribute sets the level.</param>
    /// <param name="product">How the product's logging generator writes records.</param>
    /// <returns>The names.</returns>
    internal static ImmutableArray<string> Of(IMethodSymbol method, string? template, bool attributeSetsLevel, ProductLogging product)
    {
        var occurrences = TemplateNames(template, trim: product.Telemetry);
        var placeholders = occurrences.Distinct(StringComparer.Ordinal).ToList();
        var values = Values(method, placeholders, attributeSetsLevel, product);
        if (product.Telemetry)
            return [.. values.Select(static v => v.Placeholder ?? v.Name).Reverse()];

        if (UsesDefine(values, occurrences, attributeSetsLevel))
            return [.. occurrences];

        return [.. values.Select(v => placeholders.Find(p => string.Equals(p, v.CodeName, StringComparison.OrdinalIgnoreCase)) ?? v.CodeName)];
    }

    /// <summary>Returns the placeholder names of a template in order, every occurrence: <c>{Name}</c>,
    /// <c>{Name:format}</c>, or <c>{Name,alignment}</c>. An escaped brace (<c>{{</c>) starts no placeholder, and an
    /// unclosed brace ends the scan. The rules are those of the core's template reader.</summary>
    /// <param name="template">The template, or <see langword="null"/>.</param>
    /// <param name="trim">Whether to trim the spaces around a name, as the generator of Microsoft.Extensions.Telemetry
    /// does (<c>{ X }</c> is <c>X</c>).</param>
    /// <returns>The names.</returns>
    internal static List<string> TemplateNames(string? template, bool trim)
    {
        var names = new List<string>();
        if (template is null)
            return names;

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

            names.Add(NameBetween(template, open, close, trim));
            index = close + 1;
        }

        return names;
    }

    /// <summary>Returns a method's value parameters in parameter order, each with its code name and the template
    /// placeholder that uses it. The first logger parameter, the first level parameter (with
    /// Microsoft.Extensions.Logging.Abstractions only when the attribute sets no level) and the first exception parameter
    /// are values only when the template names them, and an exception never with Microsoft.Extensions.Telemetry, which
    /// also leaves out a data-classified
    /// parameter (it logs one for redaction). That generator renames a parameter with
    /// <c>[TagName]</c> and expands one with <c>[LogProperties]</c> or <c>[TagProvider]</c>. <c>[TagName]</c> and
    /// <c>[TagProvider]</c> are not compiled into the product, and the properties <c>[LogProperties]</c> expands to are not
    /// modeled, so such a parameter keeps its own name here.</summary>
    /// <param name="method">The method.</param>
    /// <param name="placeholders">The template's distinct placeholders.</param>
    /// <param name="attributeSetsLevel">Whether the attribute sets the level.</param>
    /// <param name="product">How the product's logging generator writes records.</param>
    /// <returns>The values.</returns>
    private static List<(string Name, string CodeName, string? Placeholder)> Values(
        IMethodSymbol method, List<string> placeholders, bool attributeSetsLevel, ProductLogging product)
    {
        var telemetry = product.Telemetry;
        var values = new List<(string Name, string CodeName, string? Placeholder)>();
        var roles = new ParameterRoles(levelTaken: !telemetry && attributeSetsLevel);
        foreach (var parameter in method.Parameters)
        {
            var placeholder = placeholders.Find(p => string.Equals(WithoutAt(p), parameter.Name, StringComparison.OrdinalIgnoreCase));
            if (IsValue(roles.Next(parameter.Type), placeholder is not null, telemetry) && !(telemetry && IsClassified(parameter)))
                values.Add((parameter.Name, CodeName(parameter, product), placeholder));
        }

        return values;
    }

    /// <summary>
    /// Returns the name a state type of the product's generator looks a parameter up by: with
    /// Microsoft.Extensions.Logging.Abstractions 8.0 and later, the name as the code writes it, so with its leading
    /// <c>@</c> when it has one (<c>@event</c>); otherwise the parameter's name. A compiled assembly does not record the
    /// <c>@</c>: a parameter of a referenced assembly counts as written with it when its name is a reserved keyword, which
    /// C# allows only so; one of the test project's own assembly as its declaration writes it.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <param name="product">How the product's logging generator writes records.</param>
    /// <returns>The code name.</returns>
    private static string CodeName(IParameterSymbol parameter, ProductLogging product)
        => product.KeepsVerbatimAt && IsWrittenWithAt(parameter) ? "@" + parameter.Name : parameter.Name;

    /// <summary>
    /// Returns whether a parameter is written with a leading <c>@</c>. In source, as its identifier token is written
    /// (also with a Unicode escape after the <c>@</c>, as in <c>@\u0076alue</c>), read from the syntax tree the
    /// compilation holds. From metadata, and for a tree whose root is not at hand (an IDE can drop one), when its name is
    /// a reserved keyword, which C# allows only with the <c>@</c>.
    /// </summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns><see langword="true"/> for a verbatim name.</returns>
    private static bool IsWrittenWithAt(IParameterSymbol parameter)
    {
        foreach (var location in parameter.Locations)
        {
            if (location.SourceTree?.TryGetRoot(out var root) is true)
                return root.FindToken(location.SourceSpan.Start).Text.StartsWith("@", StringComparison.Ordinal);
        }

        return SyntaxFacts.GetKeywordKind(parameter.Name) is not SyntaxKind.None;
    }

    /// <summary>Returns whether a parameter is a value: an ordinary parameter always; the logger and the level when the
    /// template names them; the exception when the template names it, except with Microsoft.Extensions.Telemetry.</summary>
    /// <param name="role">The parameter's role.</param>
    /// <param name="named">Whether a template placeholder uses the parameter.</param>
    /// <param name="telemetry">Whether the generator of Microsoft.Extensions.Telemetry compiled the product.</param>
    /// <returns><see langword="true"/> for a value.</returns>
    private static bool IsValue(ParameterRole role, bool named, bool telemetry) => role switch
    {
        ParameterRole.Logger or ParameterRole.Level => named,
        ParameterRole.Exception => named && !telemetry,
        _ => true,
    };

    /// <summary>Returns whether the generator of Microsoft.Extensions.Logging.Abstractions writes the method through
    /// <c>LoggerMessage.Define</c>: the attribute sets the level, there are at most six values, and the template's
    /// placeholders, every occurrence counted, name exactly them, in parameter order. A repeated placeholder therefore
    /// leads to the generator's own state type.</summary>
    /// <param name="values">The values.</param>
    /// <param name="occurrences">The template's placeholders, every occurrence.</param>
    /// <param name="attributeSetsLevel">Whether the attribute sets the level.</param>
    /// <returns><see langword="true"/> for the <c>Define</c> path.</returns>
    private static bool UsesDefine(List<(string Name, string CodeName, string? Placeholder)> values, List<string> occurrences, bool attributeSetsLevel)
        => attributeSetsLevel
        && values.Count <= MaxDefineValues
        && occurrences.Count == values.Count
        && values.Select((v, i) => string.Equals(WithoutAt(occurrences[i]), v.Name, StringComparison.OrdinalIgnoreCase)).All(static same => same);

    /// <summary>Returns the name of the placeholder between two braces: the text up to a <c>,</c> alignment or a
    /// <c>:</c> format.</summary>
    /// <param name="template">The template.</param>
    /// <param name="open">The index of the opening brace.</param>
    /// <param name="close">The index of the closing brace.</param>
    /// <param name="trim">Whether to trim the spaces around the name.</param>
    /// <returns>The name.</returns>
    private static string NameBetween(string template, int open, int close, bool trim)
    {
        var nameEnd = template.IndexOfAny([',', ':'], open + 1, close - open - 1);
        var name = template.Substring(open + 1, (nameEnd < 0 ? close : nameEnd) - open - 1);
        return trim ? name.Trim() : name;
    }

    /// <summary>Returns whether a parameter carries a data classification (an attribute derived from
    /// <c>DataClassificationAttribute</c>): the generator of Microsoft.Extensions.Telemetry logs such a value as a
    /// classified tag for redaction, which the record's key/value state does not carry.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns><see langword="true"/> for a classified parameter.</returns>
    private static bool IsClassified(IParameterSymbol parameter)
        => parameter.GetAttributes().Any(static a => DerivesFrom(a.AttributeClass, ClassificationAttribute));

    /// <summary>Returns whether a type is, or derives from, the type with the given fully qualified name.</summary>
    /// <param name="type">The type, when it resolved.</param>
    /// <param name="fullName">The fully qualified name, with the <c>global::</c> prefix.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool DerivesFrom(INamedTypeSymbol? type, string fullName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), fullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Returns a placeholder name without a leading <c>@</c>.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The name without the <c>@</c>.</returns>
    private static string WithoutAt(string name)
        => name.Length > 0 && name[0] is '@' ? name.Substring(1) : name;
}
