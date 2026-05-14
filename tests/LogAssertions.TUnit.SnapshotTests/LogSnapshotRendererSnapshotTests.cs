using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using LogAssertions.Render;
using Microsoft.Extensions.Logging;
using SnapshotAssertions.TUnit;

namespace LogAssertions.TUnit.SnapshotTests;

/// <summary>
/// End-to-end integration test for the canonical "render a log capture, pin via snapshot"
/// pattern documented in the <c>README.md</c> cookbook. Exercises
/// <see cref="LogSnapshotRenderer"/> from <c>LogAssertions</c> paired with
/// <c>MatchesSnapshot()</c> from <c>SnapshotAssertions.TUnit</c> against a committed baseline.
/// </summary>
/// <remarks>
/// The two packages share no PackageReference: <c>LogAssertions.TUnit</c> does not depend on
/// <c>SnapshotAssertions.TUnit</c>. This test project adds both as consumer-side dependencies
/// to validate the pairing the same way a consumer would. A baseline drift on either side
/// (renderer format change, snapshot framework change) surfaces here before it reaches
/// downstream consumers. This mirrors the renderer-snapshot pairing test that
/// <c>MathAssertions.TUnit</c> and <c>TimeAssertions.TUnit</c> already carry.
/// </remarks>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class LogSnapshotRendererSnapshotTests
{
    /// <summary>
    /// Pins the rendered text of a small fixed log sequence against the committed
    /// <c>LogRenderedSequence.expected.txt</c> baseline. The baseline is the canonical shape
    /// consumers will see: an indexed record block per entry, abbreviated level, leaf-only
    /// category, the message in quotes, and an indented <c>state:</c> line for structured state.
    /// </summary>
    [Test]
    public async Task LogSnapshotRendererProducesSnapshotMatchingBaseline(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var (factory, collector) = LogCollectorBuilder.Create();
        using (factory)
        {
#pragma warning disable CA1848, CA1873
            var logger = factory.CreateLogger("My.App.OrderService");
            logger.LogInformation("order {OrderId} accepted", 42);
            logger.LogWarning("inventory low for {Sku}", "ABC-123");
#pragma warning restore CA1848, CA1873

            var rendered = LogSnapshotRenderer.Render(collector);

            await Assert.That(rendered).MatchesSnapshot("LogRenderedSequence");
        }
    }
}
