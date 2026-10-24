using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using Civilization = Unciv.Logic.Civilization.Civilization;
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

    private static (TestGame Game, TileMap Map) GameWithMap(int width = 5, int height = 5, string baseTerrain = "Grassland")
    {
        var game = new TestGame();
        var map = TileMapOf(game.Ruleset, width, height, baseTerrain);
        game.GameInfo.TileMap = map;
        map.GameInfo = game.GameInfo;
        return (game, map);
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
        public override void UpdateCivResources() { }
        public bool CanPassThrough { get; set; }
        public override bool CanPassThroughTiles(IGameContextCivilization otherCiv) => CanPassThrough;
        public override bool CanSeeResource(TileResource? resource) => resource != null;
        public override bool MatchesFilter(string filter, GameContext? state, bool multiFilter) => filter == CivId;
        public HashSet<TileClass> Viewable { get; } = [];
        public override bool IsTileViewable(TileClass tile) => Viewable.Contains(tile);
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
        var (game, map) = GameWithMap(5, 5);
        var tile = map[HexCoord.Of(2, 2)];
        tile.AddTerrainFeature("Forest");
        var civ = game.AddCiv();
        civ.Nation.ForestsAndJunglesAreRoads = true;
        Assert.False(tile.HasConnection(civ)); // not owned: not friendly
        Assert.Equal(RoadStatus.None, tile.GetConnectionStatus(civ));
        var city = game.AddCity(civ, map[HexCoord.Of(0, 0)]);
        tile.SetOwningCity(city);
        Assert.True(tile.HasConnection(civ));
        Assert.Equal(RoadStatus.Road, tile.GetConnectionStatus(civ));
        Assert.True(tile.HasRoadConnection(civ, true));
        civ.Nation.ForestsAndJunglesAreRoads = false;
        Assert.False(tile.HasConnection(civ));
    }

    [Fact]
    public void SetOwningCity_SetsOwnerCityCenterAndRoadOwner()
    {
        var (game, map) = GameWithMap(5, 5);
        var civ = game.AddCiv();
        civ.CivId = "A";
        game.GameInfo.Turns = 7;
        var center = map[HexCoord.Of(2, 2)];
        var city = game.AddCity(civ, center);
        var other = map[HexCoord.Of(2, 3)];
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
        var civ = MapUnitFactory.NewCiv("A");
        Assert.Empty(tile.GetUnits());
        Assert.Null(tile.GetFirstUnit());
        var air1 = MapUnitFactory.NewUnit(civ, "Fighter", "Air");
        var air2 = MapUnitFactory.NewUnit(civ, "Bomber", "Air");
        var civilian = MapUnitFactory.NewUnit(civ, "Worker");
        var military = MapUnitFactory.NewUnit(civ, "Warrior");
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
        var info = new FakeGameInfoState();
        var owner = MapUnitFactory.NewCiv("A", info);
        var enemy = MapUnitFactory.NewCiv(Constants.Barbarians, info); // barbarians are at war with everybody
        tile.SetOwningCity(new CityObject { Civ = owner });
        Assert.False(tile.IsBlockaded());
        var ship = MapUnitFactory.NewUnit(enemy, "Trireme", "Water", tile.Neighbors[0]);
        tile.Neighbors[0].MilitaryUnit = ship;
        Assert.True(tile.IsBlockaded());
        // A land unit on water is embarked, and embarked units do not blockade
        var embarked = MapUnitFactory.NewUnit(enemy, "Raft", "Land", tile.Neighbors[1]);
        tile.Neighbors[0].MilitaryUnit = null;
        tile.Neighbors[1].MilitaryUnit = embarked;
        Assert.True(embarked.IsEmbarked());
        Assert.False(tile.IsBlockaded());
        tile.MilitaryUnit = MapUnitFactory.NewUnit(owner, "Frigate", "Water", tile); // an own unit on the tile unblocks it
        tile.Neighbors[0].MilitaryUnit = ship;
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
        tile.CivilianUnit = MapUnitFactory.NewUnit(MapUnitFactory.NewCiv("A"), "Worker");
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
        var (game, map) = GameWithMap(5, 5);
        var tile = map[HexCoord.Of(2, 2)];
        var owner = game.AddCiv();
        owner.CivId = "A";
        owner.Nation.Name = "A";
        var visitor = game.AddCiv();
        visitor.CivId = "B";
        visitor.Nation.Name = "B";
        var city = game.AddCity(owner, map[HexCoord.Of(0, 0)]);
        tile.SetOwningCity(city);
        Assert.True(tile.MatchesFilter("your", owner));
        Assert.False(tile.MatchesFilter("your", visitor));
        Assert.False(tile.MatchesFilter("Unowned"));
        Assert.True(tile.MatchesFilter("Friendly", owner));
        Assert.False(tile.MatchesFilter("Foreign", owner));
        Assert.True(tile.MatchesFilter("Foreign", visitor));
        visitor.GetDiplomacyManagerOrMeet(owner).HasOpenBorders = true;
        Assert.True(tile.MatchesFilter("Friendly Land", visitor));
        Assert.False(tile.MatchesFilter("Enemy", visitor));
        visitor.GetDiplomacyManagerOrMeet(owner).DeclareWar();
        Assert.True(tile.MatchesFilter("Enemy Land", visitor));
        Assert.True(tile.MatchesFilter("A")); // falls through to the owner's filter match
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
        var (game, map) = GameWithMap(9, 9);
        var tile = map[HexCoord.Of(4, 4)];
        var civ = game.AddCiv();
        var other = game.AddCiv();
        Assert.True(tile.CanBeSettled(civ));
        SetBase(tile, "Coast");
        Assert.False(tile.CanBeSettled(civ));
        SetBase(tile, "Mountain");
        Assert.False(tile.CanBeSettled(civ));
        SetBase(tile, "Grassland");
        Assert.True(tile.CanBeSettled(civ));
        // a city centre within minimalCityDistance (3) on the same continent
        var near = map[HexCoord.Of(5, 5)];
        var nearCity = game.AddCity(civ, near);
        Assert.False(tile.CanBeSettled(civ));
        // foreign territory: city further away, but its borders cover the tile
        var far = map[HexCoord.Of(0, 0)];
        var farCity = game.AddCity(other, far);
        near.SetOwningCity(null);
        Assert.True(tile.CanBeSettled(civ));
        tile.SetOwningCity(farCity);
        Assert.False(tile.CanBeSettled(civ));
        Assert.True(tile.CanBeSettled(other));
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
        tile.CivilianUnit = MapUnitFactory.NewUnit(MapUnitFactory.NewCiv("A"), "Worker");
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

    [Fact]
    public void UnitHeightAndTileHeight_SumElevationsAndBlockSameElevationSight()
    {
        var ruleset = TileRuleset();
        ruleset.Terrains["Hill"].Uniques = ["Rough terrain", "Has an elevation of [2] for visibility calculations"];
        ruleset.Terrains["Forest"].Uniques = ["Rough terrain", "Has an elevation of [1] for visibility calculations", "Blocks line-of-sight from tiles at same elevation"];
        var map = TileMapOf(ruleset);
        var tile = map[HexCoord.Of(2, 2)];
        Assert.Equal((0, 0), (tile.UnitHeight, tile.TileHeight));
        SetBase(tile, "Hill");
        Assert.Equal((2, 2), (tile.UnitHeight, tile.TileHeight));
        tile.SetTerrainFeatures(["Forest"]);
        tile.SetTerrainTransients();
        Assert.Equal(3, tile.UnitHeight); // elevations of base terrain and feature add up
        Assert.Equal(4, tile.TileHeight); // the forest blocks sight at the same elevation: one higher
    }

    [Fact]
    public void CanCivPassThrough_OwnerWarAndConquestRules()
    {
        var (game, map) = GameWithMap(5, 5);
        var owner = game.AddCiv();
        var visitor = game.AddCiv();
        var tile = map[HexCoord.Of(2, 3)];
        Assert.True(tile.CanCivPassThrough(visitor)); // unowned
        var city = game.AddCity(owner, map[HexCoord.Of(2, 2)]);
        tile.SetOwningCity(city);
        Assert.True(tile.CanCivPassThrough(owner));
        Assert.False(tile.CanCivPassThrough(visitor)); // foreign land: decided by CanPassThroughTiles
        visitor.GetDiplomacyManagerOrMeet(owner).HasOpenBorders = true;
        Assert.True(tile.CanCivPassThrough(visitor));

        var center = map[HexCoord.Of(2, 2)];
        visitor.GetDiplomacyManagerOrMeet(owner).DeclareWar();
        Assert.False(center.CanCivPassThrough(visitor)); // an enemy city centre blocks even with open borders
        city.HasJustBeenConquered = true;
        Assert.True(center.CanCivPassThrough(visitor));
    }

    [Fact]
    public void WorkingCityIsLockedAndProvidesYield()
    {
        var (game, map) = GameWithMap(5, 5);
        game.Ruleset.TileImprovements["Farm"] = new TileImprovement
        {
            Name = "Farm",
            Uniques = ["Tile provides yield without assigned population"]
        };
        var civ = game.AddCiv();
        var owning = game.AddCity(civ, map[HexCoord.Of(0, 0)]);
        var other = game.AddCity(civ, map[HexCoord.Of(4, 4)]);
        var tile = map[HexCoord.Of(2, 2)];
        Assert.Null(tile.GetWorkingCity()); // no owner
        Assert.False(tile.ProvidesYield()); // no city
        tile.SetOwningCity(owning);
        Assert.Null(tile.GetWorkingCity());
        Assert.False(tile.IsWorked());
        Assert.False(tile.IsLocked());
        Assert.False(tile.ProvidesYield());

        other.WorkedTiles.Add(tile.Position); // worked by a different city of the same civ
        Assert.Same(other, tile.GetWorkingCity());
        Assert.True(tile.IsWorked());
        Assert.True(tile.ProvidesYield());
        owning.WorkedTiles.Add(tile.Position);
        Assert.Same(owning, tile.GetWorkingCity()); // the owning city is checked first
        other.WorkedTiles.Clear();
        owning.WorkedTiles.Clear();

        tile.SetImprovementBasic("Farm");
        Assert.True(tile.ProvidesYield()); // improvement unique, unworked
        tile.ImprovementIsPillaged = true;
        Assert.False(tile.ProvidesYield());
        tile.ImprovementIsPillaged = false;
        tile.SetImprovementBasic("Road");
        Assert.False(tile.ProvidesYield());

        owning.WorkedTiles.Add(tile.Position);
        Assert.False(tile.IsLocked());
        owning.LockedTiles.Add(tile.Position);
        Assert.True(tile.IsLocked());
        Assert.False(map[HexCoord.Of(0, 0)].IsLocked()); // not worked, not locked
        var centerTile = map[HexCoord.Of(0, 0)];
        Assert.True(centerTile.ProvidesYield()); // city centres always provide yield
    }

    [Fact]
    public void ProvidesResources_CityCentreImprovementAndGreatImprovementRules()
    {
        var game = new TestGame();
        var ruleset = game.Ruleset;
        ruleset.TileResources["Iron"].ImprovedBy = ["Mine"];
        ruleset.TileResources["Iron"].RevealedBy = null;
        ruleset.TileResources["Wheat"].ImprovedBy = ["Farm"];
        ruleset.TileImprovements["Mine"] = new TileImprovement { Name = "Mine", TurnsToBuild = 5, TechRequired = "Mining" };
        ruleset.TileImprovements["Farm"] = new TileImprovement { Name = "Farm", TurnsToBuild = 3 };
        ruleset.TileImprovements["Fort"] = new TileImprovement { Name = "Fort", Uniques = ["Great Improvement"] };
        foreach (var resource in ruleset.TileResources.Values) resource.SetTransients(ruleset);
        var map = TileMapOf(ruleset, 5, 5);
        game.GameInfo.TileMap = map;
        map.GameInfo = game.GameInfo;
        var civ = game.AddCiv();
        civ.Tech.TechsResearched.Clear();
        var tile = map[HexCoord.Of(2, 2)];

        Assert.False(tile.ProvidesResources(civ)); // no resource (CanSeeResource is false)
        tile.SetTileResource("Iron");
        Assert.False(tile.ProvidesResources(civ)); // not improved
        tile.SetImprovementBasic("Fort");
        Assert.True(tile.ProvidesResources(civ)); // strategic resource under a great improvement
        tile.SetTileResource("Wheat");
        Assert.False(tile.ProvidesResources(civ)); // bonus resources do not count that way

        tile.SetImprovementBasic("Farm");
        Assert.True(tile.ProvidesResources(civ));
        tile.ImprovementIsPillaged = true;
        Assert.False(tile.ProvidesResources(civ));
        tile.ImprovementIsPillaged = false;

        tile.SetTileResource("Iron");
        tile.SetImprovementBasic("Mine");
        Assert.False(tile.ProvidesResources(civ)); // tech missing
        civ.Tech.TechsResearched.Add("Mining");
        Assert.True(tile.ProvidesResources(civ));

        // City centre: needs a buildable improvement whose tech is known
        var centre = map[HexCoord.Of(1, 1)];
        game.AddCity(civ, centre);
        centre.SetTileResource("Iron");
        civ.Tech.TechsResearched.Clear();
        Assert.False(centre.ProvidesResources(civ));
        civ.Tech.TechsResearched.Add("Mining");
        Assert.True(centre.ProvidesResources(civ));
        ruleset.TileImprovements["Mine"].TurnsToBuild = -1; // only 'placeable', not buildable by workers
        Assert.False(centre.ProvidesResources(civ));
    }

    private static (TestGame Game, Civilization Civ, CityObject City) ProductionBonusScenario(string bonusUnique)
    {
        var (game, map) = GameWithMap(10, 10);
        game.GameInfo.Speed.Modifier = 1f;
        game.Ruleset.Terrains["Forest"] = new Terrain
        {
            Name = "Forest",
            Type = TerrainType.TerrainFeature,
            Uniques = ["Rough terrain", bonusUnique]
        };
        game.Ruleset.TileImprovements["Remove Forest"] = new TileImprovement { Name = "Remove Forest" };
        var civ = game.AddCiv(isPlayer: true);
        civ.CivId = "A";
        civ.Nation.Name = "A";
        var city = game.AddCity(civ, map[HexCoord.Of(2, 2)]);
        city.Name = "Rome";
        civ.Notifications.Clear();
        return (game, civ, city);
    }

    private static TileClass ForestAt(TileMap map, int x, int y)
    {
        var tile = map[HexCoord.Of(x, y)];
        tile.SetTerrainFeatures(["Forest"]);
        return tile;
    }

    [Fact]
    public void RemovingFeature_ProductionBonusGoesToClosestCityScaledByDistanceAndOwnership()
    {
        var (game, civ, city) = ProductionBonusScenario("Provides a one-time bonus of [+10 Production] to the closest city when cut down");
        var map = game.TileMap;
        var adjacentUnowned = ForestAt(map, 2, 3);
        adjacentUnowned.SetOwningCity(null);
        adjacentUnowned.SetImprovement("Remove Forest", civ);
        Assert.Empty(adjacentUnowned.TerrainFeatures);
        Assert.Equal(6, city.CityConstructions.ProductionOverflow); // 10 * 2/3 for foreign or unowned land, truncated
        Assert.Single(civ.Notifications);
        Assert.Contains("Clearing a [Forest]", civ.Notifications[0].Text);
        Assert.Contains("[Rome]", civ.Notifications[0].Text);

        city.CityConstructions.ProductionOverflow = 0;
        var ownedAdjacent = ForestAt(map, 3, 2);
        ownedAdjacent.SetOwningCity(city);
        ownedAdjacent.SetImprovement("Remove Forest", civ);
        Assert.Equal(10, city.CityConstructions.ProductionOverflow); // distance 1, own tile: full

        city.CityConstructions.ProductionOverflow = 0;
        var ownedFar = ForestAt(map, 2, 5); // distance 3: (6-3)/4 = 0.75
        ownedFar.SetOwningCity(city);
        ownedFar.SetImprovement("Remove Forest", civ);
        Assert.Equal(7, city.CityConstructions.ProductionOverflow); // 7.5 truncated

        city.CityConstructions.ProductionOverflow = 0;
        var tooFar = ForestAt(map, 2, 8); // distance 6 > 5
        tooFar.SetOwningCity(city);
        tooFar.SetImprovement("Remove Forest", civ);
        Assert.Equal(0, city.CityConstructions.ProductionOverflow);
    }

    [Fact]
    public void RemovingFeature_NoBonusWithoutCivOrUnique()
    {
        var (game, civ, city) = ProductionBonusScenario("Provides a one-time bonus of [+10 Production] to the closest city when cut down");
        var tile = ForestAt(game.TileMap, 2, 3);
        tile.SetImprovement("Remove Forest"); // no civ: no broader effects
        Assert.Equal(0, city.CityConstructions.ProductionOverflow);

        var (game2, civ2, city2) = ProductionBonusScenario("Rough terrain");
        ForestAt(game2.TileMap, 2, 3).SetImprovement("Remove Forest", civ2);
        Assert.Equal(0, city2.CityConstructions.ProductionOverflow);
        Assert.Empty(civ2.Notifications);
    }

    [Fact]
    public void RemovingFeature_ProductionBonusAppliesGameSpeedModifier()
    {
        var (game, civ, city) = ProductionBonusScenario("Provides a one-time bonus of [+10 Production] to the closest city when cut down <(modified by game speed)>");
        game.GameInfo.Speed.Modifier = 1.5f;
        ForestAt(game.TileMap, 3, 2).SetOwningCity(city);
        game.TileMap[HexCoord.Of(3, 2)].SetImprovement("Remove Forest", civ);
        Assert.Equal(15, city.CityConstructions.ProductionOverflow);
    }

    [Fact]
    public void RestrictedBuildableImprovements_OnlyListedImprovementsMayBeBuilt()
    {
        var ruleset = TileRuleset();
        ruleset.Terrains["Hill"].Uniques = ["Rough terrain", "Only [Farm] improvements may be built on this tile"];
        ruleset.TileImprovements["Mine"] = new TileImprovement { Name = "Mine", TerrainsCanBeBuiltOn = ["Hill", "Grassland"] };
        ruleset.TileImprovements["Farm"].TerrainsCanBeBuiltOn = ["Hill", "Grassland"];
        var map = TileMapOf(ruleset);
        var hill = map[HexCoord.Of(2, 2)];
        SetBase(hill, "Hill");
        var grass = map[HexCoord.Of(1, 1)];

        Assert.True(hill.ImprovementFunctions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Farm"]));
        Assert.False(hill.ImprovementFunctions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Mine"]));
        Assert.True(grass.ImprovementFunctions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Mine"])); // unrestricted terrain
    }
}
