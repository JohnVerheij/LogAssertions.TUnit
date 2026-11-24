using System;
using Microsoft.Extensions.Logging;

#if EXTENSIONS_FIXTURE
namespace LogAssertions.Generator.Fixture.Extensions;
#else
namespace LogAssertions.Generator.Fixture;
#endif

/// <summary>Parameters whose role depends on how their type converts: both Microsoft generators make a parameter the
/// logger or the exception only for an identity or implicit reference conversion.</summary>
/// <typeparam name="TException">A type parameter constrained to an exception: the exception.</typeparam>
/// <typeparam name="TLogger">A type parameter constrained to a reference type that implements <c>ILogger</c>: the
/// logger.</typeparam>
/// <typeparam name="TValue">A type parameter constrained to <c>ILogger</c> alone, which may be a struct: a
/// value.</typeparam>
internal static partial class ConstrainedLogMessages<TException, TLogger, TValue>
    where TException : Exception
    where TLogger : class, ILogger
    where TValue : ILogger
{
    /// <summary>The exception through a type parameter.</summary>
    [LoggerMessage(EventId = 1401, Level = LogLevel.Error, Message = "Constrained exception {X}")]
    internal static partial void ExceptionParameter(ILogger logger, TException exception, int x);

    /// <summary>The logger through a type parameter.</summary>
    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "Constrained logger {X}")]
    internal static partial void LoggerParameter(TLogger logger, int x);

    // SYSLIB1015 (a parameter the message does not use) is the shape under test: a parameter named in the template is a
    // value whatever its type, so the role shows only for one the template does not name.
#pragma warning disable SYSLIB1015

    /// <summary>A type parameter that may be a struct, before the logger: a value; the logger is the next
    /// parameter.</summary>
    [LoggerMessage(EventId = 1403, Level = LogLevel.Information, Message = "Unconstrained value {X}")]
    internal static partial void ValueParameter(TValue value, ILogger logger, int x);
#pragma warning restore SYSLIB1015
}
