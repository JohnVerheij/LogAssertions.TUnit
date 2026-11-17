using System;
using System.Collections.Generic;
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
/// built) while it runs; otherwise none. The record and its exception text are written as one string, and the check
/// that the test still runs and the write are one step, so a test that ends while its record is formatted gets
/// nothing. A record that reaches no test is counted, by level, and the first one's level and category are kept. When
/// the provider is disposed with a count above zero it writes one line saying so, with the counts per level and that
/// first record, to the test current at that moment.
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
    /// <summary>The levels in the order the skipped counts are written: most severe first.</summary>
    private static readonly LogLevel[] LevelsBySeverity =
        [LogLevel.Critical, LogLevel.Error, LogLevel.Warning, LogLevel.Information, LogLevel.Debug, LogLevel.Trace];

    private readonly int[] _skippedByLevel = new int[LevelsBySeverity.Length];
    private int _skippedRecordCount;
    private int _disposed;
    private string? _firstSkipped;

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
                $"{skipped} log record(s) were not teed to a test report ({DescribeSkipped()}): each was logged after its test had ended, or where no test was current."));
        }
    }

    /// <summary>Resolves the test the next record goes to. Allocation-free.</summary>
    /// <returns>The test, or <see langword="null"/> when the record reaches no test.</returns>
    internal TestContext? ResolveTarget() => _lifetimes.ResolveTeeTarget(TestContext.Current, _owner);

    /// <summary>Counts a record that reached no test, by level, and keeps the level and category of the first one. Only
    /// the first allocates, so skipping stays allocation-free.</summary>
    /// <param name="level">The record's level.</param>
    /// <param name="category">The record's logger category.</param>
    private void CountSkipped(LogLevel level, string category)
    {
        Interlocked.Increment(ref _skippedRecordCount);
        Interlocked.Increment(ref _skippedByLevel[SeverityIndex(level)]);
        if (Volatile.Read(ref _firstSkipped) is null)
        {
            Interlocked.CompareExchange(ref _firstSkipped, $"[{level}] {category}", comparand: null);
        }
    }

    /// <summary>The position of a level in <see cref="LevelsBySeverity"/>. The tee is enabled for every level but
    /// <see cref="LogLevel.None"/>, which the logging pipeline never passes to it; it counts with
    /// <see cref="LogLevel.Trace"/> if it is passed directly.</summary>
    /// <param name="level">The record's level.</param>
    /// <returns>The index into the per-level counts.</returns>
    private static int SeverityIndex(LogLevel level) => level switch
    {
        LogLevel.Critical => 0,
        LogLevel.Error => 1,
        LogLevel.Warning => 2,
        LogLevel.Information => 3,
        LogLevel.Debug => 4,
        _ => 5,
    };

    /// <summary>The skipped records per level, most severe first, and the first skipped record: for example
    /// <c>1 Information, 2 Debug; the first: [Information] Shop.Orders</c>.</summary>
    /// <returns>The description.</returns>
    private string DescribeSkipped()
    {
        var parts = new List<string>(LevelsBySeverity.Length);
        for (var i = 0; i < LevelsBySeverity.Length; i++)
        {
            var count = Volatile.Read(ref _skippedByLevel[i]);
            if (count > 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{count} {LevelsBySeverity[i]}"));
            }
        }

        return string.Join(", ", parts) + "; the first: " + Volatile.Read(ref _firstSkipped);
    }

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

            // Decided before formatting, so a record that reaches no test costs nothing.
            var target = _provider.ResolveTarget();
            if (target is null)
            {
                _provider.CountSkipped(logLevel, _category);
                return;
            }

            // One string, one write: a record logged on another thread meanwhile cannot land between a record and
            // its exception text.
            var line = $"[{logLevel}] {_category}: {formatter(state, exception)}";
            var text = exception is null ? line : line + Environment.NewLine + exception.ToString();
            if (!_provider._lifetimes.TryGetEntry(target, out var entry))
            {
                // Without the attribute nothing marks the test ended, so there is no end to race.
                target.Output.StandardOutput.WriteLine(text);
            }
            else if (!entry.TryWriteLine(target, text))
            {
                // The test ended while the record was formatted.
                _provider.CountSkipped(logLevel, _category);
            }
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
