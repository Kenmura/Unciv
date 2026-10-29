using System.Globalization;
using Unciv.Queries.Empire;
using Unciv.UI.Screens.VictoryScreen;

namespace Unciv.Assets;

/// <summary>One figure of a rival's ribbon: <see cref="Text"/> is "?" when the game does not tell the viewer (<see cref="Known"/> false).</summary>
public sealed record RivalFigure(string Id, AssetKey Icon, string Text, string Tooltip, bool Known);

/// <summary>One rival on the ribbon. <see cref="Luxuries"/> is null when the engine would not say what they can trade, else the luxuries they could put on the table (maybe none).</summary>
public sealed record RivalRow(string CivName, bool AtWar, IReadOnlyList<RivalFigure> Figures, IReadOnlyList<string>? Luxuries);

/// <summary>
/// C087: the rival stats ribbon of the world HUD as plain data, Godot-free so it is unit-tested. It computes no game value and asks for nothing the visibility filter does
/// not already allow: the figures are the engine's rankings of each known major civ (present only when the game shows rankings, the same rule as the overview's rankings
/// page), the luxuries come from the trade screen's list of what a met civ could trade (<see cref="TradeOptionsResult.Theirs"/>), and everything else is the unknown
/// placeholder. Science is the number of technologies researched: a rival's science per turn is private (design/SERVER.md section 3).
/// </summary>
public static class RivalRibbonModel
{
    public const string Unknown = "?";

    /// <summary>Ranking type, figure id, icon and tooltip label (an existing translation key) in display order.</summary>
    private static readonly (RankingType Ranking, string Id, AssetKey Icon, string Label)[] Figures =
    [
        (RankingType.Technologies, "Science", AssetKey.Of(RulesetAssetKeys.StatIcon, "Science"), "Technologies"),
        (RankingType.Culture, "Culture", AssetKey.Of(RulesetAssetKeys.StatIcon, "Culture"), "Culture"),
        (RankingType.Gold, "Gold", AssetKey.Of(RulesetAssetKeys.StatIcon, "Gold"), "Gold"),
        (RankingType.Force, "Military", AssetKey.Of(RulesetAssetKeys.UiIcon, "Shield"), "Force"),
    ];

    /// <summary>The civs that get a row: the known major civs still in the game, in the overview's (name) order.</summary>
    public static IReadOnlyList<OverviewCiv> Rivals(EmpireOverviewResult? overview) =>
        overview == null ? [] : overview.KnownCivs.Where(it => !it.IsCityState && !it.IsDefeated).ToList();

    public static IReadOnlyList<RivalRow> Rows(EmpireOverviewResult? overview, IReadOnlyDictionary<string, TradeOptionsResult?> trade) =>
        Rivals(overview).Select(civ => new RivalRow(civ.CivName, civ.AtWar,
            Figures.Select(it => Figure(it, civ)).ToList(),
            trade.GetValueOrDefault(civ.CivName) is { } options ? Luxuries(options) : null)).ToList();

    /// <summary>The luxuries the rival could trade now, in the engine's order.</summary>
    public static IReadOnlyList<string> Luxuries(TradeOptionsResult options) =>
        options.Theirs.Where(it => it.Type == "Luxury_Resource" && it.Tradable).Select(it => it.Name).ToList();

    private static RivalFigure Figure((RankingType Ranking, string Id, AssetKey Icon, string Label) figure, OverviewCiv civ) =>
        civ.Rankings.TryGetValue(figure.Ranking.ToString(), out int value)
            ? new RivalFigure(figure.Id, figure.Icon, Compact(value), figure.Label, true)
            : new RivalFigure(figure.Id, figure.Icon, Unknown, figure.Label, false);

    /// <summary>Whole numbers up to 9999, then thousands with one decimal ("12.3k") so a column stays narrow.</summary>
    public static string Compact(int value)
    {
        if (value > -10000 && value < 10000) return value.ToString(CultureInfo.InvariantCulture);
        double thousands = Math.Truncate(value / 100.0) / 10.0;
        return thousands.ToString("0.#", CultureInfo.InvariantCulture) + "k";
    }
}
