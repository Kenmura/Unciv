using Unciv.Core.Tests.Support;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/FreeBuildingTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class FreeBuildingFixtureTests
{
    private readonly TestGame testGame = new();

    public FreeBuildingFixtureTests() => testGame.MakeHexagonalMap(5);

    private Unciv.Logic.Civilization.Civilization CivWithAllTechs(string unique)
    {
        var civ = testGame.AddCiv([unique]);
        foreach (var tech in testGame.Ruleset.Technologies.Keys)
            civ.Tech.AddTechnology(tech);
        return civ;
    }

    private void AddFiveCities(Unciv.Logic.Civilization.Civilization civ)
    {
        testGame.AddCity(civ, testGame.GetTile(1, 1));
        testGame.AddCity(civ, testGame.GetTile(1, 2));
        testGame.AddCity(civ, testGame.GetTile(2, 2));
        testGame.AddCity(civ, testGame.GetTile(2, 1));
        testGame.AddCity(civ, testGame.GetTile(0, 1));
    }

    [Fact]
    public void ShouldOnlyGiveCheapestStatBuildingInSetAmountOfCities()
    {
        var civ = CivWithAllTechs("Provides the cheapest [Culture] building in your first [4] cities for free");
        AddFiveCities(civ);
        Assert.Equal(4, civ.Cities.Count(it => it.CityConstructions.IsBuilt("Monument")));
    }

    [Fact]
    public void ShouldOnlyGive1StatBuilding()
    {
        var civ = CivWithAllTechs("Provides the cheapest [Culture] building in your first [4] cities for free");
        var capitalCity = testGame.AddCity(civ, testGame.GetTile(1, 1));
        Assert.True(capitalCity.CityConstructions.IsBuilt("Monument"));
        Assert.DoesNotContain(capitalCity.CityConstructions.GetBuiltBuildings(), it => it.Name != "Monument" && it.Name != "Palace");
    }

    [Fact]
    public void ShouldOnlyGiveTheSpecificBuildingInSetAmountOfCities()
    {
        var civ = CivWithAllTechs("Provides a [Monument] in your first [4] cities for free");
        AddFiveCities(civ);
        Assert.Equal(4, civ.Cities.Count(it => it.CityConstructions.IsBuilt("Monument")));
    }

    [Fact]
    public void FreeSpecificBuildingsShouldOnlyGiveTheSpecificBuilding()
    {
        var civ = CivWithAllTechs("Provides a [Monument] in your first [4] cities for free");
        var capitalCity = testGame.AddCity(civ, testGame.GetTile(1, 1));
        Assert.True(capitalCity.CityConstructions.IsBuilt("Monument"));
        Assert.DoesNotContain(capitalCity.CityConstructions.GetBuiltBuildings(), it => it.Name != "Monument" && it.Name != "Palace");
    }

    [Fact]
    public void CanGiveSpecificBuildingsInAllCities()
    {
        var civ = CivWithAllTechs("Gain a free [Monument] [in all cities]");
        AddFiveCities(civ);
        Assert.Equal(5, civ.Cities.Count(it => it.CityConstructions.IsBuilt("Monument")));
    }
}
