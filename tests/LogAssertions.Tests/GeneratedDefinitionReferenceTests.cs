using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions.Render;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Ex = LogAssertions.Generator.Fixture.Extensions;
using Rt = LogAssertions.Generator.Fixture;

[assembly: LogAssertions.GenerateLogDefinitions(typeof(LogAssertions.Generator.Fixture.FixtureLogMessages))]
[assembly: LogAssertions.GenerateLogDefinitions(typeof(LogAssertions.Generator.Fixture.Extensions.FixtureLogMessages))]

namespace LogAssertions.Tests;

/// <summary>
/// Exact-reference tests of the log-definition generator: each definition generated from a fixture assembly's metadata
/// must equal what the method's real record carries, as Microsoft's logging generator of the target framework wrote it.
/// Every method runs through both Microsoft generators: the one of Microsoft.Extensions.Logging.Abstractions (the
/// runtime fixture) and the one of Microsoft.Extensions.Telemetry (the extensions fixture). This is the guard on the
/// derived event ID and on the record shapes, which are those generators' internal code: when one changes, the tests
/// of the affected generator and target framework fail.
/// </summary>
[Category("Generator")]
[Timeout(10_000)]
internal sealed class GeneratedDefinitionReferenceTests
{
    /// <summary>Every method of the runtime fixture.</summary>
    /// <returns>The cases.</returns>
    public static IEnumerable<Func<ReferenceCase>> RuntimeCases()
    {
        yield return () => new ReferenceCase("Runtime/ConstructorArguments", Rt.FixtureLogMessagesLogDefinitions.ConstructorArguments, static log => Rt.FixtureLogMessages.ConstructorArguments(log, 7, "Oslo"));
        yield return () => new ReferenceCase("Runtime/NamedArguments", Rt.FixtureLogMessagesLogDefinitions.NamedArguments, static log => Rt.FixtureLogMessages.NamedArguments(log, 1, 3));
        yield return () => new ReferenceCase("Runtime/DerivedFromMethodName", Rt.FixtureLogMessagesLogDefinitions.DerivedFromMethodName, static log => Rt.FixtureLogMessages.DerivedFromMethodName(log, "x"));
        yield return () => new ReferenceCase("Runtime/DerivedFromEventName", Rt.FixtureLogMessagesLogDefinitions.DerivedFromEventName, static log => Rt.FixtureLogMessages.DerivedFromEventName(log, "x"));
        yield return () => new ReferenceCase("Runtime/EventIdAndEventName", Rt.FixtureLogMessagesLogDefinitions.EventIdAndEventName, static log => Rt.FixtureLogMessages.EventIdAndEventName(log, "x"));
        yield return () => new ReferenceCase("Runtime/LevelAndMessageConstructor", Rt.FixtureLogMessagesLogDefinitions.LevelAndMessageConstructor, static log => Rt.FixtureLogMessages.LevelAndMessageConstructor(log, "x"));
        yield return () => new ReferenceCase("Runtime/NoMessage", Rt.FixtureLogMessagesLogDefinitions.NoMessage, static log => Rt.FixtureLogMessages.NoMessage(log));
        yield return () => new ReferenceCase("Runtime/EmptyMessage", Rt.FixtureLogMessagesLogDefinitions.EmptyMessage, static log => Rt.FixtureLogMessages.EmptyMessage(log));
        yield return () => new ReferenceCase("Runtime/NoPlaceholders", Rt.FixtureLogMessagesLogDefinitions.NoPlaceholders, static log => Rt.FixtureLogMessages.NoPlaceholders(log));
        yield return () => new ReferenceCase("Runtime/FormatAlignmentAndEscapes", Rt.FixtureLogMessagesLogDefinitions.FormatAlignmentAndEscapes, static log => Rt.FixtureLogMessages.FormatAlignmentAndEscapes(log, 1.5m, "n"));
        yield return () => new ReferenceCase("Runtime/CaseAndEscapedCharacters", Rt.FixtureLogMessagesLogDefinitions.CaseAndEscapedCharacters, static log => Rt.FixtureLogMessages.CaseAndEscapedCharacters(log, "orders/a.json"));
        yield return () => new ReferenceCase("Runtime/WithException", Rt.FixtureLogMessagesLogDefinitions.WithException, static log => Rt.FixtureLogMessages.WithException(log, new InvalidOperationException("boom"), "op"));
        yield return () => new ReferenceCase("Runtime/SevenPlaceholders", Rt.FixtureLogMessagesLogDefinitions.SevenPlaceholders, static log => Rt.FixtureLogMessages.SevenPlaceholders(log, 1, 2, 3, 4, 5, 6, 7));
        yield return () => new ReferenceCase("Runtime/ContextualKeyword", Rt.FixtureLogMessagesLogDefinitions.async, static log => Rt.FixtureLogMessages.async(log, "x"));
        yield return () => new ReferenceCase("Runtime/EmptyEventName", Rt.FixtureLogMessagesLogDefinitions.EmptyEventName, static log => Rt.FixtureLogMessages.EmptyEventName(log));
        yield return () => new ReferenceCase("Runtime/WhitespaceEventName", Rt.FixtureLogMessagesLogDefinitions.WhitespaceEventName, static log => Rt.FixtureLogMessages.WhitespaceEventName(log));
        yield return () => new ReferenceCase("Runtime/UnusedParameter", Rt.FixtureLogMessagesLogDefinitions.UnusedParameter, static log => Rt.FixtureLogMessages.UnusedParameter(log, 1, 2));
        yield return () => new ReferenceCase("Runtime/NoMessageWithParameter", Rt.FixtureLogMessagesLogDefinitions.NoMessageWithParameter, static log => Rt.FixtureLogMessages.NoMessageWithParameter(log, 5));
        yield return () => new ReferenceCase("Runtime/TwoUnusedParameters", Rt.FixtureLogMessagesLogDefinitions.TwoUnusedParameters, static log => Rt.FixtureLogMessages.TwoUnusedParameters(log, 1, 2, 3, 4));
        yield return () => new ReferenceCase("Runtime/LevelValueParameter", Rt.FixtureLogMessagesLogDefinitions.LevelValueParameter, static log => Rt.FixtureLogMessages.LevelValueParameter(log, 1, LogLevel.Debug));
        yield return () => new ReferenceCase("Runtime/Backslashes", Rt.FixtureLogMessagesLogDefinitions.Backslashes, static log => Rt.FixtureLogMessages.Backslashes(log, 1));
        yield return () => new ReferenceCase("Runtime/AtDefine", Rt.FixtureLogMessagesLogDefinitions.AtDefine, static log => Rt.FixtureLogMessages.AtDefine(log, "i", 2));
        yield return () => new ReferenceCase("Runtime/AtDynamic", Rt.FixtureLogMessagesLogDefinitions.AtDynamic, static log => Rt.FixtureLogMessages.AtDynamic(log, LogLevel.Warning, "r"));
        yield return () => new ReferenceCase("Runtime/LevelInTemplate", Rt.FixtureLogMessagesLogDefinitions.LevelInTemplate, static log => Rt.FixtureLogMessages.LevelInTemplate(log, LogLevel.Warning, 1));
        yield return () => new ReferenceCase("Runtime/ExceptionInTemplate", Rt.FixtureLogMessagesLogDefinitions.ExceptionInTemplate, static log => Rt.FixtureLogMessages.ExceptionInTemplate(log, 1, new InvalidOperationException("boom")));
        yield return () => new ReferenceCase("Runtime/BackslashEventName", Rt.FixtureLogMessagesLogDefinitions.BackslashEventName, static log => Rt.FixtureLogMessages.BackslashEventName(log));
        yield return () => new ReferenceCase("Runtime/BackslashEventNameDerived", Rt.FixtureLogMessagesLogDefinitions.BackslashEventNameDerived, static log => Rt.FixtureLogMessages.BackslashEventNameDerived(log));
        yield return () => new ReferenceCase("Runtime/QuotedEventName", Rt.FixtureLogMessagesLogDefinitions.QuotedEventName, static log => Rt.FixtureLogMessages.QuotedEventName(log));
        yield return () => new ReferenceCase("Runtime/NestedType", Rt.FixtureLogMessages_NestedLogDefinitions.InNestedType, static log => Rt.FixtureLogMessages.Nested.InNestedType(log, "x"));
        yield return () => new ReferenceCase("Runtime/InstanceMethod", Rt.InstanceLogMessagesLogDefinitions.Started, static log => new Rt.InstanceLogMessages(log).Started(3));
        yield return () => new ReferenceCase("Runtime/GenericType", Rt.GenericLogMessages_1LogDefinitions.InGenericType, static log => Rt.GenericLogMessages<int>.InGenericType(log, 5));
    }

    /// <summary>Every method of the extensions fixture.</summary>
    /// <returns>The cases.</returns>
    public static IEnumerable<Func<ReferenceCase>> ExtensionsCases()
    {
        yield return () => new ReferenceCase("Extensions/ConstructorArguments", Ex.FixtureLogMessagesLogDefinitions.ConstructorArguments, static log => Ex.FixtureLogMessages.ConstructorArguments(log, 7, "Oslo"));
        yield return () => new ReferenceCase("Extensions/NamedArguments", Ex.FixtureLogMessagesLogDefinitions.NamedArguments, static log => Ex.FixtureLogMessages.NamedArguments(log, 1, 3));
        yield return () => new ReferenceCase("Extensions/DerivedFromMethodName", Ex.FixtureLogMessagesLogDefinitions.DerivedFromMethodName, static log => Ex.FixtureLogMessages.DerivedFromMethodName(log, "x"));
        yield return () => new ReferenceCase("Extensions/DerivedFromEventName", Ex.FixtureLogMessagesLogDefinitions.DerivedFromEventName, static log => Ex.FixtureLogMessages.DerivedFromEventName(log, "x"));
        yield return () => new ReferenceCase("Extensions/EventIdAndEventName", Ex.FixtureLogMessagesLogDefinitions.EventIdAndEventName, static log => Ex.FixtureLogMessages.EventIdAndEventName(log, "x"));
        yield return () => new ReferenceCase("Extensions/LevelAndMessageConstructor", Ex.FixtureLogMessagesLogDefinitions.LevelAndMessageConstructor, static log => Ex.FixtureLogMessages.LevelAndMessageConstructor(log, "x"));
        yield return () => new ReferenceCase("Extensions/NoMessage", Ex.FixtureLogMessagesLogDefinitions.NoMessage, static log => Ex.FixtureLogMessages.NoMessage(log));
        yield return () => new ReferenceCase("Extensions/EmptyMessage", Ex.FixtureLogMessagesLogDefinitions.EmptyMessage, static log => Ex.FixtureLogMessages.EmptyMessage(log));
        yield return () => new ReferenceCase("Extensions/NoPlaceholders", Ex.FixtureLogMessagesLogDefinitions.NoPlaceholders, static log => Ex.FixtureLogMessages.NoPlaceholders(log));
        yield return () => new ReferenceCase("Extensions/FormatAlignmentAndEscapes", Ex.FixtureLogMessagesLogDefinitions.FormatAlignmentAndEscapes, static log => Ex.FixtureLogMessages.FormatAlignmentAndEscapes(log, 1.5m, "n"));
        yield return () => new ReferenceCase("Extensions/CaseAndEscapedCharacters", Ex.FixtureLogMessagesLogDefinitions.CaseAndEscapedCharacters, static log => Ex.FixtureLogMessages.CaseAndEscapedCharacters(log, "orders/a.json"));
        yield return () => new ReferenceCase("Extensions/WithException", Ex.FixtureLogMessagesLogDefinitions.WithException, static log => Ex.FixtureLogMessages.WithException(log, new InvalidOperationException("boom"), "op"));
        yield return () => new ReferenceCase("Extensions/SevenPlaceholders", Ex.FixtureLogMessagesLogDefinitions.SevenPlaceholders, static log => Ex.FixtureLogMessages.SevenPlaceholders(log, 1, 2, 3, 4, 5, 6, 7));
        yield return () => new ReferenceCase("Extensions/ContextualKeyword", Ex.FixtureLogMessagesLogDefinitions.async, static log => Ex.FixtureLogMessages.async(log, "x"));
        yield return () => new ReferenceCase("Extensions/EmptyEventName", Ex.FixtureLogMessagesLogDefinitions.EmptyEventName, static log => Ex.FixtureLogMessages.EmptyEventName(log));
        yield return () => new ReferenceCase("Extensions/WhitespaceEventName", Ex.FixtureLogMessagesLogDefinitions.WhitespaceEventName, static log => Ex.FixtureLogMessages.WhitespaceEventName(log));
        yield return () => new ReferenceCase("Extensions/UnusedParameter", Ex.FixtureLogMessagesLogDefinitions.UnusedParameter, static log => Ex.FixtureLogMessages.UnusedParameter(log, 1, 2));
        yield return () => new ReferenceCase("Extensions/NoMessageWithParameter", Ex.FixtureLogMessagesLogDefinitions.NoMessageWithParameter, static log => Ex.FixtureLogMessages.NoMessageWithParameter(log, 5));
        yield return () => new ReferenceCase("Extensions/TwoUnusedParameters", Ex.FixtureLogMessagesLogDefinitions.TwoUnusedParameters, static log => Ex.FixtureLogMessages.TwoUnusedParameters(log, 1, 2, 3, 4));
        yield return () => new ReferenceCase("Extensions/LevelValueParameter", Ex.FixtureLogMessagesLogDefinitions.LevelValueParameter, static log => Ex.FixtureLogMessages.LevelValueParameter(log, 1, LogLevel.Debug));
        yield return () => new ReferenceCase("Extensions/Backslashes", Ex.FixtureLogMessagesLogDefinitions.Backslashes, static log => Ex.FixtureLogMessages.Backslashes(log, 1));
        yield return () => new ReferenceCase("Extensions/AtDefine", Ex.FixtureLogMessagesLogDefinitions.AtDefine, static log => Ex.FixtureLogMessages.AtDefine(log, "i", 2));
        yield return () => new ReferenceCase("Extensions/AtDynamic", Ex.FixtureLogMessagesLogDefinitions.AtDynamic, static log => Ex.FixtureLogMessages.AtDynamic(log, LogLevel.Warning, "r"));
        yield return () => new ReferenceCase("Extensions/LevelInTemplate", Ex.FixtureLogMessagesLogDefinitions.LevelInTemplate, static log => Ex.FixtureLogMessages.LevelInTemplate(log, LogLevel.Warning, 1));
        yield return () => new ReferenceCase("Extensions/ExceptionInTemplate", Ex.FixtureLogMessagesLogDefinitions.ExceptionInTemplate, static log => Ex.FixtureLogMessages.ExceptionInTemplate(log, 1, new InvalidOperationException("boom")));
        yield return () => new ReferenceCase("Extensions/BackslashEventName", Ex.FixtureLogMessagesLogDefinitions.BackslashEventName, static log => Ex.FixtureLogMessages.BackslashEventName(log));
        yield return () => new ReferenceCase("Extensions/BackslashEventNameDerived", Ex.FixtureLogMessagesLogDefinitions.BackslashEventNameDerived, static log => Ex.FixtureLogMessages.BackslashEventNameDerived(log));
        yield return () => new ReferenceCase("Extensions/SpacedPlaceholders", Ex.FixtureLogMessagesLogDefinitions.SpacedPlaceholders, static log => Ex.FixtureLogMessages.SpacedPlaceholders(log, 1, 2));
        yield return () => new ReferenceCase("Extensions/ClassifiedParameter", Ex.FixtureLogMessagesLogDefinitions.ClassifiedParameter, static log => Ex.FixtureLogMessages.ClassifiedParameter(log, "u", "c"));
        yield return () => new ReferenceCase("Extensions/QuotedEventName", Ex.FixtureLogMessagesLogDefinitions.QuotedEventName, static log => Ex.FixtureLogMessages.QuotedEventName(log));
        yield return () => new ReferenceCase("Extensions/NestedType", Ex.FixtureLogMessages_NestedLogDefinitions.InNestedType, static log => Ex.FixtureLogMessages.Nested.InNestedType(log, "x"));
        yield return () => new ReferenceCase("Extensions/InstanceMethod", Ex.InstanceLogMessagesLogDefinitions.Started, static log => new Ex.InstanceLogMessages(log).Started(3));
        yield return () => new ReferenceCase("Extensions/GenericType", Ex.GenericLogMessages_1LogDefinitions.InGenericType, static log => Ex.GenericLogMessages<int>.InGenericType(log, 5));
    }

    /// <summary>Verifies a generated definition renders the same contract line as the definition captured from the
    /// method's real record: event ID, event name, level, template, and placeholder names. For a method that takes its
    /// level as a parameter, the generated definition has no level and the captured one the probe call's, so the level is
    /// left out of that comparison.</summary>
    /// <param name="reference">The fixture method.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    [MethodDataSource(nameof(RuntimeCases))]
    [MethodDataSource(nameof(ExtensionsCases))]
    public async Task GeneratedEqualsCapturedAsync(ReferenceCase reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = LogDefinition.Capture(reference.Invoke);
        var expected = reference.Generated.Level is null
            ? LogDefinition.Create(captured.Id, null, captured.Template, captured.PlaceholderNames)
            : captured;

        await Assert.That(LogContractRenderer.Render([reference.Generated]))
            .IsEqualTo(LogContractRenderer.Render([expected]));
    }

    /// <summary>Verifies a generated definition lists the placeholder names in the order the record carries them, which
    /// the contract line (it orders them by the template) does not show.</summary>
    /// <param name="reference">The fixture method.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    [MethodDataSource(nameof(RuntimeCases))]
    [MethodDataSource(nameof(ExtensionsCases))]
    public async Task GeneratedPlaceholderNamesAreInRecordOrderAsync(ReferenceCase reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = LogDefinition.Capture(reference.Invoke);

        await Assert.That(string.Join(",", reference.Generated.PlaceholderNames))
            .IsEqualTo(string.Join(",", captured.PlaceholderNames));
    }

    /// <summary>Verifies a generated definition matches the method's real record.</summary>
    /// <param name="reference">The fixture method.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    [MethodDataSource(nameof(RuntimeCases))]
    [MethodDataSource(nameof(ExtensionsCases))]
    public async Task GeneratedMatchesTheRecordAsync(ReferenceCase reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        reference.Invoke(new FakeLogger(collector));

        await Assert.That(collector.CountMatching(LogFilter.Matching(reference.Generated))).IsEqualTo(1);
    }

    /// <summary>Verifies the documented limit of <c>[TagName]</c> under the generator of Microsoft.Extensions.Telemetry:
    /// the attribute is not compiled into the product, so the generated placeholder names keep the parameter names, while
    /// the identity still matches the record.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task TagNamesKeepTheIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FakeLogCollector collector = new();
        FakeLogger logger = new(collector);
        Ex.FixtureLogMessages.TagNames(logger, "v", "s");
        var captured = LogDefinition.Capture(static log => Ex.FixtureLogMessages.TagNames(log, "v", "s"));

        await Assert.That(collector.CountMatching(LogFilter.Matching(Ex.FixtureLogMessagesLogDefinitions.TagNames))).IsEqualTo(1);
        await Assert.That(string.Join(",", Ex.FixtureLogMessagesLogDefinitions.TagNames.PlaceholderNames)).IsEqualTo("second,value");
        await Assert.That(string.Join(",", captured.PlaceholderNames)).IsEqualTo("other.name,custom");
    }

    /// <summary>Verifies the definition of a method that takes its level as a parameter, through both generators: no
    /// level, and the rest equal to the captured definition.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task LevelParameterHasNoLevelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        (LogDefinition Generated, Action<ILogger> Invoke)[] methods =
        [
            (Rt.FixtureLogMessagesLogDefinitions.LevelParameter, static log => Rt.FixtureLogMessages.LevelParameter(log, LogLevel.Warning, "x")),
            (Ex.FixtureLogMessagesLogDefinitions.LevelParameter, static log => Ex.FixtureLogMessages.LevelParameter(log, LogLevel.Warning, "x")),
        ];

        foreach (var (generated, invoke) in methods)
        {
            var captured = LogDefinition.Capture(invoke);

            await Assert.That(generated.Level).IsNull();
            await Assert.That(generated.Id).IsEqualTo(captured.Id);
            await Assert.That(generated.Template).IsEqualTo(captured.Template);
            await Assert.That(generated.PlaceholderNames).IsEquivalentTo(captured.PlaceholderNames);
            await Assert.That(LogContractRenderer.Render([generated]))
                .IsEqualTo("1004 LevelParameter ? \"The level is a parameter {Value}\" Value\n");
        }
    }

    /// <summary>Verifies the derived event IDs are the ones Microsoft's generator assigns, pinned as numbers, so a change
    /// of its hash shows here by name as well as in the comparison with the records.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task DerivedEventIdsArePinnedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(Rt.FixtureLogMessagesLogDefinitions.DerivedFromMethodName.Id)
            .IsEqualTo(new EventId(Generator.EventIdHash.Of("DerivedFromMethodName"), "DerivedFromMethodName"));
        await Assert.That(Rt.FixtureLogMessagesLogDefinitions.DerivedFromEventName.Id)
            .IsEqualTo(new EventId(Generator.EventIdHash.Of("CustomEventName"), "CustomEventName"));
        await Assert.That(Rt.FixtureLogMessagesLogDefinitions.EmptyEventName.Id)
            .IsEqualTo(new EventId(Generator.EventIdHash.Of("EmptyEventName"), "EmptyEventName"));
        await Assert.That(Generator.EventIdHash.Of("OrderShipped")).IsEqualTo(943003114);
        await Assert.That(Generator.EventIdHash.Of("OrderShippedEvent")).IsEqualTo(1918069794);
        await Assert.That(Generator.EventIdHash.Of("LevelParam")).IsEqualTo(741982794);
    }
}
