using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Queries.Empire;

namespace Unciv.Assets;

/// <summary>One technology placed in the tree. <see cref="GridColumn"/> and <see cref="GridRow"/> are zero-based positions with unused leading columns and rows removed.</summary>
public sealed record TechNode(string Name, string Era, int GridColumn, int GridRow, string State, string Label, Rgb Fill, IReadOnlyList<string> Prerequisites, string Description);

/// <summary>A prerequisite link of the tree: <see cref="From"/> must be researched before <see cref="To"/>.</summary>
public sealed record TechEdge(string From, string To);

/// <summary>The tree as drawn: nodes in column, row order, the links between them, the size of the grid and one heading (era name) per column.</summary>
public sealed record TechLayout(IReadOnlyList<TechNode> Nodes, IReadOnlyList<TechEdge> Edges, int Columns, int Rows, IReadOnlyList<string> ColumnEras);

/// <summary>A button of the tech panel and the command it sends; <see cref="Why"/> explains a disabled one. <see cref="Command"/> is null exactly when the button is disabled.</summary>
public sealed record TechAction(string Id, string Label, bool Enabled, string Why, GameCommand? Command);

/// <summary>
/// C019: the tech screen's content as plain data, Godot-free so it is unit-tested. It places the engine's <see cref="TechTreeResult"/> on a grid, words each node and
/// decides which command a button sends: choosing research replaces the research queue with the tech and the unresearched prerequisites it needs (in prerequisite order,
/// as Unciv's tech picker does), adding appends only the part not yet queued, removing drops the tech and every queued tech that depends on it. Costs, turns, states
/// and unlock text are the engine's; the only rule decided here is how a click becomes a queue, and <see cref="SetResearchQueue"/> validates that again.
/// </summary>
public static class TechScreenModel
{
    public const string Researched = "Researched";

    public const string Queued = "Queued";

    public const string Researchable = "Researchable";

    public const string Locked = "Locked";

    public const string Unavailable = "Unavailable";

    /// <summary>
    /// The tree: techs that have a column and are not hidden for this civ (<c>Unavailable</c>), ordered by column, row, name (ordinal). A tech that lists a prerequisite which is not
    /// placed gets no link for it. The grid starts at the smallest column and row actually used.
    /// </summary>
    public static TechLayout Layout(TechTreeResult tree)
    {
        var placed = tree.Techs.Where(it => it.Column != null && it.State != Unavailable).ToList();
        if (placed.Count == 0) return new TechLayout([], [], 0, 0, []);
        int minColumn = placed.Min(it => it.Column!.Value);
        int minRow = placed.Min(it => it.Row);
        var nodes = placed
            .OrderBy(it => it.Column).ThenBy(it => it.Row).ThenBy(it => it.Name, StringComparer.Ordinal)
            .Select(it => new TechNode(it.Name, it.Era, it.Column!.Value - minColumn, it.Row - minRow, it.State, Label(it), PlaceholderPalette.ForTechState(it.State), it.Prerequisites, it.Description))
            .ToList();
        var names = nodes.Select(it => it.Name).ToHashSet(StringComparer.Ordinal);
        var edges = nodes.SelectMany(node => node.Prerequisites.Where(names.Contains).Select(from => new TechEdge(from, node.Name))).ToList();
        int columns = nodes.Max(it => it.GridColumn) + 1;
        var eras = new string[columns];
        for (int i = 0; i < columns; i++) eras[i] = nodes.FirstOrDefault(it => it.GridColumn == i)?.Era ?? "";
        return new TechLayout(nodes, edges, columns, nodes.Max(it => it.GridRow) + 1, eras);
    }

    /// <summary>Two lines: the name, then the research state (cost, progress and turns).</summary>
    public static string Label(TechTreeEntry tech) => tech.Name + "\n" + Detail(tech);

    public static string Detail(TechTreeEntry tech) => tech.State switch
    {
        Researched => "researched",
        Unavailable => "not available",
        _ when tech.Progress > 0 => $"{I(tech.Progress)}/{I(tech.Cost)}{TurnsSuffix(tech)}",
        _ => $"{I(tech.Cost)} science{TurnsSuffix(tech)}",
    };

    private static string TurnsSuffix(TechTreeEntry tech) => tech.Turns switch
    {
        null => "", // no science per turn: no estimate to show
        0 => ", 0 turns",
        1 => ", 1 turn",
        { } turns => $", {I(turns)} turns",
    };

    /// <summary>The heading line: the current research with its turns, or what to do when there is none; a free technology comes first.</summary>
    public static string Header(TechTreeResult tree)
    {
        if (tree.FreeTechs > 0) return $"Choose {I(tree.FreeTechs)} free technolog{(tree.FreeTechs == 1 ? "y" : "ies")}";
        var current = tree.CurrentTech == null ? null : Find(tree, tree.CurrentTech);
        return current == null ? "Nothing is being researched" : "Researching " + current.Name + TurnsSuffix(current);
    }

    /// <summary>The research queue, numbered, each with its turns; empty when nothing is queued.</summary>
    public static IReadOnlyList<string> QueueLines(TechTreeResult tree)
    {
        var lines = new List<string>();
        for (int i = 0; i < tree.Queue.Count; i++)
        {
            var tech = Find(tree, tree.Queue[i]);
            lines.Add($"{I(i + 1)}. {tree.Queue[i]}{(tech == null ? "" : TurnsSuffix(tech))}");
        }
        return lines;
    }

    /// <summary>The unlock list of one tech: the engine's description, one entry per line.</summary>
    public static IReadOnlyList<string> UnlockLines(TechNode node) =>
        node.Description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The buttons for one tech, in screen order. In free-tech mode there is one (take it, when it is researchable now). Otherwise: research it now (replaces the queue),
    /// add it to the end of the queue, and remove it from the queue. A tech that is researched, hidden, or has a hidden prerequisite offers nothing.
    /// </summary>
    public static IReadOnlyList<TechAction> Actions(TechTreeResult tree, string techName)
    {
        var tech = Find(tree, techName);
        if (tech == null) return [];
        if (tree.FreeTechs > 0)
        {
            bool ok = tech.State is Researchable or Queued;
            return [new TechAction("TakeFree", "Take for free", ok, ok ? "" : "It cannot be researched yet.", ok ? new PickFreeTechnology { Tech = techName } : null)];
        }
        string? closed = tech.State switch
        {
            Researched => "Already researched.",
            Unavailable => "This technology is not available.",
            _ => null,
        };
        var path = closed == null ? PathTo(tree, techName, new HashSet<string>(StringComparer.Ordinal)) : null;
        closed ??= path == null ? "A prerequisite cannot be researched." : null;
        if (closed != null)
            return [Disabled("Research", "Research now", closed), Disabled("Enqueue", "Add to queue", closed), Disabled("Dequeue", "Remove from queue", closed)];

        var actions = new List<TechAction>();
        if (path!.SequenceEqual(tree.Queue)) actions.Add(Disabled("Research", "Research now", "Already the research plan."));
        else actions.Add(Enabled("Research", "Research now", path!));

        if (tech.State == Queued) actions.Add(Disabled("Enqueue", "Add to queue", "Already queued."));
        else actions.Add(Enabled("Enqueue", "Add to queue", [.. tree.Queue, .. PathTo(tree, techName, tree.Queue.ToHashSet(StringComparer.Ordinal))!]));

        if (tech.State != Queued)
        {
            actions.Add(Disabled("Dequeue", "Remove from queue", "Not in the queue."));
        }
        else
        {
            var kept = tree.Queue.Where(it => it != techName && !DependsOn(tree, it, techName)).ToList();
            actions.Add(kept.Count == 0 ? Disabled("Dequeue", "Remove from queue", "The queue cannot be empty; research something else instead.") : Enabled("Dequeue", "Remove from queue", kept));
        }
        return actions;
    }

    /// <summary>
    /// The unresearched techs needed to research <paramref name="techName"/>, prerequisites first and the tech last, leaving out those researched and those in
    /// <paramref name="skip"/>. Null when a needed prerequisite is hidden for this civ (it can never be researched).
    /// </summary>
    public static IReadOnlyList<string>? PathTo(TechTreeResult tree, string techName, ISet<string> skip)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Visit(tree, techName, skip, visited, result) ? result : null;
    }

    private static bool Visit(TechTreeResult tree, string name, ISet<string> skip, HashSet<string> visited, List<string> result)
    {
        if (!visited.Add(name) || skip.Contains(name)) return true;
        var tech = Find(tree, name);
        if (tech == null || tech.State == Unavailable) return false;
        if (tech.State == Researched) return true;
        foreach (var prerequisite in tech.Prerequisites)
            if (!Visit(tree, prerequisite, skip, visited, result)) return false;
        result.Add(name);
        return true;
    }

    /// <summary>True when <paramref name="name"/> needs <paramref name="prerequisite"/>, directly or through other techs.</summary>
    public static bool DependsOn(TechTreeResult tree, string name, string prerequisite)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(name);
        while (pending.Count > 0)
        {
            var tech = Find(tree, pending.Pop());
            if (tech == null) continue;
            foreach (var next in tech.Prerequisites)
            {
                if (next == prerequisite) return true;
                if (seen.Add(next)) pending.Push(next);
            }
        }
        return false;
    }

    private static TechTreeEntry? Find(TechTreeResult tree, string name) => tree.Techs.FirstOrDefault(it => it.Name == name);

    private static TechAction Enabled(string id, string label, IReadOnlyList<string> queue) => new(id, label, true, "", new SetResearchQueue { Techs = [.. queue] });

    private static TechAction Disabled(string id, string label, string why) => new(id, label, false, why, null);

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
}
