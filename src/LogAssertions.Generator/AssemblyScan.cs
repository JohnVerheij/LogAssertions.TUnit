using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LogAssertions.Generator;

/// <summary>The state of scanning one opted-in assembly: how its logging generator writes records, where its opt-in
/// attribute is, the definitions found, and the diagnostics of the whole run.</summary>
internal sealed class AssemblyScan
{
    /// <summary>Initializes the scan.</summary>
    /// <param name="product">How the assembly's logging generator writes records.</param>
    /// <param name="own">Whether the assembly is the test project itself.</param>
    /// <param name="friend">Whether the assembly names the test project in <c>InternalsVisibleTo</c>.</param>
    /// <param name="location">The location of the opt-in attribute.</param>
    /// <param name="diagnostics">Receives the diagnostics.</param>
    internal AssemblyScan(ProductLogging product, bool own, bool friend, LocationInfo? location, ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        Product = product;
        Own = own;
        Friend = friend;
        Location = location;
        Diagnostics = diagnostics;
    }

    /// <summary>How the assembly's logging generator writes records.</summary>
    internal ProductLogging Product { get; }

    /// <summary>Whether the assembly is the test project itself, which sees all of its own members.</summary>
    internal bool Own { get; }

    /// <summary>Whether the assembly names the test project in <c>InternalsVisibleTo</c>.</summary>
    internal bool Friend { get; }

    /// <summary>The location of the opt-in attribute.</summary>
    internal LocationInfo? Location { get; }

    /// <summary>Receives the diagnostics.</summary>
    internal ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

    /// <summary>The definitions found, one per type with <c>[LoggerMessage]</c> methods (possibly none left after
    /// LOGA002 and LOGA004).</summary>
    internal List<TypeDefinitions> Found { get; } = [];

    /// <summary>Returns whether the test project sees a type or method of the assembly: what the compiler imports from a
    /// reference to it (public and protected members, internal ones for a friend). A reference assembly or a compiled
    /// one holds no more than that; a project reference in an IDE holds everything, so the generator applies the same
    /// rule and the editor sees the definitions the build sees.</summary>
    /// <param name="symbol">The type or method.</param>
    /// <returns><see langword="true"/> when the test project sees it.</returns>
    internal bool Sees(ISymbol symbol) => Own || symbol.DeclaredAccessibility switch
    {
        Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
        Accessibility.Internal or Accessibility.ProtectedAndInternal => Friend,
        _ => false,
    };
}
