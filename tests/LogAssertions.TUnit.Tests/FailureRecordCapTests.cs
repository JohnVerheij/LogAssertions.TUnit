using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The dump inside a failure message shows the last 50 captured records, after one line that counts the earlier ones,
/// for every assertion; the explicit dumps (<c>DumpTo</c>, <c>DumpToTestOutput</c>, <c>DumpToTestOutputOnFailure</c>)
/// write every record.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class FailureRecordCapTests
{
    private const string NotShown = "earlier record(s) not shown)";

    /// <summary>49 and 50 records are all shown, with no count line; of 51, the first is left out and counted.</summary>
    [Test]
    [Arguments(49)]
    [Arguments(50)]
    [Arguments(51)]
    public async Task HasLogged_FailureShowsTheLast50Records(int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Filled(count);

        var exception = await CatchAsync(async () => await Assert.That(collector).HasLogged().Containing("absent", StringComparison.Ordinal));

        var message = exception!.Message;
        await Assert.That(message).Contains(string.Create(CultureInfo.InvariantCulture, $"Captured records ({count} total):"), StringComparison.Ordinal);
        await Assert.That(Shown(message)).IsEqualTo(Math.Min(count, 50));
        await Assert.That(message.Contains(Line(1), StringComparison.Ordinal)).IsEqualTo(count <= 50);
        await Assert.That(message).Contains(Line(count), StringComparison.Ordinal);
        if (count > 50)
            await Assert.That(message).Contains("  (1 " + NotShown + Environment.NewLine + Line(2), StringComparison.Ordinal);
        else
            await Assert.That(message).DoesNotContain(NotShown, StringComparison.Ordinal);
    }

    /// <summary><c>HasNotLogged()</c>, <c>HasLoggedOnly()</c>, a sequence, and <c>WillLog</c> cap their failure dumps the
    /// same way.</summary>
    [Test]
    public async Task EveryAssertion_CapsItsFailureDump(CancellationToken cancellationToken)
    {
        var collector = Filled(51);

        foreach (var exception in new[]
        {
            await CatchAsync(async () => await Assert.That(collector).HasNotLogged().Containing("record", StringComparison.Ordinal)),
            await CatchAsync(async () => await Assert.That(collector).HasLoggedOnly(LogLevel.Information)),
            await CatchAsync(async () => await Assert.That(collector).HasLoggedSequence()
                .Containing("record 001", StringComparison.Ordinal)
                .Then().Containing("absent", StringComparison.Ordinal)),
            await CatchAsync(async () => await Assert.That(collector).WillLog(TimeSpan.FromSeconds(5), cancellationToken)
                .Containing("record", StringComparison.Ordinal).Once()),
        })
        {
            await Assert.That(Shown(exception!.Message)).IsEqualTo(50);
            await Assert.That(exception.Message).Contains("  (1 " + NotShown, StringComparison.Ordinal);
        }
    }

    /// <summary>The explicit dumps write all 51 records and no count line.</summary>
    [Test]
    public async Task ExplicitDumps_WriteEveryRecord(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = Filled(51);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        collector.DumpTo(writer);
        collector.DumpToTestOutput();

        // The on-failure dump writes the records captured after it was armed, through a table of this test's own
        // (the running test has no result yet, so it counts as not passed).
        var armed = new FakeLogCollector();
        var table = new TestLifetimeTable();
        table.MarkStarted(TestContext.Current!);
        table.Arm(TestContext.Current!, armed, DumpVerbosity.Default);
        Fill(armed, 51);
        await table.MarkEndedAsync(TestContext.Current!);

        var output = TestContext.Current!.GetStandardOutput();
        await Assert.That(Shown(writer.ToString())).IsEqualTo(51);
        await Assert.That(Shown(output)).IsEqualTo(51 + 51);
        await Assert.That(writer.ToString() + output).DoesNotContain(NotShown, StringComparison.Ordinal);
    }

    /// <summary>Creates a collector holding <paramref name="count"/> records, <c>record 001</c> on.</summary>
    /// <param name="count">The number of records.</param>
    /// <returns>The collector.</returns>
    private static FakeLogCollector Filled(int count)
    {
        var collector = new FakeLogCollector();
        Fill(collector, count);
        return collector;
    }

    /// <summary>Logs <paramref name="count"/> records into <paramref name="collector"/>, <c>record 001</c> on.</summary>
    /// <param name="collector">The collector.</param>
    /// <param name="count">The number of records.</param>
    private static void Fill(FakeLogCollector collector, int count)
    {
        var logger = new FakeLogger(collector, "Cap");
        for (var i = 1; i <= count; i++)
            DirectLog.Write(logger, Record(i));
    }

    /// <summary>The message of record <paramref name="number"/>.</summary>
    /// <param name="number">The record's number, from 1.</param>
    /// <returns>The message.</returns>
    private static string Record(int number) => string.Create(CultureInfo.InvariantCulture, $"record {number:D3}");

    /// <summary>The dump line of record <paramref name="number"/>.</summary>
    /// <param name="number">The record's number, from 1.</param>
    /// <returns>The line, with its line break.</returns>
    private static string Line(int number) => "  [info] Cap: " + Record(number) + Environment.NewLine;

    /// <summary>Counts the dump lines of records in <paramref name="text"/>.</summary>
    /// <param name="text">A failure message or a dump.</param>
    /// <returns>The number of record lines.</returns>
    private static int Shown(string text)
    {
        var shown = 0;
        for (var at = text.IndexOf("] Cap: record ", StringComparison.Ordinal); at >= 0; at = text.IndexOf("] Cap: record ", at + 1, StringComparison.Ordinal))
            shown++;
        return shown;
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
