using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace LogAssertions.Generator;

/// <summary>Where a diagnostic is reported: the file, span, and line span of an opt-in attribute, kept as values so a
/// result does not hold on to a compilation and two equal results compare equal.</summary>
internal sealed class LocationInfo : IEquatable<LocationInfo>
{
    /// <summary>Initializes the location.</summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="textSpan">The span in the file.</param>
    /// <param name="lineSpan">The line span in the file.</param>
    private LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
    {
        FilePath = filePath;
        TextSpan = textSpan;
        LineSpan = lineSpan;
    }

    /// <summary>The file path.</summary>
    internal string FilePath { get; }

    /// <summary>The span in the file.</summary>
    internal TextSpan TextSpan { get; }

    /// <summary>The line span in the file.</summary>
    internal LinePositionSpan LineSpan { get; }

    /// <summary>Returns the location of an attribute's syntax, or <see langword="null"/> when it has none.</summary>
    /// <param name="syntax">The attribute's syntax reference.</param>
    /// <returns>The location, or <see langword="null"/>.</returns>
    internal static LocationInfo? Of(SyntaxReference? syntax)
    {
        if (syntax is null)
            return null;

        var location = syntax.SyntaxTree.GetLocation(syntax.Span);
        var lineSpan = location.GetLineSpan();
        return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    /// <summary>Returns the source location among <paramref name="sources"/> with the file and span of
    /// <paramref name="location"/>, so the compiler can apply a <c>#pragma</c> to a diagnostic reported there; else a
    /// location by file and span, or <see cref="Location.None"/> for none.</summary>
    /// <param name="location">The location, or <see langword="null"/>.</param>
    /// <param name="sources">The source locations of the assembly attributes.</param>
    /// <returns>The Roslyn location.</returns>
    internal static Location Resolve(LocationInfo? location, ImmutableArray<Location> sources)
    {
        if (location is null)
            return Location.None;

        return sources.FirstOrDefault(source => source.SourceSpan.Equals(location.TextSpan)
                && string.Equals(source.GetLineSpan().Path, location.FilePath, StringComparison.Ordinal))
            ?? Location.Create(location.FilePath, location.TextSpan, location.LineSpan);
    }

    /// <inheritdoc/>
    public bool Equals(LocationInfo? other)
        => other is not null
        && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal)
        && TextSpan.Equals(other.TextSpan)
        && LineSpan.Equals(other.LineSpan);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as LocationInfo);

    /// <inheritdoc/>
    public override int GetHashCode() => TextSpan.GetHashCode();
}
