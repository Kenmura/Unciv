using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/ForeignCivView.kt (T084). Stripped: the purity annotations.

/// <summary>Should contain information that should be knowable to us about foreign civilizations. Superclass of <see cref="CivView"/>.</summary>
public class ForeignCivView(CivilizationClass civ, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : OwnedView<CivilizationClass>(civ, viewer, spectatorMode, gameView)
{
    protected CivilizationClass Civ { get; } = civ;

    internal override CivilizationClass Owner() => Civ;

    public string CivName => Civ.CivName;

    public string CivId => Civ.CivId;

    public int Gold => Civ.Gold;

    public bool IsCityState => Civ.IsCityState;

    public Ruleset Ruleset { get; } = civ.GameInfo.Ruleset;

    // Navigation
    // TEMP - should be removed once migration ends
    public CivilizationClass GetCiv() => Civ;

    // Data retrieval
    public ImmutableColor GetOuterColor() => Civ.Nation.GetOuterColor();

    public ImmutableColor GetInnerColor() => Civ.Nation.GetInnerColor();

    public string GetStyle() => Civ.Nation.Style;

    public string GetStyleOrCivName() => Civ.Nation.GetStyleOrCivName();

    public Era GetEra() => Civ.GetEra();

    public bool IsAtWarWith(ForeignCivView other) => Civ.IsAtWarWith(other.Civ);

    public DiplomacyManagerView? GetDiplomacyManagerWith(ForeignCivView other)
    {
        var manager = Civ.GetDiplomacyManager(other.Civ);
        return manager == null ? null : GameView.GetDiplomacyManagerView(manager);
    }

    public bool IsBarbarian() => Civ.IsBarbarian;

    public bool IsMajorCiv() => Civ.IsMajorCiv();

    public bool IsDefeated() => Civ.IsDefeated();

    public virtual IReadOnlyList<ForeignCityView> Cities() =>
        Civ.Cities.Where(it => it.GetCenterTile().IsExplored(Viewer)).Select(it => GameView.GetForeignCityView(it)).ToList();

    public Nation GetNation() => Civ.Nation;

    public bool Knows(ForeignCivView other) => Civ.Knows(other.Civ);

    public List<ForeignCivView> GetCivsAtWarWith() => Civ.GetCivsAtWarWith().Select(it => GameView.GetForeignCivView(it)).ToList();

    public int GetStatForRanking(RankingType category) => Civ.GetStatForRanking(category);

    public int GetGoldPerTurn() => KotlinMath.FloatToInt(Civ.Stats.StatsForNextTurn.Gold);

    public ResourceSupplyList GetPerTurnResourcesWithOriginsForTrade() => Civ.GetPerTurnResourcesWithOriginsForTrade();

    public int GetResearchAgreementCost(ForeignCivView other) => Civ.DiplomacyFunctions.GetResearchAgreementCost(other.Civ);
}
