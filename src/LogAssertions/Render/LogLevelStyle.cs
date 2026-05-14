using Microsoft.Extensions.Logging;

namespace LogAssertions.Render;

/// <summary>How <see cref="LogSnapshotRenderer"/> renders a record's <see cref="LogLevel"/>.</summary>
public enum LogLevelStyle
{
    /// <summary>
    /// Four-character lowercase abbreviation matching the conventional
    /// <c>Microsoft.Extensions.Logging</c> console formatter: <c>trce</c>, <c>dbug</c>,
    /// <c>info</c>, <c>warn</c>, <c>fail</c>, <c>crit</c>, <c>none</c>. Fixed width keeps
    /// header columns aligned across mixed-level snapshots.
    /// </summary>
    Abbreviation,

    /// <summary>The full <see cref="LogLevel"/> name, e.g. <c>Information</c>.</summary>
    Full,
}
