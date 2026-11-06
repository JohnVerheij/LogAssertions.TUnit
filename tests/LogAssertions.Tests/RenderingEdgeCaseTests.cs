using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.Tests;

/// <summary>Edge cases of the failure-message rendering and of the filter descriptions: a level outside the defined
/// range, a property or scope value that is <see langword="null"/>, a <see langword="null"/> scope, and a scope with
/// several pairs.</summary>
[Category("Smoke")]
[Timeout(10_000)]
internal sealed class RenderingEdgeCaseTests
{
    /// <summary>A level outside the defined range renders as its number.</summary>
    [Test]
    public async Task LevelAbbreviation_UndefinedLevel_RendersTheNumber(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(LogAssertionRendering.LevelAbbreviation((LogLevel)42)).IsEqualTo("42");
    }

    /// <summary>The public renderer writes every record, at the default verbosity: only the TUnit adapter's failure
    /// messages are capped.</summary>
    [Test]
    public async Task AppendCapturedRecords_WritesEveryRecord(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "All");
        for (var i = 0; i < 51; i++)
            logger.Log(LogLevel.Information, default, "record", null, static (state, _) => state);

        var sb = new System.Text.StringBuilder();
        LogAssertionRendering.AppendCapturedRecords(sb, collector.GetSnapshot());

        await Assert.That(Regex.Count(sb.ToString(), Regex.Escape("[info] All: record"))).IsEqualTo(51);
        await Assert.That(sb.ToString()).DoesNotContain("not shown", StringComparison.Ordinal);
    }

    /// <summary>A <see langword="null"/> property value, a <see langword="null"/> scope, and a scope with several
    /// pairs (one of them <see langword="null"/>) render as <c>null</c> and as comma-separated pairs.</summary>
    [Test]
    public async Task DumpTo_NullValuesAndMultiPairScopes_Render(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var collector = new FakeLogCollector();
        var logger = new FakeLogger(collector, "Edge");
        var state = new List<KeyValuePair<string, object?>> { new("First", 1), new("Value", null), new("{OriginalFormat}", "value {Value}") };
        using (logger.BeginScope<object>(null!))
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = null }))
        {
            logger.Log(LogLevel.Information, default, state, null, static (_, _) => "value");
        }

        using var writer = new StringWriter();
        collector.DumpTo(writer);
        var dump = writer.ToString();

        await Assert.That(dump).Contains("    props: First=1, Value=null", StringComparison.Ordinal);
        await Assert.That(dump).Contains("    scope: null | a=1, b=null", StringComparison.Ordinal);
    }

    /// <summary>The descriptions of the regex filter and of the scope-property filter with a <see langword="null"/>
    /// and a non-<see langword="null"/> value.</summary>
    [Test]
    public async Task FilterDescriptions_RenderTheirValues(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(LogFilter.Matching(new Regex("^a+$", RegexOptions.None, TimeSpan.FromSeconds(1))).Description).IsEqualTo("Message matches /^a+$/");
        await Assert.That(LogFilter.WithScopeProperty("RequestId", (object?)null).Description).IsEqualTo("Scope RequestId = null");
        await Assert.That(LogFilter.WithScopeProperty("RequestId", (object)42).Description).IsEqualTo("Scope RequestId = 42");
    }
}
