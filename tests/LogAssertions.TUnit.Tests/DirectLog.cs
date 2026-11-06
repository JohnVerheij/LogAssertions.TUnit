using Microsoft.Extensions.Logging;

namespace LogAssertions.TUnit.Tests;

/// <summary>Writes records directly through <see cref="ILogger.Log{TState}"/>, with a static formatter.</summary>
internal static class DirectLog
{
    /// <summary>Writes one record with <paramref name="message"/>.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="message">The message.</param>
    /// <param name="level">The level. Default is <see cref="LogLevel.Information"/>.</param>
    public static void Write(ILogger logger, string message, LogLevel level = LogLevel.Information)
        => logger.Log(level, default, message, null, static (state, _) => state);
}
