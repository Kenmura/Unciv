using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using CityObject = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>B004: Brave New World content uniques that need no new subsystem (bnw-spec 3.2, 4.13): improvement rules, founding claim,
/// free policies per era, the Reformation belief tier and the generated content that uses them.</summary>
[Collection("RulesetCache")]
public sealed class BnwContentTests
{
    private readonly TestGame game = new();

    private (Civilization civ, CityObject city) Setup(params string[] nationUniques)
    {
        game.MakeHexagonalMap(6, Constants.Grassland);
        var civ = game.AddCiv(nationUniques);
        foreach (var tech in game.Ruleset.Technologies.Values) civ.Tech.AddTechnology(tech.Name);
        var city = game.AddCity(civ, game.TileMap[0, 0]);
        return (civ, city);
    }

    private static bool CanBuild(Tile tile, TileImprovement improvement, Civilization civ) =>
        tile.ImprovementFunctions.CanBuildImprovement(improvement, civ.State);

    [Fact]
    public void MustBeAdjacentToLuxuryNeedsALuxuryNeighbour()
    {
        var (civ, city) = Setup();
        var chateau = game.CreateTileImprovement("Must be built adjacent to a luxury resource");
        chateau.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var tile = game.TileMap[1, 1];
        game.AddTileToCity(city, tile);
        tile.SetOwningCity(city);
        Assert.False(CanBuild(tile, chateau, civ));
        var luxury = game.Ruleset.TileResources.Values.First(it => it.ResourceType == ResourceType.Luxury);
        var neighbour = tile.Neighbors.First(it => !it.IsCityCenter());
        neighbour.TileResource = luxury;
        Assert.True(CanBuild(tile, chateau, civ));
        // a strategic neighbour is not a luxury
        neighbour.TileResource = game.Ruleset.TileResources.Values.First(it => it.ResourceType == ResourceType.Strategic);
        Assert.False(CanBuild(tile, chateau, civ));
    }

    [Fact]
    public void NoTwoAdjacentRejectsANeighbourOfTheFilteredKind()
    {
        var (civ, city) = Setup();
        var improvement = game.CreateTileImprovement("Cannot be built adjacent to another [Farm]");
        improvement.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var tile = game.TileMap[1, 1];
        tile.SetOwningCity(city);
        Assert.True(CanBuild(tile, improvement, civ));
        var neighbour = tile.Neighbors.First(it => !it.IsCityCenter());
        neighbour.SetImprovement("Farm");
        Assert.False(CanBuild(tile, improvement, civ));
        neighbour.SetImprovement("Mine");
        Assert.True(CanBuild(tile, improvement, civ));
    }

    [Fact]
    public void RequiresAdjacentLandCountsLandNeighbours()
    {
        var (civ, city) = Setup();
        var improvement = game.CreateTileImprovement("Requires [6] adjacent land tiles");
        improvement.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var tile = game.TileMap[1, 1];
        tile.SetOwningCity(city);
        Assert.True(CanBuild(tile, improvement, civ));
        var water = tile.Neighbors.First(it => !it.IsCityCenter());
        water.BaseTerrain = Constants.Ocean;
        water.SetTransients();
        Assert.False(CanBuild(tile, improvement, civ));
        var relaxed = game.CreateTileImprovement("Requires [5] adjacent land tiles");
        relaxed.TerrainsCanBeBuiltOn = [Constants.Grassland];
        Assert.True(CanBuild(tile, relaxed, civ));
    }

    [Fact]
    public void OnlyInCityStateTerritoryNeedsACityStateOwner()
    {
        var (civ, city) = Setup();
        var feitoria = game.CreateTileImprovement("Can only be built in City-State territory");
        feitoria.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var cityState = game.AddCiv(cityStateType: "Cultured");
        var csCity = game.AddCity(cityState, game.TileMap[5, 5]);
        var own = game.TileMap[1, 1];
        own.SetOwningCity(city);
        var foreign = game.TileMap[4, 5];
        foreign.SetOwningCity(csCity);
        var unowned = game.TileMap[-3, 2];
        Assert.False(CanBuild(own, feitoria, civ));
        Assert.False(CanBuild(unowned, feitoria, civ));
        Assert.True(CanBuild(foreign, feitoria, civ));
        Assert.Contains(ImprovementBuildingProblem.OutsideBorders, own.ImprovementFunctions.GetImprovementBuildingProblems(feitoria, civ.State));
        Assert.DoesNotContain(ImprovementBuildingProblem.OutsideBorders, foreign.ImprovementFunctions.GetImprovementBuildingProblems(feitoria, civ.State));
    }

    [Fact]
    public void IgnoresTileOwnershipLiftsTheBorderRule()
    {
        var (civ, _) = Setup();
        var plain = game.CreateTileImprovement();
        plain.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var landmark = game.CreateTileImprovement("Can be built in any civ's territory");
        landmark.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var other = game.AddCiv();
        var otherCity = game.AddCity(other, game.TileMap[5, 5]);
        var foreign = game.TileMap[4, 5];
        foreign.SetOwningCity(otherCity);
        Assert.False(CanBuild(foreign, plain, civ));
        Assert.True(CanBuild(foreign, landmark, civ));
    }

    [Fact]
    public void RemovesResourceWhenBuiltClearsTheResource()
    {
        var (civ, city) = Setup();
        var dig = game.CreateTileImprovement("Removes the resource from the tile when built");
        dig.TerrainsCanBeBuiltOn = [Constants.Grassland];
        var tile = game.TileMap[1, 1];
        tile.SetOwningCity(city);
        tile.TileResource = game.Ruleset.TileResources.Values.First(it => it.ResourceType == ResourceType.Strategic);
        tile.ResourceAmount = 3;
        tile.ImprovementFunctions.SetImprovement(dig.Name, civ);
        Assert.Null(tile.Resource);
        Assert.Equal(0, tile.ResourceAmount);
        Assert.Equal(dig.Name, tile.Improvement);
    }

    [Fact]
    public void FoundedCitiesClaimTheNearestExtraTiles()
    {
        var (_, plain) = Setup();
        Assert.Equal(7, plain.GetTiles().Count());
        var game2 = new TestGame();
        game2.MakeHexagonalMap(6, Constants.Grassland);
        var shoshone = game2.AddCiv(["Founded cities claim [8] extra tiles of radius"]);
        var city = game2.AddCity(shoshone, game2.TileMap[0, 0]);
        var tiles = city.GetTiles().ToList();
        Assert.Equal(7 + 8, tiles.Count);
        // the extra tiles are the nearest ones: ring 2 holds 12, so none comes from ring 3
        Assert.All(tiles, it => Assert.True(it.AerialDistanceTo(city.GetCenterTile()) <= 2));
    }

    [Fact]
    public void FoundedCitiesSkipTilesOfOtherCities()
    {
        game.MakeHexagonalMap(6, Constants.Grassland);
        var other = game.AddCiv();
        game.AddCity(other, game.TileMap[0, 3]);
        var civ = game.AddCiv(["Founded cities claim [8] extra tiles of radius"]);
        var city = game.AddCity(civ, game.TileMap[0, 0]);
        Assert.All(city.GetTiles(), it => Assert.Same(city, it.GetCity()));
        Assert.Equal(15, city.GetTiles().Count());
        Assert.All(city.GetTiles(), it => Assert.True(it.AerialDistanceTo(city.GetCenterTile()) <= 3));
    }

    [Fact]
    public void FreePoliciesPerEraAreGrantedForEveryEraEntered()
    {
        game.MakeHexagonalMap(2);
        var civ = game.AddCiv(["[1] free Policies each time you enter a new era"]);
        var plain = game.AddCiv();
        var byEra = game.Ruleset.Technologies.Values.OrderBy(it => it.Column!.ColumnNumber).ToList();
        int totalEntered = 0, initialFree = civ.Policies.FreePolicies; // the first technology of a new civ already enters its start era
        foreach (var tech in byEra)
        {
            int eraBefore = civ.GetEraNumber(), freeBefore = civ.Policies.FreePolicies;
            civ.Tech.AddTechnology(tech.Name);
            plain.Tech.AddTechnology(tech.Name);
            int entered = int.Max(0, civ.GetEraNumber() - eraBefore);
            // exactly one free policy per era passed, also when one technology skips several eras
            Assert.Equal(entered, civ.Policies.FreePolicies - freeBefore);
            totalEntered += entered;
        }
        Assert.True(totalEntered >= 4);
        Assert.Equal(totalEntered, civ.Policies.FreePolicies - initialFree);
        Assert.Equal(0, plain.Policies.FreePolicies);
        Assert.True(civ.Policies.ShouldOpenPolicyPicker);
    }

    [Fact]
    public void ReformationIsAFounderStyleBeliefTier()
    {
        Assert.True(BeliefType.Reformation.IsFounder());
        Assert.False(BeliefType.Reformation.IsFollower());
        Assert.Equal(UniqueTarget.FounderBelief, new Belief(BeliefType.Reformation).GetUniqueTarget());
        Assert.NotEqual("", BeliefType.Reformation.GetColor());
        Assert.Equal((int)BeliefType.Enhancer + 2, (int)BeliefType.Reformation); // appended after Any, Kotlin ordinals stay
    }

    [Fact]
    public void ReformationBeliefsAreReadBackFromTheReligion()
    {
        var game2 = new TestGame();
        var belief = game2.CreateBelief(BeliefType.Reformation, "May buy [University] buildings with [Faith] [in cities following this religion]");
        var religion = new Unciv.Models.Religion("Test", game2.GameInfo, "founder", 0);
        religion.AddBelief(belief);
        Assert.Equal([belief.Name], religion.GetAllBeliefsOrdered().Select(it => it.Name));
        Assert.Equal([belief.Name], religion.GetBeliefs(BeliefType.Reformation).Select(it => it.Name));
    }

    [Fact]
    public void BnwHasTheThirteenNewBeliefsInTheirTiers()
    {
        var r = BnwRulesetTests.LoadBnw();
        var reformation = new[] { "Charitable Missions", "Evangelism", "Heathen Conversion", "Jesuit Education", "Religious Fervor", "Sacred Sites",
            "To the Glory of God", "Underground Sect", "Unity of the Prophets" };
        foreach (var name in reformation) Assert.Equal(BeliefType.Reformation, r.Beliefs[name].Type);
        foreach (var name in new[] { "Earth Mother", "God-King", "Sun God", "Tears of the Gods" }) Assert.Equal(BeliefType.Pantheon, r.Beliefs[name].Type);
        Assert.Equal(3, r.Beliefs["Jesuit Education"].Uniques.Count);
        Assert.Equal(["[+1 Faith] from every [Copper]", "[+1 Faith] from every [Iron]", "[+1 Faith] from every [Salt]"], r.Beliefs["Earth Mother"].Uniques);
        Assert.Equal(["[+2 Faith] from every [Pearls]", "[+2 Faith] from every [Gems]"], r.Beliefs["Tears of the Gods"].Uniques);
        Assert.All(r.Beliefs.Values.Where(it => it.Type == BeliefType.Reformation), it => Assert.NotEmpty(it.Uniques));
    }

    [Fact]
    public void BnwHasTheFourCivImprovementsWithTheirRules()
    {
        var r = BnwRulesetTests.LoadBnw();
        Assert.Equal("Portugal", r.TileImprovements["Feitoria"].UniqueTo);
        Assert.True(((IHasUniques)r.TileImprovements["Feitoria"]).HasUnique(UniqueType.OnlyInCityStateTerritory));
        Assert.Equal("France", r.TileImprovements["Chateau"].UniqueTo);
        Assert.True(((IHasUniques)r.TileImprovements["Chateau"]).HasUnique(UniqueType.MustBeAdjacentToLuxury));
        Assert.True(((IHasUniques)r.TileImprovements["Chateau"]).HasUnique(UniqueType.NoTwoAdjacent));
        Assert.Equal(2, r.TileImprovements["Chateau"].Culture);
        Assert.Equal(1, r.TileImprovements["Chateau"].Gold);
        Assert.Equal(["Jungle"], r.TileImprovements["Brazilwood Camp"].TerrainsCanBeBuiltOn);
        Assert.Equal(2, r.TileImprovements["Brazilwood Camp"].Gold);
        Assert.Equal(["Desert"], r.TileImprovements["Kasbah"].TerrainsCanBeBuiltOn);
        Assert.Equal(1, r.TileImprovements["Kasbah"].Food);
        Assert.Equal(1, r.TileImprovements["Kasbah"].Production);
    }

    [Fact]
    public void BnwNationTraitsCarryTheirUniques()
    {
        var r = BnwRulesetTests.LoadBnw();
        Assert.Contains("[-25]% XP required for promotions", r.Nations["The Zulus"].Uniques);
        Assert.Contains("Founded cities claim [8] extra tiles of radius", r.Nations["The Shoshone"].Uniques);
        Assert.Contains("[1] free Policies each time you enter a new era", r.Nations["Poland"].Uniques);
        Assert.Contains("May not annex cities", r.Nations["Venice"].Uniques);
        Assert.Contains("Cannot build [Settler] units", r.Nations["Venice"].Uniques);
        Assert.Contains("[+3 Gold, +1 Culture] for each trade partner", r.Nations["Morocco"].Uniques);
    }
}
