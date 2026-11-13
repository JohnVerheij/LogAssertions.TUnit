using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// <c>WithEventIdIn(params int[])</c> on every assertion of the chain (<c>HasLogged</c>, <c>HasNotLogged</c>, the
/// steps of <c>HasLoggedSequence</c>, <c>WillLog</c>) and on <see cref="LogFilter"/>: which records it matches, the
/// failure text, and the arguments it rejects. The seed holds the events 1 to 5 (see <see cref="TestLogMessages"/>).
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class WithEventIdInTests
{
    /// <summary>Records with any of the listed IDs match, with one ID, several, duplicates, and negative or extreme IDs.</summary>
    [Test]
    public async Task HasLogged_MatchesRecordsWithAnyListedId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: true);

        await Assert.That(collector).HasLogged().WithEventIdIn(3).Once();
        await Assert.That(collector).HasLogged().WithEventIdIn(3, 4).Exactly(2);
        await Assert.That(collector).HasLogged().WithEventIdIn(3, 3, 4, 4).Exactly(2);
        await Assert.That(collector).HasLogged().WithEventIdIn(int.MinValue, -1, int.MaxValue).Exactly(3);
        await Assert.That(collector).HasLogged().WithEventIdIn(1, 2, 3, 4, 5).AtLevel(LogLevel.Error).Once();
        int[] set = [3, 4];
        await Assert.That(collector).HasLogged().WithEventIdIn(set).Exactly(2);
    }

    /// <summary><c>HasNotLogged</c> passes when no record has a listed ID, and fails when one has.</summary>
    [Test]
    public async Task HasNotLogged_PassesOnlyWhenNoRecordHasAListedId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: false);

        await Assert.That(collector).HasNotLogged().WithEventIdIn(6, 7, 8);
        await Assert.That(async () => await Assert.That(collector).HasNotLogged().WithEventIdIn(6, 5))
            .Throws<AssertionException>();
    }

    /// <summary>A sequence step takes the same filter.</summary>
    [Test]
    public async Task HasLoggedSequence_StepMatchesAnyListedId(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: false);

        await Assert.That(collector).HasLoggedSequence()
            .WithEventIdIn(1, 2)
            .Then().WithEventIdIn(4, 9)
            .Then().WithEventIdIn(5);
        await Assert.That(async () => await Assert.That(collector).HasLoggedSequence()
                .WithEventIdIn(5)
                .Then().WithEventIdIn(1, 2))
            .Throws<AssertionException>();
    }

    /// <summary><c>WillLog</c> passes on a record logged later with one of the IDs.</summary>
    [Test]
    public async Task WillLog_PassesOnArrivalOfAListedId(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Worker");
        _ = Task.Run(
            async () =>
            {
                await Task.Yield();
                logger.Log(LogLevel.Information, new EventId(11), "tick", null, static (state, _) => state);
                logger.Log(LogLevel.Information, new EventId(12), "tock", null, static (state, _) => state);
            },
            cancellationToken);

        var record = await Assert.That(collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken)
            .WithEventIdIn(12, 13).Once().GetMatch();

        await Assert.That(record.Message).IsEqualTo("tock");
    }

    /// <summary>Inside <c>Assert.Multiple()</c> a failed assertion is reported with the others.</summary>
    [Test]
    public async Task InsideAssertMultiple_FailureIsReportedWithTheFilter(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: false);

        var failure = await Assert.That(async () =>
        {
            using (Assert.Multiple())
            {
                await Assert.That(collector).HasLogged().WithEventIdIn(1, 2).Exactly(2);
                await Assert.That(collector).HasLogged().WithEventIdIn(40, 41).Once();
            }
        }).Throws<AssertionException>();

        await Assert.That(failure!.Message).Contains("EventId in {40, 41}", StringComparison.Ordinal);
    }

    /// <summary>The failure names the call with its distinct IDs, and the filter description lists them in braces so it
    /// does not read as the range <c>EventId in [min, max]</c>.</summary>
    [Test]
    public async Task Failure_NamesTheCallAndTheSet(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: false);

        var failure = await Assert.That(async () => await Assert.That(collector).HasLogged().WithEventIdIn(40, 41, 40).Once())
            .Throws<AssertionException>();

        await Assert.That(failure!.Message).Contains(".WithEventIdIn(40, 41)", StringComparison.Ordinal);
        await Assert.That(failure.Message).Contains("EventId in {40, 41}", StringComparison.Ordinal);
        await Assert.That(failure.Message).DoesNotContain("EventId in [", StringComparison.Ordinal);
    }

    /// <summary>An empty set and a <see langword="null"/> array are rejected, on the chain and on the filter.</summary>
    [Test]
    public async Task EmptyAndNull_AreRejected(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: false);

        await Assert.That(() => LogFilter.WithEventIdIn()).Throws<ArgumentException>().WithParameterName("eventIds");
        await Assert.That(() => LogFilter.WithEventIdIn(null!)).Throws<ArgumentNullException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithEventIdIn()).Throws<ArgumentException>();
        await Assert.That(async () => await Assert.That(collector).HasLogged().WithEventIdIn(null!)).Throws<ArgumentNullException>();
    }

    /// <summary>The filter copies the array: changing it afterwards does not change what the filter matches.</summary>
    [Test]
    public async Task Filter_CopiesTheArray(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Seed(withExtremeIds: false);
        int[] ids = [1];
        var filter = LogFilter.WithEventIdIn(ids);

        ids[0] = 5;

        await Assert.That(collector.CountMatching(filter)).IsEqualTo(1);
        await Assert.That(collector.Filter(filter)[0].Id.Id).IsEqualTo(1);
        await Assert.That(filter.Description).IsEqualTo("EventId in {1}");
    }

    /// <summary>The filter description is a set in braces, in the order the IDs first appear, in the invariant culture.</summary>
    [Test]
    public async Task Filter_DescribesTheDistinctIds(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(LogFilter.WithEventIdIn(2, 1, 2, -3).Description).IsEqualTo("EventId in {2, 1, -3}");
        await Assert.That(LogFilter.WithEventIdIn(1000000).Description).IsEqualTo("EventId in {1000000}");
    }

    /// <summary>Creates a collector with the events 1 to 5, and with <paramref name="withExtremeIds"/> three more records
    /// with the IDs <see cref="int.MinValue"/>, -1, and <see cref="int.MaxValue"/>.</summary>
    /// <param name="withExtremeIds">Whether to add the records with extreme IDs.</param>
    /// <returns>The collector.</returns>
    private static FakeLogCollector Seed(bool withExtremeIds)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Seed");
        TestLogMessages.TraceSample(logger);
        TestLogMessages.DebugSample(logger);
        TestLogMessages.StartedProcessing(logger);
        TestLogMessages.ValidationFailed(logger);
        TestLogMessages.OperationFailed(logger, new InvalidOperationException("boom"));
        if (withExtremeIds)
        {
            foreach (var id in new[] { int.MinValue, -1, int.MaxValue })
            {
                logger.Log(LogLevel.Information, new EventId(id), "extreme", null, static (state, _) => state);
            }
        }

        return collector;
    }
}
