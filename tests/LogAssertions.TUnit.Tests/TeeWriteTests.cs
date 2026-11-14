using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// The tee's write of one record, with other work running alongside: it picks the test, formats the record with its
/// exception text, and writes it in one write, under the lock the end of the test takes. A test that ends while its
/// record is being formatted gets no record; the write waits while the end holds the lock; a record and its exception
/// text stay together; records logged at once from many threads are each written once and whole. The first test
/// pauses the formatter (user code that can be slow) and ends the test there, so it needs no timing luck.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class TeeWriteTests
{
    /// <summary>The test ends (here on the test's thread) while its record is being formatted (here on a thread-pool
    /// thread that carries the test's context). The record must not be written into the ended test, and must be
    /// counted as not teed: the end check and the write have to be one step, as they are for arming a dump.</summary>
    [Test]
    public async Task TestEndsWhileItsRecordIsFormatted_RecordIsNotWrittenIntoTheEndedTest(CancellationToken cancellationToken)
    {
        var test = TestContext.Current!;
        var table = new TestLifetimeTable();
        table.MarkStarted(test);
        using var provider = new TestOutputLoggerProvider(owner: null, table);
        var logger = provider.CreateLogger("Ending");
        var formatting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ended = new ManualResetEventSlim();

        var logging = Task.Run(
            () => logger.Log(LogLevel.Information, default, "record of a test that ends meanwhile", null, (state, _) =>
            {
                formatting.TrySetResult();
                ended.Wait(cancellationToken);
                return state;
            }),
            cancellationToken);
        await formatting.Task.WaitAsync(cancellationToken);
        await table.MarkEndedAsync(test);
        ended.Set();
        await logging;

        await Assert.That(test.GetStandardOutput()).DoesNotContain("record of a test that ends meanwhile", StringComparison.Ordinal);
        await Assert.That(provider.SkippedRecordCount).IsEqualTo(1);
    }

    /// <summary>The teed write waits for the lock the end of the test takes: while another thread holds the entry's
    /// lock, a write does not complete; once that thread releases it, the write completes. A write that took another
    /// lock (or none) would complete at once. It does not catch a check made outside the lock with only the write
    /// inside it.</summary>
    [Test]
    public async Task TeedWrite_WaitsForTheLockTheEndTakes(CancellationToken cancellationToken)
    {
        var test = TestContext.Current!;
        var entry = new TestLifetimeEntry();
        entry.Start();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (entry.Gate)
            {
                held.TrySetResult();
                try
                {
                    release.Wait(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // The test was canceled: leave the lock. An exception on this thread would end the test host.
                }
            }
        })
        {
            IsBackground = true,
        };
        holder.Start();
        try
        {
            await held.Task.WaitAsync(cancellationToken);

            var writing = Task.Run(() => entry.TryWriteLine(test, "Gate: record written once the lock is free"), cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            await Assert.That(writing.IsCompleted).IsFalse();

            release.Set();
            await Assert.That(await writing.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)).IsTrue();
            await Assert.That(test.GetStandardOutput()).Contains("Gate: record written once the lock is free", StringComparison.Ordinal);
        }
        finally
        {
            // On every exit path the holder leaves the lock and ends before release is disposed.
            release.Set();
            holder.Join();
        }
    }

    /// <summary>Eight threads log 200 records each, every one with a thrown exception, into one test at the same time.
    /// Rendering an exception's stack trace takes a moment, so another thread's record can be written while it renders;
    /// every exception text must still follow its own record directly.</summary>
    [Test]
    public async Task ConcurrentRecordsWithExceptions_EachExceptionTextFollowsItsRecord(CancellationToken cancellationToken)
    {
        var test = TestContext.Current!;
        using var provider = new TestOutputLoggerProvider(test);

        await AssertEachExceptionFollowsItsRecordAsync(test, provider.CreateLogger("Split"), "Split", cancellationToken);
    }

    /// <summary>The same in a project without the attribute (a table that does not track the test), where no end check
    /// serializes the writes: the record and its exception text are still one write, so they stay together.</summary>
    [Test]
    public async Task ConcurrentRecordsWithExceptions_WithoutTheAttribute_EachExceptionTextFollowsItsRecord(CancellationToken cancellationToken)
    {
        var test = TestContext.Current!;
        using var provider = new TestOutputLoggerProvider(test, new TestLifetimeTable(static _ => false));

        await AssertEachExceptionFollowsItsRecordAsync(test, provider.CreateLogger("Untracked"), "Untracked", cancellationToken);
    }

    /// <summary>Eight threads log 500 records each into one test at the same time: every record is written exactly
    /// once, as one whole line.</summary>
    [Test]
    public async Task ConcurrentRecords_AreEachWrittenOnceAndWhole(CancellationToken cancellationToken)
    {
        const int Threads = 8;
        const int PerThread = 500;
        var test = TestContext.Current!;
        using var provider = new TestOutputLoggerProvider(test);
        var logger = provider.CreateLogger("Concurrent");
        using var start = new ManualResetEventSlim();

        var writers = Enumerable.Range(0, Threads)
            .Select(thread => Task.Run(
                () =>
                {
                    start.Wait(cancellationToken);
                    for (var i = 0; i < PerThread; i++)
                        DirectLog.Write(logger, $"concurrent-{thread}-{i}");
                },
                cancellationToken))
            .ToArray();
        start.Set();
        await Task.WhenAll(writers);

        var written = test.GetStandardOutput()
            .Split(Environment.NewLine)
            .Where(line => line.Contains("Concurrent:", StringComparison.Ordinal))
            .ToList();
        var expected = new HashSet<string>(
            Enumerable.Range(0, Threads).SelectMany(thread => Enumerable.Range(0, PerThread).Select(i => $"[Information] Concurrent: concurrent-{thread}-{i}")),
            StringComparer.Ordinal);

        await Assert.That(written.Count).IsEqualTo(Threads * PerThread);
        await Assert.That(expected.SetEquals(written)).IsTrue();
    }

    /// <summary>Logs 200 records with a thrown exception from each of eight threads at once, then checks that every
    /// record line in the test's output is followed by its own exception text.</summary>
    /// <param name="test">The test whose output the tee writes.</param>
    /// <param name="logger">The tee's logger.</param>
    /// <param name="category">The logger's category.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the check has run.</returns>
    private static async Task AssertEachExceptionFollowsItsRecordAsync(TestContext test, ILogger logger, string category, CancellationToken cancellationToken)
    {
        const int Threads = 8;
        const int PerThread = 200;
        using var start = new ManualResetEventSlim();
        var exceptions = Enumerable.Range(0, Threads)
            .Select(thread => Enumerable.Range(0, PerThread).Select(i => Thrown($"exception-{thread}-{i}")).ToArray())
            .ToArray();

        var writers = Enumerable.Range(0, Threads)
            .Select(thread => Task.Run(
                () =>
                {
                    start.Wait(cancellationToken);
                    for (var i = 0; i < PerThread; i++)
                        logger.Log(LogLevel.Error, default, $"record-{thread}-{i}", exceptions[thread][i], static (state, _) => state);
                },
                cancellationToken))
            .ToArray();
        start.Set();
        await Task.WhenAll(writers);

        var prefix = $"[Error] {category}: record-";
        var lines = test.GetStandardOutput().Split(Environment.NewLine);
        var records = 0;
        var split = new List<string>();
        for (var at = 0; at < lines.Length; at++)
        {
            if (!lines[at].StartsWith(prefix, StringComparison.Ordinal))
                continue;

            records++;
            var expected = "System.InvalidOperationException: exception-" + lines[at][prefix.Length..];
            if (at + 1 >= lines.Length || !string.Equals(lines[at + 1], expected, StringComparison.Ordinal))
                split.Add(lines[at]);
        }

        await Assert.That(records).IsEqualTo(Threads * PerThread);
        await Assert.That(split).IsEmpty().Because($"{split.Count} record(s) were not followed by their own exception text");
    }

    /// <summary>Throws and catches an exception, so it carries a stack trace to render.</summary>
    /// <param name="message">The exception's message.</param>
    /// <returns>The thrown exception.</returns>
    private static InvalidOperationException Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }
}
