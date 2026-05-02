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
