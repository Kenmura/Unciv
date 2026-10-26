using System.Globalization;
using Unciv.Commands;
using Unciv.Assets.Localization;
using Unciv.Commands.Empire;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;

namespace Unciv.Assets;

/// <summary>
/// One policy of the tree. <see cref="Id"/> is the stable node name. <see cref="Pickable"/> is the engine's verdict (<see cref="PolicyTreeResult"/>: adoptable by the
/// rules and the culture or a free policy is there, the same checks <see cref="AdoptPolicy"/> runs); <see cref="Hint"/> says why a policy is not, or that it is adopted.
/// </summary>
public sealed record PolicyCell(string Id, string Name, string Kind, int Row, int Column, string State, bool Pickable, string Hint)
{
    public bool IsAdopted => State == PolicyScreenModel.AdoptedState;

    public bool IsBranchStart => Kind == PolicyScreenModel.BranchStartKind;

    public bool IsComplete => Kind == PolicyScreenModel.BranchCompleteKind;
}

/// <summary>A branch as a column: its opener, the members laid out on a <see cref="Columns"/> x <see cref="Rows"/> grid (1-based cell positions), and the completion bonus.</summary>
public sealed record PolicyBranchView(string Name, string Title, string Progress, PolicyCell Start, IReadOnlyList<PolicyCell> Members, PolicyCell? Complete, int Columns, int Rows);

/// <summary>
/// C020: the policy screen's content as plain data, Godot-free so it is unit-tested. It words the engine's <see cref="PolicyTreeResult"/> (branches, adoptable or locked
/// state with the reason, culture stored and needed, free policies) and the golden-age line of the top bar, lays the members out by their row and column, and decides
/// which command a press sends. It computes no game value and decides no legality: whether a policy can be adopted is the engine's. The Civ V ruleset has no
/// ideologies or tenets, so the screen shows branches only; a ruleset with other branch kinds still renders because everything comes from the tree.
/// </summary>
public static class PolicyScreenModel
{
    public const string AdoptedState = "Adopted";

    public const string AdoptableState = "Adoptable";

    public const string BranchStartKind = "BranchStart";

    public const string BranchCompleteKind = "BranchComplete";

    /// <summary>Culture in the bank against the price of the next policy, and the free policies in hand.</summary>
    public static IReadOnlyList<string> HeaderLines(PolicyTreeResult tree)
    {
        var lines = new List<string> { $"Culture: {Int(tree.StoredCulture)} / {Int(tree.CultureNeededForNextPolicy)}" };
        if (tree.FreePolicies > 0) lines.Add($"Free policies: {Int(tree.FreePolicies)}");
        lines.Add(tree.CanAdopt ? "You can adopt a policy now" : $"Next policy in {Int(Math.Max(0, tree.CultureNeededForNextPolicy - tree.StoredCulture))} more culture");
        return lines;
    }

    /// <summary>The golden-age line of the top bar: turns left while one runs, otherwise the happiness stored towards the next.</summary>
    public static string GoldenAgeLine(EmpireTopBarResult bar) => bar.GoldenAgeTurnsLeft > 0
        ? $"Golden Age: {Int(bar.GoldenAgeTurnsLeft)} turns left"
        : $"Golden Age: {Int(bar.GoldenAgeStored)} / {Int(bar.GoldenAgeNeeded)} happiness stored";

    /// <summary>The branches in the engine's order, each with its members placed by row and column.</summary>
    public static IReadOnlyList<PolicyBranchView> Branches(PolicyTreeResult tree)
    {
        var views = new List<PolicyBranchView>();
        foreach (var branch in tree.Branches)
        {
            var start = Cell(branch.Start, tree, branch);
            var members = branch.Policies.Where(it => it.Kind == "Member").Select(it => Cell(it, tree, branch))
                .OrderBy(it => it.Row).ThenBy(it => it.Column).ToList();
            var complete = branch.Policies.Where(it => it.Kind == BranchCompleteKind).Select(it => Cell(it, tree, branch)).FirstOrDefault();
            int columns = members.Count == 0 ? 0 : members.Max(it => it.Column);
            int rows = members.Count == 0 ? 0 : members.Max(it => it.Row);
            string title = branch.Era.Length > 0 ? $"{branch.Name} ({branch.Era})" : branch.Name;
            views.Add(new PolicyBranchView(branch.Name, title, $"{Int(branch.Adopted)} / {Int(branch.Total)} adopted", start, members, complete, columns, rows));
        }
        return views;
    }

    public static PolicyCell Cell(PolicyEntry entry, PolicyTreeResult tree, PolicyBranchEntry branch)
    {
        bool complete = entry.Kind == BranchCompleteKind;
        bool pickable = !complete && entry.State == AdoptableState && tree.CanAdopt;
        return new PolicyCell("Policy_" + entry.Name, entry.Name, entry.Kind, entry.Row, entry.Column, entry.State, pickable, HintFor(entry, tree, branch));
    }

    private static string HintFor(PolicyEntry entry, PolicyTreeResult tree, PolicyBranchEntry branch)
    {
        if (entry.State == AdoptedState) return "Adopted";
        if (entry.State == AdoptableState)
            return tree.CanAdopt ? "Can be adopted now" : $"Needs {Int(tree.CultureNeededForNextPolicy)} culture ({Int(tree.StoredCulture)} stored)";
        string reason = entry.LockedReason ?? "";
        if (reason.StartsWith("requires:", StringComparison.Ordinal)) return "Needs " + reason["requires:".Length..] + " first";
        if (reason.StartsWith("era:", StringComparison.Ordinal)) return "Opens in the " + (branch.Era.Length > 0 ? branch.Era : reason["era:".Length..]);
        return reason == "complete-automatically" ? "Adopted automatically with the last policy of the branch" : "Not available to you";
    }

    /// <summary>The command the Adopt button sends for a policy, or null when the engine does not offer it now.</summary>
    public static GameCommand? AdoptCommand(PolicyCell cell) => cell.Pickable ? new AdoptPolicy { Policy = cell.Name } : null;

    public static string AdoptLabel(PolicyCell cell) => cell.IsAdopted ? "Adopted" : "Adopt " + cell.Name;

    /// <summary>Fill-in text for the detail panel: the name, the state, why it cannot be adopted (if so) and the effects the ruleset lists.</summary>
    public static string Detail(PolicyCell cell, string description)
    {
        var lines = new List<string> { cell.Name, cell.Hint };
        var body = description.Split('\n').Where(it => it.Length > 0 && it != cell.Name).ToList(); // the ruleset text of a member starts with its own name
        if (body.Count > 0) lines.Add(string.Join("\n", body));
        return string.Join("\n", lines);
    }

    /// <summary>The line under the tree when the engine refuses a command: the fixed word is translated, the engine's reason is too unless it is composed output (digits or markers) that <c>Tr()</c> would re-format.</summary>
    public static string RefusedLine(string reason) => "Refused".Tr() + ": " + Translated(reason);

    /// <summary>The line under the tree when the session call itself failed; the exception text is not a translation key and is shown as it is.</summary>
    public static string ConnectionProblemLine(string error) => "Connection problem".Tr() + ": " + error;

    /// <summary>The line shown when the engine had no policy tree to give.</summary>
    public static string UnavailableLine() => "The engine would not show the policies.".Tr();

    private static string Translated(string text) => ComposedText.IsComposed(text) ? text : text.Tr();

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
