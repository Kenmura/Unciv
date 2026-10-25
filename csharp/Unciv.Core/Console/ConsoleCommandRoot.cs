namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleCommandRoot</c>. The tree of dev console commands. <c>history</c> is a UI feature of the popup and not part of the
/// engine; the other roots are listed with their groups.
/// </summary>
public sealed class ConsoleCommandRoot : ConsoleCommandNode
{
    public ConsoleCommandRoot()
    {
        Subcommands["unit"] = ConsoleUnitCommands.Create();
        Subcommands["city"] = ConsoleCityCommands.Create();
        Subcommands["tile"] = ConsoleTileCommands.Create();
        Subcommands["civ"] = ConsoleCivCommands.Create();
        Subcommands["game"] = ConsoleGameCommands.Create();
        Subcommands["diplomacy"] = ConsoleDiplomacyCommands.Create();
    }

    /// <summary>Kotlin <c>enumerateCommands</c> (a testing aid): every leaf with the sub-command names leading to it and its format.</summary>
    public IEnumerable<(IReadOnlyList<string> Nodes, string Format)> EnumerateCommands() => Enumerate(this, []);

    private static IEnumerable<(IReadOnlyList<string> Nodes, string Format)> Enumerate(ConsoleCommand node, List<string> nodes)
    {
        if (node is ConsoleAction action) yield return (nodes, action.Format);
        else if (node is ConsoleCommandNode inner)
            foreach (var (name, sub) in inner.Subcommands)
                foreach (var entry in Enumerate(sub, [.. nodes, name]))
                    yield return entry;
    }
}
