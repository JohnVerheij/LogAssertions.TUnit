using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LogAssertions.Generator;

/// <summary>Assigns the roles of a method's parameters in parameter order: the first logger, the first level (when the
/// level is not set otherwise), and the first exception; every other parameter is ordinary. A parameter is a logger or
/// an exception when its type converts to <c>ILogger</c> or <c>System.Exception</c> by identity or by an implicit
/// reference conversion, the rule both Microsoft generators apply: a struct that implements <c>ILogger</c> is no logger,
/// and a type parameter is one when it is known to be a reference type and a constraint leads to the type.</summary>
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

    /// <summary>Returns whether a type converts to <c>ILogger</c> by identity or by an implicit reference conversion.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> for a logger.</returns>
    private static bool IsLogger(ITypeSymbol type)
        => ConvertsByReference(type, static t => IsLoggingType(t, "ILogger"));

    /// <summary>Returns whether a type converts to <c>System.Exception</c> by identity or by an implicit reference
    /// conversion.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> for an exception.</returns>
    private static bool IsException(ITypeSymbol type)
        => ConvertsByReference(type, static t => string.Equals(FullName(t), "global::System.Exception", StringComparison.Ordinal));

    /// <summary>Returns whether a type converts to a target type by identity, or by an implicit reference conversion: the
    /// type is known to be a reference type, and the target is among its base types and interfaces (for a type
    /// parameter, those its constraints lead to).</summary>
    /// <param name="type">The type.</param>
    /// <param name="isTarget">Whether a type is the target type.</param>
    /// <returns><see langword="true"/> when the type converts.</returns>
    private static bool ConvertsByReference(ITypeSymbol type, Func<ITypeSymbol, bool> isTarget)
        => isTarget(type) || (type.IsReferenceType && LeadsTo(type, isTarget));

    /// <summary>Returns whether the target type is a base type or an interface of a type, or, for a type parameter, of
    /// one of its constraints.</summary>
    /// <param name="type">The type.</param>
    /// <param name="isTarget">Whether a type is the target type.</param>
    /// <returns><see langword="true"/> when the type leads to the target.</returns>
    private static bool LeadsTo(ITypeSymbol type, Func<ITypeSymbol, bool> isTarget)
    {
        if (isTarget(type))
            return true;

        if (type is ITypeParameterSymbol parameter)
            return parameter.ConstraintTypes.Any(constraint => LeadsTo(constraint, isTarget));

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (isTarget(current))
                return true;
        }

        return type.AllInterfaces.Any(i => isTarget(i));
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
