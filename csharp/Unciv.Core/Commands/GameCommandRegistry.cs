using System.Reflection;
using Unciv.Json;
using Unciv.Logic;

namespace Unciv.Commands;

/// <summary>
/// Builds the registry of every <see cref="GameCommand"/> marked with <see cref="CommandTypeAttribute"/> by scanning an
/// assembly. Registration order is the ordinal order of the wire names, so it never depends on reflection order.
/// </summary>
public static class GameCommandRegistry
{
    private static bool IsKebabCase(string name) =>
        name.Length > 0
        && name[0] is >= 'a' and <= 'z'
        && name[^1] != '-'
        && !name.Contains("--", StringComparison.Ordinal)
        && name.All(it => it is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>All concrete <see cref="GameCommand"/> classes of <paramref name="assembly"/> (default: this engine), ordered by wire name.</summary>
    public static IReadOnlyList<(string Name, Type CommandClass)> Scan(Assembly? assembly = null)
    {
        var found = (assembly ?? typeof(GameCommand).Assembly).GetTypes()
            .Where(it => it is { IsClass: true, IsAbstract: false } && typeof(GameCommand).IsAssignableFrom(it))
            .Select(it => (Name: CommandTypeAttribute.NameOf(it), CommandClass: it))
            .OrderBy(it => it.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var (name, commandClass) in found)
        {
            if (!IsKebabCase(name))
                throw new InvalidOperationException($"{commandClass.Name}: command type \"{name}\" must be kebab-case");
            if (commandClass.GetConstructor(Type.EmptyTypes) == null)
                throw new InvalidOperationException($"{commandClass.Name} needs a public parameterless constructor to be deserialized");
        }
        return found;
    }

    public static CommandRegistry<GameInfo> Create(Assembly? assembly = null)
    {
        var registry = new CommandRegistry<GameInfo>(UncivJson.DefaultOptions);
        foreach (var (name, commandClass) in Scan(assembly))
            registry.Register(commandClass, name);
        return registry;
    }
}
