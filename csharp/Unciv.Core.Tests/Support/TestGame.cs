using Unciv.Logic;
using Unciv.Logic.City.Managers;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnit = Unciv.Logic.Map.MapUnits.MapUnit;
using PolicyObject = Unciv.Models.Ruleset.Policy;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using TileResourceObject = Unciv.Models.Ruleset.Tile.TileResource;

namespace Unciv.Core.Tests.Support;

// Port of tests/src/com/unciv/testing/TestGame.kt (T035). Deviations:
// - UncivGame.Current / GameSettings (debug variables, the locale used by .tr(), volumes) do not exist in the headless engine, so the
//   constructor's `forUITesting` parameter and the ImageGetter.ruleset assignment are dropped.
// - RulesetCache is process-wide shared state: the Civ_V_GnK ruleset is loaded once from android/assets/jsons (noMods) and then cloned,
//   exactly like Kotlin's `if (RulesetCache.isEmpty()) loadRulesets(noMods = true)`.
// - QuestManager (T069) is not ported: each new Civilization gets a no-op stand-in while its lateinit is unset.
// - Kotlin's `Random.Default` for initCityState is a fixed-seed GameRandom (determinism; the value is ignored by the stand-in anyway).
/// <summary>
/// A testing game using a fresh clone of the Civ_V_GnK ruleset so it can be modded in-place.
/// By default it has a map consisting of a single tile. Starts with no civilizations, not even barbarians.
/// Initially <see cref="GameInfo.CurrentPlayerCiv"/> is an uninitialized dummy and not suitable for e.g. a nextTurn.
/// </summary>
public sealed class TestGame
{
    private static readonly object RulesetLoadLock = new();

    private int objectsCreated;
    public RulesetClass Ruleset { get; }
    public GameInfo GameInfo { get; } = new();

    /// <summary>[TileMap] has a default size of 1 tile, use <see cref="MakeHexagonalMap"/> or <see cref="MakeRectangularMap"/> to change that</summary>
    public TileMap TileMap => GameInfo.TileMap;

    public TestGame(params string[] addGlobalUniques) : this(BaseRuleset.Civ_V_GnK, addGlobalUniques)
    {
    }

    /// <summary>A game on another base ruleset, for example <see cref="BaseRuleset.Civ_V_BNW"/> (B013).</summary>
    public TestGame(BaseRuleset baseRuleset, params string[] addGlobalUniques)
    {
        // Create a new ruleset we can easily edit, and set the important variables of gameInfo
        lock (RulesetLoadLock)
        {
            if (RulesetCache.Count == 0)
            {
                var jsons = JsonsPath();
                RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
            }
            Ruleset = RulesetCache.Get(baseRuleset.FullName())!.Clone();
        }
        Ruleset.AddGlobalUniques(addGlobalUniques);

        GameInfo.Ruleset = Ruleset;
        GameInfo.Difficulty = "Prince";
        GameInfo.GameParameters.Speed = Speed.DefaultForSimulation;
        GameInfo.SetGlobalTransients();

        GameInfo.CurrentPlayerCiv = new Civilization(); // Will be uninitialized, do not build on for tests

        // Create a tilemap, needed for city centers
        GameInfo.TileMap = new TileMap(0, Ruleset, false);
        TileMap.MapParameters.MapSize = new MapSize(0);
        TileMap.Ruleset = Ruleset;
        TileMap.GameInfo = GameInfo;

        foreach (var baseUnit in Ruleset.Units.Values)
            baseUnit.SetRuleset(Ruleset);
    }

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    public void SetSpeed(string speed)
    {
        GameInfo.GameParameters.Speed = speed;
        GameInfo.SetGlobalTransients();
    }

    public void SetDifficulty(string difficulty)
    {
        GameInfo.Difficulty = difficulty;
        GameInfo.SetGlobalTransients();
    }

    /// <summary>
    /// Makes a new rectangular tileMap and sets it in gameInfo. Removes all existing tiles.
    /// Kotlin quirk kept: [baseTerrain] is applied to the tiles of the map being replaced, not to the new ones.
    /// </summary>
    public void MakeRectangularMap(int newHeight, int newWidth, string baseTerrain = Constants.Desert)
    {
        var newTileMap = new TileMap(newWidth, newHeight, Ruleset, TileMap.MapParameters.WorldWrap);
        newTileMap.MapParameters.MapSize = new MapSize(newWidth, newHeight);
        ReplaceMap(newTileMap, baseTerrain);
    }

    /// <summary>Makes a new hexagonal tileMap with radius [newRadius] and sets it in gameInfo. Same baseTerrain quirk as <see cref="MakeRectangularMap"/>.</summary>
    public void MakeHexagonalMap(int newRadius, string baseTerrain = Constants.Desert)
    {
        var newTileMap = new TileMap(newRadius, Ruleset, false);
        newTileMap.MapParameters.MapSize = new MapSize(newRadius);
        ReplaceMap(newTileMap, baseTerrain);
    }

    private void ReplaceMap(TileMap newTileMap, string baseTerrain)
    {
        foreach (var row in TileMap.TileMatrix)
            foreach (var tile in row)
                if (tile != null)
                    tile.BaseTerrain = baseTerrain;

        GameInfo.TileMap = newTileMap;
        TileMap.GameInfo = GameInfo;
    }

    public TileClass GetTile(HexCoord position) => TileMap[position];
    public TileClass GetTile(int x, int y) => TileMap[x, y];

    /// <summary>Sets the [terrain] and [features] of the tile at [position], and then returns it</summary>
    public TileClass SetTileTerrainAndFeatures(HexCoord position, string terrain, params string[] features)
    {
        SetTileTerrain(position, terrain);
        return SetTileFeatures(position, features);
    }

    public TileClass SetTileTerrain(HexCoord position, string terrain)
    {
        var tile = TileMap[position];
        tile.BaseTerrain = terrain;
        tile.SetTerrainTransients();
        return tile;
    }

    public TileClass SetTileFeatures(HexCoord position, params string[] features)
    {
        var tile = TileMap[position];
        tile.SetTerrainFeatures([]);
        foreach (var feature in features)
            tile.AddTerrainFeature(feature);
        tile.SetTerrainTransients();
        return tile;
    }

    public Civilization AddCiv(string[]? uniques = null, bool isPlayer = false, string? cityStateType = null)
    {
        var nation = CreateRulesetObject(Ruleset.Nations, uniques ?? [], () => new Nation
        {
            Cities = ["The Capital"],
            CityStateType = cityStateType,
        });
        return AddCiv(nation, isPlayer, cityStateType);
    }

    public Civilization AddCiv(Nation nation, bool isPlayer = false, string? cityStateType = null, bool useNationCityStateType = true)
    {
        if (useNationCityStateType && cityStateType == null) cityStateType = nation.CityStateType;
        var civInfo = new Civilization(nation) { GameInfo = GameInfo };
        if (isPlayer) civInfo.PlayerType = PlayerType.Human;
        civInfo.Cache.UpdateState();
        GameInfo.Civilizations.Add(civInfo);
        civInfo.SetTransients();

        // Add 1 tech to the player so the era is computed correctly
        civInfo.Tech.AddTechnology(Ruleset.Technologies.Values.MinBy(it => it.Era(), Comparer<string>.Create(string.CompareOrdinal))!.Name);
        if (cityStateType != null)
            civInfo.CityStateFunctions.InitCityState(Ruleset, "Ancient era", [], new GameRandom(0));
        return civInfo;
    }

    public Civilization AddBarbarianCiv()
    {
        var nation = new Nation { Name = Constants.Barbarians };
        var barbarianCivilization = new Civilization(nation) { GameInfo = GameInfo };
        barbarianCivilization.Cache.UpdateState();
        GameInfo.Civilizations.Add(barbarianCivilization);
        return barbarianCivilization;
    }

    public CityObject AddCity(Civilization civInfo, TileClass tile, bool replacePalace = false, int initialPopulation = 1)
    {
        var city = new CityFounder().FoundCity(civInfo, tile.Position);
        city.Population.AddPopulation(initialPopulation - 1);

        if (replacePalace && civInfo.Cities.Count == 1)
        {
            // Add a capital indicator without any other stats
            var palaceWithoutStats = CreateBuilding(UniqueType.IndicatesCapital.Text());
            city.CityConstructions.RemoveBuilding("Palace");
            city.CityConstructions.AddBuilding(palaceWithoutStats);
        }
        return city;
    }

    public void AddTileToCity(CityObject city, TileClass tile) => city.Tiles.Add(tile.Position);

    public MapUnit AddUnit(string name, Civilization civInfo, TileClass? tile)
    {
        var baseUnit = Ruleset.Units[name];
        baseUnit.SetRuleset(Ruleset);
        var mapUnit = baseUnit.NewMapUnit(civInfo);
        civInfo.Units.AddUnit(mapUnit);
        if (tile != null)
        {
            mapUnit.PutInTile(tile);
            mapUnit.CurrentMovement = mapUnit.GetMaxMovement();
        }
        return mapUnit;
    }

    public string CreateSpecialist()
    {
        var name = $"specialist-{objectsCreated++}";
        Ruleset.Specialists[name] = new Specialist();
        return name;
    }

    public Religion AddReligion(Civilization foundingCiv)
    {
        var religion = new Religion($"Religion-{objectsCreated++}", GameInfo, foundingCiv, GameInfo.Turns);
        foundingCiv.ReligionManager.Religion = religion;
        GameInfo.Religions[religion.Name] = religion;
        return religion;
    }

    private T CreateRulesetObject<T>(LinkedHashMap<string, T> rulesetCollection, string[] uniques, Func<T> factory) where T : class, IRulesetObject
    {
        var obj = factory();
        var name = $"{typeof(T).Name}-{objectsCreated++}";
        obj.Name = name;
        foreach (var unique in uniques) obj.Uniques.Add(unique);
        rulesetCollection.Put(name, obj);
        return obj;
    }

    public MapUnit AddDefaultMeleeUnitWithUniques(Civilization civInfo, TileClass tile, params string[] uniques)
    {
        var createBaseUnit = CreateBaseUnit("Sword", uniques);
        createBaseUnit.Movement = 2;
        createBaseUnit.Strength = 8;
        return AddUnit(createBaseUnit.Name, civInfo, tile);
    }

    public MapUnit AddDefaultRangedUnitWithUniques(Civilization civInfo, TileClass tile, params string[] uniques)
    {
        var createBaseUnit = CreateBaseUnit("Archery", uniques);
        createBaseUnit.Movement = 2;
        createBaseUnit.Strength = 5;
        createBaseUnit.RangedStrength = 7;
        createBaseUnit.Range = 2;
        return AddUnit(createBaseUnit.Name, civInfo, tile);
    }

    /// <summary>Kotlin default argument <c>unitType = createUnitType().name</c>: pass null to create a fresh unit type.</summary>
    public BaseUnit CreateBaseUnit(string? unitType = null, params string[] uniques)
    {
        var type = unitType ?? CreateUnitType().Name;
        var unit = CreateRulesetObject(Ruleset.Units, uniques, () => new BaseUnit { UnitType = type });
        unit.SetRuleset(GameInfo.Ruleset);
        return unit;
    }

    public Belief CreateBelief(BeliefType type = BeliefType.Any, params string[] uniques) =>
        CreateRulesetObject(Ruleset.Beliefs, uniques, () => new Belief(type));

    public Building CreateBuilding(params string[] uniques)
    {
        var building = CreateRulesetObject(Ruleset.Buildings, uniques, () => new Building());
        building.Ruleset = GameInfo.Ruleset;
        return building;
    }

    public TileResourceObject CreateResource(params string[] uniques) =>
        CreateRulesetObject(Ruleset.TileResources, uniques, () => new TileResourceObject());

    public Building CreateWonder(params string[] uniques)
    {
        var createdBuilding = CreateBuilding(uniques);
        createdBuilding.IsWonder = true;
        return createdBuilding;
    }

    /// <summary>
    /// Creates a new Policy. This is internally inconsistent as it has no branch, and thus only supports simple tests:
    /// do not run policyFilter on a TestGame with such a Policy. Use <see cref="CreatePolicyBranch"/> for a consistent state.
    /// </summary>
    public PolicyObject CreatePolicy(params string[] uniques) =>
        CreateRulesetObject(Ruleset.Policies, uniques, () => new PolicyObject());

    /// <summary>Creates a new PolicyBranch, optionally including [policy] as member Policy, and including its own "... complete" Policy.</summary>
    public PolicyBranch CreatePolicyBranch(string[]? uniques = null, PolicyObject? policy = null)
    {
        var branch = CreateRulesetObject(Ruleset.PolicyBranches, uniques ?? [], () => new PolicyBranch());
        branch.Branch = branch;
        Ruleset.Policies[branch.Name] = branch;
        if (policy != null)
        {
            policy.Branch = branch;
            branch.Policies.Add(policy);
        }
        var complete = new PolicyObject { Name = branch.Name + PolicyObject.BranchCompleteSuffix };
        complete.Branch = branch;
        branch.Policies.Add(complete);
        Ruleset.Policies[complete.Name] = complete;
        return branch;
    }

    public TileImprovement CreateTileImprovement(params string[] uniques) =>
        CreateRulesetObject(Ruleset.TileImprovements, uniques, () => new TileImprovement());

    public UnitType CreateUnitType(params string[] uniques) =>
        CreateRulesetObject(Ruleset.UnitTypes, uniques, () => new UnitType());

    public Promotion CreateUnitPromotion(params string[] uniques) =>
        CreateRulesetObject(Ruleset.UnitPromotions, uniques, () => new Promotion());
}
