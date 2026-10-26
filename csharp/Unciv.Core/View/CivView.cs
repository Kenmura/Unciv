using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/CivView.kt (T084). Stripped: the purity annotations.
// T085 added cities(), getUnits and the helpers idleUnitsCount, dueUnitsCount, getCapital, isHolyCityBlocked.

/// <summary>View of a <see cref="CivilizationClass"/> from the perspective of the viewer via the <see cref="GameView"/>.</summary>
public class CivView(CivilizationClass civ, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : ForeignCivView(civ, viewer, spectatorMode, gameView)
{
    // Navigation
    public override IReadOnlyList<CityView> Cities() => Civ.Cities.Select(it => GameView.GetCityView(it)).ToList();

    public TradeView GetTradeView(ForeignCivView otherCiv) => new(Civ, otherCiv.Unwrap(), GameView);

    // Data retrieval
    public bool HasStatToBuy(Stat stat, int price) => Civ.HasStatToBuy(stat, price);

    public bool CanSeeTile(TileView tileView) => tileView.Unwrap().IsVisible(Civ);

    public bool CanSeeResource(TileResource? resource) => Civ.CanSeeResource(resource);

    public bool CanBuildImprovementOn(TileImprovement improvement, TileView tileView) =>
        tileView.Unwrap().ImprovementFunctions.CanBuildImprovement(improvement, Civ.State);

    public IEnumerable<ImprovementBuildingProblem> GetImprovementBuildingProblems(TileImprovement improvement, TileView tileView) =>
        tileView.Unwrap().ImprovementFunctions.GetImprovementBuildingProblems(improvement, Civ.State);

    // Note from Kotlin: compare with getCivResourcesByName for overlap/conflict
    public int GetResourceAmount(string resourceName) => Civ.GetResourceAmount(resourceName);

    public Technology? TechnologyByName(string? name) => name == null ? null : Civ.GameInfo.Ruleset.Technologies.GetValueOrDefault(name);

    public bool HasUnique(UniqueType type) => Civ.HasUnique(type);

    public bool IsReligionEnabled() => Civ.GameInfo.IsReligionEnabled();

    public int GetGreatPersonPoints(string name) => Civ.GreatPeople.GreatPersonPointsCounter[name];

    public int GetPointsRequiredForGreatPerson(string name) => Civ.GreatPeople.GetPointsRequiredForGreatPerson(name);

    public Counter<string> GetGreatPersonPointsCounter() => Civ.GreatPeople.GreatPersonPointsCounter;

    public Counter<string> GetGreatPersonPointsForNextTurn() => Civ.GreatPeople.GetGreatPersonPointsForNextTurn();

    public Counter<string> GetGreatGeneralPointsCounter() => Civ.GreatPeople.GreatGeneralPointsCounter;

    public Counter<string> GetPointsForNextGreatGeneralCounter() => Civ.GreatPeople.PointsForNextGreatGeneralCounter;

    public bool IsCivConstructionDisabled(string name) => Civ.DisabledCityConstructions.Contains(name);

    public JavaHashMap<string, int> GetCivResourcesByName() => Civ.GetCivResourcesByName();

    public bool IsSpectator() => Civ.IsSpectator();

    /// <summary><c>true</c> when this civ is a human player defeated in a singleplayer game (T226: in a simultaneous game, any defeated human when the lobby allows it) - the map is fully revealed for them to watch the game play out.</summary>
    public bool IsMapRevealed() => Logic.TurnState.IsSimultaneousGame(Civ)
        ? Logic.TurnState.DefeatedSeesWholeMap(Civ)
        : !Civ.GameInfo.GameParameters.IsOnlineMultiplayer && Civ.IsCurrentPlayer() && Civ.IsDefeated();

    public bool HasExplored(TileView tileView) => Civ.HasExplored(tileView.Unwrap());

    public bool IsCurrentPlayer() => Civ.IsCurrentPlayer();

    /// <summary>Whether this civ may act now (sequential: the current player; simultaneous: any human still playing the round).</summary>
    public bool IsActive() => Civ.IsActive();

    public bool IsHuman() => Civ.IsHuman();

    public List<ForeignCivView> GetKnownCivs() => Civ.GetKnownCivs().Select(it => GameView.GetForeignCivView(it)).ToList();

    public float GetPersonalityValue(PersonalityValue value) => Civ.GetPersonality()[value];

    public int GetHappiness() => Civ.GetHappiness();

    // Tech
    public bool IsResearched(string techName) => Civ.Tech.IsResearched(techName);

    public string? CurrentTechnologyName() => Civ.Tech.CurrentTechnologyName();

    public string TurnsToTech(string techName) => Civ.Tech.TurnsToTech(techName);

    public bool CanResearchTech() => Civ.Tech.CanResearchTech();

    public bool HasResearchedAnyTech() => Civ.Tech.ResearchedTechnologies.Count != 0;

    public bool ShouldOpenTechPicker() => Civ.ShouldOpenTechPicker();

    // Policies
    public bool HasAdoptedPolicies() => Civ.Policies.AdoptedPolicies.Count != 0;

    /// <summary>Names of the adopted policies; private to the owner (T212).</summary>
    public List<string> GetAdoptedPolicies() => [.. Civ.Policies.AdoptedPolicies];

    public bool CanAdoptPolicy() => Civ.Policies.CanAdoptPolicy();

    public bool ShouldShowPolicyPicker() => Civ.Policies.ShouldShowPolicyPicker();

    // Espionage
    public bool ShouldShowMoveSpies() => Civ.EspionageManager.ShouldShowMoveSpies();

    // Religion
    public bool CanFoundPantheon() => Civ.ReligionManager.ReligionState != ReligionState.Pantheon && Civ.ReligionManager.CanFoundOrExpandPantheon();

    public bool CanExpandPantheon() => Civ.ReligionManager.ReligionState == ReligionState.Pantheon && Civ.ReligionManager.CanFoundOrExpandPantheon();

    public bool IsFoundingReligion() => Civ.ReligionManager.ReligionState == ReligionState.FoundingReligion;

    public bool IsEnhancingReligion() => Civ.ReligionManager.ReligionState == ReligionState.EnhancingReligion;

    public bool HasFreeBeliefs() => Civ.ReligionManager.HasFreeBeliefs();

    public Religion? GetReligion() => Civ.ReligionManager.Religion;

    public Counter<BeliefType> GetBeliefsToChooseAtFounding() => Civ.ReligionManager.GetBeliefsToChooseAtFounding();

    public Counter<BeliefType> GetBeliefsToChooseAtEnhancing() => Civ.ReligionManager.GetBeliefsToChooseAtEnhancing();

    public Counter<BeliefType> FreeBeliefsAsEnums() => Civ.ReligionManager.FreeBeliefsAsEnums();

    // Victory
    /// <summary>The victories this civilization can achieve - see <see cref="VictoryManager.GetAvailableVictories"/></summary>
    public List<Victory> GetAvailableVictories() => Civ.VictoryManager.GetAvailableVictories();

    /// <summary><see cref="GetAvailableVictories"/> minus the ones the ruleset hides from the victory screen</summary>
    public List<Victory> GetVictoriesShownInVictoryScreen() => Civ.VictoryManager.GetVictoriesShownInVictoryScreen();

    public bool MayVoteForDiplomaticVictory() => Civ.MayVoteForDiplomaticVictory();

    // Units
    public bool HasMovedAutomatedUnitsThisTurn() => Civ.HasMovedAutomatedUnits;

    /// <summary>The civ's own units, wrapped for the viewer's own use (e.g. movement-plan arrows).</summary>
    public List<MapUnitView> GetUnits() => Civ.Units.GetCivUnits().Select(it => GameView.GetMapUnitView(it)).ToList();

    public StatMap GetStatMapForNextTurn() => Civ.Stats.GetStatMapForNextTurn();

    public JavaHashMap<string, float> GetHappinessBreakdown() => Civ.Stats.GetHappinessBreakdown();

    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType) => Civ.GetMatchingUniques(uniqueType);

    public float GetGoldPercentConvertedToScience() => Civ.Tech.GoldPercentConvertedToScience;

    public JavaHashMap<string, double> CalculateScoreBreakdown() => Civ.CalculateScoreBreakdown();

    // Actions
    public bool TryDisableCivConstruction(string name)
    {
        foreach (var city in Civ.Cities) city.DisabledConstructions.Add(name);
        Civ.DisabledCityConstructions.Add(name);
        return true;
    }

    public bool TryEnableCivConstruction(string name)
    {
        foreach (var city in Civ.Cities) city.DisabledConstructions.Remove(name);
        Civ.DisabledCityConstructions.Remove(name);
        return true;
    }

    public bool TrySetGoldPercentConvertedToScience(float value)
    {
        Civ.Tech.GoldPercentConvertedToScience = value;
        return true;
    }

    public bool TryUpdateAllCityStats()
    {
        foreach (var city in Civ.Cities) city.CityStats.Update();
        return true;
    }

    public bool TryDismissPolicyPicker()
    {
        Civ.Policies.ShouldOpenPolicyPicker = false;
        return true;
    }

    public bool TryDismissMoveSpies()
    {
        Civ.EspionageManager.DismissedShouldMoveSpies = true;
        return true;
    }

    public bool TryMarkMovedAutomatedUnits()
    {
        Civ.HasMovedAutomatedUnits = true;
        return true;
    }

    public bool TryAutomateAllUnits()
    {
        foreach (var unit in Civ.Units.GetCivUnits()) unit.DoAction();
        return true;
    }
}

// Helpers built on top of the view API, rather than being part of it
// This is so we can separate the minimal API, from the helpful API.
public static class CivViewExtensions
{
    public static int IdleUnitsCount(this CivView civView, bool due) => civView.GetUnits().Count(it => it.IsIdle() && it.Due == due);

    public static int DueUnitsCount(this CivView civView) => civView.GetUnits().Count(it => it.Due && it.IsIdle());

    /// <summary><c>true</c> if we are at war with any known, non-defeated civ</summary>
    public static bool IsAtWar(this CivView civView) => civView.GetKnownCivs().Any(it => !it.IsDefeated() && civView.IsAtWarWith(it));

    /// <summary>Our capital city, or <c>null</c> if we have none</summary>
    public static CityView? GetCapital(this CivView civView) => civView.Cities().FirstOrDefault(it => it.IsCapital());

    /// <summary><c>true</c> if the holy city of our religion has been blocked by another religion's inquisitor, see <see cref="ForeignCityView.IsBlockedHolyCity"/></summary>
    public static bool IsHolyCityBlocked(this CivView civView)
    {
        var religionName = civView.GetReligion()?.Name;
        if (religionName == null) return false;
        return civView.Cities().Any(it => it.IsBlockedHolyCity() && it.GetReligionThisIsTheHolyCityOf() == religionName);
    }
}
