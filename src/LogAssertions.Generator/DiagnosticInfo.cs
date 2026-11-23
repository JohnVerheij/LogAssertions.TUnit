using System;
using System.Collections.Generic;
using System.Linq;

namespace LogAssertions.Generator;

/// <summary>One diagnostic to report: its descriptor's ID, where (the opt-in attribute of the assembly it concerns), and
/// its message arguments.</summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
    /// <summary>Initializes the diagnostic.</summary>
    /// <param name="id">The descriptor's ID.</param>
    /// <param name="location">Where to report it, or <see langword="null"/> for no location.</param>
    /// <param name="arguments">The message arguments.</param>
    internal DiagnosticInfo(string id, LocationInfo? location, params string[] arguments)
    {
        Id = id;
        Location = location;
        Arguments = arguments;
    }

    /// <summary>The descriptor's ID.</summary>
    internal string Id { get; }

    /// <summary>Where to report it, or <see langword="null"/> for no location.</summary>
    internal LocationInfo? Location { get; }

    /// <summary>The message arguments.</summary>
    internal IReadOnlyList<string> Arguments { get; }

    /// <inheritdoc/>
    public bool Equals(DiagnosticInfo? other)
        => other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Equals(Location, other.Location)
        && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as DiagnosticInfo);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
}
