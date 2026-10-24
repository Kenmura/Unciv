using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using RulesetObject = Unciv.Models.Ruleset.Ruleset;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T065: stand-ins for the sub-objects of MapUnit (UnitMovement T067, UnitPromotions/UnitUpgradeManager T066) and a factory for real
// MapUnits over real Civilizations. The fakes hold data for what the tests read.

internal sealed class FakeUnitPromotions : IUnitPromotions
{
    public JavaHashSet<string> Promotions { get; } = new();
    public List<Promotion> PromotionObjects { get; } = [];
    public List<(string Name, bool IsFree)> Added { get; } = [];
    public int NumberOfPromotions => Promotions.Count;
    public int XP { get; set; }
    public int XpForNextPromotion() => 10;
    public FakePromotionTree Tree { get; } = new();
    public IPromotionTree CreatePromotionTree() => Tree;
    public MapUnitClass? TransientsSetFor { get; private set; }
    public void AddPromotion(string promotionName, bool isFree = false)
    {
        Added.Add((promotionName, isFree));
        Promotions.Add(promotionName);
    }
    public IEnumerable<Promotion> GetPromotions(bool sorted = false) => PromotionObjects;
    public IUnitPromotions Clone(MapUnitClass unit)
    {
        var clone = new FakeUnitPromotions();
        foreach (var promotion in Promotions) clone.Promotions.Add(promotion);
        clone.PromotionObjects.AddRange(PromotionObjects);
        return clone;
    }
    public void SetTransients(MapUnitClass unit) => TransientsSetFor = unit;
}

internal sealed class FakePromotionTree : IPromotionTree
{
    public List<string> Possible { get; } = [];
    public List<string> Nodes { get; } = [];
    public IEnumerable<string> PossiblePromotionNames => Possible;
    public IEnumerable<string> AllNodePromotionNames() => Nodes;
}

internal sealed class FakeUnitMovement : IUnitMovement
{
    public bool CanMoveToResult { get; set; } = true;
    public bool CanPassThroughResult { get; set; } = true;
    public string CannotMoveReason { get; set; } = "";
    public int PathfindingCacheCleared { get; private set; }
    public int Teleports { get; private set; }
    public List<TileClass> Reachable { get; } = [];
    public bool CanReachResult { get; set; } = true;
    public TileClass? HeadTowardsResult { get; set; }
    public List<TileClass> MovedTo { get; } = [];
    public Func<TileClass, bool>? CanMoveToFunc { get; set; }
    public bool CanMoveTo(TileClass tile) => CanMoveToFunc?.Invoke(tile) ?? CanMoveToResult;
    public bool CanPassThrough(TileClass tile) => CanPassThroughResult;
    public string GetCannotMoveToReason(TileClass tile) => CannotMoveReason;
    public void TeleportToClosestMoveableTile() => Teleports++;
    public void ClearPathfindingCache() => PathfindingCacheCleared++;
    public IEnumerable<TileClass> GetDistanceToTilesKeys() => Reachable;
    public bool CanReach(TileClass destination) => CanReachResult;
    public TileClass HeadTowards(TileClass destination) => HeadTowardsResult ?? destination;
    public Action<TileClass>? OnMoveTo { get; set; }
    public void MoveToTile(TileClass destination)
    {
        MovedTo.Add(destination);
        OnMoveTo?.Invoke(destination);
    }
}

internal sealed class FakeUnitUpgradeManager : IUnitUpgradeManager;

internal static class MapUnitFactory
{
    /// <summary>A real civilization over a <see cref="FakeGameInfoState"/> (shared through <paramref name="info"/> when given).</summary>
    public static Civilization NewCiv(string name, FakeGameInfoState? info = null, params string[] nationUniques)
    {
        info ??= new FakeGameInfoState();
        var civ = new Civilization(new NationObject { Name = name, Uniques = nationUniques.ToList() }) { CivId = name, GameInfo = info, Units = new FakeUnitManager(), CityStateFunctions = new FakeCityStateFunctions() };
        civ.State = new GameContext(civ);
        civ.Tech.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.GoldenAges.CivInfo = civ;
        civ.GreatPeople.CivInfo = civ;
        info.Civs.Add(civ);
        return civ;
    }

    private static RulesetObject RulesetOf(Civilization civ) => ((IGameInfoState)civ.GameInfo).Ruleset;

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
            Promotions = new FakeUnitPromotions(),
            Movement = new FakeUnitMovement(),
            Upgrade = new FakeUnitUpgradeManager(),
        };
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
    public static FakeUnitPromotions Promo(this MapUnitClass unit) => (FakeUnitPromotions)unit.Promotions;
}
