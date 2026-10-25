using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Queries.Units;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Commands;

// T190: every unique-driven action family goes through UnitActionDescriptor/ExecuteUnitAction (no per-family command and no parameter
// map: the two-step families (Paradrop, AirSweep, religion founding) only toggle the unit or the civ state here and the second step is
// MoveUnit / AirSweep / FoundReligion). As in T189 each scenario is built twice, once through the command and once by invoking the
// closure directly; both games must end in the same state.
[Collection("RulesetCache")]
public sealed class UnitActionFamiliesTests
{
    private sealed class Env
    {
        public readonly TestGame Game = new();
        public readonly CivilizationObject A;
        public readonly CivilizationObject B;
        public readonly GameSession Session;
        public readonly Unciv.Logic.City.City Capital;

        public Env()
        {
            Game.MakeHexagonalMap(6);
            A = Game.AddCiv(Game.Ruleset.Nations["Germany"], isPlayer: true);
            B = Game.AddCiv(Game.Ruleset.Nations["Egypt"], isPlayer: true);
            Game.GameInfo.CurrentPlayerCiv = A;
            Game.GameInfo.CurrentPlayer = A.CivId;
            Capital = Game.AddCity(A, Game.GetTile(-6, 0));
            Game.AddUnit("Warrior", B, Game.GetTile(6, 0)); // keeps the other civ alive
            foreach (var tile in Game.TileMap.TileList) tile.SetExplored(A, true);
            Session = new GameSession(Game.GameInfo);
        }

        public MapUnit WithUniques(int x, int y, params string[] uniques)
        {
            var unit = Game.AddDefaultMeleeUnitWithUniques(A, Game.GetTile(x, y), uniques);
            unit.CurrentMovement = unit.GetMaxMovement();
            return unit;
        }

        public MapUnit InCapital(params string[] uniques) => WithUniques(-6, 0, uniques);

        public ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(Game.GameInfo, forceZip: false)));
    }

    private const string TradeMission = "Can undertake a trade mission with City-State, giving a large sum of gold and [30] Influence";

    public static TheoryData<string> Scenarios() => new()
    {
        "HurryResearch", "HurryPolicy", "HurryWonder", "HurryBuilding", "ConductTradeMission", "FoundReligion", "EnhanceReligion",
        "SpreadReligion", "RemoveHeresy", "Paradrop", "AirSweep", "Transform", "TriggerUnique", "AddInCapital", "EscortFormation",
        "StopEscortFormation",
    };

    private static (MapUnit Unit, UnitActionType Type) Build(Env env, string scenario)
    {
        var game = env.Game;
        switch (scenario)
        {
            case "HurryResearch":
                env.A.Tech.TechsToResearch.Add(game.Ruleset.Technologies.Values.Skip(3).First().Name);
                env.A.Tech.ScienceOfLast8Turns[0] = 40;
                return (env.WithUniques(0, 0, "Can hurry technology research"), UnitActionType.HurryResearch);
            case "HurryPolicy":
                return (env.WithUniques(0, 0, "Can generate a large amount of culture"), UnitActionType.HurryPolicy);
            case "HurryWonder":
                var wonder = game.CreateWonder();
                wonder.Cost = 5000;
                env.Capital.CityConstructions.SetCurrentConstruction(wonder.Name);
                return (env.InCapital("Can speed up the construction of a wonder"), UnitActionType.HurryWonder);
            case "HurryBuilding":
                var building = game.CreateBuilding();
                building.Cost = 100;
                env.Capital.CityConstructions.SetCurrentConstruction(building.Name);
                return (env.InCapital("Can speed up construction of a building"), UnitActionType.HurryBuilding);
            case "ConductTradeMission":
                var cityState = game.AddCiv(cityStateType: "Cultured");
                var csCity = game.AddCity(cityState, game.GetTile(5, 0));
                env.A.DiplomacyFunctions.MakeCivilizationsMeet(cityState);
                var missionTile = game.GetTile(4, 0);
                var trader = env.WithUniques(4, 0, TradeMission);
                game.AddTileToCity(csCity, missionTile);
                missionTile.SetOwningCity(csCity);
                return (trader, UnitActionType.ConductTradeMission);
            case "FoundReligion":
                return (env.InCapital("May found a religion"), UnitActionType.FoundReligion);
            case "EnhanceReligion":
                var religion = game.AddReligion(env.A);
                for (var i = 0; i < 2; i++)
                {
                    game.CreateBelief(BeliefType.Follower);
                    game.CreateBelief(BeliefType.Enhancer);
                }
                SetReligionState(env.A, ReligionState.Religion);
                _ = religion;
                return (env.InCapital("May enhance a religion"), UnitActionType.EnhanceReligion);
            case "SpreadReligion":
                var spreading = game.AddReligion(env.A);
                var foreign = game.AddCiv(isPlayer: true);
                var foreignCity = game.AddCity(foreign, game.GetTile(4, 1));
                var missionary = env.WithUniques(2, 1, "Can Spread Religion"); // placed before the border reaches the tile
                game.AddTileToCity(foreignCity, game.GetTile(2, 1));
                game.GetTile(2, 1).SetOwningCity(foreignCity);
                missionary.BaseUnit.ReligiousStrength = 100;
                missionary.Religion = spreading.Name;
                return (missionary, UnitActionType.SpreadReligion);
            case "RemoveHeresy":
                var own = game.AddReligion(env.A);
                var rival = game.AddReligion(env.B);
                env.Capital.Religion.AddPressure(rival.Name, 200);
                var inquisitor = env.InCapital("Can remove other religions from cities");
                inquisitor.Religion = own.Name;
                return (inquisitor, UnitActionType.RemoveHeresy);
            case "Paradrop": return (env.WithUniques(0, 0, "May Paradrop to [Land] tiles up to [3] tiles away"), UnitActionType.Paradrop);
            case "AirSweep": return (env.InCapital("Can perform Air Sweep"), UnitActionType.AirSweep);
            case "Transform":
                var target = game.CreateBaseUnit("Sword");
                target.Movement = 2;
                target.Strength = 10;
                return (env.WithUniques(0, 0, $"Can transform to [{target.Name}]"), UnitActionType.Transform);
            case "TriggerUnique":
                return (env.WithUniques(0, 0, "Gain [100] [Gold] <by consuming this unit>"), UnitActionType.TriggerUnique);
            case "AddInCapital":
                env.Capital.IsOriginalCapital = true;
                return (env.InCapital("Can be added to [Spaceship] in the Capital"), UnitActionType.AddInCapital);
            case "EscortFormation":
                return EscortPair(env, false);
            case "StopEscortFormation":
                return EscortPair(env, true);
            default: throw new ArgumentException(scenario);
        }
    }

    private static (MapUnit, UnitActionType) EscortPair(Env env, bool formed)
    {
        var military = env.Game.AddUnit("Warrior", env.A, env.Game.GetTile(0, 0));
        env.Game.AddUnit("Worker", env.A, env.Game.GetTile(0, 0));
        military.CurrentMovement = military.GetMaxMovement();
        if (formed) military.StartEscorting();
        return (military, formed ? UnitActionType.StopEscortFormation : UnitActionType.EscortFormation);
    }

    private static void SetReligionState(CivilizationObject civ, ReligionState state) =>
        typeof(ReligionManager).GetProperty(nameof(ReligionManager.ReligionState))!
            .SetValue(civ.ReligionManager, state);

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ExecutingTheDescriptorEqualsInvokingTheClosure(string scenario)
    {
        var viaCommand = new Env();
        var (unit, type) = Build(viaCommand, scenario);
        var all = UnitActionDescriptor.Describe(unit);
        var offered = all.Where(it => it.Descriptor.ActionType == type && it.Action.Action != null).ToList();
        Assert.True(offered.Count > 0, string.Join("; ", all.Select(it => $"{it.Descriptor} {it.Action.Action != null}")));
        var descriptor = offered[0].Descriptor;
        Assert.Equal(all.Count, all.Select(it => it.Descriptor).Distinct().Count()); // the descriptors tell the actions apart

        var direct = new Env();
        var (directUnit, _) = Build(direct, scenario);
        UnitActionDescriptor.Describe(directUnit).First(it => it.Descriptor == descriptor).Action.Action!();
        ulong expected = direct.Hash();

        var result = viaCommand.Session.Submit(viaCommand.A.CivId,
            new ExecuteUnitAction { UnitId = unit.Id, Descriptor = descriptor, Confirmed = true });

        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(expected, viaCommand.Hash());
        Assert.NotEqual(0UL, expected);
    }

    [Theory]
    [InlineData("HurryResearch")]
    [InlineData("HurryPolicy")]
    [InlineData("HurryWonder")]
    [InlineData("HurryBuilding")]
    [InlineData("ConductTradeMission")]
    [InlineData("TriggerUnique")]
    [InlineData("AddInCapital")]
    [InlineData("FoundReligion")]
    [InlineData("Transform")]
    public void FamiliesTakeNoParameterAndAreNamedByTheirUnique(string scenario)
    {
        var env = new Env();
        var (unit, type) = Build(env, scenario);
        var offered = UnitActionDescriptor.Describe(unit).First(it => it.Descriptor.ActionType == type);
        Assert.Null(offered.Action.Confirmation);
        Assert.NotNull(offered.Descriptor.Key); // named by its unique, never by the title

        var result = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = offered.Descriptor });

        Assert.True(result.Accepted, result.Validation.Message);
        Assert.True(unit.IsDestroyed); // consumed (Transform: replaced by a new unit with the same id)
    }

    [Fact]
    public void ReligionFoundingStartsTheFlowThatFoundReligionFinishes()
    {
        var env = new Env();
        var (prophet, type) = Build(env, "FoundReligion");
        var descriptor = UnitActionDescriptor.Describe(prophet).First(it => it.Descriptor.ActionType == type).Descriptor;

        var result = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = prophet.Id, Descriptor = descriptor });

        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(ReligionState.FoundingReligion, env.A.ReligionManager.ReligionState);
        // the name and beliefs come with the next command, validated there: the action itself has no parameters
        Assert.Equal(env.Capital.Id, env.A.ReligionManager.FoundingCityId);
    }

    [Fact]
    public void ParadropIsTwoStepsTheActionThenAMove()
    {
        var env = new Env();
        var (unit, type) = Build(env, "Paradrop");
        var toggle = UnitActionDescriptor.Describe(unit).First(it => it.Descriptor.ActionType == type).Descriptor;
        Assert.True(env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = toggle }).Accepted);
        Assert.True(unit.IsPreparingParadrop());

        // the action now reads as current and a second execution toggles it off again
        Assert.True(UnitActionDescriptor.Describe(unit).First(it => it.Descriptor == toggle).Action.IsCurrentAction);
        var drop = env.Session.Submit(env.A.CivId, new MoveUnit { UnitId = unit.Id, To = new HexCoord(0, 2) });
        Assert.True(drop.Accepted, drop.Validation.Message);
        Assert.Equal(new HexCoord(0, 2), unit.CurrentTile.Position);
        Assert.False(unit.IsPreparingParadrop());
    }

    [Fact]
    public void ParadropIsRejectedAfterTheUnitHasMoved()
    {
        var env = new Env();
        var (unit, type) = Build(env, "Paradrop");
        var toggle = UnitActionDescriptor.Describe(unit).First(it => it.Descriptor.ActionType == type).Descriptor;
        unit.Movement.MoveToTile(env.Game.GetTile(1, 0));
        ulong before = env.Hash();

        var result = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = toggle });

        Assert.False(result.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, result.Validation.Code);
        Assert.Equal(before, env.Hash());
    }

    [Fact]
    public void CityBoundHurryActionsAreRejectedOutsideTheCity()
    {
        var env = new Env();
        var wonder = env.Game.CreateWonder();
        wonder.Cost = 5000;
        env.Capital.CityConstructions.SetCurrentConstruction(wonder.Name);
        var outside = env.WithUniques(0, 0, "Can speed up the construction of a wonder");
        var descriptor = UnitActionDescriptor.Describe(outside).Single(it => it.Descriptor.ActionType == UnitActionType.HurryWonder).Descriptor;
        ulong before = env.Hash();

        var result = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = outside.Id, Descriptor = descriptor });

        Assert.False(result.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, result.Validation.Code);
        Assert.Equal(before, env.Hash());
        Assert.False(outside.IsDestroyed);
    }

    [Fact]
    public void SeveralUniquesOfOneFamilyGetOneDescriptorEach()
    {
        var env = new Env();
        var unit = env.WithUniques(0, 0, "Gain [100] [Gold] <by consuming this unit>", "Gain [50] [Faith] <by consuming this unit>",
            "Gain [100] [Gold] <by consuming this unit>");
        var triggers = UnitActionDescriptor.Describe(unit).Where(it => it.Descriptor.ActionType == UnitActionType.TriggerUnique).ToList();
        Assert.Equal(3, triggers.Count);
        Assert.Equal(3, triggers.Select(it => it.Descriptor).Distinct().Count());
        Assert.Equal(2, triggers.Count(it => it.Descriptor.Key == triggers[0].Descriptor.Key));
        Assert.Equal([0, 1], triggers.Where(it => it.Descriptor.Key == triggers[0].Descriptor.Key).Select(it => it.Descriptor.Index).ToArray());

        // the second of the two equal gold uniques (the faith one sits between them) is addressed by its index
        int gold = env.A.Gold;
        var result = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = triggers[2].Descriptor });
        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(gold + 100, env.A.Gold);
    }

    [Fact]
    public void StockpileCostsGateTheActionAndAreSpentWhenItRuns()
    {
        var env = new Env();
        var oil = env.Game.CreateResource("Stockpiled");
        var unit = env.WithUniques(0, 0, $"Gain [100] [Gold] <costs [3] [{oil.Name}]>");
        var descriptor = UnitActionDescriptor.Describe(unit).Single(it => it.Descriptor.ActionType == UnitActionType.TriggerUnique).Descriptor;
        env.A.GainStockpiledResource(oil, 2);
        ulong before = env.Hash();

        var tooPoor = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = descriptor });

        Assert.False(tooPoor.Accepted);
        Assert.Equal(CommandErrors.NotAllowed, tooPoor.Validation.Code);
        Assert.Equal(before, env.Hash());

        env.A.GainStockpiledResource(oil, 3);
        int gold = env.A.Gold;
        var paid = env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = descriptor });
        Assert.True(paid.Accepted, paid.Validation.Message);
        Assert.Equal(2, env.A.ResourceStockpiles[oil.Name]);
        Assert.Equal(gold + 100, env.A.Gold);
    }

    [Fact]
    public void TheQueryListsEveryFamilyWithTheDescriptorTheCommandAccepts()
    {
        var env = new Env();
        var (unit, _) = Build(env, "TriggerUnique");
        var view = new Unciv.View.GameView(env.Game.GameInfo, env.A);
        var info = new UnitActionsQuery(unit.Id).Run(view).Actions.First(it => it.Descriptor.ActionType == UnitActionType.TriggerUnique);
        Assert.True(info.IsEnabled);
        Assert.True(env.Session.Submit(env.A.CivId, new ExecuteUnitAction { UnitId = unit.Id, Descriptor = info.Descriptor }).Accepted);
        Assert.True(unit.IsDestroyed);
    }
}
