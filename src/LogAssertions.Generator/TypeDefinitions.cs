using System.Collections.Immutable;

namespace LogAssertions.Generator;

/// <summary>The definitions of one type, and the name and namespace of the class generated for them.</summary>
internal sealed class TypeDefinitions
{
    /// <summary>Initializes the definitions.</summary>
    /// <param name="ns">The namespace as C# writes it, or <see langword="null"/> for the global namespace.</param>
    /// <param name="hintNamespace">The namespace without keyword escapes, for the hint name, or <see langword="null"/>.</param>
    /// <param name="className">The generated class name.</param>
    /// <param name="typeDisplay">The type, as C# displays it.</param>
    /// <param name="methods">The definitions, ordered by method name.</param>
    /// <param name="optInLocation">The location of the opt-in attribute of the type's assembly.</param>
    internal TypeDefinitions(
        string? ns, string? hintNamespace, string className, string typeDisplay, ImmutableArray<MethodDefinition> methods, LocationInfo? optInLocation)
    {
        OptInLocation = optInLocation;
        Namespace = ns;
        HintNamespace = hintNamespace;
        ClassName = className;
        TypeDisplay = typeDisplay;
        Methods = methods;
    }

    /// <summary>The namespace as C# writes it, or <see langword="null"/> for the global namespace.</summary>
    internal string? Namespace { get; }

    /// <summary>The namespace without keyword escapes, for the hint name, or <see langword="null"/>.</summary>
    internal string? HintNamespace { get; }

    /// <summary>The generated class name.</summary>
    internal string ClassName { get; }

    /// <summary>The type, as C# displays it.</summary>
    internal string TypeDisplay { get; }

    /// <summary>The definitions, ordered by method name.</summary>
    internal ImmutableArray<MethodDefinition> Methods { get; }

    /// <summary>The location of the opt-in attribute of the type's assembly, for its diagnostics.</summary>
    internal LocationInfo? OptInLocation { get; }
}
