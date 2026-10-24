using Unciv.Core.Tests.Support;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Fixtures.CityFixtures;

// T072: port of tests/src/com/unciv/logic/city/managers/CityPopulationManagerTest.kt over the real TestGame.
// The "best job" cases depend on the city-work ranking, which is the ported Automation (T087).
[Collection("RulesetCache")]
public sealed class CityPopulationFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;
    private readonly CityObject city;

    public CityPopulationFixtureTests()
    {
        testGame.MakeHexagonalMap(3);
        civ = testGame.AddCiv();
        city = testGame.AddCity(civ, testGame.GetTile(HexCoord.Zero), initialPopulation: 1);
    }

    [Fact]
    public void ShouldIncreaseFoodRequirementsForNextPop()
    {
        var biggerCity = testGame.AddCity(civ, testGame.GetTile(new HexCoord(1, 0)), initialPopulation: 2);
        Assert.True(city.Population.GetFoodToNextPopulation() < biggerCity.Population.GetFoodToNextPopulation());
    }

    [Fact]
    public void ShouldChangeFoodRequirementsForDifferentGamespeeds()
    {
        var quickSpeedGame = new TestGame();
        quickSpeedGame.SetSpeed("Quick");
        var quickSpeedCity = quickSpeedGame.AddCity(quickSpeedGame.AddCiv(), quickSpeedGame.GetTile(HexCoord.Zero), initialPopulation: 1);

        var epicSpeedGame = new TestGame();
        epicSpeedGame.SetSpeed("Epic");
        var epicSpeedCity = epicSpeedGame.AddCity(epicSpeedGame.AddCiv(), epicSpeedGame.GetTile(HexCoord.Zero), initialPopulation: 1);

        var quickFoodRequirements = quickSpeedCity.Population.GetFoodToNextPopulation();
        var standardCityFoodRequirements = city.Population.GetFoodToNextPopulation();
        var epicCityFoodRequirements = epicSpeedCity.Population.GetFoodToNextPopulation();

        Assert.True(quickFoodRequirements < standardCityFoodRequirements);
        Assert.True(standardCityFoodRequirements < epicCityFoodRequirements);
    }

    [Fact]
    public void ShouldIncreaseFoodRequirementsForCityStates()
    {
        var cityState = testGame.AddCiv(cityStateType: "Militaristic");
        var cityStateCity = testGame.AddCity(cityState, testGame.GetTile(new HexCoord(1, 0)), initialPopulation: 1);

        Assert.True(city.Population.GetFoodToNextPopulation() < cityStateCity.Population.GetFoodToNextPopulation());
    }

    [Fact]
    public void ShouldIncreaseFoodRequirementsForAiOnEasierDifficulties()
    {
        var easierDifficultyGame = new TestGame();
        easierDifficultyGame.SetDifficulty("Chieftain");
        var easierCity = easierDifficultyGame.AddCity(easierDifficultyGame.AddCiv(), easierDifficultyGame.GetTile(HexCoord.Zero), initialPopulation: 1);

        Assert.True(easierCity.Population.GetFoodToNextPopulation() > city.Population.GetFoodToNextPopulation());
    }

    [Fact]
    public void ShouldDecreaseFoodRequirementsForAiOnHigherDifficulties()
    {
        var harderDifficultyGame = new TestGame();
        harderDifficultyGame.SetDifficulty("Deity");
        var harderCity = harderDifficultyGame.AddCity(harderDifficultyGame.AddCiv(), harderDifficultyGame.GetTile(HexCoord.Zero), initialPopulation: 1);

        Assert.True(harderCity.Population.GetFoodToNextPopulation() < city.Population.GetFoodToNextPopulation());
    }

    [Fact]
    public void ShouldRemoveOnePopWhenOnStarvationAndNoFoodReserves()
    {
        city.Population.SetPopulation(2);
        city.Population.FoodStored = 0;

        city.Population.NextTurn(-1);

        Assert.Equal(1, city.Population.Population);
    }

    [Fact]
    public void ShouldNotRemoveOnePopWhenOnStarvationButWithEnoughFoodReserves()
    {
        city.Population.SetPopulation(2);
        city.Population.FoodStored = 5;

        city.Population.NextTurn(-1);

        Assert.Equal(2, city.Population.Population);
    }

    [Fact]
    public void ShouldNotRemoveOnePopWhenOnStarvationButWith1Pop()
    {
        city.Population.FoodStored = 0;

        city.Population.NextTurn(-1);

        Assert.Equal(1, city.Population.Population);
    }

    [Fact]
    public void ShouldIncreasePopWhenEnoughFoodIsStored()
    {
        city.Population.FoodStored = 14;

        city.Population.NextTurn(1);

        Assert.Equal(2, city.Population.Population);
        Assert.Equal(0, city.Population.FoodStored);
    }

    [Fact]
    public void ShouldCarryOverExcessFoodWhenNewPopIsBorn()
    {
        city.Population.FoodStored = 14;

        city.Population.NextTurn(4);

        Assert.Equal(2, city.Population.Population);
        Assert.Equal(3, city.Population.FoodStored);
    }

    [Fact]
    public void ShouldStartWithFoodStoredWhenNewPopIsBornAndUniques()
    {
        var building = testGame.CreateBuilding("[50]% Food is carried over after population increases [in this city]");
        city.CityConstructions.AddBuilding(building);
        city.Population.FoodStored = 14;

        city.Population.NextTurn(1);

        Assert.Equal(2, city.Population.Population);
        Assert.Equal(7, city.Population.FoodStored);
    }

    [Fact]
    public void ShouldAutomaticallyAssignNewPopToJob()
    {
        city.Population.FoodStored = 14;

        city.Population.NextTurn(1);

        Assert.Equal(2, city.Population.Population);
        Assert.Equal(0, city.Population.GetFreePopulation());
    }

    [Fact]
    public void ShouldAutomaticallyAssignNewPopToBestJob()
    {
        city.ClearWorkedTiles();
        var anchorTile = city.TileMap[new HexCoord(-1, 0)];
        city.WorkTile(anchorTile);
        city.LockTile(anchorTile); // force the first pop to work on a specific tile to avoid being reassigned
        var goodTile = testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);
        goodTile.SetImprovementBasic("Farm");

        Assert.False(city.IsWorked(goodTile));

        city.Population.FoodStored = 14;

        city.Population.NextTurn(1);

        Assert.Equal(2, city.Population.Population);
        Assert.True(city.IsWorked(goodTile));
    }

    [Fact]
    public void ShouldAutomaticallyAssignNewPopToBestJobAccordingToCityFocus()
    {
        city.SetCityFocus(CityFocus.GoldFocus);
        city.ClearWorkedTiles();
        var anchorTile2 = city.TileMap[new HexCoord(-1, 0)];
        city.WorkTile(anchorTile2);
        city.LockTile(anchorTile2); // force the first pop to work on a specific tile to avoid being reassigned
        var goodFoodTile = testGame.SetTileTerrain(new HexCoord(1, 0), Constants.Grassland);
        goodFoodTile.SetImprovementBasic("Farm");
        Assert.False(city.IsWorked(goodFoodTile));

        var goodGoldTile = testGame.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        var goldImprovement = testGame.CreateTileImprovement("[+5 Gold]");
        goodGoldTile.SetImprovementBasic(goldImprovement);
        Assert.False(city.IsWorked(goodGoldTile));

        city.Population.FoodStored = 14;

        city.Population.NextTurn(1);

        Assert.Equal(2, city.Population.Population);
        Assert.True(city.IsWorked(goodGoldTile));
        Assert.False(city.IsWorked(goodFoodTile));
    }

    [Fact]
    public void ShouldAutomaticallyAssignNewPopToBestJobWithSpecialists()
    {
        city.SetCityFocus(CityFocus.GoldFocus);
        city.ClearWorkedTiles();
        var anchorTile3 = city.TileMap[new HexCoord(-1, 0)];
        city.WorkTile(anchorTile3);
        city.LockTile(anchorTile3); // force the first pop to work on a specific tile to avoid being reassigned
        var specialistBuilding = testGame.CreateBuilding();
        specialistBuilding.SpecialistSlots.Add("Merchant", 1);
        city.CityConstructions.AddBuilding(specialistBuilding);

        city.Population.FoodStored = 14;
        Assert.Empty(city.Population.SpecialistAllocations);

        city.Population.NextTurn(1);

        Assert.Equal(2, city.Population.Population);
        Assert.True(city.Population.SpecialistAllocations.ContainsKey("Merchant"));
    }
}
