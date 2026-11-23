using System;

namespace LogAssertions.Generator;

/// <summary>
/// The event ID Microsoft's <c>[LoggerMessage]</c> generators derive when the attribute sets no <c>EventId</c>: the
/// 32-bit FNV-1a hash of the UTF-16 code units of a text (see <see cref="ProductLogging.EventId"/> for which text), as a
/// non-negative <see cref="int"/>. A hash of <see cref="int.MinValue"/> gives 0, as in the generator of
/// Microsoft.Extensions.Telemetry; the generator of Microsoft.Extensions.Logging.Abstractions fails on such a name, so no
/// product of it has one. It is those generators' internal code, not a public API; the exact-reference tests compare it
/// with the IDs real <c>[LoggerMessage]</c> methods log on every target framework.
/// </summary>
internal static class EventIdHash
{
    /// <summary>The FNV-1a 32-bit offset basis, 2166136261, as the <see cref="int"/> with the same bits. Signed
    /// arithmetic gives the same bits as unsigned arithmetic for XOR and for multiplication modulo 2^32.</summary>
    private const int OffsetBasis = -2128831035;

    /// <summary>The FNV-1a 32-bit prime.</summary>
    private const int Prime = 16777619;

    /// <summary>Computes the event ID Microsoft's generators derive from <paramref name="eventName"/>.</summary>
    /// <param name="eventName">The hashed text, typically the event name.</param>
    /// <returns>The event ID.</returns>
    internal static int Of(string eventName)
    {
        var hash = OffsetBasis;
        foreach (var c in eventName)
        {
            hash = unchecked((c ^ hash) * Prime);
        }

        return hash == int.MinValue ? 0 : Math.Abs(hash);
    }
}
