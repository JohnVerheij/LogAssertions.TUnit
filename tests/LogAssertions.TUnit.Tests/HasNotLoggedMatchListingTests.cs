using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The failure of <c>HasNotLogged()</c> always shows the records that matched: when the last 50 captured records hold
/// every match, the usual listing of captured records; when an older match would be hidden, the matching records
/// themselves (capped like the captured records).
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class HasNotLoggedMatchListingTests
{
    /// <summary>A match older than the last 50 captured records is listed, not hidden behind newer noise.</summary>
    [Test]
    public async Task OlderMatch_IsListed(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Old");
        DirectLog.Write(logger, "the offender");
        for (var i = 0; i < 59; i++)
            DirectLog.Write(logger, string.Create(CultureInfo.InvariantCulture, $"noise {i}"));

        var exception = await CatchAsync(async () => await Assert.That(collector).HasNotLogged().Containing("offender", StringComparison.Ordinal));

        var message = exception!.Message.ReplaceLineEndings("\n");
        await Assert.That(message).Contains("1 record(s) matched\n\nMatching records (1 of 60 captured):\n  [info] Old: the offender\n", StringComparison.Ordinal);
        await Assert.That(message).DoesNotContain("noise", StringComparison.Ordinal);
    }

    /// <summary>When every match is within the last 50 captured records, the usual captured-records listing stays.</summary>
    [Test]
    public async Task RecentMatch_KeepsTheCapturedRecordsListing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Recent");
        DirectLog.Write(logger, "noise");
        DirectLog.Write(logger, "the offender");

        var exception = await CatchAsync(async () => await Assert.That(collector).HasNotLogged().Containing("offender", StringComparison.Ordinal));

        var message = exception!.Message.ReplaceLineEndings("\n");
        await Assert.That(message).Contains("Captured records (2 total):\n  [info] Recent: noise\n  [info] Recent: the offender\n", StringComparison.Ordinal);
        await Assert.That(message).DoesNotContain("Matching records", StringComparison.Ordinal);
    }

    /// <summary>More than 50 matches are capped like the captured records: the latest 50, after a line that counts the
    /// earlier ones.</summary>
    [Test]
    public async Task ManyMatches_AreCapped(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Many");
        for (var i = 0; i < 120; i++)
            DirectLog.Write(logger, "match");

        var exception = await CatchAsync(async () => await Assert.That(collector).HasNotLogged().Containing("match", StringComparison.Ordinal));

        var message = exception!.Message.ReplaceLineEndings("\n");
        await Assert.That(message).Contains("Matching records (120 of 120 captured):\n  (70 earlier record(s) not shown)\n", StringComparison.Ordinal);
    }

    /// <summary>Runs <paramref name="run"/> and returns the <see cref="AssertionException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <param name="run">The assertion to run.</param>
    /// <returns>The exception, or <see langword="null"/>.</returns>
    private static async Task<AssertionException?> CatchAsync(Func<Task> run)
    {
        try
        {
            await run();
            return null;
        }
        catch (AssertionException exception)
        {
            return exception;
        }
    }
}
