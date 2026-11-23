using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using LogAssertions.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LogAssertions.Tests;

/// <summary>Compiles product assemblies to metadata and runs <see cref="LogDefinitionGenerator"/> on a consumer that
/// references them, as a test project references a product.</summary>
internal static class GeneratorHarness
{
    /// <summary>The assembly name of every consumer compilation; product sources name it in
    /// <c>InternalsVisibleTo</c>.</summary>
    internal const string ConsumerName = "Consumer";

    /// <summary>The assemblies the test runs with, which include the logging abstractions, the core, and the fixture;
    /// the test assembly itself is left out, as a consumer may take its name.</summary>
    private static readonly MetadataReference[] PlatformReferences =
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(static path => !string.Equals(Path.GetFileName(path), "LogAssertions.Tests.dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))];

    /// <summary>Compiles a product assembly and returns it as metadata, the way a consumer sees it.</summary>
    /// <param name="assemblyName">The assembly name.</param>
    /// <param name="source">The source.</param>
    /// <returns>The metadata reference.</returns>
    internal static MetadataReference Product(string assemblyName, string source)
        => Product(assemblyName, source, PlatformReferences);

    /// <summary>Compiles a stand-in for Microsoft.Extensions.Logging.Abstractions with the given major assembly version:
    /// the logger, the level, and the <c>[LoggerMessage]</c> attribute.</summary>
    /// <param name="major">The major version, such as 7.</param>
    /// <returns>The metadata reference.</returns>
    internal static MetadataReference FakeLogging(int major)
        => Product(
            "Microsoft.Extensions.Logging.Abstractions",
            "[assembly: System.Reflection.AssemblyVersion(\"" + major.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0.0.0\")]\n" + """
            namespace Microsoft.Extensions.Logging
            {
                public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical, None }

                public interface ILogger { }

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class LoggerMessageAttribute : System.Attribute
                {
                    public int EventId { get; set; } = -1;
                    public string? EventName { get; set; }
                    public LogLevel Level { get; set; }
                    public string Message { get; set; } = "";
                }
            }
            """,
            WithoutLogging(PlatformReferences));

    /// <summary>Compiles a stand-in for Microsoft.Extensions.Telemetry.Abstractions: a product that uses its
    /// <c>TelemetryMarker</c> references it, as one compiled by the generator of Microsoft.Extensions.Telemetry does.</summary>
    /// <returns>The metadata reference.</returns>
    internal static MetadataReference FakeTelemetry() => FakeTelemetry(10, 11);

    /// <summary>Compiles a stand-in for Microsoft.Extensions.Telemetry.Abstractions with the given version.</summary>
    /// <param name="major">The major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <returns>The metadata reference.</returns>
    internal static MetadataReference FakeTelemetry(int major, int minor)
        => Product(
            "Microsoft.Extensions.Telemetry.Abstractions",
            "[assembly: System.Reflection.AssemblyVersion(\"" + major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                + minor.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0.0\")]\n"
                + "namespace Microsoft.Extensions.Logging { public static class TelemetryMarker { public static int Value; } }",
            PlatformReferences);

    /// <summary>Compiles a product without emitting it, as a compilation reference: what an IDE gives a project that
    /// references another project. It holds every member, private ones included.</summary>
    /// <param name="assemblyName">The assembly name.</param>
    /// <param name="source">The source.</param>
    /// <returns>The compilation reference.</returns>
    internal static MetadataReference ProjectReference(string assemblyName, string source)
        => CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).ToMetadataReference();

    /// <summary>Compiles a product assembly with the real references plus the given ones.</summary>
    /// <param name="assemblyName">The assembly name.</param>
    /// <param name="source">The source.</param>
    /// <param name="extra">The further references.</param>
    /// <returns>The metadata reference.</returns>
    internal static MetadataReference ProductWith(string assemblyName, string source, params MetadataReference[] extra)
        => Product(assemblyName, source, [.. PlatformReferences, .. extra]);

    /// <summary>Compiles a product assembly against a stand-in for the logging abstractions instead of the real ones.</summary>
    /// <param name="assemblyName">The assembly name.</param>
    /// <param name="source">The source.</param>
    /// <param name="logging">The stand-in.</param>
    /// <returns>The metadata reference.</returns>
    internal static MetadataReference ProductOn(string assemblyName, string source, MetadataReference logging)
        => Product(assemblyName, source, [.. WithoutLogging(PlatformReferences), logging]);

    /// <summary>Compiles a product assembly and returns it as metadata.</summary>
    /// <param name="assemblyName">The assembly name.</param>
    /// <param name="source">The source.</param>
    /// <param name="references">The references.</param>
    /// <returns>The metadata reference.</returns>
    private static PortableExecutableReference Product(string assemblyName, string source, MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if (!emit.Success)
        {
            throw new InvalidOperationException(
                "The product did not compile: " + string.Join("; ", emit.Diagnostics.Where(static d => d.Severity is DiagnosticSeverity.Error)));
        }

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    /// <summary>Creates a consumer compilation.</summary>
    /// <param name="source">The consumer source, typically the opt-in attributes.</param>
    /// <param name="products">The product references.</param>
    /// <returns>The compilation.</returns>
    internal static CSharpCompilation Consumer(string source, params MetadataReference[] products)
        => Consumer(source, PlatformReferences, products);

    /// <summary>Creates a consumer compilation that does not reference the logging abstractions, so the
    /// <c>[LoggerMessage]</c> attribute of a product does not resolve.</summary>
    /// <param name="source">The consumer source, typically the opt-in attributes.</param>
    /// <param name="products">The product references.</param>
    /// <returns>The compilation.</returns>
    internal static CSharpCompilation ConsumerWithoutLogging(string source, params MetadataReference[] products)
        => Consumer(source, WithoutLogging(PlatformReferences), products);

    /// <summary>Leaves the logging abstractions out of a set of references.</summary>
    /// <param name="references">The references.</param>
    /// <returns>The references without the logging abstractions.</returns>
    private static MetadataReference[] WithoutLogging(MetadataReference[] references)
        => [.. references.Where(static r => !string.Equals(Path.GetFileName(r.Display), "Microsoft.Extensions.Logging.Abstractions.dll", StringComparison.OrdinalIgnoreCase))];

    /// <summary>Creates a consumer compilation with the given platform references.</summary>
    /// <param name="source">The consumer source.</param>
    /// <param name="platform">The platform references.</param>
    /// <param name="products">The product references.</param>
    /// <returns>The compilation.</returns>
    private static CSharpCompilation Consumer(string source, MetadataReference[] platform, MetadataReference[] products)
        => CSharpCompilation.Create(
            ConsumerName,
            [CSharpSyntaxTree.ParseText(source)],
            [.. platform, .. products],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>Runs the generator on a consumer and compiles the result.</summary>
    /// <param name="consumer">The consumer compilation.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The outcome.</returns>
    internal static GeneratorRun Run(Compilation consumer, CancellationToken cancellationToken)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new LogDefinitionGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(consumer, out var output, out _, cancellationToken);
        var result = driver.GetRunResult().Results.Single();
        return new GeneratorRun(
            result.GeneratedSources.ToDictionary(static s => s.HintName, static s => s.SourceText.ToString(), StringComparer.Ordinal),
            result.Diagnostics,
            [.. output.GetDiagnostics(cancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Error)],
            [.. output.GetDiagnostics(cancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Warning
                && d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) is true)]);
    }
}
