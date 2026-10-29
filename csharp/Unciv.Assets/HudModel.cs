using System.Globalization;
using Unciv.Logic;
using Unciv.Models.StatTypes;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.Assets;

/// <summary>One entry of the top bar: <see cref="Id"/> is stable (for tests and node names), <see cref="Icon"/> may be null (turn and year have none).</summary>
public sealed record HudStat(string Id, AssetKey? Icon, string Text, string Tooltip);

public enum HudActionKind
{
    EndTurn,
    NextUnit,
    Decision,
    UndoEndTurn,
    Resolving,
}

/// <summary>What the next-action button says and does. <see cref="Decision"/> is set for <see cref="HudActionKind.Decision"/> (and for NextUnit, which is the DueUnits decision).</summary>
public sealed record HudAction(HudActionKind Kind, string Label, DecisionKind? Decision, bool Enabled);

/// <summary>
/// C011: the world screen's top bar, turn banner and next-action button as plain data, Godot-free so it is unit-tested. The numbers come from the engine's
/// <see cref="EmpireTopBarResult"/> and the "what is left to do" answer from the engine's <see cref="PendingDecisionsResult"/> (the same list that makes End turn
/// refuse); this class only formats and orders them, it computes no game value.
/// </summary>
public static class HudModel
{
    /// <summary>Kotlin <c>NextTurnAction</c> texts, in the order <see cref="PendingDecisions"/> lists the decisions.</summary>
    private static readonly Dictionary<DecisionKind, string> DecisionLabels = new()
    {
        [DecisionKind.FreeGreatPerson] = "Choose a free great person",
        [DecisionKind.CityProduction] = "Pick construction",
        [DecisionKind.Technology] = "Pick a technology",
        [DecisionKind.Policy] = "Pick a policy",
        [DecisionKind.MoveSpies] = "Move spies",
        [DecisionKind.Pantheon] = "Choose a pantheon",
        [DecisionKind.FoundReligion] = "Found a religion",
        [DecisionKind.EnhanceReligion] = "Enhance religion",
        [DecisionKind.ReformReligion] = "Reform religion",
        [DecisionKind.DiplomaticVote] = "Vote",
        [DecisionKind.DueUnits] = "Next unit",
        [DecisionKind.MoveAutomatedUnits] = "Move automated units",
        [DecisionKind.Alert] = "Review alert",
        [DecisionKind.TradeRequest] = "Review trade offer",
    };

    public const string EndTurnLabel = "Next turn";

    public static IReadOnlyList<HudStat> TopBar(EmpireTopBarResult bar)
    {
        var stats = new List<HudStat>
        {
            new("Gold", StatKey(Stat.Gold), $"{Int(bar.Gold)} ({Signed(bar.GoldPerTurn)})", "Gold, and its change next turn"),
            new("Science", StatKey(Stat.Science), Signed(bar.SciencePerTurn), "Science per turn"),
            new("Culture", StatKey(Stat.Culture), Signed(bar.CulturePerTurn), "Culture per turn"),
            new("Happiness", StatKey(Stat.Happiness), Int(bar.Happiness), "Happiness"),
            new("Faith", StatKey(Stat.Faith), Signed(bar.FaithPerTurn), "Faith per turn"),
            GoldenAge(bar),
        };
        foreach (var resource in bar.Resources)
            stats.Add(new HudStat("Resource:" + resource.Name, AssetKey.Of(RulesetAssetKeys.ResourceIcon, resource.Name), Int(resource.Amount), $"{resource.Name} ({resource.Type})"));
        stats.Add(new HudStat("Turn", null, TurnAndYear(bar.Turn, bar.Year), "Turn and year"));
        return stats;
    }

    private static HudStat GoldenAge(EmpireTopBarResult bar) => bar.GoldenAgeTurnsLeft > 0
        ? new HudStat("GoldenAge", StatKey(Stat.Happiness), $"Golden Age {Int(bar.GoldenAgeTurnsLeft)}", "Golden Age: turns left")
        : new HudStat("GoldenAge", StatKey(Stat.Happiness), $"{Int(bar.GoldenAgeStored)}/{Int(bar.GoldenAgeNeeded)}", "Happiness stored towards the next Golden Age");

    private static AssetKey StatKey(Stat stat) => AssetKey.Of(RulesetAssetKeys.StatIcon, stat.ToString());

    /// <summary>The year as the Kotlin calendar writes it: absolute value with BC for negative years, AD otherwise.</summary>
    public static string YearText(int year) => year < 0 ? Int(-year) + " BC" : Int(year) + " AD";

    public static string TurnAndYear(int turn, int year) => $"Turn {Int(turn)} - {YearText(year)}";

    /// <summary>The banner shown when the viewer's turn starts.</summary>
    public static string Banner(int turn, int year, string civName) => $"Your turn, {civName}\n{TurnAndYear(turn, year)}";

    /// <summary>
    /// The next-action button. A simultaneous game (TurnStatus) can say the viewer already ended the turn or the host is resolving it; otherwise the first pending
    /// decision decides the text, and with nothing pending it is End turn. Any decision stays on the button until answered, as in Kotlin; the screen also offers a
    /// separate End turn whenever no decision is <c>Blocking</c> (see <see cref="CanEndTurn"/>).
    /// </summary>
    public static HudAction NextAction(PendingDecisionsResult? pending, TurnStatusDto? status = null, string? viewer = null)
    {
        if (status is { Phase: "Resolving" }) return new HudAction(HudActionKind.Resolving, "Resolving turn...", null, false);
        if (viewer != null && status != null && status.Ended.Contains(viewer, StringComparer.Ordinal))
            return new HudAction(HudActionKind.UndoEndTurn, "Undo end turn", null, true);
        var first = pending?.Decisions.FirstOrDefault();
        if (first == null || !Enum.TryParse<DecisionKind>(first.Kind, out var kind)) return new HudAction(HudActionKind.EndTurn, EndTurnLabel, null, true);
        var label = DecisionLabels.GetValueOrDefault(kind, first.Kind);
        return new HudAction(kind == DecisionKind.DueUnits ? HudActionKind.NextUnit : HudActionKind.Decision, label, kind, true);
    }

    /// <summary>True when no pending decision refuses End turn (the engine's Blocking flag), so the secondary End turn button may be shown.</summary>
    public static bool CanEndTurn(PendingDecisionsResult? pending) => pending == null || pending.Decisions.All(it => !it.Blocking);

    /// <summary>The unit after <paramref name="current"/> in the ordered list of due unit ids (wrapping); the first when nothing is current or it is not in the list; null when empty.</summary>
    public static int? NextDueUnit(IReadOnlyList<int> dueUnitIds, int? current)
    {
        if (dueUnitIds.Count == 0) return null;
        var sorted = dueUnitIds.OrderBy(it => it).ToList();
        if (current is not { } id) return sorted[0];
        var next = sorted.FirstOrDefault(it => it > id, -1);
        return next >= 0 ? next : sorted[0];
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"+5" / "-3" / "+0": the per-turn value rounded half away from zero.</summary>
    private static string Signed(float value)
    {
        var rounded = (int)MathF.Round(value, MidpointRounding.AwayFromZero);
        return (rounded >= 0 ? "+" : "-") + Int(Math.Abs(rounded));
    }
}
