using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Pins the v0.4.0 filter additions: <c>WithInnerException&lt;T&gt;</c> +
/// <c>WithInnerExceptionMessage</c> (F1) and <c>WithScopeProperties</c> subset match (F2).
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class LogAssertionsFiltersV040Tests
{
    // ---- F1: WithInnerException<T> + WithInnerExceptionMessage ----

    /// <summary>Outer wraps inner: WithInnerException&lt;TInner&gt; matches when the inner
    /// exception is exactly the requested type.</summary>
    [Test]
    public async Task WithInnerException_ExactInnerType_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer", new FormatException("inner")));

        await Assert.That(collector).HasLogged().WithInnerException<FormatException>().Once();
    }

    /// <summary>Inner-type assignability: WithInnerException&lt;Exception&gt; matches when
    /// any inner exception exists.</summary>
    [Test]
    public async Task WithInnerException_BaseType_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer", new FormatException("inner")));

        await Assert.That(collector).HasLogged().WithInnerException<Exception>().Once();
    }

    /// <summary>Wrong inner type: WithInnerException&lt;TInner&gt; does not match (assertion
    /// fails on the Once() terminator).</summary>
    [Test]
    public async Task WithInnerException_WrongInnerType_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer", new FormatException("inner")));

        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithInnerException<NotSupportedException>().Once())
            .Throws<AssertionException>();
    }

    /// <summary>No inner exception: WithInnerException&lt;TInner&gt; does not match.</summary>
    [Test]
    public async Task WithInnerException_NoInner_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer-only, no inner"));

        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithInnerException<FormatException>().Once())
            .Throws<AssertionException>();
    }

    /// <summary>WithInnerExceptionMessage matches a substring of the inner exception's
    /// message.</summary>
    [Test]
    public async Task WithInnerExceptionMessage_MatchingSubstring_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer", new FormatException("the inner cause is X")));

        await Assert.That(collector).HasLogged().WithInnerExceptionMessage("inner cause", StringComparison.Ordinal).Once();
    }

    /// <summary>WithInnerExceptionMessage does not match when the substring is not present in
    /// the inner exception's message.</summary>
    [Test]
    public async Task WithInnerExceptionMessage_AbsentSubstring_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer", new FormatException("inner")));

        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithInnerExceptionMessage("not-there", StringComparison.Ordinal).Once())
            .Throws<AssertionException>();
    }

    /// <summary>WithInnerExceptionMessage does not match when no inner exception exists.</summary>
    [Test]
    public async Task WithInnerExceptionMessage_NoInner_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer-only"));

        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithInnerExceptionMessage("anything", StringComparison.Ordinal).Once())
            .Throws<AssertionException>();
    }

    /// <summary>F1 composition: WithException&lt;TOuter&gt; AND WithInnerException&lt;TInner&gt;
    /// can be chained — typical gRPC pattern (RpcException-wraps-domain-exception).</summary>
    [Test]
    public async Task WithException_AndWithInnerException_Compose(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collector = CreateCollectorWithWrappedException(
            outer: new InvalidOperationException("outer", new FormatException("inner")));

        await Assert.That(collector).HasLogged()
            .WithException<InvalidOperationException>()
            .WithInnerException<FormatException>()
            .Once();
    }

    // ---- F2: WithScopeProperties subset match ----

    /// <summary>Single-scope match: every key/value pair in the required dictionary appears
    /// in the active scope.</summary>
    [Test]
    public async Task WithScopeProperties_AllPairsInOneScope_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[]
        {
            new KeyValuePair<string, object?>("CycleNumber", 7),
            new KeyValuePair<string, object?>("Stage", "validate"),
        }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        var required = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CycleNumber"] = 7,
            ["Stage"] = "validate",
        };
        await Assert.That(collector).HasLogged().WithScopeProperties(required).Once();
    }

    /// <summary>Subset across multiple scopes: required pairs may live in different scopes.</summary>
    [Test]
    public async Task WithScopeProperties_AcrossNestedScopes_Matches(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CycleNumber", 7) }))
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("Stage", "validate") }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        var required = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CycleNumber"] = 7,
            ["Stage"] = "validate",
        };
        await Assert.That(collector).HasLogged().WithScopeProperties(required).Once();
    }

    /// <summary>Missing key fails the match.</summary>
    [Test]
    public async Task WithScopeProperties_MissingKey_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CycleNumber", 7) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        var required = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CycleNumber"] = 7,
            ["Stage"] = "validate",
        };
        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithScopeProperties(required).Once())
            .Throws<AssertionException>();
    }

    /// <summary>Wrong value fails the match (key present but value mismatches).</summary>
    [Test]
    public async Task WithScopeProperties_WrongValue_Fails(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CycleNumber", 7) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        var required = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CycleNumber"] = 99,
        };
        await Assert.That(async () =>
            await Assert.That(collector).HasLogged().WithScopeProperties(required).Once())
            .Throws<AssertionException>();
    }

    /// <summary>Empty required dictionary matches every record (vacuous truth).</summary>
    [Test]
    public async Task WithScopeProperties_EmptyRequired_MatchesEveryRecord(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.StartedProcessing(logger); // no scope at all

        var required = new Dictionary<string, object?>(StringComparer.Ordinal);
        await Assert.That(collector).HasLogged().WithScopeProperties(required).Once();
    }

    /// <summary>Snapshot-on-construction: mutating the input dictionary after creating the
    /// filter does not change subsequent matches.</summary>
    [Test]
    public async Task WithScopeProperties_MutatingInput_DoesNotAffectExistingFilter(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope(new[] { new KeyValuePair<string, object?>("CycleNumber", 7) }))
        {
            TestLogMessages.StartedProcessing(logger);
        }

        var required = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CycleNumber"] = 7,
        };
        var filter = LogFilter.WithScopeProperties(required);

        // Mutate after construction — should not affect the snapshot the filter holds.
        required["CycleNumber"] = 99;
        required["Extra"] = "should-not-leak";

        var matchCount = collector.CountMatching(filter);
        await Assert.That(matchCount).IsEqualTo(1);
    }

    private static FakeLogCollector CreateCollectorWithWrappedException(Exception outer)
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        var logger = factory.CreateLogger("Test");
        TestLogMessages.OperationFailed(logger, outer);
        return collector;
    }
}
