using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// Which tests are running, as <see cref="LogAssertionsTestLifetimeAttribute"/> reports them, and the failure dumps
/// armed for them. Entries are keyed weakly by <see cref="TestContext"/>, so a finished test's entry goes with its
/// context. A retried test starts and ends once per attempt, so an entry can run again after it has ended.
/// </summary>
internal sealed class TestLifetimeTable
{
    /// <summary>The message of the exception thrown when a dump is armed in a test the attribute did not start.</summary>
    internal const string MissingAttributeMessage =
        "DumpToTestOutputOnFailure() needs the LogAssertionsTestLifetime attribute to see when the test ends. "
        + "Add this line once to the test project, for example in GlobalUsings.cs: "
        + "[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]";

    /// <summary>The message of the exception thrown when a dump is armed after its test has ended.</summary>
    internal const string EndedTestMessage =
        "DumpToTestOutputOnFailure() was called after the current test had ended. "
        + "Call it in the test or in a [Before(Test)] hook.";

    /// <summary>The note written once per run when a built-in tee is built in a test the attribute did not start.</summary>
    internal const string MissingAttributeNote =
        "LogAssertions: this test project does not apply the LogAssertionsTestLifetime attribute, so the test-output tee "
        + "cannot see when a test ends, and a record logged on the flow of a test that has ended still goes to that "
        + "test's report. Add this line once to the test project, for example in GlobalUsings.cs: "
        + "[assembly: LogAssertions.TUnit.LogAssertionsTestLifetime]";

    private readonly ConditionalWeakTable<TestContext, TestLifetimeEntry> _entries = new();
    private int _missingAttributeNoted;

    /// <summary>Gets the table that the attribute, the failure dumps, and the built-in tee share.</summary>
    internal static TestLifetimeTable Shared { get; } = new();

    /// <summary>Marks the start of an attempt of the test: it is running, and no dump is armed for it yet.</summary>
    /// <param name="context">The test.</param>
    internal void MarkStarted(TestContext context)
        => _entries.GetValue(context, static _ => new TestLifetimeEntry()).Start();

    /// <summary>Marks the end of an attempt of the test: marks it ended, then writes its armed dumps when the attempt
    /// did not pass. A test the table never saw start is ignored.</summary>
    /// <param name="context">The test.</param>
    /// <returns>A task that completes when the test is marked ended and the dumps are written.</returns>
    internal ValueTask MarkEndedAsync(TestContext context)
        => _entries.TryGetValue(context, out var entry) ? entry.EndAsync(context) : ValueTask.CompletedTask;

    /// <summary>Arms a failure dump of <paramref name="collector"/> for the running attempt of the test.</summary>
    /// <param name="context">The current test.</param>
    /// <param name="collector">The collector to dump.</param>
    /// <param name="verbosity">How much detail to write per record.</param>
    /// <exception cref="InvalidOperationException">The attribute did not start the test, or the test has ended.</exception>
    internal void Arm(TestContext context, FakeLogCollector collector, DumpVerbosity verbosity)
    {
        if (!_entries.TryGetValue(context, out var entry))
        {
            throw new InvalidOperationException(MissingAttributeMessage);
        }

        if (!entry.TryArm(collector, verbosity))
        {
            throw new InvalidOperationException(EndedTestMessage);
        }
    }

    /// <summary>Writes <see cref="MissingAttributeNote"/> to the output of <paramref name="test"/>, where a built-in tee
    /// is being built, when the attribute did not start that test: once per table, so once per run for
    /// <see cref="Shared"/>. Runs when a tee is built, never per record.</summary>
    /// <param name="test">The test current where the tee is built, or <see langword="null"/>.</param>
    internal void NoteIfUntracked(TestContext? test)
    {
        if (test is null || TryGetEntry(test, out _) || Interlocked.Exchange(ref _missingAttributeNoted, 1) is not 0)
        {
            return;
        }

        test.Output.WriteLine(MissingAttributeNote);
    }

    /// <summary>
    /// Resolves the test a teed record goes to, or <see langword="null"/> when the record is not teed. The rules,
    /// in order:
    /// <list type="number">
    /// <item><description>The test current at the log call, unless the attribute marked it ended; then no test (the
    /// record does not fall back to the owner).</description></item>
    /// <item><description>When no test is current, the owning test (current when the tee was built), while it runs:
    /// until the attribute marks it ended, or, for a test the attribute does not track, until it has a
    /// result.</description></item>
    /// <item><description>Otherwise no test.</description></item>
    /// </list>
    /// </summary>
    /// <param name="current">The test current at the log call, or <see langword="null"/>.</param>
    /// <param name="owner">The test current when the tee was built, or <see langword="null"/>.</param>
    /// <returns>The test to tee to, or <see langword="null"/>.</returns>
    /// <remarks>Allocation-free: it runs for every teed record.</remarks>
    internal TestContext? ResolveTeeTarget(TestContext? current, TestContext? owner)
    {
        if (current is not null)
        {
            return TryGetEntry(current, out var entry) && !entry.IsRunning ? null : current;
        }

        if (owner is null)
        {
            return null;
        }

        if (TryGetEntry(owner, out var ownerEntry))
        {
            return ownerEntry.IsRunning ? owner : null;
        }

        return owner.Execution.Result is null ? owner : null;
    }

    /// <summary>Looks up the entry of a test.</summary>
    /// <param name="context">The test.</param>
    /// <param name="entry">The entry when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the attribute started the test at least once.</returns>
    private bool TryGetEntry(TestContext context, [NotNullWhen(true)] out TestLifetimeEntry? entry)
        => _entries.TryGetValue(context, out entry);
}
