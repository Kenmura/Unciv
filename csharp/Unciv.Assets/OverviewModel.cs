using System.Globalization;
using Unciv.Commands.Empire;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;
using Unciv.Queries.Victory;
using Unciv.Utils;

namespace Unciv.Assets;

/// <summary>C074: a typed column: the untranslated heading and whether its cells are numbers (the table right-aligns them in tabular figures and sorts them by value).</summary>
public sealed record OverviewColumn(string Heading, bool Numeric = false)
{
    public static implicit operator OverviewColumn(string heading) => new(heading);
}

/// <summary>A table the overview screen draws: typed columns and rows of cells. <c>OverviewRow.Key</c> identifies the row for the jump button (city name, unit id) or is empty.</summary>
public sealed record OverviewTable(IReadOnlyList<OverviewColumn> Columns, IReadOnlyList<OverviewRow> Rows, string Empty)
{
    public int Count => Rows.Count;
}

/// <summary>C074: one labelled value of a stats card; <see cref="Stat"/> names the stat icon to show (gold, science, culture, faith, happiness) or is empty. <see cref="Indent"/> marks a breakdown line.</summary>
public sealed record StatsEntry(string Label, string Value, string Stat = "", bool Indent = false);

/// <summary>C074: a card of the stats page: a title and its entries.</summary>
public sealed record StatsCard(string Title, IReadOnlyList<StatsEntry> Entries);

public sealed record OverviewRow(string Key, IReadOnlyList<string> Cells, int X = -1, int Y = -1);

/// <summary>A civ of the global politics tab with the engine's diplomacy panel for it; the panel is null when the engine refused it.</summary>
public sealed record PoliticsEntry(OverviewCiv Civ, DiplomacyPanelResult? Panel);

/// <summary>One option of a spy's destination menu and the command that picks it.</summary>
public sealed record SpyMove(string Label, MoveSpy Command);

/// <summary>A spy of the espionage tab (C074: rank, location and action are the table's typed cells, a dead spy shows a dash and "Dead"; <see cref="CoupShort"/> is the coup button's text and <see cref="CoupLabel"/> its tooltip): <see cref="Moves"/> are only the destinations the engine offers (<see cref="SpyDestinationsQuery"/>), <see cref="Coup"/> is null unless a coup is possible.</summary>
public sealed record SpyRow(string Name, string Rank, string Location, string Action, IReadOnlyList<SpyMove> Moves, SetSpyCoup? Coup, string CoupLabel, string CoupShort);

/// <summary>
/// C023: the empire overview screen's content as plain data, Godot-free so it is unit-tested. Cities and units come from the viewer's own snapshot and only the viewer's
/// (an entry without the private <c>Own</c> detail, or of another civ, is never listed); everything else is worded from the engine's query answers (stats, resources,
/// rankings, religion, spies). No game value is computed here: the screen only formats and decides which command a button sends.
/// </summary>
public static class OverviewModel
{
    public const string Stats = "Stats";
    public const string Cities = "Cities";
    public const string Units = "Units";
    public const string Resources = "Resources";
    public const string Ranking = "Ranking";
    public const string Religion = "Religion";
    public const string Espionage = "Espionage";

    public const string Politics = "Politics";

    public static IReadOnlyList<string> Tabs { get; } = [Stats, Cities, Units, Resources, Ranking, Religion, Espionage, Politics];

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string F(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Signed(float value) => (value >= 0f ? "+" : "") + F(value);

    /// <summary>
    /// C074: the stats page as cards (Empire, Economy, Progress, Score): one entry per number of the engine's overview, the score breakdown as indented entries of the Score card.
    /// Values are worded only; the per-turn numbers carry their sign.
    /// </summary>
    public static IReadOnlyList<StatsCard> StatsCards(EmpireOverviewResult overview)
    {
        var score = new List<StatsEntry> { new("Score", N(overview.Score)) };
        foreach (var (name, value) in overview.ScoreBreakdown) score.Add(new StatsEntry(name, value.ToString("0.#", CultureInfo.InvariantCulture), Indent: true));
        return
        [
            new("Empire", [new("Civilization", overview.CivName), new("Turn", N(overview.Turn)), new("Cities", N(overview.Cities)), new("Units", N(overview.Units))]),
            new("Economy", [new("Gold", N(overview.Gold), "Gold"), new("Gold per turn", Signed(overview.GoldPerTurn), "Gold"), new("Happiness", N(overview.Happiness), "Happiness")]),
            new("Progress",
            [
                new("Science per turn", Signed(overview.SciencePerTurn), "Science"), new("Culture per turn", Signed(overview.CulturePerTurn), "Culture"),
                new("Faith per turn", Signed(overview.FaithPerTurn), "Faith"), new("Technologies researched", N(overview.TechsResearched)), new("Policies adopted", N(overview.PoliciesAdopted)),
            ]),
            new("Score", score),
        ];
    }

    /// <summary>The civs the viewer knows with their war status, then the known civs' rankings when the game shows them.</summary>
    public static OverviewTable KnownCivs(EmpireOverviewResult overview) => new(["Civilization", "Kind", "Status", new OverviewColumn("Score", true)],
        overview.KnownCivs.Select(it => new OverviewRow(it.CivName, [it.CivName, it.IsCityState ? "City-state" : "Major", it.IsDefeated ? "Defeated" : it.AtWar ? "At war" : "Peace",
            it.Rankings.TryGetValue("Score", out int score) ? N(score) : "-"])).ToList(), "You have met nobody yet.");

    /// <summary>The viewer's own cities (those with private detail), capital first then by name.</summary>
    public static OverviewTable CityTable(ClientSnapshot snapshot) => new(["City", new OverviewColumn("Pop", true), "Health", "Building", new OverviewColumn("Buildings", true), "Notes"],
        snapshot.Cities.Where(it => it.Owner == snapshot.You && it.Own != null)
            .OrderByDescending(it => it.IsCapital).ThenBy(it => it.Name, StringComparer.Ordinal)
            .Select(it => new OverviewRow(it.Name, [it.Name, N(it.Population), $"{N(it.Health)}/{N(it.MaxHealth)}",
                it.Own!.ConstructionQueue.Count > 0 ? it.Own.ConstructionQueue[0] : "-", N(it.Own.Buildings.Count), CityNotes(it)], it.X, it.Y)).ToList(),
        "You have no cities.");

    private static string CityNotes(CityDto city)
    {
        var notes = new List<string>();
        if (city.IsCapital) notes.Add("Capital");
        if (city.IsPuppet) notes.Add("Puppet");
        if (city.IsBeingRazed) notes.Add("Razing");
        return notes.Count == 0 ? "-" : string.Join(", ", notes);
    }

    /// <summary>The viewer's own units (those with private detail) by name, then id.</summary>
    public static OverviewTable UnitTable(ClientSnapshot snapshot) => new(["Unit", "Position", new OverviewColumn("Health", true), new OverviewColumn("Moves", true), "Order"],
        snapshot.Units.Where(it => it.Owner == snapshot.You && it.Own != null)
            .OrderBy(it => it.Name, StringComparer.Ordinal).ThenBy(it => it.Id)
            .Select(it => new OverviewRow(N(it.Id), [it.Name, $"{N(it.X)},{N(it.Y)}", N(it.Own!.Health), F(it.Own.CurrentMovement), UnitOrder(it)], it.X, it.Y)).ToList(),
        "You have no units.");

    private static string UnitOrder(UnitDto unit) =>
        unit.Own!.Automated ? "Automated" : unit.Own.Action ?? (unit.IsFortified ? "Fortified" : unit.IsEmbarked ? "Embarked" : "-");

    /// <summary>The luxury and strategic resources the engine says the viewer has, in the engine's order.</summary>
    public static OverviewTable ResourceTable(EmpireTopBarResult bar) => new(["Resource", "Type", new OverviewColumn("Amount", true)],
        bar.Resources.Select(it => new OverviewRow(it.Name, [it.Name, it.Type, N(it.Amount)])).ToList(), "You have no luxury or strategic resources.");

    /// <summary>
    /// One table per ranking column, best first as the engine ordered it (defeated civs last, shown as "Defeated"). A civ the viewer has not met has no name in the
    /// answer and is shown as "Unknown civilization"; the viewer is marked "(you)". The title of a table is the column's label.
    /// </summary>
    public static IReadOnlyList<(string Title, OverviewTable Table)> RankingTables(IReadOnlyList<RankingColumn> columns) => columns.Select(column =>
        (column.Label, new OverviewTable([new OverviewColumn("Rank", true), "Civilization", new OverviewColumn("Value", true)],
            column.Entries.Select((entry, index) => new OverviewRow(entry.Civ.CivName ?? "",
                [N(index + 1), (entry.Civ.CivName ?? "Unknown civilization") + (entry.Civ.IsViewer ? " (you)" : ""), entry.Civ.IsDefeated ? "Defeated" : N(entry.Value)])).ToList(),
            "No rankings."))).ToList();

    /// <summary>The religion page: state, faith, own religion and beliefs, the beliefs still to choose, and what is still free to take.</summary>
    public static IReadOnlyList<string> ReligionLines(ReligionResult religion)
    {
        if (!religion.Enabled) return ["Religion is not enabled in this game."];
        var lines = new List<string> { $"State: {religion.State}", $"Faith: {N(religion.Faith)}" };
        if (religion.DisplayName != null) lines.Add("Religion: " + religion.DisplayName);
        lines.Add(religion.OwnBeliefs.Count == 0
            ? "You hold no beliefs yet."
            : "Beliefs: " + string.Join(", ", religion.OwnBeliefs.Select(it => $"{it.Name} ({it.Type})")));
        if (religion.CanFoundPantheon) lines.Add($"You can found a pantheon ({N(religion.FaithForPantheon)} faith).");
        else if (religion.CanExpandPantheon) lines.Add($"You can add to your pantheon ({N(religion.FaithForPantheon)} faith).");
        foreach (var (type, count) in religion.ToChoose) lines.Add($"To choose: {N(count)} {type} belief(s)");
        foreach (var (type, names) in religion.Available) lines.Add($"Free {type} beliefs: {N(names.Count)}");
        return lines;
    }

    /// <summary>
    /// The spies with the destinations the engine offers (hideout first, then cities in the engine's order) and the coup button when the engine allows one.
    /// A city of a civ the viewer has not met is labelled by city name only.
    /// </summary>
    public static IReadOnlyList<SpyRow> SpyRows(SpyDestinationsResult spies) => spies.Spies.Select(spy =>
    {
        var moves = new List<SpyMove>();
        if (spy.CanMoveToHideout) moves.Add(new SpyMove("Hideout", new MoveSpy { Spy = spy.Name, CityId = null }));
        foreach (var destination in spy.Destinations)
            moves.Add(new SpyMove(destination.Civ.Length > 0 ? $"{destination.CityName} ({destination.Civ})" : destination.CityName, new MoveSpy { Spy = spy.Name, CityId = destination.CityId }));
        bool staging = spy.Action == "Coup";
        string percent = spy.CoupChance is { } chance ? KotlinMath.RoundToInt(chance * 100f).ToString(CultureInfo.InvariantCulture) : "";
        string coupLabel = spy.CanDoCoup && spy.CoupChance != null ? (staging ? "Call off the coup" : $"Stage a coup ({percent}% chance)") : "";
        string coupShort = coupLabel.Length == 0 ? "" : staging ? "Call off" : $"Coup {percent}%";
        return new SpyRow(spy.Name, N(spy.Rank), spy.IsAlive ? spy.Location : "-", spy.IsAlive ? spy.Action : "Dead", moves, spy.CanDoCoup ? new SetSpyCoup { Spy = spy.Name, Stage = !staging } : null, coupLabel, coupShort);
    }).ToList();

    /// <summary>
    /// C050: the global politics tab's table, one row per civ the viewer has met (the engine's known civs, in its order) with its relation to the viewer as the diplomacy panel
    /// answers it: status, how that civ sees the viewer, a city-state's influence, open borders and the peace treaty. Relations between two other civs are not shown (no query
    /// answers them, so the client cannot leak them); a civ the engine would not describe shows dashes.
    /// </summary>
    public static OverviewTable PoliticsTable(IReadOnlyList<PoliticsEntry> entries) => new(["Civilization", "Kind", "Status", "Attitude", "Influence", "Borders", "Peace treaty"],
        entries.Select(entry =>
        {
            var civ = entry.Civ;
            var panel = entry.Panel;
            string status = civ.IsDefeated ? "Defeated" : panel == null ? (civ.AtWar ? "War" : "Peace") : panel.Status == "DefensivePact" ? "Defensive pact" : panel.Status;
            string influence = panel is { Influence: { } value } ? F(value) + (panel.IsAlly ? " (ally)" : "") : "-";
            string borders = panel == null ? "-" : panel.HasOpenBorders && panel.TheyHaveOpenBordersWithUs ? "Both ways" : panel.HasOpenBorders ? "You may enter" : panel.TheyHaveOpenBordersWithUs ? "They may enter" : "-";
            string treaty = panel is { TurnsToPeaceTreaty: > 0 } ? $"{N(panel.TurnsToPeaceTreaty)} turns" : "-";
            return new OverviewRow(civ.CivName, [civ.CivName, civ.IsCityState ? "City-state" : "Major", status, panel?.Relationship ?? "-", influence, borders, treaty]);
        }).ToList(), "You have met nobody yet.");

    /// <summary>
    /// C050: the World Congress vote under the politics table. <paramref name="vote"/> is null when the engine refuses the query; candidates are civ ids that
    /// <paramref name="civName"/> turns into display names. Abstaining is always possible, as in the Kotlin vote picker.
    /// </summary>
    public static IReadOnlyList<string> VoteLines(DiplomaticVoteResult? vote, Func<string, string> civName)
    {
        if (vote == null) return ["The engine would not show the diplomatic vote."];
        if (!vote.CanVote) return ["No diplomatic vote is open for you now."];
        return ["A diplomatic vote is open for you.", "You can vote for: " + string.Join(", ", vote.Candidates.Select(civName)) + ".", "You may also abstain."];
    }
}
