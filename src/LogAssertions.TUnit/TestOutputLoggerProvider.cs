using System;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// An <see cref="ILoggerProvider"/> that mirrors every log record to the active TUnit test's output
/// writer as it is logged, so a unit test asserting on a captured collector still shows its logs
/// inline in the per-test HTML report (which otherwise renders empty because nothing reached the
/// test's standard output). Added alongside the capturing <c>FakeLoggerProvider</c> by
/// <see cref="TestOutputLogCollectorBuilder.CreateTeed(LogLevel)"/>.
/// </summary>
/// <remarks>
/// The owning <see cref="ILoggerFactory"/> applies the configured minimum level before dispatching to
/// providers, so this tee receives exactly the records the collector captures. Writes are skipped
/// when <see cref="TestContext.Current"/> is <see langword="null"/> (a background thread, where the
/// AsyncLocal context does not flow), so off-context logging is silently dropped rather than throwing.
/// </remarks>
internal sealed class TestOutputLoggerProvider : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
        // No unmanaged or per-provider state to release; the test output writer is owned by TUnit.
    }

    private sealed class TestOutputLogger : ILogger
    {
        private readonly string _category;

        public TestOutputLogger(string category) => _category = category;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var writer = TestContext.Current?.Output.StandardOutput;
            if (writer is null)
                return;

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
