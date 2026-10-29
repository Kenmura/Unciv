using System.Globalization;
using Unciv.Assets.Localization;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Assets;

/// <summary>
/// One policy of the tree. <see cref="Id"/> is the stable node name. <see cref="Pickable"/> is the engine's verdict (<see cref="PolicyTreeResult"/>: adoptable by the
/// rules and the culture or a free policy is there, the same checks <see cref="AdoptPolicy"/> runs); <see cref="Hint"/> says why a policy is not, or that it is adopted.
/// </summary>
public sealed record PolicyCell(string Id, string Name, string Kind, int Row, int Column, string State, bool Pickable, string Hint)
{
    /// <summary>C071: the policies that must be adopted first (the links drawn into this node); empty for a branch opener.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>C071: what the policy does, one entry per distinct line of the ruleset text (repeated lines are counted, not repeated); empty when no ruleset was given.</summary>
    public IReadOnlyList<PolicyEffect> Effects { get; init; } = [];

    /// <summary>C071: the policy's icon (<c>icon/policy/&lt;name&gt;</c>).</summary>
    public AssetKey IconKey => AssetKey.Of(RulesetAssetKeys.PolicyIcon, Name);

    public bool IsAdopted => State == PolicyScreenModel.AdoptedState;

    public bool IsBranchStart => Kind == PolicyScreenModel.BranchStartKind;

    public bool IsComplete => Kind == PolicyScreenModel.BranchCompleteKind;
}

/// <summary>C071: one effect line of a policy. <see cref="Count"/> says how often the ruleset repeats it (Volunteer Army lists "Free [Foreign Legion] appears" six times); <see cref="Display"/> is the line shown.</summary>
public sealed record PolicyEffect(string Text, int Count)
{
    public string Display => Count > 1 ? $"{Text} (×{Count.ToString(CultureInfo.InvariantCulture)})" : Text;
}

/// <summary>
/// A branch as a card: its opener, the members laid out on a <see cref="Columns"/> x <see cref="Rows"/> grid, and the completion bonus. A member's cell position is
/// <c>Row - RowOrigin</c>, <c>Column - ColumnOrigin</c> (zero-based): the origin is 1, or 0 when the ruleset starts a branch at column 0 (the ideologies do).
/// </summary>
public sealed record PolicyBranchView(string Name, string Title, string Progress, PolicyCell Start, IReadOnlyList<PolicyCell> Members, PolicyCell? Complete, int Columns, int Rows)
{
    /// <summary>C071: the era the branch opens in (empty when the ruleset gives none).</summary>
    public string Era { get; init; } = "";

    public int Adopted { get; init; }

    public int Total { get; init; }

    public int ColumnOrigin { get; init; } = 1;

    public int RowOrigin { get; init; } = 1;

    /// <summary>C071: the branch can still be started (its opener is adoptable now or adopted); a locked branch is drawn dimmed with <see cref="PolicyCell.Hint"/> of its opener.</summary>
    public bool IsOpen => Start.State != "Locked";

    /// <summary>C071: every cell of the card, opener first, then the members, then the completion bonus.</summary>
    public IEnumerable<PolicyCell> Cells => new[] { Start }.Concat(Members).Concat(Complete == null ? [] : [Complete]);
}

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
        if (tree.FreeTenets > 0) lines.Add($"Free tenets: {Int(tree.FreeTenets)}");
        lines.Add(tree.CanAdopt ? "You can adopt a policy now" : $"Next policy in {Int(Math.Max(0, tree.CultureNeededForNextPolicy - tree.StoredCulture))} more culture");
        return lines;
    }

    /// <summary>The golden-age line of the top bar: turns left while one runs, otherwise the happiness stored towards the next.</summary>
    public static string GoldenAgeLine(EmpireTopBarResult bar) => bar.GoldenAgeTurnsLeft > 0
        ? $"Golden Age: {Int(bar.GoldenAgeTurnsLeft)} turns left"
        : $"Golden Age: {Int(bar.GoldenAgeStored)} / {Int(bar.GoldenAgeNeeded)} happiness stored";

    /// <summary>The branches in the engine's order, each with its members placed by row and column.</summary>
    public static IReadOnlyList<PolicyBranchView> Branches(PolicyTreeResult tree, RulesetClass? ruleset = null)
    {
        var views = new List<PolicyBranchView>();
        foreach (var branch in tree.Branches)
        {
            var start = Cell(branch.Start, tree, branch, ruleset);
            var members = branch.Policies.Where(it => it.Kind == "Member").Select(it => Cell(it, tree, branch, ruleset))
                .OrderBy(it => it.Row).ThenBy(it => it.Column).ToList();
            var complete = branch.Policies.Where(it => it.Kind == BranchCompleteKind).Select(it => Cell(it, tree, branch, ruleset)).FirstOrDefault();
            // C071: the origin is 1 unless a member sits at 0 (the ideologies start at column 0), so no member falls off the grid
            int columnOrigin = members.Count == 0 ? 1 : Math.Min(1, members.Min(it => it.Column));
            int rowOrigin = members.Count == 0 ? 1 : Math.Min(1, members.Min(it => it.Row));
            int columns = members.Count == 0 ? 0 : members.Max(it => it.Column) - columnOrigin + 1;
            int rows = members.Count == 0 ? 0 : members.Max(it => it.Row) - rowOrigin + 1;
            string title = branch.Era.Length > 0 ? $"{branch.Name} ({branch.Era})" : branch.Name;
            views.Add(new PolicyBranchView(branch.Name, title, $"{Int(branch.Adopted)} / {Int(branch.Total)} adopted", start, members, complete, columns, rows)
            {
                Era = branch.Era,
                Adopted = branch.Adopted,
                Total = branch.Total,
                ColumnOrigin = columnOrigin,
                RowOrigin = rowOrigin,
            });
        }
        return views;
    }

    public static PolicyCell Cell(PolicyEntry entry, PolicyTreeResult tree, PolicyBranchEntry branch, RulesetClass? ruleset = null)
    {
        bool complete = entry.Kind == BranchCompleteKind;
        bool pickable = !complete && entry.State == AdoptableState && tree.CanAdopt;
        return new PolicyCell("Policy_" + entry.Name, entry.Name, entry.Kind, entry.Row, entry.Column, entry.State, pickable, HintFor(entry, tree, branch))
        {
            Requires = entry.Requires,
            Effects = ruleset == null ? [] : EffectLines(DescriptionOf(ruleset, entry.Name), entry.Name),
        };
    }

    /// <summary>C071: the ruleset's description of a policy or branch (its effects, one line each); empty for an unknown name.</summary>
    public static string DescriptionOf(RulesetClass ruleset, string name) =>
        ruleset.Policies.TryGetValue(name, out var policy) ? policy.GetDescription()
        : ruleset.PolicyBranches.TryGetValue(name, out var branch) ? branch.GetDescription() : "";

    /// <summary>
    /// C071: the effect lines of a description: the policy's own name line (the ruleset text of a member starts with it, translated or not) and blank lines are dropped, and a line
    /// the ruleset repeats is shown once with its count (Volunteer Army's six "Free [Foreign Legion] appears" become one line, ×6). The order is that of the first occurrence.
    /// </summary>
    public static IReadOnlyList<PolicyEffect> EffectLines(string description, string name = "")
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        string translated = name.Length == 0 ? "" : name.Tr();
        foreach (string raw in description.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || (name.Length > 0 && (line == name || line == translated))) continue;
            if (counts.TryGetValue(line, out int count)) counts[line] = count + 1;
            else
            {
                counts[line] = 1;
                order.Add(line);
            }
        }
        return order.Select(it => new PolicyEffect(it, counts[it])).ToList();
    }

    /// <summary>
    /// C071: the search filter shared with the tech screen (<see cref="SearchMatcher"/>): an empty query matches every policy, otherwise a policy matches when the query is part of its
    /// name (also as the player sees it, through <paramref name="translate"/>) or of one of its effect lines.
    /// </summary>
    public static bool Matches(PolicyCell cell, string query, Func<string, string>? translate = null) =>
        SearchMatcher.Matches(query, [cell.Name], cell.Effects.Select(it => it.Text), translate);

    private static string HintFor(PolicyEntry entry, PolicyTreeResult tree, PolicyBranchEntry branch)
    {
        if (entry.State == AdoptedState) return "Adopted";
        if (entry.State == AdoptableState)
            return tree.CanAdopt ? "Can be adopted now" : $"Needs {Int(tree.CultureNeededForNextPolicy)} culture ({Int(tree.StoredCulture)} stored)";
        string reason = entry.LockedReason ?? "";
        if (reason.StartsWith("requires:", StringComparison.Ordinal)) return "Needs " + reason["requires:".Length..] + " first";
        if (reason == "ideology-locked") return "Needs the Modern era or three Factories";
        if (reason.StartsWith("ideology:", StringComparison.Ordinal)) return "Needs the " + reason["ideology:".Length..] + " ideology";
        if (reason.StartsWith("tenet-level:", StringComparison.Ordinal)) return "Needs more Level " + reason["tenet-level:".Length..] + " tenets than Level " + (int.Parse(reason["tenet-level:".Length..], CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture) + " tenets";
        if (reason.StartsWith("era:", StringComparison.Ordinal)) return "Opens in the " + (branch.Era.Length > 0 ? branch.Era : reason["era:".Length..]);
        return reason == "complete-automatically" ? "Adopted automatically with the last policy of the branch" : "Not available to you";
    }

    /// <summary>The command the Adopt button sends for a policy, or null when the engine does not offer it now.</summary>
    public static GameCommand? AdoptCommand(PolicyCell cell) => cell.Pickable ? new AdoptPolicy { Policy = cell.Name } : null;

    public static string AdoptLabel(PolicyCell cell) => cell.IsAdopted ? "Adopted" : "Adopt " + cell.Name;

    /// <summary>The detail panel's text: the name and the state (both through <paramref name="translate"/>), then the effect lines as the ruleset worded them (they are already translated).</summary>
    public static string Detail(PolicyCell cell, Func<string, string> translate)
    {
        var lines = new List<string> { translate(cell.Name), translate(cell.Hint) };
        lines.AddRange(cell.Effects.Select(it => it.Display));
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
