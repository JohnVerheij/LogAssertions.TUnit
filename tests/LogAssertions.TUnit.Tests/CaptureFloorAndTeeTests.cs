using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for the 0.8.0 capture surface: the G5 vacuous-below-floor guard on <c>HasNotLogged()</c>
/// (driven by the capture floor recorded by <see cref="LogCollectorBuilder.Create(LogLevel)"/>), and
/// the G1 live tee provided by <see cref="TestOutputLogCollectorBuilder.CreateTeed(LogLevel)"/>.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class CaptureFloorAndTeeTests
{
    // --- G5: vacuous below-floor guard ---

    [Test]
    public async Task HasNotLogged_AtLevelBelowFloor_FailsAsVacuous(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Information);
        using (factory)
        {
            var exception = await Assert.That(async () =>
                await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Trace))
                .Throws<AssertionException>();
            await Assert.That(exception!.Message).Contains("vacuously true");
            await Assert.That(exception.Message).Contains("capture floor is Information");
        }
    }

    [Test]
    public async Task HasNotLogged_AtLevelOrBelowFloor_FailsAsVacuous(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Warning);
        using (factory)
        {
            await Assert.That(async () =>
                await Assert.That(collector).HasNotLogged().AtLevelOrBelow(LogLevel.Information))
                .Throws<AssertionException>();
        }
    }

    [Test]
    public async Task HasNotLogged_AtAnyLevelAllBelowFloor_FailsAsVacuous(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Warning);
        using (factory)
        {
            await Assert.That(async () =>
                await Assert.That(collector).HasNotLogged().AtAnyLevel(LogLevel.Trace, LogLevel.Debug))
                .Throws<AssertionException>();
        }
    }

    [Test]
    public async Task HasNotLogged_ContradictoryLevelFilters_NotReportedAsVacuousFloor(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Information);
        using (factory)
        {
            // AtLevelOrBelow(Debug) + AtLevelOrAbove(Error) is an empty match set regardless of the
            // floor, so the floor guard must stay silent and the genuine absence assertion passes
            // instead of failing with a misattributed below-floor vacuity message.
            await Assert.That(collector)
                .HasNotLogged()
                .AtLevelOrBelow(LogLevel.Debug)
                .AtLevelOrAbove(LogLevel.Error);
        }
    }

    [Test]
    public async Task HasNotLogged_ExclusionLeavesOnlyBelowFloorLevels_FailsAsVacuous(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Information);
        using (factory)
        {
            // AtAnyLevel(Debug, Error) then NotAtLevel(Error) leaves a matchable set of {Debug} only,
            // which is below the Information floor, so the absence is vacuous. A contiguous min..max
            // range would see Debug..Error and miss this; the exact level set catches it.
            var exception = await Assert.That(async () =>
                await Assert.That(collector).HasNotLogged()
                    .AtAnyLevel(LogLevel.Debug, LogLevel.Error)
                    .NotAtLevel(LogLevel.Error))
                .Throws<AssertionException>();
            await Assert.That(exception!.Message).Contains("vacuously true");
        }
    }

    [Test]
    public async Task HasNotLogged_ExclusionLeavesAnAboveFloorLevel_PassesWhenAbsent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Information);
        using (factory)
        {
            // {Debug, Warning} minus Debug leaves {Warning}, at/above the floor, so the absence is
            // genuine and the guard must stay silent.
            await Assert.That(collector).HasNotLogged()
                .AtAnyLevel(LogLevel.Debug, LogLevel.Warning)
                .NotAtLevel(LogLevel.Debug);
        }
    }

    [Test]
    public async Task HasNotLogged_AtOrAboveFloor_PassesWhenGenuinelyAbsent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Information);
        using (factory)
        {
            var logger = factory.CreateLogger("Test");
            TestLogMessages.StartedProcessing(logger);
            // Warning is at/above the floor and was genuinely never logged, so this is not vacuous.
            await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Warning);
        }
    }

    [Test]
    public async Task HasNotLogged_DefaultTraceFloor_NeverVacuous(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            // Floor is Trace, so asserting absence at Trace is genuine, not vacuous.
            await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Trace);
        }
    }

    [Test]
    public async Task HasNotLogged_RawCollectorWithoutRegisteredFloor_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A collector not built by LogCollectorBuilder has no registered floor, so the guard does
        // not fire and a normal (non-vacuous) absence assertion passes.
        var collector = new FakeLogCollector();
        await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Trace);
    }

    // --- G1: live tee into the TUnit test output ---

    [Test]
    public async Task CreateTeed_CapturesAndTeesWithinTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = TestOutputLogCollectorBuilder.CreateTeed();
        using (factory)
        {
            var logger = factory.CreateLogger("TeeCategory");
            TestLogMessages.TeedMessage(logger);
            using (logger.BeginScope("a-scope"))
                TestLogMessages.TeedException(logger, new InvalidOperationException("boom"));

            await Assert.That(collector).HasLogged().Containing("teed message", StringComparison.Ordinal).Once();
            await Assert.That(collector).HasLogged().Containing("with exception", StringComparison.Ordinal).Once();
        }
    }

    [Test]
    public async Task CreateTeed_RegistersFloor_SoVacuousGuardApplies(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = TestOutputLogCollectorBuilder.CreateTeed(LogLevel.Information);
        using (factory)
        {
            var exception = await Assert.That(async () =>
                await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Trace))
                .Throws<AssertionException>();
            await Assert.That(exception!.Message).Contains("capture floor is Information");
        }
    }

    [Test]
    public async Task CreateTeed_BackgroundThread_CapturesRecord(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (factory, collector) = TestOutputLogCollectorBuilder.CreateTeed();
        using (factory)
        {
            var logger = factory.CreateLogger("BackgroundCategory");
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // Thread.UnsafeStart does not flow ExecutionContext, so TestContext.Current is null on the
            // worker thread. CreateTeed captures the owning test's output writer at construction, so the
            // record is teed to this test rather than dropped (the drop-free tee is verified directly in
            // CapturedWriter_TeesFromBackgroundThread); here we assert the collector still captures it.
            var thread = new Thread(() =>
            {
                try
                {
                    TestLogMessages.FromBackground(logger);
                    done.SetResult();
                }
#pragma warning disable CA1031 // Surface any worker-thread failure into the awaited task instead of stalling until timeout.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    done.SetException(ex);
                }
            })
            { IsBackground = true };
            thread.UnsafeStart();

            await done.Task.WaitAsync(cancellationToken);
            await Assert.That(collector).HasLogged().Containing("from background", StringComparison.Ordinal).Once();
        }
    }

    /// <summary>Proves the capture-at-construction mode: a provider bound to a captured writer tees a
    /// record logged on a thread where <c>TestContext.Current</c> is null, instead of dropping it.</summary>
    [Test]
    public async Task CapturedWriter_TeesFromBackgroundThread(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var writer = new StringWriter();
        using var provider = new TestOutputLoggerProvider(writer);
        var logger = provider.CreateLogger("BackgroundCategory");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // UnsafeStart does not flow ExecutionContext, so TestContext.Current is null on the worker; the
        // captured writer must still receive the record (emit-time resolution would drop it here).
        var thread = new Thread(() =>
        {
            try
            {
                logger.Log(LogLevel.Information, default, "from worker thread", null, static (s, _) => s);
                done.SetResult();
            }
#pragma warning disable CA1031 // Surface any worker-thread failure into the awaited task instead of stalling until timeout.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                done.SetException(ex);
            }
        })
        { IsBackground = true };
        thread.UnsafeStart();

        await done.Task.WaitAsync(cancellationToken);
        await Assert.That(writer.ToString()).Contains("from worker thread", StringComparison.Ordinal);
    }

    /// <summary>The emit-time mode (no captured writer) drops a record logged where no test resolves,
    /// rather than throwing - the documented shared-host fallback.</summary>
    [Test]
    public async Task EmitTimeProvider_OffContext_SkipsWithoutThrowing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var provider = new TestOutputLoggerProvider();
        var logger = provider.CreateLogger("BackgroundCategory");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                logger.Log(LogLevel.Information, default, "dropped", null, static (s, _) => s);
                done.SetResult();
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                done.SetException(ex);
            }
        })
        { IsBackground = true };
        thread.UnsafeStart();

        // Completes without throwing: the off-context write is skipped, not an error.
        await done.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Directly exercises the internal tee provider's interface members, which the
    /// LoggerFactory dispatch path does not all reach (IsEnabled, BeginScope, Dispose).</summary>
    [Test]
    public async Task TestOutputLoggerProvider_InterfaceMembers_Behave(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var provider = new TestOutputLoggerProvider();
        var logger = provider.CreateLogger("DirectCategory");

        await Assert.That(logger.IsEnabled(LogLevel.Information)).IsTrue();
        await Assert.That(logger.IsEnabled(LogLevel.None)).IsFalse();

        using (logger.BeginScope("a-scope"))
            logger.Log(LogLevel.Warning, default, "direct state", new InvalidOperationException("x"), static (s, _) => s);
    }
}
