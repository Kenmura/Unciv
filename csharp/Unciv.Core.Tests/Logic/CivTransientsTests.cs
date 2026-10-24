using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic;

// T058: CivInfoTransientCache, CivInfoStatsForNextTurn, CapitalConnectionsFinder and CivConstructions over the seam fakes.
// The TestGame-based fixture (CapitalConnectionsFinderTests) needs real cities and maps and is deferred to T072.
public class CivTransientsTests
{
    private static (Civilization Civ, FakeGameInfoState Info) NewCiv(string name = "Rome")
    {
        var info = new FakeGameInfoState();
        var civ = new Civilization(new NationObject { Name = name }) { GameInfo = info, PlayerType = PlayerType.Human };
        info.Civs.Add(civ);
        civ.Policies.CivInfo = civ;
        civ.Tech.CivInfo = civ;
        civ.Tech.Era = new Unciv.Models.Ruleset.Tech.Era { Name = "Ancient era", EraNumber = 0 };
        civ.CityStateFunctions = new FakeCityStateFunctions();
        civ.Units = new TestUnitManager(civ);
        civ.CivConstructions.SetTransients(civ);
        return (civ, info);
    }

    [Fact]
    public void CapitalConnectionMedium_KeepsTheKotlinEnumProperties()
    {
        Assert.Equal(RoadStatus.None, CapitalConnectionMedium.Unknown.RoadType());
        Assert.Equal(RoadStatus.Road, CapitalConnectionMedium.Road.RoadType());
        Assert.Equal(RoadStatus.Railroad, CapitalConnectionMedium.Railroad.RoadType());
        Assert.Equal(RoadStatus.None, CapitalConnectionMedium.Harbor.RoadType());
        Assert.Equal(RoadStatus.Road, CapitalConnectionMedium.HarborFromRoad.RoadType());
        Assert.Equal(RoadStatus.Railroad, CapitalConnectionMedium.HarborFromRailroad.RoadType());
        Assert.Equal(
            [CapitalConnectionMedium.Start, CapitalConnectionMedium.Railroad, CapitalConnectionMedium.HarborFromRailroad],
            Enum.GetValues<CapitalConnectionMedium>().Where(it => it.CheckRailroad()));
    }

    [Fact]
    public void UpdateProximity_SelfPrecalculatedAndNoCities()
    {
        var (rome, info) = NewCiv("Rome");
        var egypt = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = info };
        Assert.Equal(Proximity.None, rome.Cache.UpdateProximity(rome));
        Assert.False(rome.Proximity.ContainsKey("Rome"));

        Assert.Equal(Proximity.Far, rome.UpdateProximity(egypt, Proximity.Far));
        Assert.Equal(Proximity.Far, rome.GetProximity("Egypt"));

        Assert.Equal(Proximity.None, rome.UpdateProximity(egypt)); // neither has cities
        Assert.Equal(Proximity.None, rome.GetProximity("Egypt"));
    }

    [Fact]
    public void UnitSupply_FollowsDifficultyCitiesAndPopulation()
    {
        var (civ, info) = NewCiv();
        info.Difficulty.UnitSupplyBase = 5;
        info.Difficulty.UnitSupplyPerCity = 2;
        info.RulesetData.ModOptions.Constants.UnitSupplyPerPopulation = 0.5;
        civ.Cities =
        [
            new FakeCityState("a", new FakeCiv("Rome")) { PopulationData = 3 },
            new FakeCityState("b", new FakeCiv("Rome")) { PopulationData = 6 },
        ];
        civ.Units = new TestUnitManager(civ) { Count = 20 };

        Assert.Equal(5, civ.Stats.GetBaseUnitSupply());
        Assert.Equal(4, civ.Stats.GetUnitSupplyFromCities());
        Assert.Equal(4, civ.Stats.GetUnitSupplyFromPop()); // 9 * 0.5 = 4.5, truncated
        Assert.Equal(13, civ.Stats.GetUnitSupply());
        Assert.Equal(7, civ.Stats.GetUnitSupplyDeficit());
        Assert.Equal(-70f, civ.Stats.GetUnitSupplyProductionPenalty()); // capped at -70%

        civ.Units = new TestUnitManager(civ) { Count = 15 };
        Assert.Equal(-20f, civ.Stats.GetUnitSupplyProductionPenalty());
        civ.Units = new TestUnitManager(civ) { Count = 2 };
        Assert.Equal(0, civ.Stats.GetUnitSupplyDeficit());
        Assert.Equal(0f, civ.Stats.GetUnitSupplyProductionPenalty() + 0f); // -min(0, 70) is -0f
    }

    [Fact]
    public void HappinessBreakdown_StartsWithTheBaseHappiness()
    {
        var (civ, info) = NewCiv();
        info.Difficulty.BaseHappiness = 9;

        var breakdown = civ.Stats.GetHappinessBreakdown();

        Assert.Equal(9f, breakdown["Base happiness"]);
        Assert.Equal(0f, breakdown["Luxury resources"]);
        Assert.Equal(0f, breakdown["City-State Luxuries"]);
        Assert.Equal(0f, breakdown["Traded Luxuries"]);
        Assert.Equal(0f, breakdown["Natural Wonders"] - 0f);
    }

    [Fact]
    public void FreeBuildings_AreGrantedOnceAndRemembered()
    {
        var (civ, info) = NewCiv();
        var library = new Building { Name = "Library" };
        info.RulesetData.Buildings.Put("Library", library);
        var city = new FakeCityState("c1", new FakeCiv("Rome")) { CivData = civ };
        city.LocalUniques.AddUnique(new UniqueObject("Gain a free [Library] [in this city]", UniqueTarget.Building, "Test"));
        civ.Cities = [city];

        civ.CivConstructions.AddFreeBuildings();

        Assert.Same(library, Assert.Single(city.Completed));
        Assert.Equal("Library", Assert.Single(city.FreeBuildingsProvidedFromThisCity["c1"]));
        Assert.True(civ.CivConstructions.HasFreeBuilding(city, library));
        Assert.Equal(["Library"], civ.CivConstructions.GetFreeBuildingNames(city));

        civ.CivConstructions.AddFreeBuildings(); // the building is there now
        Assert.Single(city.Completed);
    }

    [Fact]
    public void AddFreeBuildings_ByAmount_RespectsTheCityLimitAndRemembers()
    {
        var (civ, info) = NewCiv();
        var granary = new Building { Name = "Granary" };
        info.RulesetData.Buildings.Put("Granary", granary);
        var first = new FakeCityState("c1", new FakeCiv("Rome"));
        var second = new FakeCityState("c2", new FakeCiv("Rome"));
        civ.Cities = [first, second];

        civ.CivConstructions.AddFreeBuildings(granary, 1);
        Assert.Single(first.Completed);
        Assert.Empty(second.Completed);
        Assert.True(civ.CivConstructions.HasFreeBuilding(first, granary));
        Assert.False(civ.CivConstructions.HasFreeBuilding(second, granary));

        civ.CivConstructions.AddFreeBuildings(granary, 2); // the first city is skipped, it has one
        Assert.Single(first.Completed);
        Assert.Single(second.Completed);
    }

    [Fact]
    public void TakeCities_ThrowsForNegativeAmounts()
    {
        var (civ, info) = NewCiv();
        var granary = new Building { Name = "Granary" };
        info.RulesetData.Buildings.Put("Granary", granary);
        civ.Cities = [new FakeCityState("c1", new FakeCiv("Rome"))];

        Assert.Throws<ArgumentException>(() => civ.CivConstructions.AddFreeBuildings(granary, -1));
        Assert.Throws<ArgumentException>(() => civ.CivConstructions.AddFreeStatBuildings(Unciv.Models.StatTypes.Stat.Food, -1));
    }

    [Fact]
    public void AddFreeBuildings_ByAmount_SnapshotsTheCityListLikeKotlinTake()
    {
        var (civ, info) = NewCiv();
        var granary = new Building { Name = "Granary" };
        info.RulesetData.Buildings.Put("Granary", granary);
        var first = new FakeCityState("c1", new FakeCiv("Rome"));
        var cities = new List<CityObject> { first };
        civ.Cities = cities;
        first.OnCompleteConstruction = _ => cities.Add(new FakeCityState("c2", new FakeCiv("Rome")));

        civ.CivConstructions.AddFreeBuildings(granary, 5); // would throw "collection was modified" with a lazy Take

        Assert.Single(first.Completed);
        Assert.Equal(2, cities.Count);
    }

    [Fact]
    public void AddFreeBuildings_CityLocalUniquesDoNotLeakIntoOtherCities()
    {
        var (civ, info) = NewCiv();
        var library = new Building { Name = "Library" };
        info.RulesetData.Buildings.Put("Library", library);
        var first = new FakeCityState("c1", new FakeCiv("Rome")) { CivData = civ };
        var second = new FakeCityState("c2", new FakeCiv("Rome")) { CivData = civ };
        first.LocalUniques.AddUnique(new UniqueObject("Gain a free [Library] [in this city]", UniqueTarget.Building, "Test"));
        civ.Cities = [first, second];

        civ.CivConstructions.AddFreeBuildings();

        Assert.Same(library, Assert.Single(first.Completed));
        Assert.Empty(second.Completed); // a city-local unique of the first city never leaks into the second
    }

    [Fact]
    public void CountConstructedObjects_CountsBuiltAndSpaceshipParts()
    {
        var (civ, _) = NewCiv();
        civ.VictoryManager.CurrentsSpaceshipParts["Library"] = 3;
        var building = new Building { Name = "Library" };
        var a = new FakeCityState("a", new FakeCiv("Rome"));
        var b = new FakeCityState("b", new FakeCiv("Rome"));
        a.Buildings.Add("Library");
        civ.Cities = [a, b];

        Assert.Equal(3 + 1, civ.CivConstructions.CountConstructedObjects(building));
        Assert.Equal(3 + 1, civ.CivConstructions.CountConstructedObjects(building, a)); // a has it built: the queue-ignore does not matter
    }

    [Fact]
    public void CivConstructions_CloneCopiesCountersIndependently()
    {
        var (civ, _) = NewCiv();
        civ.CivConstructions.BuiltItemsWithIncreasingCost["Settler"] = 2;
        civ.CivConstructions.BoughtItemsWithIncreasingPrice["Library"] = 1;

        var clone = civ.CivConstructions.Clone();
        clone.BuiltItemsWithIncreasingCost["Settler"] = 5;

        Assert.Equal(2, civ.CivConstructions.GetBuiltItemsWithIncreasingCost("Settler"));
        Assert.Equal(5, clone.GetBuiltItemsWithIncreasingCost("Settler"));
        Assert.Equal(1, clone.GetBoughtItemsWithIncreasingPrice("Library"));
        Assert.Equal(0, clone.GetBoughtItemsWithIncreasingPrice("Other"));
        Assert.Same(civ, clone.CivInfo); // Kotlin copies the lateinit civInfo
    }

    [Fact]
    public void CivConstructions_CivInfoIsLateinit()
    {
        Assert.Throws<InvalidOperationException>(() => new CivConstructions().CivInfo);
    }
}
