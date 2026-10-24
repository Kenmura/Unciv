using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;

using PolicyClass = Unciv.Models.Ruleset.Policy;

namespace Unciv.Core.Tests.Support;

// T035: the TestGame helper itself (tests/src/com/unciv/testing/TestGame.kt has no test of its own), plus the TimedUniquesTests case that only needs it.
[Collection("RulesetCache")]
public class TestGameTests
{
    [Fact]
    public void ConstructorBuildsAnEditableGnkRulesetCloneWithOneTile()
    {
        var game = new TestGame("[+1 Gold] <for [1] turns>");
        Assert.Equal(BaseRuleset.Civ_V_GnK.FullName(), game.Ruleset.Name);
        Assert.Same(game.Ruleset, game.GameInfo.Ruleset);
        Assert.NotSame(RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName()), game.Ruleset);
        Assert.Contains("[+1 Gold] <for [1] turns>", game.Ruleset.GlobalUniques.Uniques);
        Assert.Equal("Prince", game.GameInfo.Difficulty);
        Assert.Equal(Speed.DefaultForSimulation, game.GameInfo.GameParameters.Speed);
        Assert.Single(game.TileMap.Values);
        Assert.Same(game.GameInfo, game.TileMap.GameInfo);
        Assert.Empty(game.GameInfo.Civilizations);

        var other = new TestGame();
        Assert.NotSame(game.Ruleset, other.Ruleset);
        Assert.DoesNotContain("[+1 Gold] <for [1] turns>", other.Ruleset.GlobalUniques.Uniques);
    }

    [Fact]
    public void SpeedAndDifficultySettersRefreshTheGlobalTransients()
    {
        var game = new TestGame();
        game.SetSpeed("Quick");
        Assert.Equal("Quick", game.GameInfo.Speed.Name);
        game.SetDifficulty("Settler");
        Assert.Equal("Settler", game.GameInfo.GetDifficulty().Name);
    }

    [Fact]
    public void HexagonalAndRectangularMapsReplaceTheTileMap()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(2);
        Assert.Equal(19, game.TileMap.Values.Count);
        Assert.Same(game.GameInfo, game.TileMap.GameInfo);
        Assert.Same(game.TileMap[HexCoord.Zero], game.GetTile(HexCoord.Zero));
        Assert.Same(game.TileMap[1, 0], game.GetTile(1, 0));

        game.MakeRectangularMap(3, 4);
        Assert.Equal(12, game.TileMap.Values.Count);
        Assert.Same(game.GameInfo, game.TileMap.GameInfo);
    }

    [Fact]
    public void TileHelpersSetTerrainAndFeatures()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(1);
        var tile = game.SetTileTerrainAndFeatures(HexCoord.Zero, Constants.Grassland, Constants.Forest);
        Assert.Same(game.GetTile(HexCoord.Zero), tile);
        Assert.Equal(Constants.Grassland, tile.BaseTerrain);
        Assert.Equal([Constants.Forest], tile.TerrainFeatures.ToArray());
        Assert.True(tile.IsLand);

        game.SetTileFeatures(HexCoord.Zero);
        Assert.Empty(tile.TerrainFeatures);
        game.SetTileTerrain(HexCoord.Zero, Constants.Ocean);
        Assert.True(tile.IsWater);
    }

    [Fact]
    public void RulesetObjectFactoriesNameAndRegisterWithUniques()
    {
        var game = new TestGame();
        var building = game.CreateBuilding("[+1 Gold] [in this city]");
        Assert.Equal(["[+1 Gold] [in this city]"], building.Uniques.ToArray());
        Assert.Same(building, game.Ruleset.Buildings[building.Name]);
        Assert.StartsWith("Building-", building.Name);
        Assert.Same(game.Ruleset, building.Ruleset);

        var wonder = game.CreateWonder();
        Assert.True(wonder.IsWonder);
        Assert.NotEqual(building.Name, wonder.Name); // shared counter

        var unitType = game.CreateUnitType("a");
        var unit = game.CreateBaseUnit(unitType.Name, "b");
        Assert.Equal(unitType.Name, unit.UnitType);
        Assert.Same(unit, game.Ruleset.Units[unit.Name]);
        Assert.StartsWith("UnitType-", game.CreateBaseUnit().UnitType);

        var belief = game.CreateBelief(BeliefType.Founder, "x");
        Assert.Equal(BeliefType.Founder, belief.Type);
        Assert.Same(belief, game.Ruleset.Beliefs[belief.Name]);
        Assert.Same(game.CreateResource("r"), game.Ruleset.TileResources.Values.Last());
        Assert.Same(game.CreateTileImprovement(), game.Ruleset.TileImprovements.Values.Last());
        Assert.Same(game.CreateUnitPromotion(), game.Ruleset.UnitPromotions.Values.Last());
        Assert.Contains(game.CreateSpecialist(), game.Ruleset.Specialists.Keys);
    }

    [Fact]
    public void PolicyBranchHasACompletionPolicyAndOptionallyAMember()
    {
        var game = new TestGame();
        var member = game.CreatePolicy("m");
        var branch = game.CreatePolicyBranch(["branch"], member);
        Assert.Same(branch, branch.Branch);
        Assert.Same(branch, member.Branch);
        Assert.Equal(2, branch.Policies.Count);
        var complete = branch.Policies[1];
        Assert.Equal(branch.Name + PolicyClass.BranchCompleteSuffix, complete.Name);
        Assert.Same(complete, game.Ruleset.Policies[complete.Name]);
        Assert.Same(branch, game.Ruleset.PolicyBranches[branch.Name]);
        Assert.Same(branch, game.Ruleset.Policies[branch.Name]);
    }

    [Fact]
    public void AddCivRegistersAStartedCivWithOneTech()
    {
        var game = new TestGame();
        var civ = game.AddCiv(["[+1 Gold] from every [Shrine]"], isPlayer: true);
        Assert.Contains(civ, game.GameInfo.Civilizations);
        Assert.Equal(PlayerType.Human, civ.PlayerType);
        Assert.Same(game.GameInfo, civ.GameInfo);
        Assert.Equal(["The Capital"], civ.Nation.Cities.ToArray());
        Assert.Contains("[+1 Gold] from every [Shrine]", civ.Nation.Uniques);
        Assert.Single(civ.Tech.TechsResearched);
        var ai = game.AddCiv();
        Assert.Equal(PlayerType.AI, ai.PlayerType);
        Assert.NotEqual(civ.CivName, ai.CivName);
    }

    [Fact]
    public void AddBarbarianCivIsRegisteredWithoutTransients()
    {
        var game = new TestGame();
        var barbarians = game.AddBarbarianCiv();
        Assert.Equal(Constants.Barbarians, barbarians.CivName);
        Assert.Contains(barbarians, game.GameInfo.Civilizations);
    }

    [Fact]
    public void AddCityAddsPopulationAndOptionallyReplacesThePalace()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(2);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.GetTile(HexCoord.Zero), true, 3);
        Assert.Equal(3, city.Population.Population);
        Assert.Equal([city], civ.Cities.ToArray());
        Assert.False(city.CityConstructions.IsBuilt("Palace"));
        Assert.True(city.IsCapital());

        var second = game.AddCity(civ, game.GetTile(2, 0), true);
        Assert.Equal(1, second.Population.Population);
        Assert.True(second.CityConstructions.IsBuilt("Palace") || !second.IsCapital());

        var third = game.AddCiv();
        var thirdCity = game.AddCity(third, game.GetTile(-2, 0));
        Assert.True(thirdCity.CityConstructions.IsBuilt("Palace"));
        var tile = game.GetTile(0, 2);
        game.AddTileToCity(thirdCity, tile);
        Assert.Contains(tile.Position, thirdCity.Tiles);
    }

    [Fact]
    public void AddUnitPlacesTheUnitWithFullMovement()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(1);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));
        Assert.Same(game.GetTile(HexCoord.Zero), unit.GetTile());
        Assert.Equal(unit.GetMaxMovement(), unit.CurrentMovement);
        Assert.Contains(unit, civ.Units.GetCivUnits());

        var unplaced = game.AddUnit("Warrior", civ, null);
        Assert.Equal(0f, unplaced.CurrentMovement);
    }

    [Fact]
    public void DefaultMeleeAndRangedUnitsCarryTheirUniquesAndStrength()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(1);
        var civ = game.AddCiv();
        var melee = game.AddDefaultMeleeUnitWithUniques(civ, game.GetTile(HexCoord.Zero), "[+10]% Strength");
        Assert.Equal(8, melee.BaseUnit.Strength);
        Assert.Equal(2, melee.BaseUnit.Movement);
        Assert.Contains("[+10]% Strength", melee.BaseUnit.Uniques);
        Assert.Contains(melee.BaseUnit.Name, game.Ruleset.Units.Keys);

        var ranged = game.AddDefaultRangedUnitWithUniques(civ, game.GetTile(1, 0));
        Assert.Equal((5, 7, 2, 2), (ranged.BaseUnit.Strength, ranged.BaseUnit.RangedStrength, ranged.BaseUnit.Range, ranged.BaseUnit.Movement));
    }

    [Fact]
    public void AddReligionRegistersItWithTheFoundingCiv()
    {
        var game = new TestGame();
        var civ = game.AddCiv();
        var religion = game.AddReligion(civ);
        Assert.Same(religion, civ.ReligionManager.Religion);
        Assert.Same(religion, game.GameInfo.Religions[religion.Name]);
        Assert.StartsWith("Religion-", religion.Name);
    }

    [Fact]
    public void AddCivWithACityStateTypeInitializesTheCityState()
    {
        var game = new TestGame();
        var cityStateType = game.Ruleset.CityStateTypes.Keys.First();
        var cityState = game.AddCiv(cityStateType: cityStateType);
        Assert.True(cityState.IsCityState);
        Assert.Equal(cityStateType, cityState.Nation.CityStateType);
        Assert.False(game.AddCiv().IsCityState);
    }

    [Fact]
    public void TimedConditionalUniqueIsTriggerableOnAGamePolicy()
    {
        // TimedUniquesTests.testConditionalTimedUniqueIsTriggerable on the real fixture (Strength/Expires need BattleDamage (ported, T081) and TurnManager, T071)
        var game = new TestGame();
        var policy = game.CreatePolicy("[+42]% Strength <when attacking> <for [Military] units> <for [1] turns>");
        var unique = policy.UniqueObjects.First(it => it.Type == UniqueType.Strength);
        Assert.True(unique.IsTriggerable, "Unique with timed conditional must be triggerable");
    }
}
