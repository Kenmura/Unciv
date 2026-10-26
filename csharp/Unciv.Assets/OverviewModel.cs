using System.Globalization;
using Unciv.Commands.Empire;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;
using Unciv.Queries.Victory;
using Unciv.Utils;

namespace Unciv.Assets;

/// <summary>A table the overview screen draws: column headings and rows of cells. <c>OverviewRow.Key</c> identifies the row for the jump button (city name, unit id) or is empty.</summary>
public sealed record OverviewTable(IReadOnlyList<string> Columns, IReadOnlyList<OverviewRow> Rows, string Empty)
{
    public int Count => Rows.Count;
}

public sealed record OverviewRow(string Key, IReadOnlyList<string> Cells, int X = -1, int Y = -1);

/// <summary>One option of a spy's destination menu and the command that picks it.</summary>
public sealed record SpyMove(string Label, MoveSpy Command);

/// <summary>A spy of the espionage tab: <see cref="Moves"/> are only the destinations the engine offers (<see cref="SpyDestinationsQuery"/>), <see cref="Coup"/> is null unless a coup is possible.</summary>
public sealed record SpyRow(string Name, string Summary, IReadOnlyList<SpyMove> Moves, SetSpyCoup? Coup, string CoupLabel);

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

    public static IReadOnlyList<string> Tabs { get; } = [Stats, Cities, Units, Resources, Ranking, Religion, Espionage];

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string F(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Signed(float value) => (value >= 0f ? "+" : "") + F(value);

    /// <summary>The stats page: one "Label: value" line per number of the engine's overview, then the score breakdown.</summary>
    public static IReadOnlyList<string> StatsLines(EmpireOverviewResult overview)
    {
        var lines = new List<string>
        {
            $"{overview.CivName} - turn {N(overview.Turn)}",
            $"Gold: {N(overview.Gold)} ({Signed(overview.GoldPerTurn)} per turn)",
            $"Science: {Signed(overview.SciencePerTurn)} per turn",
            $"Culture: {Signed(overview.CulturePerTurn)} per turn",
            $"Faith: {Signed(overview.FaithPerTurn)} per turn",
            $"Happiness: {N(overview.Happiness)}",
            $"Cities: {N(overview.Cities)}",
            $"Units: {N(overview.Units)}",
            $"Technologies researched: {N(overview.TechsResearched)}",
            $"Policies adopted: {N(overview.PoliciesAdopted)}",
            $"Score: {N(overview.Score)}",
        };
        foreach (var (name, value) in overview.ScoreBreakdown) lines.Add($"  {name}: {value.ToString("0.#", CultureInfo.InvariantCulture)}");
        return lines;
    }

    /// <summary>The civs the viewer knows with their war status, then the known civs' rankings when the game shows them.</summary>
    public static OverviewTable KnownCivs(EmpireOverviewResult overview) => new(["Civilization", "Kind", "Status", "Score"],
        overview.KnownCivs.Select(it => new OverviewRow(it.CivName, [it.CivName, it.IsCityState ? "City-state" : "Major", it.IsDefeated ? "Defeated" : it.AtWar ? "At war" : "Peace",
            it.Rankings.TryGetValue("Score", out int score) ? N(score) : "-"])).ToList(), "You have met nobody yet.");

    /// <summary>The viewer's own cities (those with private detail), capital first then by name.</summary>
    public static OverviewTable CityTable(ClientSnapshot snapshot) => new(["City", "Pop", "Health", "Building", "Buildings", "Notes"],
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
    public static OverviewTable UnitTable(ClientSnapshot snapshot) => new(["Unit", "Position", "Health", "Moves", "Order"],
        snapshot.Units.Where(it => it.Owner == snapshot.You && it.Own != null)
            .OrderBy(it => it.Name, StringComparer.Ordinal).ThenBy(it => it.Id)
            .Select(it => new OverviewRow(N(it.Id), [it.Name, $"{N(it.X)},{N(it.Y)}", N(it.Own!.Health), F(it.Own.CurrentMovement), UnitOrder(it)], it.X, it.Y)).ToList(),
        "You have no units.");

    private static string UnitOrder(UnitDto unit) =>
        unit.Own!.Automated ? "Automated" : unit.Own.Action ?? (unit.IsFortified ? "Fortified" : unit.IsEmbarked ? "Embarked" : "-");

    /// <summary>The luxury and strategic resources the engine says the viewer has, in the engine's order.</summary>
    public static OverviewTable ResourceTable(EmpireTopBarResult bar) => new(["Resource", "Type", "Amount"],
        bar.Resources.Select(it => new OverviewRow(it.Name, [it.Name, it.Type, N(it.Amount)])).ToList(), "You have no luxury or strategic resources.");

    /// <summary>
    /// One table per ranking column, best first as the engine ordered it (defeated civs last, shown as "Defeated"). A civ the viewer has not met has no name in the
    /// answer and is shown as "Unknown civilization"; the viewer is marked "(you)". The title of a table is the column's label.
    /// </summary>
    public static IReadOnlyList<(string Title, OverviewTable Table)> RankingTables(IReadOnlyList<RankingColumn> columns) => columns.Select(column =>
        (column.Label, new OverviewTable(["Rank", "Civilization", "Value"],
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
        string summary = spy.IsAlive
            ? $"{spy.Name} (rank {N(spy.Rank)}): {spy.Action} in {spy.Location}"
            : $"{spy.Name}: dead";
        string coupLabel = spy.CanDoCoup && spy.CoupChance is { } chance
            ? (staging ? "Call off the coup" : $"Stage a coup ({KotlinMath.RoundToInt(chance * 100f).ToString(CultureInfo.InvariantCulture)}% chance)")
            : "";
        return new SpyRow(spy.Name, summary, moves, spy.CanDoCoup ? new SetSpyCoup { Spy = spy.Name, Stage = !staging } : null, coupLabel);
    }).ToList();
}
