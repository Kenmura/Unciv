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

    /// <summary>The visibility rule of Kotlin <c>VictoryScreenCivGroup</c>: name only for known, own, defeated civs (or when the viewer is defeated).</summary>
    public static VictoryCivRef Ref(Civilization viewer, Civilization civ) => new(
        viewer.Knows(civ) || ReferenceEquals(viewer, civ) || civ.IsDefeated() || viewer.IsDefeated() ? civ.CivName : null,
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
}
