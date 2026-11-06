using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Routing of the built-in tee when one logger factory outlives the test that built it: a factory in a
/// <see langword="static"/> field (or a class fixture) is set up lazily during the first test of its class and then
/// logs during later tests. With <see cref="LogAssertionsTestLifetimeAttribute"/> applied (this assembly), each record
/// goes to the test that is current and running when it is logged, and a record that no running test claims is
/// counted instead of being written into a test that has ended.
/// </summary>
[NotInParallel(nameof(SharedTeeRoutingTests))]
[Timeout(15_000)]
internal sealed class SharedTeeRoutingTests
{
    /// <summary>Gets or sets the factory shared by the tests of this class, built during the first one.</summary>
    private static ILoggerFactory? SharedFactory { get; set; }

    /// <summary>Gets or sets the context of the test that built <see cref="SharedFactory"/>.</summary>
    private static TestContext? SetupTest { get; set; }

    /// <summary>Gets or sets the gate that releases the loop started in <see cref="LoopTest_StartsALoop"/>.</summary>
    private static SemaphoreSlim? LoopGate { get; set; }

    /// <summary>Gets or sets the completion the loop sets once it has logged.</summary>
    private static TaskCompletionSource? LoopDone { get; set; }

    /// <summary>Gets or sets the context of the test that started the loop.</summary>
    private static TestContext? LoopStarter { get; set; }

    /// <summary>The first test of the class builds the shared factory; its own record reaches its own output.</summary>
    [Test]
    public async Task SetupTest_BuildsTheSharedFactory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SetupTest = TestContext.Current;
        var collector = new FakeLogCollector();
        SharedFactory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector));

        DirectLog.Write(SharedFactory.CreateLogger("Shared"), "record of the setup test");

        await Assert.That(SetupTest!.GetStandardOutput()).Contains("record of the setup test", StringComparison.Ordinal);
    }

    /// <summary>A later test logs through the shared factory on its own flow: the record reaches the later test's
    /// output and not the output of the test that built the factory.</summary>
    [Test]
    [DependsOn(nameof(SetupTest_BuildsTheSharedFactory))]
    public async Task LaterTest_RecordGoesToTheLaterTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = TestContext.Current!;
        await Assert.That(current).IsNotSameReferenceAs(SetupTest!);

        DirectLog.Write(SharedFactory!.CreateLogger("Shared"), "record of the later test");

        await Assert.That(current.GetStandardOutput()).Contains("record of the later test", StringComparison.Ordinal);
        await Assert.That(SetupTest!.GetStandardOutput()).DoesNotContain("record of the later test", StringComparison.Ordinal);
    }

    /// <summary>A later test that awaits and logs on the thread pool keeps its own flow: the record still reaches the
    /// later test.</summary>
    [Test]
    [DependsOn(nameof(LaterTest_RecordGoesToTheLaterTest))]
    public async Task LaterTest_ThreadPoolRecordGoesToTheLaterTest(CancellationToken cancellationToken)
    {
        var current = TestContext.Current!;
        var logger = SharedFactory!.CreateLogger("Shared");

        await Task.Run(
            async () =>
            {
                await Task.Yield();
                DirectLog.Write(logger, "pool record of the later test");
            },
            cancellationToken);

        await Assert.That(current.GetStandardOutput()).Contains("pool record of the later test", StringComparison.Ordinal);
        await Assert.That(SetupTest!.GetStandardOutput()).DoesNotContain("pool record of the later test", StringComparison.Ordinal);
    }

    /// <summary>A record logged where no test is current (a thread started without the execution context) would fall
    /// back to the owning test, but that test has ended: the record is written into no test's output, and counted.</summary>
    [Test]
    [DependsOn(nameof(LaterTest_ThreadPoolRecordGoesToTheLaterTest))]
    public async Task LaterTest_OffContextRecord_IsNotTeedToTheEndedSetupTest(CancellationToken cancellationToken)
    {
        var current = TestContext.Current!;
        var logger = SharedFactory!.CreateLogger("Shared");

        await OffContext.RunAsync(() => DirectLog.Write(logger, "off-context record during the later test"), cancellationToken);

        await Assert.That(current.GetStandardOutput()).DoesNotContain("off-context record", StringComparison.Ordinal);
        await Assert.That(SetupTest!.GetStandardOutput()).DoesNotContain("off-context record", StringComparison.Ordinal);
    }

    /// <summary>Part 1 of the loop case: a test starts a loop on its own flow, so the loop carries this test's context.</summary>
    [Test]
    [DependsOn(nameof(LaterTest_OffContextRecord_IsNotTeedToTheEndedSetupTest))]
    public async Task LoopTest_StartsALoop(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoopStarter = TestContext.Current;
        LoopGate = new SemaphoreSlim(0);
        var gate = LoopGate;
        var logger = SharedFactory!.CreateLogger("Loop");
        _ = Task.Run(
            async () =>
            {
                await gate.WaitAsync(CancellationToken.None);
                DirectLog.Write(logger, "loop record logged during the next test");
                LoopDone!.SetResult();
            },
            CancellationToken.None);
        await Assert.That(gate.CurrentCount).IsEqualTo(0);
    }

    /// <summary>Part 2 of the loop case: the loop logs while the next test runs. Its context is the test that started
    /// it, which has ended, so the record is written into neither test's output, and counted.</summary>
    [Test]
    [DependsOn(nameof(LoopTest_StartsALoop))]
    public async Task LoopTest_NextTestReleasesTheLoop(CancellationToken cancellationToken)
    {
        var current = TestContext.Current!;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopDone = done;
        LoopGate!.Release();
        await done.Task.WaitAsync(cancellationToken);

        await Assert.That(current.GetStandardOutput()).DoesNotContain("loop record", StringComparison.Ordinal);
        await Assert.That(LoopStarter!.GetStandardOutput()).DoesNotContain("loop record", StringComparison.Ordinal);
    }

    /// <summary>Disposing the shared factory reports the two records above that reached no test.</summary>
    [Test]
    [DependsOn(nameof(LoopTest_NextTestReleasesTheLoop))]
    public async Task LastTest_DisposesTheFactory_ReportsTheSkippedRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedFactory!.Dispose();

        await Assert.That(TestContext.Current!.GetStandardOutput())
            .Contains("2 log record(s) were not teed to a test report", StringComparison.Ordinal);
    }

    /// <summary>Disposes the shared factory and the gate after the last test (a second disposal does nothing).</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    /// <returns>A completed task.</returns>
    [After(Class)]
    public static Task DisposeSharedFactory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedFactory?.Dispose();
        LoopGate?.Dispose();
        return Task.CompletedTask;
    }
}
