using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using CityObject = Unciv.Logic.City.City;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T141: port of tests/src/com/unciv/logic/map/TileImprovementConstructionTests.kt over the real TestGame.
// Deferred to the follow-up ticket named in T141 Notes (needs Tile.startWorkingOnImprovement, ported in T139, tests in T148): stockpiledImprovementCostIsChargedOnlyWhenAffordable
// and nonStockpileImprovementCostsDoNotChargeTheStockpile.
[Collection("RulesetCache")]
public sealed class TileImprovementConstructionFixtureTests
{
    private readonly TestGame testGame = new();
    private Civilization civInfo;
    private readonly TileMap tileMap;
    private readonly CityObject city;

    public TileImprovementConstructionFixtureTests()
    {
        testGame.MakeHexagonalMap(4);
        tileMap = testGame.TileMap;
        civInfo = testGame.AddCiv();
        foreach (var tech in testGame.Ruleset.Technologies.Values)
            civInfo.Tech.AddTechnology(tech.Name);
        city = testGame.AddCity(civInfo, tileMap[0, 0]);
    }

    private void SwitchToUniqueCiv(string uniqueTo)
    {
        civInfo = testGame.AddCiv([uniqueTo]);
        foreach (var tech in testGame.Ruleset.Technologies.Values)
            civInfo.Tech.AddTechnology(tech.Name);
        city.Civ = civInfo;
        city.Id = city.Id != CityObject.NoId ? city.Id : CityObject.PseudoRandomId(civInfo);
    }

    [Fact]
    public void AllTerrainSpecificImprovementsCanBeBuilt()
    {
        foreach (var improvement in testGame.Ruleset.TileImprovements.Values)
        {
            var terrain = improvement.TerrainsCanBeBuiltOn.FirstOrDefault();
            if (terrain == null) continue;
            if (terrain == "Land") terrain = testGame.Ruleset.Terrains.Values.First(it => it.Type == TerrainType.Land).Name;
            if (terrain == "Water") terrain = testGame.Ruleset.Terrains.Values.First(it => it.Type == TerrainType.Water).Name;
            // If this improvement requires additional conditions to be true,
            // its too complex to handle all of them, so just skip it and hope its fine
            if (((IHasUniques)improvement).HasUnique(UniqueType.CanOnlyBeBuiltOnTile, GameContext.IgnoreConditionalsState)) continue;
            if (((IHasUniques)improvement).HasUnique(UniqueType.Unbuildable, GameContext.IgnoreConditionalsState)) continue;

            var tile = tileMap[1, 1];
            tile.BaseTerrain = terrain;
            tile.TileResource = null;
            if (((IHasUniques)improvement).HasUnique(UniqueType.CanOnlyImproveResource, GameContext.IgnoreConditionalsState))
            {
                var resource = testGame.Ruleset.TileResources.Values.FirstOrDefault(it => it.IsImprovedBy(improvement.Name));
                if (resource == null) continue;
                tile.TileResource = resource;
            }
            tile.SetTransients();

            if (improvement.UniqueTo != null) SwitchToUniqueCiv(improvement.UniqueTo);

            var canBeBuilt = tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
            Assert.True(canBeBuilt, improvement.Name);
        }
    }

    [Fact]
    public void AllResourceImprovementsCanBeBuilt() => TestResourceImprovements(wrongDomain: false);

    [Fact]
    public void ResourceImprovementsOnWrongDomainCannotBeBuilt() => TestResourceImprovements(wrongDomain: true);

    private void TestResourceImprovements(bool wrongDomain)
    {
        var landTile = tileMap[1, 1];
        var coastTile = tileMap[1, 2];
        coastTile.BaseTerrain = Constants.Coast;
        coastTile.SetTransients();
        coastTile.SetOwningCity(city); // Yes ownership is tested

        bool TestImprovement(TileImprovement improvement, TileResource tileResource, TileClass tile)
        {
            tile.TileResource = tileResource;
            tile.SetTransients();
            return tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
        }

        bool TestPair(TileImprovement improvement, TileResource tileResource)
        {
            // This relies on our builtin rulesets not using more complex filter in these uniques
            var landOk = landTile.ImprovementFunctions.ExtendedDomainCheck(improvement)
                && ((IHasUniques)improvement).GetMatchingUniques(UniqueType.CanOnlyBeBuiltOnTile).All(it => it.Params[0] == "Land")
                && !((IHasUniques)improvement).GetMatchingUniques(UniqueType.CannotBuildOnTile).Any(it => it.Params[0] == "Land");
            var coastOk = coastTile.ImprovementFunctions.ExtendedDomainCheck(improvement)
                && ((IHasUniques)improvement).GetMatchingUniques(UniqueType.CanOnlyBeBuiltOnTile).All(it => it.Params[0] == "Water")
                && !((IHasUniques)improvement).GetMatchingUniques(UniqueType.CannotBuildOnTile).Any(it => it.Params[0] == "Water");
            if (wrongDomain && landOk && coastOk) return false; // Can't test wrong domain as the improvement has CanOnlyImproveResource which always ignores domain
            if (landOk != wrongDomain && !TestImprovement(improvement, tileResource, landTile)) return false;
            if (coastOk != wrongDomain && !TestImprovement(improvement, tileResource, coastTile)) return false;
            return true;
        }

        // Test all combinations - e.g. both Oil well and Offshore Platform for Oil
        var items = testGame.Ruleset.TileImprovements.Values
            .SelectMany(improvement => testGame.Ruleset.TileResources.Values
                .Where(it => it.IsImprovedBy(improvement.Name))
                .Select(resource => (improvement, resource)))
            .ToList();
        Assert.NotEmpty(items);

        // Kotlin runTestParcours: every case is run and the failures are reported together
        var failures = items.Where(it => TestPair(it.improvement, it.resource) == wrongDomain)
            .Select(it => $"{it.improvement.Name} / {it.resource.Name}").ToList();
        Assert.True(failures.Count == 0, "Improvements improving a resource " + (wrongDomain ? "cannot" : "can") + " be built on that resource as expected: failed " + string.Join(", ", failures));
    }

    [Fact]
    public void CoastalImprovementsCanBeBuilt()
    {
        var coastTile = tileMap[1, 2];
        coastTile.BaseTerrain = Constants.Coast;
        coastTile.SetTransients();

        var coastalTile = tileMap[1, 1];

        foreach (var improvement in testGame.Ruleset.TileImprovements.Values)
        {
            if (!improvement.Uniques.Contains("Can only be built on [Coastal] tiles")) continue;
            if (improvement.UniqueTo != null) SwitchToUniqueCiv(improvement.UniqueTo);
            var canBeBuilt = coastalTile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
            Assert.True(canBeBuilt, improvement.Name);
        }
    }

    [Fact]
    public void CoastalImprovementsCanNOTBeBuiltInland()
    {
        var tile = tileMap[1, 1];

        foreach (var improvement in testGame.Ruleset.TileImprovements.Values)
        {
            if (!improvement.Uniques.Contains("Can only be built on [Coastal] tiles")) continue;
            civInfo.SetNameForUnitTests(improvement.UniqueTo ?? "OtherCiv");
            var canBeBuilt = tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
            Assert.False(canBeBuilt, improvement.Name);
        }
    }

    [Fact]
    public void UniqueToOtherImprovementsCanNOTBeBuilt()
    {
        foreach (var improvement in testGame.Ruleset.TileImprovements.Values)
        {
            if (improvement.UniqueTo == null) continue;
            civInfo.SetNameForUnitTests("OtherCiv");
            var tile = tileMap[1, 1];
            var canBeBuilt = tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
            Assert.False(canBeBuilt, improvement.Name);
        }
    }

    [Fact]
    public void ImprovementsCanNOTBeBuiltOnWrongResource()
    {
        foreach (var resource in testGame.Ruleset.TileResources.Values)
        {
            if (resource.GetImprovements().Count == 0) continue;
            var improvement = testGame.Ruleset.TileImprovements[resource.GetImprovements().First()];
            if (!((IHasUniques)improvement).HasUnique(UniqueType.CanOnlyImproveResource)) continue;
            var wrongResource = testGame.Ruleset.TileResources.Values.FirstOrDefault(it => it != resource && !it.IsImprovedBy(improvement.Name));
            if (wrongResource == null) continue;
            var tile = tileMap[1, 1];
            tile.BaseTerrain = "Plains";
            tile.TileResource = wrongResource;
            tile.SetTransients();
            var canBeBuilt = tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
            Assert.False(canBeBuilt, improvement.Name);
        }
    }

    [Fact]
    public void BuildingGreatImprovementRemovesFeatures()
    {
        var tile = tileMap[1, 1];
        tile.BaseTerrain = "Plains";
        tile.AddTerrainFeature("Hill");
        tile.AddTerrainFeature("Forest");
        Assert.Equal(["Hill", "Forest"], tile.TerrainFeatures);

        tile.ImprovementFunctions.SetImprovement("Landmark");
        Assert.Equal(["Hill"], tile.TerrainFeatures);
    }

    [Fact]
    public void BuildingImprovementWithCivAppliesTerrainChange()
    {
        var tile = tileMap[1, 1];
        var improvement = testGame.CreateTileImprovement("Turn this tile into a [Coast] tile");

        tile.ImprovementFunctions.SetImprovement(improvement, civInfo);

        Assert.Equal("Coast", tile.BaseTerrain);
    }

    [Fact]
    public void CitadelTakesOverAdjacentTiles()
    {
        var tile = tileMap[1, 1];
        Assert.False(tile.Neighbors.All(it => it.OwningCity == city));
        tile.ImprovementFunctions.SetImprovement("Citadel", civInfo);
        Assert.True(tile.Neighbors.All(it => it.OwningCity == city));
    }

    [Fact]
    public void TerraceFarmCanNOTBeBuiltOnBonus()
    {
        var tile = tileMap[1, 1];
        tile.SetTileResource("Sheep");
        tile.SetTransients();
        tile.AddTerrainFeature("Hill");
        civInfo.SetNameForUnitTests("Inca");

        foreach (var improvement in testGame.Ruleset.TileImprovements.Values)
        {
            if (!improvement.Uniques.Contains("Cannot be built on [Bonus resource] tiles")) continue;
            var canBeBuilt = tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State);
            Assert.False(canBeBuilt, improvement.Name);
        }
    }

    [Fact]
    public void BuildingRoadBuildsARoad()
    {
        var tile = tileMap[1, 1];
        tile.ImprovementFunctions.SetImprovement("Road");
        Assert.True(tile.RoadStatus == RoadStatus.Road);
    }

    [Fact]
    public void RemovingRoadRemovesRoad()
    {
        var tile = tileMap[1, 1];
        tile.RoadStatus = RoadStatus.Road;
        tile.ImprovementFunctions.SetImprovement("Remove Road");
        Assert.True(tile.RoadStatus == RoadStatus.None);
    }

    [Fact]
    public void RemovingForestRemovesForestAndLumbermill()
    {
        var tile = tileMap[1, 1];
        tile.AddTerrainFeature("Forest");
        tile.ImprovementFunctions.SetImprovement("Lumber mill");
        Assert.True(tile.TileImprovement!.Name == "Lumber mill");
        tile.ImprovementFunctions.SetImprovement("Remove Forest");
        Assert.Empty(tile.TerrainFeatures);
        Assert.Null(tile.Improvement); // Lumber mill can ONLY be on Forest, and is therefore removed
    }

    [Fact]
    public void RemovingForestRemovesForestButNotCamp()
    {
        var tile = tileMap[1, 1];
        tile.AddTerrainFeature("Forest");
        tile.SetTileResource("Deer");
        tile.BaseTerrain = "Plains";
        tile.ImprovementFunctions.SetImprovement("Camp");
        Assert.True(tile.TileImprovement!.Name == "Camp");
        tile.ImprovementFunctions.SetImprovement("Remove Forest");
        Assert.Empty(tile.TerrainFeatures);
        Assert.Equal("Camp", tile.Improvement); // Camp can be both on Forest AND on Plains, so not removed
    }

    [Fact]
    public void ImprovementCannotBuildWhenNotAllowed()
    {
        var tile = tileMap[1, 1];
        tile.BaseTerrain = "Grassland";
        tile.AddTerrainFeature("Forest");

        var improvement = testGame.CreateTileImprovement();
        Assert.False(tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State), "Forest doesn't allow building unless allowed");

        var allowedImprovement = testGame.CreateTileImprovement();
        allowedImprovement.TerrainsCanBeBuiltOn = [.. allowedImprovement.TerrainsCanBeBuiltOn, "Forest"];
        Assert.True(tile.ImprovementFunctions.CanBuildImprovement(allowedImprovement, civInfo.State), "Forest should allow building when allowed");
        tile.ImprovementFunctions.SetImprovement(allowedImprovement);
        Assert.True(tile.TileImprovement == allowedImprovement);
        Assert.True(tile.TerrainFeatures.Contains("Forest"), "Forest should not be removed with this improvement");
    }

    [Fact]
    public void ImprovementDoesntNeedRemovalCanBuildHere()
    {
        var tile = tileMap[1, 1];
        tile.BaseTerrain = "Grassland";
        tile.AddTerrainFeature("Forest");

        var improvement = testGame.CreateTileImprovement("Does not need removal of [Forest]");
        Assert.True(tile.ImprovementFunctions.CanBuildImprovement(improvement, civInfo.State));
        tile.ImprovementFunctions.SetImprovement(improvement);
        Assert.True(tile.TileImprovement == improvement);
        Assert.True(tile.TerrainFeatures.Contains("Forest"), "Forest should not be removed with this improvement");
    }

    [Fact]
    public void StatsDiffFromRemovingForestTakesRemovedLumberMillIntoAccount()
    {
        var tile = tileMap[1, 1];
        tile.BaseTerrain = "Grassland";
        tile.AddTerrainFeature("Forest");

        var lumberMill = testGame.Ruleset.TileImprovements["Lumber mill"];
        tile.ImprovementFunctions.SetImprovement(lumberMill);
        Assert.True(tile.TileImprovement == lumberMill);

        // 1f 1p from forest, 2p from lumber mill since all techs are researched
        var tileStats = tile.Stats.GetTileStats(civInfo);
        Assert.True(tileStats.Equals(new Stats(production: 3f, food: 1f)));

        var statsDiff = tile.Stats.GetStatDiffForImprovement(testGame.Ruleset.TileImprovements["Remove Forest"], civInfo, null);

        // We'll be reverting back to grassland stats - 2f only
        Assert.True(statsDiff.Equals(new Stats(food: +1f, production: -3f)));
    }

    // ---- road maintenance

    private sealed record RoadMaintenanceSetup(TestGame Game, Civilization Civ, CityObject City);

    private static RoadMaintenanceSetup RoadMaintenanceGame(string? roadUnique = null, string? railroadUnique = null, bool isPlayer = false)
    {
        var game = new TestGame();
        if (roadUnique != null) ReplaceImprovementMaintenance(game.Ruleset, RoadStatus.Road.ToString(), roadUnique);
        if (railroadUnique != null) ReplaceImprovementMaintenance(game.Ruleset, RoadStatus.Railroad.ToString(), railroadUnique);
        game.MakeHexagonalMap(3);
        var civ = game.AddCiv(isPlayer: isPlayer);
        civ.Tech.AddTechnology("The Wheel", false);
        var city = game.AddCity(civ, game.GetTile(HexCoord.Zero));
        return new RoadMaintenanceSetup(game, civ, city);
    }

    private static void ReplaceImprovementMaintenance(Ruleset ruleset, string improvementName, string unique)
    {
        var original = ruleset.TileImprovements[improvementName];
        var replacement = new TileImprovement
        {
            Name = original.Name,
            TerrainsCanBeBuiltOn = original.TerrainsCanBeBuiltOn,
            TurnsToBuild = original.TurnsToBuild,
            TechRequired = original.TechRequired,
        };
        replacement.Uniques.AddRange(original.Uniques.Where(it => !it.StartsWith("Costs [", StringComparison.Ordinal)));
        replacement.Uniques.Add(unique);
        ruleset.TileImprovements.Put(improvementName, replacement);
    }

    private static TileClass OwnedNonCenterTile(CityObject city) => city.GetCenterTile().Neighbors.First();

    private static List<TileClass> UnownedTiles(CityObject city)
    {
        var tiles = new List<TileClass>();
        city.GetCenterTile().ForEachTileAtDistance(2, tiles.Add);
        return tiles;
    }

    private static float TransportationUpkeepGold(Civilization civ) => civ.Stats.GetStatMapForNextTurn()["Transportation upkeep"].Gold;

    private static void AssertTransportationUpkeep(Civilization civ, float expectedGold) => Assert.Equal(expectedGold, TransportationUpkeepGold(civ));

    [Fact]
    public void CityFoundingDoesNotRegisterCenterAsNeutralRoad()
    {
        var (_, civ, roadCity) = RoadMaintenanceGame();
        var center = roadCity.GetCenterTile();

        Assert.Equal(RoadStatus.Road, center.RoadStatus);
        Assert.DoesNotContain(center.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, 0f);

        center.SetRoadStatus(RoadStatus.Road, civ);
        Assert.DoesNotContain(center.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, 0f);

        civ.Tech.AddTechnology("Railroads", false);
        center.SetRoadStatus(RoadStatus.Railroad, civ);
        Assert.Equal(RoadStatus.Railroad, center.RoadStatus);
        Assert.DoesNotContain(center.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, 0f);
    }

    [Fact]
    public void OwnedRoadIsChargedOnceNotAsNeutral()
    {
        var (_, civ, roadCity) = RoadMaintenanceGame(roadUnique: UniqueType.ImprovementAllMaintenance.Text().FillPlaceholders("100", "Gold"));
        var ownedTile = OwnedNonCenterTile(roadCity);
        Assert.Equal(civ, ownedTile.GetOwner());
        Assert.Equal(civ, ownedTile.GetRoadOwner());

        ownedTile.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.Equal(RoadStatus.Road, ownedTile.RoadStatus);
        Assert.DoesNotContain(ownedTile.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -100f);

        ownedTile.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.DoesNotContain(ownedTile.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -100f);

        ownedTile.ImprovementFunctions.SetImprovement("Remove Road", civ);
        Assert.Equal(RoadStatus.None, ownedTile.RoadStatus);
        Assert.DoesNotContain(ownedTile.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, 0f);
    }

    [Fact]
    public void UnownedRoadIsNeutralAndClaimingTransfersToTerritory()
    {
        var (_, civ, roadCity) = RoadMaintenanceGame(roadUnique: UniqueType.ImprovementAllMaintenance.Text().FillPlaceholders("100", "Gold"));
        var tiles = UnownedTiles(roadCity);
        var removable = tiles[0];
        var pillaged = tiles[1];
        var claimed = tiles[2];

        removable.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.Contains(removable.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -100f);

        removable.ImprovementFunctions.SetImprovement("Remove Road", civ);
        Assert.DoesNotContain(removable.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, 0f);

        pillaged.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.Contains(pillaged.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -100f);
        pillaged.SetPillaged();
        Assert.True(pillaged.RoadIsPillaged);
        AssertTransportationUpkeep(civ, 0f);

        claimed.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.Contains(claimed.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -100f);
        roadCity.Expansion.TakeOwnership(claimed);
        Assert.DoesNotContain(claimed.Position, civ.NeutralRoads);
        Assert.Equal(civ, claimed.GetOwner());
        AssertTransportationUpkeep(civ, -100f);
    }

    [Fact]
    public void TerritorialMaintenanceIsFreeOnNeutralAndChargedOnceWhenOwned()
    {
        var territorial = UniqueType.ImprovementMaintenance.Text().FillPlaceholders("100", "Gold");
        var (_, civ, roadCity) = RoadMaintenanceGame(roadUnique: territorial);
        var ownedTile = OwnedNonCenterTile(roadCity);
        var unownedTile = UnownedTiles(roadCity).First();

        unownedTile.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.Contains(unownedTile.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, 0f);

        ownedTile.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.DoesNotContain(ownedTile.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -100f);
    }

    [Fact]
    public void DefaultRulesetRoadAndRailroadMaintenance()
    {
        var (_, civ, roadCity) = RoadMaintenanceGame();
        var ownedRoad = OwnedNonCenterTile(roadCity);
        var ownedRailroad = roadCity.GetCenterTile().Neighbors.First(it => it != ownedRoad);
        var unownedRoad = UnownedTiles(roadCity).First();

        AssertTransportationUpkeep(civ, 0f);

        ownedRoad.ImprovementFunctions.SetImprovement("Road", civ);
        AssertTransportationUpkeep(civ, -1f);

        ownedRailroad.ImprovementFunctions.SetImprovement("Railroad", civ);
        AssertTransportationUpkeep(civ, -3f);

        unownedRoad.ImprovementFunctions.SetImprovement("Road", civ);
        Assert.Contains(unownedRoad.Position, civ.NeutralRoads);
        AssertTransportationUpkeep(civ, -4f);
        Assert.DoesNotContain(ownedRoad.Position, civ.NeutralRoads);
        Assert.DoesNotContain(ownedRailroad.Position, civ.NeutralRoads);
        Assert.DoesNotContain(roadCity.GetCenterTile().Position, civ.NeutralRoads);
    }

    [Fact]
    public void TransportationUpkeepMatchesStatsTransientsAndTreasury()
    {
        var (game, civ, roadCity) = RoadMaintenanceGame(roadUnique: UniqueType.ImprovementAllMaintenance.Text().FillPlaceholders("100", "Gold"), isPlayer: true);
        OwnedNonCenterTile(roadCity).ImprovementFunctions.SetImprovement("Road", civ);
        UnownedTiles(roadCity).First().ImprovementFunctions.SetImprovement("Road", civ);

        var breakdown = civ.Stats.GetStatMapForNextTurn();
        Assert.Equal(-200f, breakdown["Transportation upkeep"].Gold);

        civ.UpdateStatsForNextTurn();
        var summed = new Stats();
        foreach (var stats in breakdown.Values) summed.Add(stats);
        Assert.True(summed.Equals(civ.Stats.StatsForNextTurn));

        var upkeepBeforeTransients = TransportationUpkeepGold(civ);
        foreach (var other in game.GameInfo.Civilizations) other.NeutralRoads.Clear();
        game.GameInfo.TileMap.SetNeutralTransients();
        Assert.Equal(upkeepBeforeTransients, TransportationUpkeepGold(civ));
        Assert.DoesNotContain(roadCity.GetCenterTile().Position, civ.NeutralRoads);
        Assert.DoesNotContain(OwnedNonCenterTile(roadCity).Position, civ.NeutralRoads);
        Assert.Contains(UnownedTiles(roadCity).First().Position, civ.NeutralRoads);

        civ.AddGold(10_000);
        civ.UpdateStatsForNextTurn();
        var goldBeforeTurn = civ.Gold;
        var predictedGoldDelta = (int)civ.Stats.StatsForNextTurn.Gold;
        new TurnManager(civ).EndTurn();
        Assert.Equal(goldBeforeTurn + predictedGoldDelta, civ.Gold);
    }
}
