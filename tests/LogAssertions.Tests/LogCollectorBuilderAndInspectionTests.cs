using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>
/// Framework-agnostic tests for <see cref="LogCollectorBuilder"/> and the inspection
/// extensions on <see cref="Microsoft.Extensions.Logging.Testing.FakeLogCollector"/>.
/// Like the rest of this project, these tests do not reference <c>LogAssertions.TUnit</c>
/// and use only raw TUnit <see cref="Assert"/> infrastructure.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogCollectorBuilderAndInspectionTests
{
    /// <summary>
    /// Verifies <see cref="LogCollectorBuilder.Create"/> returns a wired (factory, collector)
    /// tuple that captures records at the requested minimum level and drops everything below.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task CreateRespectsMinimumLevelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create(LogLevel.Information);
        using (factory)
        {
            var logger = factory.CreateLogger("Test");
#pragma warning disable CA1848
            logger.LogTrace("dropped");
            logger.LogDebug("dropped");
            logger.LogInformation("kept");
            logger.LogWarning("kept");
#pragma warning restore CA1848

            await Assert.That(collector.CountMatching()).IsEqualTo(2);
            await Assert.That(collector.CountMatching(LogFilter.AtLevel(LogLevel.Information))).IsEqualTo(1);
            await Assert.That(collector.CountMatching(LogFilter.AtLevel(LogLevel.Warning))).IsEqualTo(1);
        }
    }

    /// <summary>
    /// Verifies <see cref="LogCollectorBuilder.Create"/>'s default minimum level is
    /// <see cref="LogLevel.Trace"/> (capture everything).
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task CreateDefaultMinimumIsTraceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848
            factory.CreateLogger("Test").LogTrace("captured");
#pragma warning restore CA1848
            await Assert.That(collector.CountMatching()).IsEqualTo(1);
        }
    }

    /// <summary>
    /// Verifies <see cref="FakeLogCollectorInspectionExtensions.Filter"/> returns a defensive
    /// copy: mutating the returned list (or further log calls) must not affect a previously
    /// returned snapshot.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task FilterReturnsDefensiveCopyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            var logger = factory.CreateLogger("Test");
#pragma warning disable CA1848
            logger.LogWarning("first");
#pragma warning restore CA1848

            var firstSnapshot = collector.Filter(LogFilter.AtLevel(LogLevel.Warning));
            await Assert.That(firstSnapshot).Count().IsEqualTo(1);

#pragma warning disable CA1848
            logger.LogWarning("second");
#pragma warning restore CA1848

            // Previously-returned list is unaffected.
            await Assert.That(firstSnapshot).Count().IsEqualTo(1);
            // A fresh query reflects the new record.
            await Assert.That(collector.Filter(LogFilter.AtLevel(LogLevel.Warning))).Count().IsEqualTo(2);
        }
    }

    /// <summary>
    /// Verifies <see cref="FakeLogCollectorInspectionExtensions.DumpTo"/> renders the
    /// captured-records snapshot in the same format the failure message uses (4-character
    /// level abbreviation, message, props/scope/exception lines as applicable).
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task DumpToWritesSnapshotInFailureFormatAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848
            factory.CreateLogger("Test").LogWarning("validation failed: TimeoutMs out of range");
#pragma warning restore CA1848

            using var writer = new StringWriter();
            collector.DumpTo(writer);
            var dump = writer.ToString();

            await Assert.That(dump).Contains("Captured records (1 total):");
            await Assert.That(dump).Contains("[warn]");
            await Assert.That(dump).Contains("Test:");
            await Assert.That(dump).Contains("validation failed");
        }
    }

    /// <summary>
    /// Verifies <see cref="FakeLogCollectorInspectionExtensions.Filter"/> and
    /// <see cref="FakeLogCollectorInspectionExtensions.CountMatching"/> reject null arguments.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task InspectionExtensionsRejectNullArgsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
            await Assert.That(() => collector.Filter(null!)).Throws<ArgumentNullException>();
            await Assert.That(() => collector.CountMatching(null!)).Throws<ArgumentNullException>();
            await Assert.That(() => collector.DumpTo(null!)).Throws<ArgumentNullException>();
        }
    }

    /// <summary>
    /// Verifies <see cref="LogAssertionRendering.LevelAbbreviation"/> returns the canonical
    /// 4-character abbreviations matching the MEL console formatter.
    /// </summary>
    /// <param name="cancellationToken">TUnit-injected cancellation token.</param>
    [Test]
    public async Task LevelAbbreviationCanonicalAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.Trace)).IsEqualTo("trce");
        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.Debug)).IsEqualTo("dbug");
        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.Information)).IsEqualTo("info");
        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.Warning)).IsEqualTo("warn");
        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.Error)).IsEqualTo("fail");
        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.Critical)).IsEqualTo("crit");
        await Assert.That(LogAssertionRendering.LevelAbbreviation(LogLevel.None)).IsEqualTo("none");
    }
}
