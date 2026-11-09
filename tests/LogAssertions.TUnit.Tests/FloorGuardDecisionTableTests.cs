using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Core;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The capture-floor guard of <c>HasLogged()</c> at the edge between a count that admits zero and one that needs a
/// match, for every combination with the level filters, on a collector with an Information floor and no records.
/// Below the floor a count that admits zero fails as vacuous and a count that needs a match names the floor; at the
/// floor (also as the highest level of a range) both are evaluated as usual; contradictory level filters never blame
/// the floor.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class FloorGuardDecisionTableTests
{
    private const string Vacuous =
        "this assertion only matches records at level Debug or below, but the collector's capture floor is Information, "
        + "so records below Information were never captured and the check is vacuously true.";

    private const string FloorNote =
        "0 record(s) matched: this assertion only matches records at level Debug or below, but the collector's capture "
        + "floor is Information, so no such record was captured.";

    /// <summary>Where the chain's levels sit relative to the floor.</summary>
    private enum Levels
    {
        /// <summary>Every level the chain matches is below the floor.</summary>
        BelowTheFloor,

        /// <summary>The highest level the chain matches is the floor itself, or above.</summary>
        Captured,

        /// <summary>The level filters admit no level at all.</summary>
        Contradictory,
    }

    /// <summary>Every level chain with every count terminator around zero and one.</summary>
    [Test]
    public async Task EveryCombination_FollowsTheFloorRules(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (string Name, Levels Levels, Func<HasLoggedAssertion, HasLoggedAssertion> Apply)[] levels =
        [
            ("AtLevel(Debug)", Levels.BelowTheFloor, static a => a.AtLevel(LogLevel.Debug)),
            ("AtLevelOrBelow(Debug)", Levels.BelowTheFloor, static a => a.AtLevelOrBelow(LogLevel.Debug)),
            ("AtLevel(Information)", Levels.Captured, static a => a.AtLevel(LogLevel.Information)),
            ("AtLevelOrBelow(Information)", Levels.Captured, static a => a.AtLevelOrBelow(LogLevel.Information)),
            ("AtLevel(Debug).NotAtLevel(Debug)", Levels.Contradictory, static a => a.AtLevel(LogLevel.Debug).NotAtLevel(LogLevel.Debug)),
        ];
        (string Name, int Min, Func<HasLoggedAssertion, HasLoggedAssertion> Apply)[] counts =
        [
            ("Never()", 0, static a => a.Never()),
            ("AtMost(0)", 0, static a => a.AtMost(0)),
            ("AtMost(1)", 0, static a => a.AtMost(1)),
            ("Exactly(0)", 0, static a => a.Exactly(0)),
            ("Between(0, 0)", 0, static a => a.Between(0, 0)),
            ("Between(0, 1)", 0, static a => a.Between(0, 1)),
            ("Exactly(1)", 1, static a => a.Exactly(1)),
            ("Between(1, 1)", 1, static a => a.Between(1, 1)),
            ("Once()", 1, static a => a.Once()),
            ("AtLeast(1)", 1, static a => a.AtLeast(1)),
            ("no count terminator", 1, static a => a),
        ];

        var outcomes = new List<(string Name, string? Expected, string? Failure, bool BelowTheFloor)>();
        foreach (var level in levels)
        {
            foreach (var count in counts)
            {
                var expected = (level.Levels, count.Min) switch
                {
                    (Levels.BelowTheFloor, 0) => Vacuous,
                    (Levels.BelowTheFloor, _) => FloorNote,
                    (_, 0) => null,
                    _ => "0 record(s) matched" + Environment.NewLine,
                };
                using var capture = LogCollectorBuilder.Create(LogLevel.Information);
                var assertion = count.Apply(level.Apply(new HasLoggedAssertion(Context(capture.Collector))));
                var failure = await CatchAsync(async () => await assertion);
                outcomes.Add(($"{level.Name}.{count.Name}", expected, failure?.Message, level.Levels is Levels.BelowTheFloor));
            }
        }

        using (Assert.Multiple())
        {
            foreach (var (name, expected, failure, belowTheFloor) in outcomes)
            {
                if (expected is null)
                {
                    await Assert.That(failure).IsNull().Because(name);
                    continue;
                }

                await Assert.That(failure ?? string.Empty).Contains(expected, StringComparison.Ordinal).Because(name);
                if (!belowTheFloor)
                    await Assert.That(failure ?? string.Empty).DoesNotContain("capture floor", StringComparison.Ordinal).Because(name);
            }
        }
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
}
