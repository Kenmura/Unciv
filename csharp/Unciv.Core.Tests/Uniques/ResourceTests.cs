using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Uniques;

// T143: port of tests/src/com/unciv/uniques/ResourceTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class ResourceTests
{
    private readonly TestGame game;
    private readonly Civilization civInfo;
    private readonly CityObject city;

    public ResourceTests()
    {
        game = new TestGame();
        game.MakeHexagonalMap(2);
        civInfo = game.AddCiv();
        city = game.AddCity(civInfo, game.TileMap[0, 0]);
    }

    [Fact]
    public void TestConsumesResourceUnique()
    {
        var consumesCoal = game.CreateBuilding("Consumes [1] [Coal]");
        city.CityConstructions.AddBuilding(consumesCoal);
        Assert.Equal(-1, civInfo.GetCivResourcesByName()["Coal"]);
    }

    [Fact]
    public void TestResourceProductionModifierDoesNotAffectConsumption()
    {
        var consumesCoal = game.CreateBuilding("Consumes [1] [Coal]");
        var doubleCoal = game.CreateBuilding("Double quantity of [Coal] produced");
        var doubleStrategic = game.CreateBuilding("Quantity of strategic resources produced by the empire +[100]%");
        var doubleStrategicProduction = game.CreateBuilding("[+100]% [Strategic] resource production");

        city.CityConstructions.AddBuilding(consumesCoal);
        Assert.Equal(-1, civInfo.GetCivResourcesByName()["Coal"]);

        city.CityConstructions.AddBuilding(doubleCoal);
        Assert.Equal(-1, civInfo.GetCivResourcesByName()["Coal"]);

        city.CityConstructions.AddBuilding(doubleStrategic);
        Assert.Equal(-1, civInfo.GetCivResourcesByName()["Coal"]);

        city.CityConstructions.AddBuilding(doubleStrategicProduction);
        Assert.Equal(-1, civInfo.GetCivResourcesByName()["Coal"]);
    }

    [Fact]
    public void TestResourceProductionAndConsumptionModifierDoesNotAffectConsumption()
    {
        var consumesCoal = game.CreateBuilding("Consumes [1] [Coal]");
        var providesCoal = game.CreateBuilding("Provides [1] [Coal]");
        var doubleStrategicProduction = game.CreateBuilding("[+100]% [Strategic] resource production");

        city.CityConstructions.AddBuilding(providesCoal);
        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]);

        city.CityConstructions.AddBuilding(doubleStrategicProduction);
        Assert.Equal(2, civInfo.GetCivResourcesByName()["Coal"]);

        city.CityConstructions.AddBuilding(consumesCoal);
        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]); // Produce 2, consume 1
    }

    [Fact]
    public void TestBuildingGrantedByUniqueGrantsResource()
    {
        var resourceProvider = game.CreateBuilding("Provides [1] [Coal]");
        var resourceProviderProvider = game.CreateBuilding($"Gain a free [{resourceProvider.Name}] [in this city]");
        city.CityConstructions.AddBuilding(resourceProviderProvider);
        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]);
    }

    [Fact]
    public void TestTileProvidesResourceOnlyWithRequiredTech()
    {
        var tile = game.TileMap[1, 1];
        tile.SetTileResource("Coal");
        tile.ResourceAmount = 1;
        tile.SetImprovement("Mine");

        civInfo.Tech.AddTechnology(game.Ruleset.TileImprovements["Mine"].TechRequired!);
        Assert.Equal(0, civInfo.GetCivResourcesByName()["Coal"]);

        civInfo.Tech.AddTechnology(game.Ruleset.TileResources["Coal"].RevealedBy!);

        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]);
    }

    [Fact]
    public void TestTileDoesNotProvideResourceWithPillagedImprovement()
    {
        var tile = game.TileMap[1, 1];
        tile.SetTileResource("Coal");
        tile.ResourceAmount = 1;
        tile.SetImprovement("Mine");

        civInfo.Tech.AddTechnology(game.Ruleset.TileImprovements["Mine"].TechRequired!);
        civInfo.Tech.AddTechnology(game.Ruleset.TileResources["Coal"].RevealedBy!);
        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]);

        tile.SetPillaged();
        Assert.Equal(0, civInfo.GetCivResourcesByName()["Coal"]);
    }

    /// <summary>The revealing tech should not affect whether we can get the resource from improvements</summary>
    [Fact]
    public void TestImprovementProvidesResourceEvenWithoutTech()
    {
        var tile = game.TileMap[1, 1];
        var improvement = game.CreateTileImprovement("Provides [1] [Coal]", "Consumes [1] [Silver]");
        tile.SetImprovement(improvement, civInfo);
        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]);
        Assert.Equal(-1, civInfo.GetCivResourcesByName()["Silver"]);
    }

    /// <summary>The revealing tech should not affect whether we can get the resource from improvements</summary>
    [Fact]
    public void TestImprovementProvidesResourceWithUniqueBonuses()
    {
        var tile = game.TileMap[1, 1];
        var improvement = game.CreateTileImprovement("Provides [1] [Coal]");
        tile.SetImprovement(improvement, civInfo);
        Assert.Equal(1, civInfo.GetCivResourcesByName()["Coal"]);

        var doubleCoal = game.CreateBuilding("[+100]% [Coal] resource production");
        city.CityConstructions.AddBuilding(doubleCoal);
        Assert.Equal(2, civInfo.GetCivResourcesByName()["Coal"]);

        var doubleStrategic = game.CreateBuilding("[+100]% [Strategic] resource production");
        city.CityConstructions.AddBuilding(doubleStrategic);
        Assert.Equal(3, civInfo.GetCivResourcesByName()["Coal"]);

        var doubleStrategicProduction = game.CreateBuilding("[+100]% [Strategic] resource production");
        city.CityConstructions.AddBuilding(doubleStrategicProduction);
        Assert.Equal(4, civInfo.GetCivResourcesByName()["Coal"]);
    }

    [Fact]
    public void Stringtoint()
    {
        Assert.Equal(1f, float.Parse("1", System.Globalization.CultureInfo.InvariantCulture));
    }

    // Resource tests
    [Fact]
    public void ShouldGetResourcesFromTiles()
    {
        civInfo.Tech.AddTechnology("Iron Working");
        civInfo.Tech.AddTechnology("Mining");

        var tile = game.GetTile(1, 1);
        tile.SetTileResource("Iron");
        tile.ResourceAmount = 4;
        tile.SetImprovementBasic("Mine");

        var cityResources = city.GetResourcesGeneratedByCity();

        Assert.Single(cityResources);
        Assert.Equal("4 Iron from Tiles", cityResources[0].ToString());
    }

    [Fact]
    public void ShouldGetResourcesFromUniqueBuildings()
    {
        var building = game.CreateBuilding("Provides [4] [Iron]");
        city.CityConstructions.AddBuilding(building);

        var resources = civInfo.DetailedCivResources;

        Assert.Single(resources);
        Assert.Equal("4 Iron from Buildings", resources[0].ToString());
    }

    [Fact]
    public void ShouldHandleStatPercentFromObjectToResourceWithABuildingFilter()
    {
        city.CityConstructions.AddBuilding("Monument");
        var building = game.CreateBuilding("[300]% of [Culture] from every [Monument] in the city added to [Iron]");
        city.CityConstructions.AddBuilding(building);
        Assert.Equal(6, city.GetAvailableResourceAmount("Iron")); // 2 Culture * 3
    }

    [Fact]
    public void ShouldHandleStatPercentFromObjectToResourceWithAImprovementFilter()
    {
        var tile = game.TileMap[1, 1];
        tile.SetTileResource("Wheat");
        tile.ResourceAmount = 1;
        tile.SetImprovement("Farm");
        city.Population.AddPopulation(5); // Add population, since the tile needs to be worked
        var building = game.CreateBuilding("[300]% of [Food] from every [Farm] in the city added to [Iron]");
        city.CityConstructions.AddBuilding(building);
        Assert.Equal(3, city.GetAvailableResourceAmount("Iron"));
    }

    [Fact]
    public void ShouldReduceResourcesDueToBuildings()
    {
        city.CityConstructions.AddBuilding("Factory");

        var resources = civInfo.DetailedCivResources;

        Assert.Single(resources);
        Assert.Equal("-1 Coal from Buildings", resources[0].ToString());
    }

    [Fact]
    public void CivWideResourcesFromBuildingUniquesPropagateBetweenCities()
    {
        var building = game.CreateBuilding("Provides [4] [Coal]");
        city.CityConstructions.AddBuilding(building);

        var otherCity = civInfo.AddCity(new HexCoord(2, 2));

        Assert.Equal(4, otherCity.GetAvailableResourceAmount("Coal"));
    }

    [Fact]
    public void CityWideResourcesFromBuildingUniquesPropagateBetweenCities()
    {
        var resource = game.CreateResource(UniqueType.CityResource.Text());
        var building = game.CreateBuilding($"Provides [4] [{resource.Name}]");
        city.CityConstructions.AddBuilding(building);

        var otherCity = civInfo.AddCity(new HexCoord(2, 2));

        Assert.Equal(4, otherCity.GetAvailableResourceAmount(resource.Name));
    }

    [Fact]
    public void CityWideResourcesNotDoubleCountedInSameCity()
    {
        var resource = game.CreateResource(UniqueType.CityResource.Text());
        var building = game.CreateBuilding($"Provides [4] [{resource.Name}]");
        city.CityConstructions.AddBuilding(building);

        Assert.Equal(4, city.GetAvailableResourceAmount(resource.Name));
    }

    [Fact]
    public void CivWideResourcesCanComeFromFollowerBeliefsAndAffectAllCities()
    {
        var religion = game.AddReligion(civInfo);
        var belief = game.CreateBelief(BeliefType.Follower, "Provides [1] [Iron]");
        religion.AddBeliefs([belief]);
        city.Population.SetPopulation(1);
        city.Religion.AddPressure(religion.Name, 1000);
        var otherCity = civInfo.AddCity(new HexCoord(2, 2)); // NOT religionized

        Assert.Equal(1, city.GetAvailableResourceAmount("Iron"));
        Assert.Equal(1, otherCity.GetAvailableResourceAmount("Iron"));
    }

    [Fact]
    public void CityResourcesWorkWithConditional()
    {
        var resource = game.CreateResource(UniqueType.CityResource.Text());
        var resourceAndConditionalBuilding = game.CreateBuilding($"Provides [2] [{resource.Name}]",
            $"[+1 Faith] <when above [1] [{resource.Name}]>");

        city.CityConstructions.AddBuilding(resourceAndConditionalBuilding);
        var faith = city.CityStats.CurrentCityStats.Faith;

        Assert.Equal(1f, faith);
    }

    [Fact]
    public void CityResourcesFromImprovementWithConditional()
    {
        var resource = game.CreateResource(UniqueType.CityResource.Text());
        var resourceImprovement = game.CreateTileImprovement($"Provides [2] [{resource.Name}] <in [non-[Fresh water]] tiles>");
        game.GetTile(1, 1).AddTerrainFeature("Oasis");

        game.GetTile(1, 1).SetImprovement(resourceImprovement);

        Assert.Equal(0, city.GetAvailableResourceAmount(resource));
    }

    [Fact]
    public void StockpiledResourcesConsumedWhenConstructionStarts()
    {
        var resource = game.CreateResource(UniqueType.Stockpiled.Text());
        var building = game.CreateBuilding($"Instantly provides [2] [{resource.Name}]");
        city.CityConstructions.AddBuilding(building);
        Assert.Equal(2, civInfo.GetCivResourcesByName()[resource.Name]);

        var consumingBuilding = game.CreateBuilding($"Costs [1] [{resource.Name}]");
        Assert.Equal(2, civInfo.GetCivResourcesByName()[resource.Name]); // no change yet
        city.CityConstructions.SetCurrentConstruction(consumingBuilding.Name);
        civInfo.PlayerType = PlayerType.Human; // to not loop endlessly on "next turn"
        game.GameInfo.CurrentPlayer = civInfo.CivId;
        game.GameInfo.CurrentPlayerCiv = civInfo;
        game.GameInfo.NextTurn(0);
        Assert.Equal(1, civInfo.GetCivResourcesByName()[resource.Name]); // 1 was consumed because production started
    }

    [Fact]
    public void ConstructionsRequiringStockpiledResourcesUnconstructableWithoutThem()
    {
        var resource = game.CreateResource(UniqueType.Stockpiled.Text());
        var consumingBuilding = game.CreateBuilding($"Costs [1] [{resource.Name}]");
        Assert.False(consumingBuilding.IsBuildable(city.CityConstructions));

        var building = game.CreateBuilding($"Instantly provides [2] [{resource.Name}]");
        city.CityConstructions.AddBuilding(building);
        Assert.True(consumingBuilding.IsBuildable(city.CityConstructions));
    }

    [Fact]
    public void SetStockpileToCountable()
    {
        var resource = game.CreateResource(UniqueType.Stockpiled.Text());
        var building = game.CreateBuilding($"Instantly provides [2] [{resource.Name}]");
        city.CityConstructions.AddBuilding(building);
        Assert.Equal(2, civInfo.GetCivResourcesByName()[resource.Name]);

        UniqueTriggerActivation.TriggerUnique(
            new Unique($"Set [{resource.Name}] to [1+1]"),
            civInfo);

        Assert.Equal(2, civInfo.GetCivResourcesByName()[resource.Name]);
    }
}
