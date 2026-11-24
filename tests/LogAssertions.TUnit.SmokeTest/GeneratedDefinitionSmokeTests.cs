[assembly: GenerateLogDefinitions(typeof(Smoke.Consumer.SmokeLogMessages))]

namespace Smoke.Consumer;

/// <summary>
/// Pins the 0.17.0 log-definition generator for an external consumer: it ships inside the LogAssertions package, reaches
/// a project that references only LogAssertions.TUnit, and its definitions match the records they describe. The Native
/// AOT gate compiles and runs this ahead of time.
/// </summary>
[Category("ConsumerSurface")]
[Timeout(10_000)]
internal sealed class GeneratedDefinitionSmokeTests
{
    /// <summary>The generated definition of <see cref="SmokeLogMessages.Ready"/> carries the attribute's identity and
    /// matches the record.</summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    [Test]
    public async Task GeneratedDefinitionMatchesItsRecordAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var logs = LogCollectorBuilder.Create();
        var logger = logs.CreateLogger("Worker");
        SmokeLogMessages.Ready(logger, "w1");

        await Assert.That(SmokeLogMessagesLogDefinitions.Ready.Id.Id).IsEqualTo(7);
        await Assert.That(SmokeLogMessagesLogDefinitions.Ready.Id.Name).IsEqualTo(nameof(SmokeLogMessages.Ready));
        await Assert.That(logs.Collector).HasLogged().Matching(SmokeLogMessagesLogDefinitions.Ready).Once();
    }
}
