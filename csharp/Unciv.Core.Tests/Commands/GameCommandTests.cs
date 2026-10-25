using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Units;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Commands;

// T164: command infrastructure over a real GameInfo: scanned registry, CommandContext codes, GameSession, first commands.
[Collection("RulesetCache")]
public sealed class GameCommandTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public GameCommandTests()
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

    // Two humans (Babylon = current player, Greece) and one AI, each with a city.
    private (GameSession Session, CityObject BabylonCity, CityObject GreeceCity) NewSession()
    {
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [new Player("Babylon", PlayerType.Human), new Player("Greece", PlayerType.Human), new Player("Rome")],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            7L, 1000L);
        var cities = new List<CityObject>();
        foreach (string name in new[] { "Babylon", "Greece" })
        {
            var civ = game.GetCivilization(name);
            var settler = civ.Units.GetCivUnits().First(it => it.BaseUnit.IsCityFounder());
            cities.Add(civ.AddCity(settler.CurrentTile.Position, settler));
        }
        Assert.Equal("Babylon", game.CurrentPlayer);
        return (new GameSession(game), cities[0], cities[1]);
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static int OwnUnitId(GameInfo game, string civ) => game.GetCivilization(civ).Units.GetCivUnits().First().Id;

    // One sample per command class: every [CommandType] class must appear here (a new domain ticket adds its own line).
    private static GameCommand Sample(string type, GameInfo game, string cityId) => type switch
    {
        "rename-city" => new RenameCity { CityId = cityId, Name = "Ur" },
        "set-unit-due" => new SetUnitDue { UnitId = OwnUnitId(game, "Babylon"), Due = false },
        _ => throw new InvalidOperationException($"No sample for command type \"{type}\"; add one to GameCommandTests.Sample"),
    };

    [Fact]
    public void EveryScannedCommandHasAUniqueKebabCaseNameAndIsRegistered()
    {
        var scanned = GameCommandRegistry.Scan();
        var registry = GameCommandRegistry.Create();

        Assert.Contains(scanned, it => it.Name == "rename-city" && it.CommandClass == typeof(RenameCity));
        Assert.Contains(scanned, it => it.Name == "set-unit-due" && it.CommandClass == typeof(SetUnitDue));
        Assert.Equal(scanned.Count, scanned.Select(it => it.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(scanned.Select(it => it.Name), registry.Types);
        Assert.Equal(scanned.Select(it => it.Name).OrderBy(it => it, StringComparer.Ordinal), registry.Types);
    }

    [Fact]
    public void EveryCommandRoundTripsThroughItsEnvelopeAndIsPureToValidate()
    {
        var (session, babylonCity, _) = NewSession();
        foreach (var (name, _) in GameCommandRegistry.Scan())
        {
            var command = Sample(name, session.Game, babylonCity.Id);
            Assert.Equal(name, command.Type);

            var envelope = session.Registry.ToEnvelope(5, "Babylon", command);
            string wire = JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions);
            var back = JsonSerializer.Deserialize<CommandEnvelope>(wire, UncivJson.DefaultOptions)!;
            var decoded = session.Registry.FromEnvelope(back);

            Assert.Equal(name, back.Type);
            Assert.Equal(command.GetType(), decoded.GetType());
            Assert.Equal(wire, JsonSerializer.Serialize(session.Registry.ToEnvelope(5, "Babylon", (GameCommand)decoded), UncivJson.DefaultOptions));
            Assert.DoesNotContain("actor", back.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"type\"", back.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);

            ulong before = Hash(session.Game);
            Assert.True(decoded.Validate(session.Game, "Babylon").IsValid);
            Assert.Equal(before, Hash(session.Game));
        }
    }

    [Fact]
    public void RejectedCommandsLeaveStateLogAndSequenceUntouched()
    {
        var (session, babylonCity, greeceCity) = NewSession();
        ulong before = Hash(session.Game);
        var theirUnit = OwnUnitId(session.Game, "Greece");

        var rejections = new (string Actor, GameCommand Command, string Code)[]
        {
            ("Greece", new RenameCity { CityId = greeceCity.Id, Name = "X" }, CommandErrors.NotYourTurn),
            ("Rome", new RenameCity { CityId = babylonCity.Id, Name = "X" }, CommandErrors.NotHuman),
            ("Atlantis", new RenameCity { CityId = babylonCity.Id, Name = "X" }, CommandErrors.UnknownCiv),
            ("Babylon", new RenameCity { CityId = "no-such-city", Name = "X" }, CommandErrors.UnknownCity),
            ("Babylon", new RenameCity { CityId = greeceCity.Id, Name = "X" }, CommandErrors.NotYourCity),
            ("Babylon", new RenameCity { CityId = babylonCity.Id, Name = "" }, CommandErrors.NotAllowed),
            ("Babylon", new SetUnitDue { UnitId = -77, Due = false }, CommandErrors.UnknownUnit),
            ("Babylon", new SetUnitDue { UnitId = theirUnit, Due = false }, CommandErrors.NotYourUnit),
        };
        foreach (var (actor, command, code) in rejections)
        {
            var result = session.Submit(actor, command);
            Assert.False(result.Accepted);
            Assert.Equal(code, result.Validation.Code);
            Assert.Equal(before, Hash(session.Game));
        }

        Assert.Empty(session.Log);
        Assert.Equal(0, session.LastSeq);
    }

    [Fact]
    public void ServerActorIsReservedAndAClientCannotClaimIt()
    {
        var (session, babylonCity, _) = NewSession();
        var command = new RenameCity { CityId = babylonCity.Id, Name = "X" };

        var viaClientPath = session.Submit("server", command);
        var viaTrustedPath = session.SubmitAsServer(command);

        Assert.Equal(CommandCodes.MalformedCommand, viaClientPath.Validation.Code);
        Assert.Equal(CommandErrors.NotHuman, viaTrustedPath.Validation.Code);
        Assert.Empty(session.Log);
    }

    [Fact]
    public void ActorInThePayloadIsIgnored()
    {
        var (session, babylonCity, _) = NewSession();
        string payload = $$"""{"cityId":"{{babylonCity.Id}}","name":"Hijacked","actorId":"Babylon","actor":"Babylon"}""";

        var result = session.Submit(new CommandEnvelope(0, "rename-city", "Greece", JsonSerializer.Deserialize<JsonElement>(payload)));

        Assert.Equal(CommandErrors.NotYourTurn, result.Validation.Code);
        Assert.NotEqual("Hijacked", babylonCity.Name);
    }

    [Fact]
    public void GameOverBlocksPlayerCommands()
    {
        var (session, babylonCity, _) = NewSession();
        session.Game.VictoryData = new VictoryData("Greece", "Domination", 1);

        var result = session.Submit("Babylon", new RenameCity { CityId = babylonCity.Id, Name = "X" });

        Assert.Equal(CommandErrors.GameOver, result.Validation.Code);
    }

    [Fact]
    public void RenameCityChangesTheNameAndIsLoggedWithASequenceNumber()
    {
        var (session, babylonCity, _) = NewSession();

        var first = session.Submit("Babylon", new RenameCity { CityId = babylonCity.Id, Name = "Ur" });
        var second = session.Submit("Babylon", new RenameCity { CityId = babylonCity.Id, Name = "Uruk" });

        Assert.True(first.Accepted);
        Assert.Equal(1, first.Seq);
        Assert.Equal(2, second.Seq);
        Assert.Equal("Uruk", babylonCity.Name);
        Assert.Equal([1L, 2L], session.Log.Select(it => it.Seq));
        Assert.All(session.Log, it => Assert.Equal("Babylon", it.ActorId));
    }

    [Fact]
    public void SetUnitDueNeedsMovementLeft()
    {
        var (session, _, _) = NewSession();
        var unit = session.Game.GetCivilization("Babylon").Units.GetCivUnits().First();
        unit.CurrentMovement = 0f;
        ulong before = Hash(session.Game);

        var exhausted = session.Submit("Babylon", new SetUnitDue { UnitId = unit.Id, Due = false });
        Assert.Equal(CommandErrors.NotAllowed, exhausted.Validation.Code);
        Assert.Equal(before, Hash(session.Game));

        unit.CurrentMovement = 2f;
        Assert.True(session.Submit("Babylon", new SetUnitDue { UnitId = unit.Id, Due = false }).Accepted);
        Assert.False(unit.Due);
        Assert.True(session.Submit("Babylon", new SetUnitDue { UnitId = unit.Id, Due = true }).Accepted);
        Assert.True(unit.Due);
    }

    [Fact]
    public void ReplayingTheLogOnAnIdenticalGameReproducesTheStateHash()
    {
        var (host, babylonCity, _) = NewSession();
        var unit = host.Game.GetCivilization("Babylon").Units.GetCivUnits().First();
        host.Submit("Babylon", new RenameCity { CityId = babylonCity.Id, Name = "Ur" });
        host.Submit("Babylon", new SetUnitDue { UnitId = unit.Id, Due = false });
        host.Submit("Greece", new RenameCity { CityId = babylonCity.Id, Name = "rejected, not logged" });

        string wire = JsonSerializer.Serialize(host.Log, UncivJson.DefaultOptions);
        var (follower, _, _) = NewSession();
        foreach (var envelope in JsonSerializer.Deserialize<List<CommandEnvelope>>(wire, UncivJson.DefaultOptions)!)
            follower.Replay(envelope);

        Assert.Equal(2, follower.LastSeq);
        Assert.Equal(Hash(host.Game), Hash(follower.Game));
    }

    [Fact]
    public void MalformedPayloadsAndUnknownTypesAreRejectedNotThrown()
    {
        var (session, _, _) = NewSession();

        var unknown = session.Submit(new CommandEnvelope(0, "teleport", "Babylon", JsonSerializer.Deserialize<JsonElement>("{}")));
        var malformed = session.Submit(new CommandEnvelope(0, "set-unit-due", "Babylon", JsonSerializer.Deserialize<JsonElement>("""{"unitId":"x"}""")));

        Assert.Equal(CommandCodes.UnknownCommand, unknown.Validation.Code);
        Assert.Equal(CommandCodes.MalformedCommand, malformed.Validation.Code);
        Assert.False(session.IsFaulted);
    }

    [Fact]
    public void RegistryRefusesTypesThatAreNotCommandsOrAreRegisteredTwice()
    {
        var registry = GameCommandRegistry.Create();

        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(string), "text"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(RenameCity), "rename-city-again"));
        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(SetUnitDue), "rename-city"));
    }

    [Fact]
    public void NotEnoughCodeNamesTheStatInLowerCase() =>
        Assert.Equal("not-enough-gold", CommandErrors.NotEnough("Gold"));
}
