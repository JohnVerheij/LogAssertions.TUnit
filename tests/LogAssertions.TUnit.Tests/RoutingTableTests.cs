using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The routing table of the README, one test per row, with <see cref="LogAssertionsTestLifetimeAttribute"/> applied
/// (this assembly). Test 1 builds a teed factory and starts a <see cref="BackgroundService"/> on it; a host (a teed
/// factory and its own background service) is started outside any test, inside
/// <see cref="ExecutionContext.SuppressFlow"/>. A <c>[Before(Class)]</c> hook alone is not outside a test: TUnit runs it
/// on the flow of the class's first test, as it runs other hooks and shared-fixture initialization.
/// </summary>
[NotInParallel(nameof(RoutingTableTests))]
[Timeout(15_000)]
internal sealed class RoutingTableTests
{
    /// <summary>Gets or sets the test that builds <see cref="Test1Factory"/> and starts <see cref="Test1Service"/>.</summary>
    private static TestContext? Test1 { get; set; }

    /// <summary>Gets or sets the teed factory built inside test 1.</summary>
    private static ILoggerFactory? Test1Factory { get; set; }

    /// <summary>Gets or sets the background service started inside test 1.</summary>
    private static TickingService? Test1Service { get; set; }

    /// <summary>Gets or sets the teed factory of the host started outside any test.</summary>
    private static ILoggerFactory? HostFactory { get; set; }

    /// <summary>Gets or sets the background service of the host started outside any test.</summary>
    private static TickingService? HostService { get; set; }

    /// <summary>Gets or sets the test that was current in the <c>[Before(Class)]</c> hook.</summary>
    private static TestContext? HookRanIn { get; set; }

    /// <summary>Gets or sets the test that was current when the host was started, expected to be none.</summary>
    private static TestContext? HostStartedIn { get; set; }

    /// <summary>Starts the host outside any test, inside <see cref="ExecutionContext.SuppressFlow"/>: its tee has no
    /// owning test, and its loop has no test context.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    /// <returns>A task that completes when the host's service has started.</returns>
    [Before(Class)]
    public static async Task StartTheHostOutsideAnyTest(CancellationToken cancellationToken)
    {
        HookRanIn = TestContext.Current;
        await OffContext.RunAsync(
            async () =>
            {
                HostStartedIn = TestContext.Current;
                HostFactory = LoggerFactory.Create(b => b.AddTeedFakeLogging(new FakeLogCollector()));
                HostService = new TickingService(HostFactory.CreateLogger("Host"));
                await HostService.StartAsync(cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Rows 2 and 3 while test 1 runs: test 1 builds a factory and starts a background service on its own
    /// flow. The service's record, and a record logged on a thread with no test context, go to test 1.</summary>
    [Test]
    public async Task Test1_StartsAService_ItsRecordsGoToTest1WhileItRuns(CancellationToken cancellationToken)
    {
        Test1 = TestContext.Current;
        Test1Factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(new FakeLogCollector()));
        Test1Service = new TickingService(Test1Factory.CreateLogger("Service"));
        await Test1Service.StartAsync(cancellationToken);
        var timer = Test1Factory.CreateLogger("Timer");

        await Test1Service.TickAsync("service record while test 1 runs", cancellationToken);
        await OffContext.RunAsync(() => DirectLog.Write(timer, "no-context record while test 1 runs"), cancellationToken);

        var output = Test1!.GetStandardOutput();
        await Assert.That(output).Contains("Service: service record while test 1 runs", StringComparison.Ordinal);
        await Assert.That(output).Contains("Timer: no-context record while test 1 runs", StringComparison.Ordinal);
    }

    /// <summary>Row 1: a record on test 2's own flow (test 2 calls the logger, or awaits a task it started) goes to
    /// test 2, not to test 1, which built the factory.</summary>
    [Test]
    [DependsOn(nameof(Test1_StartsAService_ItsRecordsGoToTest1WhileItRuns))]
    public async Task Row1_RecordOnTest2sOwnFlow_GoesToTest2(CancellationToken cancellationToken)
    {
        var test2 = TestContext.Current!;
        var logger = Test1Factory!.CreateLogger("Own");

        DirectLog.Write(logger, "own-flow record of test 2");
        await Task.Run(
            async () =>
            {
                await Task.Yield();
                DirectLog.Write(logger, "task record of test 2");
            },
            cancellationToken);

        var output = test2.GetStandardOutput();
        await Assert.That(output).Contains("Own: own-flow record of test 2", StringComparison.Ordinal);
        await Assert.That(output).Contains("Own: task record of test 2", StringComparison.Ordinal);
        await Assert.That(Test1!.GetStandardOutput()).DoesNotContain("of test 2", StringComparison.Ordinal);
    }

    /// <summary>Row 2: the background service started inside test 1 logs during test 2. Test 1 has ended, so the
    /// record is not teed (and is counted); it never goes to test 2.</summary>
    [Test]
    [DependsOn(nameof(Row1_RecordOnTest2sOwnFlow_GoesToTest2))]
    public async Task Row2_ServiceStartedInTest1_LogsDuringTest2_IsNotTeed_NeverToTest2(CancellationToken cancellationToken)
    {
        await Test1Service!.TickAsync("service record during test 2", cancellationToken);

        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain("service record during test 2", StringComparison.Ordinal);
        await Assert.That(Test1!.GetStandardOutput()).DoesNotContain("service record during test 2", StringComparison.Ordinal);
    }

    /// <summary>Row 3: a record logged on a thread with no test context goes to the test that built the tee only while
    /// it runs; test 1 has ended, so the record is not teed (and is counted).</summary>
    [Test]
    [DependsOn(nameof(Row2_ServiceStartedInTest1_LogsDuringTest2_IsNotTeed_NeverToTest2))]
    public async Task Row3_NoTestContext_AfterTheOwnerEnded_IsNotTeed(CancellationToken cancellationToken)
    {
        var timer = Test1Factory!.CreateLogger("Timer");

        await OffContext.RunAsync(() => DirectLog.Write(timer, "no-context record during test 2"), cancellationToken);

        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain("no-context record during test 2", StringComparison.Ordinal);
        await Assert.That(Test1!.GetStandardOutput()).DoesNotContain("no-context record during test 2", StringComparison.Ordinal);
    }

    /// <summary>Row 4: for a host started outside any test (inside <c>ExecutionContext.SuppressFlow()</c>), its own loop
    /// records are not teed (and are counted), and a record from a request the test makes, handled on the test's flow
    /// as an in-memory test server does, goes to the test. The <c>[Before(Class)]</c> hook itself ran on the flow of a
    /// test of this class, so a host started there without suppressing the flow counts as started inside that test
    /// (row 2).</summary>
    [Test]
    public async Task Row4_HostStartedOutsideATest_LoopIsNotTeed_RequestsGoToTheTest(CancellationToken cancellationToken)
    {
        await HostService!.TickAsync("host loop record", cancellationToken);
        HostService.HandleRequest("host request record");

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(HostStartedIn).IsNull();
        await Assert.That(HookRanIn).IsNotNull();
        await Assert.That(HookRanIn!.ClassContext).IsSameReferenceAs(TestContext.Current.ClassContext);
        await Assert.That(output).Contains("Host: host request record", StringComparison.Ordinal);
        await Assert.That(output).DoesNotContain("host loop record", StringComparison.Ordinal);
    }

    /// <summary>Disposing each factory writes the count of its records that reached no test: rows 2 and 3 for test 1's
    /// factory, row 4's loop record for the host's.</summary>
    [Test]
    [DependsOn(nameof(Row3_NoTestContext_AfterTheOwnerEnded_IsNotTeed))]
    [DependsOn(nameof(Row4_HostStartedOutsideATest_LoopIsNotTeed_RequestsGoToTheTest))]
    public async Task Disposal_CountsTheRecordsThatReachedNoTest(CancellationToken cancellationToken)
    {
        await Test1Service!.StopAsync(cancellationToken);
        await HostService!.StopAsync(cancellationToken);
        Test1Factory!.Dispose();
        HostFactory!.Dispose();

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(output).Contains("2 log record(s) were not teed to a test report", StringComparison.Ordinal);
        await Assert.That(output).Contains("1 log record(s) were not teed to a test report", StringComparison.Ordinal);
    }

    /// <summary>Stops and disposes what the tests started (a second disposal does nothing).</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    /// <returns>A completed task.</returns>
    [After(Class)]
    public static Task StopEverything(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Test1Service?.Dispose();
        HostService?.Dispose();
        Test1Factory?.Dispose();
        HostFactory?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A background service that logs one record each time it is ticked, on its own flow: the flow it was
    /// started on. It also handles a request on the caller's flow.</summary>
    /// <param name="logger">The logger the service writes to.</param>
    private sealed class TickingService(ILogger logger) : BackgroundService
    {
        private readonly Channel<(string Message, TaskCompletionSource Logged)> _ticks =
            Channel.CreateUnbounded<(string Message, TaskCompletionSource Logged)>();

        /// <summary>Asks the service to log <paramref name="message"/> and waits until it has.</summary>
        /// <param name="message">The message to log.</param>
        /// <param name="cancellationToken">The test's token.</param>
        /// <returns>A task that completes when the record is logged.</returns>
        public Task TickAsync(string message, CancellationToken cancellationToken)
        {
            var logged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = _ticks.Writer.TryWrite((message, logged));
            return logged.Task.WaitAsync(cancellationToken);
        }

        /// <summary>Handles a request on the caller's flow and logs <paramref name="message"/>.</summary>
        /// <param name="message">The message to log.</param>
        public void HandleRequest(string message) => DirectLog.Write(logger, message);

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var (message, logged) in _ticks.Reader.ReadAllAsync(stoppingToken))
            {
                DirectLog.Write(logger, message);
                logged.SetResult();
            }
        }
    }
}
