// What the README samples use without declaring it themselves: the placeholders of a test (the collector, a logger, a
// token), a shared host, and stand-ins for the consumer's own types. Compiled by CI together with every C# sample of
// the READMEs (readme_samples.py of assertions-ci); never part of a package.
using System;
using System.Threading;
using System.Threading.Tasks;
using LogAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace ReadmeSamples;

/// <summary>The members every README sample can use.</summary>
public partial class Prelude
{
    /// <summary>The collector of the test.</summary>
    protected FakeLogCollector collector = new();

    /// <summary>A second collector.</summary>
    protected FakeLogCollector otherCollector = new();

    /// <summary>The capture of the test.</summary>
    protected LogCapture logs = LogCollectorBuilder.Create();

    /// <summary>A logger of the code under test.</summary>
    protected ILogger logger = new FakeLogger();

    /// <summary>The builder of a logging setup.</summary>
    protected ILoggingBuilder b = null!;

    /// <summary>A provider to add next to the tee.</summary>
    protected ILoggerProvider myProvider = new FakeLoggerProvider();

    /// <summary>The token of the test.</summary>
    protected CancellationToken cancellationToken;

    /// <summary>The fake clock of the test.</summary>
    protected FakeTimeProvider clock = new();

    /// <summary>How far the fake clock moves on each timer.</summary>
    protected TimeSpan step = TimeSpan.FromMilliseconds(100);

    /// <summary>An input the validator rejects.</summary>
    protected string invalidInput = "";

    /// <summary>A message ID of the scenario.</summary>
    protected string messageId = "m-1";

    /// <summary>Whether the scenario expects a retry.</summary>
    protected bool expectRetry;

    /// <summary>The result of the operation under test.</summary>
    protected OperationResult result = new(OperationStatus.Succeeded);

    /// <summary>A shared host, started in a hook.</summary>
    protected SharedHost host = null!;

    /// <summary>The definition of an event of the scenario.</summary>
    protected LogDefinition OrderProcessed = Define(1201, "OrderProcessed");

    /// <summary>The definition of an event of the scenario.</summary>
    protected LogDefinition OrderShipped = Define(1202, "OrderShipped");

    /// <summary>The definition of an event of the scenario.</summary>
    protected LogDefinition PaymentFailed = Define(1300, "PaymentFailed");

    /// <summary>The definition of an event of the scenario.</summary>
    protected LogDefinition QueueFull = Define(1400, "QueueFull");

    /// <summary>The definition of an event of the scenario.</summary>
    protected LogDefinition UpstreamContractViolated = Define(1500, "UpstreamContractViolated");

    /// <summary>The definition of an event of the scenario.</summary>
    protected LogDefinition StaleSessionDropped = Define(1501, "StaleSessionDropped");

    /// <summary>The code under test.</summary>
    protected static void DoWork()
    {
    }

    /// <summary>Builds the host of the scenario, with the tee.</summary>
    /// <returns>The host.</returns>
    protected static SharedHost BuildHost() => new();

    private static LogDefinition Define(int id, string name)
        => LogDefinition.Capture(log => log.Log(LogLevel.Information, new EventId(id, name), name, null, static (state, _) => state));
}

/// <summary>A host that starts asynchronously.</summary>
public sealed class SharedHost
{
    /// <summary>Starts the host.</summary>
    /// <param name="cancellationToken">The token of the start.</param>
    /// <returns>A task that completes when the host runs.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>How an operation ended.</summary>
public enum OperationStatus
{
    /// <summary>It worked.</summary>
    Succeeded,
}

/// <summary>The result of an operation.</summary>
/// <param name="Status">How it ended.</param>
public sealed record OperationResult(OperationStatus Status);

/// <summary>A validator of the code under test.</summary>
/// <param name="logger">Its logger.</param>
public sealed class MyValidator(ILogger logger)
{
    /// <summary>Validates an input.</summary>
    /// <param name="input">The input.</param>
    public void Validate(string input) => logger.Log(LogLevel.Warning, default, input, null, static (state, _) => state);
}

/// <summary>A service of the code under test.</summary>
/// <param name="logger">Its logger.</param>
public sealed class MyService(ILogger logger)
{
    /// <summary>Does the work.</summary>
    public void DoWork() => logger.Log(LogLevel.Information, default, "work", null, static (state, _) => state);
}

/// <summary>The log messages of the code under test.</summary>
public static class LogMessages
{
    /// <summary>Logs a shipment.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="orderId">The order.</param>
    /// <param name="city">The destination.</param>
    public static void OrderShipped(ILogger logger, int orderId, string city)
        => logger.Log(LogLevel.Information, new EventId(1202, "OrderShipped"), orderId + city, null, static (state, _) => state);

    /// <summary>Logs a failed payment.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="orderId">The order.</param>
    public static void PaymentFailed(ILogger logger, int orderId)
        => logger.Log(LogLevel.Warning, new EventId(1300, "PaymentFailed"), orderId, null, static (state, _) => state.ToString());

    /// <summary>Logs a started order.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="orderId">The order.</param>
    public static void OrderStarted(ILogger logger, int orderId)
        => logger.Log(LogLevel.Information, new EventId(1200, "OrderStarted"), orderId, null, static (state, _) => state.ToString());
}

/// <summary>An exception of a transport the code under test uses.</summary>
public sealed class RpcException : Exception
{
}

/// <summary>An exception of a database the code under test uses.</summary>
public sealed class DbUpdateConcurrencyException : Exception
{
}
