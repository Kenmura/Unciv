using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// Tile (T053), against a real TileMap. The Kotlin fixtures FreshWaterTests and TileImprovementConstructionTests need TestGame
// (T035/T072) and are ported there.
public partial class TileTests
{
    private static Ruleset TileRuleset()
    {
        var ruleset = new Ruleset();
        void Add(string name, TerrainType type, Action<Terrain>? init = null)
        {
            var terrain = new Terrain { Name = name, Type = type };
            init?.Invoke(terrain);
            ruleset.Terrains.Put(name, terrain);
        }
        Add("Grassland", TerrainType.Land);
        Add("Hill", TerrainType.Land, it => { it.DefenceBonus = 0.25f; it.Uniques = ["Rough terrain", "Occurs in groups around high elevations"]; });
        Add("Mountain", TerrainType.Land, it => it.Impassable = true);
        Add("Coast", TerrainType.Water);
        Add("Ocean", TerrainType.Water);
        Add("Forest", TerrainType.TerrainFeature, it => { it.DefenceBonus = 0.25f; it.Uniques = ["Rough terrain"]; });
        ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron", ResourceType = ResourceType.Strategic });
        ruleset.TileResources.Put("Wheat", new TileResource { Name = "Wheat", ResourceType = ResourceType.Bonus });
        ruleset.TileImprovements.Put("Road", new TileImprovement { Name = "Road" });
        ruleset.TileImprovements.Put("Farm", new TileImprovement { Name = "Farm" });
        return ruleset;
    }

    /// <summary>A square patch of tiles, (0,0) to (width-1,height-1), all on <paramref name="baseTerrain"/>.</summary>
    private static TileMap TileMapOf(Ruleset ruleset, int width = 5, int height = 5, string baseTerrain = "Grassland")
    {
        var map = new TileMap(width * height) { GameInfo = new FakeGameInfo() };
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = baseTerrain });
        }
        map.SetTransients(ruleset);
        return map;
    }

    private static void SetBase(TileClass tile, string baseTerrain)
    {
        tile.BaseTerrain = baseTerrain;
        tile.SetTerrainTransients();
    }

    private class TileCiv(string id, FakeGameInfo? gameInfo = null) : FakeCiv(id, gameInfo)
    {
        public HashSet<IGameContextCivilization> AtWarWith { get; } = [];
        public bool Friendly { get; set; }
        public bool Forests { get; set; }
        public override bool ForestsAndJunglesAreRoads => Forests;
        public override bool Knows(IGameContextCivilization otherCiv) => true;
        public override bool IsAtWarWith(IGameContextCivilization otherCiv) => AtWarWith.Contains(otherCiv);
        public override bool IsConsideredFriendlyTerritoryOf(IGameContextCivilization otherCiv) => Friendly;
        public override bool CanSeeResource(TileResource? resource) => resource != null;
        public override bool MatchesFilter(string filter, GameContext? state, bool multiFilter) => filter == CivId;
        public HashSet<TileClass> Viewable { get; } = [];
        public override bool IsTileViewable(TileClass tile) => Viewable.Contains(tile);
    }

    private sealed class TileCity(string id, TileCiv civ, HexCoord location) : FakeCity(id, civ)
    {
        public override string Name => "Rome";
        public override HexCoord Location => location;
        public HashSet<TileClass> Worked { get; } = [];
        public override bool IsWorked(TileClass tile) => Worked.Contains(tile);
        public override bool IsTileLocked(HexCoord position) => false;
        public override bool IsCapital() => true;
        public override bool HasJustBeenConquered => false;
    }

    private sealed class TileUnit(string unitName, TileCiv unitCiv) : FakeUnit(unitName, 2f, unitCiv)
    {
        public bool Embarked { get; set; }
        public override bool IsEmbarked() => Embarked;
        public override IGameContextUnit Clone() => new TileUnit(Name, unitCiv) { Embarked = Embarked };
    }

    [Fact]
    public void SetTerrainTransients_SetsLandWaterOceanAndLastTerrain()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.True(tile.IsLand);
        Assert.False(tile.IsWater);
        Assert.Equal("Grassland", tile.LastTerrain.Name);
        SetBase(tile, "Ocean");
        Assert.True(tile.IsWater);
        Assert.True(tile.IsOcean);
        Assert.False(tile.IsLand);
        tile.AddTerrainFeature("Forest");
        Assert.Equal("Forest", tile.LastTerrain.Name);
        Assert.Equal(["Ocean", "Forest"], tile.AllTerrains.Select(it => it.Name).ToList());
    }

    [Fact]
    public void SetTerrainTransients_MissingTerrainThrows()
    {
        var map = TileMapOf(TileRuleset(), 1, 1);
        var tile = map[HexCoord.Zero];
        tile.BaseTerrain = "Nope";
        var ex = Assert.Throws<InvalidOperationException>(tile.SetTerrainTransients);
        Assert.Equal("Terrain Nope does not exist in ruleset!", ex.Message);
    }

    [Fact]
    public void Neighbors_AreCachedOnceAndExcludeSelf()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.Equal(6, tile.Neighbors.Count);
        Assert.DoesNotContain(tile, tile.Neighbors);
        Assert.Same(tile.Neighbors, tile.Neighbors);
        // Edge tiles only have the neighbors the map has
        Assert.Equal(3, map[HexCoord.Zero].Neighbors.Count);
    }

    [Fact]
    public void Neighbors_OfTileWithoutMapIsEmpty()
    {
        var ruleset = TileRuleset();
        var tile = new TileClass { Ruleset = ruleset, BaseTerrain = "Grassland" };
        Assert.False(tile.IsTilemapInitialized());
        Assert.Empty(tile.Neighbors);
        Assert.Null(((IGameContextTile)tile).GameInfo);
    }

    [Fact]
    public void IsAdjacentToCoast_NeedsLandAndCoastNeighbor()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.False(tile.IsAdjacentToCoast());
        // the laziness is per tile and not reset: use a fresh map
        map = TileMapOf(TileRuleset());
        tile = map[HexCoord.Of(2, 2)];
        SetBase(tile.Neighbors[0], "Coast");
        Assert.True(tile.IsAdjacentToCoast());
        SetBase(tile, "Coast");
        Assert.False(tile.IsAdjacentToCoast()); // water tiles are never "coastal"
    }

    [Fact]
    public void River_EdgesAreSharedBetweenNeighborsAndResetTheCache()
    {
        var map = TileMapOf(TileRuleset());
        var a = map[HexCoord.Of(2, 2)];
        var b = map[HexCoord.Of(3, 3)]; // directly below a (clock position 12 seen from a)
        Assert.False(a.IsAdjacentToRiver());
        Assert.True(a.SetConnectedByRiver(b, true));
        Assert.False(a.SetConnectedByRiver(b, true)); // already set
        Assert.True(a.IsConnectedByRiver(b));
        Assert.True(b.IsConnectedByRiver(a));
        Assert.True(a.IsAdjacentToRiver());
        Assert.True(b.IsAdjacentToRiver());
        Assert.True(a.HasBottomRiver || b.HasBottomRiver);
        Assert.True(a.SetConnectedByRiver(b, false));
        Assert.False(a.IsConnectedByRiver(b));
        Assert.False(a.IsAdjacentToRiver());
        Assert.False(a.SetConnectedByRiver(map[HexCoord.Of(0, 4)], true)); // not a neighbor: nothing happens
    }

    [Fact]
    public void IsConnectedByRiver_ThrowsForSelfAndNonNeighbor()
    {
        var map = TileMapOf(TileRuleset());
        var a = map[HexCoord.Of(2, 2)];
        Assert.Throws<InvalidOperationException>(() => a.IsConnectedByRiver(a));
        Assert.Throws<InvalidOperationException>(() => a.IsConnectedByRiver(map[HexCoord.Zero]));
    }

    [Fact]
    public void IsAdjacentToRiver_BottomEdgeCountsWithoutNeighbor()
    {
        var map = TileMapOf(TileRuleset(), 2, 2);
        var corner = map[HexCoord.Zero];
        corner.HasBottomRiver = true;
        Assert.True(corner.IsAdjacentToRiver());
        Assert.True(corner.MatchesFilter(Constants.River));
        Assert.True(corner.IsAdjacentTo(Constants.FreshWater));
    }

    [Fact]
    public void AerialDistanceTo_IsHexDistanceAndWrapsWhenWorldWrapIsOn()
    {
        var map = TileMapOf(TileRuleset(), 8, 8);
        var a = map[HexCoord.Of(1, 1)];
        var b = map[HexCoord.Of(4, 2)];
        Assert.Equal(3, a.AerialDistanceTo(b));
        Assert.Equal(0, a.AerialDistanceTo(a));
        map.MapParameters.WorldWrap = true;
        map.Width = 4; // distance 3 > width / 2, the unwrapped position (here the same) is used
        Assert.Equal(3, a.AerialDistanceTo(b));
    }

    [Fact]
    public void RoadStatus_PillagedRoadCountsAsNone()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var civ = new TileCiv("A");
        Assert.False(tile.HasConnection(civ));
        tile.SetRoadStatus(RoadStatus.Road, null);
        Assert.True(tile.HasConnection(civ));
        Assert.True(tile.HasRoadConnection(civ, true));
        Assert.False(tile.HasRailroadConnection(false));
        Assert.Equal(RoadStatus.Road, tile.GetConnectionStatus(civ));
        tile.RoadIsPillaged = true;
        Assert.Equal(RoadStatus.None, tile.GetUnpillagedRoad());
        Assert.False(tile.HasRoadConnection(civ, true));
        Assert.True(tile.HasRoadConnection(civ, false));
        Assert.Null(tile.GetUnpillagedRoadImprovement());
        Assert.NotNull(tile.GetRoadTileImprovement());
        Assert.True(tile.IsPillaged());
        Assert.Equal("Road", tile.GetImprovementToRepair()?.Name);
        tile.SetRepaired();
        Assert.False(tile.IsPillaged());
    }

    [Fact]
    public void ForestAndJungleAreRoads_NeedsNationFlagFeatureAndFriendlyTerritory()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        tile.AddTerrainFeature("Forest");
        var civ = new TileCiv("A") { Forests = true };
        Assert.False(tile.HasConnection(civ)); // not owned: not friendly
        Assert.Equal(RoadStatus.None, tile.GetConnectionStatus(civ));
        var city = new TileCity("c", civ, HexCoord.Of(0, 0));
        tile.SetOwningCity(city);
        Assert.True(tile.HasConnection(civ));
        Assert.Equal(RoadStatus.Road, tile.GetConnectionStatus(civ));
        Assert.True(tile.HasRoadConnection(civ, true));
        civ.Forests = false;
        Assert.False(tile.HasConnection(civ));
    }

    [Fact]
    public void SetOwningCity_SetsOwnerCityCenterAndRoadOwner()
    {
        var map = TileMapOf(TileRuleset());
        var civ = new TileCiv("A");
        ((FakeGameInfo)map.GameInfo).Turns = 7;
        var city = new TileCity("c", civ, HexCoord.Of(2, 2));
        var center = map[HexCoord.Of(2, 2)];
        var other = map[HexCoord.Of(2, 3)];
        center.SetOwningCity(city);
        other.SetOwningCity(city);
        Assert.True(center.IsCityCenter());
        Assert.False(other.IsCityCenter());
        Assert.Same(civ, other.GetOwner());
        Assert.Same(city, other.GetCity());
        Assert.Same(civ, other.GetRoadOwner());
        Assert.Same(city, center.GetCity());
        // ITileHistorySource
        ITileHistorySource source = center;
        Assert.Equal("A", source.OwnerCivID);
        Assert.True(source.IsCityCenter);
        Assert.True(source.IsCityCapital);
        Assert.Equal(7, source.Turns);
        // Razing the city on a tile with a road clears the road owner
        other.SetRoadStatus(RoadStatus.Road, null);
        other.SetOwningCity(null);
        Assert.Null(other.GetOwner());
        Assert.Null(other.GetRoadOwner());
    }

    [Fact]
    public void NeutralRoad_IsTrackedOnTheCreatingCiv()
    {
        var map = TileMapOf(TileRuleset());
        var civ = new TileCiv("A");
        var tile = map[HexCoord.Of(1, 1)];
        tile.SetRoadStatus(RoadStatus.Road, civ);
        Assert.Contains(tile.Position, civ.NeutralRoads);
        Assert.Same(civ, tile.GetRoadOwner());
        tile.RemoveRoad();
        Assert.DoesNotContain(tile.Position, civ.NeutralRoads);
        Assert.Equal(RoadStatus.None, tile.RoadStatus);
        tile.SetRoadStatus(RoadStatus.Road, civ);
        tile.RemoveRoadOwner();
        Assert.Null(tile.GetRoadOwner());
    }

    [Fact]
    public void Units_AreListedMilitaryCivilianThenAir_AndRemoved()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var civ = new TileCiv("A");
        Assert.Empty(tile.GetUnits());
        Assert.Null(tile.GetFirstUnit());
        var air1 = new TileUnit("Fighter", civ);
        var air2 = new TileUnit("Bomber", civ);
        var civilian = new TileUnit("Worker", civ);
        var military = new TileUnit("Warrior", civ);
        tile.AirUnits.Add(air1);
        tile.AirUnits.Add(air2);
        Assert.Same(air1, tile.GetFirstUnit());
        tile.CivilianUnit = civilian;
        tile.MilitaryUnit = military;
        Assert.Equal([military, civilian, air1, air2], tile.GetUnits());
        Assert.Same(military, tile.GetFirstUnit());
        tile.RemoveUnit(air1);
        tile.RemoveUnit(civilian);
        tile.RemoveUnit(military);
        Assert.Equal([air2], tile.GetUnits());
    }

    [Fact]
    public void IsBlockaded_EnemyShipAdjacentToOwnedWaterTile()
    {
        var map = TileMapOf(TileRuleset(), baseTerrain: "Coast");
        var tile = map[HexCoord.Of(2, 2)];
        var owner = new TileCiv("A");
        var enemy = new TileCiv("B");
        enemy.AtWarWith.Add(owner);
        tile.SetOwningCity(new TileCity("c", owner, HexCoord.Zero));
        Assert.False(tile.IsBlockaded());
        var ship = new TileUnit("Trireme", enemy);
        tile.Neighbors[0].MilitaryUnit = ship;
        Assert.True(tile.IsBlockaded());
        ship.Embarked = true;
        Assert.False(tile.IsBlockaded());
        ship.Embarked = false;
        tile.MilitaryUnit = new TileUnit("Frigate", owner); // an own unit on the tile unblocks it
        Assert.False(tile.IsBlockaded());
    }

    [Fact]
    public void Explored_VisibleMapAndSpectatorsSeeEverything()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var info = (FakeGameInfo)map.GameInfo;
        var civ = new TileCiv("A", info);
        Assert.False(tile.IsExplored(civ));
        tile.SetExplored(civ, true);
        Assert.True(tile.IsExplored(civ));
        tile.SetExplored(civ, false);
        Assert.False(tile.IsExplored(civ));
        Assert.False(tile.IsVisible(civ));
        civ.Viewable.Add(tile);
        Assert.True(tile.IsVisible(civ));
        civ.Viewable.Clear();
        info.EngineOptions.VisibleMap = true;
        Assert.True(tile.IsVisible(civ));
        Assert.True(tile.IsExplored(civ));
    }

    [Fact]
    public void Clone_CopiesStateButSharesNothingMutable()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var civ = new TileCiv("A");
        tile.AddTerrainFeature("Forest");
        tile.SetTileResource(tile.Ruleset.TileResources["Wheat"]);
        tile.QueueImprovement("Farm", 5);
        tile.HasBottomRiver = true;
        tile.CivilianUnit = new TileUnit("Worker", civ);
        tile.SetContinent(3);
        var clone = tile.Clone();
        Assert.Equal(tile.Position, clone.Position);
        Assert.Equal(["Forest"], clone.TerrainFeatures);
        Assert.Equal("Wheat", clone.Resource);
        Assert.Equal("Farm", clone.ImprovementInProgress);
        Assert.Equal(5, clone.TurnsToImprovement);
        Assert.True(clone.HasBottomRiver);
        Assert.Equal(3, clone.GetContinent());
        Assert.NotSame(tile.CivilianUnit, clone.CivilianUnit);
        Assert.NotSame(tile.History, clone.History);
        Assert.True(clone.IsLand);
        clone.StopWorkingOnImprovement();
        Assert.Equal("Farm", tile.ImprovementInProgress);
        Assert.Null(tile.Clone(false).CivilianUnit);
    }

    [Fact]
    public void ImprovementQueue_CountsDownAndMarksCreatesOneImprovement()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.False(tile.HasImprovementInProgress());
        Assert.Equal(0, tile.TurnsToImprovement);
        tile.QueueImprovement("Farm", 2);
        Assert.True(tile.HasImprovementInProgress());
        Assert.Equal("Farm", tile.GetTileImprovementInProgress()?.Name);
        Assert.False(tile.IsMarkedForCreatesOneImprovement());
        tile.StopWorkingOnImprovement();
        Assert.False(tile.HasImprovementInProgress());
        tile.QueueImprovement("Farm", -1);
        Assert.True(tile.IsMarkedForCreatesOneImprovement());
        Assert.True(tile.IsMarkedForCreatesOneImprovement("Farm"));
        Assert.False(tile.IsMarkedForCreatesOneImprovement("Road"));
        tile.StopWorkingOnImprovement(); // does nothing for marked tiles
        Assert.True(tile.HasImprovementInProgress());
    }

    [Fact]
    public void ImprovementQueueEntry_CountDownStopsAtZero()
    {
        var entry = new TileClass.ImprovementQueueEntry("Farm", 2);
        Assert.True(entry.CountDown());
        Assert.False(entry.CountDown());
        Assert.False(entry.CountDown());
        Assert.Equal(0, entry.TurnsToImprovement);
        Assert.Equal("Farm: 0⏳", entry.ToString());
        var negative = new TileClass.ImprovementQueueEntry("Farm", -1);
        Assert.False(negative.CountDown());
        Assert.Equal(0, negative.TurnsToImprovement);
    }

    [Fact]
    public void Pillaging_ChecksUniquesAndPicksImprovementBeforeRoad()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.False(tile.CanPillageTile());
        Assert.Null(tile.GetImprovementToPillage());
        tile.SetImprovementBasic("Farm");
        tile.SetRoadStatus(RoadStatus.Road, null);
        Assert.True(tile.CanPillageTile());
        Assert.Equal("Farm", tile.GetImprovementToPillageName());
        tile.ImprovementIsPillaged = true;
        Assert.Equal("Road", tile.GetImprovementToPillageName());
        Assert.Equal("Farm", tile.GetImprovementToRepair()?.Name);
        Assert.Null(tile.GetUnpillagedTileImprovement());
        Assert.Null(tile.GetUnpillagedImprovement());
        tile.RoadIsPillaged = true;
        Assert.Null(tile.GetImprovementToPillage());
        Assert.False(tile.CanPillageTile());
        tile.SetRepaired();
        Assert.False(tile.ImprovementIsPillaged);
        Assert.True(tile.RoadIsPillaged); // repair fixes the improvement first
    }

    [Fact]
    public void GetShownImprovement_UsesLastSeenForHumans()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        tile.SetImprovementBasic("Farm");
        Assert.Equal("Farm", tile.GetShownImprovement(null));
        Assert.Equal("Farm", tile.GetShownImprovement(new TileCiv("AI")));
    }

    [Fact]
    public void SetTileResource_StrategicAmountFollowsMapResourcesAndDeposit()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var iron = tile.Ruleset.TileResources["Iron"];
        iron.MajorDepositAmount = new TileResource.DepositAmount { Sparse = 4, Default = 6, Abundant = 8 };
        iron.MinorDepositAmount = new TileResource.DepositAmount { Sparse = 1, Default = 2, Abundant = 3 };
        tile.SetTileResource(iron, majorDeposit: true);
        Assert.Equal(6, tile.ResourceAmount);
        map.MapParameters.MapResources = MapResourceSetting.Sparse.GetLabel();
        tile.SetTileResource(iron, majorDeposit: true);
        Assert.Equal(4, tile.ResourceAmount);
        map.MapParameters.MapResources = MapResourceSetting.Abundant.GetLabel();
        tile.SetTileResource(iron, majorDeposit: false);
        Assert.Equal(3, tile.ResourceAmount);
        tile.SetTileResource(tile.Ruleset.TileResources["Wheat"]);
        Assert.Equal(0, tile.ResourceAmount);
        Assert.Equal("Wheat", tile.Resource);
    }

    [Fact]
    public void SetTileResource_RandomChoiceNeedsARandomSource_AndUsesTheDefaultFrequency()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var iron = tile.Ruleset.TileResources["Iron"];
        iron.MajorDepositAmount = new TileResource.DepositAmount { Default = 6 };
        iron.MinorDepositAmount = new TileResource.DepositAmount { Default = 2 };
        Assert.Throws<InvalidOperationException>(() => tile.SetTileResource(iron));
        // Same seed, same choice: the draw is compared against 0.04 (1 per 25 tiles) when no terrain sets a frequency
        var expectedMajor = new GameRandom(42).NextDouble() < 0.04;
        tile.SetTileResource(iron, null, new GameRandom(42));
        Assert.Equal(expectedMajor ? 6 : 2, tile.ResourceAmount);
    }

    [Fact]
    public void SetTerrainTransients_FixesMissingStrategicAmountAsSmallDeposit()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var iron = tile.Ruleset.TileResources["Iron"];
        iron.MinorDepositAmount = new TileResource.DepositAmount { Default = 2 };
        tile.SetTileResource("Iron", false);
        tile.ResourceAmount = 0;
        tile.SetTerrainTransients();
        Assert.Equal(2, tile.ResourceAmount);
    }

    [Fact]
    public void MatchesFilter_TerrainResourceAndSpecialFilters()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var civ = new TileCiv("A");
        tile.AddTerrainFeature("Forest");
        tile.SetTileResource(tile.Ruleset.TileResources["Wheat"]);
        Assert.True(tile.MatchesFilter("Land"));
        Assert.False(tile.MatchesFilter("Water"));
        Assert.True(tile.MatchesFilter("Terrain"));
        Assert.True(tile.MatchesFilter("All"));
        Assert.True(tile.MatchesFilter("Grassland"));
        Assert.True(tile.MatchesFilter("Forest"));
        Assert.True(tile.MatchesFilter("Wheat"));
        Assert.True(tile.MatchesFilter("Bonus resource"));
        Assert.False(tile.MatchesFilter("Strategic resource"));
        Assert.False(tile.MatchesFilter("Featureless"));
        Assert.False(tile.MatchesFilter("Open terrain"));
        Assert.True(tile.MatchesFilter("unimproved"));
        Assert.False(tile.MatchesFilter("improved"));
        Assert.True(tile.MatchesFilter("Unowned"));
        Assert.False(tile.MatchesFilter("your", civ));
        Assert.True(tile.MatchesFilter("resource", civ));
        Assert.False(tile.MatchesFilter("resource", null));
        Assert.True(tile.MatchesFilter("{Land} {Forest}"));
        Assert.False(tile.MatchesFilter("non-[Forest]"));
        Assert.True(tile.MatchesFilter("non-[Water]"));
        Assert.False(tile.MatchesFilter("Foreign", null));
        tile.SetImprovementBasic("Farm");
        Assert.True(tile.MatchesFilter("improved"));
        tile.ImprovementIsPillaged = true;
        Assert.True(tile.MatchesFilter("pillaged"));
        Assert.True(tile.MatchesFilter("unimproved"));
    }

    [Fact]
    public void MatchesFilter_OwnershipFilters()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        var owner = new TileCiv("A");
        var visitor = new TileCiv("B");
        tile.SetOwningCity(new TileCity("c", owner, HexCoord.Zero));
        Assert.True(tile.MatchesFilter("your", owner));
        Assert.False(tile.MatchesFilter("your", visitor));
        Assert.False(tile.MatchesFilter("Unowned"));
        Assert.True(tile.MatchesFilter("Friendly", owner));
        Assert.False(tile.MatchesFilter("Foreign", owner));
        Assert.True(tile.MatchesFilter("Foreign", visitor)); // owner.Friendly is false
        owner.Friendly = true; // the owner's diplomacy with the visitor
        Assert.True(tile.MatchesFilter("Friendly Land", visitor));
        Assert.False(tile.MatchesFilter("Enemy", visitor));
        visitor.AtWarWith.Add(owner);
        Assert.True(tile.MatchesFilter("Enemy Land", visitor));
        Assert.True(tile.MatchesFilter("A")); // falls through to the owner's filter match (the fake matches civ names)
        Assert.False(tile.MatchesFilter("Z"));
    }

    [Fact]
    public void DefensiveBonus_UsesBestFeatureAndNaturalWonder()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.Equal(0f, tile.GetDefensiveBonus());
        tile.AddTerrainFeature("Forest");
        Assert.Equal(0.25f, tile.GetDefensiveBonus());
        SetBase(tile, "Hill");
        Assert.True(tile.IsHill());
        Assert.Equal("Hill", tile.GetHillTerrain()?.Name);
        Assert.Equal(0.25f, tile.GetDefensiveBonus()); // the feature replaces the base terrain bonus
        Assert.True(tile.IsRoughTerrain());
    }

    [Fact]
    public void Impassable_ComesFromTheLastTerrain()
    {
        var map = TileMapOf(TileRuleset());
        var tile = map[HexCoord.Of(2, 2)];
        Assert.False(tile.IsImpassible());
        SetBase(tile, "Mountain");
        Assert.True(tile.IsImpassible());
        Assert.Contains("Impassable", tile.ToString());
    }

    [Fact]
    public void CanBeSettled_RejectsWaterImpassableNearbyCitiesAndForeignTerritory()
    {
        var map = TileMapOf(TileRuleset(), 9, 9);
        var info = new SettleGameInfo();
        map.GameInfo = info;
        var tile = map[HexCoord.Of(4, 4)];
        var civ = new TileCiv("A");
        Assert.True(tile.CanBeSettled(civ));
        SetBase(tile, "Coast");
        Assert.False(tile.CanBeSettled(civ));
        SetBase(tile, "Mountain");
        Assert.False(tile.CanBeSettled(civ));
        SetBase(tile, "Grassland");
        Assert.True(tile.CanBeSettled(civ));
        // a city centre within minimalCityDistance (3) on the same continent
        var near = map[HexCoord.Of(5, 5)];
        near.SetOwningCity(new TileCity("c", civ, near.Position));
        Assert.False(tile.CanBeSettled(civ));
        // foreign territory: city further away, but its borders cover the tile
        var far = map[HexCoord.Of(0, 0)];
        var other = new TileCiv("B");
        far.SetOwningCity(new TileCity("d", other, far.Position));
        near.SetOwningCity(null);
        Assert.True(tile.CanBeSettled(civ));
        tile.SetOwningCity(new TileCity("d", other, far.Position));
        Assert.False(tile.CanBeSettled(civ));
        Assert.True(tile.CanBeSettled(other));
    }

    private sealed class SettleGameInfo : FakeGameInfo
    {
        public override Unciv.Models.ModConstants GetModConstants() => new();
    }

    [Fact]
    public void Continent_CanOnlyBeAssignedOnce()
    {
        var map = TileMapOf(TileRuleset(), 1, 1);
        var tile = map[HexCoord.Zero];
        Assert.Equal(-1, tile.GetContinent());
        tile.SetContinent(2);
        Assert.Equal(2, tile.GetContinent());
        var ex = Assert.Throws<InvalidOperationException>(() => tile.SetContinent(3));
        Assert.Contains("Continent already assigned", ex.Message);
        tile.ClearContinent();
        tile.SetContinent(3);
        Assert.Equal(3, tile.GetContinent());
    }

    [Fact]
    public void TerrainFeatures_AddRemoveAndRulesetIncompatibility()
    {
        var ruleset = TileRuleset();
        var map = TileMapOf(ruleset, 1, 1);
        var tile = map[HexCoord.Zero];
        tile.AddTerrainFeature("Forest");
        tile.AddTerrainFeature("Forest");
        Assert.Equal(["Forest"], tile.TerrainFeatures);
        Assert.Empty(tile.GetRulesetIncompatibility(ruleset));
        tile.RemoveTerrainFeature("Forest");
        tile.RemoveTerrainFeature("Forest");
        Assert.Empty(tile.TerrainFeatures);
        tile.AddTerrainFeature("Forest");
        tile.RemoveTerrainFeatures();
        Assert.Empty(tile.TerrainFeatureObjects);

        tile.SetTerrainFeatures(["Missing"]);
        Assert.Empty(tile.TerrainFeatureObjects);
        tile.SetImprovementBasic("Farm");
        tile.NaturalWonder = "Krakatoa";
        var empty = new Ruleset();
        Assert.Equal(
            ["Base terrain [Grassland] does not exist in ruleset!", "Terrain feature [Missing] does not exist in ruleset!",
                "Improvement [Farm] does not exist in ruleset!", "Natural Wonder [Krakatoa] does not exist in ruleset!"],
            tile.GetRulesetIncompatibility(empty));
    }

    [Fact]
    public void RemoveMissingTerrainModReferences_DropsWhatTheRulesetLacks()
    {
        var ruleset = TileRuleset();
        var map = TileMapOf(ruleset, 1, 1);
        var tile = map[HexCoord.Zero];
        tile.SetTerrainFeatures(["Forest", "Missing"]);
        tile.SetTileResource("Gone", false);
        tile.SetImprovementBasic("Farm");
        tile.Improvement = "Gone";
        tile.QueueImprovement("Gone", 3);
        tile.NaturalWonder = "Gone";
        tile.RemoveMissingTerrainModReferences(ruleset);
        Assert.Equal(["Forest"], tile.TerrainFeatures);
        Assert.Null(tile.Resource);
        Assert.Null(tile.Improvement);
        Assert.False(tile.HasImprovementInProgress());
        Assert.Null(tile.NaturalWonder);
    }

    [Fact]
    public void TerrainUniqueMap_IsCachedPerTerrainList()
    {
        var map = TileMapOf(TileRuleset(), 3, 3);
        var before = map.TileUniqueMapCache.Count;
        Assert.Equal(1, before); // one list ("Grassland") for all nine tiles
        var tile = map[HexCoord.Of(1, 1)];
        tile.AddTerrainFeature("Forest");
        Assert.Equal(2, map.TileUniqueMapCache.Count);
        Assert.True(tile.TerrainHasUnique(UniqueType.RoughTerrain));
        Assert.Single(tile.GetTerrainMatchingUniques(UniqueType.RoughTerrain));
        Assert.Single(tile.GetMatchingUniques(UniqueType.RoughTerrain));
        Assert.Equal(["Grassland", "Forest"], tile.CachedTerrainData.TerrainNameSet.OrderByDescending(it => it).ToList());
    }

    [Fact]
    public void ToString_ListsTheImportantProperties()
    {
        var map = TileMapOf(TileRuleset(), 3, 3);
        var tile = map[HexCoord.Of(1, 2)];
        Assert.Equal("Tile @HexCoord(x=1, y=2), Grassland", tile.ToString());
        tile.AddTerrainFeature("Forest");
        var iron = tile.Ruleset.TileResources["Iron"];
        iron.MinorDepositAmount = new TileResource.DepositAmount { Default = 2 };
        tile.SetTileResource(iron, false);
        tile.SetRoadStatus(RoadStatus.Road, null);
        tile.SetImprovementBasic("Farm");
        tile.CivilianUnit = new TileUnit("Worker", new TileCiv("A"));
        Assert.Equal("Tile @HexCoord(x=1, y=2), Grassland, Forest, {2} {Iron}, Road, Farm, Worker - A", tile.ToString());
        Assert.Equal("Tile @HexCoord(x=0, y=0), uninitialized", new TileClass().ToString());
    }

    [Fact]
    public void LateinitMembers_ThrowBeforeAssignment()
    {
        var tile = new TileClass();
        Assert.Throws<InvalidOperationException>(() => tile.BaseTerrain);
        Assert.Throws<InvalidOperationException>(() => tile.Ruleset);
        Assert.Throws<InvalidOperationException>(() => tile.TileMap);
        Assert.Throws<InvalidOperationException>(() => tile.GetBaseTerrain());
        Assert.Throws<InvalidOperationException>(() => tile.GetNaturalWonder());
    }

    [Fact]
    public void LatitudeLongitudeRowColumn_MirrorHexMath()
    {
        var map = TileMapOf(TileRuleset(), 5, 5);
        var tile = map[HexCoord.Of(3, 1)];
        Assert.Equal(4, tile.Latitude);
        Assert.Equal(2, tile.Longitude);
        Assert.Equal(2, tile.GetRow());
        Assert.Equal(-2, tile.GetColumn());
    }
}
