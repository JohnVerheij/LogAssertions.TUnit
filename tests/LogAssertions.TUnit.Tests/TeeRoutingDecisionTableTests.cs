using System.Threading;
using System.Threading.Tasks;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Every combination of the inputs of <see cref="TestLifetimeTable.ResolveTeeTarget"/>: the test current at the log call
/// and the owning test, each absent, not tracked (with or without a result), tracked and running, or tracked and ended.
/// The rules: a current test gets the record unless the table marked it ended, and the owner is then never asked; with
/// no current test, a tracked owner gets it while it runs, and an untracked owner until it has a result.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class TeeRoutingDecisionTableTests
{
    /// <summary>How a table knows a test.</summary>
    private enum Tracking
    {
        /// <summary>The table never saw the test start.</summary>
        Untracked,

        /// <summary>The table saw the test start, and not end.</summary>
        Running,

        /// <summary>The table saw the test start and end.</summary>
        Ended,
    }

    /// <summary>Gets or sets the context of <see cref="A_StoresItsContext"/>, which has a result by the time
    /// <see cref="EveryCombination_FollowsTheRules"/> runs.</summary>
    private static TestContext? FinishedTest { get; set; }

    /// <summary>Stores this test's context for <see cref="EveryCombination_FollowsTheRules"/>.</summary>
    [Test]
    public async Task A_StoresItsContext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FinishedTest = TestContext.Current;
        await Assert.That(FinishedTest).IsNotNull();
    }

    /// <summary>Each case on a table of its own: the running test (no result yet) and a finished one (with a result),
    /// as the current test and as the owner, in each tracking state.</summary>
    [Test]
    [DependsOn(nameof(A_StoresItsContext))]
    public async Task EveryCombination_FollowsTheRules(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var running = TestContext.Current!;
        var finished = FinishedTest!;
        await Assert.That(running.Execution.Result).IsNull();
        await Assert.That(finished.Execution.Result).IsNotNull();

        RoutingCase[] cases =
        [
            new("current untracked, no owner", running, Tracking.Untracked, null, Tracking.Untracked, running),
            new("current running, no owner", running, Tracking.Running, null, Tracking.Untracked, running),
            new("current ended, no owner", running, Tracking.Ended, null, Tracking.Untracked, null),
            new("current untracked with a result, no owner", finished, Tracking.Untracked, null, Tracking.Untracked, finished),
            new("current with a result running again (a retry), no owner", finished, Tracking.Running, null, Tracking.Untracked, finished),
            new("current with a result ended, no owner", finished, Tracking.Ended, null, Tracking.Untracked, null),
            new("current untracked, owner running", running, Tracking.Untracked, finished, Tracking.Running, running),
            new("current ended, owner running", running, Tracking.Ended, finished, Tracking.Running, null),
            new("current ended, owner untracked without a result", finished, Tracking.Ended, running, Tracking.Untracked, null),
            new("current ended, owner is the same test", running, Tracking.Ended, running, Tracking.Ended, null),
            new("no current, no owner", null, Tracking.Untracked, null, Tracking.Untracked, null),
            new("no current, owner untracked without a result", null, Tracking.Untracked, running, Tracking.Untracked, running),
            new("no current, owner untracked with a result", null, Tracking.Untracked, finished, Tracking.Untracked, null),
            new("no current, owner running", null, Tracking.Untracked, running, Tracking.Running, running),
            new("no current, owner ended", null, Tracking.Untracked, running, Tracking.Ended, null),
            new("no current, owner with a result running again (a retry)", null, Tracking.Untracked, finished, Tracking.Running, finished),
            new("no current, owner with a result ended", null, Tracking.Untracked, finished, Tracking.Ended, null),
        ];

        using (Assert.Multiple())
        {
            foreach (var routingCase in cases)
            {
                var target = await RouteAsync(routingCase);
                if (routingCase.Expected is null)
                    await Assert.That(target).IsNull().Because(routingCase.Name);
                else
                    await Assert.That(target).IsSameReferenceAs(routingCase.Expected).Because(routingCase.Name);
            }
        }
    }

    /// <summary>Builds a table that knows the case's tests as the case says, and resolves the target.</summary>
    /// <param name="routingCase">The case.</param>
    /// <returns>The test the record goes to, or <see langword="null"/>.</returns>
    private static async Task<TestContext?> RouteAsync(RoutingCase routingCase)
    {
        var table = new TestLifetimeTable();
        await TrackAsync(table, routingCase.Current, routingCase.CurrentTracking);
        if (!ReferenceEquals(routingCase.Owner, routingCase.Current))
            await TrackAsync(table, routingCase.Owner, routingCase.OwnerTracking);
        return table.ResolveTeeTarget(routingCase.Current, routingCase.Owner);
    }

    /// <summary>Marks <paramref name="test"/> in <paramref name="table"/> as <paramref name="tracking"/> says.</summary>
    /// <param name="table">The table.</param>
    /// <param name="test">The test, or <see langword="null"/> for none.</param>
    /// <param name="tracking">The state to put it in.</param>
    /// <returns>A task that completes when the test is marked.</returns>
    private static async Task TrackAsync(TestLifetimeTable table, TestContext? test, Tracking tracking)
    {
        if (test is null || tracking is Tracking.Untracked)
            return;

        table.MarkStarted(test);
        if (tracking is Tracking.Ended)
            await table.MarkEndedAsync(test);
    }

    /// <summary>One combination of the inputs, and the test the record must go to.</summary>
    /// <param name="Name">What the case is, named in a failure.</param>
    /// <param name="Current">The test current at the log call, or <see langword="null"/>.</param>
    /// <param name="CurrentTracking">How the table knows the current test.</param>
    /// <param name="Owner">The owning test, or <see langword="null"/>.</param>
    /// <param name="OwnerTracking">How the table knows the owner.</param>
    /// <param name="Expected">The test the record must go to, or <see langword="null"/> for none.</param>
    private sealed record RoutingCase(
        string Name, TestContext? Current, Tracking CurrentTracking, TestContext? Owner, Tracking OwnerTracking, TestContext? Expected);
}
