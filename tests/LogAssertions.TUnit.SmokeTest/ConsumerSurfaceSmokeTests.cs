namespace Smoke.Consumer;

/// <summary>
/// Smoke tests proving that an external consumer can adopt LogAssertions.TUnit purely via
/// the README's recommended GlobalUsings.cs snippet — no extra <c>using LogAssertions.TUnit;</c>
/// directive, no other wiring. The test class lives in <c>Smoke.Consumer</c> deliberately:
/// LogAssertions.TUnit's own test project is in the <c>LogAssertions.TUnit.Tests</c> namespace,
/// which inherits parent-namespace visibility into <c>LogAssertions.TUnit</c> — that
/// inheritance masked the v0.2.0/v0.2.1 shorthand-resolution bug. By placing this file in a
/// namespace with NO parent relationship to LogAssertions.TUnit, this project is the canonical
/// regression coverage for the resolution-pathway bug class.
/// </summary>
[Category("ConsumerSurface")]
[Timeout(10_000)]
internal sealed class ConsumerSurfaceSmokeTests
{
    /// <summary>
    /// Pins that the core entry point <c>HasLogged()</c> resolves cleanly for an external
    /// consumer using only the README's GlobalUsings snippet. Emitted by TUnit's source
    /// generator into <c>TUnit.Assertions.Extensions</c>, so it auto-imports alongside
    /// <c>Assert.That</c>.
    /// </summary>
    [Test]
    public async Task CoreEntryPointHasLoggedResolvesAndPassesAsync(CancellationToken cancellationToken)
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            ILogger logger = factory.CreateLogger("ConsumerCategory");
            logger.LogInformation("smoke");

            await Assert.That(collector)
                .HasLogged()
                .AtLevel(LogLevel.Information)
                .Containing("smoke", StringComparison.Ordinal)
                .Once();
        }
    }

    /// <summary>
    /// Pins that the inverse entry point <c>HasNotLogged()</c> resolves cleanly. Same auto-
    /// discovery path as <c>HasLogged</c>; the v0.2.0/v0.2.1 namespace bug also affected this
    /// surface.
    /// </summary>
    [Test]
    public async Task CoreEntryPointHasNotLoggedResolvesAndPassesAsync(CancellationToken cancellationToken)
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            ILogger logger = factory.CreateLogger("ConsumerCategory");
            logger.LogInformation("smoke");

            await Assert.That(collector).HasNotLogged().AtLevelOrAbove(LogLevel.Error);
        }
    }

    /// <summary>
    /// Pins that the shorthand entry point <c>HasLoggedOnce()</c> resolves cleanly. This is
    /// the canonical regression test for the v0.2.0/v0.2.1 bug — the shorthands originally
    /// lived in the <c>LogAssertions.TUnit</c> namespace and were invisible to consumers who
    /// only pulled in the README's GlobalUsings. v0.2.2 moved them to
    /// <c>TUnit.Assertions.Extensions</c>; this test pins that fix.
    /// </summary>
    [Test]
    public async Task ShorthandEntryPointHasLoggedOnceResolvesAndPassesAsync(CancellationToken cancellationToken)
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            ILogger logger = factory.CreateLogger("ConsumerCategory");
            logger.LogWarning("retry");

            await Assert.That(collector)
                .HasLoggedOnce()
                .AtLevel(LogLevel.Warning)
                .Containing("retry", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Pins that the v0.3.0 value-returning terminator <c>GetMatch()</c> resolves and returns
    /// the matched <see cref="FakeLogRecord"/> for an external consumer. Catches accidental
    /// internalisation of the new public API.
    /// </summary>
    [Test]
    public async Task ValueReturningTerminatorGetMatchResolvesAndReturnsRecordAsync(CancellationToken cancellationToken)
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            ILogger logger = factory.CreateLogger("ConsumerCategory");
            logger.LogError(new InvalidOperationException("broken pipe"), "stream failed");

            FakeLogRecord match = await Assert.That(collector)
                .HasLogged()
                .AtLevel(LogLevel.Error)
                .Once()
                .GetMatch();

            await Assert.That(match.Exception!.Message).Contains("broken pipe");
        }
    }

    /// <summary>
    /// Pins that the v0.3.0 inspection helper <c>DumpToTestOutput()</c> resolves and runs
    /// inside an external-consumer test project. The TUnit-bound entry point lives in the
    /// <c>LogAssertions.TUnit</c> namespace; this test exercises explicit-import resolution
    /// since it isn't in the README's GlobalUsings (consumers reaching for it presumably
    /// already have <c>using LogAssertions.TUnit;</c> in scope, or invoke it in a file that does).
    /// </summary>
    [Test]
    public async Task InspectionHelperDumpToTestOutputResolvesAndRunsAsync(CancellationToken cancellationToken)
    {
        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            ILogger logger = factory.CreateLogger("ConsumerCategory");
            logger.LogInformation("dumped");

            // Explicit-import via fully-qualified call. Validates the TUnit-bound entry point
            // is accessible to an external consumer.
            global::LogAssertions.TUnit.FakeLogCollectorTUnitInspectionExtensions.DumpToTestOutput(collector);

            // Sanity-check the collector saw the record we logged (the dump itself is best-effort
            // since its output goes to the test runner; this assertion proves the pipeline was wired).
            await Assert.That(collector)
                .HasLogged()
                .AtLevel(LogLevel.Information)
                .Containing("dumped", StringComparison.Ordinal)
                .Once();
        }
    }
}
