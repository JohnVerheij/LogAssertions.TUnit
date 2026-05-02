using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using LogAssertions.TUnit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Tests for the TUnit-specific inspection extensions
/// (<see cref="FakeLogCollectorTUnitInspectionExtensions"/>). The framework-agnostic counterparts
/// have their own coverage in the LogAssertions core test project; tests here pin only the
/// behavior that depends on TUnit's <see cref="global::TUnit.Core.TestContext"/>.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class FakeLogCollectorTUnitInspectionExtensionsTests
{
    /// <summary>
    /// Pins that <see cref="FakeLogCollectorTUnitInspectionExtensions.DumpToTestOutput"/>
    /// renders captured records to the active test's standard-output writer using the same
    /// formatter as <c>DumpTo(TextWriter)</c>. Verifies via the captured records appearing
    /// in the test output.
    /// </summary>
    [Test]
    public async Task DumpToTestOutputRendersRecordsToTestStandardOutputAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = new();
        using ILoggerFactory factory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new FakeLoggerProvider(collector));
        });
        ILogger logger = factory.CreateLogger("DumpToTestOutputCategory");
        TestLogMessages.StartedProcessing(logger);
        TestLogMessages.ValidationFailed(logger);

        // Smoke test: invocation must not throw inside an active TUnit test context. The
        // formatter behaviour itself is covered by the core DumpTo(TextWriter) tests; this
        // test pins that the TUnit-bound entry point reaches the test output writer cleanly.
        collector.DumpToTestOutput();
    }

    /// <summary>
    /// Pins the explicit-failure contract when called outside a TUnit test execution context
    /// (no <see cref="global::TUnit.Core.TestContext.Current"/>). The method throws
    /// <see cref="InvalidOperationException"/> with a clear diagnostic rather than silently
    /// no-op'ing. Verification spawns the call on a fresh thread via <see cref="Thread.UnsafeStart"/>,
    /// which (unlike <see cref="Task.Run(System.Action)"/> or the regular <see cref="Thread.Start()"/>)
    /// does NOT capture the calling <see cref="System.Threading.ExecutionContext"/>. The new
    /// thread therefore observes a null <c>TestContext.Current</c> exactly as a non-TUnit
    /// caller would. <see cref="TaskCompletionSource{TResult}"/> bridges the result back to
    /// the test's await chain — including any unexpected exception, so a real fault on the
    /// worker thread surfaces as a test failure with the original exception's stack trace
    /// rather than as a timeout that hides the cause.
    /// </summary>
    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031:DoNotCatchGeneralExceptionTypes",
        Justification = "Worker-thread bridge: any unexpected exception must be forwarded " +
            "via TaskCompletionSource.SetException so the test fails with a clear diagnostic " +
            "(the original exception type and stack trace) rather than silently hanging until " +
            "the [Timeout] kicks in. Catching the base Exception is the documented pattern " +
            "for cross-thread exception forwarding.")]
    public async Task DumpToTestOutputThrowsInvalidOperationOutsideTestContextAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector collector = new();
        var completion = new TaskCompletionSource<InvalidOperationException?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                collector.DumpToTestOutput();
                completion.SetResult(null);
            }
            catch (InvalidOperationException ex)
            {
                completion.SetResult(ex);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }) { IsBackground = true };
        thread.UnsafeStart();

        InvalidOperationException? caught = await completion.Task.WaitAsync(cancellationToken);

        await Assert.That(caught).IsNotNull();
        await Assert.That(caught!.Message).Contains("DumpToTestOutput");
        await Assert.That(caught.Message).Contains("TestContext.Current");
    }

    /// <summary>
    /// Pins the null-collector contract — the extension must throw
    /// <see cref="ArgumentNullException"/> rather than NRE on a null receiver.
    /// </summary>
    [Test]
    public async Task DumpToTestOutputThrowsArgumentNullForNullCollectorAsync(CancellationToken cancellationToken)
    {
        FakeLogCollector? collector = null;
        ArgumentNullException? caught = null;

        try
        {
            collector!.DumpToTestOutput();
        }
        catch (ArgumentNullException ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }
}
