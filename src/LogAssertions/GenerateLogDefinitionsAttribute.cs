using System;

namespace LogAssertions;

/// <summary>
/// Generates a <see cref="LogDefinition"/> for each <c>[LoggerMessage]</c> method of the assembly that contains
/// <see cref="TypeInAssembly"/>, at compile time and without reflection. Apply it once per product assembly in the test
/// project.
/// </summary>
/// <example>
/// <code>
/// [assembly: GenerateLogDefinitions(typeof(OrderService))]
///
/// await Assert.That(collector).HasLogged().Matching(OrderServiceLogDefinitions.LogOrderShipped).Once();
/// </code>
/// </example>
/// <remarks>
/// <para>
/// For each type with <c>[LoggerMessage]</c> methods, the generator adds an internal static class to the test project,
/// in the type's namespace, named after the type with <c>LogDefinitions</c> appended (a nested type joins the names of
/// its containing types with <c>_</c>; a generic type adds <c>_</c> and its number of type parameters). The class has
/// one <see cref="LogDefinition"/> field per method, named after the method.
/// </para>
/// <para>
/// The generator reads the attributes from the assembly's metadata, so it sees the methods the test project can see:
/// public and protected ones, and internal ones when the product assembly names the test project in
/// <c>InternalsVisibleTo</c>. A private method is not in the reference assembly a test project compiles against; make it
/// internal. Its warnings (<c>LOGA001</c> to <c>LOGA004</c>) are reported at this attribute.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GenerateLogDefinitionsAttribute : Attribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="typeInAssembly">Any type of the product assembly whose <c>[LoggerMessage]</c> methods get a
    /// definition. Must be non-null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="typeInAssembly"/> is <see langword="null"/>.</exception>
    public GenerateLogDefinitionsAttribute(Type typeInAssembly)
    {
        ArgumentNullException.ThrowIfNull(typeInAssembly);
        TypeInAssembly = typeInAssembly;
    }

    /// <summary>A type of the product assembly whose <c>[LoggerMessage]</c> methods get a definition.</summary>
    public Type TypeInAssembly { get; }
}
