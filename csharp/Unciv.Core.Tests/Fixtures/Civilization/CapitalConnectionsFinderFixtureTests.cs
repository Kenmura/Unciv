using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.Tiles;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/CapitalConnectionsFinderTests.kt over the real TestGame.
// Legend to maps: A capital, - road, = railroad, C connected city, N not connected city, O open borders, X closed borders, ~ water.
// The two commented-out harbor cases of the Kotlin file stay out (they are commented out upstream too).
[Collection("RulesetCache")]
public sealed class CapitalConnectionsFinderFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization ourCiv;

    public CapitalConnectionsFinderFixtureTests()
    {
        testGame.MakeHexagonalMap(5);
        ourCiv = testGame.AddCiv();

        // Add techs to utilize roads
        ourCiv.Tech.AddTechnology(testGame.Ruleset.TileImprovements[nameof(RoadStatus.Road)].TechRequired!);
        ourCiv.Tech.AddTechnology(testGame.Ruleset.TileImprovements[nameof(RoadStatus.Railroad)].TechRequired!);
    }

    private void CreateMedium(int from, int to, RoadStatus type)
    {
        for (var i = from; i <= to; i++)
            testGame.TileMap[0, i].RoadStatus = type;
    }

    private void MeetCivAndSetBorders(Civilization civ, bool areBordersOpen)
    {
        ourCiv.GetDiplomacyManager(civ)!.MakePeace();
        ourCiv.GetDiplomacyManager(civ)!.HasOpenBorders = areBordersOpen;
    }

    [Fact]
    public void OwnCitiesAreConnectedByRoad()
    {
        // Map: C-A N
        testGame.AddCity(ourCiv, testGame.TileMap[0, 0]);
        var connectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, -2]);
        var notConnectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, 2]);

        CreateMedium(-2, 0, RoadStatus.Road);
        ourCiv.Cache.UpdateCitiesConnectedToCapital();

        Assert.True(connectedCity.IsConnectedToCapital());
        Assert.False(notConnectedCity.IsConnectedToCapital());
    }

    [Fact]
    public void OwnCitiesAreConnectedByRailroad()
    {
        // Map: N A=C
        testGame.AddCity(ourCiv, testGame.TileMap[0, 0]);
        var connectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, 2]);
        var notConnectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, -2]);
        CreateMedium(0, 2, RoadStatus.Railroad);

        ourCiv.Cache.UpdateCitiesConnectedToCapital();

        Assert.True(connectedCity.IsConnectedToCapital());
        Assert.False(notConnectedCity.IsConnectedToCapital());
    }

    [Fact]
    public void CitiesAreConnectedByRoadsViaOpenBorders()
    {
        // Map: N-X=A-O=C
        testGame.AddCity(ourCiv, testGame.TileMap[0, 0]);
        var connectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, 4]);
        var notConnectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, -4]);

        var openCiv = testGame.AddCiv();
        testGame.AddCity(openCiv, testGame.TileMap[0, 2]);
        MeetCivAndSetBorders(openCiv, true);

        // The path to "not connected" goes through closed territory
        var closedCiv = testGame.AddCiv();
        testGame.AddCity(closedCiv, testGame.TileMap[0, -2]);
        MeetCivAndSetBorders(closedCiv, false);

        CreateMedium(-4, -2, RoadStatus.Road);
        CreateMedium(-2, 0, RoadStatus.Railroad);
        CreateMedium(0, 2, RoadStatus.Road);
        CreateMedium(2, 4, RoadStatus.Railroad);

        ourCiv.Cache.UpdateCitiesConnectedToCapital();

        Assert.True(connectedCity.IsConnectedToCapital());
        Assert.False(notConnectedCity.IsConnectedToCapital());
    }

    [Fact]
    public void CitiesAreConnectedByRoadsViaCityStates()
    {
        // Map: N=X-A=O-C
        testGame.AddCity(ourCiv, testGame.TileMap[0, 0]);
        var connectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, 4]);
        var notConnectedCity = testGame.AddCity(ourCiv, testGame.TileMap[0, -4]);

        var openCiv = testGame.AddCiv(cityStateType: "Cultured");
        testGame.AddCity(openCiv, testGame.TileMap[0, 2]);
        ourCiv.GetDiplomacyManager(openCiv)!.MakePeace();

        var closedCiv = testGame.AddCiv(cityStateType: "Cultured");
        testGame.AddCity(closedCiv, testGame.TileMap[0, -2]);
        ourCiv.DiplomacyFunctions.MakeCivilizationsMeet(closedCiv);
        ourCiv.GetDiplomacyManager(closedCiv)!.DeclareWar();

        CreateMedium(-4, -2, RoadStatus.Railroad);
        CreateMedium(-2, 0, RoadStatus.Road);
        CreateMedium(0, 2, RoadStatus.Railroad);
        CreateMedium(2, 4, RoadStatus.Road);

        ourCiv.Cache.UpdateCitiesConnectedToCapital();

        Assert.True(connectedCity.IsConnectedToCapital());
        Assert.False(notConnectedCity.IsConnectedToCapital());
    }
}
