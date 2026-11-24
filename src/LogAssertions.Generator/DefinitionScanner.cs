using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace LogAssertions.Generator;

/// <summary>Finds the <c>[LoggerMessage]</c> methods of the opted-in assemblies and builds the generation result.</summary>
internal static class DefinitionScanner
{
    /// <summary>The namespace of the opt-in attribute and of the generated code's types.</summary>
    private const string LogAssertionsNamespace = "LogAssertions";

    /// <summary>The namespace of Microsoft's logging attribute.</summary>
    private const string LoggingNamespace = "Microsoft.Extensions.Logging";

    /// <summary>The namespace display that names a namespace without the <c>global::</c> prefix.</summary>
    private static readonly SymbolDisplayFormat NamespaceFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    /// <summary>Scans the assemblies <paramref name="compilation"/> opts in and returns what to generate.</summary>
    /// <param name="compilation">The compilation of the test project.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The sources and diagnostics.</returns>
    internal static GenerationResult Scan(Compilation compilation, CancellationToken cancellationToken)
    {
        var optIns = OptIns(compilation);
        if (optIns.Count is 0)
            return GenerationResult.Empty;

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var types = new List<TypeDefinitions>();
        foreach (var (assembly, location) in optIns)
        {
            var scan = new AssemblyScan(
                ProductLogging.Of(assembly),
                SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly),
                assembly.GivesAccessTo(compilation.Assembly),
                location,
                diagnostics);
            CollectNamespace(assembly.GlobalNamespace, scan, cancellationToken);
            if (scan.Found.Count is 0)
                diagnostics.Add(new DiagnosticInfo(GeneratorDiagnostics.NoVisibleMethods.Id, location, assembly.Identity.Name));

            types.AddRange(scan.Found.Where(static t => t.Methods.Length > 0));
        }

        return new GenerationResult(Sources(compilation, types, diagnostics), diagnostics.ToImmutable());
    }

    /// <summary>Writes one source per generated class, leaving out two types that would generate the same class
    /// (LOGA003) and a class whose name a type the compilation sees, or a namespace, already has (LOGA004).</summary>
    /// <param name="compilation">The compilation of the test project, without generated sources.</param>
    /// <param name="types">The definitions of every type.</param>
    /// <param name="diagnostics">Receives the diagnostics.</param>
    /// <returns>The sources.</returns>
    private static ImmutableArray<GeneratedSource> Sources(
        Compilation compilation,
        List<TypeDefinitions> types,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var sources = ImmutableArray.CreateBuilder<GeneratedSource>();
        var hintNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in types
            .GroupBy(static t => QualifiedName(t.Namespace, t.ClassName), StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var type = members[0];
            if (members.Length > 1)
            {
                diagnostics.Add(new DiagnosticInfo(
                    GeneratorDiagnostics.DuplicateClassName.Id, type.OptInLocation, type.TypeDisplay, members[1].TypeDisplay, group.Key));
            }
            else if (IsTaken(compilation, QualifiedName(type.HintNamespace, type.ClassName)))
            {
                diagnostics.Add(new DiagnosticInfo(GeneratorDiagnostics.NameTaken.Id, type.OptInLocation, group.Key, type.TypeDisplay));
            }
            else
            {
                sources.Add(new GeneratedSource(HintName(type, hintNames), SourceWriter.Write(type)));
            }
        }

        return sources.ToImmutable();
    }

    /// <summary>Returns whether the full name of a generated class is taken: a type the compilation sees has it, or a
    /// namespace of the compilation or of a reference does (a class and a namespace cannot share a full name).</summary>
    /// <param name="compilation">The compilation of the test project.</param>
    /// <param name="fullName">The class's full name, its namespace in plain identifiers.</param>
    /// <returns><see langword="true"/> when the name is taken.</returns>
    private static bool IsTaken(Compilation compilation, string fullName)
    {
        if (compilation.GetTypesByMetadataName(fullName).Any(existing => compilation.IsSymbolAccessibleWithin(existing, compilation.Assembly)))
            return true;

        INamespaceSymbol? current = compilation.GlobalNamespace;
        foreach (var part in fullName.Split('.'))
        {
            current = current.GetMembers(part).OfType<INamespaceSymbol>().FirstOrDefault();
            if (current is null)
                return false;
        }

        return true;
    }

    /// <summary>Returns the distinct assemblies the compilation names in <c>[assembly: GenerateLogDefinitions]</c>, in
    /// the order of the attributes, each with the location of the first attribute that names it.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The assemblies and locations.</returns>
    private static List<(IAssemblySymbol Assembly, LocationInfo? Location)> OptIns(Compilation compilation)
    {
        var optIns = new List<(IAssemblySymbol Assembly, LocationInfo? Location)>();
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (OptedInAssembly(attribute) is { } assembly
                && !optIns.Exists(o => SymbolEqualityComparer.Default.Equals(o.Assembly, assembly)))
            {
                optIns.Add((assembly, LocationInfo.Of(attribute.ApplicationSyntaxReference)));
            }
        }

        return optIns;
    }

    /// <summary>Returns the assembly an assembly attribute opts in: for <c>GenerateLogDefinitions(typeof(T))</c>, the
    /// assembly that contains <c>T</c> (an array's element type). A type that does not resolve opts in nothing: its
    /// error type would name the test project.</summary>
    /// <param name="attribute">The assembly attribute.</param>
    /// <returns>The assembly, or <see langword="null"/>.</returns>
    private static IAssemblySymbol? OptedInAssembly(AttributeData attribute)
        => IsAttribute(attribute.AttributeClass, LogAssertionsNamespace, "GenerateLogDefinitionsAttribute")
            && attribute.ConstructorArguments.Length is 1
            && attribute.ConstructorArguments[0].Value is ITypeSymbol type
            && ElementType(type) is { TypeKind: not TypeKind.Error, ContainingAssembly: { } assembly }
            ? assembly
            : null;

    /// <summary>Collects the definitions of the types in a namespace and its nested namespaces.</summary>
    /// <param name="ns">The namespace.</param>
    /// <param name="scan">The scan of the namespace's assembly.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    private static void CollectNamespace(INamespaceSymbol ns, AssemblyScan scan, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var type in ns.GetTypeMembers().Where(t => scan.Sees(t)))
        {
            CollectType(type, scan);
        }

        foreach (var nested in ns.GetNamespaceMembers())
        {
            CollectNamespace(nested, scan, cancellationToken);
        }
    }

    /// <summary>Collects the definitions of a type and its nested types.</summary>
    /// <param name="type">The type.</param>
    /// <param name="scan">The scan of the type's assembly.</param>
    private static void CollectType(INamedTypeSymbol type, AssemblyScan scan)
    {
        var methods = new List<MethodDefinition>();
        // Only ordinary methods: the attribute may also sit on an accessor or an operator, which both Microsoft
        // generators ignore (they read method declarations), so such a member logs nothing.
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind is MethodKind.Ordinary && scan.Sees(m)))
        {
            if (LoggerMessage(method) is { } attribute)
                methods.Add(Read(method, attribute, scan.Product));
        }

        if (methods.Count > 0)
            AddType(type, methods, scan);

        foreach (var nested in type.GetTypeMembers().Where(t => scan.Sees(t)))
        {
            CollectType(nested, scan);
        }
    }

    /// <summary>Adds the definitions of one type, leaving out methods that share a name (LOGA002) and a method named like
    /// the generated class (LOGA004). The type is added even when no method is left, so the assembly does not count as
    /// one without visible methods.</summary>
    /// <param name="type">The type.</param>
    /// <param name="methods">Its definitions.</param>
    /// <param name="scan">The scan of the type's assembly.</param>
    private static void AddType(INamedTypeSymbol type, List<MethodDefinition> methods, AssemblyScan scan)
    {
        var display = type.ToDisplayString();
        var className = ClassName(type);
        var unique = ImmutableArray.CreateBuilder<MethodDefinition>();
        foreach (var group in methods
            .GroupBy(static m => m.MethodName, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            if (group.Skip(1).Any())
                scan.Diagnostics.Add(new DiagnosticInfo(GeneratorDiagnostics.DuplicateMethodName.Id, scan.Location, display, group.Key));
            else if (string.Equals(group.Key, className, StringComparison.Ordinal))
                scan.Diagnostics.Add(new DiagnosticInfo(GeneratorDiagnostics.NameTaken.Id, scan.Location, className, display + "." + group.Key));
            else
                unique.Add(group.First());
        }

        string? ns = null;
        string? hintNamespace = null;
        if (type.ContainingNamespace is { IsGlobalNamespace: false } containing)
        {
            ns = containing.ToDisplayString(NamespaceFormat);
            hintNamespace = PlainName(containing);
        }

        scan.Found.Add(new TypeDefinitions(ns, hintNamespace, className, display, unique.ToImmutable(), scan.Location));
    }

    /// <summary>Reads the definition of a method from its <c>[LoggerMessage]</c> attribute, constructor arguments and
    /// named arguments alike.</summary>
    /// <param name="method">The method.</param>
    /// <param name="attribute">Its attribute.</param>
    /// <param name="product">How the product's logging generator writes records.</param>
    /// <returns>The definition.</returns>
    private static MethodDefinition Read(IMethodSymbol method, AttributeData attribute, ProductLogging product)
    {
        var (eventId, eventName, level, message) = Arguments(attribute);

        // Both Microsoft generators name the event after the method when the event name is empty or whitespace. An empty
        // or absent message is no template (one logs an empty one, the other none; a definition treats both alike).
        var nameSet = !string.IsNullOrWhiteSpace(eventName);
        var name = nameSet ? eventName! : method.Name;
        var template = string.IsNullOrEmpty(message) ? null : message;
        return new MethodDefinition(
            method.Name,
            eventId ?? product.EventId(name, nameSet, method.Name),
            nameSet ? LoggedTemplate.EventName(name) : name,
            level,
            template is null ? null : LoggedTemplate.Of(template, product),
            MethodPlaceholders.Of(method, template, level is not null, product));
    }

    /// <summary>Reads the event ID, event name, level, and message a <c>[LoggerMessage]</c> attribute sets, through its
    /// constructor or as named arguments; each is <see langword="null"/> when the attribute does not set it.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The values the attribute sets.</returns>
    private static (int? EventId, string? EventName, int? Level, string? Message) Arguments(AttributeData attribute)
    {
        int? eventId = null;
        string? eventName = null;
        int? level = null;
        string? message = null;

        // A constructor that does not bind (an error in source the project opts in itself) has no arguments to read.
        var parameters = attribute.AttributeConstructor?.Parameters ?? [];
        for (var i = 0; i < attribute.ConstructorArguments.Length && i < parameters.Length; i++)
        {
            Apply(parameters[i].Name, attribute.ConstructorArguments[i].Value, ref eventId, ref eventName, ref level, ref message);
        }

        foreach (var named in attribute.NamedArguments)
        {
            Apply(named.Key, named.Value.Value, ref eventId, ref eventName, ref level, ref message);
        }

        return (eventId, eventName, level, message);
    }

    /// <summary>Applies one attribute argument to the definition being read. The name matches a constructor parameter
    /// (<c>eventId</c>, <c>level</c>, <c>message</c>) or a property (<c>EventId</c>, <c>EventName</c>, <c>Level</c>,
    /// <c>Message</c>); any other argument is ignored.</summary>
    /// <param name="name">The parameter or property name.</param>
    /// <param name="value">The argument value.</param>
    /// <param name="eventId">The event ID read so far.</param>
    /// <param name="eventName">The event name read so far.</param>
    /// <param name="level">The level read so far.</param>
    /// <param name="message">The message read so far.</param>
    private static void Apply(string name, object? value, ref int? eventId, ref string? eventName, ref int? level, ref string? message)
    {
        switch (name.ToUpperInvariant())
        {
            case "EVENTID":
                eventId = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "EVENTNAME":
                eventName = value as string;
                break;
            case "LEVEL":
                level = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "MESSAGE":
                message = value as string;
                break;
        }
    }

    /// <summary>Returns the generated class name of a type: its name and the names of its containing types joined by
    /// <c>_</c>, a generic type's number of type parameters after a <c>_</c>, and <c>LogDefinitions</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The class name.</returns>
    private static string ClassName(INamedTypeSymbol type)
    {
        var parts = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            parts.Push(current.Arity > 0 ? current.Name + "_" + current.Arity.ToString(System.Globalization.CultureInfo.InvariantCulture) : current.Name);
        }

        return string.Join("_", parts) + "LogDefinitions";
    }

    /// <summary>Returns a namespace's name without keyword escapes (<c>Edge.event</c> for <c>Edge.@event</c>): a hint
    /// name must not contain <c>@</c>.</summary>
    /// <param name="ns">The namespace, not the global one.</param>
    /// <returns>The name.</returns>
    private static string PlainName(INamespaceSymbol ns)
    {
        var parts = new Stack<string>();
        for (var current = ns; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        {
            parts.Push(current.Name);
        }

        return string.Join(".", parts);
    }

    /// <summary>Returns a hint name for a generated class that no other class of the run has, ignoring case: the plain
    /// namespace and the class name, with a number added on a clash (two type names that differ only in case).</summary>
    /// <param name="type">The definitions of the type.</param>
    /// <param name="taken">The hint names given so far; receives this one.</param>
    /// <returns>The hint name.</returns>
    private static string HintName(TypeDefinitions type, HashSet<string> taken)
    {
        var stem = QualifiedName(type.HintNamespace, type.ClassName);
        var hint = stem + ".g.cs";
        var number = 1;
        while (!taken.Add(hint))
        {
            number++;
            hint = stem + "." + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".g.cs";
        }

        return hint;
    }

    /// <summary>Returns a method's <c>[LoggerMessage]</c> attribute, or <see langword="null"/> when it has none.</summary>
    /// <param name="method">The method.</param>
    /// <returns>The attribute, or <see langword="null"/>.</returns>
    private static AttributeData? LoggerMessage(IMethodSymbol method)
        => method.GetAttributes().FirstOrDefault(static a => IsAttribute(a.AttributeClass, LoggingNamespace, "LoggerMessageAttribute"));

    /// <summary>Returns the element type of an array type, through every rank, or the type itself: an opt-in that names
    /// <c>typeof(Order[])</c> means the assembly of <c>Order</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The element type.</returns>
    private static ITypeSymbol ElementType(ITypeSymbol type)
    {
        while (type is IArrayTypeSymbol array)
        {
            type = array.ElementType;
        }

        return type;
    }

    /// <summary>Returns a class name qualified with its namespace.</summary>
    /// <param name="ns">The namespace, or <see langword="null"/> for the global namespace.</param>
    /// <param name="className">The class name.</param>
    /// <returns>The qualified name.</returns>
    private static string QualifiedName(string? ns, string className)
        => ns is null ? className : ns + "." + className;

    /// <summary>Returns whether a class is the attribute with the given namespace and name. An attribute class the
    /// compilation cannot resolve is no match: its arguments would read as absent, and the definition would be wrong.</summary>
    /// <param name="attributeClass">The attribute class, when it resolved.</param>
    /// <param name="ns">The namespace.</param>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool IsAttribute(INamedTypeSymbol? attributeClass, string ns, string name)
        => attributeClass is { TypeKind: not TypeKind.Error }
        && string.Equals(attributeClass.Name, name, StringComparison.Ordinal)
        && string.Equals(attributeClass.ContainingNamespace.ToDisplayString(), ns, StringComparison.Ordinal);
}
