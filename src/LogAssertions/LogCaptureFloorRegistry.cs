using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LogAssertions;

/// <summary>
/// Internal side-channel recording the minimum capture level (the <see cref="ILoggerFactory"/>
/// floor) that a <see cref="FakeLogCollector"/> was created with by
/// <see cref="LogCollectorBuilder"/>. An assertion consults it to detect a <em>vacuous</em> check:
/// asserting the absence of records at a level the floor filtered out, which would otherwise pass
/// silently because nothing at that level was ever captured.
/// </summary>
/// <remarks>
/// Keyed weakly (<see cref="ConditionalWeakTable{TKey, TValue}"/>) so registering a floor does not
/// keep a collector alive. The value is a boxed <see cref="LogLevel"/> (the table requires a
/// reference-typed value). This is an implementation detail shared with the adapter assembly via
/// <c>InternalsVisibleTo</c>; it is not public API.
/// </remarks>
internal static class LogCaptureFloorRegistry
{
    private static readonly ConditionalWeakTable<FakeLogCollector, object> Floors = new();

    /// <summary>Records the capture floor for <paramref name="collector"/>.</summary>
    /// <param name="collector">The collector to associate the floor with.</param>
    /// <param name="minimumLevel">The minimum level the owning factory captures.</param>
    public static void Register(FakeLogCollector collector, LogLevel minimumLevel)
        => Floors.AddOrUpdate(collector, minimumLevel);

    /// <summary>Reads the capture floor previously registered for <paramref name="collector"/>.</summary>
    /// <param name="collector">The collector to look up.</param>
    /// <param name="minimumLevel">The registered floor when this returns <see langword="true"/>;
    /// otherwise <see cref="LogLevel.Trace"/>.</param>
    /// <returns><see langword="true"/> when a floor was registered for the collector.</returns>
    public static bool TryGetFloor(FakeLogCollector collector, out LogLevel minimumLevel)
    {
        if (Floors.TryGetValue(collector, out var boxed) && boxed is LogLevel level)
        {
            minimumLevel = level;
            return true;
        }

        minimumLevel = LogLevel.Trace;
        return false;
    }
}
