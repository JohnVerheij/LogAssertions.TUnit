using System;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// An <see cref="ILoggerProvider"/> that mirrors every log record to a TUnit test's output as it is logged, so a test
/// asserting on a captured collector still shows its logs inline in the per-test report. Added alongside the capturing
/// <c>FakeLoggerProvider</c> by <see cref="TestOutputLogCollectorBuilder.CreateTeed(LogLevel)"/> and
/// <see cref="FakeLoggingBuilderExtensions.AddTeedFakeLogging"/>, and on its own by
/// <see cref="TestOutputLogCollectorBuilder.CreateTeedDiscarding(LogLevel)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each record goes to the test chosen by <see cref="TestLifetimeTable.ResolveTeeTarget"/>: the test current at the log
/// call, unless it has ended (then none); when no test is current, the owning test (current when the provider was
/// built) while it runs; otherwise none. A record that
/// reaches no test is counted, and when the provider is disposed with a count above zero it writes one line saying so
/// to the test current at that moment.
/// </para>
/// <para>
/// The provider must be registered so that the logging container disposes it (through a factory delegate, not as an
/// instance), or the count line is never written.
/// </para>
/// </remarks>
internal sealed class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly TestContext? _owner;
    private readonly TestLifetimeTable _lifetimes;
    private int _skippedRecordCount;
    private int _disposed;

    /// <summary>Creates a provider owned by <paramref name="owner"/>, routed by the shared lifetime table.</summary>
    /// <param name="owner">The test current when the tee is built, or <see langword="null"/> when none is.</param>
    public TestOutputLoggerProvider(TestContext? owner)
        : this(owner, TestLifetimeTable.Shared)
    {
    }

    /// <summary>Creates a provider owned by <paramref name="owner"/>, routed by <paramref name="lifetimes"/>.</summary>
    /// <param name="owner">The test current when the tee is built, or <see langword="null"/> when none is.</param>
    /// <param name="lifetimes">The table that knows which tests are running.</param>
    internal TestOutputLoggerProvider(TestContext? owner, TestLifetimeTable lifetimes)
    {
        _owner = owner;
        _lifetimes = lifetimes;
    }

    /// <summary>Gets the number of records that reached no test.</summary>
    internal int SkippedRecordCount => Volatile.Read(ref _skippedRecordCount);

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(categoryName, this);

    /// <summary>Writes the count of records that reached no test, when there were any, to the test current now. A
    /// second call does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is not 0)
        {
            return;
        }

        var skipped = SkippedRecordCount;
        if (skipped > 0)
        {
            TestContext.Current?.Output.StandardOutput.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{skipped} log record(s) were not teed to a test report: each was logged after its test had ended, or where no test was current."));
        }
    }

    /// <summary>Resolves the test the next record goes to. Allocation-free.</summary>
    /// <returns>The test, or <see langword="null"/> when the record reaches no test.</returns>
    internal TestContext? ResolveTarget() => _lifetimes.ResolveTeeTarget(TestContext.Current, _owner);

    /// <summary>Counts a record that reached no test.</summary>
    private void CountSkipped() => Interlocked.Increment(ref _skippedRecordCount);

    private sealed class TestOutputLogger : ILogger
    {
        private readonly string _category;
        private readonly TestOutputLoggerProvider _provider;

        public TestOutputLogger(string category, TestOutputLoggerProvider provider)
        {
            _category = category;
            _provider = provider;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var target = _provider.ResolveTarget();
            if (target is null)
            {
                _provider.CountSkipped();
                return;
            }

            // The writer is fetched only when a record is written: TUnit allocates on each access.
            var writer = target.Output.StandardOutput;
            writer.WriteLine($"[{logLevel}] {_category}: {formatter(state, exception)}");
            if (exception is not null)
                writer.WriteLine(exception.ToString());
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
            // A logging scope this tee does not track; nothing to release.
        }
    }
}
