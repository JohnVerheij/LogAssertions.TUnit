using System;
using System.Collections.Immutable;
using System.Linq;

namespace LogAssertions.Generator;

/// <summary>What the generator adds to a compilation: the source files and the diagnostics. Equal results compare equal,
/// so the incremental pipeline skips the output step when a compilation change leaves the result unchanged.</summary>
internal sealed class GenerationResult : IEquatable<GenerationResult>
{
    /// <summary>The result of a compilation that opts in no assembly.</summary>
    internal static readonly GenerationResult Empty = new([], []);

    /// <summary>Initializes the result.</summary>
    /// <param name="sources">The source files.</param>
    /// <param name="diagnostics">The diagnostics.</param>
    internal GenerationResult(ImmutableArray<GeneratedSource> sources, ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Sources = sources;
        Diagnostics = diagnostics;
    }

    /// <summary>The source files.</summary>
    internal ImmutableArray<GeneratedSource> Sources { get; }

    /// <summary>The diagnostics.</summary>
    internal ImmutableArray<DiagnosticInfo> Diagnostics { get; }

    /// <inheritdoc/>
    public bool Equals(GenerationResult? other)
        => other is not null
        && Sources.SequenceEqual(other.Sources)
        && Diagnostics.SequenceEqual(other.Diagnostics);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GenerationResult);

    /// <inheritdoc/>
    public override int GetHashCode() => (Sources.Length * 397) ^ Diagnostics.Length;
}
