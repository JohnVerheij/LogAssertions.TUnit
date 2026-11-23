using System;

namespace LogAssertions.Generator;

/// <summary>One generated source file: its hint name and its text.</summary>
internal sealed class GeneratedSource : IEquatable<GeneratedSource>
{
    /// <summary>Initializes the file.</summary>
    /// <param name="hintName">The hint name, unique per compilation.</param>
    /// <param name="text">The source text.</param>
    internal GeneratedSource(string hintName, string text)
    {
        HintName = hintName;
        Text = text;
    }

    /// <summary>The hint name, unique per compilation.</summary>
    internal string HintName { get; }

    /// <summary>The source text.</summary>
    internal string Text { get; }

    /// <inheritdoc/>
    public bool Equals(GeneratedSource? other)
        => other is not null
        && string.Equals(HintName, other.HintName, StringComparison.Ordinal)
        && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as GeneratedSource);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(HintName);
}
