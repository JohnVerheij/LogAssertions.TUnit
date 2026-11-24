using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LogAssertions.Generator;

/// <summary>
/// Generates a <c>LogDefinition</c> for each <c>[LoggerMessage]</c> method of every assembly a test project names in
/// <c>[assembly: GenerateLogDefinitions(typeof(T))]</c>. It reads the attributes through Roslyn symbols at compile time
/// and writes each definition as literals; the generated code uses no reflection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LogDefinitionGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var result = context.CompilationProvider.Select(static (compilation, cancellationToken)
            => DefinitionScanner.Scan(compilation, cancellationToken));

        // The source locations of the assembly attributes, so a diagnostic is reported in source at its opt-in attribute,
        // where a #pragma can suppress it. Only an edit to a file with an assembly attribute changes them.
        var attributes = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is AttributeSyntax { Parent: AttributeListSyntax { Target: { } target } }
                && target.Identifier.IsKind(SyntaxKind.AssemblyKeyword),
            static (syntax, _) => syntax.Node.GetLocation()).Collect();
        context.RegisterSourceOutput(result.Combine(attributes), static (production, input) => Emit(production, input.Left, input.Right));
    }

    /// <summary>Adds the generated sources and reports the diagnostics of one result.</summary>
    /// <param name="production">The production context.</param>
    /// <param name="generation">The result.</param>
    /// <param name="attributes">The source locations of the assembly attributes.</param>
    private static void Emit(SourceProductionContext production, GenerationResult generation, ImmutableArray<Location> attributes)
    {
        foreach (var source in generation.Sources)
        {
            production.AddSource(source.HintName, source.Text);
        }

        foreach (var diagnostic in generation.Diagnostics)
        {
            production.ReportDiagnostic(Diagnostic.Create(
                Descriptor(diagnostic.Id),
                LocationInfo.Resolve(diagnostic.Location, attributes),
                diagnostic.Arguments.Cast<object>().ToArray()));
        }
    }

    /// <summary>Returns the descriptor with the given ID.</summary>
    /// <param name="id">The ID.</param>
    /// <returns>The descriptor.</returns>
    private static DiagnosticDescriptor Descriptor(string id) => id switch
    {
        "LOGA001" => GeneratorDiagnostics.NoVisibleMethods,
        "LOGA002" => GeneratorDiagnostics.DuplicateMethodName,
        "LOGA003" => GeneratorDiagnostics.DuplicateClassName,
        _ => GeneratorDiagnostics.NameTaken,
    };
}
