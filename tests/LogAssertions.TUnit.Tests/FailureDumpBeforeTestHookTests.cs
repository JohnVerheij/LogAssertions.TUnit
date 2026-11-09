using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// A test whose <c>[Before(Test)]</c> hook arms a dump, logs, and then fails: the body never runs, but TUnit still sends
/// the end notification to <see cref="LogAssertionsTestLifetimeAttribute"/>, so the dump is written. The hook fails only
/// the first attempt; the retry reads the first attempt's dump.
/// </summary>
[Timeout(15_000)]
internal sealed class FailureDumpBeforeTestHookTests
{
    private readonly FakeLogCollector _collector = new();

    /// <summary>Arms a dump and logs a record, then fails the first attempt.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    [Before(Test)]
    public void ArmLogThenFailTheFirstAttempt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = TestContext.Current!.Execution.CurrentRetryAttempt + 1;
        _collector.DumpToTestOutputOnFailure();
        DirectLog.Write(new FakeLogger(_collector, "BeforeHook"), $"hook record of attempt {attempt}");
        if (attempt == 1)
            throw new InvalidOperationException("Fails the first attempt in its [Before(Test)] hook, before the body.");
    }

    /// <summary>Only attempt 2 reaches the body; it finds the hook's record in attempt 1's dump.</summary>
    [Test]
    [Retry(1)]
    public async Task FailingBeforeTestHook_DumpHoldsTheHookRecord(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(TestContext.Current!.Execution.CurrentRetryAttempt).IsEqualTo(1);

        var output = TestContext.Current.GetStandardOutput();
        await Assert.That(output).Contains("Captured records of attempt 1 since the dump was armed (1 of 1 total, times in UTC):", StringComparison.Ordinal);
        await Assert.That(output).Contains("BeforeHook: hook record of attempt 1", StringComparison.Ordinal);
    }
}
