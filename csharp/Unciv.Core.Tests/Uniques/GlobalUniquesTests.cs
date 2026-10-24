using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Uniques;

// T143: port of tests/src/com/unciv/uniques/GlobalUniquesTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class GlobalUniquesTests
{
    private const float Epsilon = 0.01f; // for float comparisons

    private readonly TestGame game = new();

    // region base stat bonus providing uniques

    [Fact]
    public void StatsOnBuilding()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var building = game.CreateBuilding("[+1 Food]");

        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.True(city.CityStats.FinalStatList["Buildings"].Equals(new Stats(food: 1f)));
    }

    [Fact]
    public void StatsNotOnBuilding()
    {
        var civInfo = game.AddCiv(["[+2 Gold]"]);
        civInfo.UpdateStatsForNextTurn();
        Assert.True(civInfo.Stats.StatsForNextTurn.Equals(new Stats(gold: 2f)));
    }

    [Fact]
    public void StatsHappinessNotOnBuilding()
    {
        var civInfo = game.AddCiv(["[+7 Happiness]"]);
        civInfo.UpdateStatsForNextTurn();
        Assert.True(civInfo.Stats.Happiness == civInfo.GetDifficulty().BaseHappiness + 7);
    }

    [Fact]
    public void StatsPerCity()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var building = game.CreateBuilding("[+1 Production] [in this city]");

        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.True(city.CityStats.FinalStatList["Buildings"].Equals(new Stats(production: 1f)));
    }

    [Fact]
    public void StatsPerSpecialist()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, initialPopulation: 2);
        var building = game.CreateBuilding("[+3 Gold] from every specialist [in this city]");
        var specialistName = game.CreateSpecialist();
        building.SpecialistSlots.Add(specialistName, 2);
        city.Population.SpecialistAllocations[specialistName] = 2;

        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.True(city.CityStats.FinalStatList["Specialists"].Equals(new Stats(gold: 6f)));
    }

    [Fact]
    public void StatsPerPopulation()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, initialPopulation: 4);
        var building = game.CreateBuilding("[+3 Gold] per [2] population [in this city]");

        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.Equal(6f, city.CityStats.FinalStatList["Buildings"].Gold);
    }

    [Fact]
    public void StatsPerXPopulation()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, initialPopulation: 2);
        var building = game.CreateBuilding("[+3 Gold] <in cities with at least [3] [Population]>");

        city.CityConstructions.AddBuilding(building);

        city.CityStats.Update();
        Assert.Equal(0f, city.CityStats.FinalStatList["Buildings"].Gold);
        city.Population.SetPopulation(5);
        city.CityStats.Update();
        Assert.Equal(3f, city.CityStats.FinalStatList["Buildings"].Gold);
    }

    [Fact]
    public void StatsFromCitiesOnSpecificTiles()
    {
        var civInfo = game.AddCiv();
        var tile = game.SetTileTerrain(HexCoord.Zero, Constants.Desert);
        var city = game.AddCity(civInfo, tile, true);
        var building = game.CreateBuilding($"[+3 Gold] in cities on [{Constants.Desert}] tiles");
        city.CityConstructions.AddBuilding(building);

        city.CityStats.Update();
        Assert.Equal(3f, city.CityStats.FinalStatList["Buildings"].Gold);
        tile.BaseTerrain = Constants.Grassland;
        tile.SetTransients();
        city.CityStats.Update();
        Assert.Equal(0f, city.CityStats.FinalStatList["Buildings"].Gold);
    }

    [Fact]
    public void StatsFromTiles()
    {
        game.MakeHexagonalMap(2);
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var building = game.CreateBuilding($"[+4 Gold] from [{Constants.Grassland}] tiles [in all cities]");
        city.CityConstructions.AddBuilding(building);

        var tile2 = game.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        Assert.Equal(4f, tile2.Stats.GetTileStats(city, civInfo).Gold);
    }

    [Fact]
    public void StatsFromTilesMultifilter()
    {
        game.MakeHexagonalMap(2);
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var building = game.CreateBuilding($"[+4 Gold] from [{{{Constants.Grassland}}} {{Farm}}] tiles [in all cities]");
        city.CityConstructions.AddBuilding(building);

        var tile2 = game.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        tile2.SetImprovement("Farm");
        Assert.Equal(4f, tile2.Stats.GetTileStats(city, civInfo).Gold);
    }

    [Fact]
    public void StatsFromTilesWithout()
    {
        game.MakeHexagonalMap(3);
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var building = game.CreateBuilding($"[+4 Gold] from [{Constants.Grassland}] tiles without [{Constants.Forest}] [in this city]");
        city.CityConstructions.AddBuilding(building);

        var tile2 = game.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        game.AddTileToCity(city, tile2);
        Assert.Equal(4f, tile2.Stats.GetTileStats(city, civInfo).Gold, 0.005f);

        var tile3 = game.SetTileTerrainAndFeatures(new HexCoord(0, 2), Constants.Grassland, Constants.Forest);
        game.AddTileToCity(city, tile3);
        Assert.False(tile3.Stats.GetTileStats(city, civInfo).Gold == 4f);
    }

    [Fact]
    public void StatsFromObject()
    {
        game.MakeHexagonalMap(1);
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, initialPopulation: 2);
        var specialist = game.CreateSpecialist();
        var building = game.CreateBuilding($"[+3 Faith] from every [{specialist}]");

        city.CityConstructions.AddBuilding(building);
        city.Population.SpecialistAllocations[specialist] = 2;

        city.CityStats.Update();
        Assert.Equal(6f, city.CityStats.FinalStatList["Specialists"].Faith);

        city.CityConstructions.RemoveBuilding(building);
        var building2 = game.CreateBuilding($"[+3 Faith] from every [{Constants.Grassland}]");
        city.CityConstructions.AddBuilding(building2);

        var tile2 = game.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        Assert.Equal(3f, tile2.Stats.GetTileStats(city, civInfo).Faith);

        city.CityConstructions.RemoveBuilding(building2);

        var emptyBuilding = game.CreateBuilding();

        var building3 = game.CreateBuilding($"[+3 Faith] from every [{emptyBuilding.Name}]");
        city.CityConstructions.AddBuilding(emptyBuilding);
        city.CityConstructions.AddBuilding(building3);
        city.CityStats.Update();
        Assert.Equal(3f, city.CityStats.FinalStatList["Buildings"].Faith);
    }

    [Fact]
    public void StatsFromTradeRoute()
    {
        game.MakeHexagonalMap(3);
        var civInfo = game.AddCiv(["[+30 Science] from each Trade Route"]);
        civInfo.Tech.AddTechnology("The Wheel"); // Required to form trade routes
        var tile1 = game.GetTile(HexCoord.Zero);
        var tile2 = game.GetTile(new HexCoord(0, 2));
        tile1.RoadStatus = RoadStatus.Road;
        tile2.RoadStatus = RoadStatus.Road;
        game.AddCity(civInfo, tile1);
        var city2 = game.AddCity(civInfo, tile2);
        var inBetweenTile = game.GetTile(new HexCoord(0, 1));
        inBetweenTile.RoadStatus = RoadStatus.Road;
        civInfo.Cache.UpdateCitiesConnectedToCapital();
        city2.CityStats.Update();

        Assert.Equal(30f, city2.CityStats.FinalStatList["Trade routes"].Science);
    }

    [Fact]
    public void StatsFromPolicies()
    {
        game.MakeHexagonalMap(3);
        var civInfo = game.AddCiv(["[+30 Science] per [2] social policies adopted"]);
        string[] policiesToAdopt = ["Tradition", "Aristocracy", "Legalism"];
        civInfo.Policies.FreePolicies = 3;
        foreach (var policyName in policiesToAdopt)
        {
            var policy = game.Ruleset.Policies[policyName];
            civInfo.Policies.Adopt(policy);
        }
        Assert.Equal(30f, civInfo.Stats.GetStatMapForNextTurn()["Policies"].Science);
    }

    [Fact]
    public void UniqueTypeOneTimeAdoptPolicyOrBelief()
    {
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civ, tile, true);

        // Policy
        Assert.False(civ.Policies.IsAdopted("Legalism"));
        city.CityConstructions.AddBuilding(game.CreateBuilding("Adopt [Legalism]"));
        Assert.True(civ.Policies.IsAdopted("Legalism"));

        // Belief
        Assert.False(civ.ReligionManager.Religion?.HasBelief("Dance of the Aurora") ?? false);
        city.CityConstructions.AddBuilding(game.CreateBuilding("Adopt [Dance of the Aurora]")); // Expected to not work, since we require a Religion
        Assert.False(civ.ReligionManager.Religion?.HasBelief("Dance of the Aurora") ?? false);
        game.AddReligion(civ); // With the Religion, we should be able to adopt the belief
        Assert.False(civ.ReligionManager.Religion?.HasBelief("Dance of the Aurora") ?? false);
        city.CityConstructions.AddBuilding(game.CreateBuilding("Adopt [Dance of the Aurora]"));
        Assert.True(civ.ReligionManager.Religion?.HasBelief("Dance of the Aurora") ?? false);
    }

    [Fact]
    public void StatsFromGlobalCitiesFollowingReligion()
    {
        var civ1 = game.AddCiv();
        var religion = game.AddReligion(civ1);
        var belief = game.CreateBelief(BeliefType.Founder, "[+30 Science] for each global city following this religion");
        religion.AddBeliefs([belief]);
        var civ2 = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var cityOfCiv2 = game.AddCity(civ2, tile, initialPopulation: 1); // Need someone to be converted
        cityOfCiv2.Religion.AddPressure(religion.Name, 1000);

        Assert.Equal(religion.Name, cityOfCiv2.Religion.GetMajorityReligionName());

        civ1.UpdateStatsForNextTurn();

        Assert.Equal(30f, civ1.Stats.StatsForNextTurn.Science);
    }

    [Fact]
    public void HappinessFromGlobalCitiesFollowingReligion()
    {
        var civ1 = game.AddCiv();
        var religion = game.AddReligion(civ1);
        var belief = game.CreateBelief(BeliefType.Founder, "[+42 Happiness] for each global city following this religion");
        religion.AddBeliefs([belief]);
        var civ2 = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var cityOfCiv2 = game.AddCity(civ2, tile, initialPopulation: 1); // Need someone to be converted
        cityOfCiv2.Religion.AddPressure(religion.Name, 1000);

        civ1.UpdateStatsForNextTurn();

        var baseHappiness = civ1.GetDifficulty().BaseHappiness;
        // Since civ1 has no cities, there are no other happiness sources
        Assert.Equal(baseHappiness + 42, civ1.Stats.Happiness);
    }

    [Fact]
    public void StatsFromGlobalFollowers()
    {
        var civ1 = game.AddCiv();
        var religion = game.AddReligion(civ1);
        var belief = game.CreateBelief(BeliefType.Founder, "[+30 Science] from every [3] global followers [in all cities]");
        religion.AddBeliefs([belief]);
        var civ2 = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var cityOfCiv2 = game.AddCity(civ2, tile, initialPopulation: 9); // Need people to be converted
        cityOfCiv2.Religion.AddPressure(religion.Name, 1000000000); // To completely overwhelm the default atheism in a city

        civ1.UpdateStatsForNextTurn();

        Assert.Equal(90f, civ1.Stats.StatsForNextTurn.Science);
    }

    // endregion

    // region stat percentage bonus providing uniques

    [Fact]
    public void StatPercentBonus()
    {
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civ, tile, true);
        var building = game.CreateBuilding("[+10 Science]", "[+200]% [Science]");
        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();

        Assert.Equal(30f, city.CityStats.FinalStatList["Buildings"].Science);
    }

    [Fact]
    public void StatPercentBonusCities()
    {
        var civ = game.AddCiv(["[+200]% [Science] [in all cities]"]);
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civ, tile, true);
        var building = game.CreateBuilding("[+10 Science]");
        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();

        Assert.Equal(30f, city.CityStats.FinalStatList["Buildings"].Science);
    }

    [Fact]
    public void StatPercentFromObject()
    {
        game.MakeHexagonalMap(1);
        var emptyBuilding = game.CreateBuilding();
        var civInfo = game.AddCiv([
            "[+3 Faith] from every [Farm]",
            $"[+200]% [Faith] from every [{emptyBuilding.Name}]",
            "[+200]% [Faith] from every [Farm]",
        ]);
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var faithBuilding = game.CreateBuilding();
        faithBuilding.Faith = 3f;
        city.CityConstructions.AddBuilding(faithBuilding);

        var tile2 = game.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        tile2.SetImprovement("Farm");
        Assert.Equal(9f, tile2.Stats.GetTileStats(city, civInfo).Faith);

        city.CityConstructions.AddBuilding(emptyBuilding);
        city.CityStats.Update();

        Assert.Equal(9f, city.CityStats.FinalStatList["Buildings"].Faith);
    }

    [Fact]
    public void AllStatsPercentFromObject()
    {
        game.MakeHexagonalMap(1);
        var emptyBuilding = game.CreateBuilding();
        var civInfo = game.AddCiv([
            "[+3 Faith] from every [Farm]",
            $"[+200]% Yield from every [{emptyBuilding.Name}]",
            "[+200]% Yield from every [Farm]",
        ]);
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var faithBuilding = game.CreateBuilding();
        faithBuilding.Faith = 3f;
        city.CityConstructions.AddBuilding(faithBuilding);

        var tile2 = game.SetTileTerrain(new HexCoord(0, 1), Constants.Grassland);
        tile2.SetImprovement("Farm");
        Assert.Equal(9f, tile2.Stats.GetTileStats(city, civInfo).Faith);

        city.CityConstructions.AddBuilding(emptyBuilding);
        city.CityStats.Update();

        Assert.Equal(9f, city.CityStats.FinalStatList["Buildings"].Faith);
    }

    [Fact]
    public void StatPercentFromReligionFollowers()
    {
        game.MakeHexagonalMap(1);
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, 1);
        var religion = game.AddReligion(civInfo);
        var belief = game.CreateBelief(BeliefType.Follower, "[+10]% [Faith] from every follower, up to [42]%");
        religion.AddBeliefs([belief]);

        city.Religion.AddPressure(religion.Name, 1000000000);

        Assert.Equal(religion.Name, city.Religion.GetMajorityReligionName());

        city.CityStats.Update();
        Assert.Equal(10f, city.CityStats.StatPercentBonusTree.TotalStats.Faith);

        city.Population.SetPopulation(10);
        city.CityStats.Update();
        Assert.Equal(42f, city.CityStats.StatPercentBonusTree.TotalStats.Faith);
    }

    [Fact]
    public void BonusStatsFromCityStates()
    {
        game.MakeHexagonalMap(1);
        var civInfo = game.AddCiv();
        var cityState = game.AddCiv(cityStateType: "Maritime");

        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        var cityStateTile = game.GetTile(new HexCoord(0, 1));
        game.AddCity(cityState, cityStateTile, true);
        civInfo.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
        cityState.GetDiplomacyManager(civInfo)!.AddInfluence(100f);

        city.CityStats.Update();
        Assert.Equal(3f, city.CityStats.FinalStatList[Constants.CityStates].Food);

        var building = game.CreateBuilding("[+100]% [Food] from City-States");
        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.Equal(6f, city.CityStats.FinalStatList[Constants.CityStates].Food);
    }

    [Fact]
    public void StatPercentFromTradeRoutes()
    {
        game.MakeHexagonalMap(3);
        var civInfo = game.AddCiv(["[+30 Science] from each Trade Route", "[+100]% [Science] from Trade Routes"]);
        civInfo.Tech.AddTechnology("The Wheel"); // Required to form trade routes
        var tile1 = game.GetTile(HexCoord.Zero);
        var tile2 = game.GetTile(new HexCoord(0, 2));
        tile1.RoadStatus = RoadStatus.Road;
        tile2.RoadStatus = RoadStatus.Road;
        game.AddCity(civInfo, tile1);
        var city2 = game.AddCity(civInfo, tile2);
        var inBetweenTile = game.GetTile(new HexCoord(0, 1));
        inBetweenTile.RoadStatus = RoadStatus.Road;

        civInfo.Cache.UpdateCitiesConnectedToCapital();
        Assert.True(city2.CityStats.IsConnectedToCapital(RoadStatus.Road));

        city2.CityStats.Update();
        Assert.Equal(60f, city2.CityStats.FinalStatList["Trade routes"].Science);
    }

    // endregion

    // region stat nullifying uniques

    [Fact]
    public void NullifiesStat()
    {
        game.MakeHexagonalMap(1);
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true, 1);

        var building = game.CreateBuilding("Nullifies [Faith] [in this city]", "[+10 Gold, +10 Faith] [in this city]");
        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.True(city.CityStats.FinalStatList.Values.Sum(it => it.Gold) >= 10f);
        Assert.Equal(0f, city.CityStats.FinalStatList.Values.Sum(it => it.Faith));
    }

    [Fact]
    public void NullifiesGrowth()
    {
        game.MakeHexagonalMap(1);
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true, 1);

        var building = game.CreateBuilding("Nullifies Growth [in this city]", "[+10 Food, +10 Gold] [in this city]");
        city.CityConstructions.AddBuilding(building);
        city.CityStats.Update();
        Assert.True(city.CityStats.FinalStatList.Values.Sum(it => it.Gold) >= 10f);
        Assert.Equal(0f, city.CityStats.FinalStatList.Values.Sum(it => it.Food));

        city.Population.AddPopulation(1);
        city.CityStats.Update();
        Assert.Equal(0f, city.CityStats.FinalStatList.Values.Sum(it => it.Food));
    }

    // endregion

    //region production percentage bonus providing uniques based on production

    [Fact]
    public void PercentProductionBuildings()
    {
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true, 0);

        var buildingToConstruct = game.CreateBuilding();
        var building = game.CreateBuilding($"[+300]% Production when constructing [{buildingToConstruct.Name}] buildings [in all cities]", "[+1 Production]");
        city.CityConstructions.AddBuilding(building);
        city.CityConstructions.AddToQueue(buildingToConstruct.Name);
        city.CityStats.Update();
        Assert.Equal(300f, city.CityStats.StatPercentBonusTree.TotalStats.Production);
    }

    [Fact]
    public void PercentProductionUnits()
    {
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true, 0);

        var unitToConstruct = game.CreateBaseUnit();
        var building = game.CreateBuilding($"[+300]% Production when constructing [{unitToConstruct.Name}] units [in all cities]", "[+1 Production]");
        city.CityConstructions.AddBuilding(building);
        city.CityConstructions.AddToQueue(unitToConstruct.Name);
        city.CityStats.Update();
        Assert.Equal(300f, city.CityStats.StatPercentBonusTree.TotalStats.Production);
    }

    [Fact]
    public void PercentProductionWonders()
    {
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true, 0);

        var buildingToConstruct = game.CreateBuilding();
        var building = game.CreateBuilding($"[+300]% Production when constructing [{buildingToConstruct.Name}] wonders [in all cities]", "[+1 Production]");
        city.CityConstructions.AddBuilding(building);
        city.CityConstructions.AddToQueue(buildingToConstruct.Name);
        city.CityStats.Update();
        Assert.Equal(0f, city.CityStats.StatPercentBonusTree.TotalStats.Production);

        buildingToConstruct.IsWonder = true;
        city.CityStats.Update();
        Assert.Equal(300f, city.CityStats.StatPercentBonusTree.TotalStats.Production);
    }

    [Fact]
    public void PercentProductionBuildingsInCapital()
    {
        game.MakeHexagonalMap(3);
        var civInfo = game.AddCiv(["[+300]% Production towards any buildings that already exist in the Capital"]);
        var tile = game.GetTile(new HexCoord(0, 2));
        var city = game.AddCity(civInfo, tile, true, 0);
        var city2 = game.AddCity(civInfo, game.GetTile(new HexCoord(0, -2)), initialPopulation: 0);

        var buildingToConstruct = game.CreateBuilding();
        city2.CityConstructions.AddToQueue(buildingToConstruct.Name);
        city2.CityStats.Update();
        Assert.Equal(0f, city2.CityStats.StatPercentBonusTree.TotalStats.Production);

        city.CityConstructions.AddBuilding(buildingToConstruct);
        city2.CityStats.Update();
        Assert.Equal(300f, city2.CityStats.StatPercentBonusTree.TotalStats.Production);
    }

    //endregion

    // region Other Global Uniques

    // region growth

    [Fact]
    public void GrowthPercentBonusTest()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        // City has 2 food from center -2 from pop, so total of 0
        var building = game.CreateBuilding("[+100]% growth [in all cities]", "[+2 Food]");
        city.CityConstructions.AddBuilding(building);

        city.CityStats.Update();
        Assert.True(city.CityStats.FinalStatList["[Buildings] ([Growth])"].Equals(new Stats(food: 2f)));
    }

    [Fact]
    public void CarryOverFoodTest()
    {
        var civInfo = game.AddCiv(["[50]% Food is carried over after population increases [in all cities]"]);
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);

        var foodNecessary = city.Population.GetFoodToNextPopulation();
        city.Population.NextTurn(foodNecessary);

        Assert.Equal((int)(foodNecessary * 0.5f), city.Population.FoodStored);
    }

    [Fact]
    public void FoodConsumptionBySpecialistsTest()
    {
        var civInfo = game.AddCiv(["[-50]% Food consumption by [Specialists] [in all cities]"]);
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, initialPopulation: 1);

        var building = game.CreateBuilding();
        var specialistName = game.CreateSpecialist();
        building.SpecialistSlots.Add(specialistName, 1);
        city.Population.SpecialistAllocations[specialistName] = 1;

        city.CityStats.Update();
        Assert.Equal(-1f, city.CityStats.FinalStatList["Population"].Food);
    }

    // endregion growth

    // region happiness

    [Fact]
    public void UnhappinessFromCitiesPercentageTest()
    {
        var civInfo = game.AddCiv(["[+100]% unhappiness from the number of cities"]);
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);

        city.CityStats.Update();
        Assert.True(System.Math.Abs(city.CityStats.HappinessList["Cities"] - -6f) < Epsilon); // Float rounding errors
    }

    [Fact]
    public void UnhappinessFromPopulationTypePercentageChangeTest()
    {
        var civInfo = game.AddCiv(["[-50]% Unhappiness from [Population] [in all cities]"]);
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true, initialPopulation: 4);

        city.CityStats.Update();
        Assert.True(System.Math.Abs(city.CityStats.HappinessList["Population"] - -2f) < Epsilon);

        var building = game.CreateBuilding("[-50]% Unhappiness from [Specialists] [in all cities]");
        var specialist = game.CreateSpecialist();
        building.SpecialistSlots[specialist] = 2;
        city.Population.SpecialistAllocations[specialist] = 2;
        city.CityConstructions.AddBuilding(building);

        city.CityStats.Update();
        Assert.True(System.Math.Abs(city.CityStats.HappinessList["Population"] - -1f) < Epsilon);
    }

    // endregion happiness

    // region Conditionals

    [Fact]
    public void ConditionalDifficulty()
    {
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true);

        (string, int)[] tests =
        [
            ("<on [Settler] difficulty>", 0),
            ("<on [Chieftain] difficulty>", 0),
            ("<on [Prince] difficulty>", 1), // TestGame is Prince
            ("<on [King] difficulty>", 0),
            ("<on [Emperor] difficulty>", 0),
            ("<on [Settler] difficulty or higher>", 1),
            ("<on [Chieftain] difficulty or higher>", 1),
            ("<on [Prince] difficulty or higher>", 1),
            ("<on [King] difficulty or higher>", 0),
            ("<on [Emperor] difficulty or higher>", 0),
            ("<on [Immortal] difficulty or higher>", 0),
            ("<on [Deity] difficulty or higher>", 0),
            ("<on [Settler] difficulty or lower>", 0),
            ("<on [Chieftain] difficulty or lower>", 0),
            ("<on [Prince] difficulty or lower>", 1),
            ("<on [King] difficulty or lower>", 1),
            ("<on [Emperor] difficulty or lower>", 1),
            ("<on [Immortal] difficulty or lower>", 1),
            ("<on [Deity] difficulty or lower>", 1),
            ("<on [Not Found] difficulty>", 0),
            ("<on [Not Found] difficulty or higher>", 0),
            ("<on [Not Found] difficulty or lower>", 0),
        ];

        Assert.Equal(0, civInfo.Gold);
        foreach (var (test, expected) in tests)
        {
            var building = game.CreateBuilding($"Gain [1] [Gold] {test}");
            city.CityConstructions.AddBuilding(building);
            Assert.True(civInfo.Gold == expected, $"Conditional `{test}` should be: {expected}");
            civInfo.AddGold(-civInfo.Gold); // Reset the gold
        }
    }

    [Fact]
    public void ConditionalFirstCivToAdopt()
    {
        var civInfo = game.AddCiv();
        var city = game.AddCity(civInfo, game.GetTile(HexCoord.Zero), true);
        // Policy
        civInfo.Policies.FreePolicies++;
        civInfo.Policies.Adopt(civInfo.Policies.GetPolicyByName("Tradition"));
        // Belief
        game.AddReligion(civInfo);
        var religion = civInfo.ReligionManager.Religion;
        if (religion != null)
        {
            religion.AddBelief("Ancestor Worship");
            religion.AddBelief("Not A Belief");
        }
        (string, int)[] tests =
        [
            ("<if no Civilization has adopted [Oligarchy]>", 1),
            ("<if no Civilization has adopted [Tradition]>", 0),
            ("<if no Civilization has adopted [God of craftsmen]>", 1),
            ("<if no Civilization has adopted [Not A Policy]>", 1),
            ("<if no Civilization has adopted [Ancestor Worship]>", 0),
            ("<if no Civilization has adopted [Not A Belief]>", 1),
        ];
        Assert.Equal(0, civInfo.Gold);
        foreach (var (test, expected) in tests)
        {
            var building = game.CreateBuilding($"Gain [1] [Gold] {test}");
            city.CityConstructions.AddBuilding(building);
            Assert.True(civInfo.Gold == expected, $"Conditional `{test}` should be: {expected}");
            civInfo.AddGold(-civInfo.Gold); // Reset the gold
        }
    }

    // endregion

    // region Great Persons

    [Fact]
    public void StatsSpendingGreatPeople()
    {
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true);
        var unit = game.AddUnit("Great Engineer", civInfo, tile);
        var building = game.CreateBuilding("Gain [250] [Gold] <upon expending a [Great Person] unit>");
        city.CityConstructions.AddBuilding(building);

        civInfo.AddGold(-civInfo.Gold); // reset gold just to be sure

        unit.Consume();
        Assert.Equal(250, civInfo.Gold);
    }

    // endregion

    // region Gain control over tiles

    [Fact]
    public void TakeOverTilesInCity()
    {
        game.MakeHexagonalMap(5);
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true);
        Assert.Equal(7, city.GetTiles().Count());
        var building = game.CreateBuilding("Gain control over [8] tiles [in this city]");
        city.CityConstructions.AddBuilding(building);
        Assert.Equal(15, city.GetTiles().Count());
    }

    // endregion

    // region Can carry air units

    [Fact]
    public void TestCarryExtraAirUnits()
    {
        game.MakeHexagonalMap(5);
        var civInfo = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var city = game.AddCity(civInfo, tile, true);
        Assert.Equal(6, city.GetMaxAirUnits());
        city.CityConstructions.AddBuilding(game.CreateBuilding("Can carry [6] extra [Air] units <in this city>"));
        Assert.Equal(12, city.GetMaxAirUnits());
        city.CityConstructions.AddBuilding(game.CreateBuilding("Can carry [-3] extra [Air] units <in this city>"));
        Assert.Equal(9, city.GetMaxAirUnits());
    }

    // endregion

    // region Reveal entire map

    private readonly Unique revealEntireMapUnique = new("Reveals the entire map");
    private const string DiscoveryGoldUnique =
        "[+100 Gold] for discovering a Natural Wonder (bonus enhanced to [+500 Gold] if first to discover it)";
    private const string KnownWonderGoldUnique = "[+1 Gold] for every known Natural Wonder";
    private const string UponDiscoveringWonderGoldUnique =
        "Gain [250] [Gold] <upon discovering a Natural Wonder>";

    private bool RevealEntireMap(Civilization civ) =>
        UniqueTriggerActivation.TriggerUnique(revealEntireMapUnique, civ);

    private TileClass PlaceNaturalWonder(HexCoord position, string wonderName)
    {
        var tile = game.GetTile(position);
        tile.NaturalWonder = wonderName;
        tile.SetTerrainTransients();
        return tile;
    }

    private Civilization SetupRevealMapCiv(params string[] uniques)
    {
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv(uniques, isPlayer: true);
        game.AddCity(civ, game.GetTile(HexCoord.Zero), true);
        return civ;
    }

    private static List<Notification> DiscoveryNotifications(Civilization civ) =>
        civ.Notifications.Where(it => it.Text.StartsWith("We have discovered [", StringComparison.Ordinal)).ToList();

    private static List<Notification> RewardNotifications(Civilization civ) =>
        civ.Notifications.Where(it => it.Text.StartsWith("We have received [", StringComparison.Ordinal)).ToList();

    private void AssertEntireMapExplored(Civilization civ) =>
        Assert.True(game.TileMap.Values.All(it => it.IsExplored(civ)), "Revealing the map should explore every tile");

    [Fact]
    public void RevealEntireMapDiscoversRemoteNaturalWonder()
    {
        var civ = SetupRevealMapCiv();
        var wonderTile = PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");

        Assert.False(wonderTile.IsExplored(civ));
        Assert.DoesNotContain("Mount Fuji", civ.NaturalWonders);
        Assert.DoesNotContain(wonderTile, civ.ViewableTiles);

        Assert.True(RevealEntireMap(civ));

        AssertEntireMapExplored(civ);
        Assert.Contains("Mount Fuji", civ.NaturalWonders);
        Assert.Equal(["Mount Fuji"], civ.NaturalWonders);
    }

    [Fact]
    public void RevealEntireMapDoesNotGrantVisionOrMeetCivilizations()
    {
        var civ = SetupRevealMapCiv();
        PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");

        var otherCiv = game.AddCiv(isPlayer: true);
        game.AddCity(otherCiv, game.GetTile(new HexCoord(0, -5)), true);
        PlaceNaturalWonder(new HexCoord(4, 0), "Old Faithful");

        var viewableBefore = civ.ViewableTiles.ToHashSet();
        var diplomacyBefore = civ.Diplomacy.Keys.ToHashSet();
        var otherViewableBefore = otherCiv.ViewableTiles.ToHashSet();

        Assert.True(RevealEntireMap(civ));

        Assert.True(viewableBefore.SetEquals(civ.ViewableTiles));
        Assert.True(diplomacyBefore.SetEquals(civ.Diplomacy.Keys));
        Assert.False(civ.Knows(otherCiv));
        Assert.False(otherCiv.Knows(civ));
        Assert.True(otherViewableBefore.SetEquals(otherCiv.ViewableTiles));
        Assert.DoesNotContain("Mount Fuji", otherCiv.NaturalWonders);
        Assert.DoesNotContain("Old Faithful", otherCiv.NaturalWonders);
        Assert.Contains("Mount Fuji", civ.NaturalWonders);
        Assert.Contains("Old Faithful", civ.NaturalWonders);
        Assert.False(game.TileMap.Values.All(it => it.IsExplored(otherCiv)));
    }

    [Fact]
    public void RevealEntireMapGrantsFirstDiscoveryRewardsAndKnownWonderStats()
    {
        var civ = SetupRevealMapCiv(DiscoveryGoldUnique, KnownWonderGoldUnique);
        PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");

        Assert.Equal(0, civ.Gold);
        Assert.True(RevealEntireMap(civ));

        Assert.Equal(500, civ.Gold);
        Assert.True(civ.Stats.GetStatMapForNextTurn()["Natural Wonders"].Equals(new Stats(gold: 1f, happiness: 1f)));
        Assert.Single(DiscoveryNotifications(civ));
        Assert.Single(RewardNotifications(civ));
    }

    [Fact]
    public void RevealEntireMapGrantsOrdinaryRewardWhenWonderAlreadyKnown()
    {
        var civ = SetupRevealMapCiv(DiscoveryGoldUnique, KnownWonderGoldUnique);
        PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");

        var otherCiv = game.AddCiv();
        otherCiv.NaturalWonders.Add("Mount Fuji");

        Assert.True(RevealEntireMap(civ));

        Assert.Equal(100, civ.Gold);
        Assert.True(civ.Stats.GetStatMapForNextTurn()["Natural Wonders"].Equals(new Stats(gold: 1f, happiness: 1f)));
        Assert.Equal(["Mount Fuji"], otherCiv.NaturalWonders);
    }

    [Fact]
    public void RevealEntireMapDiscoversEachWonderNameOnce()
    {
        var civ = SetupRevealMapCiv(DiscoveryGoldUnique);
        PlaceNaturalWonder(new HexCoord(0, 4), "Great Barrier Reef");
        PlaceNaturalWonder(new HexCoord(0, 5), "Great Barrier Reef");
        PlaceNaturalWonder(new HexCoord(4, 0), "Mount Fuji");

        Assert.True(RevealEntireMap(civ));

        Assert.True(new HashSet<string> { "Great Barrier Reef", "Mount Fuji" }.SetEquals(civ.NaturalWonders));
        Assert.Equal(2, civ.NaturalWonders.Count);
        Assert.Equal(1000, civ.Gold);
        Assert.Equal(2, DiscoveryNotifications(civ).Count);
        Assert.Equal(2, RewardNotifications(civ).Count);

        Assert.True(RevealEntireMap(civ));
        civ.Cache.UpdateViewableTiles();

        Assert.True(new HashSet<string> { "Great Barrier Reef", "Mount Fuji" }.SetEquals(civ.NaturalWonders));
        Assert.Equal(2, civ.NaturalWonders.Count);
        Assert.Equal(1000, civ.Gold);
        Assert.Equal(2, DiscoveryNotifications(civ).Count);
        Assert.Equal(2, RewardNotifications(civ).Count);
    }

    [Fact]
    public void RevealEntireMapFiresUponDiscoveringNaturalWonderOncePerNewWonder()
    {
        var civ = SetupRevealMapCiv(UponDiscoveringWonderGoldUnique);
        PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");
        PlaceNaturalWonder(new HexCoord(4, 0), "Old Faithful");
        civ.NaturalWonders.Add("Mount Fuji");

        Assert.True(RevealEntireMap(civ));

        Assert.True(new HashSet<string> { "Mount Fuji", "Old Faithful" }.SetEquals(civ.NaturalWonders));
        Assert.Equal(250, civ.Gold);
        Assert.Single(DiscoveryNotifications(civ));
        Assert.Equal(1, civ.Notifications.Count(it => it.Text.Contains("due to discovering a Natural Wonder", StringComparison.Ordinal)));

        Assert.True(RevealEntireMap(civ));
        Assert.Equal(250, civ.Gold);
        Assert.Single(DiscoveryNotifications(civ));
        Assert.Equal(1, civ.Notifications.Count(it => it.Text.Contains("due to discovering a Natural Wonder", StringComparison.Ordinal)));
    }

    [Fact]
    public void RevealEntireMapSucceedsWithoutNewDiscoveries()
    {
        var civ = SetupRevealMapCiv(DiscoveryGoldUnique, UponDiscoveringWonderGoldUnique);

        Assert.True(RevealEntireMap(civ));
        AssertEntireMapExplored(civ);
        Assert.Empty(civ.NaturalWonders);
        Assert.Equal(0, civ.Gold);
        Assert.Empty(DiscoveryNotifications(civ));
        Assert.Empty(RewardNotifications(civ));

        PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");
        civ.NaturalWonders.Add("Mount Fuji");
        var knownBefore = civ.NaturalWonders.ToList();

        Assert.True(RevealEntireMap(civ));
        AssertEntireMapExplored(civ);
        Assert.Equal(knownBefore, civ.NaturalWonders);
        Assert.Equal(0, civ.Gold);
        Assert.Empty(DiscoveryNotifications(civ));
        Assert.Empty(RewardNotifications(civ));
    }

    [Fact]
    public void NormalSightDoesNotDiscoverWondersOutsideViewableTiles()
    {
        var civ = SetupRevealMapCiv(DiscoveryGoldUnique, UponDiscoveringWonderGoldUnique);
        var wonderTile = PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");

        civ.Cache.UpdateViewableTiles();

        Assert.DoesNotContain(wonderTile, civ.ViewableTiles);
        Assert.DoesNotContain("Mount Fuji", civ.NaturalWonders);
        Assert.Equal(0, civ.Gold);
        Assert.False(wonderTile.IsExplored(civ));
        Assert.True(civ.ViewableTiles.All(it => it.IsExplored(civ)));
    }

    [Fact]
    public void SpectatorRegistersNaturalWondersWithoutRewards()
    {
        game.MakeHexagonalMap(5);
        var spectator = game.AddCiv(game.Ruleset.Nations[Constants.Spectator], isPlayer: true);
        PlaceNaturalWonder(new HexCoord(0, 4), "Mount Fuji");

        Assert.True(RevealEntireMap(spectator));

        AssertEntireMapExplored(spectator);
        Assert.Contains("Mount Fuji", spectator.NaturalWonders);
        Assert.Equal(0, spectator.Gold);
        Assert.DoesNotContain(spectator.Notifications, it => it.Text.Contains("discover", StringComparison.OrdinalIgnoreCase));

        spectator.NaturalWonders.Clear();
        spectator.Cache.UpdateViewableTiles();

        Assert.Contains("Mount Fuji", spectator.NaturalWonders);
        Assert.Equal(0, spectator.Gold);
        Assert.DoesNotContain(spectator.Notifications, it => it.Text.Contains("discover", StringComparison.OrdinalIgnoreCase));
    }

    // endregion

    // endregion
}
