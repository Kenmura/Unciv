using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.City;
using Unciv.Logic.Automation.Unit;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Nation;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Logic.Automation;

// T158. ConstructionAutomation.kt has no Kotlin test fixture: these are C#-only cases, with expected values derived by hand from the Kotlin source.
[Collection("RulesetCache")]
public class ConstructionAutomationTests
{
    private const string EnableScience = "Enables conversion of city production to [Science]";
    private const string EnableGold = "Enables conversion of city production to [Gold]";
    private const string EnableCulture = "Enables conversion of city production to [Culture]";

    private readonly TestGame testGame;

    public ConstructionAutomationTests() : this([]) { }

    private ConstructionAutomationTests(string[] globalUniques) => testGame = new TestGame(globalUniques);

    private static ConstructionAutomationTests With(params string[] globalUniques) => new(globalUniques);

    private (CivilizationObject Civ, CityObject City) CivWithCity(bool human = true, CivilizationObject? existing = null)
    {
        testGame.MakeHexagonalMap(6, Constants.Grassland);
        var civ = existing ?? testGame.AddCiv(isPlayer: human);
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        city.CityConstructions.SetCurrentConstruction("Nothing");
        return (civ, city);
    }

    /// <summary>For human civs ConstructionAutomation drops everything in DisabledConstructions: leave only <paramref name="allowed"/>.</summary>
    private void OnlyAllow(CityObject city, params string[] allowed)
    {
        foreach (var name in testGame.Ruleset.Buildings.Keys.Concat(testGame.Ruleset.Units.Keys))
            if (!allowed.Contains(name)) city.DisabledConstructions.Add(name);
    }

    private static List<string> Started(CivilizationObject civ) =>
        civ.Notifications.Select(it => it.Text).Where(it => it.Contains("has started working on", StringComparison.Ordinal)).ToList();

    private static string Current(CityObject city) => city.CityConstructions.CurrentConstructionName();

    [Fact]
    public void KeepsANonPerpetualConstruction()
    {
        var (_, city) = CivWithCity();
        city.CityConstructions.SetCurrentConstruction("Warrior");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Warrior", Current(city));
    }

    [Fact]
    public void FallsBackToIdleWhenNothingIsBuildable()
    {
        var (civ, city) = CivWithCity();
        OnlyAllow(city);
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city));
        Assert.Empty(Started(civ));
    }

    [Fact]
    public void FallbackPrefersScienceThenGoldThenCulture()
    {
        var game = With(EnableScience, EnableGold, EnableCulture);
        var (civ, city) = game.CivWithCity();
        game.OnlyAllow(city);
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Science", Current(city));
        Assert.Equal(["[" + city.Name + "] has started working on [Science]"], Started(civ));

        // the same decision again does not notify a second time
        city.CityConstructions.SetCurrentConstruction("Nothing");
        city.CityConstructions.SetCurrentConstruction("Science");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Single(Started(civ));
    }

    [Fact]
    public void FallbackSkipsScienceWhenAllTechsAreResearchedAndTakesGold()
    {
        var game = With(EnableScience, EnableGold);
        var (civ, city) = game.CivWithCity();
        game.OnlyAllow(city);
        foreach (var tech in game.testGame.Ruleset.Technologies.Keys) civ.Tech.AddTechnology(tech);
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Gold", Current(city));
    }

    [Fact]
    public void FallbackTakesCultureWhenOnlyCultureIsEnabled()
    {
        var game = With(EnableCulture);
        var (_, city) = game.CivWithCity();
        game.OnlyAllow(city);
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Culture", Current(city));
    }

    [Fact]
    public void ChoosesAWorkerWhenTheCivHasNone()
    {
        var (civ, city) = CivWithCity();
        OnlyAllow(city, "Worker", "Warrior");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        // 1 wanted worker / (0 + 0.17) beats the halved military modifier
        Assert.Equal("Worker", Current(city));
        Assert.Single(Started(civ));
    }

    [Fact]
    public void ChoosesAMilitaryUnitWhenWorkersAreCovered()
    {
        var (civ, city) = CivWithCity();
        testGame.AddUnit("Worker", civ, testGame.GetTile(1, 0));
        OnlyAllow(city, "Worker", "Warrior");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Warrior", Current(city));
    }

    [Fact]
    public void PuppetsDoNotBuildUnits()
    {
        var (_, city) = CivWithCity();
        city.IsPuppet = true;
        OnlyAllow(city, "Worker", "Warrior");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city));
    }

    [Fact]
    public void AMilitaryUnitIsSkippedWithoutWarWhenGoldIsNegativeOrCivIsBroke()
    {
        var (civ, city) = CivWithCity();
        testGame.AddUnit("Worker", civ, testGame.GetTile(1, 0));
        OnlyAllow(city, "Warrior");
        civ.AddGold(-50 - civ.Gold - 1);
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city));

        civ.AddGold(100);
        civ.Stats.StatsForNextTurn.Gold = -1f;
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city));
    }

    [Fact]
    public void ChoosesABuildingByStats()
    {
        var (_, city) = CivWithCity();
        testGame.AddUnit("Worker", city.Civ, testGame.GetTile(1, 0));
        city.CityConstructions.AddBuilding("Palace");
        OnlyAllow(city, "Monument");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Monument", Current(city));
    }

    [Fact]
    public void WillNotBuildPersonalityUniqueExcludesConstructions()
    {
        testGame.Ruleset.Personalities.Put("Avoider", new Personality { Name = "Avoider", Uniques = ["Will not build [Monument]"] });
        var nation = new Nation { Name = "AvoiderNation", Cities = ["The Capital"], Personality = "Avoider" };
        var ai = testGame.AddCiv(nation);
        var (_, city) = CivWithCity(existing: ai);
        city.CityConstructions.AddBuilding("Palace");
        testGame.AddUnit("Worker", ai, testGame.GetTile(1, 0));
        city.CityConstructions.SetCurrentConstruction("Nothing");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.NotEqual("Monument", Current(city));
    }

    private void PrepareFishingSpot(CivilizationObject civ, CityObject city, string resource = "Fish")
    {
        civ.Tech.AddTechnology("Sailing");
        var water = testGame.SetTileTerrain(HexCoord.Of(1, 0), "Coast");
        water.SetTileResource(resource);
        water.SetOwningCity(city);
        water.SetExplored(civ, true);
        city.TilesInRange.Add(water);
    }

    [Fact]
    public void ChoosesWorkBoatsForAnOwnedSeaResource()
    {
        var (civ, city) = CivWithCity();
        PrepareFishingSpot(civ, city);
        testGame.AddUnit("Worker", civ, testGame.GetTile(0, 1));
        OnlyAllow(city, "Work Boats");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Work Boats", Current(city));
    }

    [Fact]
    public void NoWorkBoatsWhenOneIsAlreadyNearby()
    {
        var (civ, city) = CivWithCity();
        PrepareFishingSpot(civ, city);
        testGame.AddUnit("Worker", civ, testGame.GetTile(0, 1));
        testGame.AddUnit("Work Boats", civ, testGame.GetTile(1, 0));
        OnlyAllow(city, "Work Boats");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city));
    }

    [Fact]
    public void NoWorkBoatsWithoutAnImprovableSeaResource()
    {
        var (civ, city) = CivWithCity();
        PrepareFishingSpot(civ, city);
        testGame.GetTile(1, 0).SetTileResource((string?)null);
        testGame.AddUnit("Worker", civ, testGame.GetTile(0, 1));
        OnlyAllow(city, "Work Boats");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city));
    }

    [Fact]
    public void SeamHasWorkableSeaResourceRequiresWaterVisibleUnimprovedResource()
    {
        var (civ, _) = CivWithCity();
        var water = testGame.SetTileTerrain(HexCoord.Of(1, 0), "Coast");
        Assert.False(WorkerAutomationSeams.HasWorkableSeaResource(water, civ)); // no resource
        water.SetTileResource("Fish");
        Assert.False(WorkerAutomationSeams.HasWorkableSeaResource(water, civ)); // the boats need Sailing
        civ.Tech.AddTechnology("Sailing");
        Assert.True(WorkerAutomationSeams.HasWorkableSeaResource(water, civ));
        Assert.True(WorkerAutomationSeams.IsNotBonusResourceOrWorkable(water, civ)); // Fish within a city's range
        var farWater = testGame.SetTileTerrain(HexCoord.Of(5, 0), "Coast");
        farWater.SetTileResource("Fish");
        Assert.False(WorkerAutomationSeams.IsNotBonusResourceOrWorkable(farWater, civ)); // a bonus outside every city's range
        water.SetImprovement("Fishing Boats");
        Assert.False(WorkerAutomationSeams.HasWorkableSeaResource(water, civ)); // already improved
        water.RemoveImprovement();
        var oilTile = testGame.SetTileTerrain(HexCoord.Of(2, 0), "Coast");
        oilTile.SetTileResource("Oil");
        Assert.True(WorkerAutomationSeams.IsNotBonusResourceOrWorkable(oilTile, civ)); // strategic: improve even out of range
        var land = testGame.GetTile(0, 1);
        land.SetTileResource("Iron");
        Assert.False(WorkerAutomationSeams.HasWorkableSeaResource(land, civ)); // land
    }

    [Fact]
    public void AtWarCivsBuildMilitaryUnitsInLowProductionCities()
    {
        var (civ, city) = CivWithCity();
        testGame.AddUnit("Worker", civ, testGame.GetTile(0, 1));
        var enemy = testGame.AddCiv();
        civ.GetDiplomacyManager(enemy)?.DeclareWar();
        OnlyAllow(city, "Warrior");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Warrior", Current(city));
    }

    [Fact]
    public void AICivSkipsNotificationsButStillChooses()
    {
        var ai = testGame.AddCiv();
        var (_, city) = CivWithCity(existing: ai);
        testGame.AddUnit("Worker", ai, testGame.GetTile(0, 1));
        city.CityConstructions.AddBuilding("Palace");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.NotEqual("Nothing", Current(city));
        Assert.Empty(Started(ai));
    }

    [Fact]
    public void WondersAreSkippedInSmallEmpires()
    {
        var (_, city) = CivWithCity();
        testGame.AddUnit("Worker", city.Civ, testGame.GetTile(0, 1));
        city.Civ.Tech.AddTechnology("Masonry");
        OnlyAllow(city, "Great Wall", "Pyramids");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Equal("Nothing", Current(city)); // population below 12
    }

    [Fact]
    public void WondersAreConsideredInPopulousEmpires()
    {
        var (civ, city) = CivWithCity();
        city.Population.AddPopulation(14);
        testGame.AddUnit("Worker", civ, testGame.GetTile(0, 1));
        civ.Tech.AddTechnology("Masonry");
        civ.Tech.AddTechnology("Bronze Working");
        OnlyAllow(city, "Pyramids", "Great Wall");
        new ConstructionAutomation(city.CityConstructions).ChooseNextConstruction();
        Assert.Contains(Current(city), new[] { "Pyramids", "Great Wall", "Nothing" });
    }
}
