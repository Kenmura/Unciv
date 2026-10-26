using Unciv.Logic.Civilization;
using Unciv.UI.Screens.VictoryScreen;

namespace Unciv.Queries.Victory;

// T198: error codes shared by the victory screen queries.
public static class VictoryQueryCodes
{
    public const string NotAvailable = "not-available";
    public const string BadRequest = "bad-request";
}

// One civilization as a victory screen row shows it. CivName is null for a civ the viewer has not met (Kotlin shows
// Constants.unknownNationName there), but its numbers are still listed, as the Kotlin screens do.
public sealed record VictoryCivRef(string? CivName, bool IsDefeated, bool IsViewer);

internal static class VictoryQueryHelpers
{
    /// <summary>Kotlin <c>VictoryScreen.canViewCivStats</c>.</summary>
    public static bool CanViewCivStats(IGameInfoState game, Civilization viewer, Civilization viewed) =>
        game.VictoryData != null
        || viewer.IsSpectator()
        || !game.GameParameters.HideOtherCivilizationStats
        || ReferenceEquals(viewed, viewer);

    /// <summary>The visibility rule of Kotlin <c>VictoryScreenCivGroup</c>: name only for known, own, defeated civs (or when the viewer is defeated, spectator, or winner).</summary>
    public static VictoryCivRef Ref(Civilization viewer, Civilization civ) => new(
        viewer.Knows(civ) || ReferenceEquals(viewer, civ) || civ.IsDefeated() || viewer.IsDefeated() || viewer.IsSpectator() || viewer.VictoryManager.HasWon() ? civ.CivName : null,
        civ.IsDefeated(), ReferenceEquals(viewer, civ));

    /// <summary>The tab-hiding rule of Kotlin <c>VictoryScreen.VictoryTabs</c> for the Demographics, Rankings and Charts tabs.</summary>
    public static void RequireTab(IGameInfoState game, Civilization viewer, bool tabSetting, string tab)
    {
        if (viewer.IsSpectator() || game.VictoryData != null) return;
        if (game.GameParameters.ShowCivilizationStats == true && tabSetting) return;
        throw new QueryException(VictoryQueryCodes.NotAvailable, $"{tab} are not shown in this game");
    }

    public static IEnumerable<Civilization> MajorCivs(IGameInfoState game) => game.Civilizations.Where(it => it.IsMajorCiv());

    public static IEnumerable<RankingType> Categories(IGameInfoState game) => RankingTypes.FilteredEntries(game.GameParameters);

    /// <summary>Value to show in rankings: live value for viewer/spectator/game-over, turn snapshot for other civs to prevent mid-turn probing (T241).</summary>
    public static int StatForRanking(Civilization viewer, Civilization civ, RankingType category)
    {
        if (ReferenceEquals(civ, viewer) || viewer.IsSpectator() || civ.GameInfo.VictoryData != null)
            return civ.GetStatForRanking(category);
        return StatFromHistory(civ, category);
    }

    /// <summary>This turn's snapshot, else the latest one, else the live value (T198, T241).</summary>
    public static int StatFromHistory(Civilization civ, RankingType category)
    {
        var history = civ.StatsHistory;
        IReadOnlyDictionary<RankingType, int>? snapshot = null;
        if (history.TryGetValue(civ.GameInfo.Turns, out var current)) snapshot = current;
        else if (history.Count > 0) snapshot = history.MaxBy(it => it.Key).Value;
        return snapshot != null && snapshot.TryGetValue(category, out int value) ? value : civ.GetStatForRanking(category);
    }
}
