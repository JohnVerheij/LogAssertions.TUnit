using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace LogAssertions.Tests;

/// <summary>The outcome of one generator run: the generated sources, the generator's diagnostics, the errors of the
/// compilation with the sources added, and the warnings in the generated sources.</summary>
/// <param name="Sources">The generated sources by hint name.</param>
/// <param name="Diagnostics">The diagnostics the generator reported.</param>
/// <param name="Errors">The compilation errors after generation.</param>
/// <param name="GeneratedWarnings">The compiler warnings located in the generated sources.</param>
internal sealed record GeneratorRun(
    IReadOnlyDictionary<string, string> Sources,
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<Diagnostic> Errors,
    ImmutableArray<Diagnostic> GeneratedWarnings);
