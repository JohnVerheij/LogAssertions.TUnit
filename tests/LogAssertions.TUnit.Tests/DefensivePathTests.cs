using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>Defensive paths of the assertion classes: a <see langword="null"/> collector, a <see langword="null"/> or
/// empty any-order group, a single failure in <c>AssertAllAsync</c>, a <see langword="null"/> scope value on the
/// chain, and a tee disposed where no test is current.</summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class DefensivePathTests
{
    /// <summary>Every assertion that reads the collector reports a <see langword="null"/> one as a failure.</summary>
    [Test]
    public async Task NullCollector_FailsEveryAssertion(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var exception in new[]
        {
            await CatchAsync<AssertionException>(async () => await Assert.That(NullCollector()!).HasLogged()),
            await CatchAsync<AssertionException>(async () => await Assert.That(NullCollector()!).HasNotLogged()),
            await CatchAsync<AssertionException>(async () => await Assert.That(NullCollector()!).HasLoggedSequence().AtLevel(LogLevel.Information)),
        })
        {
            await Assert.That(exception!.Message).Contains("collector was null", StringComparison.Ordinal);
        }
    }

    /// <summary>An any-order group with no sub-steps is satisfied, and a <see langword="null"/> sub-step configurator
    /// is rejected.</summary>
    [Test]
    public async Task ThenAnyOrder_EmptyGroupPasses_NullConfiguratorThrows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        DirectLog.Write(new FakeLogger(collector, "Seq"), "only");

        await Assert.That(collector).HasLoggedSequence().Containing("only", StringComparison.Ordinal)
            .ThenAnyOrder(Array.Empty<Action<HasLoggedSequenceAssertion>>());

        await Assert.That(await CatchAsync<ArgumentNullException>(async () =>
            await Assert.That(collector).HasLoggedSequence().ThenAnyOrder((Action<HasLoggedSequenceAssertion>)null!))).IsNotNull();
    }

    /// <summary>A single failing assertion in <c>AssertAllAsync</c> is rethrown as it is, not wrapped.</summary>
    [Test]
    public async Task AssertAllAsync_SingleFailure_RethrowsItUnchanged(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();

        var exception = await CatchAsync<AssertionException>(async () =>
            await Assert.That(collector).AssertAllAsync(
                c => c.HasNotLogged(),
                c => c.HasLogged().Containing("missing", StringComparison.Ordinal)));

        await Assert.That(exception!.Message).DoesNotContain("AssertAll:", StringComparison.Ordinal);
        await Assert.That(exception.Message).Contains("0 record(s) matched", StringComparison.Ordinal);
    }

    /// <summary>A <see langword="null"/> scope value on the chain matches a scope property whose value is
    /// <see langword="null"/>.</summary>
    [Test]
    public async Task WithScopeProperty_NullValue_MatchesANullScopeValue(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Scope");
        using (logger.BeginScope(new System.Collections.Generic.Dictionary<string, object?>(StringComparer.Ordinal) { ["Tenant"] = null }))
        {
            DirectLog.Write(logger, "scoped");
        }

        await Assert.That(collector).HasLogged().WithScopeProperty("Tenant", (object?)null).Once();
    }

    /// <summary>A tee disposed where no test is current, with skipped records, writes its count nowhere and does not
    /// throw.</summary>
    [Test]
    public async Task TeeDisposedOffContext_WithSkippedRecords_DoesNotThrow(CancellationToken cancellationToken)
    {
        using var provider = new TestOutputLoggerProvider(owner: null);
        var logger = provider.CreateLogger("OffContext");

        await OffContext.RunAsync(
            () =>
            {
                DirectLog.Write(logger, "skipped");
                provider.Dispose();
            },
            cancellationToken);

        await Assert.That(provider.SkippedRecordCount).IsEqualTo(1);
        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain("were not teed", StringComparison.Ordinal);
    }

    /// <summary>Returns a null collector without a constant expression the analyzer would reject.</summary>
    /// <returns>Always null.</returns>
    private static FakeLogCollector? NullCollector() => null;

    /// <summary>Runs <paramref name="run"/> and returns the <typeparamref name="TException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <typeparam name="TException">The exception type to catch.</typeparam>
    /// <param name="run">The code to run.</param>
    /// <returns>The exception, or <see langword="null"/>.</returns>
    private static async Task<TException?> CatchAsync<TException>(Func<Task> run)
        where TException : Exception
    {
        try
        {
            await run();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }
}
