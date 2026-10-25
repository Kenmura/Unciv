using System.Text.Json;
using Unciv.Commands;
using Unciv.Commands.Units;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Commands.Units;

// T188: movement and combat commands over a hexagonal test map, two human civs at war.
[Collection("RulesetCache")]
public sealed class MovementCombatCommandTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject a;
    private readonly CivilizationObject b;
    private readonly GameSession session;

    public MovementCombatCommandTests()
    {
        testGame.MakeHexagonalMap(6);
        a = testGame.AddCiv(isPlayer: true);
        b = testGame.AddCiv(isPlayer: true);
        a.DiplomacyFunctions.MakeCivilizationsMeet(b);
        a.GetDiplomacyManager(b)!.DeclareWar();
        testGame.GameInfo.CurrentPlayerCiv = a;
        testGame.GameInfo.CurrentPlayer = a.CivId;
        Unit("Warrior", a, -6, 0); // a civ with neither city nor unit is defeated
        session = new GameSession(testGame.GameInfo);
    }

    private TileClass Tile(int x, int y) => testGame.GetTile(x, y);

    private MapUnit Unit(string name, CivilizationObject civ, int x, int y)
    {
        var unit = testGame.AddUnit(name, civ, Tile(x, y));
        unit.CurrentMovement = unit.GetMaxMovement();
        return unit;
    }

    private static HexCoord At(TileClass tile) => tile.Position;

    private void See(CivilizationObject civ) => civ.ViewableTiles = testGame.TileMap.TileList.ToHashSet();

    private ulong Hash() => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(testGame.GameInfo, forceZip: false)));

    private void AssertRejected(GameCommand command, string code, string actor = "")
    {
        ulong before = Hash();
        int logged = session.Log.Count;
        var result = session.Submit(actor == "" ? a.CivId : actor, command);
        Assert.False(result.Accepted);
        Assert.Equal(code, result.Validation.Code);
        Assert.Equal(before, Hash());
        Assert.Equal(logged, session.Log.Count);
    }

    // --- registration and wire format

    [Fact]
    public void CommandsAreRegisteredWithKebabCaseNamesAndRoundTripThroughJson()
    {
        var names = GameCommandRegistry.Scan().ToDictionary(it => it.Name, it => it.CommandClass);
        Assert.Equal(typeof(MoveUnit), names["move-unit"]);
        Assert.Equal(typeof(SetMoveOrder), names["set-move-order"]);
        Assert.Equal(typeof(SwapUnits), names["swap-units"]);
        Assert.Equal(typeof(AttackWithUnit), names["attack-with-unit"]);
        Assert.Equal(typeof(BombardWithCity), names["bombard-with-city"]);
        Assert.Equal(typeof(Nuke), names["nuke"]);
        Assert.Equal(typeof(AirSweep), names["air-sweep"]);
        Assert.Equal(typeof(DisbandUnit), names["disband-unit"]);

        string wire = JsonSerializer.Serialize(new MoveUnit { UnitId = 4, To = new HexCoord(2, -3) }, UncivJson.DefaultOptions);
        var back = JsonSerializer.Deserialize<MoveUnit>(wire, UncivJson.DefaultOptions)!;
        Assert.Equal(4, back.UnitId);
        Assert.Equal(new HexCoord(2, -3), back.To);
    }

    // --- MoveUnit

    [Fact]
    public void MoveUnitMovesOneTurnAndClearsAMoveOrder()
    {
        var warrior = Unit("Warrior", a, -3, 0);
        warrior.Action = "moveTo 3,0";
        var target = warrior.CurrentTile.Neighbors.First(it => warrior.Movement.CanMoveTo(it));

        var result = session.Submit(a.CivId, new MoveUnit { UnitId = warrior.Id, To = At(target) });

        Assert.True(result.Accepted);
        Assert.Same(target, warrior.CurrentTile);
        Assert.Null(warrior.Action);
        Assert.Single(session.Log);
    }

    [Fact]
    public void MoveUnitRejectsWhatTheUiWouldNotOffer()
    {
        var warrior = Unit("Warrior", a, -3, 0);
        var foreign = Unit("Warrior", b, 3, 0);
        var near = warrior.CurrentTile.Neighbors.First();
        Unit("Warrior", b, near.Position.x, near.Position.y);

        AssertRejected(new MoveUnit { UnitId = warrior.Id, To = At(Tile(3, 0)) }, CommandErrors.InvalidTarget); // too far for one turn
        AssertRejected(new MoveUnit { UnitId = warrior.Id, To = At(warrior.CurrentTile) }, CommandErrors.InvalidTarget);
        AssertRejected(new MoveUnit { UnitId = warrior.Id, To = At(near) }, CommandErrors.InvalidTarget); // enemy there
        AssertRejected(new MoveUnit { UnitId = warrior.Id, To = new HexCoord(99, 99) }, CommandErrors.UnknownTile);
        AssertRejected(new MoveUnit { UnitId = foreign.Id, To = At(Tile(2, 0)) }, CommandErrors.NotYourUnit);
        AssertRejected(new MoveUnit { UnitId = -5, To = At(Tile(2, 0)) }, CommandErrors.UnknownUnit);
        AssertRejected(new MoveUnit { UnitId = warrior.Id, To = At(Tile(-2, 0)) }, CommandErrors.NotYourTurn, b.CivId);

        warrior.CurrentMovement = 0f;
        AssertRejected(new MoveUnit { UnitId = warrior.Id, To = At(Tile(-2, 0)) }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void TheSecondOfTwoMovesToTheSameTileIsRejectedByItsOwnPredicate()
    {
        var first = Unit("Warrior", a, -3, 0);
        var second = Unit("Warrior", a, -3, 1);
        var target = first.CurrentTile.Neighbors.Intersect(second.CurrentTile.Neighbors).First();

        Assert.True(session.Submit(a.CivId, new MoveUnit { UnitId = first.Id, To = At(target) }).Accepted);
        var again = session.Submit(a.CivId, new MoveUnit { UnitId = second.Id, To = At(target) });

        // Two military units of one civ cannot share a tile.
        Assert.False(again.Accepted);
        Assert.Equal(CommandErrors.InvalidTarget, again.Validation.Code);
    }

    // --- SetMoveOrder

    [Fact]
    public void SetMoveOrderHeadsTowardsNowAndStoresTheOrderUntilArrival()
    {
        var warrior = Unit("Warrior", a, -3, 0);
        var destination = Tile(3, 0);

        Assert.True(session.Submit(a.CivId, new SetMoveOrder { UnitId = warrior.Id, To = At(destination) }).Accepted);
        Assert.NotEqual(Tile(-3, 0), warrior.CurrentTile);
        Assert.Equal("moveTo 3,0", warrior.Action);

        for (int turn = 0; turn < 6 && warrior.CurrentTile != destination; turn++)
        {
            warrior.CurrentMovement = warrior.GetMaxMovement();
            warrior.DoAction();
        }
        Assert.Same(destination, warrior.CurrentTile);
    }

    [Fact]
    public void SetMoveOrderToAReachableTileLeavesNoOrderAndRejectsBadTargets()
    {
        var warrior = Unit("Warrior", a, -3, 0);
        var near = warrior.CurrentTile.Neighbors.First(it => warrior.Movement.CanMoveTo(it));
        warrior.Action = "Explore";

        Assert.True(session.Submit(a.CivId, new SetMoveOrder { UnitId = warrior.Id, To = At(near) }).Accepted);
        Assert.Same(near, warrior.CurrentTile);
        Assert.Null(warrior.Action);

        AssertRejected(new SetMoveOrder { UnitId = warrior.Id, To = At(near) }, CommandErrors.InvalidTarget);
        AssertRejected(new SetMoveOrder { UnitId = warrior.Id, To = new HexCoord(50, 50) }, CommandErrors.UnknownTile);
        warrior.CurrentMovement = 0f;
        AssertRejected(new SetMoveOrder { UnitId = warrior.Id, To = At(Tile(3, 0)) }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void SetMoveOrderClearsExploringAfterMovingAndKeepsAnActionWhenNothingMoves()
    {
        var warrior = Unit("Warrior", a, -3, 0);
        warrior.Action = "Explore";
        Assert.True(session.Submit(a.CivId, new SetMoveOrder { UnitId = warrior.Id, To = At(Tile(3, 0)) }).Accepted);
        Assert.Equal("moveTo 3,0", warrior.Action);

        var boxed = Unit("Warrior", a, 0, 4);
        foreach (var around in boxed.CurrentTile.Neighbors.ToList()) Unit("Warrior", a, around.Position.x, around.Position.y);
        boxed.Action = "Fortify";
        var origin = boxed.CurrentTile;
        session.Submit(a.CivId, new SetMoveOrder { UnitId = boxed.Id, To = At(Tile(0, -4)) });
        if (boxed.CurrentTile == origin) Assert.Equal("Fortify", boxed.Action);
    }

    [Fact]
    public void SetMoveOrderRejectsAnUnreachableDestination()
    {
        var warrior = Unit("Warrior", a, -3, 0);
        var island = Tile(3, 0);
        foreach (var around in island.Neighbors.ToList()) testGame.SetTileTerrain(around.Position, "Mountain");
        foreach (var tile in testGame.TileMap.TileList) tile.SetExplored(a, true); // unexplored tiles are assumed passable

        AssertRejected(new SetMoveOrder { UnitId = warrior.Id, To = At(island) }, CommandErrors.InvalidTarget);
    }

    // --- SwapUnits

    [Fact]
    public void SwapUnitsExchangesTwoFriendlyUnitsOfTheSameKind()
    {
        var first = Unit("Warrior", a, -3, 0);
        var neighbor = first.CurrentTile.Neighbors.First(it => it.Position.x <= 0);
        var second = Unit("Warrior", a, neighbor.Position.x, neighbor.Position.y);
        var firstHome = first.CurrentTile;

        Assert.True(session.Submit(a.CivId, new SwapUnits { UnitId = first.Id, To = At(neighbor) }).Accepted);

        Assert.Same(neighbor, first.CurrentTile);
        Assert.Same(firstHome, second.CurrentTile);
    }

    [Fact]
    public void SwapUnitsNeedsAFriendlyPartner()
    {
        var first = Unit("Warrior", a, -3, 0);
        var neighbors = first.CurrentTile.Neighbors.ToList();
        var enemy = Unit("Warrior", b, neighbors[0].Position.x, neighbors[0].Position.y);
        AssertRejected(new SwapUnits { UnitId = first.Id, To = At(neighbors[1]) }, CommandErrors.InvalidTarget); // empty
        AssertRejected(new SwapUnits { UnitId = first.Id, To = At(neighbors[0]) }, CommandErrors.InvalidTarget); // enemy
        AssertRejected(new SwapUnits { UnitId = enemy.Id, To = At(neighbors[1]) }, CommandErrors.NotYourUnit);
        AssertRejected(new SwapUnits { UnitId = first.Id, To = new HexCoord(70, 70) }, CommandErrors.UnknownTile);
    }

    // --- AttackWithUnit

    [Fact]
    public void AttackWithUnitMovesIntoPositionAndFightsTheDefenderOfTheTile()
    {
        See(a);
        var attacker = Unit("Warrior", a, 0, 0);
        var defenderTile = attacker.CurrentTile.Neighbors.First();
        var defender = Unit("Warrior", b, defenderTile.Position.x, defenderTile.Position.y);
        See(a);

        var result = session.Submit(a.CivId, new AttackWithUnit { UnitId = attacker.Id, Target = At(defenderTile) });

        Assert.True(result.Accepted, result.Validation.Message);
        Assert.Equal(1, attacker.AttacksThisTurn);
        Assert.True(defender.Health < 100 || defender.IsDestroyed);
    }

    [Fact]
    public void AttackWithUnitRejectsMissingTargetsCiviliansAndFriends()
    {
        var attacker = Unit("Warrior", a, 0, 0);
        var neighbors = attacker.CurrentTile.Neighbors.ToList();
        var enemy = Unit("Warrior", b, neighbors[0].Position.x, neighbors[0].Position.y);
        var friend = Unit("Warrior", a, neighbors[1].Position.x, neighbors[1].Position.y);
        var worker = Unit("Worker", a, neighbors[2].Position.x, neighbors[2].Position.y);
        var far = Unit("Warrior", b, 4, 0);
        See(a);

        AssertRejected(new AttackWithUnit { UnitId = attacker.Id, Target = At(neighbors[3]) }, CommandErrors.InvalidTarget); // empty
        AssertRejected(new AttackWithUnit { UnitId = attacker.Id, Target = At(friend.CurrentTile) }, CommandErrors.InvalidTarget);
        AssertRejected(new AttackWithUnit { UnitId = attacker.Id, Target = At(far.CurrentTile) }, CommandErrors.InvalidTarget);
        AssertRejected(new AttackWithUnit { UnitId = worker.Id, Target = At(enemy.CurrentTile) }, CommandErrors.NotAllowed);
        AssertRejected(new AttackWithUnit { UnitId = attacker.Id, Target = new HexCoord(80, 0) }, CommandErrors.UnknownTile);

        a.GetDiplomacyManager(b)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.Peace;
        AssertRejected(new AttackWithUnit { UnitId = attacker.Id, Target = At(enemy.CurrentTile) }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void ADeadDefenderMakesTheSecondAttackOnTheSameTileInvalid()
    {
        var first = Unit("Warrior", a, 0, 0);
        var targetTile = first.CurrentTile.Neighbors.First();
        var secondTile = targetTile.Neighbors.First(it => it != first.CurrentTile && it.GetUnits().Count() == 0);
        var second = Unit("Warrior", a, secondTile.Position.x, secondTile.Position.y);
        var defender = Unit("Warrior", b, targetTile.Position.x, targetTile.Position.y);
        defender.Health = 1;
        See(a);

        Assert.True(session.Submit(a.CivId, new AttackWithUnit { UnitId = first.Id, Target = At(targetTile) }).Accepted);
        Assert.True(defender.IsDestroyed);
        var again = session.Submit(a.CivId, new AttackWithUnit { UnitId = second.Id, Target = At(targetTile) });

        Assert.False(again.Accepted);
        Assert.Equal(CommandErrors.InvalidTarget, again.Validation.Code);
    }

    // --- BombardWithCity

    [Fact]
    public void BombardWithCityHitsAVisibleEnemyInRangeOnceAndOnlyOncePerTurn()
    {
        var city = testGame.AddCity(a, Tile(0, 0));
        var inRange = Tile(0, 2);
        var enemy = Unit("Warrior", b, inRange.Position.x, inRange.Position.y);
        var outOfRange = Unit("Warrior", b, 0, 5);
        See(a);
        Assert.True(city.CanBombard());

        AssertRejected(new BombardWithCity { CityId = city.Id, Target = At(outOfRange.CurrentTile) }, CommandErrors.InvalidTarget);
        AssertRejected(new BombardWithCity { CityId = "nope", Target = At(inRange) }, CommandErrors.UnknownCity);
        AssertRejected(new BombardWithCity { CityId = city.Id, Target = At(Tile(0, 1)) }, CommandErrors.InvalidTarget); // nobody there

        var result = session.Submit(a.CivId, new BombardWithCity { CityId = city.Id, Target = At(inRange) });

        Assert.True(result.Accepted, result.Validation.Message);
        Assert.True(enemy.Health < 100 || enemy.IsDestroyed);
        Assert.True(city.AttackedThisTurn);
        var second = session.Submit(a.CivId, new BombardWithCity { CityId = city.Id, Target = At(inRange) });
        Assert.Equal(CommandErrors.NotAllowed, second.Validation.Code);
    }

    [Fact]
    public void BombardWithCityDoesNotTargetAnEnemyCity()
    {
        var city = testGame.AddCity(a, Tile(0, 0));
        testGame.AddCity(b, Tile(0, 2));
        See(a);
        AssertRejected(new BombardWithCity { CityId = city.Id, Target = At(Tile(0, 2)) }, CommandErrors.InvalidTarget);
    }

    [Fact]
    public void BombardWithCityRejectsAForeignCity()
    {
        var theirs = testGame.AddCity(b, Tile(0, 4));
        AssertRejected(new BombardWithCity { CityId = theirs.Id, Target = At(Tile(0, 3)) }, CommandErrors.NotYourCity);
    }

    // --- Nuke

    private MapUnit NukeUnit(string name = "Missile")
    {
        testGame.AddCity(a, Tile(0, 5));
        var baseUnit = testGame.CreateBaseUnit(name, "Nuclear weapon of Strength [1]", "Self-destructs when attacking");
        baseUnit.Movement = 1;
        baseUnit.Range = 12;
        baseUnit.Strength = 100;
        baseUnit.RangedStrength = 100;
        var unit = testGame.AddUnit(baseUnit.Name, a, Tile(0, 5));
        unit.CurrentMovement = 1f;
        return unit;
    }

    [Fact]
    public void NukeLaunchesAtAnExploredTileInRange()
    {
        var nuke = NukeUnit();
        var target = Tile(0, -2);
        target.SetExplored(a, true);
        var victim = Unit("Warrior", a, 0, -2);

        Assert.True(session.Submit(a.CivId, new Nuke { UnitId = nuke.Id, Target = At(target) }).Accepted);

        Assert.True(nuke.IsDestroyed);
        Assert.True(victim.IsDestroyed);
    }

    [Fact]
    public void NukeRejectsOwnTileUnexploredTargetsAndOrdinaryUnits()
    {
        var nuke = NukeUnit();
        var warrior = Unit("Warrior", a, -3, 0);
        Tile(0, -2).SetExplored(a, false);

        AssertRejected(new Nuke { UnitId = nuke.Id, Target = At(nuke.CurrentTile) }, CommandErrors.InvalidTarget);
        AssertRejected(new Nuke { UnitId = nuke.Id, Target = At(Tile(0, -2)) }, CommandErrors.InvalidTarget);
        AssertRejected(new Nuke { UnitId = warrior.Id, Target = At(Tile(0, -2)) }, CommandErrors.NotAllowed);
        nuke.CurrentMovement = 0f;
        AssertRejected(new Nuke { UnitId = nuke.Id, Target = At(Tile(0, -2)) }, CommandErrors.NotAllowed);
    }

    [Fact]
    public void NukeRefusesToHitACivWeCannotAttack()
    {
        var nuke = NukeUnit();
        var target = Tile(0, -2);
        target.SetExplored(a, true);
        Unit("Warrior", b, 0, -2);
        var treaty = new Unciv.Logic.Trade.Trade();
        treaty.OurOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.PeaceTreaty, Unciv.Logic.Trade.TradeOfferType.Treaty, 1, 10));
        a.GetDiplomacyManager(b)!.Trades.Add(treaty);

        AssertRejected(new Nuke { UnitId = nuke.Id, Target = At(target) }, CommandErrors.InvalidTarget);
    }

    // --- AirSweep

    [Fact]
    public void AirSweepNeedsAPreparingUnitAndATargetInRange()
    {
        testGame.AddCity(a, Tile(0, 5));
        var fighter = Unit("Fighter", a, 0, 5);
        var inRange = Tile(0, 2);

        AssertRejected(new AirSweep { UnitId = fighter.Id, Target = At(inRange) }, CommandErrors.NotAllowed); // not preparing

        fighter.Action = "Air Sweep";
        AssertRejected(new AirSweep { UnitId = fighter.Id, Target = At(Tile(0, -6)) }, CommandErrors.InvalidTarget);
        AssertRejected(new AirSweep { UnitId = fighter.Id, Target = At(fighter.CurrentTile) }, CommandErrors.InvalidTarget);

        Assert.True(session.Submit(a.CivId, new AirSweep { UnitId = fighter.Id, Target = At(inRange) }).Accepted);
        Assert.Equal(1, fighter.AttacksThisTurn);
    }

    // --- DisbandUnit

    [Fact]
    public void DisbandUnitRemovesTheUnitAndRefundsGoldInsideOwnBorders()
    {
        var city = testGame.AddCity(a, Tile(0, 0));
        var inside = Unit("Warrior", a, city.GetCenterTile().Position.x, city.GetCenterTile().Position.y);
        var outside = Unit("Warrior", a, 4, 0);
        int gold = a.Gold;
        int refund = inside.BaseUnit.GetDisbandGold(a);

        Assert.True(session.Submit(a.CivId, new DisbandUnit { UnitId = inside.Id }).Accepted);
        Assert.True(inside.IsDestroyed);
        Assert.Equal(gold + refund, a.Gold);

        Assert.True(session.Submit(a.CivId, new DisbandUnit { UnitId = outside.Id }).Accepted);
        Assert.Equal(gold + refund, a.Gold);
        Assert.DoesNotContain(a.Units.GetCivUnits(), it => it.Id == outside.Id);
    }

    [Fact]
    public void DisbandUnitRejectsForeignAndExhaustedUnits()
    {
        var mine = Unit("Warrior", a, 0, 0);
        var theirs = Unit("Warrior", b, 1, 0);
        AssertRejected(new DisbandUnit { UnitId = theirs.Id }, CommandErrors.NotYourUnit);
        mine.CurrentMovement = 0f;
        AssertRejected(new DisbandUnit { UnitId = mine.Id }, CommandErrors.NotAllowed);
    }
}
