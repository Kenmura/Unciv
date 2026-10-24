using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapGenerator.Mapregions;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Utils;

namespace Unciv.Core.Tests.Models.Ruleset;

// T106: the ruleset-object members that T030-T033 deferred, plus the Kotlin fixtures VictoryAvailabilityTests, VictoryMilestoneTests
// (HaveCountableMilestoneTests) and CityStateTypeStartBiasTests, ported over TestGame.
[Collection("RulesetCache")]
public class DeferredRulesetMembersTests
{
    private const string VictoryName = "Reach the goal";

    private static Victory ReadVictory(string json) => UncivJson.Deserialize<Victory>(json)!;

    private static TestGame GameWithVictory(bool hiddenInVictoryScreen = false, params string[] uniques)
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(2);
        var uniquesJson = string.Join(", ", uniques.Select(it => $"\"{it}\""));
        var victory = ReadVictory(
            $$"""{ "name": "{{VictoryName}}", "uniques": [{{uniquesJson}}], "hiddenInVictoryScreen": {{(hiddenInVictoryScreen ? "true" : "false")}}, "milestones": ["Build [Monument]"] }""");
        testGame.Ruleset.Victories[victory.Name] = victory;
        testGame.GameInfo.GameParameters.VictoryTypes = [victory.Name];
        return testGame;
    }

    private static List<string> AvailableVictories(Civilization civ) => civ.VictoryManager.GetAvailableVictories().Select(it => it.Name).ToList();
    private static List<string> VictoriesShown(Civilization civ) => civ.VictoryManager.GetVictoriesShownInVictoryScreen().Select(it => it.Name).ToList();

    /// <summary>The civ completes the only milestone - who wins is then decided by the victory's uniques alone</summary>
    private static string? CompleteMilestone(TestGame testGame, bool human)
    {
        var civ = testGame.AddCiv(isPlayer: human);
        var tile = human ? testGame.GetTile(2, 0) : testGame.GetTile(0, 0);
        testGame.AddCity(civ, tile).CityConstructions.AddBuilding("Monument");
        return civ.VictoryManager.GetVictoryTypeAchieved();
    }

    // region VictoryAvailabilityTests

    [Fact]
    public void AnAiCivDoesNotAchieveAVictoryOnlyAvailableToHumans()
    {
        var testGame = GameWithVictory(false, "Only available <for [Human player] Civilizations>");
        Assert.Null(CompleteMilestone(testGame, false));
        Assert.Equal(VictoryName, CompleteMilestone(testGame, true));
    }

    [Fact]
    public void AnAiCivDoesNotAchieveAVictoryUnavailableToAis()
    {
        var testGame = GameWithVictory(false, "Unavailable <for [AI player] Civilizations>");
        Assert.Null(CompleteMilestone(testGame, false));
        Assert.Equal(VictoryName, CompleteMilestone(testGame, true));
    }

    [Fact]
    public void AVictoryWithoutUniquesIsAchievedByAnyone()
    {
        Assert.Equal(VictoryName, CompleteMilestone(GameWithVictory(), false));
        Assert.Equal(VictoryName, CompleteMilestone(GameWithVictory(), true));
    }

    [Fact]
    public void ANationSpecificVictoryIsAchievedOnlyByThatNation()
    {
        var testGame = GameWithVictory(false, "Only available <for [Babylon] Civilizations>");
        var babylon = testGame.AddCiv(testGame.Ruleset.Nations["Babylon"]);
        var other = testGame.AddCiv();
        testGame.AddCity(babylon, testGame.GetTile(0, 0)).CityConstructions.AddBuilding("Monument");
        testGame.AddCity(other, testGame.GetTile(2, 0)).CityConstructions.AddBuilding("Monument");

        Assert.Equal(VictoryName, babylon.VictoryManager.GetVictoryTypeAchieved());
        Assert.Null(other.VictoryManager.GetVictoryTypeAchieved());
    }

    [Fact]
    public void AVictoryIsOfferedInTheNewGameOptionsUnlessItSaysOtherwise()
    {
        static List<string> Selectable(params string[] uniques) =>
            GameWithVictory(false, uniques).Ruleset.SelectableVictories().Select(it => it.Name).ToList();

        Assert.Contains(VictoryName, Selectable());
        // Availability conditionals cannot be evaluated without a civilization - such a victory stays selectable
        Assert.Contains(VictoryName, Selectable("Only available <for [Human player] Civilizations>"));
        Assert.Contains(VictoryName, Selectable("Only available <for [Babylon] Civilizations>"));

        var hidden = Selectable("Will not be chosen for new games");
        Assert.DoesNotContain(VictoryName, hidden);
        Assert.NotEmpty(hidden); // The ruleset's own victories are still selectable
    }

    [Fact]
    public void AVictoryUnavailableToACivIsNotPresentedToItAsItsOwnGoal()
    {
        var testGame = GameWithVictory(false, "Only available <for [Human player] Civilizations>");
        var human = testGame.AddCiv(isPlayer: true);
        var ai = testGame.AddCiv();

        Assert.Equal([VictoryName], VictoriesShown(human));
        Assert.Empty(VictoriesShown(ai));
    }

    [Fact]
    public void AVictoryHiddenInTheVictoryScreenIsAvailableButNotShown()
    {
        var testGame = GameWithVictory(true);
        var civ = testGame.AddCiv(isPlayer: true);

        Assert.Equal([VictoryName], AvailableVictories(civ));
        Assert.Empty(VictoriesShown(civ));
        // ... and it is won by completing its milestones, like any other
        testGame.AddCity(civ, testGame.GetTile(0, 0)).CityConstructions.AddBuilding("Monument");
        Assert.Equal(VictoryName, civ.VictoryManager.GetVictoryTypeAchieved());
    }

    [Fact]
    public void EveryPerCivEntryPointGoesThroughTheSingleFilteringSource()
    {
        var testGame = GameWithVictory(false, "Only available <for [Human player] Civilizations>");
        var human = testGame.AddCiv(isPlayer: true);
        var ai = testGame.AddCiv();

        Assert.True(testGame.GameInfo.Ruleset.Victories.ContainsKey(VictoryName));
        Assert.Contains(VictoryName, testGame.GameInfo.GameParameters.VictoryTypes);

        Assert.Equal([VictoryName], AvailableVictories(human));
        Assert.Equal([VictoryName], VictoriesShown(human));
        Assert.Equal([VictoryName], human.GetPreferredVictoryTypes());

        Assert.Empty(AvailableVictories(ai));
        Assert.Empty(VictoriesShown(ai));
        Assert.Equal([Constants.NeutralVictoryType], ai.GetPreferredVictoryTypes());
        Assert.Null(ai.VictoryManager.GetVictoryTypeAchieved());
    }

    [Fact]
    public void AnAiCivDoesNotPreferAVictoryItMayNotAchieve()
    {
        var testGame = GameWithVictory(false, "Only available <for [Human player] Civilizations>");
        var ai = testGame.AddCiv();
        Assert.Equal([Constants.NeutralVictoryType], ai.GetPreferredVictoryTypes());
        Assert.Equal([VictoryName], testGame.AddCiv(isPlayer: true).GetPreferredVictoryTypes());
    }

    [Fact]
    public void VictoryUniquesAreValidated()
    {
        static List<RulesetError> ErrorsFor(params string[] uniques) =>
            GameWithVictory(false, uniques).Ruleset.GetErrorList().Where(it => it.Text.Contains(VictoryName, StringComparison.Ordinal)).ToList();

        Assert.Empty(ErrorsFor("Only available <for [Human player] Civilizations>"));
        Assert.Empty(ErrorsFor("Will not be chosen for new games"));
        // "Cannot be hurried" is a real unique, but not one a victory can carry
        Assert.Contains(ErrorsFor("Cannot be hurried"),
            it => it.ErrorSeverityToReport >= RulesetErrorSeverity.Warning && it.Text.Contains("is not allowed on its target type", StringComparison.Ordinal));
        // A bad parameter inside a victory unique is reported, instead of being silently ignored
        Assert.Contains(ErrorsFor("Only available <for [Bogus] Civilizations>"), it => it.Text.Contains("Bogus", StringComparison.Ordinal));
    }

    // endregion

    // region HaveCountableMilestoneTests

    private static Victory VictoryWithMilestones(params string[] milestones)
    {
        var milestoneList = string.Join(", ", milestones.Select(it => $"\"{it}\""));
        return ReadVictory($$"""{ "name": "Test", "milestones": [ {{milestoneList}} ] }""");
    }

    [Fact]
    public void MilestonesOfACountableVictoryCanBeCompletedInAnyOrder()
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(2);
        var victory = VictoryWithMilestones(
            "Have at least [1] [Owned [Farm] Tiles]",
            "Build [Granary]",
            "Have at least [1] [Adopted [Tradition] Policies]");
        testGame.Ruleset.Victories[victory.Name] = victory;
        testGame.GameInfo.GameParameters.VictoryTypes = [victory.Name];
        var civ = testGame.AddCiv();
        var city = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var farmMilestone = victory.MilestoneObjects[0];
        var policyMilestone = victory.MilestoneObjects[2];

        Assert.False(farmMilestone.HasBeenCompletedBy(civ));

        // Out of the order they are declared in: the policy first, then the building, then the farm
        civ.Policies.FreePolicies = 1;
        civ.Policies.Adopt(testGame.Ruleset.Policies["Tradition"], branchCompletion: false);
        Assert.True(policyMilestone.HasBeenCompletedBy(civ));
        city.CityConstructions.AddBuilding("Granary");
        Assert.Null(civ.VictoryManager.GetVictoryTypeAchieved()); // Not won yet - the farm is missing

        var farmTile = testGame.GetTile(1, 0);
        testGame.AddTileToCity(city, farmTile);
        farmTile.SetImprovement("Farm");
        Assert.True(farmMilestone.HasBeenCompletedBy(civ));
        Assert.Equal("Test", civ.VictoryManager.GetVictoryTypeAchieved());
    }

    [Fact]
    public void CountableMilestoneShowsItsProgress()
    {
        var testGame = new TestGame();
        testGame.MakeHexagonalMap(2);
        var victory = VictoryWithMilestones("Have at least [3] [Cities]");
        var civ = testGame.AddCiv();
        var milestone = victory.MilestoneObjects[0];

        Assert.Equal("{Have at least [3] [Cities]} (0/3)", milestone.GetVictoryScreenButtonHeaderText(false, civ));
        testGame.AddCity(civ, testGame.GetTile(0, 0));
        Assert.Equal("{Have at least [3] [Cities]} (1/3)", milestone.GetVictoryScreenButtonHeaderText(false, civ));
        Assert.False(milestone.HasBeenCompletedBy(civ));
    }

    [Fact]
    public void AnInvalidAmountOrCountableIsARulesetError()
    {
        var testGame = new TestGame();
        var victory = VictoryWithMilestones("Have at least [lots] [No Such Countable]");
        testGame.Ruleset.Victories[victory.Name] = victory;
        var errors = RulesetValidator.Create(testGame.Ruleset).GetErrorList();
        Assert.Contains(errors, it => it.Text.Contains("Have at least [lots] [No Such Countable]", StringComparison.Ordinal));
    }

    // endregion

    // region CityStateTypeStartBiasTests

    [Fact]
    public void GnkMaritimeCityStateTypeHasCoastStartBiasUnique()
    {
        var ruleset = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName())!;
        var maritime = ruleset.CityStateTypes["Maritime"];
        Assert.Contains(maritime.UniqueMap.GetMatchingUniques(UniqueType.StartBias, GameContext.IgnoreConditionalsState),
            it => it.Params[0] == "Coast");
    }

    [Fact]
    public void NationGetStartBiasMergesTypeUniqueAndNationField()
    {
        var ruleset = new TestGame().Ruleset;
        var type = new CityStateType { Name = "TestType", Uniques = ["Start bias [Coast]"] };
        ruleset.CityStateTypes[type.Name] = type;
        var nation = new Nation { Name = "TestCS", CityStateType = type.Name, StartBias = ["Grassland"] };

        Assert.Equal(["Grassland", "Coast"], nation.GetStartBias(ruleset).ToList());
    }

    [Fact]
    public void NationStartBiasUniqueMergesWithField()
    {
        var ruleset = new TestGame().Ruleset;
        var nation = new Nation { Name = "BiasNation", StartBias = ["Hills"], Uniques = ["Start bias [Coast]"] };

        Assert.Equal(["Hills", "Coast"], nation.GetStartBias(ruleset).ToList());
    }

    [Fact]
    public void PrefersCoastalStartUsesCityStateTypeStartBiasUnique()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(3);
        var ruleset = game.Ruleset;
        var type = new CityStateType { Name = "CoastalType", Uniques = ["Start bias [Coast]"] };
        ruleset.CityStateTypes[type.Name] = type;
        var nation = new Nation { Name = "CoastalCS", CityStateType = type.Name };
        ruleset.Nations[nation.Name] = nation;

        var civ = game.AddCiv(nation);
        Assert.True(MinorCivPlacer.PrefersCoastalStart(civ, ruleset));
    }

    [Fact]
    public void FilterTilesByStartBiasKeepsHillAndDropsAvoidSnow()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(2, Constants.Grassland);
        // Distance 2 so Avoid [Snow] on the hill tile does not see the snow neighbor.
        var hillTile = game.SetTileTerrainAndFeatures(HexCoord.Zero, Constants.Grassland, Constants.Hill);
        var snowTile = game.SetTileTerrain(new HexCoord(2, 0), Constants.Snow);

        var tiles = new List<global::Unciv.Logic.Map.Tiles.Tile> { hillTile, snowTile };
        Assert.Equal([hillTile], MinorCivPlacer.FilterTilesByStartBias(tiles, ["Hill"]));

        var avoidSnow = MinorCivPlacer.FilterTilesByStartBias(tiles, ["Avoid [Snow]"]);
        Assert.Equal([hillTile], avoidSnow);
        Assert.DoesNotContain(snowTile, avoidSnow);
    }

    // endregion

    // region Members ported by T106

    [Fact]
    public void SpecialistColorObjectIsTheRgbColor()
    {
        var specialist = UncivJson.Deserialize<Specialist>("""{ "name": "Scientist", "color": [255, 0, 51] }""")!;
        var color = specialist.ColorObject;
        Assert.Equal(1f, color.R);
        Assert.Equal(0f, color.G);
        Assert.Equal(51 / 255f, color.B);
        Assert.Same(color, specialist.ColorObject);
    }

    [Fact]
    public void TileResourceGetImprovingImprovementReturnsTheFirstBuildableImprovement()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(2, Constants.Plains);
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        var tile = game.GetTile(1, 0);
        game.AddTileToCity(city, tile);
        tile.SetTileResource("Horses");
        civ.Tech.AddTechnology("Animal Husbandry");
        var context = civ.State;
        var horses = game.Ruleset.TileResources["Horses"];

        var improving = horses.GetImprovingImprovement(tile, context);
        Assert.NotNull(improving);
        Assert.True(horses.IsImprovedBy(improving.Name));
        Assert.True(tile.ImprovementFunctions.CanBuildImprovement(improving, context));

        Assert.Null(horses.GetImprovingImprovement(tile, GameContext.EmptyState)); // no civ in the context
    }

    [Fact]
    public void TileImprovementGetTurnsToBuildAppliesSpeedAndUniques()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(2, Constants.Plains);
        var improvement = game.CreateTileImprovement();
        improvement.TurnsToBuild = 10;

        // No ImprovementTimeIncrease unique: increase is 0% -> 1f, so the time is the improvement's own turnsToBuild
        var plain = game.AddCiv();
        Assert.Equal(10, improvement.GetTurnsToBuild(plain, game.AddUnit("Worker", plain, game.GetTile(0, 0))));

        // -100% rate: increase == 0 -> buildTime 0 -> clamped to one turn
        var never = game.AddCiv(["Can build [All] improvements at a [-100]% rate"]);
        Assert.Equal(1, improvement.GetTurnsToBuild(never, game.AddUnit("Worker", never, game.GetTile(0, 1))));
        Assert.Equal(1f, plain.GameInfo.Speed.ImprovementBuildLengthModifier);

        // +100% rate halves the time (speed modifier of the simulation speed is 1)
        var fast = game.AddCiv(["Can build [All] improvements at a [100]% rate"]);
        var fastWorker = game.AddUnit("Worker", fast, game.GetTile(1, 0));
        Assert.Equal(KotlinMath.RoundToInt(fast.GameInfo.Speed.ImprovementBuildLengthModifier * 10 / 2f), improvement.GetTurnsToBuild(fast, fastWorker));

        // The specific time unique then multiplies the result: 5 * 0.5 = 2.5 rounds half-up to 3
        var faster = game.AddCiv(["Can build [All] improvements at a [100]% rate", "[-50]% construction time for [All] improvements"]);
        var fasterWorker = game.AddUnit("Worker", faster, game.GetTile(-1, 0));
        Assert.Equal(KotlinMath.RoundToInt(faster.GameInfo.Speed.ImprovementBuildLengthModifier * 10 / 2f * 0.5f), improvement.GetTurnsToBuild(faster, fasterWorker));
    }

    [Fact]
    public void TileImprovementDescriptionWrappersDelegateToTheDescriptions()
    {
        var game = new TestGame();
        var farm = game.Ruleset.TileImprovements["Farm"];
        Assert.False(string.IsNullOrEmpty(farm.GetDescription(game.Ruleset)));
        Assert.NotEmpty(farm.GetShortDescription().ToList());
        Assert.Equal(farm.GetShortDescription().Select(it => it.Text), farm.GetShortDecription().Select(it => it.Text));
    }

    [Fact]
    public void TileImprovementConstructorUnitsDropTerrainsOutsideTheCanOnlyBeBuiltOnFilter()
    {
        var game = new TestGame();
        var improvement = game.CreateTileImprovement("Can only be built on [Hill] tiles");
        improvement.TurnsToBuild = 5;
        improvement.TerrainsCanBeBuiltOn = [Constants.Plains];
        Assert.Empty(improvement.GetConstructorUnits(game.Ruleset));

        // Built on the allowed terrain, the same improvement is built by workers
        improvement.TerrainsCanBeBuiltOn = [Constants.Plains, Constants.Hill];
        Assert.Contains(improvement.GetConstructorUnits(game.Ruleset), it => it.Name == "Worker");
    }

    // endregion
}
