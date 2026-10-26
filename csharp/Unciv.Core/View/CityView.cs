using Unciv.Logic.Battle;
using Unciv.Logic.City;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/CityView.kt (T085). Stripped: the purity annotations.
// Kotlin's CityView re-declares the superclass's `protected open val city`; here the base class's City property serves.

/// <summary>
/// View of a <see cref="CityClass"/> from the perspective of the viewer's <see cref="GameView"/>. UI should use this and not city directly.
/// This should only be for cities we can see as if we own them - our cities, spied cities, or if we're spectator
/// </summary>
public class CityView(CityClass city, CivilizationClass viewer, bool spectatorMode, GameView gameView)
    : ForeignCityView(city, viewer, spectatorMode, gameView)
{
    // Navigation
    /// <summary>The viewing player's full CivView (always a self-view). For the city's owning civ, use <see cref="ForeignCityView.OwningCiv"/>.</summary>
    public CivView ViewingCiv() => GameView.CivView;

    /// <summary>Cities the viewer can page through in CityScreen: own cities normally, or spy-visited cities when spying.</summary>
    public List<CityView> GetViewableCities()
    {
        var isSpying = City.Civ != Viewer && Viewer.GameInfo.IsEspionageEnabled() && !Viewer.IsSpectator();
        return isSpying
            ? Viewer.EspionageManager.GetCitiesWithOurSpies()
                .Where(it => it.Civ != Viewer)
                .Select(it => GameView.GetCityView(it))
                .ToList()
            : City.Civ.Cities.Select(it => GameView.GetCityView(it)).ToList();
    }

    // Data retrieval
    public bool IsInRange(TileView tileView) => City.TilesInRange.Contains(tileView.Unwrap());

    public IEnumerable<TileView> GetTiles() => City.GetTiles().Select(it => GameView.TileMapView.GetTile(it));

    public int GetWorkRange() => City.GetWorkRange();

    public bool IsWorked(TileView tileView) => City.IsWorked(GetTile(tileView));

    public bool CanBuyTile(TileView tileView) => City.Expansion.CanBuyTile(GetTile(tileView));

    public int GetGoldCostOfTile(TileView tileView, int extraTiles = 0) => City.Expansion.GetGoldCostOfTile(GetTile(tileView), extraTiles);

    // Population
    public int GetFreePopulation() => City.Population.GetFreePopulation();

    public int GetFoodStored() => City.Population.FoodStored;

    public int GetFoodToNextPopulation() => City.Population.GetFoodToNextPopulation();

    public Counter<string> GetMaxSpecialists() => City.Population.GetMaxSpecialists();

    public Counter<string> GetNewSpecialists() => City.Population.GetNewSpecialists();

    public bool ManualSpecialists => City.ManualSpecialists;

    public int? GetNumTurnsToStarvation() => City.Population.GetNumTurnsToStarvation();

    public int? GetNumTurnsToNewPopulation() => City.Population.GetNumTurnsToNewPopulation();

    public int FoodForNextTurn() => City.FoodForNextTurn();

    public Stats GetStatsOfSpecialist(string specialistName) => City.CityStats.GetStatsOfSpecialist(specialistName);

    // City state
    public string DemandedResource => City.DemandedResource;

    public int GetFlag(CityFlags flag) => City.GetFlag(flag);

    public CityFocus GetCityFocus() => City.GetCityFocus();

    public bool AvoidGrowth => City.AvoidGrowth;

    public GameContext GetState() => City.State;

    // Stats
    public Stats GetCurrentCityStats() => City.CityStats.CurrentCityStats;

    public LinkedHashMap<string, float> GetHappinessList() => City.CityStats.HappinessList;

    public StatTreeNode GetBaseStatTree() => City.CityStats.BaseStatTree;

    public StatTreeNode GetStatPercentBonusTree() => City.CityStats.StatPercentBonusTree;

    public StatMap GetFinalStatList() => City.CityStats.FinalStatList;

    // Expansion
    public bool HasChoosableTiles() => City.Expansion.GetChoosableTiles().Any();

    public int GetCultureToNextTile() => City.Expansion.GetCultureToNextTile();

    public int GetCultureStored() => City.Expansion.CultureStored;

    // Constructions
    public CityConstructionsView Constructions => new(City.CityConstructions, GameView, Viewer, SpectatorMode);

    public IEnumerable<Building> GetBuiltBuildings() => City.CityConstructions.GetBuiltBuildings();

    public bool HasMatchingUnique(UniqueType uniqueType) => City.GetMatchingUniques(uniqueType).Any();

    public JavaHashSet<string> GetDisabledConstructions() => City.DisabledConstructions;

    public bool IsStatRelated(Stat stat, Building building) => building.IsStatRelated(stat, City);

    public string GetProductionTooltip(PerpetualConstruction construction) => construction.GetProductionTooltip(City);

    public Counter<string> GetResourceRequirementsPerTurn(IConstruction construction) =>
        construction is BaseUnit baseUnit
            ? baseUnit.GetResourceRequirementsPerTurn(City.Civ.State)
            : construction.GetResourceRequirementsPerTurn(City.State);

    public Counter<string> GetStockpiledResourceRequirements(IConstruction construction) =>
        construction.GetStockpiledResourceRequirements(City.State);

    public int GetConstructionProductionCost(INonPerpetualConstruction construction) => construction.GetProductionCost(City.Civ, City);

    public string GetUnitDescription(BaseUnit unit) => unit.GetDescription(City);

    public string GetBuildingDescription(Building building) => building.GetDescription(City, true);

    public int GetConversionRate(PerpetualConstruction.StatConversion statConversion) => statConversion.GetConversionRate(City);

    public int GetGoldForSellingBuilding(string buildingName) => City.GetGoldForSellingBuilding(buildingName);

    public bool HasSoldBuildingThisTurn() => City.HasSoldBuildingThisTurn;

    public bool IsGodModeEnabled() => City.Civ.GameInfo.GameParameters.GodMode;

    public bool? GetUnitShouldUseSavedPromotion(string baseUnit) => City.UnitShouldUseSavedPromotion.TryGetValue(baseUnit, out var value) ? value : null;

    public MapUnitView? GetGarrison()
    {
        var garrison = City.GetGarrison();
        return garrison == null ? null : GameView.GetMapUnitView(garrison);
    }

    public bool CanBeDestroyed() => City.CanBeDestroyed();

    public int GetExpandRange() => City.GetExpandRange();

    public TileView? ChooseNewTileToOwn()
    {
        var tile = City.Expansion.ChooseNewTileToOwn();
        return tile == null ? null : GameView.TileMapView.GetTile(tile);
    }

    public TileImprovement? GetImprovementToCreate(Building construction) => construction.GetImprovementToCreate(City.GetRuleset(), City.Civ);

    public bool HasFreeBuilding(Building building) => City.Civ.CivConstructions.HasFreeBuilding(City, building);

    // Resources/misc
    public Counter<string> GetResourceStockpiles() => City.ResourceStockpiles;

    public ResourceSupplyList GetCityResourcesAvailableToCity() => CityResources.GetCityResourcesAvailableToCity(City);

    public GreatPersonPointsBreakdown GetGreatPersonPointsBreakdown() => new(City);

    public Ruleset GetRuleset() => City.GetRuleset();

    public Stats GetBuildingStats(Building building) => building.GetStats(City);

    public int GetStatReserve(Stat stat) => City.GetStatReserve(stat);

    public bool CanBePurchasedWithAnyStat(INonPerpetualConstruction construction) => construction.CanBePurchasedWithAnyStat(City);

    public bool CanBePurchasedWithStat(INonPerpetualConstruction construction, Stat stat) => construction.CanBePurchasedWithStat(City, stat);

    public bool IsOwnedTile(TileView tileView) => ReferenceEquals(tileView.Unwrap().GetCity(), City);

    public Stats GetStatDiffForImprovement(TileView tileView, TileImprovement improvement) =>
        tileView.Unwrap().Stats.GetStatDiffForImprovement(improvement, City.Civ, City);

    private static Tile GetTile(TileView tileView) => tileView.Unwrap();

    // ACTIONS
    private bool CanChangeState() => City.Civ == Viewer && Viewer.IsActive();

    public bool TryLockTile(TileView tileView)
    {
        if (!CanChangeState()) return false;
        if (!IsWorked(tileView)) return false;
        return City.LockTile(GetTile(tileView));
    }

    public bool TryUnlockTile(TileView tileView)
    {
        if (!CanChangeState()) return false;
        return City.UnlockTile(GetTile(tileView));
    }

    public bool TryBuyTile(TileView tileView)
    {
        if (!CanChangeState()) return false;
        if (!City.Expansion.CanBuyTile(GetTile(tileView))) return false;
        City.Expansion.BuyTile(GetTile(tileView));
        return true;
    }

    public bool TryWorkTile(TileView tileView)
    {
        if (!CanChangeState()) return false;
        return City.WorkTile(GetTile(tileView));
    }

    public bool TryStopWorkingTile(TileView tileView)
    {
        if (!CanChangeState()) return false;
        return City.StopWorkingTile(GetTile(tileView));
    }

    public bool TryAddToQueue(string name)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.AddToQueue(name);
        return true;
    }

    public bool TryRemoveFromQueue(int index, bool automatic)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.RemoveFromQueue(index, automatic);
        return true;
    }

    public int? TryRaisePriority(int index)
    {
        if (!CanChangeState()) return null;
        return City.CityConstructions.RaisePriority(index);
    }

    public int? TryLowerPriority(int index)
    {
        if (!CanChangeState()) return null;
        return City.CityConstructions.LowerPriority(index);
    }

    // Note (Kotlin): CityStats being stateful breaks the "stateless" ideal of View API, think about this :/
    public bool UpdateTileStats()
    {
        City.CityStats.UpdateTileStats();
        return true;
    }

    // Note (Kotlin): CityStats being stateful breaks the "stateless" ideal of View API, think about this :/
    public bool UpdateCityStats()
    {
        City.CityStats.Update();
        return true;
    }

    public bool TryRenameCity(string name)
    {
        if (!CanChangeState()) return false;
        City.Name = name;
        return true;
    }

    public bool TryAnnexCity()
    {
        if (!CanChangeState()) return false;
        City.AnnexCity();
        return true;
    }

    public bool TrySetRazing(bool raze)
    {
        if (!CanChangeState()) return false;
        City.IsBeingRazed = raze;
        return true;
    }

    public bool TryAddToQueueWithTile(IConstruction construction, TileView tileView)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.AddToQueue(construction, tile: tileView.Unwrap());
        return true;
    }

    public bool TrySetUnitShouldUseSavedPromotion(string baseUnit, bool value)
    {
        if (!CanChangeState()) return false;
        City.UnitShouldUseSavedPromotion[baseUnit] = value;
        return true;
    }

    public bool TrySellBuilding(Building construction)
    {
        if (!CanChangeState()) return false;
        City.SellBuilding(construction);
        return true;
    }

    public bool TryMoveEntryToTop(int index)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.MoveEntryToTop(index);
        return true;
    }

    public bool TryMoveEntryToEnd(int index)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.MoveEntryToEnd(index);
        return true;
    }

    public bool TryAddToQueueConstruction(IConstruction construction, bool addToTop = false)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.AddToQueue(construction, addToTop: addToTop);
        return true;
    }

    public bool TryRemoveAllByName(string name)
    {
        if (!CanChangeState()) return false;
        City.CityConstructions.RemoveAllByName(name);
        return true;
    }

    public bool TryDisableConstruction(string name)
    {
        if (!CanChangeState()) return false;
        City.DisabledConstructions.Add(name);
        return true;
    }

    public bool TryEnableConstruction(string name)
    {
        if (!CanChangeState()) return false;
        City.DisabledConstructions.Remove(name);
        return true;
    }

    public bool TryReassignPopulation(bool resetLocked = false)
    {
        if (!CanChangeState()) return false;
        City.ReassignPopulation(resetLocked);
        return true;
    }

    public bool TryToggleAvoidGrowth()
    {
        if (!CanChangeState()) return false;
        City.AvoidGrowth = !City.AvoidGrowth;
        City.ReassignPopulation();
        return true;
    }

    public bool TryEnableManualSpecialists()
    {
        if (!CanChangeState()) return false;
        City.ManualSpecialists = true;
        return true;
    }

    public bool TryDisableManualSpecialists()
    {
        if (!CanChangeState()) return false;
        City.ManualSpecialists = false;
        City.ReassignPopulation();
        return true;
    }

    public bool TryAssignSpecialist(string specialistName)
    {
        if (!CanChangeState()) return false;
        City.Population.SpecialistAllocations.Add(specialistName, 1);
        City.ManualSpecialists = true;
        City.CityStats.Update();
        return true;
    }

    public bool TryUnassignSpecialist(string specialistName)
    {
        if (!CanChangeState()) return false;
        City.Population.SpecialistAllocations.Add(specialistName, -1);
        City.ManualSpecialists = true;
        City.CityStats.Update();
        return true;
    }

    public bool TrySetCityFocus(CityFocus focus)
    {
        if (!CanChangeState()) return false;
        City.SetCityFocus(focus);
        City.ReassignPopulation();
        return true;
    }

    public DamageDealt TryBombard(AttackableTileView attackableTileView) =>
        Battle.AttackOrNuke(new CityCombatant(City), attackableTileView.Unwrap());
}

// Helpers built on top of the view API, rather than being part of it
public static class CityViewExtensions
{
    public static bool IsStarving(this CityView view) => view.FoodForNextTurn() < 0;

    public static bool IsGrowing(this CityView view) => view.FoodForNextTurn() > 0;

    public static string CurrentConstructionName(this CityView view) => view.Constructions.CurrentConstructionName();
}
