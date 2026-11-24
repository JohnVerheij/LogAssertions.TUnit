using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace LogAssertions.Generator;

/// <summary>
/// Which of Microsoft's <c>[LoggerMessage]</c> generators compiled a product assembly, and how it writes the records.
/// The generator of Microsoft.Extensions.Telemetry (Microsoft.Gen.Logging) replaces the one of
/// Microsoft.Extensions.Logging.Abstractions wherever it is referenced, and the code it writes references
/// Microsoft.Extensions.Telemetry.Abstractions; so does the compiled product. The version of the product's reference to
/// either package is the version of its generator (the assembly versions carry major and minor).
/// </summary>
internal sealed class ProductLogging
{
    /// <summary>Initializes the traits.</summary>
    /// <param name="telemetry">The version of Microsoft.Extensions.Telemetry.Abstractions the product references, or
    /// <see langword="null"/> when the generator of Microsoft.Extensions.Logging.Abstractions compiled it.</param>
    /// <param name="abstractionsMajor">The major version of Microsoft.Extensions.Logging.Abstractions the product
    /// references.</param>
    private ProductLogging(Version? telemetry, int abstractionsMajor)
    {
        TelemetryVersion = telemetry;
        AbstractionsMajor = abstractionsMajor;
    }

    /// <summary>Whether the generator of Microsoft.Extensions.Telemetry compiled the product: it stores the values in
    /// reverse parameter order, trims the spaces around a placeholder's name, logs a level parameter only when the
    /// template names it, never logs the exception or a data-classified parameter as a value. The generator of Microsoft.Extensions.Logging.Abstractions stores them in parameter order, logs a level
    /// parameter when the attribute sets the level or the template names it, and the exception when the template names
    /// it.</summary>
    internal bool Telemetry => TelemetryVersion is not null;

    /// <summary>Whether the record's template is the message as written. The generator of
    /// Microsoft.Extensions.Logging.Abstractions before 11.0 copies the message into a C# literal without escaping a
    /// backslash, so a backslash escape in it takes effect (see <see cref="LoggedTemplate"/>).</summary>
    internal bool LogsMessageAsWritten => Telemetry || AbstractionsMajor >= 11;

    /// <summary>Whether the product's logging generator, in a state type of its own, names a parameter written with a
    /// leading <c>@</c> (<c>@event</c>) by that code name: the generator of Microsoft.Extensions.Logging.Abstractions 8.0
    /// and later.</summary>
    internal bool KeepsVerbatimAt => !Telemetry && AbstractionsMajor >= 8;

    /// <summary>The version of Microsoft.Extensions.Telemetry.Abstractions the product references, or
    /// <see langword="null"/>.</summary>
    private Version? TelemetryVersion { get; }

    /// <summary>The major version of Microsoft.Extensions.Logging.Abstractions the product references.</summary>
    private int AbstractionsMajor { get; }

    /// <summary>Reads the traits of a product assembly from the assemblies it references.</summary>
    /// <param name="assembly">The product assembly.</param>
    /// <returns>The traits.</returns>
    internal static ProductLogging Of(IAssemblySymbol assembly)
    {
        var references = assembly.Modules.SelectMany(static m => m.ReferencedAssemblies).ToArray();
        var telemetry = references.FirstOrDefault(static r => string.Equals(r.Name, "Microsoft.Extensions.Telemetry.Abstractions", StringComparison.Ordinal));
        var abstractions = references.FirstOrDefault(static r => string.Equals(r.Name, "Microsoft.Extensions.Logging.Abstractions", StringComparison.Ordinal));
        return new ProductLogging(telemetry?.Version, abstractions?.Version.Major ?? int.MaxValue);
    }

    /// <summary>
    /// Returns the event ID the product's generator writes for a method whose attribute sets no <c>EventId</c>.
    /// Microsoft.Extensions.Logging.Abstractions from 8.0, and Microsoft.Extensions.Telemetry from 9.8, hash the event
    /// name; Telemetry 8.2 to 9.7 hash the C# text it writes for the name (<c>nameof(Method)</c>, or the event name in
    /// quotes); Telemetry before 8.2 writes 0, and Abstractions before 8.0 writes -1.
    /// </summary>
    /// <param name="eventName">The event name: the attribute's, or the method name.</param>
    /// <param name="eventNameSet">Whether the attribute sets the event name.</param>
    /// <param name="methodName">The method name.</param>
    /// <returns>The event ID.</returns>
    internal int EventId(string eventName, bool eventNameSet, string methodName)
    {
        if (TelemetryVersion is { } telemetry)
        {
            if (telemetry < new Version(8, 2))
                return 0;

            if (telemetry < new Version(9, 8))
                return EventIdHash.Of(eventNameSet ? "\"" + eventName + "\"" : "nameof(" + methodName + ")");

            return EventIdHash.Of(eventName);
        }

        return AbstractionsMajor >= 8 ? EventIdHash.Of(eventName) : -1;
    }
}
