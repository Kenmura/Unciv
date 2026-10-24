using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using RulesetObject = Unciv.Models.Ruleset.Ruleset;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T065: stand-ins for the sub-objects of MapUnit (UnitMovement T067: an override of the real class) and a factory for real
// MapUnits over real Civilizations. The fakes hold data for what the tests read.

internal sealed class FakeUnitMovement(MapUnitClass unit) : UnitMovement(unit)
{
    public bool CanMoveToResult { get; set; } = true;
    public bool CanPassThroughResult { get; set; } = true;
    public CannotMoveToReason? CannotMoveReason { get; set; }
    public int PathfindingCacheCleared { get; private set; }
    public int Teleports { get; private set; }
    public List<TileClass> Reachable { get; } = [];
    public bool CanReachResult { get; set; } = true;
    public TileClass? HeadTowardsResult { get; set; }
    public List<TileClass> MovedTo { get; } = [];
    public Func<TileClass, bool>? CanMoveToFunc { get; set; }
    public override bool CanMoveTo(TileClass tile, bool assumeCanPassThrough = false, bool allowSwap = false, bool includeOtherEscortUnit = true) =>
        CanMoveToFunc?.Invoke(tile) ?? CanMoveToResult;
    public override bool CanPassThrough(TileClass tile, bool includeOtherEscortUnit = true) => CanPassThroughResult;
    public override CannotMoveToReason? GetCannotMoveToReason(TileClass tile, bool assumeCanPassThrough = false, bool allowSwap = false, bool includeOtherEscortUnit = true) =>
        CannotMoveReason;
    public override void TeleportToClosestMoveableTile() => Teleports++;
    public override void ClearPathfindingCache() => PathfindingCacheCleared++;
    public override PathsToTilesWithinTurn GetDistanceToTiles(bool considerZoneOfControl = true, List<bool?>? passThroughCacheNew = null,
        Dictionary<int, float>? movementCostCache = null, bool includeOtherEscortUnit = true)
    {
        var paths = new PathsToTilesWithinTurn();
        foreach (var tile in Reachable) paths[tile] = new ParentTileAndTotalMovement(tile, tile, 0f);
        return paths;
    }
    public override bool CanReach(TileClass destination) => CanReachResult;
    public override TileClass HeadTowards(TileClass destination) => HeadTowardsResult ?? destination;
    public Action<TileClass>? OnMoveTo { get; set; }
    public override void MoveToTile(TileClass destination, bool considerZoneOfControl = true)
    {
        MovedTo.Add(destination);
        OnMoveTo?.Invoke(destination);
    }
}

internal static class MapUnitFactory
{
    /// <summary>A real civilization over a <see cref="FakeGameInfoState"/> (shared through <paramref name="info"/> when given).</summary>
    public static Civilization NewCiv(string name, FakeGameInfoState? info = null, params string[] nationUniques)
    {
        info ??= new FakeGameInfoState();
        var civ = new Civilization(new NationObject { Name = name, Uniques = nationUniques.ToList() }) { CivId = name, GameInfo = info, CityStateFunctions = new FakeCityStateFunctions() };
        civ.Units = new TestUnitManager(civ);
        civ.State = new GameContext(civ);
        civ.Tech.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.GoldenAges.CivInfo = civ;
        civ.GreatPeople.CivInfo = civ;
        info.Civs.Add(civ);
        return civ;
    }

    internal static RulesetObject RulesetOf(Civilization civ) => ((IGameInfoState)civ.GameInfo).Ruleset;

    /// <summary>Registers the three movement domains as unit types "Land", "Water" and "Air".</summary>
    public static void AddUnitTypes(RulesetObject ruleset)
    {
        foreach (var domain in new[] { "Land", "Water", "Air" })
            ruleset.UnitTypes[domain] = new UnitType(domain, domain);
    }

    public static BaseUnit NewBaseUnit(RulesetObject ruleset, string name, string type = "Land", int movement = 2, int strength = 10,
        int ranged = 0, params string[] uniques)
    {
        AddUnitTypes(ruleset);
        var baseUnit = new BaseUnit { Name = name, UnitType = type, Movement = movement, Strength = strength, RangedStrength = ranged, Cost = 100, Uniques = uniques.ToList() };
        ruleset.Units[name] = baseUnit;
        baseUnit.SetRuleset(ruleset);
        return baseUnit;
    }

    /// <summary>A unit of <paramref name="civ"/> with fake sub-objects, unplaced unless <paramref name="tile"/> is given.</summary>
    public static MapUnitClass NewUnit(Civilization civ, BaseUnit baseUnit, TileClass? tile = null)
    {
        var unit = new MapUnitClass
        {
            Name = baseUnit.Name,
            Owner = civ.CivId,
            Civ = civ,
            BaseUnit = baseUnit,
            Promotions = new UnitPromotions(),
        };
        unit.Promotions.SetTransients(unit);
        unit.Movement = new FakeUnitMovement(unit);
        if (tile != null) unit.CurrentTile = tile;
        unit.UpdateUniques();
        return unit;
    }

    /// <summary>Convenience: a unit named <paramref name="name"/> of the movement domain <paramref name="type"/> (Land, Water, Air).</summary>
    public static MapUnitClass NewUnit(Civilization civ, string name, string type = "Land", TileClass? tile = null, params string[] uniques)
    {
        var ruleset = RulesetOf(civ);
        var baseUnit = ruleset.Units.TryGetValue(name, out var existing) ? existing : NewBaseUnit(ruleset, name, type, uniques: uniques);
        return NewUnit(civ, baseUnit, tile);
    }

    /// <summary>A real city at <paramref name="location"/> whose sub-objects are the data-holding fakes (see CityCoreTests).</summary>
    public static CityObject NewCity(Civilization civ, string name, HexCoord location)
    {
        civ.EspionageManager.SetTransients(civ);
        civ.CivConstructions.SetTransients(civ);
        var fake = new FakeCityState(name, new FakeCiv(civ.CivId)) { CivData = civ, LocationData = location };
        fake.City.SetTransients(civ);
        return fake.City;
    }

    public static FakeUnitMovement Move(this MapUnitClass unit) => (FakeUnitMovement)unit.Movement;

    /// <summary>Registers <paramref name="promotion"/> in the ruleset and gives it to the unit (without the effects of AddPromotion).</summary>
    public static void GivePromotion(this MapUnitClass unit, Promotion promotion)
    {
        RulesetOf(unit.Civ).UnitPromotions[promotion.Name] = promotion;
        unit.Promotions.Promotions.Add(promotion.Name);
    }
}
