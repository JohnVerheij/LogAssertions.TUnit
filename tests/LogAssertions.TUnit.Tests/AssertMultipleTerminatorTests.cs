using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The value-returning terminators inside <c>Assert.Multiple()</c>. TUnit records a failed assertion there instead of
/// throwing it, so <c>GetMatch()</c> and <c>GetMatches()</c> have no match to return: they throw
/// <see cref="InvalidOperationException"/> naming the failure instead of returning a made-up value, and the scope still
/// reports the original failure when it ends.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class AssertMultipleTerminatorTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    /// <summary>A failed <c>HasLogged()</c> (no match) or <c>WillLog()</c> (more matches than <c>Once()</c> allows) inside
    /// the scope: the terminator throws, naming the failure; caught or not, the scope reports the original failure.</summary>
    [Test]
    [Arguments("HasLogged().GetMatch()")]
    [Arguments("HasLogged().GetMatches()")]
    [Arguments("WillLog().GetMatch()")]
    [Arguments("WillLog().GetMatches()")]
    public async Task FailedAssertion_TerminatorThrows_AndTheScopeReportsTheFailure(string terminator, CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Multiple");
        DirectLog.Write(logger, "ready 1");
        DirectLog.Write(logger, "ready 2");
        Func<Task> run = terminator switch
        {
            "HasLogged().GetMatch()" => async () => _ = await Assert.That(collector).HasLogged().Containing("absent", StringComparison.Ordinal).Once().GetMatch(),
            "HasLogged().GetMatches()" => async () => _ = await Assert.That(collector).HasLogged().Containing("absent", StringComparison.Ordinal).GetMatches(),
            "WillLog().GetMatch()" => async () => _ = await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("ready", StringComparison.Ordinal).Once().GetMatch(),
            _ => async () => _ = await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("ready", StringComparison.Ordinal).Once().GetMatches(),
        };
        var failure = terminator.StartsWith("HasLogged", StringComparison.Ordinal)
            ? "0 record(s) matched"
            : "2 record(s) matched, more than the maximum of 1";

        InvalidOperationException? thrown = null;
        var reported = await CatchAsync<AssertionException>(async () =>
        {
            using (Assert.Multiple())
            {
                try
                {
                    await run();
                }
                catch (InvalidOperationException exception)
                {
                    thrown = exception;
                }
            }
        });
        var uncaught = await CatchAsync<Exception>(async () =>
        {
            using (Assert.Multiple())
            {
                await run();
            }
        });

        await Assert.That(thrown).IsNotNull();
        await Assert.That(thrown!.Message).StartsWith(
            terminator.Split('.')[1] + " has no match to return: the assertion failed inside Assert.Multiple(), which records the failure instead of throwing it. The failure:",
            StringComparison.Ordinal);
        await Assert.That(thrown.Message).Contains("The failure:" + Environment.NewLine + "Expected ", StringComparison.Ordinal);
        await Assert.That(thrown.Message).Contains(failure, StringComparison.Ordinal);
        await Assert.That(reported!.Message).Contains(failure, StringComparison.Ordinal);
        await Assert.That(uncaught).IsTypeOf<AssertionException>();
        await Assert.That(uncaught!.Message).Contains(failure, StringComparison.Ordinal);
    }

    /// <summary>A passing assertion inside the scope returns its matches as usual.</summary>
    [Test]
    public async Task PassedAssertion_ReturnsTheMatches(CancellationToken cancellationToken)
    {
        var collector = new FakeLogCollector();
        DirectLog.Write(new FakeLogger(collector, "Multiple"), "ready");

        using (Assert.Multiple())
        {
            var match = await Assert.That(collector).HasLogged().Containing("ready", StringComparison.Ordinal).Once().GetMatch();
            var waited = await Assert.That(collector).WillLog(Bound, cancellationToken).Containing("ready", StringComparison.Ordinal).GetMatches();

            await Assert.That(match.Message).IsEqualTo("ready");
            await Assert.That(waited).Count().IsEqualTo(1);
        }
    }

    /// <summary>Runs <paramref name="run"/> and returns the <typeparamref name="TException"/> it threw, or
    /// <see langword="null"/>.</summary>
    /// <typeparam name="TException">The exception type to catch.</typeparam>
    /// <param name="run">The code to run.</param>
    /// <returns>The exception, or <see langword="null"/> when none was thrown.</returns>
    private static async Task<TException?> CatchAsync<TException>(Func<Task> run)
        where TException : Exception
    {
        try
        {
            await run();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }
}
