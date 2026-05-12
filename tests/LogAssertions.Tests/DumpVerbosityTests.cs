using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;

// CA1848 / CA1873 are about hot-path logger performance; in tests the call sites are exercised
// once per test and clarity matters more than allocation, so the suppression is whole-file.
#pragma warning disable CA1848
#pragma warning disable CA1873

namespace LogAssertions.Tests;

/// <summary>
/// Pins the v0.4.0 <see cref="DumpVerbosity"/> contract: the three levels produce predictable
/// shapes (Compact: one line per record, Default: standard one-liner detail, Verbose: includes
/// full exception <c>ToString()</c>). Tests check broad markers rather than exact whitespace,
/// matching the family-wide stance that rendering output is "not stable".
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class DumpVerbosityTests
{
    [Test]
    public async Task Compact_OmitsPropsScopeAndExceptionDetail(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Compact");
        try
        { throw new InvalidOperationException("boom"); }
        catch (InvalidOperationException ex) { logger.LogError(ex, "Op {Id} failed", 7); }

        using var sw = new StringWriter();
        collector.DumpTo(sw, DumpVerbosity.Compact);
        var text = sw.ToString();

        await Assert.That(text).Contains("[fail]");
        await Assert.That(text).Contains("Op 7 failed");
        // Compact must NOT carry the indented detail markers.
        await Assert.That(text).DoesNotContain("props:");
        await Assert.That(text).DoesNotContain("exception:");
    }

    [Test]
    public async Task Default_IncludesPropsAndOneLineExceptionSummary(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Default");
        try
        { throw new InvalidOperationException("boom"); }
        catch (InvalidOperationException ex) { logger.LogError(ex, "Op {Id} failed", 7); }

        using var sw = new StringWriter();
        collector.DumpTo(sw, DumpVerbosity.Default);
        var text = sw.ToString();

        await Assert.That(text).Contains("[fail]");
        await Assert.That(text).Contains("Op 7 failed");
        await Assert.That(text).Contains("props:");
        await Assert.That(text).Contains("exception: InvalidOperationException: boom");
        // Default must NOT include the full stack-trace (Verbose-only).
        await Assert.That(text).DoesNotContain("at LogAssertions.Tests");
    }

    [Test]
    public async Task Verbose_IncludesFullExceptionToString(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Verbose");
        try
        { throw new InvalidOperationException("boom"); }
        catch (InvalidOperationException ex) { logger.LogError(ex, "Op {Id} failed", 7); }

        using var sw = new StringWriter();
        collector.DumpTo(sw, DumpVerbosity.Verbose);
        var text = sw.ToString();

        await Assert.That(text).Contains("[fail]");
        await Assert.That(text).Contains("exception: InvalidOperationException: boom");
        // Verbose includes the full ToString(): stack frame from this test method should appear.
        await Assert.That(text).Contains("at LogAssertions.Tests.DumpVerbosityTests");
    }

    /// <summary>
    /// Verifies the scope-rendering contract: <see cref="DumpVerbosity.Compact"/> suppresses
    /// scope output entirely, while <see cref="DumpVerbosity.Default"/> includes it. Without
    /// this test, a regression in <c>AppendScopes</c> (e.g. always-emit or never-emit) would go
    /// undetected by the other DumpVerbosity tests because none of them log inside a scope.
    /// </summary>
    [Test]
    public async Task ScopeRendering_CompactSuppresses_DefaultIncludes(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Scope");
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["RequestId"] = "abc-123" }))
        {
            logger.LogInformation("under-scope");
        }

        using var swCompact = new StringWriter();
        collector.DumpTo(swCompact, DumpVerbosity.Compact);
        var compactText = swCompact.ToString();
        await Assert.That(compactText).Contains("under-scope");
        await Assert.That(compactText).DoesNotContain("scope:");
        await Assert.That(compactText).DoesNotContain("RequestId");

        using var swDefault = new StringWriter();
        collector.DumpTo(swDefault, DumpVerbosity.Default);
        var defaultText = swDefault.ToString();
        await Assert.That(defaultText).Contains("under-scope");
        await Assert.That(defaultText).Contains("scope:");
        await Assert.That(defaultText).Contains("RequestId");
    }

    [Test]
    public async Task NoArgOverload_IsDefaultVerbosity(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Default");
        logger.LogInformation("hello {Who}", "world");

        using var swDefault = new StringWriter();
        collector.DumpTo(swDefault);
        using var swExplicit = new StringWriter();
        collector.DumpTo(swExplicit, DumpVerbosity.Default);

        await Assert.That(swDefault.ToString()).IsEqualTo(swExplicit.ToString());
    }

    [Test]
    public async Task EmptyCollector_StillRendersHeader(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;

        using var sw = new StringWriter();
        collector.DumpTo(sw, DumpVerbosity.Compact);
        await Assert.That(sw.ToString()).Contains("Captured records (0 total)");
    }
}
