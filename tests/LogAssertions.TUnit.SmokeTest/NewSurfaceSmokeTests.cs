using LogAssertions.Render;

namespace Smoke.Consumer;

/// <summary>
/// Pins the 0.14.0 surface for an external consumer, through the packed package: <c>LogCapture</c> from
/// <c>LogCollectorBuilder.Create()</c>, <c>WillLog</c>, <c>AtMostOncePer</c>, <c>LogContractRenderer</c>, and
/// <c>CreateTeedDiscarding</c>. This project does not apply <c>LogAssertionsTestLifetime</c>, so it also pins what a
/// consumer without the attribute gets: <c>DumpToTestOutputOnFailure()</c> throws with the line to add, the run gets
/// one note naming that line, and the tee uses the fallback rule. The Native AOT gate compiles and runs these ahead of
/// time.
/// </summary>
[Category("ConsumerSurface")]
[Timeout(10_000)]
[NotInParallel(nameof(NewSurfaceSmokeTests))]
internal sealed class NewSurfaceSmokeTests
{
    /// <summary>Gets or sets the teed factory built in <see cref="A_BuildsATeedFactory"/>.</summary>
    private static ILoggerFactory? Teed { get; set; }

    /// <summary>Gets or sets the context of <see cref="A_BuildsATeedFactory"/>.</summary>
    private static TestContext? Owner { get; set; }

    /// <summary>The capture is the factory; <c>WillLog</c> waits for background work; <c>AtMostOncePer</c> checks
    /// spacing; the contract renderer writes one line per definition.</summary>
    [Test]
    public async Task CaptureWillLogRateAndContractResolveAndRunAsync(CancellationToken cancellationToken)
    {
        using var logs = LogCollectorBuilder.Create();
        var logger = logs.CreateLogger("Worker");
        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                logger.LogWarning("worker ready");
            },
            cancellationToken);

        await Assert.That(logs.Collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken).Containing("ready", StringComparison.Ordinal).Once();
        await Assert.That(logs.Collector).HasLogged().Containing("ready", StringComparison.Ordinal).AtMostOncePer(TimeSpan.FromMinutes(1));

        var contract = LogContractRenderer.Render([LogDefinition.Capture(static log => SmokeLogMessages.Ready(log, "w"))]);
        await Assert.That(contract).IsEqualTo("7 Ready Information \"worker {Name} ready\" Name\n");

        using var discarding = global::LogAssertions.TUnit.TestOutputLogCollectorBuilder.CreateTeedDiscarding(LogLevel.Information);
        await Assert.That(discarding.CreateLogger("Quiet").IsEnabled(LogLevel.Information)).IsTrue();

        using var clocked = LogCollectorBuilder.Create(TimeProvider.System, LogLevel.Information);
        using var teedClocked = global::LogAssertions.TUnit.TestOutputLogCollectorBuilder.CreateTeed(TimeProvider.System);
        await Assert.That(clocked.CreateLogger("Clock").IsEnabled(LogLevel.Debug)).IsFalse();
        await Assert.That(teedClocked.Collector.Count).IsEqualTo(0);
    }

    /// <summary>Without the attribute, arming a failure dump throws and names the line to add.</summary>
    [Test]
    public async Task WithoutTheAttribute_DumpToTestOutputOnFailureThrowsWithTheLineToAddAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? message = null;
        try
        {
            global::LogAssertions.TUnit.FakeLogCollectorTUnitInspectionExtensions.DumpToTestOutputOnFailure(new FakeLogCollector());
        }
        catch (InvalidOperationException ex)
        {
            message = ex.Message;
        }

        await Assert.That(message).IsNotNull();
        await Assert.That(message!).Contains("[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]", StringComparison.Ordinal);
    }

    /// <summary>Without the attribute, the first tee built in the run writes one note, to the test that built it, naming
    /// the line to add; later tees write none. Runs alone, so no tee is being built meanwhile.</summary>
    [Test]
    [NotInParallel]
    public async Task WithoutTheAttribute_TheRunGetsOneNoteNamingTheLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var first = global::LogAssertions.TUnit.TestOutputLogCollectorBuilder.CreateTeed();
        using var second = global::LogAssertions.TUnit.TestOutputLogCollectorBuilder.CreateTeed();

        var noted = 0;
        foreach (var test in TestContext.Current!.ClassContext.AssemblyContext.AllTests)
        {
            var output = test.GetStandardOutput();
            const string Note = "does not apply the LogAssertionsTestLifetime attribute";
            if (output.Contains(Note, StringComparison.Ordinal))
            {
                noted++;
                await Assert.That(output.IndexOf(Note, StringComparison.Ordinal)).IsEqualTo(output.LastIndexOf(Note, StringComparison.Ordinal));
                await Assert.That(output).Contains("[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]", StringComparison.Ordinal);
            }
        }

        await Assert.That(noted).IsEqualTo(1);
    }

    /// <summary>Gets or sets the gate that releases the loop started in <see cref="A_BuildsATeedFactory"/>.</summary>
    private static SemaphoreSlim? LoopGate { get; set; }

    /// <summary>Gets or sets the completion the loop sets once it has logged.</summary>
    private static TaskCompletionSource? LoopDone { get; set; }

    /// <summary>Builds a teed factory owned by this test and starts a loop on this test's flow, for the fallback pins
    /// below.</summary>
    [Test]
    public async Task A_BuildsATeedFactory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Owner = TestContext.Current;
        Teed = LoggerFactory.Create(b => global::LogAssertions.TUnit.FakeLoggingBuilderExtensions.AddTeedFakeLogging(b, new FakeLogCollector()));
        LoopGate = new SemaphoreSlim(0);
        var gate = LoopGate;
        var logger = Teed.CreateLogger("Loop");
        _ = Task.Run(
            async () =>
            {
                await gate.WaitAsync(CancellationToken.None);
                logger.LogInformation("loop record of the ended owner");
                LoopDone!.SetResult();
            },
            CancellationToken.None);
        await Assert.That(Owner).IsNotNull();
    }

    /// <summary>Documented behavior without the attribute: a loop keeps the context of the test that started it, so
    /// its record still goes to that test after the test has ended (the loop trap of the README).</summary>
    [Test]
    [DependsOn(nameof(A_BuildsATeedFactory))]
    public async Task WithoutTheAttribute_LoopOfAnEndedTest_StillTeesToItAsync(CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        LoopDone = done;
        LoopGate!.Release();
        await done.Task.WaitAsync(cancellationToken);

        await Assert.That(Owner!.GetStandardOutput()).Contains("loop record of the ended owner", StringComparison.Ordinal);
        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain("loop record of the ended owner", StringComparison.Ordinal);
        LoopGate.Dispose();
    }

    /// <summary>Without the attribute the owning test counts as ended once it has a result: a record logged where no
    /// test is current reaches neither test, while a record on this test's own flow reaches this test.</summary>
    [Test]
    [DependsOn(nameof(WithoutTheAttribute_LoopOfAnEndedTest_StillTeesToItAsync))]
    public async Task WithoutTheAttribute_OffContextRecordAfterTheOwnerEnded_IsNotTeedAsync(CancellationToken cancellationToken)
    {
        var logger = Teed!.CreateLogger("Fallback");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            logger.LogInformation("off-context fallback record");
            done.SetResult();
        })
        { IsBackground = true };
        thread.UnsafeStart();
        await done.Task.WaitAsync(cancellationToken);
        logger.LogInformation("own-flow fallback record");

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(output).Contains("own-flow fallback record", StringComparison.Ordinal);
        await Assert.That(output).DoesNotContain("off-context fallback record", StringComparison.Ordinal);
        await Assert.That(Owner!.GetStandardOutput()).DoesNotContain("off-context fallback record", StringComparison.Ordinal);
        Teed.Dispose();
    }
}
