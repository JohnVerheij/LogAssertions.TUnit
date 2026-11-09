using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace LogAssertions.TUnit.Tests;

/// <summary>
/// TUnit (1.81) runs a test's <c>[Before(Class)]</c> hooks, its class constructor and the initialization of its shared
/// fixtures with the test already current, but before the test-start notification reaches
/// <see cref="LogAssertionsTestLifetimeAttribute"/>; it runs <c>[After(Test)]</c> hooks before the end notification, and
/// disposes the test class after it. This assembly applies the attribute, so nothing at those points may claim it does
/// not: no "attribute missing" note from a tee built there, and no "needs the attribute" message from a dump armed
/// there. The class also pins where records logged at those points go. The tests run in order, one at a time.
/// </summary>
[NotInParallel(nameof(TestLifetimeOrderTests))]
[Timeout(15_000)]
internal sealed class TestLifetimeOrderTests : IAsyncDisposable
{
    /// <summary>What the message for a project without the attribute says.</summary>
    private const string ClaimsTheAttributeIsMissing = "needs the LogAssertionsTestLifetime attribute";

    /// <summary>The message of the dump armed in the constructor, or <see langword="null"/> when it armed.</summary>
    private readonly string? _constructorArm = ArmAndDescribe();

    /// <summary>The tee built in the constructor, disposed with the instance.</summary>
    private readonly LogCapture _constructorTee = TestOutputLogCollectorBuilder.CreateTeed();

    /// <summary>Logs one record through the tee built in the constructor, before the test has started.</summary>
    public TestLifetimeOrderTests() => DirectLog.Write(_constructorTee.CreateLogger("Constructor"), "record logged in the constructor");

    /// <summary>Gets the fixture TUnit injects and initializes for each test, on that test's flow.</summary>
    [MethodDataSource(nameof(NewFixture))]
    public required TeedFixture Fixture { get; init; }

    /// <summary>Gets or sets the message of the dump armed in <see cref="BuildInBeforeClass"/>, or
    /// <see langword="null"/> when it armed.</summary>
    private static string? BeforeClassArm { get; set; }

    /// <summary>Gets or sets the tee built in <see cref="BuildInBeforeClass"/>.</summary>
    private static LogCapture? BeforeClassTee { get; set; }

    /// <summary>Gets or sets the first test of the class, where TUnit ran the class hook and the fixture.</summary>
    private static TestContext? FirstTest { get; set; }

    /// <summary>Arms a dump and builds a tee in a <c>[Before(Class)]</c> hook, as the README's shared-capture example
    /// does with a teed capture.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    [Before(Class)]
    public static void BuildInBeforeClass(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeClassArm = ArmAndDescribe();
        BeforeClassTee = TestOutputLogCollectorBuilder.CreateTeed();
    }

    /// <summary>Disposes the tee built in <see cref="BuildInBeforeClass"/>.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    /// <returns>A task that completes when the tee is disposed.</returns>
    [After(Class)]
    public static ValueTask DisposeBeforeClassTee(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return BeforeClassTee?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>Creates the fixture TUnit injects into <see cref="Fixture"/>.</summary>
    /// <returns>A factory of a new fixture per test.</returns>
    public static Func<TeedFixture> NewFixture() => static () => new TeedFixture();

    /// <summary>Logs one record through the tee built in the constructor, after the test body.</summary>
    /// <param name="cancellationToken">The hook's token.</param>
    [After(Test)]
    public void LogInAfterTest(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectLog.Write(_constructorTee.CreateLogger("AfterTest"), "record logged in the [After(Test)] hook");
    }

    /// <summary>A dump armed in a <c>[Before(Class)]</c> hook cannot belong to a test, so it is refused, with the
    /// message that the call came before TUnit started the test, not the one for a project without the attribute.</summary>
    [Test]
    public async Task ArmInBeforeClass_DoesNotSayTheAttributeIsMissing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FirstTest = TestContext.Current;

        await Assert.That(BeforeClassArm ?? string.Empty).DoesNotContain(ClaimsTheAttributeIsMissing, StringComparison.Ordinal);
        await Assert.That(BeforeClassArm).IsEqualTo(TestLifetimeTable.NotStartedMessage);
    }

    /// <summary>A dump armed in the test class's constructor is refused with the message that the call came before
    /// TUnit started the test (pinned here word for word), not the one for a project without the attribute.</summary>
    [Test]
    [DependsOn(nameof(ArmInBeforeClass_DoesNotSayTheAttributeIsMissing), ProceedOnFailure = true)]
    public async Task ArmInTheConstructor_DoesNotSayTheAttributeIsMissing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(_constructorArm ?? string.Empty).DoesNotContain(ClaimsTheAttributeIsMissing, StringComparison.Ordinal);
        await Assert.That(_constructorArm).IsEqualTo(
            "DumpToTestOutputOnFailure() was called before TUnit started the current test: in a [Before(Class)] hook, the "
            + "test class's constructor, or while a fixture or data source was initialized. "
            + "Call it in the test or in a [Before(Test)] hook.");
    }

    /// <summary>A dump armed while TUnit initializes a fixture is refused with the message that the call came before
    /// TUnit started the test, not the one for a project without the attribute.</summary>
    [Test]
    [DependsOn(nameof(ArmInTheConstructor_DoesNotSayTheAttributeIsMissing), ProceedOnFailure = true)]
    public async Task ArmInAFixtureInitializer_DoesNotSayTheAttributeIsMissing(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(Fixture.Arm ?? string.Empty).DoesNotContain(ClaimsTheAttributeIsMissing, StringComparison.Ordinal);
        await Assert.That(Fixture.Arm).IsEqualTo(TestLifetimeTable.NotStartedMessage);
    }

    /// <summary>Tees built in the class hook, the constructors and the fixture write no "attribute missing" note:
    /// the note is written once per run, to the test current where the first such tee is built, which here is the
    /// first test of the class (no other test of this assembly builds a tee before its test has started).</summary>
    [Test]
    [DependsOn(nameof(ArmInAFixtureInitializer_DoesNotSayTheAttributeIsMissing), ProceedOnFailure = true)]
    public async Task TeesBuiltBeforeTheTestStarted_WriteNoMissingAttributeNote(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Assert.That(FirstTest!.GetStandardOutput()).DoesNotContain(TestLifetimeTable.MissingAttributeNote, StringComparison.Ordinal);
        await Assert.That(TestContext.Current!.GetStandardOutput()).DoesNotContain(TestLifetimeTable.MissingAttributeNote, StringComparison.Ordinal);
    }

    /// <summary>Where the first test's records went: the one logged in its constructor (before the test started) and
    /// the one logged in its <c>[After(Test)]</c> hook are in its report; the one logged while TUnit disposed its
    /// instance (after the end notification) is not teed, and the tee's count line, written when the instance
    /// disposed it, says one record was not teed.</summary>
    [Test]
    [DependsOn(nameof(TeesBuiltBeforeTheTestStarted_WriteNoMissingAttributeNote), ProceedOnFailure = true)]
    public async Task RecordsAroundTheFirstTest_GoWhereTheDocsSay(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = FirstTest!.GetStandardOutput();

        await Assert.That(output).Contains("Constructor: record logged in the constructor", StringComparison.Ordinal);
        await Assert.That(output).Contains("AfterTest: record logged in the [After(Test)] hook", StringComparison.Ordinal);
        await Assert.That(output).DoesNotContain("record logged while TUnit disposes the test class", StringComparison.Ordinal);
        await Assert.That(output).Contains("1 log record(s) were not teed to a test report", StringComparison.Ordinal);
    }

    /// <summary>Logs one record through the tee built in the constructor while TUnit disposes the instance, then
    /// disposes the tee.</summary>
    /// <returns>A task that completes when the tee is disposed.</returns>
    public ValueTask DisposeAsync()
    {
        DirectLog.Write(_constructorTee.CreateLogger("Disposal"), "record logged while TUnit disposes the test class");
        return _constructorTee.DisposeAsync();
    }

    /// <summary>Arms a dump of a new collector for the current test.</summary>
    /// <returns>The message of the refusal, or <see langword="null"/> when the dump was armed.</returns>
    private static string? ArmAndDescribe()
    {
        try
        {
            new FakeLogCollector().DumpToTestOutputOnFailure();
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>A fixture that arms a dump and builds a tee when TUnit initializes it, as a host fixture with a teed
    /// logger would.</summary>
    internal sealed class TeedFixture : IAsyncInitializer, IAsyncDisposable
    {
        /// <summary>Gets the message of the dump armed at initialization, or <see langword="null"/> when it armed.</summary>
        public string? Arm { get; private set; }

        /// <summary>Gets the tee built at initialization.</summary>
        public LogCapture? Tee { get; private set; }

        /// <inheritdoc/>
        public Task InitializeAsync()
        {
            Arm = ArmAndDescribe();
            Tee = TestOutputLogCollectorBuilder.CreateTeed();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => Tee?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
