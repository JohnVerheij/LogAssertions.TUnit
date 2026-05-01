using System.Threading;
using System.Threading.Tasks;
using PublicApiGenerator;
using VerifyTUnit;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Pins the public API surface of <c>LogAssertions.TUnit</c> as a Verify snapshot.
/// Any change to a public type, member, signature, attribute, or visibility produces a
/// diff against <c>PublicApiTests.PublicApi.verified.txt</c> and fails the test until
/// the snapshot is explicitly re-accepted (by replacing the verified file with the
/// received output). This is a stronger guard than ApiCompat package validation, which
/// only fires on the next packed version against a baseline; the snapshot fires on
/// every PR.
/// </summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class PublicApiTests
{
    /// <summary>
    /// Generates the public API of the LogAssertions.TUnit assembly and verifies it
    /// against the committed <c>.verified.txt</c> snapshot.
    /// </summary>
    [Test]
    public Task PublicApiHasNotChangedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = typeof(LogAssertionBase<HasLoggedAssertion>).Assembly;
        var publicApi = assembly.GeneratePublicApi();
        return Verifier.Verify(publicApi);
    }
}
