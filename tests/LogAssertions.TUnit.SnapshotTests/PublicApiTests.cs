using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using PublicApiGenerator;
using VerifyTUnit;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Pins the public API surface of both shipped packages (<c>LogAssertions</c> and
/// <c>LogAssertions.TUnit</c>) as Verify snapshots. Any change to a public type, member,
/// signature, attribute, or visibility produces a diff against the corresponding
/// <c>.verified.txt</c> and fails the test until the snapshot is explicitly re-accepted.
/// Stronger than ApiCompat's per-version baseline check because these snapshots fire on
/// every PR.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class PublicApiTests
{
    /// <summary>
    /// Pins the public surface of the framework-agnostic <c>LogAssertions</c> assembly:
    /// <c>ILogRecordFilter</c>, <c>LogFilter</c>, <c>LogAssertionRendering</c>,
    /// <c>LogCollectorBuilder</c>, and the <c>FakeLogCollector</c> inspection extensions.
    /// </summary>
    [Test]
    public Task LogAssertionsPublicApiHasNotChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = typeof(ILogRecordFilter).Assembly;
        var publicApi = assembly.GeneratePublicApi();
        return Verifier.Verify(publicApi).UseMethodName("LogAssertions");
    }

    /// <summary>
    /// Pins the public surface of the TUnit adapter assembly: the three assertion classes
    /// (<c>HasLoggedAssertion</c>, <c>HasNotLoggedAssertion</c>, <c>HasLoggedSequenceAssertion</c>),
    /// the source-generated entry-point extensions, the shorthand entry points, and
    /// <c>AssertAllExtensions</c>.
    /// </summary>
    [Test]
    public Task LogAssertionsTUnitPublicApiHasNotChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = typeof(LogAssertionBase<HasLoggedAssertion>).Assembly;
        var publicApi = assembly.GeneratePublicApi();
        return Verifier.Verify(publicApi).UseMethodName("LogAssertions.TUnit");
    }
}
