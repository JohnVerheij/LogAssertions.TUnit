using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Tests;

/// <summary>Tests for <see cref="LogCapture"/>, as returned by <see cref="LogCollectorBuilder.Create(LogLevel)"/>: it is
/// the factory, its loggers write into its collector, and disposing it disposes the factory while the collector keeps
/// its records.</summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogCaptureTests
{
    /// <summary>The capture is the logger factory: a logger it creates writes into <see cref="LogCapture.Collector"/>,
    /// at the minimum level and above.</summary>
    [Test]
    public async Task Create_LoggersWriteIntoTheCollector(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var logs = LogCollectorBuilder.Create(LogLevel.Information);

        // CreateLogger<T> is an extension on ILoggerFactory: the capture is the factory.
        var logger = logs.CreateLogger<LogCaptureTests>();
        logger.Log(LogLevel.Debug, default, "below the minimum", null, static (state, _) => state);
        logger.Log(LogLevel.Information, default, "captured", null, static (state, _) => state);

        await Assert.That(logs.Collector.Count).IsEqualTo(1);
        await Assert.That(logs.Collector.LatestRecord.Message).IsEqualTo("captured");
        await Assert.That(logs.Collector.LatestRecord.Category).IsEqualTo("LogAssertions.Tests.LogCaptureTests");
    }

    /// <summary>A provider added to the capture receives the records too.</summary>
    [Test]
    public async Task AddProvider_AddedProviderReceivesTheRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var logs = LogCollectorBuilder.Create();
        var added = new FakeLogCollector();
        using var provider = new FakeLoggerProvider(added);

        logs.AddProvider(provider);
        logs.CreateLogger("Added").Log(LogLevel.Information, default, "to both", null, static (state, _) => state);

        await Assert.That(logs.Collector.Count).IsEqualTo(1);
        await Assert.That(added.Count).IsEqualTo(1);
    }

    /// <summary>Disposing the capture disposes a provider added to it.</summary>
    [Test]
    public async Task Dispose_DisposesAnAddedProvider(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logs = LogCollectorBuilder.Create();
        using var provider = new DisposalProbe();
        logs.AddProvider(provider);

        await logs.DisposeAsync();

        await Assert.That(provider.Disposed).IsTrue();
    }

    /// <summary>Disposing the capture disposes the factory (a later logger can't be created); the collector keeps its
    /// records; disposing again, also asynchronously, does nothing.</summary>
    [Test]
    public async Task Dispose_DisposesTheFactory_TheCollectorKeepsItsRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logs = LogCollectorBuilder.Create();
        logs.CreateLogger("Disposed").Log(LogLevel.Information, default, "kept", null, static (state, _) => state);

        DisposeSynchronously(logs);
        await logs.DisposeAsync();

        await Assert.That(() => logs.CreateLogger("After")).Throws<ObjectDisposedException>();
        await Assert.That(logs.Collector.LatestRecord.Message).IsEqualTo("kept");
    }

    /// <summary>Disposal does not stop capture: a logger created before it keeps writing into the collector (the README
    /// says so; it used to claim the opposite).</summary>
    [Test]
    public async Task Dispose_LoggerCreatedBefore_KeepsWritingIntoTheCollector(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logs = LogCollectorBuilder.Create();
        var logger = logs.CreateLogger("Before");
        await logs.DisposeAsync();

        logger.Log(LogLevel.Information, default, "after disposal", null, static (state, _) => state);

        await Assert.That(logs.Collector.LatestRecord.Message).IsEqualTo("after disposal");
    }

    /// <summary><see langword="await"/> <see langword="using"/> disposes the capture.</summary>
    [Test]
    public async Task DisposeAsync_DisposesTheFactory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logs = LogCollectorBuilder.Create();
        await using (logs)
        {
            logs.CreateLogger("Async").Log(LogLevel.Information, default, "logged", null, static (state, _) => state);
        }

        await Assert.That(() => logs.CreateLogger("After")).Throws<ObjectDisposedException>();
        await Assert.That(logs.Collector.Count).IsEqualTo(1);
    }

    /// <summary>After disposal a provider can't be added either, as the member documents.</summary>
    [Test]
    public async Task Dispose_AddProviderAfterwards_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logs = LogCollectorBuilder.Create();
        await logs.DisposeAsync();
        using var provider = new DisposalProbe();

        await Assert.That(() => logs.AddProvider(provider)).Throws<ObjectDisposedException>();
    }

    /// <summary>One capture shared by work on eight threads at once (as parallel tests sharing a class-level capture
    /// log): every record is captured exactly once.</summary>
    [Test]
    public async Task SharedCapture_ConcurrentLoggers_CaptureEveryRecordOnce(CancellationToken cancellationToken)
    {
        const int Threads = 8;
        const int PerThread = 500;
        using var logs = LogCollectorBuilder.Create();
        using var start = new ManualResetEventSlim();

        var writers = new Task[Threads];
        for (var thread = 0; thread < Threads; thread++)
        {
            var logger = logs.CreateLogger($"Thread{thread}");
            var lines = new string[PerThread];
            for (var i = 0; i < PerThread; i++)
                lines[i] = $"{thread}-{i}";
            writers[thread] = Task.Run(
                () =>
                {
                    start.Wait(cancellationToken);
                    foreach (var line in lines)
                        logger.Log(LogLevel.Information, default, line, null, static (state, _) => state);
                },
                cancellationToken);
        }

        start.Set();
        await Task.WhenAll(writers);

        var messages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in logs.Collector.GetSnapshot())
            messages.Add(record.Message);
        await Assert.That(logs.Collector.Count).IsEqualTo(Threads * PerThread);
        await Assert.That(messages.Count).IsEqualTo(Threads * PerThread);
    }

    /// <summary><see langword="null"/> arguments are rejected.</summary>
    [Test]
    public async Task NullArguments_Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var logs = LogCollectorBuilder.Create();

        await Assert.That(() => logs.CreateLogger(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => logs.AddProvider(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogCollectorBuilder.Create(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>With a clock, each record's timestamp comes from that clock, at the minimum level and above; without one,
    /// timestamps come from the system clock.</summary>
    [Test]
    public async Task Create_WithAClock_StampsEachRecordWithItsTime(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var noon = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        using var faked = LogCollectorBuilder.Create(new FixedClock(noon), LogLevel.Information);
        using var system = LogCollectorBuilder.Create();

        var faker = faked.CreateLogger("Clock");
        faker.Log(LogLevel.Debug, default, "below the floor", null, static (state, _) => state);
        faker.Log(LogLevel.Information, default, "on the fake clock", null, static (state, _) => state);
        var before = DateTimeOffset.UtcNow;
        system.CreateLogger("Clock").Log(LogLevel.Information, default, "on the system clock", null, static (state, _) => state);
        var after = DateTimeOffset.UtcNow;

        await Assert.That(faked.Collector.Count).IsEqualTo(1);
        await Assert.That(faked.Collector.LatestRecord.Timestamp).IsEqualTo(noon);
        await Assert.That(system.Collector.LatestRecord.Timestamp).IsBetween(before, after);
    }

    /// <summary>Disposes the capture through <see cref="IDisposable.Dispose"/>, twice.</summary>
    /// <param name="logs">The capture.</param>
    private static void DisposeSynchronously(LogCapture logs)
    {
        logs.Dispose();
        logs.Dispose();
    }

    /// <summary>A provider that records whether it was disposed.</summary>
    private sealed class DisposalProbe : ILoggerProvider
    {
        /// <summary>Gets a value indicating whether the provider was disposed.</summary>
        public bool Disposed { get; private set; }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new FakeLogger(category: categoryName);

        /// <inheritdoc/>
        public void Dispose() => Disposed = true;
    }

    /// <summary>A clock that always reads <paramref name="now"/>.</summary>
    /// <param name="now">The time it reads.</param>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => now;
    }
}
