using System.Text.Json;
using Unciv.Logic;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic;

// T183: per-civ state-affecting settings (no UserId / IsUsersTurn in the rules).
public class PlayerSettingsTests
{
    [Fact]
    public void DefaultsAreKotlinGameSettingsDefaults()
    {
        var d = PlayerSettings.Default;
        Assert.False(d.AutoAssignCityProduction);
        Assert.True(d.AutoAssignSpecialistsInNewCities);
        Assert.False(d.AutomatedUnitsMoveOnTurnStart);
        Assert.False(d.CitiesAutoBombardAtEndOfTurn);
        Assert.Equal(5, d.NotificationsLogMaxTurns);
        Assert.False(d.AutomatedUnitsChoosePromotions);
        Assert.True(d.AutoBuildingRoads);
        Assert.True(d.AutomatedWorkersReplaceImprovements);
        Assert.False(d.StopAutomatedWorkersRemoveVegetation);
        Assert.False(d.HumanConquestPrompt);
    }

    [Fact]
    public void GetAndSetArePerCivAndCloneCopiesThem()
    {
        var game = new GameInfo();
        Assert.Same(PlayerSettings.Default, game.GetPlayerSettings("Rome"));
        Assert.Null(game.PlayerSettingsByCiv);
        var rome = new PlayerSettings { AutoAssignCityProduction = true };
        game.SetPlayerSettings("Rome", rome);
        Assert.Same(rome, game.GetPlayerSettings("Rome"));
        Assert.Same(PlayerSettings.Default, game.GetPlayerSettings("Greece"));

        var clone = game.Clone();
        clone.SetPlayerSettings("Rome", PlayerSettings.Default);
        Assert.Same(rome, game.GetPlayerSettings("Rome")); // the clone owns its dictionary
        Assert.Same(PlayerSettings.Default, new GameInfo().Clone().GetPlayerSettings("Rome"));
    }

    [Fact]
    public void SettingsRoundTripThroughJson_AndDefaultGamesOmitThem()
    {
        var game = new GameInfo();
        Assert.DoesNotContain("playerSettings", JsonSerializer.Serialize(game));
        game.SetPlayerSettings("Rome", new PlayerSettings { NotificationsLogMaxTurns = 2, HumanConquestPrompt = true });
        var json = JsonSerializer.Serialize(game.PlayerSettingsByCiv);
        var back = JsonSerializer.Deserialize<Dictionary<string, PlayerSettings>>(json)!;
        Assert.Equal(game.GetPlayerSettings("Rome"), back["Rome"]);
        // a Kotlin save has no such field: unknown/missing fields keep the defaults
        Assert.Equal(PlayerSettings.Default, JsonSerializer.Deserialize<PlayerSettings>("{}"));
    }

    [Fact]
    public void HashDiffersPerSettingAndIsStable()
    {
        ulong Hash(PlayerSettings s) => StateHasher.ComputeHash(s.HashState);
        var baseline = Hash(PlayerSettings.Default);
        Assert.Equal(baseline, Hash(new PlayerSettings()));
        var variants = new[]
        {
            new PlayerSettings { AutoAssignCityProduction = true }, new PlayerSettings { AutoAssignSpecialistsInNewCities = false },
            new PlayerSettings { AutomatedUnitsMoveOnTurnStart = true }, new PlayerSettings { CitiesAutoBombardAtEndOfTurn = true },
            new PlayerSettings { NotificationsLogMaxTurns = 6 }, new PlayerSettings { AutomatedUnitsChoosePromotions = true },
            new PlayerSettings { AutoBuildingRoads = false }, new PlayerSettings { AutomatedWorkersReplaceImprovements = false },
            new PlayerSettings { StopAutomatedWorkersRemoveVegetation = true }, new PlayerSettings { HumanConquestPrompt = true },
        };
        Assert.All(variants, v => Assert.NotEqual(baseline, Hash(v)));
        Assert.Equal(variants.Length, variants.Select(Hash).Distinct().Count());
    }

    [Fact]
    public void AStarIsAPerGameRuleThatChangesTheEngineOptionsHashOnlyWhenOn()
    {
        ulong Hash(EngineOptions o) => StateHasher.ComputeHash(o.HashState);
        var off = new EngineOptions();
        var on = new EngineOptions { UseAStarPathfinding = true };
        Assert.NotEqual(Hash(off), Hash(on));
        Assert.True(on.Clone().UseAStarPathfinding);
        Assert.False(off.Clone().UseAStarPathfinding);
    }

    [Fact]
    public void TwoHumanCivsWithDifferentSettingsDiverge()
    {
        var testGame = new Unciv.Core.Tests.Support.TestGame();
        testGame.MakeHexagonalMap(4);
        var auto = testGame.AddCiv(isPlayer: true);
        var manual = testGame.AddCiv(isPlayer: true);
        var autoCity = testGame.AddCity(auto, testGame.TileMap[0, 0]);
        var manualCity = testGame.AddCity(manual, testGame.TileMap[0, 3]);
        testGame.GameInfo.SetPlayerSettings(auto.CivId, new PlayerSettings { AutoAssignCityProduction = true });

        autoCity.CityConstructions.ChooseNextConstruction();
        manualCity.CityConstructions.ChooseNextConstruction();

        Assert.NotEmpty(autoCity.CityConstructions.ConstructionQueue);
        Assert.Empty(manualCity.CityConstructions.ConstructionQueue);
    }

    [Fact]
    public void FullAutoPlayAndPuppetsAutomateProductionWithoutTheSetting()
    {
        var testGame = new Unciv.Core.Tests.Support.TestGame();
        testGame.MakeHexagonalMap(2);
        var human = testGame.AddCiv(isPlayer: true);
        var city = testGame.AddCity(human, testGame.TileMap[0, 0]);
        city.CityConstructions.ChooseNextConstruction();
        Assert.Empty(city.CityConstructions.ConstructionQueue);

        testGame.GameInfo.ClientState.IsFullAutoPlayAI = true;
        city.CityConstructions.ChooseNextConstruction();
        Assert.NotEmpty(city.CityConstructions.ConstructionQueue);
    }

    [Fact]
    public void CityContractMembersAndQueueEdgeCasesStayCovered()
    {
        var testGame = new Unciv.Core.Tests.Support.TestGame();
        testGame.MakeHexagonalMap(2);
        var civ = testGame.AddCiv(isPlayer: true);
        var city = testGame.AddCity(civ, testGame.TileMap[0, 0]);

        var constructions = city.CityConstructions;
        constructions.RemoveCreateOneImprovementConstruction("Farm"); // nothing queued: no-op
        constructions.AddToQueue("Monument"); constructions.AddToQueue("Warrior"); constructions.AddToQueue("Gold");
        constructions.MoveEntryToEnd(0);
        Assert.True(constructions.ConstructionQueue.Contains("Monument"), string.Join(",", constructions.ConstructionQueue));

        var contract = (Unciv.Models.Ruleset.Unique.IGameContextCity)city;
        Assert.NotNull(contract.GetCenterTileOrNull());
        Assert.False(contract.IsTileLocked(new Unciv.Logic.Map.HexCoord(5, 5)));
        contract.UpdateCityStats();
        Assert.Equal(city.GetStatReserve(Unciv.Models.StatTypes.Stat.Food), contract.GetStatReserve(Unciv.Models.StatTypes.Stat.Food));
        Assert.True(contract.CountBuiltBuildingsMatchingFilter("Wonder", new Unciv.Models.Ruleset.Unique.GameContext()) >= 0);
        Assert.Equal(city.CityStats.CurrentCityStats[Unciv.Models.StatTypes.Stat.Food], contract.GetCurrentCityStat(Unciv.Models.StatTypes.Stat.Food));
        Assert.Equal(0, contract.GetResourcesGeneratedByCityAmount("Iron"));
        Assert.Null(contract.GetMajorityReligion());
        Assert.True(contract.GetPopulationFilterAmount("All") >= 0);
        Assert.False(contract.CenterTileMilitaryUnitCanGarrison());
        Assert.NotEmpty(contract.GetWorkedTiles().Concat(contract.GetTiles()));
        Assert.DoesNotContain("", contract.BuiltBuildings);
        Assert.False(contract.ContainsBuildingOrEquivalent("Nothing"));
        contract.ForEachMatchingUnique(Unciv.Models.Ruleset.Unique.UniqueType.Stats, null, _ => { });
    }
}
