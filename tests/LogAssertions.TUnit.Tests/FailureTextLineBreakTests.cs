using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Assertions.Exceptions;
using TUnit.Core;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// Caller-supplied text that holds a line break (a substring, a key, a message template) stays on one line in the
/// filter descriptions and the allowlist of a failure message, and a captured message with line breaks stays on one
/// line in its record: the failure text is line-structured, so one value must never split a line.
/// </summary>
[Category("Smoke")]
[Timeout(15_000)]
internal sealed class FailureTextLineBreakTests
{
    /// <summary>A substring with a line break is described on one line by <c>HasLogged()</c>, <c>HasNotLogged()</c>, and
    /// a sequence step, in its expectation clause.</summary>
    [Test]
    public async Task FilterDescription_WithLineBreak_IsOneLine(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Lines");
        DirectLog.Write(logger, "first\r\nsecond");

        var hasLogged = await CatchAsync(async () => await Assert.That(collector).HasLogged()
            .Containing("none\r\nsuch", StringComparison.Ordinal));
        var hasNotLogged = await CatchAsync(async () => await Assert.That(collector).HasNotLogged()
            .Containing("first\r\nsecond", StringComparison.Ordinal));
        var sequence = await CatchAsync(async () => await Assert.That(collector).HasLoggedSequence()
            .Containing("none\nsuch", StringComparison.Ordinal)
            .Then().Containing("never\rseen", StringComparison.Ordinal));
        var anyOrder = await CatchAsync(async () => await Assert.That(collector).HasLoggedSequence()
            .ThenAnyOrder(
                step => step.Containing("none\nsuch", StringComparison.Ordinal),
                step => step.Containing("never\rseen", StringComparison.Ordinal)));

        await Assert.That(hasLogged!.Message.ReplaceLineEndings("\n"))
            .Contains("matching: Message contains \"none such\" (Ordinal)", StringComparison.Ordinal);
        await Assert.That(hasNotLogged!.Message.ReplaceLineEndings("\n"))
            .Contains("matching: Message contains \"first second\" (Ordinal)", StringComparison.Ordinal);
        await Assert.That(sequence!.Message.ReplaceLineEndings("\n"))
            .Contains("Message contains \"none such\" (Ordinal) then Message contains \"never seen\" (Ordinal)", StringComparison.Ordinal)
            .And.Contains("Step filters: Message contains \"none such\" (Ordinal)", StringComparison.Ordinal);
        await Assert.That(anyOrder!.Message.ReplaceLineEndings("\n"))
            .Contains("(any-order: Message contains \"none such\" (Ordinal) / Message contains \"never seen\" (Ordinal))", StringComparison.Ordinal)
            .And.Contains("  [1] Message contains \"none such\" (Ordinal)", StringComparison.Ordinal);
    }

    /// <summary>A captured message with line breaks is one line of the failure message's record list, and the
    /// message template of an allowed definition is one line of the allowlist.</summary>
    [Test]
    public async Task CapturedMessageAndAllowlist_WithLineBreaks_AreOneLine(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Lines");
        DirectLog.Write(logger, "first\r\nsecond\nthird", LogLevel.Warning);
        var allowed = LogDefinition.Capture(log => log.Log(
            LogLevel.Warning,
            new EventId(9, "Multi\nLine"),
            new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "tpl\r\nline") },
            null,
            static (_, _) => "x"));

        var exception = await CatchAsync(async () => await Assert.That(collector).HasLoggedOnly(LogLevel.Warning).Allowing(allowed));

        var message = exception!.Message.ReplaceLineEndings("\n");
        await Assert.That(message).Contains("\n  [warn] Lines: first second third\n", StringComparison.Ordinal);
        await Assert.That(message).Contains("Allowed definitions:\n  Multi Line#9 \"tpl line\"\n", StringComparison.Ordinal);
    }

    /// <summary>A string argument of a chain method is written into the expression text as a C# string literal reads:
    /// backslash, quote, CR and LF are escaped, so the expression stays on one line and shows the exact argument.</summary>
    [Test]
    public async Task ExpressionText_EscapesCallerStringsAsLiterals(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        Func<string?, bool> any = static _ => true;
        var definition = LogDefinition.Capture(Multi);

        var first = await CatchAsync(async () => await Assert.That(collector).HasLogged()
            .Containing("c\r\n\"q\"", StringComparison.Ordinal)
            .NotContaining("n\nc", StringComparison.Ordinal)
            .WithMessageTemplate("t\ne")
            .WithExceptionMessage("x\ry", StringComparison.Ordinal)
            .WithInnerExceptionMessage("i\nm", StringComparison.Ordinal)
            .WithProperty("k\n1", "v\\1")
            .WithProperty("p\nk", any)
            .WithScopeProperty("s\n1", (object?)"v\n2")
            .WithProperty("none", (string?)null));
        var second = await CatchAsync(async () => await Assert.That(collector).HasLogged()
            .WithCategory("cat\negory")
            .WithLoggerName("log\rger")
            .ExcludingCategory("ex\ncl")
            .WithEventName("ev\nent")
            .Matching(definition)
            .MatchingCall(Multi)
            .WithProperty("n\n", 7)
            .WithScopeProperty("t\n", 8));

        var one = first!.Message;
        await Assert.That(one).Contains("""
            .Containing("c\r\n\"q\"", Ordinal).NotContaining("n\nc", Ordinal).WithMessageTemplate("t\ne")
            """, StringComparison.Ordinal);
        await Assert.That(one).Contains("""
            .WithExceptionMessage("x\ry", Ordinal).WithInnerExceptionMessage("i\nm", Ordinal)
            """, StringComparison.Ordinal);
        await Assert.That(one).Contains("""
            .WithProperty("k\n1", "v\\1").WithProperty("p\nk", predicate).WithScopeProperty("s\n1", v\n2).WithProperty("none", "")
            """, StringComparison.Ordinal);
        var two = second!.Message;
        await Assert.That(two).Contains("""
            .WithCategory("cat\negory").WithLoggerName("log\rger").ExcludingCategory("ex\ncl").WithEventName("ev\nent")
            """, StringComparison.Ordinal);
        await Assert.That(two).Contains("""
            .Matching(Multi\nLine#9 \"tpl\r\nline\").MatchingCall(Multi\nLine#9 \"tpl\r\nline\").WithProperty("n\n", 7).WithScopeProperty("t\n", 8)
            """, StringComparison.Ordinal);
        await Assert.That(one).DoesNotContain("c\r\n\"q", StringComparison.Ordinal);
        await Assert.That(two).DoesNotContain("cat\negory", StringComparison.Ordinal);
    }

    /// <summary>A definition whose event name and template hold line breaks.</summary>
    /// <param name="log">The logger.</param>
    private static void Multi(ILogger log) => log.Log(
        LogLevel.Warning,
        new EventId(9, "Multi\nLine"),
        new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "tpl\r\nline") },
        null,
        static (_, _) => "x");

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
