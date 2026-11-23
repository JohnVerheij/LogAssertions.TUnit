using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LogAssertions.Generator;

/// <summary>Assigns the roles of a method's parameters in parameter order: the first logger, the first level (when the
/// level is not set otherwise), and the first exception; every other parameter is ordinary.</summary>
/// <param name="levelTaken">Whether no parameter can be the level, because the attribute sets it and the generator of
/// Microsoft.Extensions.Logging.Abstractions then logs a level parameter as a value.</param>
internal sealed class ParameterRoles(bool levelTaken)
{
    /// <summary>The namespace of <c>ILogger</c> and <c>LogLevel</c>.</summary>
    private const string LoggingNamespace = "Microsoft.Extensions.Logging";

    /// <summary>Whether the logger role is taken.</summary>
    private bool _logger;

    /// <summary>Whether the level role is taken.</summary>
    private bool _level = levelTaken;

    /// <summary>Whether the exception role is taken.</summary>
    private bool _exception;

    /// <summary>Returns the role of the next parameter and takes it.</summary>
    /// <param name="type">The parameter's type.</param>
    /// <returns>The role.</returns>
    internal ParameterRole Next(ITypeSymbol type)
    {
        if (!_logger && IsLogger(type))
        {
            _logger = true;
            return ParameterRole.Logger;
        }

        if (!_level && IsLoggingType(type, "LogLevel"))
        {
            _level = true;
            return ParameterRole.Level;
        }

        if (!_exception && IsException(type))
        {
            _exception = true;
            return ParameterRole.Exception;
        }

        return ParameterRole.Ordinary;
    }

    /// <summary>Returns whether a type is <c>ILogger</c> or implements it.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> for a logger.</returns>
    private static bool IsLogger(ITypeSymbol type)
        => IsLoggingType(type, "ILogger") || type.AllInterfaces.Any(static i => IsLoggingType(i, "ILogger"));

    /// <summary>Returns whether a type is <c>System.Exception</c> or derives from it.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> for an exception.</returns>
    private static bool IsException(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(FullName(current), "global::System.Exception", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Returns whether a type is the named type of the logging namespace.</summary>
    /// <param name="type">The type.</param>
    /// <param name="name">The type name.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool IsLoggingType(ITypeSymbol type, string name)
        => string.Equals(FullName(type), "global::" + LoggingNamespace + "." + name, StringComparison.Ordinal);

    /// <summary>Returns the fully qualified name of a type, without a nullable annotation.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name, with the <c>global::</c> prefix.</returns>
    private static string FullName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
