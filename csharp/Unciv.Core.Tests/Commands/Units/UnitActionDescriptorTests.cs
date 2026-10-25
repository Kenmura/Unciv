using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Queries;
using Unciv.Queries.Units;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Commands.Units;

// T189: unit action descriptors. Each scenario is built twice: once the descriptor goes through ExecuteUnitAction, once the
// closure of the same action is invoked directly; both games must end in the same state.
[Collection("RulesetCache")]
public sealed class UnitActionDescriptorTests
{
    private sealed class Env
    {
        public readonly TestGame Game = new();
        public readonly CivilizationObject A;
        public readonly CivilizationObject B;
        public readonly GameSession Session;

        public Env()
        {
            Game.MakeHexagonalMap(6);
            A = Game.AddCiv(Game.Ruleset.Nations["Germany"], isPlayer: true);
            B = Game.AddCiv(Game.Ruleset.Nations["Egypt"], isPlayer: true);
            Game.GameInfo.CurrentPlayerCiv = A;
            Game.GameInfo.CurrentPlayer = A.CivId;
            Game.AddCity(A, Game.GetTile(-6, 0)); // keeps the civ alive and gives it a capital
            Game.AddUnit("Warrior", B, Game.GetTile(6, 0)); // keeps the other civ alive
            foreach (var tile in Game.TileMap.TileList) tile.SetExplored(A, true);
            Session = new GameSession(Game.GameInfo);
        }

        public MapUnit Unit(string name, int x, int y)
        {
            var unit = Game.AddUnit(name, A, Game.GetTile(x, y));
            unit.CurrentMovement = unit.GetMaxMovement();
            return unit;
        }

        public ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(Game.GameInfo, forceZip: false)));
    }

    // type, unit name, tile, setup, expected key (null: no key to check)
    public static TheoryData<string> Scenarios() => new()
    {
        "Fortify", "FortifyUntilHealed", "Sleep", "SleepUntilHealed", "Explore", "Automate", "StopMovement", "StopExploration",
        "StopAutomation", "Skip", "FoundCity", "Pillage", "Upgrade", "SetUp", "CreateImprovement", "GiftUnit",
    };

    private static (MapUnit Unit, UnitActionType Type, string? Key) Build(Env env, string scenario)
    {
        switch (scenario)
        {
            case "Fortify": return (env.Unit("Warrior", 0, 0), UnitActionType.Fortify, null);
            case "FortifyUntilHealed":
                var hurt = env.Unit("Warrior", 0, 0);
                hurt.Health = 50;
                return (hurt, UnitActionType.FortifyUntilHealed, null);
            case "Sleep": return (env.Unit("Worker", 0, 0), UnitActionType.Sleep, null);
            case "SleepUntilHealed":
                var worker = env.Unit("Worker", 0, 0);
                worker.Health = 50;
                return (worker, UnitActionType.SleepUntilHealed, null);
            case "Explore": return (env.Unit("Warrior", 0, 0), UnitActionType.Explore, null);
            case "Automate": return (env.Unit("Worker", 0, 0), UnitActionType.Automate, null);
            case "StopMovement":
                var mover = env.Unit("Warrior", 0, 0);
                mover.Action = "moveTo 3,0";
                return (mover, UnitActionType.StopMovement, null);
            case "StopExploration":
                var explorer = env.Unit("Warrior", 0, 0);
                explorer.Action = UnitActionType.Explore.Value();
                return (explorer, UnitActionType.StopExploration, null);
            case "StopAutomation":
                var automated = env.Unit("Worker", 0, 0);
                automated.Automated = true;
                automated.Action = "Automate";
                return (automated, UnitActionType.StopAutomation, null);
            case "Skip": return (env.Unit("Warrior", 0, 0), UnitActionType.Skip, null);
            case "FoundCity": return (env.Unit("Settler", 0, 0), UnitActionType.FoundCity, null);
            case "Pillage":
                var tile = env.Game.GetTile(0, 0);
                tile.ImprovementFunctions.SetImprovement("Farm");
                return (env.Unit("Warrior", 0, 0), UnitActionType.Pillage, null);
            case "Upgrade":
                var city = env.A.Cities[0];
                env.Game.AddTileToCity(city, env.Game.GetTile(-5, 0));
                env.Game.GetTile(-5, 0).SetOwningCity(city);
                env.A.Tech.AddTechnology("Machinery");
                env.A.Tech.AddTechnology("Construction");
                env.A.AddGold(1000);
                return (env.Unit("Archer", -5, 0), UnitActionType.Upgrade, "Composite Bowman");
            case "SetUp": return (env.Unit("Catapult", 0, 0), UnitActionType.SetUp, null);
            case "CreateImprovement":
                env.Game.GetTile(0, 0).SetOwningCity(env.A.Cities[0]);
                env.A.Tech.AddTechnology("Engineering");
                return (env.Game.AddDefaultMeleeUnitWithUniques(env.A, env.Game.GetTile(0, 0), "Can instantly construct a [Fort] improvement"),
                    UnitActionType.CreateImprovement, "Fort");
            case "GiftUnit":
                var cityState = env.Game.AddCiv(cityStateType: "Cultured");
                env.Game.AddCity(cityState, env.Game.GetTile(5, 0));
                var giftTile = env.Game.GetTile(4, 0);
                env.Game.AddTileToCity(cityState.Cities[0], giftTile);
                giftTile.SetOwningCity(cityState.Cities[0]);
                return (env.Unit("Warrior", 4, 0), UnitActionType.GiftUnit, null);
            default: throw new ArgumentException(scenario);
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ExecutingTheDescriptorEqualsInvokingTheClosure(string scenario)
    {
        var viaCommand = new Env();
        var (unit, type, key) = Build(viaCommand, scenario);
        var offered = UnitActionDescriptor.Describe(unit).Where(it => it.Descriptor.ActionType == type && it.Action.Action != null).ToList();
        Assert.True(offered.Count > 0, string.Join("; ", UnitActionDescriptor.Describe(unit).Select(it => $"{it.Descriptor} {it.Action.Action != null}")));
        var descriptor = offered[0].Descriptor;
        if (key != null) Assert.Equal(key, descriptor.Key);

        var direct = new Env();
        var (directUnit, _, _) = Build(direct, scenario);
        UnitActionDescriptor.Describe(directUnit).First(it => it.Descriptor == descriptor).Action.Action!();

        var result = viaCommand.Session.Submit(viaCommand.A.CivId,
            new ExecuteUnitAction { UnitId = unit.Id, Descriptor = descriptor, Confirmed = true });

        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(direct.Hash(), viaCommand.Hash());
    }

    [Fact]
    public void DescriptorsAndCommandsRoundTripThroughJson()
    {
        var command = new ExecuteUnitAction { UnitId = 3, Descriptor = new UnitActionDescriptor(UnitActionType.Upgrade, "Composite Bowman", 1), Confirmed = true };
        var back = JsonSerializer.Deserialize<ExecuteUnitAction>(JsonSerializer.Serialize(command, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        Assert.Equal(command.Descriptor, back.Descriptor);
        Assert.True(back.Confirmed);

        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(ExecuteUnitAction), names["execute-unit-action"]);
        Assert.Equal(typeof(SetWorkerOrder), names["set-worker-order"]);
        Assert.Equal(typeof(PromoteUnit), names["promote-unit"]);
    }

    private static void AssertRejected(Env env, GameCommand command, string code, string? actor = null)
    {
        ulong before = env.Hash();
        int logged = env.Session.Log.Count;
        var result = env.Session.Submit(actor ?? env.A.CivId, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, env.Hash());
        Assert.Equal(logged, env.Session.Log.Count);
    }

    [Fact]
    public void ExecuteRejectsStaleDisabledAndUnconfirmedActions()
    {
        var env = new Env();
        var warrior = env.Unit("Warrior", 0, 0);
        var fortify = new UnitActionDescriptor(UnitActionType.Fortify);

        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = new UnitActionDescriptor(UnitActionType.Fortify, "nope") }, CommandErrors.StaleReference);
        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = new UnitActionDescriptor(UnitActionType.Fortify, null, 5) }, CommandErrors.StaleReference);
        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = new UnitActionDescriptor(UnitActionType.Fortify, null, -1) }, CommandErrors.StaleReference);
        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = new UnitActionDescriptor(UnitActionType.Paradrop) }, CommandErrors.StaleReference);
        AssertRejected(env, new ExecuteUnitAction { UnitId = 9999, Descriptor = fortify }, CommandErrors.UnknownUnit);
        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = fortify }, CommandErrors.NotYourTurn, env.B.CivId);

        // the action list changes with the state: a fortified unit has no enabled fortify action any more
        Assert.True(env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = fortify }).Accepted);
        Assert.True(warrior.IsFortified());
        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = fortify }, CommandErrors.NotAllowed);

        // a disabled action (no movement left) is not executed
        var tired = env.Unit("Warrior", 1, 0);
        tired.CurrentMovement = 0f;
        AssertRejected(env, new ExecuteUnitAction { UnitId = tired.Id, Descriptor = new UnitActionDescriptor(UnitActionType.Skip) }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void ActionsWithAConfirmationNeedConfirmed()
    {
        var env = new Env();
        var tile = env.Game.GetTile(0, 0);
        tile.ImprovementFunctions.SetImprovement("Farm");
        var warrior = env.Unit("Warrior", 0, 0);
        var pillage = UnitActionDescriptor.Describe(warrior).First(it => it.Descriptor.ActionType == UnitActionType.Pillage);
        Assert.NotNull(pillage.Action.Confirmation);
        Assert.NotNull(pillage.Action.Action);
        AssertRejected(env, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = pillage.Descriptor }, CommandErrors.NotAllowed);
        Assert.True(env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = pillage.Descriptor, Confirmed = true }).Accepted);
    }

    [Fact]
    public void SeveralActionsOfOneTypeAreToldApartByKeyAndIndex()
    {
        var env = new Env();
        var city = env.A.Cities[0];
        env.Game.AddTileToCity(city, env.Game.GetTile(-5, 0));
        env.Game.GetTile(-5, 0).SetOwningCity(city);
        env.A.Tech.AddTechnology("Machinery");
        env.A.Tech.AddTechnology("Construction");
        env.A.AddGold(1000);
        var warrior = env.Unit("Archer", -5, 0);

        var all = UnitActionDescriptor.Describe(warrior);
        Assert.Equal(all.Count, all.Select(it => it.Descriptor).Distinct().Count());
        foreach (var (descriptor, action) in all)
            Assert.Equal(action.Type, descriptor.Resolve(warrior)!.Type);
        var upgrades = all.Where(it => it.Descriptor.ActionType == UnitActionType.Upgrade).Select(it => it.Descriptor.Key).ToList();
        Assert.Contains("Composite Bowman", upgrades);
    }

    [Fact]
    public void FoundCityIsCheckedAgainstTheCurrentBoard()
    {
        var env = new Env();
        var settler = env.Unit("Settler", 0, 0);
        var found = UnitActionDescriptor.Describe(settler).First(it => it.Descriptor.ActionType == UnitActionType.FoundCity).Descriptor;
        // someone else takes the tile first
        env.Game.AddCity(env.B, env.Game.GetTile(0, 1));

        AssertRejected(env, new ExecuteUnitAction { UnitId = settler.Id, Descriptor = found }, CommandErrors.NotAllowed);
        Assert.False(settler.IsDestroyed);
    }

    [Fact]
    public void TheQueryListsTheDescriptorsTheCommandAccepts()
    {
        var env = new Env();
        var warrior = env.Unit("Warrior", 0, 0);
        var view = new Unciv.View.GameView(env.Game.GameInfo, env.A);
        ulong before = env.Hash();

        var result = new UnitActionsQuery(warrior.Id).Run(view);

        Assert.Equal(before, env.Hash());
        Assert.Equal(warrior.Id, result.UnitId);
        var fortify = result.Actions.First(it => it.Descriptor.ActionType == UnitActionType.Fortify);
        Assert.True(fortify.IsEnabled);
        Assert.Null(fortify.Confirmation);
        Assert.True(env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = warrior.Id, Descriptor = fortify.Descriptor }).Accepted);
        Assert.Throws<QueryException>(() => new UnitActionsQuery(12345).Run(view));
    }

    // --- SetWorkerOrder

    [Fact]
    public void WorkerOrdersBuildCancelAndConnectRoad()
    {
        var env = new Env();
        var worker = env.Unit("Worker", 0, 0);
        var tile = env.Game.GetTile(0, 0);
        env.A.Tech.AddTechnology("The Wheel");
        env.A.Tech.AddTechnology("Pottery");
        env.A.Tech.AddTechnology("Masonry");
        worker.Action = "Sleep";

        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Build, Improvement = "Nonexistent" }, CommandErrors.InvalidTarget);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Build, Improvement = "Road", SecondImprovement = "Nonexistent" }, CommandErrors.InvalidTarget);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Build, Improvement = "Farm" }, CommandErrors.NotAllowed);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.ConnectRoad, To = new HexCoord(99, 99) }, CommandErrors.UnknownTile);

        var result = env.Session.Submit(env.A.CivId, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Build, Improvement = "Road" });
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal("Road", tile.ImprovementInProgress);
        Assert.Null(worker.Action); // woken up

        // the same order again does not restart
        int turns = tile.TurnsToImprovement;
        Assert.True(env.Session.Submit(env.A.CivId, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Build, Improvement = "Road" }).Accepted);
        Assert.Equal(turns, tile.TurnsToImprovement);

        Assert.True(env.Session.Submit(env.A.CivId, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Cancel }).Accepted);
        Assert.Null(tile.ImprovementInProgress);

        var connect = env.Session.Submit(env.A.CivId, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.ConnectRoad, To = new HexCoord(-3, 0) });
        Assert.True(connect.Accepted, connect.Validation.Message);
    }

    [Fact]
    public void ConnectRoadNeedsABuilderAndARoad()
    {
        var env = new Env();
        var warrior = env.Unit("Warrior", 0, 0);
        AssertRejected(env, new SetWorkerOrder { UnitId = warrior.Id, Order = WorkerOrderKind.ConnectRoad, To = new HexCoord(1, 0) }, CommandErrors.NotAllowed);
        var worker = env.Unit("Worker", 1, 0);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.Build, Improvement = Constants.CancelImprovementOrder }, CommandErrors.InvalidTarget);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.ConnectRoad, To = new HexCoord(1, 1) }, CommandErrors.NotAllowed); // no road tech
    }

    [Fact]
    public void ConnectRoadRejectsTargetsTheUiDoesNotOffer()
    {
        var env = new Env();
        env.A.Tech.AddTechnology("The Wheel");
        var worker = env.Unit("Worker", 0, 0);
        var water = env.Game.GetTile(2, 0);
        env.Game.SetTileTerrain(water.Position, Constants.Ocean);
        var hidden = env.Game.GetTile(0, 2);
        hidden.SetExplored(env.A, false);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.ConnectRoad, To = water.Position }, CommandErrors.InvalidTarget);
        AssertRejected(env, new SetWorkerOrder { UnitId = worker.Id, Order = WorkerOrderKind.ConnectRoad, To = hidden.Position }, CommandErrors.InvalidTarget);
    }

    // --- PromoteUnit

    [Fact]
    public void PromoteUnitNeedsExperienceAndAnAvailablePromotion()
    {
        var env = new Env();
        var warrior = env.Unit("Warrior", -6, 0);
        const string available = "Shock I";
        Assert.Contains(available, warrior.Promotions.GetAvailablePromotions().Select(it => it.Name));

        AssertRejected(env, new PromoteUnit { UnitId = warrior.Id, Promotion = available }, CommandErrors.NotAllowed); // no experience

        // a free promotion makes the unit promotable, but a paid one still needs the experience
        env.Game.CreateUnitPromotion(UniqueType.FreePromotion.Text()).UnitTypes.Add(warrior.BaseUnit.UnitType);
        AssertRejected(env, new PromoteUnit { UnitId = warrior.Id, Promotion = available }, CommandErrors.NotEnough("xp"));

        warrior.Promotions.XP = 1000;
        AssertRejected(env, new PromoteUnit { UnitId = warrior.Id, Promotion = "Nonexistent" }, CommandErrors.InvalidTarget);
        var result = env.Session.Submit(env.A.CivId, new PromoteUnit { UnitId = warrior.Id, Promotion = available, SaveAsDefault = true });
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Contains(available, warrior.Promotions.Promotions);
        Assert.True(env.A.Cities[0].UnitShouldUseSavedPromotion["Warrior"]);

        warrior.CurrentMovement = 0f;
        AssertRejected(env, new PromoteUnit { UnitId = warrior.Id, Promotion = "Shock II" }, CommandErrors.NotAllowed);

        var guest = env.Unit("Warrior", 3, 3);
        var foreignCity = env.Game.AddCity(env.B, env.Game.GetTile(3, 3));
        guest.Promotions.XP = 1000;
        AssertRejected(env, new PromoteUnit { UnitId = guest.Id, Promotion = available, SaveAsDefault = true }, CommandErrors.NotAllowed);
        foreignCity.IsPuppet = true;

        var outside = env.Unit("Warrior", 0, 0);
        outside.Promotions.XP = 1000;
        AssertRejected(env, new PromoteUnit { UnitId = outside.Id, Promotion = available, SaveAsDefault = true }, CommandErrors.NotAllowed);
    }
}
