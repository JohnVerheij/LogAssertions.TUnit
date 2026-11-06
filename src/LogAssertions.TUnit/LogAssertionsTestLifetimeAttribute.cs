using System;
using System.Threading.Tasks;
using TUnit.Core;
using TUnit.Core.Enums;
using TUnit.Core.Interfaces;

namespace LogAssertions.TUnit;

/// <summary>
/// Lets LogAssertions see when each test starts and ends. Apply it once to the test project:
/// <code>
/// [assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]
/// </code>
/// It enables <c>DumpToTestOutputOnFailure()</c>, which throws without it, and makes the built-in test-output tee
/// exact: a record logged after its test has ended is not written into that test's report.
/// </summary>
/// <remarks>
/// <para>
/// TUnit offers no public way for code inside a test to observe that test's end, so the package needs this one line
/// of opt-in. The attribute receives TUnit's test start (before the <c>[Before(Test)]</c> hooks) and test end (after
/// the <c>[After(Test)]</c> hooks) for every test of the assembly, and once per attempt for a retried test.
/// </para>
/// <para>
/// Without it, the tee falls back to a coarser rule: a record logged where no test is current goes to the test that
/// built the tee only until that test has a result, and a record logged on a test's own flow always goes to that test.
/// </para>
/// <para>
/// All state is static and keyed weakly by the test's <see cref="TestContext"/>, so nothing outlives the test.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LogAssertionsTestLifetimeAttribute : Attribute, ITestStartEventReceiver, ITestEndEventReceiver
{
    /// <summary>Gets the stage of the start notification: early, before the <c>[Before(Test)]</c> hooks, so a dump can
    /// be armed in them.</summary>
    EventReceiverStage ITestStartEventReceiver.Stage => EventReceiverStage.Early;

    /// <summary>Gets the stage of the end notification: late, after the <c>[After(Test)]</c> hooks, so their records
    /// are still teed and dumped.</summary>
    EventReceiverStage ITestEndEventReceiver.Stage => EventReceiverStage.Late;

    /// <summary>Marks the start of an attempt of the test.</summary>
    /// <param name="context">The test that starts.</param>
    /// <returns>A completed task.</returns>
    ValueTask ITestStartEventReceiver.OnTestStart(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TestLifetimeTable.Shared.MarkStarted(context);
        return ValueTask.CompletedTask;
    }

    /// <summary>Marks the end of an attempt of the test, then writes the dumps armed for it when it did not pass.</summary>
    /// <param name="context">The test that ends.</param>
    /// <returns>A task that completes when the dumps are written.</returns>
    ValueTask ITestEndEventReceiver.OnTestEnd(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TestLifetimeTable.Shared.MarkEndedAsync(context);
    }
}
