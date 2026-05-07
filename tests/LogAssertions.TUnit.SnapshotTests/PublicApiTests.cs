using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using PublicApiGenerator;
using SnapshotAssertions.TUnit;

namespace LogAssertions.TUnit.SnapshotTests;

/// <summary>
/// Pins the public API surface of both shipped packages (<c>LogAssertions</c> and
/// <c>LogAssertions.TUnit</c>) using <c>SnapshotAssertions.TUnit</c>'s <c>MatchesSnapshot()</c>
/// chain. Any change to a public type, member, signature, attribute, or visibility produces a
/// diff against the corresponding <c>.expected.txt</c> file under <c>Snapshots/</c> and fails
/// the test until the snapshot is explicitly re-accepted (write the new content to the
/// expected path, or run with <c>SNAPSHOT_ACCEPT=1</c> to auto-write).
/// </summary>
/// <remarks>
/// <para>
/// Stronger than ApiCompat's per-version baseline check because these snapshots fire on every
/// PR, not just at pack time.
/// </para>
/// <para>
/// Cross-package dogfooding: this project consumes <c>SnapshotAssertions.TUnit</c> as a
/// downstream user of the family would, demonstrating that the family's snapshot helper is
/// suitable for the package's own public-API surface checks. Replaces the earlier Verify-based
/// approach (Verify is not promoted by this family — see <c>CONVENTIONS.md</c>) and removes
/// the Verify <c>Deterministic=false</c> / <c>Microsoft.CodeCoverage</c> Linux interaction
/// that previously required a separate no-coverage CI step.
/// </para>
/// </remarks>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class PublicApiTests
{
    /// <summary>
    /// Pins the public surface of the framework-agnostic <c>LogAssertions</c> assembly:
    /// <c>ILogRecordFilter</c>, <c>LogFilter</c>, <c>LogAssertionRendering</c>,
    /// <c>LogCollectorBuilder</c>, the <c>FakeLogCollector</c> inspection extensions, and
    /// v0.4.0's <c>DumpVerbosity</c> enum.
    /// </summary>
    [Test]
    public async Task LogAssertionsPublicApiHasNotChangedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var assembly = typeof(ILogRecordFilter).Assembly;
        var publicApi = assembly.GeneratePublicApi();

        await Assert.That(publicApi).MatchesSnapshot();
    }

    /// <summary>
    /// Pins the public surface of the TUnit adapter assembly: the three assertion classes
    /// (<c>HasLoggedAssertion</c>, <c>HasNotLoggedAssertion</c>, <c>HasLoggedSequenceAssertion</c>),
    /// the source-generated entry-point extensions, the shorthand entry points,
    /// <c>AssertAllExtensions</c>, and v0.4.0's <c>FakeLogCollectorTUnitInspectionExtensions</c>
    /// verbosity overload.
    /// </summary>
    [Test]
    public async Task LogAssertionsTUnitPublicApiHasNotChangedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var assembly = typeof(LogAssertionBase<HasLoggedAssertion>).Assembly;
        var publicApi = assembly.GeneratePublicApi();

        await Assert.That(publicApi).MatchesSnapshot();
    }
}
