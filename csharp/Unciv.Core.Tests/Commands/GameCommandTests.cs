using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Commands.Diplomacy;
using Unciv.Core.Tests.Commands.Empire;
using Unciv.Core.Tests.Commands.GreatWorks;
using Unciv.Core.Tests.Commands.Turn;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.City;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

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
    private static GameCommand Sample(string type, GameInfo game, string cityId, Scenario s, CityScenario c) => type switch
    {
        "rename-city" => new RenameCity { CityId = cityId, Name = "Ur" },
        "set-unit-due" => new SetUnitDue { UnitId = OwnUnitId(game, "Babylon"), Due = false },
        "move-unit" => new MoveUnit { UnitId = s.MoverId, To = s.MoveTo },
        "set-move-order" => new SetMoveOrder { UnitId = s.MoverId, To = s.FarTo },
        "swap-units" => new SwapUnits { UnitId = s.SwapId, To = s.SwapTo },
        "attack-with-unit" => new AttackWithUnit { UnitId = s.AttackerId, Target = s.DefenderTile },
        "bombard-with-city" => new BombardWithCity { CityId = cityId, Target = s.BombardTo },
        "nuke" => new Nuke { UnitId = s.NukeId, Target = s.NukeTo },
        "air-sweep" => new AirSweep { UnitId = s.FighterId, Target = s.SweepTo },
        "disband-unit" => new DisbandUnit { UnitId = s.MoverId },
        "execute-unit-action" => new ExecuteUnitAction { UnitId = s.MoverId, Descriptor = new UnitActionDescriptor(UnitActionType.Skip) },
        "set-worker-order" => new SetWorkerOrder { UnitId = s.MoverId, Order = WorkerOrderKind.Cancel },
        "promote-unit" => PromotionSample(game, s.MoverId),
        "queue-construction" => new QueueConstruction { CityId = cityId, Name = "Warrior" },
        "remove-from-queue" => new RemoveFromQueue { CityId = cityId, Index = 0 },
        "reorder-queue" => new ReorderQueue { CityId = cityId, Index = 0, Move = QueueMove.Down },
        "purchase-construction" => new PurchaseConstruction { CityId = cityId, Name = "Monument" },
        "buy-tile" => new BuyTile { CityId = cityId, Tile = c.BuyTile },
        "work-tile" => new WorkTile { CityId = cityId, Tile = c.Unworked },
        "stop-working-tile" => new StopWorkingTile { CityId = cityId, Tile = c.Worked },
        "lock-tile" => new LockTile { CityId = cityId, Tile = c.Worked },
        "unlock-tile" => new UnlockTile { CityId = cityId, Tile = c.Locked },
        "assign-specialist" => new AssignSpecialist { CityId = cityId, Specialist = "Merchant" },
        "unassign-specialist" => new UnassignSpecialist { CityId = cityId, Specialist = "Artist" },
        "set-manual-specialists" => new SetManualSpecialists { CityId = cityId, Manual = false },
        "set-city-focus" => new SetCityFocus { CityId = cityId, Focus = CityFocus.ProductionFocus },
        "reassign-population" => new ReassignPopulation { CityId = cityId, ResetLocked = true },
        "toggle-avoid-growth" => new ToggleAvoidGrowth { CityId = cityId },
        "sell-building" => new SellBuilding { CityId = cityId, Building = "Temple" },
        "annex-city" => new AnnexCity { CityId = c.PuppetCityId },
        "set-razing" => new SetRazing { CityId = c.RazeCityId, Raze = true },
        "set-unit-saved-promotion" => new SetUnitSavedPromotion { CityId = cityId, Unit = "Warrior", Value = false },
        "set-construction-disabled" => new SetConstructionDisabled { CityId = cityId, Name = "Warrior", Disabled = true },
        "remove-all-by-name" => new RemoveAllByName { CityId = cityId, Name = "Warrior" },
        "set-civ-construction-disabled" => new SetCivConstructionDisabled { CityId = cityId, Name = "Warrior", Disabled = true },
        _ => throw new InvalidOperationException($"No sample for command type \"{type}\"; add one to GameCommandTests.Sample"),
    };

    // A unit with experience to spare picks the first promotion it is offered (T189).
    private static PromoteUnit PromotionSample(GameInfo game, int unitId)
    {
        var unit = game.GetCivilization("Babylon").Units.GetCivUnits().First(it => it.Id == unitId);
        unit.Promotions.XP = 1000;
        return new PromoteUnit { UnitId = unitId, Promotion = unit.Promotions.GetAvailablePromotions().First().Name };
    }

    // Units and targets that make every movement and combat sample valid on the started game (T188).
    private sealed record Scenario(
        int MoverId, HexCoord MoveTo, HexCoord FarTo, int SwapId, HexCoord SwapTo, int AttackerId, HexCoord DefenderTile,
        HexCoord BombardTo, int NukeId, HexCoord NukeTo, int FighterId, HexCoord SweepTo);

    // City commands (T191): a city with a queue, buildings, specialist slots, a free citizen, a locked tile; plus a puppet and a razable city.
    private sealed record CityScenario(HexCoord BuyTile, HexCoord Unworked, HexCoord Worked, HexCoord Locked, string PuppetCityId, string RazeCityId);

    private static CityScenario ArrangeCity(GameInfo game, CityObject city)
    {
        var babylon = game.GetCivilization("Babylon");
        babylon.AddGold(5000);
        city.CityConstructions.AddBuilding("Temple");
        city.CityConstructions.AddBuilding("Market");
        city.UnitShouldUseSavedPromotion["Warrior"] = true;
        city.CityConstructions.AddToQueue("Warrior");
        city.CityConstructions.AddToQueue("Warrior");
        city.Population.SetPopulation(5);
        city.ReassignPopulation(resetLocked: true);
        city.ManualSpecialists = true;
        city.Population.SpecialistAllocations.Clear();
        var worked = city.GetWorkedTiles().Where(it => !it.IsCityCenter()).ToList();
        Assert.True(worked.Count >= 4);
        city.StopWorkingTile(worked[0]);
        city.StopWorkingTile(worked[1]);
        city.Population.SpecialistAllocations.Add("Artist", 1);
        city.LockTile(worked[3]);
        city.CityStats.Update();

        var buy = game.TileMap.TileList.First(it => city.Expansion.CanBuyTile(it));
        var center = city.GetCenterTile();
        CityObject FarCity(Func<TileClass, bool> extra)
        {
            var tile = game.TileMap.TileList.First(it => !it.IsWater && it.GetOwner() == null && it.AerialDistanceTo(center) >= 8 && extra(it));
            return babylon.AddCity(tile.Position);
        }
        var puppet = FarCity(_ => true);
        puppet.IsPuppet = true;
        var razable = FarCity(it => it.AerialDistanceTo(puppet.GetCenterTile()) >= 8);
        return new CityScenario(buy.Position, worked[0].Position, worked[2].Position, worked[3].Position, puppet.Id, razable.Id);
    }

    private static Scenario Arrange(GameInfo game, CityObject city)
    {
        var babylon = game.GetCivilization("Babylon");
        var rome = game.GetCivilization("Rome");
        babylon.DiplomacyFunctions.MakeCivilizationsMeet(rome);
        babylon.GetDiplomacyManager(rome)!.DeclareWar();
        var center = city.GetCenterTile();

        MapUnit Place(Unciv.Logic.Civilization.Civilization civ, string name, HexCoord near)
        {
            var unit = civ.Units.PlaceUnitNearTile(near, name)!;
            unit.CurrentMovement = unit.GetMaxMovement();
            return unit;
        }

        var mover = Place(babylon, "Warrior", center.Position);
        var swapA = Place(babylon, "Warrior", center.Position);
        var swapB = Place(babylon, "Warrior", swapA.CurrentTile.Position);
        var attacker = Place(babylon, "Warrior", center.Position);
        var defender = Place(rome, "Warrior", attacker.CurrentTile.Position);
        var bombarded = Place(rome, "Warrior", center.Position);
        var nuke = Place(babylon, "Atomic Bomb", center.Position);
        var fighter = Place(babylon, "Fighter", center.Position);
        fighter.Action = "Air Sweep";
        babylon.ViewableTiles = game.TileMap.TileList.ToHashSet();

        var moveTo = mover.CurrentTile.Neighbors.First(it => mover.Movement.CanMoveTo(it) && mover.Movement.CanReachInCurrentTurn(it));
        var farTo = game.TileMap.TileList.First(it => !it.IsWater && it.AerialDistanceTo(mover.CurrentTile) is >= 2 and <= 3 && mover.Movement.CanReach(it));
        var nukeTo = game.TileMap.TileList.First(it => Unciv.Logic.Battle.Nuke.MayUseNuke(new Unciv.Logic.Battle.MapUnitCombatant(nuke), it));
        var sweepTo = fighter.GetTile().GetTilesInDistanceRange(new IntRange(1, fighter.GetRange())).First();
        return new Scenario(mover.Id, moveTo.Position, farTo.Position, swapA.Id, swapB.CurrentTile.Position, attacker.Id,
            defender.CurrentTile.Position, bombarded.CurrentTile.Position, nuke.Id, nukeTo.Position, fighter.Id, sweepTo.Position);
    }

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
        var scenario = Arrange(session.Game, babylonCity);
        var cityScenario = ArrangeCity(session.Game, babylonCity);
        foreach (var (name, _) in GameCommandRegistry.Scan())
        {
            var game = session.Game;
            string actor = "Babylon";
            GameCommand command;
            if (DiplomacySamples.Types.Contains(name))
            {
                var world = DiplomacySamples.NewWorld();
                game = world.Game.GameInfo;
                actor = world.A.CivId;
                command = DiplomacySamples.Sample(name, world);
            }
            else if (GreatWorkSamples.Types.Contains(name))
            {
                var sample = new GreatWorkSamples.Sample();
                game = sample.World.Game.GameInfo;
                actor = sample.World.A.CivId;
                command = GreatWorkSamples.Command(name, sample);
            }
            else if (EmpireSamples.Types.Contains(name))
            {
                var (fresh, _, _) = NewSession();
                game = fresh.Game;
                command = EmpireSamples.Sample(name, game);
            }
            else if (name == "establish-trade-route")
            {
                var (world, sample) = Logic.TradeRoutes.TradeRouteTests.NewCommandSample();
                game = world.Game.GameInfo;
                actor = world.A.CivId;
                command = sample;
            }
            else if (name == "run-console-command")
            {
                var (fresh, _, _) = NewSession();
                game = fresh.Game;
                game.GameParameters.GodMode = true;
                command = new Unciv.Commands.DevConsole.RunConsoleCommand { Line = "civ addstat gold 5" };
            }
            else if (TurnSamples.Types.Contains(name))
            {
                var (fresh, _, _) = NewSession();
                game = fresh.Game;
                (command, actor) = TurnSamples.Sample(name, game);
            }
            else command = Sample(name, game, babylonCity.Id, scenario, cityScenario);
            Assert.Equal(name, command.Type);

            var envelope = session.Registry.ToEnvelope(5, actor, command);
            string wire = JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions);
            var back = JsonSerializer.Deserialize<CommandEnvelope>(wire, UncivJson.DefaultOptions)!;
            var decoded = session.Registry.FromEnvelope(back);

            Assert.Equal(name, back.Type);
            Assert.Equal(command.GetType(), decoded.GetType());
            Assert.Equal(wire, JsonSerializer.Serialize(session.Registry.ToEnvelope(5, actor, (GameCommand)decoded), UncivJson.DefaultOptions));
            Assert.DoesNotContain("actor", back.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"type\"", back.Payload.GetRawText(), StringComparison.OrdinalIgnoreCase);

            ulong before = Hash(game);
            var validation = decoded.Validate(game, actor);
            Assert.True(validation.IsValid, name + ": " + validation.Message);
            Assert.Equal(before, Hash(game));
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
