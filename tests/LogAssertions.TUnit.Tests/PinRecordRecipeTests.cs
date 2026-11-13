using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The README recipe "Pin the records other tools read": <c>WithEventName(name).WithMessageTemplate(template)</c> passes
/// for the record with that name and template, and fails, listing the captured records, when the name or the template
/// changes.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class PinRecordRecipeTests
{
    /// <summary>The pair passes for the record that has both.</summary>
    [Test]
    public async Task NameAndTemplate_MatchingRecord_Passes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed();

        await Assert.That(collector).HasLogged().WithEventName("Bootstrap").WithMessageTemplate("App started").Once();
        await Assert.That(collector).HasLogged().WithEventName("Item").WithMessageTemplate("Item {ItemId} failed").Once();
    }

    /// <summary>A renamed event or a reworded template fails the pair, and the failure shows the captured records.</summary>
    [Test]
    public async Task ChangedNameOrTemplate_FailsAndListsTheCapturedRecords(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed();

        var renamed = await Assert.That(async () => await Assert.That(collector).HasLogged()
                .WithEventName("Startup").WithMessageTemplate("App started").Once())
            .Throws<AssertionException>();
        var reworded = await Assert.That(async () => await Assert.That(collector).HasLogged()
                .WithEventName("Bootstrap").WithMessageTemplate("App has started").Once())
            .Throws<AssertionException>();

        await Assert.That(renamed!.Message).Contains("Captured records", StringComparison.Ordinal);
        await Assert.That(renamed.Message).Contains("App started", StringComparison.Ordinal);
        await Assert.That(reworded!.Message).Contains("Captured records", StringComparison.Ordinal);
        await Assert.That(reworded.Message).Contains(".WithMessageTemplate(\"App has started\")", StringComparison.Ordinal);
    }

    /// <summary>Creates a collector with a named event and a templated one.</summary>
    /// <returns>The collector.</returns>
    private static FakeLogCollector Seed()
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Recipe");
        TestLogMessages.AppStarted(logger);
        TestLogMessages.NamedItemFailed(logger, "a");
        return collector;
    }
}
