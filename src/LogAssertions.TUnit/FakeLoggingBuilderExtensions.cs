using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TUnit.Core;

namespace LogAssertions.TUnit;

/// <summary>
/// <see cref="ILoggingBuilder"/> extensions that wire a <see cref="FakeLogCollector"/> into a logging
/// builder you already own - a host's <c>ConfigureLogging</c>, a
/// <see cref="LoggerFactory.Create(System.Action{ILoggingBuilder})"/> callback, or any other - so the
/// capture composes alongside your own level, filters, and providers. Use these when the self-contained
/// <see cref="TestOutputLogCollectorBuilder.CreateTeed(LogLevel)"/> capture does not fit because the capture
/// must live inside an existing builder, for example an ASP.NET Core test host.
/// </summary>
public static class FakeLoggingBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="FakeLoggerProvider"/> backed by <paramref name="collector"/> for assertions and
    /// sets the builder's minimum level to <paramref name="minimumLevel"/>, registering that level as the
    /// collector's capture floor so the vacuous-absence guard applies. No record is teed to
    /// the test output; use <see cref="AddTeedFakeLogging"/> for that.
    /// </summary>
    /// <remarks>
    /// The helper owns the builder's minimum level so the registered capture floor is accurate: a floor
    /// below the level the builder actually filters at would let the vacuity guard miss a genuinely vacuous
    /// absence and pass when it should fail. Pass a higher <paramref name="minimumLevel"/> to capture less.
    /// <para>
    /// Clock: each record's <see cref="FakeLogRecord.Timestamp"/> comes from <paramref name="collector"/>'s own clock
    /// (<c>FakeLogCollectorOptions.TimeProvider</c>; for a fake clock, create the collector with
    /// <c>FakeLogCollector.Create(new FakeLogCollectorOptions { TimeProvider = clock })</c>).
    /// </para>
    /// </remarks>
    /// <param name="builder">The logging builder to add the capture provider to.</param>
    /// <param name="collector">The collector that receives records for assertions.</param>
    /// <param name="minimumLevel">The minimum level to capture, also registered as the capture floor.
    /// Default is <see cref="LogLevel.Trace"/> (everything).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="collector"/> is null.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The provider is registered into the ILoggingBuilder and becomes part of the built logger pipeline; its lifetime is the caller's, not this method's. FakeLoggerProvider holds no unmanaged resources.")]
    public static ILoggingBuilder AddFakeLogging(this ILoggingBuilder builder, FakeLogCollector collector, LogLevel minimumLevel = LogLevel.Trace)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(collector);

        builder.SetMinimumLevel(minimumLevel);
        builder.AddProvider(new FakeLoggerProvider(collector));
        LogCaptureFloorRegistry.Register(collector, minimumLevel);
        return builder;
    }

    /// <summary>
    /// Adds a <see cref="FakeLoggerProvider"/> backed by <paramref name="collector"/> for assertions, plus the built-in
    /// tee that mirrors each record into the test report. Sets the builder's minimum level and registers the capture
    /// floor exactly as <see cref="AddFakeLogging"/> does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tee picks the test for each record when it is logged: the test current at the log call (the test's own
    /// flow, including the thread pool work it starts); otherwise the test that was current when this method ran,
    /// while that test runs; otherwise no test. A record that reaches no test is counted, and the count is written
    /// to the test current when the logging factory is disposed.
    /// </para>
    /// <para>
    /// With <see cref="LogAssertionsTestLifetimeAttribute"/> applied to the test project, a test counts as running
    /// until its <c>[After(Test)]</c> hooks have finished, and a record logged on the flow of a test that has ended
    /// (for example by a background loop that test started) is not teed. Without the attribute, the fallback ends when
    /// the owning test has a result, and a record on a test's own flow always goes to that test.
    /// </para>
    /// <para>
    /// The same call works per test and for a host shared across tests. A background loop keeps the context of the
    /// test it was started in, so start a shared host outside any test, inside
    /// <see cref="System.Threading.ExecutionContext.SuppressFlow"/>; records produced for a test (its requests, its
    /// calls) still reach that test. Start the host inside the block without awaiting it, and await the start after
    /// the block: the block must end on the thread that opened it, so never <see langword="await"/> inside it
    /// (<c>Task started; using (ExecutionContext.SuppressFlow()) { host = Build(); started = host.StartAsync(token); }
    /// await started;</c>). TUnit runs hooks such as <c>[Before(Class)]</c> and the initialization of shared
    /// fixtures on the flow of the first test that needs them, so a host started there without suppressing the flow
    /// counts as started inside that test.
    /// </para>
    /// <para>
    /// To add another provider, chain it on the builder: <c>.AddTeedFakeLogging(collector).AddProvider(provider)</c>,
    /// or, for TUnit's own correlated output, <c>.AddFakeLogging(collector, level).AddCorrelatedTUnit(level)</c>
    /// from the <c>TUnit.Logging.Microsoft</c> package.
    /// </para>
    /// <para>
    /// Clock: each record's <see cref="FakeLogRecord.Timestamp"/> comes from <paramref name="collector"/>'s own clock,
    /// as with <see cref="AddFakeLogging"/>. The tee writes no times.
    /// </para>
    /// </remarks>
    /// <param name="builder">The logging builder to add the providers to.</param>
    /// <param name="collector">The collector that receives records for assertions.</param>
    /// <param name="minimumLevel">The minimum level to capture and tee, also registered as the capture floor.
    /// Default is <see cref="LogLevel.Trace"/> (everything).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="collector"/> is null.</exception>
    public static ILoggingBuilder AddTeedFakeLogging(this ILoggingBuilder builder, FakeLogCollector collector, LogLevel minimumLevel = LogLevel.Trace)
    {
        AddFakeLogging(builder, collector, minimumLevel);
        AddTestOutputTee(builder);
        return builder;
    }

    /// <summary>
    /// Registers the built-in tee, owned by the test current now. It is registered through a factory delegate, so the
    /// logging container disposes it with the factory and the tee can report the records that reached no test. In a
    /// test the lifetime attribute did not start, the first tee of the run writes a note naming the line to add.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    internal static void AddTestOutputTee(ILoggingBuilder builder)
    {
        var owner = TestContext.Current;
        TestLifetimeTable.Shared.NoteIfUntracked(owner);
        builder.Services.AddSingleton<ILoggerProvider>(_ => new TestOutputLoggerProvider(owner));
    }
}
