using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LogAssertions.Tests;

/// <summary>The 0.13 tuple form, <c>var (factory, collector) = LogCollectorBuilder.Create();</c>, compiled against the
/// core: <c>LogCapture</c> has no <c>Deconstruct</c>, so it fails with the compiler's own error (CS1061).</summary>
[Category("Smoke")]
[Timeout(60_000)]
internal sealed class DeconstructCompileTests
{
    /// <summary>Compiles the tuple form with the C# compiler against the assemblies this test runs with.</summary>
    [Test]
    public async Task TupleForm_FailsToCompile_BecauseCaptureHasNoDeconstruct(CancellationToken cancellationToken)
    {
        var source = CSharpSyntaxTree.ParseText(
            """
            using LogAssertions;

            internal static class Consumer
            {
                internal static object Use()
                {
                    var (factory, collector) = LogCollectorBuilder.Create();
                    return (factory, collector);
                }
            }
            """,
            cancellationToken: cancellationToken);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "Consumer", [source], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var errors = compilation.GetDiagnostics(cancellationToken).Where(static d => d.Severity is DiagnosticSeverity.Error).ToArray();

        await Assert.That(errors).IsNotEmpty();
        await Assert.That(errors[0].Id).IsEqualTo("CS1061");
        await Assert.That(errors[0].GetMessage(CultureInfo.InvariantCulture)).Contains(
            "'LogCapture' does not contain a definition for 'Deconstruct'", StringComparison.Ordinal);
    }
}
