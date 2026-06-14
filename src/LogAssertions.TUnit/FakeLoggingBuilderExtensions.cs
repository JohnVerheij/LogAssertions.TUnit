using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions.TUnit;

/// <summary>
/// <see cref="ILoggingBuilder"/> extensions that wire a <see cref="FakeLogCollector"/> into a logging
/// builder you already own - a host's <c>ConfigureLogging</c>, a
/// <see cref="LoggerFactory.Create(System.Action{ILoggingBuilder})"/> callback, or any other - so the
/// capture composes alongside your own level, filters, and providers. Use these when the self-contained
/// <see cref="TestOutputLogCollectorBuilder.CreateTeed(LogLevel)"/> tuple does not fit because the capture
/// must live inside an existing builder, for example an ASP.NET Core test host.
/// </summary>
public static class FakeLoggingBuilderExtensions
{
    /// <summary>
    /// Adds a <see cref="FakeLoggerProvider"/> backed by <paramref name="collector"/> for assertions and
    /// sets the builder's minimum level to <paramref name="minimumLevel"/>, registering that level as the
    /// collector's capture floor so the vacuous-<c>HasNotLogged()</c> guard applies. No record is teed to
    /// the test output; use <see cref="AddTeedFakeLogging"/> for that.
    /// </summary>
    /// <remarks>
    /// The helper owns the builder's minimum level so the registered capture floor is accurate: a floor
    /// below the level the builder actually filters at would let the vacuity guard miss a genuinely vacuous
    /// absence and pass when it should fail. Pass a higher <paramref name="minimumLevel"/> to capture less.
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
    /// Adds a <see cref="FakeLoggerProvider"/> backed by <paramref name="collector"/> for assertions, plus a
    /// tee provider that mirrors each record into the test report - <paramref name="teeProvider"/> when
    /// supplied, otherwise the built-in <see cref="TestOutputLoggerProvider"/>. Supplying your own provider
    /// lets you plug in correlation that survives background threads (for example TUnit's
    /// <c>CorrelatedTUnitLoggerProvider</c>) without this package taking a dependency on it. Sets the
    /// builder's minimum level and registers the capture floor exactly as <see cref="AddFakeLogging"/> does.
    /// </summary>
    /// <remarks>
    /// A supplied <paramref name="teeProvider"/> is registered into the builder and becomes part of the
    /// built logger pipeline. The logging factory does not dispose externally-supplied provider instances,
    /// so dispose it yourself if it holds resources (the usual display/correlation providers do not). The
    /// built-in tee writes to <c>TestContext.Current.Output</c>, so a record logged on a background thread
    /// (where the AsyncLocal context does not flow) is captured but not teed - pass a correlation-aware
    /// provider when that matters.
    /// </remarks>
    /// <param name="builder">The logging builder to add the providers to.</param>
    /// <param name="collector">The collector that receives records for assertions.</param>
    /// <param name="minimumLevel">The minimum level to capture and tee, also registered as the capture floor.
    /// Default is <see cref="LogLevel.Trace"/> (everything).</param>
    /// <param name="teeProvider">The provider that mirrors records into the test report, or null to use the
    /// built-in <see cref="TestOutputLoggerProvider"/>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="collector"/> is null.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Both providers are registered into the ILoggingBuilder and become part of the built logger pipeline; their lifetime is the caller's, not this method's.")]
    public static ILoggingBuilder AddTeedFakeLogging(this ILoggingBuilder builder, FakeLogCollector collector, LogLevel minimumLevel = LogLevel.Trace, ILoggerProvider? teeProvider = null)
    {
        AddFakeLogging(builder, collector, minimumLevel);
        builder.AddProvider(teeProvider ?? new TestOutputLoggerProvider());
        return builder;
    }
}
