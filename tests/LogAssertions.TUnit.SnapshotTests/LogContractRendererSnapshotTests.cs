using System.Threading;
using System.Threading.Tasks;
using LogAssertions.Render;
using SnapshotAssertions.TUnit;

namespace LogAssertions.TUnit.SnapshotTests;

/// <summary>
/// The pattern the README documents for a log contract: render the definitions with
/// <see cref="LogContractRenderer"/> and pin the text with <c>MatchesSnapshot()</c>, so a renamed placeholder or a
/// reworded template shows as a diff against the committed <c>LogContract.expected.txt</c> baseline.
/// </summary>
[Category("Smoke")]
[Timeout(5_000)]
internal sealed class LogContractRendererSnapshotTests
{
    /// <summary>Pins the contract of three definitions, given in scrambled order.</summary>
    /// <param name="ct">The test's token.</param>
    [Test]
    public async Task LogContractRendererProducesSnapshotMatchingBaseline(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var contract = LogContractRenderer.Render(
        [
            LogDefinition.Capture(log => SnapshotLogMessages.PaymentFailed(log, 0)),
            LogDefinition.Capture(log => SnapshotLogMessages.OrderProcessed(log, 0, 0)),
            LogDefinition.Capture(log => SnapshotLogMessages.OrderShipped(log, 0, "")),
        ]);

        await Assert.That(contract).MatchesSnapshot("LogContract");
    }
}
