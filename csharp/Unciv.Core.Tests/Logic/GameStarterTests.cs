using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T099: GameStarter.kt on the vanilla ruleset. The Kotlin fixtures (LoadGameTest, CityStateTypeStartBiasTests) need TestGame;
// here the real CityStateFunctions (T068) runs.
[Collection("RulesetCache")]
public sealed class GameStarterTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public GameStarterTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameSetupInfo Setup(Action<GameParameters>? tweak = null, params Player[] players)
    {
        var parameters = new GameParameters
        {
            BaseRuleset = ruleset.Name,
            NumberOfCityStates = 2,
            Players = players.Length != 0
                ? [.. players]
                : [new Player("Babylon", PlayerType.Human), new Player("Greece"), new Player("Rome")],
        };
        tweak?.Invoke(parameters);
        return new GameSetupInfo(parameters, new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Small });
    }

    private static GameInfo Start(GameSetupInfo setup, long seed = 7) =>
        GameStarter.StartNewGame(setup, seed, 1000L);

    private static string Dump(GameInfo game) =>
        game.GameId + "|" + string.Join(';', game.Civilizations.Select(civ => civ.CivId + ":" + civ.PlayerType + ":" + civ.GetCivUnitsDump()));

    [Fact]
    public void StartsAGameWithBarbarianPlayersAndCityStates()
    {
        var game = Start(Setup());
        Assert.Equal(1000L, game.CurrentTurnStartTime);
        Assert.Equal(0, game.HistoryStartTurn);
        Assert.Equal(game.GameParameters.Difficulty, game.Difficulty);
        Assert.Equal(Constants.Barbarians, game.Civilizations[0].CivId);
        var names = game.Civilizations.Select(it => it.CivId).ToList();
        Assert.Contains("Babylon", names);
        Assert.Contains("Greece", names);
        Assert.Contains("Rome", names);
        Assert.Equal(2, game.Civilizations.Count(it => it.IsCityState));
        Assert.Equal(PlayerType.Human, game.Civilizations.Single(it => it.CivId == "Babylon").PlayerType);
        Assert.Empty(game.TileMap.StartingLocationsByNation); // cleared once done
        Assert.Empty(game.TileMap.StartingLocations);
    }

    [Fact]
    public void EveryRealCivGetsStartingUnitsAndTheBarbarianNone()
    {
        var game = Start(Setup());
        foreach (var civ in game.Civilizations.Where(it => !it.IsBarbarian))
        {
            var units = civ.Units.GetCivUnits().ToList();
            Assert.NotEmpty(units);
            if (civ.IsMajorCiv()) Assert.Contains(units, it => it.Name == "Settler");
            if (civ.IsCityState) Assert.All(units, it => Assert.True(it.BaseUnit.IsCityFounder()));
            Assert.All(units, it => Assert.Equal(it.GetMaxMovement(), it.CurrentMovement));
        }
        Assert.Empty(game.Civilizations.Single(it => it.IsBarbarian).Units.GetCivUnits());
    }

    [Fact]
    public void StartingGoldAndCultureFollowTheEraAndSpeed()
    {
        var game = Start(Setup());
        var era = game.Ruleset.Eras[game.GameParameters.StartingEra];
        var expectedGold = KotlinMath.FloatToInt(era.StartingGold * game.Speed.GoldCostModifier);
        Assert.Equal(expectedGold, game.Civilizations.Single(it => it.CivId == "Greece").Gold);
        Assert.Equal(0, game.Civilizations.Single(it => it.IsBarbarian).Gold);
    }

    [Fact]
    public void TheSameSeedGivesTheSameGameAndAnotherSeedAnotherGameId()
    {
        var first = Dump(Start(Setup(), seed: 11));
        Assert.Equal(first, Dump(Start(Setup(), seed: 11)));
        Assert.NotEqual(first, Dump(Start(Setup(), seed: 12)));
    }

    [Fact]
    public void NoBarbariansOmitsTheBarbarianCiv()
    {
        var game = Start(Setup(it => it.NoBarbarians = true));
        Assert.DoesNotContain(game.Civilizations, it => it.IsBarbarian);
    }

    [Fact]
    public void SpectatorsComeFirstAndGetNoUnitsOrGold()
    {
        var game = Start(Setup(null, new Player("Babylon", PlayerType.Human), new Player(Constants.Spectator, PlayerType.Human), new Player("Greece")));
        var real = game.Civilizations.Where(it => !it.IsBarbarian).ToList();
        Assert.Equal(Constants.Spectator, real[0].CivId);
        Assert.Empty(real[0].Units.GetCivUnits());
        Assert.Equal(0, real[0].Gold);
        Assert.NotEmpty(real[0].Tech.TechsResearched); // spectators know every tech
    }

    [Fact]
    public void RandomPlayersResolveToUnchosenMajorNations()
    {
        var game = Start(Setup(null, new Player("Babylon", PlayerType.Human), new Player(), new Player()));
        var majors = game.Civilizations.Where(it => it.IsMajorCiv()).Select(it => it.CivId).ToList();
        Assert.Equal(3, majors.Count);
        Assert.Equal(3, majors.Distinct().Count());
        Assert.Contains("Babylon", majors);
    }

    [Fact]
    public void RandomNumberOfPlayersAcceptsSwappedBoundsAndAddsAIPlayers()
    {
        var game = Start(Setup(it =>
        {
            it.RandomNumberOfPlayers = true;
            it.MinNumberOfPlayers = 5;
            it.MaxNumberOfPlayers = 4;
        }, new Player("Babylon", PlayerType.Human), new Player("Greece")));
        var majorCount = game.Civilizations.Count(it => it.IsMajorCiv());
        Assert.InRange(majorCount, 4, 5);
    }

    [Fact]
    public void RandomNumberOfPlayersBelowTheListSkipsAIPlayers()
    {
        var game = Start(Setup(it =>
        {
            it.RandomNumberOfPlayers = true;
            it.MinNumberOfPlayers = 2;
            it.MaxNumberOfPlayers = 2;
        }));
        Assert.Equal(2, game.Civilizations.Count(it => it.IsMajorCiv()));
        Assert.Contains(game.Civilizations, it => it.CivId == "Babylon"); // the human stays
    }

    [Fact]
    public void OneCityChallengeKeepsASingleSettlerForTheHuman()
    {
        var game = Start(Setup(it => it.OneCityChallenge = true));
        var settlers = game.Civilizations.Single(it => it.CivId == "Babylon").Units.GetCivUnits().Count(it => it.Name == "Settler");
        Assert.Equal(1, settlers);
    }

    [Fact]
    public void ShufflePlayerOrderKeepsEveryone()
    {
        var game = Start(Setup(it => it.ShufflePlayerOrder = true));
        Assert.Equal(3, game.Civilizations.Count(it => it.IsMajorCiv()));
    }

    [Fact]
    public void DeadModsAreRemovedAndUnknownBaseRulesetAndSpeedAreRepaired()
    {
        var setup = Setup(it =>
        {
            it.Mods.Add("No such mod");
            it.BaseRuleset = "No such ruleset";
            it.Speed = "No such speed";
        });
        var game = Start(setup);
        Assert.Empty(setup.GameParameters.Mods);
        Assert.Equal(ruleset.Name, game.GameParameters.BaseRuleset);
        Assert.True(game.Ruleset.Speeds.ContainsKey(game.GameParameters.Speed));
    }

    [Fact]
    public void ASavedMapNeedsAFileAndAnOpener()
    {
        var setup = Setup();
        setup.MapParameters.Name = "saved";
        Assert.Throws<InvalidOperationException>(() => Start(setup));
        setup.MapFile = "x.map";
        Assert.Throws<InvalidOperationException>(() => Start(setup));
    }

    [Fact]
    public void RandomCityStateCountIsDrawnBetweenTheSwappedBounds()
    {
        var game = Start(Setup(it =>
        {
            it.RandomNumberOfCityStates = true;
            it.MinNumberOfCityStates = 2; // min and max given the wrong way round are swapped
            it.MaxNumberOfCityStates = 1;
        }));
        Assert.InRange(game.Civilizations.Count(it => it.IsCityState), 1, 2);
    }

    [Fact]
    public void NegativeCityStateCountFailsLoudly()
    {
        var setup = Setup(it => it.NumberOfCityStates = -1);
        Assert.Throws<ArgumentException>(() => Start(setup));
    }

    [Fact]
    public void PlayerNationTransient()
    {
        var player = new Player("Babylon");
        Assert.Throws<InvalidOperationException>(() => player.ChosenNation);
        player.SetNationTransient(ruleset);
        Assert.Equal("Babylon", player.ChosenNation.Name);
        var fromNation = new Player(ruleset.Nations["Greece"], PlayerType.Human, "id");
        Assert.Equal("Greece", fromNation.ChosenCiv);
        Assert.Equal("id", fromNation.PlayerId);
    }
}

internal static class GameStarterTestExtensions
{
    public static string GetCivUnitsDump(this Civilization civ) =>
        string.Join(',', civ.Units.GetCivUnits().Select(it => $"{it.Name}@{it.CurrentTile.Position.X}/{it.CurrentTile.Position.Y}"));
}
