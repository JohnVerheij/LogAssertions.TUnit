using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for the 0.9.0 <see cref="ILoggingBuilder"/> helpers - <c>AddFakeLogging</c> and
/// <c>AddTeedFakeLogging</c> - and the tee-provider seam that lets a consumer plug in its own
/// display/correlation provider without LogAssertions taking a dependency on it.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class FakeLoggingBuilderExtensionsTests
{
    [Test]
    public async Task AddTeedFakeLogging_RegistersFloor_SoVacuousGuardApplies(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        using var factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector, LogLevel.Information));

        var exception = await Assert.That(async () =>
            await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Trace))
            .Throws<AssertionException>();
        await Assert.That(exception!.Message).Contains("capture floor is Information");
    }

    /// <summary>Another provider is chained on the builder (the documented replacement of the removed
    /// <c>teeProvider</c> parameter): it receives the record, and so do the capture and the built-in tee.</summary>
    [Test]
    public async Task AddTeedFakeLogging_ChainedProvider_ReceivesTheRecordToo(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = new FakeLogCollector();
        var chained = new FakeLogCollector();

        using var factory = LoggerFactory.Create(b =>
            b.AddTeedFakeLogging(captured).AddProvider(new FakeLoggerProvider(chained)));
        var logger = factory.CreateLogger("Category");
        TestLogMessages.TeedMessage(logger);

        await Assert.That(captured).HasLogged().Containing("teed message", StringComparison.Ordinal).Once();
        await Assert.That(chained).HasLogged().Containing("teed message", StringComparison.Ordinal).Once();
        await Assert.That(TestContext.Current!.GetStandardOutput()).Contains("Category: teed message", StringComparison.Ordinal);
    }

    [Test]
    public async Task AddFakeLogging_CapturesAndRegistersFloor_WithoutTee(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        using var factory = LoggerFactory.Create(b => b.AddFakeLogging(collector, LogLevel.Information));
        var logger = factory.CreateLogger("Category");

        TestLogMessages.StartedProcessing(logger);

        await Assert.That(collector).HasLogged().Containing("Started processing", StringComparison.Ordinal).Once();
        var exception = await Assert.That(async () =>
            await Assert.That(collector).HasNotLogged().AtLevel(LogLevel.Trace))
            .Throws<AssertionException>();
        await Assert.That(exception!.Message).Contains("capture floor is Information");
    }

    [Test]
    public async Task AddFakeLogging_NullBuilder_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        await Assert.That(() => FakeLoggingBuilderExtensions.AddFakeLogging(null!, collector))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddTeedFakeLogging_NullCollector_ThrowsArgumentNull(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Assert.That(() => LoggerFactory.Create(b => b.AddTeedFakeLogging(null!, LogLevel.Trace)))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AddTeedFakeLogging_NullCollector_RegistersNoProvider(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = -1;
        var after = -1;
        Exception? thrown = null;

        using var factory = LoggerFactory.Create(b =>
        {
            before = b.Services.Count(static d => d.ServiceType == typeof(ILoggerProvider));
            try
            {
                b.AddTeedFakeLogging(null!);
            }
            catch (ArgumentNullException exception)
            {
                thrown = exception;
            }

            after = b.Services.Count(static d => d.ServiceType == typeof(ILoggerProvider));
        });

        await Assert.That(thrown).IsNotNull();
        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    public async Task AddTeedFakeLogging_TheTeeWritesARecord_BeforeTheCollectorStoresIt(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var test = TestContext.Current!;
        string? reportWhenStored = null;

        // The collector calls its sink when it stores a record: the report is read at that moment.
        var collector = FakeLogCollector.Create(new FakeLogCollectorOptions
        {
            OutputSink = _ => reportWhenStored ??= test.GetStandardOutput(),
        });
        using var factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector));

        DirectLog.Write(factory.CreateLogger("Order"), "written before it is stored");

        await Assert.That(reportWhenStored).IsNotNull();
        await Assert.That(reportWhenStored!).Contains("Order: written before it is stored", StringComparison.Ordinal);
    }

    [Test]
    public async Task AddTeedFakeLogging_ARecordFromAnotherFlow_IsInTheReport_OnceTheTestHasSeenIt(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        using var factory = LoggerFactory.Create(b => b.AddTeedFakeLogging(collector));
        var logger = factory.CreateLogger("Check");
        var failure = new InvalidOperationException("the check failed");

        // A background check without the test's flow reports an error; the test waits for it in the collector and looks
        // at its report at once, as a test that ends right after seeing the record would.
        var check = OffContext.RunAsync(
            () => logger.Log(LogLevel.Error, default, "check reported an error", failure, static (state, _) => state),
            cancellationToken);
        await collector.WaitForLogAsync(static record => record.Level == LogLevel.Error, cancellationToken);
        var report = TestContext.Current!.GetStandardOutput();
        await check;

        await Assert.That(report).Contains("[Error] Check: check reported an error", StringComparison.Ordinal);
        await Assert.That(report).Contains("InvalidOperationException: the check failed", StringComparison.Ordinal);
    }
}
