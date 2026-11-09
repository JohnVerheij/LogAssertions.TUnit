using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using TUnit.Assertions.Core;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// <c>WillLog</c> where the wait ends on its token or its timeout: the outcome is decided on the records the collector
/// holds when the wait ends, so records captured by then count whether the token was canceled before the assertion
/// ran or the timeout fired just after the matching record arrived. A decision table over the token, the records
/// already captured, and the count, plus the timeout race on a fake clock.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class WillLogEdgeTests
{
    /// <summary>A bound long enough never to be reached in these tests.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    /// <summary>Every case on a collector of its own, with the records already captured when the assertion runs.</summary>
    [Test]
    public async Task EveryCombination_DecidesOnTheCapturedRecords(CancellationToken cancellationToken)
    {
        WillLogCase[] cases =
        [
            new("live token, 1 captured, Once", Canceled: false, Captured: 1, static a => a.Once(), Failure: null),
            new("live token, 2 captured, Once", false, 2, static a => a.Once(), "2 record(s) matched, more than the maximum of 1"),
            new("live token, 2 captured, Exactly(2)", false, 2, static a => a.Exactly(2), null),
            new("live token, 3 captured, AtLeast(2)", false, 3, static a => a.AtLeast(2), null),
            new("live token, 1 captured, Between(1, 1)", false, 1, static a => a.Between(1, 1), null),
            new("canceled token, 0 captured, Once", true, 0, static a => a.Once(), "the wait was canceled with 0 record(s) matched"),
            new("canceled token, 1 captured, Once", true, 1, static a => a.Once(), null),
            new("canceled token, 1 captured, no count terminator", true, 1, static a => a, null),
            new("canceled token, 2 captured, Once", true, 2, static a => a.Once(), "2 record(s) matched, more than the maximum of 1"),
            new("canceled token, 1 captured, Exactly(2)", true, 1, static a => a.Exactly(2), "the wait was canceled with 1 record(s) matched"),
            new("canceled token, 2 captured, Between(1, 3)", true, 2, static a => a.Between(1, 3), null),
        ];

        var outcomes = new List<(WillLogCase Case, AssertionException? Failure)>();
        foreach (var willLogCase in cases)
            outcomes.Add((willLogCase, await RunAsync(willLogCase, cancellationToken)));

        using (Assert.Multiple())
        {
            foreach (var (willLogCase, failure) in outcomes)
            {
                if (willLogCase.Failure is null)
                    await Assert.That(failure?.Message).IsNull().Because(willLogCase.Name);
                else
                    await Assert.That(failure?.Message ?? string.Empty).Contains(willLogCase.Failure, StringComparison.Ordinal).Because(willLogCase.Name);
            }
        }
    }

    /// <summary>The matching record arrives, then the timeout fires before the wait has read it (on a fake clock, so
    /// both happen before the wait resumes): the record was captured within the timeout, so the assertion passes and
    /// returns it.</summary>
    [Test]
    public async Task RecordArrivesThenTheTimeoutFires_BeforeTheWaitResumes_Passes(CancellationToken cancellationToken)
    {
        var clock = new FakeTimeProvider();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Race");
        var match = new WillLogAssertion(Context(collector), TimeSpan.FromSeconds(1), clock, cancellationToken)
            .Containing("ready", StringComparison.Ordinal)
            .Once()
            .GetMatch();
        await Assert.That(match.IsCompleted).IsFalse();

        DirectLog.Write(logger, "service ready");
        clock.Advance(TimeSpan.FromSeconds(2));

        await Assert.That((await match).Message).IsEqualTo("service ready");
    }

    /// <summary>The timeout fires with no matching record: the assertion fails with the timeout headline and the
    /// count, on the fake clock of its timeout.</summary>
    [Test]
    public async Task TimeoutFiresWithNoMatch_FailsWithTheHeadline(CancellationToken cancellationToken)
    {
        var clock = new FakeTimeProvider();
        var collector = new FakeLogCollector();
        DirectLog.Write(new FakeLogger(collector, "Race"), "noise");
        var assertion = new WillLogAssertion(Context(collector), TimeSpan.FromSeconds(1), clock, cancellationToken)
            .Containing("ready", StringComparison.Ordinal)
            .Once();
        var run = CatchAsync(async () => await assertion);
        await Assert.That(run.IsCompleted).IsFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        var failure = await run;

        await Assert.That(failure?.Message ?? string.Empty).Contains("the wait timed out after 00:00:01 with 0 record(s) matched", StringComparison.Ordinal);
    }

    /// <summary>Eight threads log 50 matching records each while the assertion waits for at least 400: it passes once
    /// they have all arrived, and returns them all.</summary>
    [Test]
    public async Task ManyProducers_WhileTheWaitRuns_PassWhenEnoughArrived(CancellationToken cancellationToken)
    {
        const int Threads = 8;
        const int PerThread = 50;
        var collector = new FakeLogCollector();
        using var start = new ManualResetEventSlim();
        var producers = new Task[Threads];
        for (var thread = 0; thread < Threads; thread++)
        {
            var logger = new FakeLogger(collector, $"Producer{thread}");
            producers[thread] = Task.Run(
                () =>
                {
                    start.Wait(cancellationToken);
                    for (var i = 0; i < PerThread; i++)
                        DirectLog.Write(logger, "item ready");
                },
                cancellationToken);
        }

        var matches = new WillLogAssertion(Context(collector), Bound, TimeProvider.System, cancellationToken)
            .Containing("ready", StringComparison.Ordinal)
            .AtLeast(Threads * PerThread)
            .GetMatches();
        start.Set();
        await Task.WhenAll(producers);

        await Assert.That((await matches).Count).IsEqualTo(Threads * PerThread);
    }

    /// <summary>Four assertions wait on one collector at once, each for its own record: each passes on its record,
    /// whatever order the records arrive in.</summary>
    [Test]
    public async Task ConcurrentWaits_OnOneCollector_EachPassOnItsOwnRecord(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Shared");
        string[] names = ["delta", "beta", "alpha", "gamma"];
        var waits = new Task<FakeLogRecord>[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            waits[i] = new WillLogAssertion(Context(collector), Bound, TimeProvider.System, cancellationToken)
                .Containing(names[i] + " ready", StringComparison.Ordinal)
                .Once()
                .GetMatch();
        }

        foreach (var name in new[] { "alpha", "beta", "gamma", "delta" })
            await Task.Run(() => DirectLog.Write(logger, name + " ready"), cancellationToken);

        for (var i = 0; i < names.Length; i++)
            await Assert.That((await waits[i]).Message).IsEqualTo(names[i] + " ready");
    }

    /// <summary>Runs one case and returns its failure, or <see langword="null"/> when it passed.</summary>
    /// <param name="willLogCase">The case.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>The failure, or <see langword="null"/>.</returns>
    private static async Task<AssertionException?> RunAsync(WillLogCase willLogCase, CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Case");
        for (var i = 1; i <= willLogCase.Captured; i++)
            DirectLog.Write(logger, $"ready {i}");

        using var canceled = new CancellationTokenSource();
        if (willLogCase.Canceled)
            await canceled.CancelAsync();

        var assertion = willLogCase.Count(
            new WillLogAssertion(Context(collector), Bound, TimeProvider.System, willLogCase.Canceled ? canceled.Token : cancellationToken)
                .Containing("ready", StringComparison.Ordinal));
        return await CatchAsync(async () => await assertion);
    }

    /// <summary>The assertion context of <paramref name="collector"/>.</summary>
    /// <param name="collector">The collector.</param>
    /// <returns>The context.</returns>
    private static AssertionContext<FakeLogCollector> Context(FakeLogCollector collector)
        => new(collector, new StringBuilder("Assert.That(collector)"));

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

    /// <summary>One combination: the token, the records already captured, the count, and the outcome.</summary>
    /// <param name="Name">What the case is, named in a failure.</param>
    /// <param name="Canceled">Whether the token is canceled before the assertion runs.</param>
    /// <param name="Captured">How many matching records are captured before the assertion runs.</param>
    /// <param name="Count">Applies the count terminator.</param>
    /// <param name="Failure">Text the failure must contain, or <see langword="null"/> when the case passes.</param>
    private sealed record WillLogCase(string Name, bool Canceled, int Captured, Func<WillLogAssertion, WillLogAssertion> Count, string? Failure);
}
