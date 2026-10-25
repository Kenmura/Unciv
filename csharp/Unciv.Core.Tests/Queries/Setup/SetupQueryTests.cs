using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Metadata;
using Unciv.Queries.Setup;

namespace Unciv.Core.Tests.Queries.Setup;

// T199: new-game option data, setup validation and the NewGame entry point.
[Collection("RulesetCache")]
public class SetupQueryTests
{
    public SetupQueryTests() => _ = new TestGame();

    private static GameSetupInfo ValidSetup()
    {
        var setup = GameSetupInfo.CreateDefault();
        setup.GameParameters.Players =
        [
            new Player("Germany", PlayerType.Human),
            new Player("Egypt"),
        ];
        setup.GameParameters.NumberOfCityStates = 0;
        setup.GameParameters.VictoryTypes = ["Domination"];
        setup.MapParameters.MapSize = new Unciv.Logic.Map.MapSize(Unciv.Logic.Map.MapSize.Predefined.Tiny);
        return setup;
    }

    [Fact]
    public void OptionsListNationsInRulesetOrderAndMarkChoosable()
    {
        var options = new NewGameOptionsQuery().Run();
        var ruleset = Unciv.Models.Ruleset.RulesetCache.GetComplexRuleset(new Unciv.Utils.LinkedHashSet<string>(), options.BaseRuleset);
        Assert.Equal(ruleset.Nations.Values.Where(it => !it.IsBarbarian).Select(it => it.Name), options.Nations.Select(it => it.Name));
        Assert.Contains(options.Nations, it => it is { Name: "Germany", Choosable: true, IsMajorCiv: true });
        Assert.DoesNotContain(options.Nations, it => it.Name == "Barbarians");
        Assert.All(options.Nations.Where(it => it.IsCityState), it => Assert.False(it.Choosable));
        Assert.Equal(ruleset.Difficulties.Keys, options.Difficulties.Select(it => it.Name));
        Assert.Equal(ruleset.Speeds.Keys, options.Speeds.Select(it => it.Name));
        Assert.Contains(options.AvailableRulesets, it => it.IsBaseRuleset);
        Assert.Contains("Hexagonal", options.MapOptions.Shapes);
        Assert.Contains("Pangaea", options.MapOptions.Types);
        Assert.Contains(options.Victories, it => it.Selectable);
        Assert.Equal(options.Victories.Where(it => it.Selectable).Select(it => it.Name), options.DefaultGameParameters.VictoryTypes);
    }

    [Fact]
    public void OptionsRoundTripThroughJson()
    {
        var options = new NewGameOptionsQuery().Run();
        var json = JsonSerializer.Serialize(options, UncivJson.DefaultOptions);
        var back = JsonSerializer.Deserialize<NewGameOptions>(json, UncivJson.DefaultOptions)!;
        Assert.Equal(options.Nations.Select(it => it.Name), back.Nations.Select(it => it.Name));
        Assert.Equal(options.Mods, back.Mods);
    }

    [Fact]
    public void OptionsFallBackToVanillaForUnknownBase()
    {
        var options = new NewGameOptionsQuery("No such ruleset", ["No such mod"]).Run();
        Assert.Equal(Unciv.Models.Ruleset.RulesetCache.GetVanillaRuleset().Name, options.BaseRuleset);
        Assert.Empty(options.Mods);
    }

    [Fact]
    public void ValidSetupHasNoErrors()
    {
        var result = new ValidateSetupQuery(ValidSetup()).Run();
        Assert.True(result.Ok, string.Join(";", result.Errors.Select(it => it.Message)));
    }

    [Fact]
    public void ValidationDoesNotMutateTheSetup()
    {
        var setup = ValidSetup();
        setup.MapParameters.MapSize = new Unciv.Logic.Map.MapSize(5, 4);
        setup.MapParameters.MapSize.Name = "Custom";
        setup.MapParameters.WorldWrap = true;
        var before = JsonSerializer.Serialize(setup, UncivJson.DefaultOptions);
        var result = new ValidateSetupQuery(setup).Run();
        Assert.Contains(result.Errors, it => it.Code == "MapSize");
        Assert.Equal(before, JsonSerializer.Serialize(setup, UncivJson.DefaultOptions));
    }

    [Fact]
    public void ValidationFindsPlayerAndVictoryProblems()
    {
        var setup = ValidSetup();
        setup.GameParameters.Players =
        [
            new Player("Germany"),
            new Player("Germany"),
            new Player("Atlantis"),
            new Player("Spectator"),
            new Player("Barbarians"),
        ];
        setup.GameParameters.VictoryTypes = [];
        var codes = new ValidateSetupQuery(setup).Run().Errors.Select(it => it.Code).ToList();
        Assert.Contains("NoHumanPlayers", codes);
        Assert.Contains("DuplicateNation", codes);
        Assert.Contains("UnknownNation", codes);
        Assert.Contains("SpectatorMustBeHuman", codes);
        Assert.Contains("NoVictoryTypes", codes);
        Assert.Contains("NationNotChoosable", codes);
    }

    [Fact]
    public void ValidationPlayerCounts()
    {
        var setup = ValidSetup();
        setup.GameParameters.Players = [new Player("Germany", PlayerType.Human)];
        Assert.Contains(new ValidateSetupQuery(setup).Run().Errors, it => it.Code == "TooFewPlayers");
        setup.GameParameters.RandomNumberOfPlayers = true;
        Assert.DoesNotContain(new ValidateSetupQuery(setup).Run().Errors, it => it.Code == "TooFewPlayers");
        setup.GameParameters.Players = Enumerable.Range(0, 500).Select(_ => new Player()).ToList();
        Assert.Contains(new ValidateSetupQuery(setup).Run().Errors, it => it.Code == "TooManyPlayers");
    }

    [Fact]
    public void HostedGameNeedsDistinctPlayerIdsAndAHumanThatIsNotSpectator()
    {
        var setup = ValidSetup();
        var hosted = NewGame.ApplyLobby(setup, [new LobbySlot("Germany", "a"), new LobbySlot("Egypt", "a"), new LobbySlot("Rome", "")]);
        var codes = new ValidateSetupQuery(hosted).Run().Errors.Select(it => it.Code).ToList();
        Assert.Contains("DuplicatePlayerId", codes);
        Assert.Contains("MissingPlayerId", codes);

        var spectators = NewGame.ApplyLobby(setup, [new LobbySlot("Spectator", "a"), new LobbySlot("Spectator", "b")]);
        Assert.Contains(new ValidateSetupQuery(spectators).Run().Errors, it => it.Code == "NoHumanPlayers");
    }

    [Fact]
    public void LobbyMappingRoundTripsAndLeavesInputUntouched()
    {
        var setup = ValidSetup();
        var slots = new[] { new LobbySlot("Germany", "k1"), new LobbySlot("Egypt", "k2") };
        var hosted = NewGame.ApplyLobby(setup, slots);
        Assert.False(setup.GameParameters.IsOnlineMultiplayer);
        Assert.True(hosted.GameParameters.IsOnlineMultiplayer);
        Assert.Equal(slots, NewGame.ToLobby(hosted));
    }

    [Fact]
    public void StartIsDeterministicInSeedAndClock()
    {
        var a = NewGame.Start(ValidSetup(), 42, 1_000);
        var b = NewGame.Start(ValidSetup(), 42, 1_000);
        Assert.Equal(a.GameId, b.GameId);
        Assert.Equal(a.CurrentTurnStartTime, b.CurrentTurnStartTime);
        Assert.Equal(a.TileMap.TileList.Count, b.TileMap.TileList.Count);
        Assert.Equal(a.Civilizations.Select(it => it.CivName), b.Civilizations.Select(it => it.CivName));
        var c = NewGame.Start(ValidSetup(), 43, 1_000);
        Assert.NotEqual(a.GameId, c.GameId);
    }

    [Fact]
    public void StartRefusesAnInvalidSetup()
    {
        var setup = ValidSetup();
        setup.GameParameters.VictoryTypes = [];
        var ex = Assert.Throws<UncivShowableException>(() => NewGame.Start(setup, 1, 1));
        Assert.Contains("No victory conditions", ex.Message);
    }
}
