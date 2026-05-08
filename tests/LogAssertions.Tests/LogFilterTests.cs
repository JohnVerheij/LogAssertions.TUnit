using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for the <see cref="LogFilter"/> static factory and the
/// <see cref="ILogRecordFilter"/> contract. These tests deliberately do not reference
/// <c>LogAssertions.TUnit</c>; they call <see cref="FakeLogCollectorInspectionExtensions.CountMatching"/>
/// (in the core package) and assert with raw <see cref="Assert"/>. If the core ever
/// silently picked up a TUnit-specific dependency, this project would fail to compile.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogFilterTests
{
    /// <summary>Builds a collector seeded with one record at every standard level.</summary>
    /// <returns>A populated collector.</returns>
    private static FakeLogCollector SeededCollector()
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
#pragma warning disable CA1848 // LoggerMessage delegates: not worth the source-gen ceremony in tests
        logger.LogTrace("trace");
        logger.LogDebug("debug");
        logger.LogInformation("info");
        logger.LogWarning("warn");
        logger.LogError("error");
#pragma warning restore CA1848
        factory.Dispose();
        return collector;
    }

    /// <summary>Verifies <see cref="LogFilter.AtLevel(LogLevel)"/> matches exactly one level.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtLevelMatchesExactLevelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();

        await Assert.That(collector.CountMatching(LogFilter.AtLevel(LogLevel.Warning))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.AtLevel(LogLevel.Critical))).IsEqualTo(0);
    }

    /// <summary>Verifies <see cref="LogFilter.AtLevelOrAbove"/> and <see cref="LogFilter.AtLevelOrBelow"/>.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task LevelComparisonFiltersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();

        await Assert.That(collector.CountMatching(LogFilter.AtLevelOrAbove(LogLevel.Warning))).IsEqualTo(2);
        await Assert.That(collector.CountMatching(LogFilter.AtLevelOrBelow(LogLevel.Debug))).IsEqualTo(2);
    }

    /// <summary>Verifies the params overload of <see cref="LogFilter.AtLevel(LogLevel[])"/>.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task AtLevelParamsMatchesAnyInSetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();

        await Assert.That(collector.CountMatching(LogFilter.AtLevel(LogLevel.Warning, LogLevel.Error))).IsEqualTo(2);
        await Assert.That(collector.CountMatching(LogFilter.AtLevel(LogLevel.Critical))).IsEqualTo(0);
    }

    /// <summary>Verifies <see cref="LogFilter.Containing"/> with explicit <see cref="StringComparison"/>.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task ContainingMatchesSubstringAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();

        await Assert.That(collector.CountMatching(LogFilter.Containing("warn", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.Containing("WARN", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
        await Assert.That(collector.CountMatching(LogFilter.Containing("WARN", StringComparison.Ordinal))).IsEqualTo(0);
    }

    /// <summary>Verifies <see cref="LogFilter.Matching(Regex)"/>.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task MatchingRegexAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();
        var pattern = new Regex("^warn$", RegexOptions.NonBacktracking);

        await Assert.That(collector.CountMatching(LogFilter.Matching(pattern))).IsEqualTo(1);
    }

    /// <summary>Verifies the <c>All</c>, <c>Any</c>, <c>Not</c> combinators compose correctly.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task CombinatorsComposeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();

        ILogRecordFilter warningOrError = LogFilter.Any(
            LogFilter.AtLevel(LogLevel.Warning),
            LogFilter.AtLevel(LogLevel.Error));
        ILogRecordFilter notDebug = LogFilter.Not(LogFilter.AtLevel(LogLevel.Debug));
        ILogRecordFilter both = LogFilter.All(warningOrError, notDebug);

        await Assert.That(collector.CountMatching(both)).IsEqualTo(2);
        await Assert.That(collector.CountMatching(LogFilter.All())).IsEqualTo(5);   // empty All -> matches every record
        await Assert.That(collector.CountMatching(LogFilter.Any())).IsEqualTo(0);   // empty Any -> matches none
    }

    /// <summary>Verifies the <see cref="ILogRecordFilter.Description"/> field is rendered for combinators.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FilterDescriptionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(LogFilter.AtLevel(LogLevel.Warning).Description).IsEqualTo("Level = Warning");
        await Assert.That(LogFilter.Containing("x", StringComparison.Ordinal).Description).IsEqualTo("Message contains \"x\" (Ordinal)");
        await Assert.That(LogFilter.All().Description).IsEqualTo("(any)");
        await Assert.That(LogFilter.Any().Description).IsEqualTo("(none)");
        await Assert.That(LogFilter.Not(LogFilter.AtLevel(LogLevel.Warning)).Description).IsEqualTo("NOT Level = Warning");
    }

    /// <summary>Verifies <see cref="LogFilter.WithEventIdInRange"/> rejects negative-width ranges.</summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task WithEventIdInRangeRejectsBadRangeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => LogFilter.WithEventIdInRange(5, 1)).Throws<ArgumentOutOfRangeException>();
    }

    /// <summary>Pins null-argument validation across every <c>LogFilter</c> factory that
    /// declares a non-null reference parameter. Each call site is exercised with a deliberate
    /// <see langword="null"/> to verify the implicit <c>ArgumentNullException.ThrowIfNull</c>
    /// path. Beyond contract verification, this drives the throw branch of each method into
    /// the coverage measurement (the happy path is exercised by the other tests in this file
    /// and by the chain tests in the TUnit suite, but the throw branch otherwise stays
    /// unhit).</summary>
    [Test]
    public async Task FactoriesRejectNullArgumentsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
    /// because the loop doesn't execute. Pins this and exercises the
    /// <see cref="LogFilter.TryMatchScope{TValue}"/> early-exit cast-failure path that gets
    /// hit when a scope is non-null but doesn't implement the expected
    /// <see cref="IEnumerable{T}"/>-of-<see cref="KeyValuePair{TKey, TValue}"/> shape.</summary>
    [Test]
    public async Task WithScopeProperty_NoActiveScopes_ReturnsFalseAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = SeededCollector();
        // The seeded collector logs without BeginScope, so every record has zero active scopes.
        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("anything", "anyvalue"))).IsEqualTo(0);
        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("anything", _ => true))).IsEqualTo(0);
    }

    /// <summary>A scope object that doesn't implement the recognised
    /// <see cref="IEnumerable{T}"/>-of-<see cref="KeyValuePair{TKey, TValue}"/> shape (e.g. a
    /// raw string passed as scope state) yields no scope-property match. Pins the cast-failure
    /// branch of <see cref="LogFilter.TryMatchScope{TValue}"/>.</summary>
    [Test]
    public async Task WithScopeProperty_NonKvpScope_DoesNotMatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using var __ = factory;
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope("a-non-kvp-string-scope"))
        {
#pragma warning disable CA1848
            logger.LogInformation("under-string-scope");
#pragma warning restore CA1848
        }

        await Assert.That(collector.CountMatching(LogFilter.WithScopeProperty("k", "v"))).IsEqualTo(0);
    }
}
