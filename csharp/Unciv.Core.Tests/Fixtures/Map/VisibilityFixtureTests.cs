using Unciv.Core.Tests.Support;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Fixtures.MapFixtures;

// T141: port of tests/src/com/unciv/logic/map/VisibilityTests.kt over the real TestGame
// (Logic/Map/TileMapTests.cs holds the same cases over a hand-built ruleset, T039).
[Collection("RulesetCache")]
public sealed class VisibilityFixtureTests
{
    private readonly TestGame testGame = new();

    public VisibilityFixtureTests() => testGame.MakeHexagonalMap(5);

    [Fact]
    public void CanSeeNearbyForest()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        var forest = testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Forest);
        var viewableTiles = grassland.GetViewableTilesList(1);
        Assert.Contains(forest, viewableTiles);
    }

    [Fact]
    public void CanSeeForestOverPlains()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Plains);
        var forest = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Forest);

        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.Contains(forest, viewableTiles);
    }

    [Fact]
    public void CannotSeePlainsOverForest()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Forest);
        var plains = testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Plains);

        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.DoesNotContain(plains, viewableTiles);
    }

    [Fact]
    public void CannotSeeForestOverForest()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Forest);
        var plains = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Plains, Constants.Forest);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.DoesNotContain(plains, viewableTiles);
    }

    [Fact]
    public void CanSeeHillOverPlains()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Plains);
        var hill = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Hill);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.Contains(hill, viewableTiles);
    }

    [Fact]
    public void CannotSeePlainsOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var plains = testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Plains);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.DoesNotContain(plains, viewableTiles);
    }

    [Fact]
    public void CannotSeeHillOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var hill = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Hill);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.DoesNotContain(hill, viewableTiles);
    }

    [Fact]
    public void CannotSeeHillOverForest()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Forest);
        var hill = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Hill);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.DoesNotContain(hill, viewableTiles);
    }

    [Fact]
    public void CannotSeeForestOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var hill = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Forest);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.DoesNotContain(hill, viewableTiles);
    }

    [Fact]
    public void CanSeeHillForestOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Forest);
        var hill = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Hill, Constants.Forest);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.Contains(hill, viewableTiles);
    }

    [Fact]
    public void CanSeeMountainOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var mountain = testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Mountain);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.Contains(mountain, viewableTiles);
    }

    [Fact]
    public void CannotSeeMountainOverHillForest()
    {
        var grassland = testGame.SetTileTerrainAndFeatures(HexCoord.Zero, Constants.Grassland, Constants.Hill);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill, Constants.Forest);
        var mountain = testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Mountain);
        var viewableTiles = grassland.GetViewableTilesList(4);

        Assert.DoesNotContain(mountain, viewableTiles);
    }

    [Fact]
    public void CannotSee3TilesAwayPlain()
    {
        var source = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Grassland);
        var beyondSight = testGame.SetTileTerrain(new HexCoord(3, 0), Constants.Grassland);

        var viewableTiles = source.GetViewableTilesList(2);

        Assert.DoesNotContain(beyondSight, viewableTiles);
    }

    [Fact]
    public void CanSeeElevation3Tiles()
    {
        var source = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Grassland);
        var beyondSight = testGame.SetTileTerrainAndFeatures(new HexCoord(3, 0), Constants.Grassland, Constants.Hill);

        var viewableTiles = source.GetViewableTilesList(2);

        Assert.Contains(beyondSight, viewableTiles);
    }

    [Fact]
    public void CanSeeElevation3TilesEvenWithInvisibleIntermediate()
    {
        var source = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var intermediate = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Hill);
        var beyondSight = testGame.SetTileTerrainAndFeatures(new HexCoord(3, 0), Constants.Grassland, Constants.Hill, Constants.Forest);

        var viewableTiles = source.GetViewableTilesList(2);

        Assert.Contains(beyondSight, viewableTiles);
        Assert.DoesNotContain(intermediate, viewableTiles);
    }

    [Fact]
    public void CannotSeeHiddenElevation3Tiles()
    {
        var source = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Forest);
        var beyondSight = testGame.SetTileTerrainAndFeatures(new HexCoord(3, 0), Constants.Grassland, Constants.Hill);

        var viewableTiles = source.GetViewableTilesList(2);

        Assert.DoesNotContain(beyondSight, viewableTiles);
    }

    [Fact]
    public void CanSeeButNotAttackHillForestOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var hillForest = testGame.SetTileTerrainAndFeatures(new HexCoord(2, 0), Constants.Grassland, Constants.Hill, Constants.Forest);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.Contains(hillForest, viewableTiles);

        var attackableTiles = testGame.TileMap.GetViewableTiles(grassland.Position, 2, true);

        Assert.DoesNotContain(hillForest, attackableTiles);
    }

    [Fact]
    public void CanSeeAndAttackMountainOverHill()
    {
        var grassland = testGame.SetTileTerrain(HexCoord.Zero, Constants.Grassland);
        testGame.SetTileTerrainAndFeatures(new HexCoord(1, 0), Constants.Grassland, Constants.Hill);
        var mountain = testGame.SetTileTerrain(new HexCoord(2, 0), Constants.Mountain);
        var viewableTiles = grassland.GetViewableTilesList(2);

        Assert.Contains(mountain, viewableTiles);

        var attackableTiles = testGame.TileMap.GetViewableTiles(grassland.Position, 2, true);

        Assert.Contains(mountain, attackableTiles);
    }
}
