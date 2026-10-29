using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Map;
using Unciv.Mathematics;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Utils;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// TileMap (T039). VisibilityTests.kt is ported with a hand-built ruleset instead of TestGame (T035/T072); UpgradeTests.kt needs
// civilizations and units and is ported in T072. The rest are hand-derived expectations from TileMap.kt.
public class TileMapTests
{
    private static Ruleset MapRuleset()
    {
        var ruleset = new Ruleset();
        void Add(string name, TerrainType type, params string[] uniques) =>
            ruleset.Terrains.Put(name, new Terrain { Name = name, Type = type, Uniques = [.. uniques] });
        Add("Desert", TerrainType.Land);
        Add("Grassland", TerrainType.Land);
        Add("Plains", TerrainType.Land);
        Add("Mountain", TerrainType.Land, "Rough terrain", "Has an elevation of [2] for visibility calculations");
        Add("Ocean", TerrainType.Water);
        Add("Hill", TerrainType.TerrainFeature, "Rough terrain", "Has an elevation of [1] for visibility calculations");
        Add("Forest", TerrainType.TerrainFeature, "Rough terrain", "Blocks line-of-sight from tiles at same elevation");
        return ruleset;
    }

    private static TileMap HexMap(int radius = 5) => new(radius, MapRuleset()) { MapParameters = { MapSize = new MapSize(radius) } };

    private static TileClass SetTile(TileMap map, HexCoord position, string baseTerrain, params string[] features)
    {
        var tile = map[position];
        tile.BaseTerrain = baseTerrain;
        tile.SetTerrainTransients();
        tile.SetTerrainFeatures(features);
        return tile;
    }

    private static List<TileClass> Viewable(TileMap map, TileClass from, int sight) => from.GetViewableTilesList(sight);

    // ---- VisibilityTests.kt

    [Fact]
    public void CanSeeNearbyForest()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        var forest = SetTile(map, HexCoord.Of(1, 0), "Grassland", "Forest");
        Assert.Contains(forest, Viewable(map, grassland, 1));
    }

    [Fact]
    public void CanSeeForestOverPlains()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Plains");
        var forest = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Forest");
        Assert.Contains(forest, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeePlainsOverForest()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Forest");
        var plains = SetTile(map, HexCoord.Of(2, 0), "Plains");
        Assert.DoesNotContain(plains, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeeForestOverForest()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Forest");
        var plains = SetTile(map, HexCoord.Of(2, 0), "Plains", "Forest");
        Assert.DoesNotContain(plains, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CanSeeHillOverPlains()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Plains");
        var hill = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Hill");
        Assert.Contains(hill, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeePlainsOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var plains = SetTile(map, HexCoord.Of(2, 0), "Plains");
        Assert.DoesNotContain(plains, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeeHillOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var hill = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Hill");
        Assert.DoesNotContain(hill, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeeHillOverForest()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Forest");
        var hill = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Hill");
        Assert.DoesNotContain(hill, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeeForestOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var forest = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Forest");
        Assert.DoesNotContain(forest, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CanSeeHillForestOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Forest");
        var hill = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Hill", "Forest");
        Assert.Contains(hill, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CanSeeMountainOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var mountain = SetTile(map, HexCoord.Of(2, 0), "Mountain");
        Assert.Contains(mountain, Viewable(map, grassland, 2));
    }

    [Fact]
    public void CannotSeeMountainOverHillForest()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland", "Hill");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill", "Forest");
        var mountain = SetTile(map, HexCoord.Of(2, 0), "Mountain");
        Assert.DoesNotContain(mountain, Viewable(map, grassland, 4));
    }

    [Fact]
    public void CannotSee3TilesAwayPlain()
    {
        var map = HexMap();
        var source = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland");
        SetTile(map, HexCoord.Of(2, 0), "Grassland");
        var beyondSight = SetTile(map, HexCoord.Of(3, 0), "Grassland");
        Assert.DoesNotContain(beyondSight, Viewable(map, source, 2));
    }

    [Fact]
    public void CanSeeElevation3Tiles()
    {
        var map = HexMap();
        var source = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland");
        SetTile(map, HexCoord.Of(2, 0), "Grassland");
        var beyondSight = SetTile(map, HexCoord.Of(3, 0), "Grassland", "Hill");
        Assert.Contains(beyondSight, Viewable(map, source, 2));
    }

    [Fact]
    public void CanSeeElevation3TilesEvenWithInvisibleIntermediate()
    {
        var map = HexMap();
        var source = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var intermediate = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Hill");
        var beyondSight = SetTile(map, HexCoord.Of(3, 0), "Grassland", "Hill", "Forest");
        var viewable = Viewable(map, source, 2);
        Assert.Contains(beyondSight, viewable);
        Assert.DoesNotContain(intermediate, viewable);
    }

    [Fact]
    public void CannotSeeHiddenElevation3Tiles()
    {
        var map = HexMap();
        var source = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland");
        SetTile(map, HexCoord.Of(2, 0), "Grassland", "Forest");
        var beyondSight = SetTile(map, HexCoord.Of(3, 0), "Grassland", "Hill");
        Assert.DoesNotContain(beyondSight, Viewable(map, source, 2));
    }

    [Fact]
    public void CanSeeButNotAttackHillForestOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var hillForest = SetTile(map, HexCoord.Of(2, 0), "Grassland", "Hill", "Forest");
        Assert.Contains(hillForest, Viewable(map, grassland, 2));
        Assert.DoesNotContain(hillForest, map.GetViewableTiles(grassland.Position, 2, true));
    }

    [Fact]
    public void CanSeeAndAttackMountainOverHill()
    {
        var map = HexMap();
        var grassland = SetTile(map, HexCoord.Zero, "Grassland");
        SetTile(map, HexCoord.Of(1, 0), "Grassland", "Hill");
        var mountain = SetTile(map, HexCoord.Of(2, 0), "Mountain");
        Assert.Contains(mountain, Viewable(map, grassland, 2));
        Assert.Contains(mountain, map.GetViewableTiles(grassland.Position, 2, true));
    }

    // ---- construction, lookup, distance

    [Fact]
    public void HexagonalConstructor_FillsWithFirstLandTerrain()
    {
        var map = HexMap(2);
        Assert.Equal(19, map.TileList.Count);
        Assert.All(map.TileList, it => Assert.Equal("Desert", it.BaseTerrain));
        Assert.Equal(-2, map.LeftX);
        Assert.Equal(-2, map.BottomY);
        Assert.Equal(map.TileList.Select((_, i) => i), map.TileList.Select(it => it.ZeroBasedIndex));
        Assert.Same(map, map.TileList[0].TileMap);
    }

    [Fact]
    public void RectangularConstructor_CoordinateRangesAndWorldWrapWidth()
    {
        var ruleset = MapRuleset();
        Assert.Equal(20, new TileMap(4, 5, ruleset).TileList.Count);
    }

    [Fact]
    public void RectangularConstructor_WorldWrapRoundsOddWidthDown()
    {
        var map = new TileMap(5, 3, MapRuleset(), true);
        Assert.Equal(12, map.TileList.Count);
        Assert.Equal(4, map.Width);
    }

    [Fact]
    public void GenerationFailsWithoutLandTerrain()
    {
        Assert.Throws<InvalidOperationException>(() => new TileMap(1, new Ruleset()));
    }

    [Fact]
    public void Lookup_ContainsGetAndOrNull()
    {
        var map = HexMap(2);
        Assert.True(map.Contains(HexCoord.Of(1, 1)));
        Assert.True(map.Contains(new Vector2(1.9f, 1f)));
        Assert.False(map.Contains(HexCoord.Of(5, 5)));
        Assert.False(map.Contains(HexCoord.Of(-9, 0)));
        Assert.Null(map.GetOrNull(2, -2)); // inside the bounding box, outside the hexagon
        Assert.Equal(HexCoord.Zero, map[HexCoord.Zero].Position);
        Assert.Throws<InvalidOperationException>(() => map[2, -2]);
    }

    [Fact]
    public void TilesAtDistance_RingSizesAndOrigin()
    {
        var map = HexMap(5);
        Assert.Equal([map[HexCoord.Zero]], map.GetTilesAtDistance(HexCoord.Zero, 0));
        Assert.Equal([map[HexCoord.Zero]], map.GetTilesAtDistance(HexCoord.Zero, -3));
        Assert.Equal(6, map.GetTilesAtDistance(HexCoord.Zero, 1).Count());
        Assert.Equal(12, map.GetTilesAtDistance(HexCoord.Zero, 2).Count());
        Assert.Equal(7, map.GetTilesInDistance(HexCoord.Zero, 1).Count());
        Assert.Equal(18, map.GetTilesInDistanceRange(HexCoord.Zero, new IntRange(1, 2)).Count());
        var seen = new List<TileClass>();
        map.ForEachTileInDistance(HexCoord.Zero, 3, seen.Add);
        Assert.Equal(37, seen.Count);
        Assert.Equal(seen.Distinct().Count(), seen.Count);
        // the ring at the map edge is clipped
        Assert.Equal(3, HexMap(1).GetTilesAtDistance(HexCoord.Of(1, 1), 1).Count(it => it.Position.x <= 1));
    }

    [Fact]
    public void ForEachTileAtDistance_SameOrderAsSequence_AndFilterApplies()
    {
        var map = HexMap(5);
        var viaSequence = map.GetTilesAtDistance(HexCoord.Of(1, 0), 3).ToList();
        var viaCallback = new List<TileClass>();
        map.ForEachTileAtDistance(HexCoord.Of(1, 0), 3, viaCallback.Add);
        Assert.Equal(viaSequence, viaCallback);
        var filtered = new List<TileClass>();
        map.ForEachTileAtDistance(HexCoord.Of(1, 0), 3, it => it.Position.x > 1, filtered.Add);
        Assert.Equal(viaSequence.Where(it => it.Position.x > 1), filtered);
    }

    [Fact]
    public void WorldWrap_FindsTilesOnTheOtherSide()
    {
        var map = new TileMap(6, 4, MapRuleset(), true) { MapParameters = { WorldWrap = true, Shape = MapShape.Rectangular, MapSize = new MapSize(6, 4) } };
        Assert.Null(map.GetOrNull(100, 100));
        var left = map.TileList.OrderBy(it => HexMath.GetColumn(it.Position)).First();
        Assert.Null(map.GetOrNull(left.Position.x - 3, left.Position.y + 3)); // radius = width / 2 = 3: just outside on the left...
        var wrapped = map.GetIfTileExistsOrNull(left.Position.x - 3, left.Position.y + 3);
        Assert.Same(left, wrapped); // ...wraps (x + r, y - r) back to the left tile itself
        map.MapParameters.WorldWrap = false;
        Assert.Null(map.GetIfTileExistsOrNull(100, 100));
    }

    [Fact]
    public void ClockPositions_NeighborsAndUnknown()
    {
        var map = HexMap(5);
        var center = map[HexCoord.Zero];
        Assert.Equal(6, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(-1, -1)]));
        Assert.Equal(12, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(1, 1)]));
        Assert.Equal(4, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(-1, 0)]));
        Assert.Equal(8, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(0, -1)]));
        Assert.Equal(10, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(1, 0)]));
        Assert.Equal(2, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(0, 1)]));
        Assert.Equal(-1, map.GetNeighborTileClockPosition(center, map[HexCoord.Of(2, 0)]));
        Assert.Same(map[HexCoord.Of(1, 1)], map.GetClockPositionNeighborTile(center, 12));
        Assert.Null(map.GetClockPositionNeighborTile(center, 5));
        Assert.Equal(HexMath.GetClockPositionToWorldVector(12), map.GetNeighborTilePositionAsWorldCoords(center, map[HexCoord.Of(1, 1)]));
    }

    [Fact]
    public void UnwrappedPosition_OutsideStaysInsidePicksNearerToOrigin()
    {
        var map = HexMap(5);
        Assert.Equal(HexCoord.Of(40, 40), map.GetUnwrappedPosition(HexCoord.Of(40, 40)));
        Assert.Equal(HexCoord.Of(5, -5), map.GetUnwrappedPosition(HexCoord.Of(0, 0))); // equal squares: not "right < left", so the left unwrap (x+5, y-5) wins
    }

    [Fact]
    public void TilesInRectangle_WalksColumnsAndRows()
    {
        var map = new TileMap(4, 4, MapRuleset());
        var tiles = map.GetTilesInRectangle(new Rectangle(-1, -1, 2, 2)).ToList();
        Assert.Equal(4, tiles.Count);
        Assert.Equal(tiles.Distinct().Count(), tiles.Count);
    }

    // ---- transients and bookkeeping

    [Fact]
    public void SetTransients_RequiresRulesetAndTiles()
    {
        Assert.Throws<InvalidOperationException>(() => new TileMap().SetTransients());
        Assert.Throws<InvalidOperationException>(() => new TileMap().SetTransients(MapRuleset()));
    }

    [Fact]
    public void SetTransients_RepeatedCallKeepsMatrix_ChangedSizeThrows()
    {
        var map = HexMap(2);
        map.SetTransients();
        Assert.Equal(5, map.TileMatrix.Count);
        map.LeftX = -10;
        Assert.Throws<InvalidOperationException>(() => map.SetTransients());
    }

    [Fact]
    public void GameInfo_IsLateinit()
    {
        var map = HexMap(1);
        Assert.False(map.HasGameInfo());
        Assert.Throws<InvalidOperationException>(() => map.GameInfo);
        map.GameInfo = new FakeGameInfo();
        Assert.True(map.HasGameInfo());
    }

    [Fact]
    public void LazyAggregates_AreComputedFromTiles()
    {
        var map = HexMap(2);
        Assert.Equal(4, map.MaxLatitude);
        Assert.Equal(2, map.MaxLongitude);
        Assert.Empty(map.NaturalWonders);
        Assert.Empty(map.Resources);
        Assert.Empty(new TileMap().NaturalWonders);
        Assert.Equal(0, new TileMap().MaxLatitude);
    }

    [Fact]
    public void Clone_CopiesTilesAndSharesCache()
    {
        var map = HexMap(1);
        map.Description = "d";
        map.StartingLocations.Add(new TileMap.StartingLocation(HexCoord.Zero, "Rome"));
        var clone = map.Clone();
        Assert.Equal(7, clone.TileList.Count);
        Assert.NotSame(map.TileList[0], clone.TileList[0]);
        Assert.NotSame(map.MapParameters, clone.MapParameters); // T219: copied, not shared
        Assert.Same(map.Ruleset, clone.Ruleset);
        Assert.Same(map.TileUniqueMapCache, clone.TileUniqueMapCache);
        Assert.Equal("d", clone.Description);
        Assert.Single(clone.StartingLocations);
        Assert.Empty(clone.TileMatrix);
        clone.SetTransients();
        Assert.Same(clone, clone.TileList[0].TileMap);
    }

    [Fact]
    public void StartingLocations_AddRemoveAndTransients()
    {
        var map = HexMap(2);
        var a = map[HexCoord.Zero];
        var b = map[HexCoord.Of(1, 0)];
        Assert.True(map.AddStartingLocation("Rome", a));
        Assert.False(map.AddStartingLocation("Rome", a, TileMap.StartingLocation.UsageType.Human));
        Assert.True(map.AddStartingLocation("Rome", b));
        Assert.True(map.AddStartingLocation("Greece", a));
        Assert.Equal(3, map.StartingLocations.Count);
        Assert.Equal(TileMap.StartingLocation.UsageType.Player, map.StartingLocations[0].Usage);

        Assert.True(map.RemoveStartingLocation("Rome", a));
        Assert.False(map.RemoveStartingLocation("Rome", a));
        Assert.Equal(2, map.StartingLocations.Count);

        map.StartingLocationsByNation.Clear();
        map.SetStartingLocationsTransients();
        Assert.Contains(b, map.StartingLocationsByNation["Rome"]);

        map.RemoveStartingLocations("Rome");
        Assert.Single(map.StartingLocations);
        Assert.Empty(map.StartingLocationsByNation["Rome"]);
        map.RemoveStartingLocations("Nobody"); // no-op

        map.RemoveStartingLocations(a.Position);
        Assert.Empty(map.StartingLocations);
        map.AddStartingLocation("Greece", a);
        map.ClearStartingLocations();
        Assert.Empty(map.StartingLocations);
        Assert.Empty(map.StartingLocationsByNation);
    }

    [Fact]
    public void StartingLocation_DefaultsAndUsageLabels()
    {
        var location = new TileMap.StartingLocation();
        Assert.Equal(HexCoord.Zero, location.Position);
        Assert.Equal("", location.Nation);
        Assert.Equal(TileMap.StartingLocation.DefaultUsage, location.Usage);
        Assert.Equal(["None", "Player", "Human"], Enum.GetValues<TileMap.StartingLocation.UsageType>().Select(TileMap.StartingLocation.GetLabel));
    }

    [Fact]
    public void RemoveMissingTerrainModReferences_DropsUnknownNations()
    {
        var map = HexMap(1);
        map.Ruleset!.Nations.Put("Rome", new Unciv.Models.Ruleset.Nation.Nation { Name = "Rome" });
        map.StartingLocations.Add(new TileMap.StartingLocation(HexCoord.Zero, "Rome"));
        map.StartingLocations.Add(new TileMap.StartingLocation(HexCoord.Zero, "Atlantis"));
        map.RemoveMissingTerrainModReferences(map.Ruleset);
        Assert.Equal(["Rome"], map.StartingLocations.Select(it => it.Nation));
    }

    [Fact]
    public void GetRulesetIncompatibility_ReportsMissingNationsAndTerrain()
    {
        var map = HexMap(1);
        map.StartingLocations.Add(new TileMap.StartingLocation(HexCoord.Zero, "Atlantis"));
        var incompatibilities = map.GetRulesetIncompatibility(map.Ruleset!);
        Assert.Equal(["Nation [Atlantis] does not exist in ruleset!"], incompatibilities.ToList());

        map.TileList[0].BaseTerrain = "Lava"; // a transients failure ends the check early with what was found so far
        var broken = map.GetRulesetIncompatibility(map.Ruleset!);
        Assert.Equal(["Base terrain [Lava] does not exist in ruleset!"], broken.ToList());
    }

    // ---- continents

    private static TileMap IslandMap()
    {
        var map = HexMap(3);
        foreach (var tile in map.TileList) SetTile(map, tile.Position, "Ocean");
        // island A: three tiles in a row; island B: one tile
        foreach (var position in new[] { HexCoord.Of(0, 0), HexCoord.Of(1, 0), HexCoord.Of(2, 0) }) SetTile(map, position, "Grassland");
        SetTile(map, HexCoord.Of(-2, -1), "Grassland");
        foreach (var tile in map.TileList) tile.SetTransients();
        return map;
    }

    [Fact]
    public void AssignContinents_NumbersByLandTilesAndSortsBySize()
    {
        var map = IslandMap();
        map.AssignContinents(TileMap.AssignContinentsMode.Assign);
        Assert.Equal(2, map.ContinentSizes.Count);
        Assert.Equal(3, map.ContinentSizes.Values.Max());
        Assert.Equal(1, map.ContinentSizes.Values.Min());
        var big = map[HexCoord.Zero].GetContinent();
        Assert.Equal(big, map[HexCoord.Of(2, 0)].GetContinent());
        Assert.NotEqual(big, map[HexCoord.Of(-2, -1)].GetContinent());
        Assert.Equal(-1, map[HexCoord.Of(0, 1)].GetContinent());
        Assert.Equal(big, map.ContinentsSortedBySize[0]);
        Assert.True(map.UsingArchipelagoRegions() == (3f / 4f < 0.25f));
    }

    [Fact]
    public void AssignContinents_EnsureKeepsExistingAndClearResets()
    {
        var map = IslandMap();
        map.AssignContinents(TileMap.AssignContinentsMode.Ensure); // empty: assigns
        var sizes = map.ContinentSizes.ToList();
        map.ContinentSizes.Clear();
        map.AssignContinents(TileMap.AssignContinentsMode.Ensure); // rebuilt from tile data
        Assert.Equal(sizes, map.ContinentSizes.ToList());
        map.AssignContinents(TileMap.AssignContinentsMode.Reassign);
        Assert.Equal(2, map.ContinentSizes.Count);
        map.AssignContinents(TileMap.AssignContinentsMode.Clear);
        Assert.True(map.ContinentSizes.IsEmpty);
        Assert.Empty(map.ContinentsSortedBySize);
        Assert.All(map.TileList, it => Assert.Equal(-1, it.GetContinent()));
    }

    [Fact]
    public void IsWaterMap_CountsBigIslandsAgainstPlayers()
    {
        var map = IslandMap();
        map.GameInfo = new FakeGameInfo();
        Assert.False(map.IsWaterMap()); // no continent over 20 tiles, but 3 default players
        map.GameInfo.GameParameters.Players.Clear();
        Assert.True(map.IsWaterMap()); // 0 >= 0
    }

    [Fact]
    public void Preview_FiltersUsage()
    {
        var preview = new TileMap.Preview();
        Assert.Empty(preview.GetDeclaredNations());
        Assert.Empty(preview.GetNationsForHumanPlayer());
    }

    // T262: the original line-of-sight scan (every tile seen so far, filtered by aerial distance 1), kept as the oracle of the neighbour lookup.
    private static List<TileClass> ReferenceViewableTiles(TileMap map, HexCoord position, int sightDistance, bool forAttack)
    {
        var aUnitHeight = map[position].UnitHeight;
        var viewableTiles = new List<TileMap.ViewableTile> { new(map[position], aUnitHeight, true, false) };
        for (var i = 1; i <= sightDistance + 1; i++)
        {
            var tilesToAddInDistanceI = new List<TileMap.ViewableTile>();
            var layer = i;
            map.ForEachTileAtDistance(position, i, cTile =>
            {
                var cTileHeight = cTile.TileHeight;
                if (layer == sightDistance + 1 && (cTileHeight <= aUnitHeight || forAttack)) return;
                var b = viewableTiles.Where(it => it.Tile.AerialDistanceTo(cTile) == 1).Min(it => it.MaxHeightSeenToTile);
                tilesToAddInDistanceI.Add(new TileMap.ViewableTile(cTile, System.Math.Max(cTileHeight, b),
                    aUnitHeight >= b || cTileHeight > b, aUnitHeight >= b || cTile.UnitHeight > b));
            });
            viewableTiles.AddRange(tilesToAddInDistanceI);
        }
        return forAttack ? viewableTiles.Where(it => it.IsAttackable).Select(it => it.Tile).ToList()
            : viewableTiles.Where(it => it.IsVisible).Select(it => it.Tile).ToList();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetViewableTiles_EqualsTheFullScan_ListAndOrder(bool wrapped)
    {
        var map = wrapped
            ? new TileMap(8, 6, MapRuleset(), true) { MapParameters = { WorldWrap = true, Shape = MapShape.Rectangular, MapSize = new MapSize(8, 6) } }
            : HexMap(7);
        string[] bases = ["Grassland", "Plains", "Desert", "Mountain", "Ocean"];
        string[][] features = [[], [], ["Hill"], ["Forest"], ["Hill", "Forest"]];
        var n = 0;
        foreach (var tile in map.Values.ToList())
        {
            n++;
            var baseTerrain = bases[System.Math.Abs(tile.Position.x * 7 + tile.Position.y * 13 + n) % bases.Length];
            SetTile(map, tile.Position, baseTerrain, baseTerrain is "Grassland" or "Plains" ? features[System.Math.Abs(n * 3 + tile.Position.x) % features.Length] : []);
        }
        foreach (var tile in map.Values)
            foreach (var sight in new[] { 0, 1, 2, 3, 4 })
                foreach (var forAttack in new[] { false, true })
                    Assert.Equal(ReferenceViewableTiles(map, tile.Position, sight, forAttack), map.GetViewableTiles(tile.Position, sight, forAttack));
    }
}
