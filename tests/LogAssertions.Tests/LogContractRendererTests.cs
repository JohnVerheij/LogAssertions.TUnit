using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions.Render;
using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>Tests for <see cref="LogContractRenderer"/>: the line format, the ordering, the escaping, the edge cases,
/// and the argument checks.</summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class LogContractRendererTests
{
    private static readonly LogDefinition OrderProcessed = LogDefinition.Capture(log => CoreTestLogMessages.OrderProcessed(log, 0, 0));
    private static readonly LogDefinition Reversed = LogDefinition.Capture(log => CoreContractLogMessages.Reversed(log, "a", "b"));
    private static readonly LogDefinition Escaped = LogDefinition.Capture(CoreTestLogMessages.Escaped);
    private static readonly LogDefinition Unnamed = LogDefinition.Capture(log => LogX(log, LogLevel.Information, new EventId(5), "unnamed {X}"));
    private static readonly LogDefinition NoTemplate = LogDefinition.Capture(
        log => log.Log(LogLevel.Information, new EventId(6, "Raw"), "raw state", null, static (state, _) => state));
    private static readonly LogDefinition SameTemplateBInformation = LogDefinition.Capture(log => LogX(log, LogLevel.Information, new EventId(1300, "Same"), "template B {X}"));
    private static readonly LogDefinition SameTemplateAWarning = LogDefinition.Capture(log => LogX(log, LogLevel.Warning, new EventId(1300, "Same"), "template A {X}"));
    private static readonly LogDefinition SameTemplateAInformation = LogDefinition.Capture(log => LogX(log, LogLevel.Information, new EventId(1300, "Same"), "template A {X}"));
    private static readonly LogDefinition BetaWithTemplateA = LogDefinition.Capture(log => LogX(log, LogLevel.Information, new EventId(1400, "Beta"), "a {X}"));
    private static readonly LogDefinition AlphaWithTemplateB = LogDefinition.Capture(log => LogX(log, LogLevel.Information, new EventId(1400, "Alpha"), "b {X}"));

    /// <summary>The handoff's example line, exactly: ID, name, level, quoted template, placeholder names in template
    /// order, LF.</summary>
    [Test]
    public async Task Render_OneDefinition_WritesTheContractLine(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var contract = LogContractRenderer.Render([OrderProcessed]);

        await Assert.That(contract).IsEqualTo("1201 OrderProcessed Information \"Order {OrderId} processed in {DurationMs} ms\" OrderId,DurationMs\n");
    }

    /// <summary>Every case at once, in scrambled input order: ordered by ID, then name (even when the templates sort
    /// the other way), then template, then level; a missing name is <c>?</c>; a missing template is
    /// <c>&lt;no template&gt;</c>; placeholder names follow the template, not the parameters; a template without
    /// placeholders ends the line; escapes keep each line whole.</summary>
    [Test]
    public async Task Render_ManyDefinitions_IsOrderedAndEscaped(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var contract = LogContractRenderer.Render(
            [SameTemplateBInformation, Reversed, BetaWithTemplateA, NoTemplate, SameTemplateAWarning, Escaped, OrderProcessed, AlphaWithTemplateB, Unnamed, SameTemplateAInformation]);

        await Assert.That(contract).IsEqualTo(
            "5 ? Information \"unnamed {X}\" X\n"
            + "6 Raw Information <no template>\n"
            + "1200 Escaped Warning \"Say \\\"hi\\\" at C:\\\\path\\r\\n\\tnow\"\n"
            + "1201 OrderProcessed Information \"Order {OrderId} processed in {DurationMs} ms\" OrderId,DurationMs\n"
            + "1201 Reversed Debug \"{B} then {A}\" B,A\n"
            + "1300 Same Information \"template A {X}\" X\n"
            + "1300 Same Warning \"template A {X}\" X\n"
            + "1300 Same Information \"template B {X}\" X\n"
            + "1400 Alpha Information \"b {X}\" X\n"
            + "1400 Beta Information \"a {X}\" X\n");
    }

    /// <summary>Placeholder names follow the template, also with a format, an alignment, a repeated placeholder, and an
    /// escaped brace; the generator's own order (here the reverse) does not leak into the contract.</summary>
    [Test]
    public async Task Render_PlaceholderNames_FollowTheTemplate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var paid = LogDefinition.Capture(log => CoreTestLogMessages.Paid(log, "payee", 1m));
        var braced = LogDefinition.Capture(log => LogState(log, 9, "{{{Payee}}} paid {Amount}", ("Amount", 1), ("Payee", "p")));

        await Assert.That(LogContractRenderer.Render([paid, braced])).IsEqualTo(
            "9 Manual Information \"{{{Payee}}} paid {Amount}\" Payee,Amount\n"
            + "1202 Paid Information \"Paid {Amount:N2} {{not a placeholder}} to {Payee,10}, {Amount:N0} in total\" Amount,Payee\n");
    }

    /// <summary>A template with an unclosed or trailing brace has no placeholders, so names the template does not
    /// contain keep their captured order.</summary>
    [Test]
    public async Task Render_UnclosedBraces_KeepTheCapturedOrder(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unclosed = LogDefinition.Capture(log => LogState(log, 7, "unclosed {Brace", ("Extra", 1), ("Brace", 2)));
        var trailing = LogDefinition.Capture(log => LogState(log, 8, "ends with {"));

        await Assert.That(LogContractRenderer.Render([trailing, unclosed])).IsEqualTo(
            "7 Manual Information \"unclosed {Brace\" Extra,Brace\n"
            + "8 Manual Information \"ends with {\"\n");
    }

    /// <summary>Two definitions that tie on ID, name, template, and level but differ in a placeholder the template does
    /// not contain render in the same order whatever the input order, as every other definition does.</summary>
    [Test]
    public async Task Render_DefinitionsThatTieOnEveryKey_AreOrderedWhateverTheInputOrder(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plain = LogDefinition.Capture(log => LogState(log, 20, "tie {A}", ("A", 1)));
        var extra = LogDefinition.Capture(log => LogState(log, 20, "tie {A}", ("A", 1), ("Extra", 2)));

        var forward = LogContractRenderer.Render([plain, extra]);
        var backward = LogContractRenderer.Render([extra, plain]);

        await Assert.That(backward).IsEqualTo(forward);
        await Assert.That(forward).IsEqualTo(
            "20 Manual Information \"tie {A}\" A\n"
            + "20 Manual Information \"tie {A}\" A,Extra\n");
    }

    /// <summary>An event name and a placeholder name are escaped as the template is, so a name with a line break still
    /// renders its definition on one line.</summary>
    [Test]
    public async Task Render_NamesWithLineBreaks_StayOnOneLine(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var named = LogDefinition.Capture(log => LogRecord(log, LogLevel.Information, new EventId(21, "two\nlines"), "{Line\r\nBreak} at\tthe end", [("Line\r\nBreak", 1)]));

        var contract = LogContractRenderer.Render([named]);

        await Assert.That(contract).IsEqualTo("21 two\\nlines Information \"{Line\\r\\nBreak} at\\tthe end\" Line\\r\\nBreak\n");
        await Assert.That(contract.Split('\n')).Count().IsEqualTo(2);
    }

    /// <summary>A large set renders in the same order whatever the input order, and the text does not depend on the
    /// current culture (the Turkish culture changes the case of "I" and the formatting of numbers).</summary>
    [Test]
    public async Task Render_LargeShuffledSet_IsOrderedAndCultureIndependent(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = new List<LogDefinition>();
        for (var id = 1; id <= 500; id++)
        {
            var current = id;
            definitions.Add(LogDefinition.Capture(log => LogX(log, LogLevel.Information, new EventId(current, "Event" + current), "Item {X} number " + current)));
        }

        var forward = LogContractRenderer.Render(definitions);
        var shuffled = new List<LogDefinition>(definitions);
        shuffled.Reverse();
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        string turkish;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            turkish = LogContractRenderer.Render(shuffled);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        await Assert.That(turkish).IsEqualTo(forward);
        await Assert.That(forward.Split('\n').Length).IsEqualTo(501);
        await Assert.That(forward).StartsWith("1 Event1 Information \"Item {X} number 1\" X\n", StringComparison.Ordinal);
    }

    /// <summary>No definitions render as the empty string.</summary>
    [Test]
    public async Task Render_NoDefinitions_IsEmpty(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(LogContractRenderer.Render([])).IsEqualTo(string.Empty);
    }

    /// <summary>A <see langword="null"/> argument and a <see langword="null"/> element are rejected.</summary>
    [Test]
    public async Task Render_NullInput_Throws(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(() => LogContractRenderer.Render(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => LogContractRenderer.Render([OrderProcessed, null!])).Throws<ArgumentException>();
    }

    /// <summary>Logs a record whose state is a key/value list built by hand: <paramref name="template"/> as its
    /// <c>{OriginalFormat}</c>, then <paramref name="pairs"/>.</summary>
    /// <param name="logger">The probe logger.</param>
    /// <param name="eventId">The numeric event ID; the name is <c>Manual</c>.</param>
    /// <param name="template">The template.</param>
    /// <param name="pairs">The other pairs, in order.</param>
    private static void LogState(ILogger logger, int eventId, string template, params (string Key, object? Value)[] pairs)
        => LogRecord(logger, LogLevel.Information, new EventId(eventId, "Manual"), template, pairs);

    /// <summary>Logs <paramref name="template"/> with the one placeholder <c>X</c>, as hand-built key/value state.</summary>
    /// <param name="logger">The probe logger.</param>
    /// <param name="level">The level.</param>
    /// <param name="eventId">The event ID.</param>
    /// <param name="template">A template whose one placeholder is <c>X</c>.</param>
    private static void LogX(ILogger logger, LogLevel level, EventId eventId, string template)
        => LogRecord(logger, level, eventId, template, [("X", 1)]);

    /// <summary>Logs a record whose state is <paramref name="template"/> as its <c>{OriginalFormat}</c>, then
    /// <paramref name="pairs"/>.</summary>
    /// <param name="logger">The probe logger.</param>
    /// <param name="level">The level.</param>
    /// <param name="eventId">The event ID.</param>
    /// <param name="template">The template.</param>
    /// <param name="pairs">The other pairs, in order.</param>
    private static void LogRecord(ILogger logger, LogLevel level, EventId eventId, string template, (string Key, object? Value)[] pairs)
    {
        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", template) };
        foreach (var (key, value) in pairs)
            state.Add(new KeyValuePair<string, object?>(key, value));
        logger.Log(level, eventId, state, null, static (_, _) => "manual");
    }
}
