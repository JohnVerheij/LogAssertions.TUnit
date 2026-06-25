using System;
using System.IO;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// An <see cref="ILoggerProvider"/> that mirrors every log record to a TUnit test's output writer as it
/// is logged, so a unit test asserting on a captured collector still shows its logs inline in the per-test
/// HTML report (which otherwise renders empty because nothing reached the test's standard output). Added
/// alongside the capturing <c>FakeLoggerProvider</c> by <see cref="TestOutputLogCollectorBuilder.CreateTeed(LogLevel)"/>
/// and <see cref="FakeLoggingBuilderExtensions.AddTeedFakeLogging"/>.
/// </summary>
/// <remarks>
/// The provider has two modes. When constructed with a captured output writer (the default for the per-test
/// builder helpers, which run on the test's own thread and pass the active test's writer), it writes to that
/// writer from any emitting thread, so a record logged on a background thread is teed rather than dropped.
/// When constructed without one, it resolves <see cref="TestContext.Current"/> at emit time and skips the
/// write where neither the AsyncLocal context nor the <c>Activity.Current</c> baggage resolves the owning
/// test - the right behavior for a provider shared across many tests, which cannot bind to a single one.
/// </remarks>
internal sealed class TestOutputLoggerProvider : ILoggerProvider
{
    private readonly TextWriter? _capturedWriter;

    /// <summary>
    /// Creates a provider that resolves the active test at emit time. A record logged where the owning test
    /// does not resolve (a background thread with no flowed context and no <c>Activity</c> baggage) is not
    /// teed. Use this when one provider serves many tests and cannot bind to a single one.
    /// </summary>
    public TestOutputLoggerProvider()
    {
    }

    /// <summary>
    /// Creates a provider bound to <paramref name="capturedWriter"/>, the active test's output writer captured
    /// once while the provider is built on the test's own thread. Records are written to that writer from any
    /// emitting thread, so background-thread logging is teed rather than dropped.
    /// </summary>
    /// <param name="capturedWriter">The owning test's output writer captured at construction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="capturedWriter"/> is null.</exception>
    public TestOutputLoggerProvider(TextWriter capturedWriter)
        => _capturedWriter = capturedWriter ?? throw new ArgumentNullException(nameof(capturedWriter));

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new TestOutputLogger(categoryName, _capturedWriter);

    /// <inheritdoc/>
    public void Dispose()
    {
        // No unmanaged or per-provider state to release; the test output writer is owned by TUnit.
    }

    private sealed class TestOutputLogger : ILogger
    {
        private readonly string _category;
        private readonly TextWriter? _capturedWriter;

        public TestOutputLogger(string category, TextWriter? capturedWriter)
        {
            _category = category;
            _capturedWriter = capturedWriter;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            // A captured writer tees from any thread; otherwise resolve the active test at emit time.
            var writer = _capturedWriter ?? TestContext.Current?.Output.StandardOutput;
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
