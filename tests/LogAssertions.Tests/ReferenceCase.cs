using System;
using Microsoft.Extensions.Logging;

namespace LogAssertions.Tests;

/// <summary>One fixture method: the definition generated from the fixture's metadata and a call of the method.</summary>
/// <param name="Name">The method name, for the test name.</param>
/// <param name="Generated">The generated definition.</param>
/// <param name="Invoke">A call of the method on the supplied logger.</param>
/// <param name="DynamicLevel">Whether the method takes its level as a parameter, so that its generated definition has no
/// level.</param>
internal readonly record struct ReferenceCase(string Name, LogDefinition Generated, Action<ILogger> Invoke, bool DynamicLevel = false)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}
