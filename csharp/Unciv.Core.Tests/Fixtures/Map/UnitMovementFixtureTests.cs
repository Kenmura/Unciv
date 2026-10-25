using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T145 / T093: port of tests/src/com/unciv/logic/map/UnitMovementTests.kt over the real TestGame, all 25 Kotlin cases, for both parameter sets:
// UnitMovementFixtureTests (ClassicPathfinding) and UnitMovementAStarFixtureTests (AStarPathfinding, GameEngineOptions.UseAStarPathfinding).
public abstract class UnitMovementFixtureTestsBase
{
    private readonly TestGame testGame = new();
    private readonly TileClass tile;
    private readonly Civilization civInfo;

    protected UnitMovementFixtureTestsBase(bool useAStar)
    {
        testGame.GameInfo.EngineOptions.UseAStarPathfinding = useAStar;
        testGame.MakeHexagonalMap(2);
        tile = testGame.TileMap[0, 0];
        civInfo = testGame.AddCiv();
        foreach (var tech in testGame.Ruleset.Technologies.Keys)
            civInfo.Tech.TechsResearched.Add(tech);
        civInfo.Tech.EmbarkedUnitsCanEnterOcean = true;
        civInfo.Tech.UnitsCanEmbark = true;
    }

    private static void SetBase(TileClass target, string terrain)
    {
        target.BaseTerrain = terrain;
        target.SetTransients();
    }

    private MapUnitClass AddFakeUnit(UnitType unitType, IEnumerable<string>? uniques = null)
    {
        var baseUnit = new BaseUnit { UnitType = unitType.Name };
        if (uniques != null) baseUnit.Uniques.AddRange(uniques);
        baseUnit.SetRuleset(testGame.Ruleset);

        var unit = new MapUnitClass
        {
            Name = baseUnit.Name,
            Civ = civInfo,
            Owner = civInfo.CivId,
            BaseUnit = baseUnit,
        };
        unit.UpdateUniques();
        return unit;
    }

    private void MountainsAround(MapUnitClass unit, int exceptX, int exceptY)
    {
        foreach (var neighbor in unit.CurrentTile.Neighbors)
        {
            if (neighbor.Position == new HexCoord(exceptX, exceptY)) continue;
            SetBase(neighbor, Constants.Mountain);
        }
    }

    [Fact]
    public void CanPassThroughPassableTerrains()
    {
        var unit = testGame.AddUnit("Warrior", civInfo, null);
        foreach (var terrain in testGame.Ruleset.Terrains.Values)
        {
            tile.BaseTerrain = terrain.Name;
            tile.SetTerrainFeatures([]);
            tile.SetTransients();

            Assert.True(terrain.Impassable != unit.Movement.CanPassThrough(tile), terrain.Name);
        }
    }

    [Fact]
    public void AllUnitTypesCanEnterCity()
    {
        testGame.AddCity(civInfo, tile);

        foreach (var type in testGame.Ruleset.UnitTypes.Values)
        {
            var unit = AddFakeUnit(type);
            Assert.True(unit.Movement.CanPassThrough(tile), type.Name);
        }
    }

    [Fact]
    public void WaterUnitCanNOTEnterLand()
    {
        foreach (var terrain in testGame.Ruleset.Terrains.Values)
        {
            if (terrain.Impassable) continue;
            SetBase(tile, terrain.Name);

            foreach (var type in testGame.Ruleset.UnitTypes.Values)
            {
                var unit = AddFakeUnit(type);
                Assert.True((unit.BaseUnit.IsWaterUnit && tile.IsLand) != unit.Movement.CanPassThrough(tile),
                    $"{type.Name} cannot be at {terrain.Name}");
            }
        }
    }

    [Fact]
    public void CanNOTEnterIce()
    {
        tile.BaseTerrain = Constants.Ocean;
        tile.SetTerrainFeatures([Constants.Ice]);
        tile.SetTransients();

        foreach (var type in testGame.Ruleset.UnitTypes.Values)
        {
            var unit = AddFakeUnit(type);
            unit.UpdateUniques();

            Assert.True(
                unit.Movement.CanPassThrough(tile) == (
                    type.Uniques.Contains("Can enter ice tiles")
                    || type.Uniques.Contains("Can pass through impassable tiles")),
                $"{type.Name} cannot be in Ice");
        }
    }

    [Fact]
    public void CanNOTEnterNaturalWonder()
    {
        tile.BaseTerrain = Constants.Plains;
        tile.NaturalWonder = "Mount Fuji";
        tile.SetTransients();

        foreach (var type in testGame.Ruleset.UnitTypes.Values)
        {
            var unit = AddFakeUnit(type);
            Assert.True(unit.Movement.CanPassThrough(tile) == ((IHasUniques)type).HasUnique(UniqueType.CanPassImpassable),
                $"{type.Name} must not enter Wonder tile");
        }
    }

    [Fact]
    public void GermanEncampmentRecruitmentUsesFullHealth()
    {
        var germany = testGame.AddCiv(["When conquering an encampment, earn [25] Gold and recruit a Barbarian unit <with [100]% chance>"]);
        testGame.AddBarbarianCiv();
        testGame.GameInfo.Barbarians.SetTransients(testGame.GameInfo);
        var campTile = testGame.GetTile(0, 0);
        testGame.GameInfo.Barbarians.CreateNewCamp(campTile);
        var unit = testGame.AddUnit("Warrior", germany, campTile.Neighbors[0]);

        unit.Movement.MoveToTile(campTile);

        var recruitedUnit = germany.Units.GetCivUnits().Single(it => it != unit);
        Assert.Equal(100, recruitedUnit.Health);
    }

    [Fact]
    public void CanNOTEnterCoastUntilProperTechIsResearched()
    {
        civInfo.Tech.UnitsCanEmbark = false;
        SetBase(tile, Constants.Coast);

        foreach (var type in testGame.Ruleset.UnitTypes.Values)
        {
            var unit = AddFakeUnit(type);

            Assert.True(unit.BaseUnit.IsLandUnit != unit.Movement.CanPassThrough(tile), $"{type.Name} cannot be in Coast");
        }
    }

    [Fact]
    public void CanNOTEnterOceanUntilProperTechIsResearched()
    {
        civInfo.Tech.EmbarkedUnitsCanEnterOcean = false;
        SetBase(tile, Constants.Ocean);

        foreach (var type in testGame.Ruleset.UnitTypes.Values)
        {
            var unit = AddFakeUnit(type);

            Assert.True(unit.BaseUnit.IsLandUnit != unit.Movement.CanPassThrough(tile), $"{type.Name} cannot be in Ocean");
        }
    }

    [Fact]
    public void CanNOTEnterOceanWithLimitations()
    {
        SetBase(tile, Constants.Ocean);

        var unitType = testGame.Ruleset.UnitTypes.Values.First();
        var unit = AddFakeUnit(unitType, ["Cannot enter ocean tiles"]);

        Assert.False(unit.Movement.CanPassThrough(tile));

        var unitCanEnterAfterAstronomy = AddFakeUnit(unitType, ["Cannot enter ocean tiles <before discovering [Astronomy]>"]);
        Assert.True(unitCanEnterAfterAstronomy.Movement.CanPassThrough(tile));

        civInfo.Tech.TechsResearched.Remove("Astronomy");
        unitCanEnterAfterAstronomy.UpdateUniques();
        Assert.False(unitCanEnterAfterAstronomy.Movement.CanPassThrough(tile));
    }

    [Fact]
    public void CanNOTPassThroughTileWithEnemyUnits()
    {
        var barbNation = new Nation { Name = Constants.Barbarians }; // they are always enemies
        var barbCiv = new Civilization(barbNation) { GameInfo = testGame.GameInfo };
        barbCiv.Cache.UpdateState();

        testGame.GameInfo.Civilizations.Add(barbCiv);

        testGame.AddUnit("Warrior", barbCiv, tile);

        foreach (var type in testGame.Ruleset.UnitTypes.Values)
        {
            var outUnit = AddFakeUnit(type);
            Assert.False(outUnit.Movement.CanPassThrough(tile), $"{type.Name} must not enter occupied tile");
        }
    }

    [Fact]
    public void CanNOTPassForeignTiles()
    {
        var otherCiv = testGame.AddCiv();
        var city = testGame.AddCity(otherCiv, testGame.TileMap[1, 1]);
        tile.SetOwningCity(city);

        var unit = testGame.AddUnit("Warrior", civInfo, null);

        Assert.False(unit.Movement.CanPassThrough(tile), "Unit must not enter other civ tile");

        city.HasJustBeenConquered = true;
        civInfo.Diplomacy[otherCiv.CivName] = new DiplomacyManager(civInfo, otherCiv);
        civInfo.GetDiplomacyManager(otherCiv)!.DiplomaticStatus = DiplomaticStatus.War;

        Assert.True(unit.Movement.CanPassThrough(tile), "Unit can capture other civ city");
    }

    [Fact]
    public void CanTeleportLandUnit()
    {
        var unit = testGame.AddUnit("Warrior", civInfo, tile);

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        Assert.True(unit.CurrentTile != tile, "Unit must be teleported to new location");
        Assert.True(unit.CurrentTile.GetOwner() == null, "Unit must be teleported to tile outside of civ's control");
    }

    [Fact]
    public void CanTeleportWaterUnit()
    {
        testGame.MakeHexagonalMap(5);
        for (var i = 1; i <= 3; i++)
            SetBase(testGame.TileMap[1, i], Constants.Ocean);

        // 1,1 is within the radius of the new city, so it will be teleported away
        var unit = testGame.AddUnit("Frigate", civInfo, testGame.TileMap[1, 1]);

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        // Don't move him all the way to 1,3 - since there's a closer tile at 1,2
        Assert.True(unit.CurrentTile.Position == new HexCoord(1, 2), "Unit must be teleported to closest tile outside of civ's control");
    }

    [Fact]
    public void CanNOTTeleportWaterUnitOverTheLand()
    {
        testGame.MakeHexagonalMap(5);
        foreach (var i in new[] { 1, 3 }) // only water tiles are 1,1 and 1,3, which are non-contiguous
            SetBase(testGame.TileMap[1, i], Constants.Ocean);

        // 1,1 is within the radius of the new city, so it will be teleported away
        var unit = testGame.AddUnit("Frigate", civInfo, testGame.TileMap[1, 1]);

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        Assert.True(unit.IsDestroyed, "Unit must not be teleported but destroyed");
    }

    [Fact]
    public void ForcedDisplacementLeavesAnEnemyCivilianUncaptured()
    {
        testGame.MakeHexagonalMap(5);
        var unit = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        // The nearest otherwise-valid tile is 1,2. Every other neighbor is a mountain.
        MountainsAround(unit, 1, 2);

        // Place an enemy civilian unit on that tile
        var atWarCiv = testGame.AddCiv();
        atWarCiv.DiplomacyFunctions.MakeCivilizationsMeet(civInfo);
        atWarCiv.GetDiplomacyManager(civInfo)!.DeclareWar();
        var enemyWorkerUnit = testGame.AddUnit("Worker", atWarCiv, testGame.TileMap[1, 2]);
        var workerTile = enemyWorkerUnit.CurrentTile;

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        Assert.Same(atWarCiv, enemyWorkerUnit.Civ);
        Assert.Same(workerTile, enemyWorkerUnit.CurrentTile);
        Assert.False(unit.IsDestroyed, "Displaced unit takes another tile");
        Assert.False(unit.CurrentTile.Position == new HexCoord(1, 2));
        Assert.NotEqual(testGame.TileMap[1, 1], unit.CurrentTile);
    }

    [Fact]
    public void BarbarianDisplacementDoesNotCaptureAnEnemyCivilian()
    {
        testGame.MakeHexagonalMap(5);
        var barbarians = testGame.AddBarbarianCiv();
        var unit = testGame.AddUnit("Warrior", barbarians, testGame.TileMap[1, 1]);
        MountainsAround(unit, 1, 2);

        var worker = testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 2]);
        var workerTile = worker.CurrentTile;
        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        Assert.Same(civInfo, worker.Civ);
        Assert.Same(workerTile, worker.CurrentTile);
        Assert.False(unit.IsDestroyed);
        Assert.False(unit.CurrentTile.Position == new HexCoord(1, 2));
        Assert.Same(barbarians, unit.Civ);
    }

    [Fact]
    public void ForcedDisplacementStacksWithAnOwnCivilian()
    {
        testGame.MakeHexagonalMap(5);
        var unit = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        MountainsAround(unit, 1, 2);
        var worker = testGame.AddUnit("Worker", civInfo, testGame.TileMap[1, 2]);

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        Assert.Same(civInfo, worker.Civ);
        Assert.Same(testGame.TileMap[1, 2], worker.CurrentTile);
        Assert.Same(worker.CurrentTile, unit.CurrentTile);
        Assert.Same(unit, unit.CurrentTile.MilitaryUnit);
        Assert.Same(worker, unit.CurrentTile.CivilianUnit);
    }

    [Fact]
    public void ForcedDisplacementDoesNotStopOnAnotherCivsCivilian()
    {
        testGame.MakeHexagonalMap(5);
        var unit = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        MountainsAround(unit, 1, 2);

        var friend = testGame.AddCiv();
        friend.DiplomacyFunctions.MakeCivilizationsMeet(civInfo);
        civInfo.GetDiplomacyManager(friend)!.HasOpenBorders = true;
        friend.GetDiplomacyManager(civInfo)!.HasOpenBorders = true;
        var worker = testGame.AddUnit("Worker", friend, testGame.TileMap[1, 2]);
        var workerTile = worker.CurrentTile;

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        Assert.Same(friend, worker.Civ);
        Assert.Same(workerTile, worker.CurrentTile);
        Assert.False(unit.IsDestroyed, "Displaced unit takes another tile");
        Assert.NotEqual(worker.CurrentTile, unit.CurrentTile);
        Assert.False(unit.CurrentTile.Position == new HexCoord(1, 2));
    }

    [Fact]
    public void MilitaryMovementStillCapturesAnEnemyCivilian()
    {
        var warrior = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[0, 0]);
        var enemy = testGame.AddCiv();
        enemy.DiplomacyFunctions.MakeCivilizationsMeet(civInfo);
        enemy.GetDiplomacyManager(civInfo)!.DeclareWar();
        var worker = testGame.AddUnit("Worker", enemy, testGame.TileMap[1, 0]);

        warrior.Movement.MoveToTile(worker.CurrentTile);

        Assert.Same(civInfo, worker.Civ);
        Assert.Same(warrior.CurrentTile, worker.CurrentTile);
        Assert.Same(worker, warrior.CurrentTile.CivilianUnit);
    }

    [Fact]
    public void OwnCityFallbackSkipsAnEnemyCivilian()
    {
        testGame.MakeHexagonalMap(7);
        var ourCity = testGame.AddCity(civInfo, testGame.GetTile(0, 0));
        var origin = testGame.GetTile(6, 6);
        // Nothing within 4 tiles is enterable, so placement has to use our city.
        origin.ForEachTileInDistance(4, nearTile =>
        {
            if (nearTile == origin) return;
            SetBase(nearTile, Constants.Mountain);
        });

        var enemy = testGame.AddCiv();
        enemy.DiplomacyFunctions.MakeCivilizationsMeet(civInfo);
        enemy.GetDiplomacyManager(civInfo)!.DeclareWar();
        var infiltrator = testGame.CreateBaseUnit("Civilian", "May enter foreign tiles without open borders");
        var closestCityTile = ourCity.GetTiles().MinBy(it => it.AerialDistanceTo(origin))!;
        var enemyCivilian = testGame.AddUnit(infiltrator.Name, enemy, closestCityTile);
        Assert.Same(closestCityTile, enemyCivilian.CurrentTile);

        var unit = testGame.AddUnit("Warrior", civInfo, origin);
        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, origin);

        Assert.Same(enemy, enemyCivilian.Civ);
        Assert.Same(closestCityTile, enemyCivilian.CurrentTile);
        Assert.False(unit.IsDestroyed);
        Assert.NotEqual(closestCityTile, unit.CurrentTile);
        Assert.Same(civInfo, unit.CurrentTile.GetOwner());
    }

    [Fact]
    public void ForcedDisplacementDestroysTheUnitWhenTheOnlyCandidateHoldsAnEnemyCivilian()
    {
        testGame.MakeHexagonalMap(4);
        var origin = testGame.GetTile(0, 0);
        var onlyCandidate = origin.Neighbors[0];
        origin.ForEachTileInDistance(4, nearTile =>
        {
            if (nearTile == origin || nearTile == onlyCandidate) return;
            SetBase(nearTile, Constants.Mountain);
        });
        var enemy = testGame.AddCiv();
        enemy.DiplomacyFunctions.MakeCivilizationsMeet(civInfo);
        enemy.GetDiplomacyManager(civInfo)!.DeclareWar();
        var worker = testGame.AddUnit("Worker", enemy, onlyCandidate);

        var unit = testGame.AddUnit("Warrior", civInfo, origin);
        SetBase(origin, Constants.Mountain);
        unit.Movement.TeleportToClosestMoveableTile();

        Assert.True(unit.IsDestroyed);
        Assert.Same(enemy, worker.Civ);
        Assert.Same(onlyCandidate, worker.CurrentTile);
    }

    [Fact]
    public void CanTeleportTransportWithPayload()
    {
        testGame.MakeHexagonalMap(5);
        for (var i = 1; i <= 3; i++)
            SetBase(testGame.TileMap[1, i], Constants.Ocean);

        var unit = testGame.AddUnit("Carrier", civInfo, testGame.TileMap[1, 1]);
        var payload = testGame.AddUnit("Fighter", civInfo, testGame.TileMap[1, 1]);

        var otherCiv = testGame.AddCiv();
        testGame.AddCity(otherCiv, tile);

        // Don't move him all the way to 1,3 - since there's a closer tile at 1,2
        Assert.True(unit.CurrentTile.Position == new HexCoord(1, 2), "Unit must be teleported to closest tile outside of civ's control");
        Assert.True(unit.CurrentTile == payload.CurrentTile, "Payload must be teleported to the same tile");
    }

    [Fact]
    public void ParadroppingTransportKeepsPayload()
    {
        var origin = testGame.TileMap[0, 0];
        var destination = testGame.TileMap[1, 0];
        SetBase(origin, Constants.Coast);
        SetBase(destination, Constants.Coast);

        var transportBaseUnit = testGame.CreateBaseUnit(
            "Aircraft Carrier",
            "Can carry [2] [Aircraft] units",
            "May Paradrop to [Water] tiles up to [2] tiles away");
        transportBaseUnit.Movement = 2;
        transportBaseUnit.Strength = 1;
        var transport = testGame.AddUnit(transportBaseUnit.Name, civInfo, origin);
        // Freed from the carrier first, so the two real payloads can fill it to capacity below -
        // a full carrier is exactly the case that used to lose its payloads.
        var untransportedAirUnit = testGame.AddUnit("Fighter", civInfo, origin);
        untransportedAirUnit.IsTransported = false;
        var payload = testGame.AddUnit("Fighter", civInfo, origin);
        var secondPayload = testGame.AddUnit("Fighter", civInfo, origin);

        transport.Action = UnitActionType.Paradrop.Value();
        transport.Movement.MoveToTile(destination);

        Assert.Equal(destination, transport.CurrentTile);
        Assert.Equal(destination, payload.CurrentTile);
        Assert.Equal(destination, secondPayload.CurrentTile);
        Assert.True(payload.IsTransported);
        Assert.True(secondPayload.IsTransported);
        Assert.Equal(UnitMovementMemoryType.UnitTeleported, transport.MostRecentMoveType);
        Assert.Equal(UnitMovementMemoryType.UnitTeleported, payload.MostRecentMoveType);
        Assert.Equal(UnitMovementMemoryType.UnitTeleported, secondPayload.MostRecentMoveType);
        Assert.Equal(origin, untransportedAirUnit.CurrentTile);
        Assert.False(untransportedAirUnit.IsTransported);
    }

    private (MapUnitClass settler1, MapUnitClass settler2, MapUnitClass warrior1, MapUnitClass warrior2) EscortPairs()
    {
        var settler1 = testGame.AddUnit("Settler", civInfo, testGame.TileMap[1, 1]);
        var settler2 = testGame.AddUnit("Settler", civInfo, testGame.TileMap[2, 2]);
        var warrior1 = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[1, 1]);
        var warrior2 = testGame.AddUnit("Warrior", civInfo, testGame.TileMap[2, 2]);
        warrior1.StartEscorting();
        warrior2.StartEscorting();
        Assert.Equal(warrior1, settler1.GetOtherEscortUnit());
        Assert.Equal(warrior2, settler2.GetOtherEscortUnit());
        return (settler1, settler2, warrior1, warrior2);
    }

    private void AssertSwapped(MapUnitClass settler1, MapUnitClass settler2, MapUnitClass warrior1, MapUnitClass warrior2)
    {
        Assert.Equal(testGame.TileMap[2, 2], warrior1.CurrentTile);
        Assert.Equal(testGame.TileMap[1, 1], settler1.CurrentTile);
        Assert.Equal(testGame.TileMap[1, 1], warrior2.CurrentTile);
        Assert.Equal(testGame.TileMap[2, 2], settler2.CurrentTile);
        Assert.Equal(warrior1, settler2.GetOtherEscortUnit());
        Assert.Equal(warrior2, settler1.GetOtherEscortUnit());
    }

    [Fact]
    public void TwoEscortsCanSwap()
    {
        var (settler1, settler2, warrior1, warrior2) = EscortPairs();

        Assert.True(warrior1.Movement.CanUnitSwapTo(testGame.TileMap[2, 2]));
        Assert.True(warrior2.Movement.CanUnitSwapTo(testGame.TileMap[1, 1]));

        warrior1.Movement.SwapMoveToTile(testGame.TileMap[2, 2]);

        AssertSwapped(settler1, settler2, warrior1, warrior2);
    }

    [Fact]
    public void TwoEscortsCanSwapEvenIfSettlerHasNoMovement()
    {
        var (settler1, settler2, warrior1, warrior2) = EscortPairs();
        settler1.CurrentMovement = 0f;

        Assert.False(warrior1.Movement.CanReachInCurrentTurn(testGame.TileMap[2, 2]));
        Assert.True(warrior2.Movement.CanReachInCurrentTurn(testGame.TileMap[1, 1]));
        Assert.False(settler1.Movement.CanReachInCurrentTurn(testGame.TileMap[2, 2]));
        Assert.True(settler2.Movement.CanReachInCurrentTurn(testGame.TileMap[1, 1]));
        Assert.True(warrior1.Movement.CanMoveTo(testGame.TileMap[2, 2], allowSwap: true));
        Assert.True(warrior2.Movement.CanMoveTo(testGame.TileMap[1, 1], allowSwap: true));
        Assert.True(settler1.Movement.CanMoveTo(testGame.TileMap[2, 2], allowSwap: true));
        Assert.True(settler2.Movement.CanMoveTo(testGame.TileMap[1, 1], allowSwap: true));
        Assert.False(warrior1.Movement.CanUnitSwapTo(testGame.TileMap[2, 2]));
        Assert.False(warrior2.Movement.CanUnitSwapTo(testGame.TileMap[1, 1]));
        Assert.False(settler1.Movement.CanUnitSwapTo(testGame.TileMap[2, 2]));
        Assert.False(settler2.Movement.CanUnitSwapTo(testGame.TileMap[1, 1]));

        warrior1.Movement.SwapMoveToTile(testGame.TileMap[2, 2]);

        AssertSwapped(settler1, settler2, warrior1, warrior2);
    }
}

[Collection("RulesetCache")]
public sealed class UnitMovementFixtureTests() : UnitMovementFixtureTestsBase(false);

[Collection("RulesetCache")]
public sealed class UnitMovementAStarFixtureTests() : UnitMovementFixtureTestsBase(true);
