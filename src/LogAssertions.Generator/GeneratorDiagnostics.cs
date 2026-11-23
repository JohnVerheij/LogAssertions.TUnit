using Microsoft.CodeAnalysis;

namespace LogAssertions.Generator;

/// <summary>The diagnostics the generator reports. Their IDs are listed in AnalyzerReleases.Shipped.md.</summary>
internal static class GeneratorDiagnostics
{
    /// <summary>The category of every diagnostic.</summary>
    private const string Category = "LogAssertions";

    /// <summary>An opted-in assembly has no <c>[LoggerMessage]</c> method the test project can see.</summary>
    internal static readonly DiagnosticDescriptor NoVisibleMethods = new(
        id: "LOGA001",
        title: "No [LoggerMessage] method is visible",
        messageFormat: "No [LoggerMessage] method of assembly '{0}' is visible to this project, so no log definitions are generated for it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A test project compiles against the reference assembly of the product, which has no private members. Make the log methods internal and name this project in the product assembly's InternalsVisibleTo.");

    /// <summary>A type has two <c>[LoggerMessage]</c> methods with the same name.</summary>
    internal static readonly DiagnosticDescriptor DuplicateMethodName = new(
        id: "LOGA002",
        title: "Two [LoggerMessage] methods share a name",
        messageFormat: "Type '{0}' has more than one [LoggerMessage] method named '{1}', so no log definition is generated for them",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A generated definition is named after its method. Give each [LoggerMessage] method of a type its own name.");

    /// <summary>A generated name is already taken: a type the compilation sees has the generated class's name, or a
    /// method has it.</summary>
    internal static readonly DiagnosticDescriptor NameTaken = new(
        id: "LOGA004",
        title: "A generated name is taken",
        messageFormat: "The generated name '{0}' is already taken, so no log definitions are generated for '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A generated class is named after its type with LogDefinitions appended, and its fields after the methods. A type that already has the class's name, or a method named like the class, would not compile.");

    /// <summary>Two types would generate the same class.</summary>
    internal static readonly DiagnosticDescriptor DuplicateClassName = new(
        id: "LOGA003",
        title: "Two types would generate the same class",
        messageFormat: "Types '{0}' and '{1}' would both generate class '{2}', so no log definitions are generated for them",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two opted-in assemblies have a type of the same name in the same namespace, or two names join to the same generated class name.");
}
