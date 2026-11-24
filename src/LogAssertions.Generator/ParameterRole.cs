namespace LogAssertions.Generator;

/// <summary>The role of a <c>[LoggerMessage]</c> method parameter.</summary>
internal enum ParameterRole
{
    /// <summary>An ordinary parameter: a value.</summary>
    Ordinary,

    /// <summary>The logger.</summary>
    Logger,

    /// <summary>The level.</summary>
    Level,

    /// <summary>The exception.</summary>
    Exception,
}
