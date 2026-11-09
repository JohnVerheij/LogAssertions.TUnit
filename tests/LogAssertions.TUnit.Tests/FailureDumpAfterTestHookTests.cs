using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// A test whose body passes and whose <c>[After(Test)]</c> hook fails: TUnit marks the attempt failed before the end
/// notification reaches <see cref="LogAssertionsTestLifetimeAttribute"/>, so the armed dump is written, and it holds the
/// record the hook logged (the end comes after the hooks). The hook fails only the first attempt; the retry reads the
/// first attempt's dump.
/// </summary>
[Timeout(15_000)]
internal sealed class FailureDumpAfterTestHookTests
{
    private readonly FakeLogCollector _collector = new();

    /// <summary>Logs a record, then fails the first attempt.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    [After(Test)]
    public void LogThenFailTheFirstAttempt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = TestContext.Current!.Execution.CurrentRetryAttempt + 1;
        DirectLog.Write(new FakeLogger(_collector, "AfterHook"), $"hook record of attempt {attempt}");
        if (attempt == 1)
            throw new InvalidOperationException("Fails the first attempt in its [After(Test)] hook, after a passing body.");
    }

    /// <summary>Attempt 1 arms a dump and logs; its body passes and its hook fails it. Attempt 2 finds the body's
    /// and the hook's record in attempt 1's dump.</summary>
    [Test]
    [Retry(1)]
    public async Task FailingAfterTestHook_DumpHoldsTheBodyAndHookRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = TestContext.Current!.Execution.CurrentRetryAttempt + 1;
        _collector.DumpToTestOutputOnFailure();
        DirectLog.Write(new FakeLogger(_collector, "Body"), $"body record of attempt {attempt}");
        if (attempt == 1)
            return;

        var output = TestContext.Current.GetStandardOutput();
        await Assert.That(output).Contains("Captured records of attempt 1 since the dump was armed (2 of 2 total, times in UTC):", StringComparison.Ordinal);
        await Assert.That(output).Contains("Body: body record of attempt 1", StringComparison.Ordinal);
        await Assert.That(output).Contains("AfterHook: hook record of attempt 1", StringComparison.Ordinal);
    }
}
