using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Net.Remote;
using Unciv.Queries;
using Unciv.Queries.Tiles;
using Unciv.Utils;

namespace Unciv.Core.Tests.Queries.Tiles;

// C088: the city-site preview. The fog cases compare answers before and after changing something the viewer must not be able to see.
[Collection("RulesetCache")]
public class CitySiteQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public CitySiteQueryTests()
    {
        testGame.MakeHexagonalMap(8);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        foreach (var tile in testGame.TileMap.TileList) testGame.SetTileTerrain(tile.Position, "Desert"); // the map's tiles start as something greener
    }

    private void Explore(Civilization civ, Func<Unciv.Logic.Map.Tiles.Tile, bool>? only = null)
    {
        foreach (var tile in testGame.TileMap.TileList)
            if (only == null || only(tile)) tile.SetExplored(civ, true);
    }

    private QueryResponse Execute(IQuery query)
    {
        var envelope = registry.ToEnvelope(player.CivName, query);
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(testGame.GameInfo));
    }

    private CitySiteResult Site(int x, int y)
    {
        var response = Execute(new CitySiteQuery(x, y));
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        return response.Result!.Value.Deserialize<CitySiteResult>(UncivJson.DefaultOptions)!;
    }

    private static float Yield(CitySiteResult site, string stat) => site.Yields.FirstOrDefault(it => it.Stat == stat)?.Value ?? 0f;

    private static string Json(CitySiteResult site) => JsonSerializer.Serialize(site, UncivJson.DefaultOptions);

    [Fact]
    public void CountsTheLandInTheWorkRadiusAndTheCityCentreMinimum()
    {
        Explore(player);
        var site = Site(0, 0);
        Assert.True(site.CanSettle);
        Assert.Equal(3, site.Radius);
        Assert.Equal(37, site.Tiles.Count); // 1 + 6 + 12 + 18
        Assert.Equal(0, site.UnexploredTiles);
        Assert.Equal(2f, Yield(site, "Food")); // desert everywhere, so only the centre's minimum
        Assert.Equal(1f, Yield(site, "Production"));

        testGame.SetTileTerrain(new HexCoord(1, 0), "Grassland");
        testGame.GetTile(1, 0).SetTileResource(testGame.Ruleset.TileResources["Wheat"]);
        site = Site(0, 0);
        Assert.Equal(5f, Yield(site, "Food")); // grassland 2 + wheat 1 + centre 2
        Assert.Equal([("Wheat", 1)], site.Resources.Select(it => (it.Name, it.Amount)));
    }

    [Fact]
    public void AnUnexploredCandidateIsRefusedLikeAnUnknownTile()
    {
        Explore(player, it => it.Position.X > 0);
        var hidden = Execute(new CitySiteQuery(0, 0));
        var nowhere = Execute(new CitySiteQuery(-1, 0));
        Assert.False(hidden.Ok);
        Assert.Equal(nowhere.Code, hidden.Code);
    }

    [Fact]
    public void UnexploredTilesAreCountedAsUnknownAndNothingElse()
    {
        Explore(player, it => it.Position.X > 0 || it.Position.Y != 0);
        var before = Site(1, 1);
        Assert.True(before.UnexploredTiles > 0);
        Assert.DoesNotContain(new Unciv.Queries.Units.TilePos(0, 0), before.Tiles);

        // change everything the viewer has not explored: terrain, resource, a city: the answer is identical
        foreach (var tile in testGame.TileMap.TileList.Where(it => !it.IsExplored(player)))
        {
            testGame.SetTileTerrain(tile.Position, "Grassland");
            tile.SetTileResource(testGame.Ruleset.TileResources["Wheat"]);
        }
        var hiddenCity = testGame.AddCity(enemy, testGame.GetTile(0, 0));
        Assert.NotNull(hiddenCity);
        var after = Site(1, 1);
        Assert.Equal(Json(before), Json(after));
        Assert.True(after.CanSettle); // the unexplored city next door forbids it for the engine, but the answer must not say so
        Assert.Empty(after.Resources);
    }

    [Fact]
    public void ABuiltImprovementOutOfSightDoesNotChangeTheYield()
    {
        Explore(player);
        testGame.SetTileTerrain(new HexCoord(2, 0), "Grassland");
        var before = Site(0, 0);
        testGame.GetTile(2, 0).Improvement = "Farm";
        Assert.Equal(Json(before), Json(Site(0, 0)));
    }

    [Fact]
    public void ReportsWaterAndKnownCitiesThatAreTooClose()
    {
        Explore(player);
        testGame.SetTileTerrain(new HexCoord(3, 3), "Ocean");
        var water = Site(3, 3);
        Assert.False(water.CanSettle);
        Assert.Contains(water.Problems, it => it.Code == CitySiteProblemCodes.Water);

        var city = testGame.AddCity(enemy, testGame.GetTile(-4, 0));
        var near = Site(-2, 0);
        Assert.False(near.CanSettle);
        Assert.Contains(near.Problems, it => it.Code == CitySiteProblemCodes.TooCloseToCity && it.Subject == city.Name);
        Assert.True(Site(4, 0).CanSettle);
    }

    [Fact]
    public void ACityOnAnExploredTileIsKnownEvenInTheFog()
    {
        Explore(player);
        var city = testGame.AddCity(enemy, testGame.GetTile(-4, 0));
        Assert.Contains(Site(-3, 0).Problems, it => it.Code == CitySiteProblemCodes.TooCloseToCity && it.Subject == city.Name);
    }

    [Fact]
    public void ForeignTerritoryIsOnlyReportedOnVisibleTiles()
    {
        Explore(player);
        var city = testGame.AddCity(enemy, testGame.GetTile(7, 0));
        var tile = testGame.GetTile(6, 0);
        testGame.AddTileToCity(city, tile);
        tile.SetOwningCity(city);
        Assert.False(tile.IsVisible(player));
        Assert.DoesNotContain(Site(6, 0).Problems, it => it.Code == CitySiteProblemCodes.ForeignTerritory); // fogged: the owner is not known

        testGame.AddUnit("Warrior", player, testGame.GetTile(5, 0));
        player.Cache.UpdateViewableTiles();
        Assert.True(tile.IsVisible(player));
        Assert.Contains(Site(6, 0).Problems, it => it.Code == CitySiteProblemCodes.ForeignTerritory);
    }

    [Fact]
    public void AnswersNeverChangeTheGame()
    {
        Explore(player);
        ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(testGame.GameInfo, forceZip: false)));
        var before = Hash();
        Site(0, 0);
        Assert.Equal(before, Hash());
    }

    [Fact]
    public void ThePolicyAllowsItAndStillRefusesAnUnexploredTile()
    {
        var exposure = RemoteQueryPolicy.Default.Get("tiles.CitySite");
        Assert.True(exposure.Allowed);
        Assert.False(exposure.AskBudget);
    }
}
