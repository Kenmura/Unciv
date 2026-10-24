using System.Reflection;
using System.Runtime.CompilerServices;
using Unciv.Mathematics;

namespace Unciv.Core.Tests.Policy;

// Structural guard rails for the headless engine (T013). The engine must stay free of UI, platform and networking
// dependencies so it can be driven by a server, a test, or a Godot client alike.
public class ArchitectureTests
{
    private static readonly Assembly CoreAssembly = typeof(Vector2).Assembly;

    private static readonly string[] AllowedAssemblyPrefixes = ["System", "Microsoft", "netstandard", "mscorlib"];

    [Fact]
    public void CoreReferencesOnlyBaseClassLibraryAssemblies()
    {
        var offenders = CoreAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "")
            .Where(name => !AllowedAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(offenders.Count == 0, "Unciv.Core references non-BCL assemblies: " + string.Join(", ", offenders));
    }

    [Fact]
    public void AllTypesLiveUnderTheUncivNamespace()
    {
        var offenders = CoreAssembly.GetTypes()
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
            .Where(type => type.FullName?.StartsWith('<') != true) // compiler-emitted <PrivateImplementationDetails> and friends
            .Where(type => !(type.Namespace ?? "").StartsWith("Unciv", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(offenders.Count == 0, "Types outside the Unciv.* namespaces: " + string.Join(", ", offenders));
    }
}
