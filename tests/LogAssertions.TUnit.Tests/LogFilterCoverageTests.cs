using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

#pragma warning disable CA1848 // LoggerMessage delegates: not worth the source-gen ceremony in tests

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Coverage-recovery tests that mirror the framework-agnostic LogFilter / scope-property tests
/// in <c>tests/LogAssertions.Tests</c>. They exist here as well because the CI's
/// <c>--coverage</c> step runs only this project, so identical tests need to live in this
/// assembly to drive the LogFilter argument-validation throw-branches into the cobertura
/// report. The two projects continue to serve different purposes:
/// <list type="bullet">
/// <item><c>LogAssertions.Tests</c> guards architectural coupling (compiles ONLY against the
/// core, would fail compilation if a TUnit dependency leaked in).</item>
/// <item>This file in <c>LogAssertions.TUnit.Tests</c> is what cobertura measures.</item>
/// </list>
/// Duplication is intentional and inexpensive (small surface, stable contract).
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogFilterCoverageTests
{
    /// <summary>Pins null-argument validation across every <see cref="LogFilter"/> factory
    /// that declares a non-null reference parameter. Each call site is exercised with a
    /// deliberate <see langword="null"/> to verify the implicit
    /// <c>ArgumentNullException.ThrowIfNull</c> path and drive the throw branch into the
    /// coverage measurement.</summary>
    [Test]
    public async Task FactoriesRejectNullArgumentsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(() => LogFilter.AtLevel(levels: null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.Containing(null!, StringComparison.Ordinal)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.ContainingAll(StringComparison.Ordinal, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.ContainingAny(StringComparison.Ordinal, null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.Matching(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithMessage(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithMessageTemplate(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithException(predicate: null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithExceptionMessage(null!, StringComparison.Ordinal)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithInnerExceptionMessage(null!, StringComparison.Ordinal)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithProperty(null!, value: null)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithProperty("k", predicate: null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithProperty(key: null!, predicate: _ => true)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithCategory(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithEventName(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperty(null!, value: null)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperty("k", predicate: null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperty(key: null!, predicate: _ => true)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.WithScopeProperties(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.Where(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.All(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.Any(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogFilter.Not(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>The scope-property filters return <see langword="false"/> when the record has
    /// NO active scopes: the predicate path that scans <c>record.Scopes</c> short-circuits
    /// because the loop doesn't execute. Drives the empty-scope branch.</summary>
    [Test]
    public async Task WithScopeProperty_NoActiveScopes_ReturnsFalseAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("no-scope");

        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("anything", "anyvalue"))).IsEqualTo(0);
        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("anything", _ => true))).IsEqualTo(0);
    }

    /// <summary>A scope object that doesn't implement the recognised
    /// <see cref="IEnumerable{T}"/>-of-<see cref="KeyValuePair{TKey, TValue}"/> shape (e.g. a
    /// raw string passed as scope state) yields no scope-property match. Drives the
    /// cast-failure branch of <c>TryMatchScope</c>.</summary>
    [Test]
    public async Task WithScopeProperty_NonKvpScope_DoesNotMatchAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope("a-non-kvp-string-scope"))
        {
            logger.LogInformation("under-string-scope");
        }

        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("k", "v"))).IsEqualTo(0);
    }

    /// <summary>The DumpVerbosity rendering-helper has a default fallback for
    /// non-standard <see cref="LogLevel"/> values. Drives the default branch of
    /// <c>LogAssertionRendering.LevelAbbreviation</c>.</summary>
    [Test]
    public async Task LevelAbbreviation_NonStandardLevel_FallsBackToToString(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var weirdLevel = (LogLevel)42;
        var abbrev = LogAssertionRendering.LevelAbbreviation(weirdLevel);
        await Assert.That(abbrev).IsEqualTo("42");
    }

    /// <summary>Drives the success path of <see cref="LogFilter.Matching(Regex)"/> by
    /// exercising the regex against records that match. Combined with the null-arg test
    /// above, this covers both branches of the helper.</summary>
    [Test]
    public async Task Matching_RegexAgainstRealRecord_MatchesAndNonMatchesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        logger.LogInformation("hello world");
        logger.LogInformation("goodbye world");

        var pattern = new Regex("^hello", RegexOptions.NonBacktracking);
        await Assert.That(collector.CountMatching(LogFilter.Matching(pattern))).IsEqualTo(1);
    }

    /// <summary>Drives the success path of <see cref="LogFilter.WithScopeProperty(string, object?)"/>
    /// by matching against a real scope. Combined with the no-active-scopes and non-KVP-scope
    /// tests above, this covers all relevant branches of the helper.</summary>
    [Test]
    public async Task WithScopeProperty_RealScopeMatch_FindsRecordAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["OrderId"] = 42,
        }))
        {
            logger.LogInformation("under-order-scope");
        }

        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("OrderId", 42))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("OrderId", v => Equals(v, 42)))).IsEqualTo(1);
    }

    /// <summary>Each assertion class's <c>CheckAsync</c> overrides have a defensive branch
    /// for the case where the source threw (TUnit propagates the exception to the assertion
    /// via <c>metadata.Exception</c>). Tests below exercise that branch by passing a source
    /// lambda that throws; the assertion is expected to fail, but the path through
    /// <c>metadata.Exception is not null</c> gets covered.</summary>
    [Test]
    public async Task HasLogged_SourceThrew_PropagatesAsAssertionExceptionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(ThrowingSourceAsync(ct)).HasLogged<FakeLogCollector>().Once();
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasNotLogged_SourceThrew_PropagatesAsAssertionExceptionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(ThrowingSourceAsync(ct)).HasNotLogged<FakeLogCollector>();
        }).Throws<AssertionException>();
    }

    [Test]
    public async Task HasLoggedSequence_SourceThrew_PropagatesAsAssertionExceptionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Assert.That(async () =>
        {
            await Assert.That(ThrowingSourceAsync(ct))
                .HasLoggedSequence<FakeLogCollector>()
                .AtLevel(LogLevel.Information);
        }).Throws<AssertionException>();
    }

    private static async Task<FakeLogCollector> ThrowingSourceAsync(CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("source threw");
    }
}
