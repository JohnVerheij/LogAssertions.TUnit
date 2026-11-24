using System;
using Microsoft.Extensions.Logging;

#if EXTENSIONS_FIXTURE
namespace LogAssertions.Generator.Fixture.Extensions;
#else
namespace LogAssertions.Generator.Fixture;
#endif

/// <summary>A struct that implements <c>ILogger</c>: it converts to the interface by boxing, so it is no logger.</summary>
internal readonly struct StructLogger : ILogger
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => false;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }
}
