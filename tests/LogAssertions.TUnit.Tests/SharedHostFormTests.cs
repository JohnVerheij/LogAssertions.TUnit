using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The shared-host recipe of the README, run as written from a <c>[Before(Class)]</c> hook: build and start the host in
/// <c>Task.Run</c> inside <c>ExecutionContext.SuppressFlow()</c>, then <c>await</c> it after the block. The calling
/// thread of the hook still has the first test as current, so building the host on it would make that test the owner of
/// the tee; work queued inside the block starts without a test. The hook must not throw, the host's loop must carry no
/// test context, and its records must reach no test, the first test included while it runs, while a record made on a
/// test's own flow reaches that test.
/// </summary>
[NotInParallel(nameof(SharedHostFormTests))]
[Timeout(15_000)]
internal sealed class SharedHostFormTests
{
    /// <summary>Gets or sets the host started by the hook.</summary>
    private static SharedHost? Host { get; set; }

    /// <summary>Gets or sets the gate that releases the loop's record.</summary>
    private static SemaphoreSlim? LoopGate { get; set; }

    /// <summary>Gets or sets the completion the loop sets once it has logged.</summary>
    private static TaskCompletionSource? LoopLogged { get; set; }

    /// <summary>Gets or sets the exception the recipe threw in the hook, if any.</summary>
    private static Exception? HookError { get; set; }

    /// <summary>Gets or sets the first test of the class, the one whose flow ran the hook.</summary>
    private static TestContext? FirstTest { get; set; }

    /// <summary>Starts the host as the README shows. The hook runs on the flow of the first test of the class.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    /// <returns>A task that completes when the host has started.</returns>
    [Before(Class)]
    public static async Task StartTheSharedHost(CancellationToken cancellationToken)
    {
        try
        {
            LoopGate = new SemaphoreSlim(0);
            LoopLogged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<SharedHost> started;
            using (ExecutionContext.SuppressFlow())
            {
                started = Task.Run(async () =>
                {
                    var built = BuildHost();                        // its tee has no owning test
                    await built.StartAsync(cancellationToken);      // its loops carry no test context
                    return built;
                }, cancellationToken);
            }

            Host = await started;
        }
        catch (InvalidOperationException exception)
        {
            HookError = exception;
        }
    }

    /// <summary>Releases the host and the gate after the last test of the class.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    [After(Class)]
    public static void StopTheSharedHost(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Host?.Dispose();
        LoopGate?.Dispose();
    }

    /// <summary>The recipe ran without an exception, the host is up, and its loop logs while this test, the first of the
    /// class and the one whose flow ran the hook, is still running: the record reaches no test, this one included.</summary>
    [Test]
    public async Task TheHook_StartedTheHost_AndItsLoopRecordReachesNoTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FirstTest = TestContext.Current;

        await Assert.That(HookError).IsNull();
        await Assert.That(Host).IsNotNull();

        LoopGate!.Release();
        await LoopLogged!.Task.WaitAsync(cancellationToken);

        await Assert.That(FirstTest!.GetStandardOutput()).DoesNotContain("record of the host loop", StringComparison.Ordinal);
    }

    /// <summary>A record made on a test's own flow through the shared host's factory reaches that test.</summary>
    [Test]
    [DependsOn(nameof(TheHook_StartedTheHost_AndItsLoopRecordReachesNoTest))]
    public async Task ATestsOwnRecord_ReachesThatTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DirectLog.Write(Host!.Factory.CreateLogger("Request"), "record of a request this test made");

        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("record of a request this test made", StringComparison.Ordinal);
    }

    /// <summary>Builds the host: a logger factory with the tee, and a loop that logs once the gate opens.</summary>
    /// <returns>The host, not started.</returns>
    private static SharedHost BuildHost() => new(LoopGate!, LoopLogged!);

    /// <summary>A host whose start completes asynchronously and leaves a loop running.</summary>
    private sealed class SharedHost : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private readonly TaskCompletionSource _logged;

        /// <summary>Initializes the host with a logger factory that has the tee.</summary>
        /// <param name="gate">Releases the loop's record.</param>
        /// <param name="logged">Completed when the loop has logged.</param>
        public SharedHost(SemaphoreSlim gate, TaskCompletionSource logged)
        {
            _gate = gate;
            _logged = logged;
            var collector = new FakeLogCollector();
            Factory = LoggerFactory.Create(builder => builder.AddTeedFakeLogging(collector));
        }

        /// <summary>Gets the host's logger factory.</summary>
        public ILoggerFactory Factory { get; }

        /// <summary>Starts the host: yields, as a real start does while it opens a connection, then starts the loop.</summary>
        /// <param name="cancellationToken">The start's token.</param>
        /// <returns>A task that completes when the loop is running.</returns>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            var logger = Factory.CreateLogger("HostLoop");
            _ = Task.Run(
                async () =>
                {
                    await _gate.WaitAsync(CancellationToken.None);
                    DirectLog.Write(logger, "record of the host loop");
                    _logged.SetResult();
                },
                CancellationToken.None);
            await Task.Delay(1, cancellationToken);
        }

        /// <summary>Disposes the host's logger factory.</summary>
        public void Dispose() => Factory.Dispose();
    }
}
