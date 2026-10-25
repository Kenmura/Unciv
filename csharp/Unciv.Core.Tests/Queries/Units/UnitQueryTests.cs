using System.Text.Json;
using Unciv.Core.Tests.Support;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Battle;
using Unciv.Logic.Civilization;
using Unciv.Logic.Files;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Queries;
using Unciv.Queries.Units;
using Unciv.Utils;
using BattleObject = Unciv.Logic.Battle.Battle;

namespace Unciv.Core.Tests.Queries.Units;

// T195: unit movement and combat previews. Every preview is compared with what the engine does when the previewed move or attack is executed.
[Collection("RulesetCache")]
public class UnitQueryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly QueryRegistry registry = new(UncivJson.DefaultOptions);

    public UnitQueryTests()
    {
        testGame.MakeHexagonalMap(6);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        registry.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        ExploreAll(player);
        ExploreAll(enemy);
    }

    private void ExploreAll(Civilization civ)
    {
        foreach (var tile in testGame.TileMap.TileList) tile.SetExplored(civ, true);
    }

    private void War()
    {
        player.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        player.GetDiplomacyManager(enemy)!.DeclareWar();
    }

    private QueryResponse Execute(Civilization viewer, IQuery query)
    {
        var envelope = registry.ToEnvelope(viewer.CivName, query);
        var wire = JsonSerializer.Deserialize<QueryEnvelope>(JsonSerializer.Serialize(envelope, UncivJson.DefaultOptions), UncivJson.DefaultOptions)!;
        return registry.Execute(wire, new QueryContext(testGame.GameInfo));
    }

    private T Ask<T>(Civilization viewer, IQuery<T> query) where T : notnull
    {
        var response = Execute(viewer, query);
        Assert.True(response.Ok, $"{response.Code}: {response.Message}");
        return response.Result!.Value.Deserialize<T>(UncivJson.DefaultOptions)!;
    }

    private static ulong Hash(GameInfo game) => StateHasher.ComputeHash(h => h.Add(UncivFiles.GameInfoToString(game, forceZip: false)));

    private static void Refill(MapUnit unit) => unit.CurrentMovement = unit.GetMaxMovement();

    // ---- reachable tiles

    [Fact]
    public void ReachableTilesMatchTheMovementActuallySpent()
    {
        testGame.SetTileTerrain(new HexCoord(1, 0), "Hill");
        testGame.SetTileTerrain(new HexCoord(0, 1), "Forest");
        var start = testGame.GetTile(0, 0);
        var unit = testGame.AddUnit("Warrior", player, start);

        var result = Ask(player, new ReachableTilesQuery(unit.Id));
        Assert.Equal(new TilePos(0, 0), result.From);
        Assert.NotEmpty(result.Tiles);
        Assert.Equal(result.Tiles.OrderBy(it => it.X).ThenBy(it => it.Y).ToList(), result.Tiles);
        Assert.Contains(result.Tiles, it => it.Cost > 1f); // hills and forest cost 2

        foreach (var entry in result.Tiles.Where(it => it.CanEnter))
        {
            unit.Movement.MoveToTile(testGame.GetTile(entry.X, entry.Y));
            Assert.Equal(entry.MovementLeft, unit.CurrentMovement, 0.001f);
            Assert.Equal(testGame.GetTile(entry.X, entry.Y), unit.GetTile());
            unit.RemoveFromTile();
            unit.PutInTile(start);
            Refill(unit);
        }
    }

    [Fact]
    public void ReachableTilesOfAnUnmovableOrExhaustedUnitIsJustItsTile()
    {
        var unit = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        unit.CurrentMovement = 0f;
        var result = Ask(player, new ReachableTilesQuery(unit.Id));
        Assert.Equal([new ReachableTile(0, 0, 0f, 0f, CanEnter: false)], result.Tiles);
    }

    [Fact]
    public void ReachableTilesNeverListUnexploredTiles()
    {
        var fog = new TestGame();
        fog.MakeHexagonalMap(6);
        var civ = fog.AddCiv(fog.Ruleset.Nations["Germany"], isPlayer: true);
        foreach (var tile in fog.TileMap.TileList.Where(it => it.AerialDistanceTo(fog.GetTile(0, 0)) <= 1)) tile.SetExplored(civ, true);
        var unit = fog.AddUnit("Scout", civ, fog.GetTile(0, 0));
        foreach (var tile in fog.TileMap.TileList.Where(it => it.AerialDistanceTo(fog.GetTile(0, 0)) > 1)) tile.SetExplored(civ, false); // the unit explored its surroundings
        // the engine itself reaches the unexplored ring at a guessed cost
        Assert.Contains(unit.Movement.GetDistanceToTiles().Keys, it => !it.IsExplored(civ));

        var local = new QueryRegistry(UncivJson.DefaultOptions);
        local.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        var response = local.Execute(local.ToEnvelope(civ.CivName, new ReachableTilesQuery(unit.Id)), new QueryContext(fog.GameInfo));
        var result = response.Result!.Value.Deserialize<ReachableTilesResult>(UncivJson.DefaultOptions)!;
        Assert.NotEmpty(result.Tiles);
        Assert.All(result.Tiles, it => Assert.True(fog.GetTile(it.X, it.Y).IsExplored(civ)));
    }

    [Fact]
    public void ForeignAndUnknownUnitsAreAnsweredAlike()
    {
        var mine = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var theirs = testGame.AddUnit("Warrior", enemy, testGame.GetTile(3, 3));
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(enemy, new ReachableTilesQuery(mine.Id)).Code);
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(player, new ReachableTilesQuery(theirs.Id)).Code);
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(player, new ReachableTilesQuery(99999)).Code);
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(player, new PathPreviewQuery(theirs.Id, 0, 0)).Code);
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(player, new AttackableTargetsQuery(UnitId: theirs.Id)).Code);
        Assert.Equal(UnitQueryCodes.UnknownUnit, Execute(player, new AttackPreviewQuery(0, 0, UnitId: theirs.Id)).Code);
    }

    // ---- path preview

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PathPreviewTurnsEqualTheTurnsAMoveTakes(bool useAStar)
    {
        testGame.GameInfo.ClientState.UseAStarPathfinding = useAStar;
        testGame.SetTileTerrain(new HexCoord(0, 2), "Hill");
        testGame.SetTileTerrain(new HexCoord(0, 3), "Hill");
        var unit = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var target = testGame.GetTile(0, 5);

        var hash = Hash(testGame.GameInfo);
        var preview = Ask(player, new PathPreviewQuery(unit.Id, 0, 5));
        Assert.Equal(hash, Hash(testGame.GameInfo));
        Assert.True(preview.Reachable);
        Assert.True(preview.CanEnter);
        Assert.True(preview.Turns >= 2);
        Assert.Equal(preview.Turns, preview.Legs.Count);
        Assert.Equal(Enumerable.Range(1, preview.Turns), preview.Legs.Select(it => it.Turn));
        Assert.Equal(new TilePos(0, 5), preview.Legs[^1].Tiles[^1]);

        var ends = new List<TilePos>();
        for (int turn = 0; turn < 20 && unit.GetTile() != target; turn++)
        {
            Refill(unit);
            unit.Movement.HeadTowards(target);
            ends.Add(new TilePos(unit.GetTile().Position.X, unit.GetTile().Position.Y));
        }
        Assert.Equal(target, unit.GetTile());
        Assert.Equal(preview.Legs.Select(it => it.Tiles[^1]), ends);
        Assert.Equal(preview.FirstStep, ends[0]);
    }

    [Fact]
    public void PathPreviewToTheOwnTileAndToAnUnreachableTile()
    {
        var unit = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var here = Ask(player, new PathPreviewQuery(unit.Id, 0, 0));
        Assert.True(here.Reachable);
        Assert.Equal(0, here.Turns);

        foreach (var neighbor in testGame.GetTile(0, 5).Neighbors) testGame.SetTileTerrain(neighbor.Position, "Mountain");
        var blocked = Ask(player, new PathPreviewQuery(unit.Id, 0, 5));
        Assert.False(blocked.Reachable);
        Assert.Empty(blocked.Legs);
        Assert.Null(blocked.FirstStep);
    }

    [Fact]
    public void PathPreviewRefusesUnexploredTargets()
    {
        var fog = new TestGame();
        fog.MakeHexagonalMap(6);
        var civ = fog.AddCiv(fog.Ruleset.Nations["Germany"], isPlayer: true);
        fog.GetTile(0, 0).SetExplored(civ, true);
        var unit = fog.AddUnit("Warrior", civ, fog.GetTile(0, 0));
        var local = new QueryRegistry(UncivJson.DefaultOptions);
        local.RegisterFromAssembly(typeof(QueryRegistry).Assembly);
        var response = local.Execute(local.ToEnvelope(civ.CivName, new PathPreviewQuery(unit.Id, 0, 5)), new QueryContext(fog.GameInfo));
        Assert.Equal(UnitQueryCodes.UnknownTile, response.Code);
    }

    [Fact]
    public void AirUnitPathIsOneHop()
    {
        testGame.AddCity(player, testGame.GetTile(0, 0));
        testGame.AddCity(player, testGame.GetTile(0, 2));
        var unit = testGame.AddUnit("Fighter", player, testGame.GetTile(0, 0));
        var preview = Ask(player, new PathPreviewQuery(unit.Id, 0, 2));
        Assert.Equal(unit.Movement.CanReach(testGame.GetTile(0, 2)), preview.Reachable);
        if (preview.Reachable) Assert.Equal(1, preview.Turns);
    }

    // ---- attackable targets

    private static List<(int, int, int, int)> Engine(IEnumerable<AttackableTile> tiles) =>
        tiles.Select(it => (it.TileToAttack.Position.X, it.TileToAttack.Position.Y, it.TileToAttackFrom.Position.X, it.TileToAttackFrom.Position.Y))
            .OrderBy(it => it).ToList();

    [Fact]
    public void AttackableTargetsEqualTheEngineList()
    {
        War();
        var warrior = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        testGame.AddUnit("Worker", enemy, testGame.GetTile(0, 2));
        var result = Ask(player, new AttackableTargetsQuery(UnitId: warrior.Id));
        Assert.Equal("attack", result.Kind);
        Assert.True(result.CanAttack);

        var flattened = result.Targets.SelectMany(t => t.AttackFrom.Select(f => (t.X, t.Y, f.X, f.Y))).OrderBy(it => it).ToList();
        var expected = Engine(TargetHelper.GetAttackableEnemies(warrior, warrior.Movement.GetDistanceToTiles()));
        Assert.NotEmpty(expected);
        Assert.Equal(expected, flattened);
        Assert.All(result.Targets, it => Assert.Equal(enemy.CivName, it.Civ));
        Assert.Contains(result.Targets, it => it.Name == "Worker");
        foreach (var origin in result.Targets.SelectMany(it => it.AttackFrom))
            Assert.Equal(warrior.CurrentMovement - warrior.Movement.GetDistanceToTiles()[testGame.GetTile(origin.X, origin.Y)].TotalMovement, origin.MovementLeft, 0.001f);
    }

    [Fact]
    public void RangedTargetsAreAttackedFromWhereTheEngineSays()
    {
        War();
        var archer = testGame.AddUnit("Archer", player, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        var result = Ask(player, new AttackableTargetsQuery(UnitId: archer.Id));
        var expected = Engine(TargetHelper.GetAttackableEnemies(archer, archer.Movement.GetDistanceToTiles()));
        Assert.Equal(expected, result.Targets.SelectMany(t => t.AttackFrom.Select(f => (t.X, t.Y, f.X, f.Y))).OrderBy(it => it).ToList());
    }

    [Fact]
    public void UnseenEnemiesAreNeitherListedNorPreviewed()
    {
        War();
        var archer = testGame.AddUnit("Archer", player, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(5, 0)); // explored, out of sight
        Assert.Empty(Ask(player, new AttackableTargetsQuery(UnitId: archer.Id)).Targets);
        Assert.Equal(UnitQueryCodes.NoTarget, Execute(player, new AttackPreviewQuery(5, 0, UnitId: archer.Id)).Code);
    }

    [Fact]
    public void AHiddenSubmarineIsNeitherListedNorPreviewedButAVisibleShipIs()
    {
        War();
        testGame.SetTileTerrain(new HexCoord(1, 0), "Coast");
        testGame.SetTileTerrain(new HexCoord(2, 0), "Coast");
        testGame.SetTileTerrain(new HexCoord(2, 1), "Coast");
        var archer = testGame.AddUnit("Archer", player, testGame.GetTile(0, 0));
        var submarine = testGame.AddUnit("Submarine", enemy, testGame.GetTile(2, 0));
        Assert.False(submarine.IsVisibleTo(player));
        var hidden = Ask(player, new AttackableTargetsQuery(UnitId: archer.Id));
        Assert.DoesNotContain(hidden.Targets, it => it is { X: 2, Y: 0 });
        var response = Execute(player, new AttackPreviewQuery(2, 0, UnitId: archer.Id));
        Assert.Equal(UnitQueryCodes.NoTarget, response.Code);
        Assert.DoesNotContain("Submarine", response.Message ?? "");

        testGame.AddUnit("Trireme", enemy, testGame.GetTile(2, 1));
        var shown = Ask(player, new AttackableTargetsQuery(UnitId: archer.Id));
        Assert.Contains(shown.Targets, it => it is { X: 2, Y: 1, Name: "Trireme" });
        Assert.DoesNotContain(shown.Targets, it => it.Name == "Submarine");
    }

    [Fact]
    public void CityBombardTargetsEqualTheEngineList()
    {
        War();
        var city = testGame.AddCity(player, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        var result = Ask(player, new AttackableTargetsQuery(CityId: city.Id));
        Assert.Equal("bombard", result.Kind);
        var expected = TargetHelper.GetBombardableTiles(city).Select(it => (it.Position.X, it.Position.Y)).OrderBy(it => it).ToList();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, result.Targets.Select(it => (it.X, it.Y)).ToList());
        Assert.Equal(new AttackOrigin(0, 0, 0f), result.Targets[0].AttackFrom.Single());
    }

    [Fact]
    public void ExactlyOneAttackerIsRequired()
    {
        Assert.Equal(UnitQueryCodes.BadRequest, Execute(player, new AttackableTargetsQuery()).Code);
        Assert.Equal(UnitQueryCodes.BadRequest, Execute(player, new AttackableTargetsQuery(1, "x")).Code);
        Assert.Equal(UnitQueryCodes.BadRequest, Execute(player, new AttackPreviewQuery(0, 0)).Code);
        var unit = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        Assert.Equal(UnitQueryCodes.BadRequest, Execute(player, new AttackPreviewQuery(1, 0, UnitId: unit.Id, FromX: 0)).Code);
    }

    // ---- attack preview

    [Fact]
    public void MeleeAttackPreviewBracketsTheRealBattle()
    {
        War();
        var attacker = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var defender = testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        var preview = Ask(player, new AttackPreviewQuery(1, 0, UnitId: attacker.Id));
        Assert.Equal("battle", preview.Kind);
        Assert.True(preview.CanAttack);
        Assert.Equal("damage", preview.Outcome);
        Assert.Equal(new TilePos(0, 0), preview.AttackFrom);
        Assert.Equal(defender.Name, preview.Defender!.Name);
        Assert.Equal(enemy.CivName, preview.Defender.Civ);
        Assert.True(preview.DamageToDefenderMin <= preview.DamageToDefenderMax);
        Assert.True(preview.DamageToAttackerMin <= preview.DamageToAttackerMax);
        Assert.True(preview.DamageToDefenderMax > 0 && preview.DamageToAttackerMax > 0);

        var attackerCombatant = new MapUnitCombatant(attacker);
        var defenderCombatant = new MapUnitCombatant(defender);
        Assert.Equal(preview.Attacker!.FinalStrength, (int)System.Math.Floor(BattleDamage.GetAttackingStrength(attackerCombatant, defenderCombatant, attacker.GetTile()) + 0.5f));
        Assert.Equal(preview.DamageToDefenderMax, BattleDamage.CalculateDamageToDefender(attackerCombatant, defenderCombatant, attacker.GetTile(), 1f));
        Assert.Equal(preview.DamageToDefenderMin, BattleDamage.CalculateDamageToDefender(attackerCombatant, defenderCombatant, attacker.GetTile(), 0f));

        for (int round = 0; round < 12; round++)
        {
            attacker.Health = 100;
            defender.Health = 100;
            Refill(attacker);
            BattleObject.Attack(attackerCombatant, defenderCombatant);
            int toDefender = 100 - defender.Health;
            int toAttacker = 100 - attacker.Health;
            Assert.InRange(toDefender, preview.DamageToDefenderMin, preview.DamageToDefenderMax);
            Assert.InRange(toAttacker, preview.DamageToAttackerMin, preview.DamageToAttackerMax);
        }
    }

    [Fact]
    public void RangedAttackPreviewShowsNoRetaliation()
    {
        War();
        var archer = testGame.AddUnit("Archer", player, testGame.GetTile(0, 0));
        var defender = testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        var preview = Ask(player, new AttackPreviewQuery(2, 0, UnitId: archer.Id));
        Assert.True(preview.CanAttack);
        Assert.Equal(new TilePos(0, 0), preview.AttackFrom);
        Assert.Equal(0, preview.DamageToAttackerMax);
        for (int round = 0; round < 8; round++)
        {
            defender.Health = 100;
            archer.Health = 100;
            Refill(archer);
            archer.AttacksThisTurn = 0;
            BattleObject.Attack(new MapUnitCombatant(archer), new MapUnitCombatant(defender));
            Assert.InRange(100 - defender.Health, preview.DamageToDefenderMin, preview.DamageToDefenderMax);
            Assert.Equal(100, archer.Health);
        }
    }

    [Fact]
    public void ModifiersAreListedWithTheirKeys()
    {
        War();
        testGame.SetTileTerrain(new HexCoord(1, 0), "Hill");
        var attacker = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var defender = testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        var preview = Ask(player, new AttackPreviewQuery(1, 0, UnitId: attacker.Id));
        var engine = BattleDamage.GetDefenceModifiers(new MapUnitCombatant(attacker), new MapUnitCombatant(defender), attacker.GetTile());
        Assert.NotEmpty(preview.Defender!.Modifiers);
        Assert.Equal(engine.Select(it => new StrengthModifier(it.Key, it.Value)), preview.Defender.Modifiers);
        Assert.Contains(preview.Defender.Modifiers, it => it.Percent > 0);
        Assert.True(preview.Defender.FinalStrength > preview.Defender.BaseStrength);
    }

    [Fact]
    public void CapturingACivilianIsPreviewedAndHappens()
    {
        War();
        var attacker = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var worker = testGame.AddUnit("Worker", enemy, testGame.GetTile(1, 0));
        var preview = Ask(player, new AttackPreviewQuery(1, 0, UnitId: attacker.Id));
        Assert.Equal("capture", preview.Outcome);
        Assert.Equal(0, preview.DamageToDefenderMax);
        BattleObject.Attack(new MapUnitCombatant(attacker), new MapUnitCombatant(worker));
        Assert.Contains(player.Units.GetCivUnits(), it => it.Name == "Worker");
    }

    [Fact]
    public void ExplicitAttackOriginIsCheckedAgainstTheEngineList()
    {
        War();
        var attacker = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        var options = Ask(player, new AttackableTargetsQuery(UnitId: attacker.Id)).Targets.Single().AttackFrom;
        var origin = options[0];
        var ok = Ask(player, new AttackPreviewQuery(2, 0, UnitId: attacker.Id, FromX: origin.X, FromY: origin.Y));
        Assert.Equal(new TilePos(origin.X, origin.Y), ok.AttackFrom);
        Assert.Equal(UnitQueryCodes.InvalidAttackFrom, Execute(player, new AttackPreviewQuery(2, 0, UnitId: attacker.Id, FromX: -5, FromY: 3)).Code);
    }

    [Fact]
    public void CityBombardPreviewMatchesTheEngine()
    {
        War();
        var city = testGame.AddCity(player, testGame.GetTile(0, 0));
        var defender = testGame.AddUnit("Warrior", enemy, testGame.GetTile(1, 0));
        var preview = Ask(player, new AttackPreviewQuery(1, 0, CityId: city.Id));
        Assert.True(preview.CanAttack);
        var cityCombatant = new CityCombatant(city);
        var defenderCombatant = new MapUnitCombatant(defender);
        Assert.Equal(BattleDamage.CalculateDamageToDefender(cityCombatant, defenderCombatant, city.GetCenterTile(), 1f), preview.DamageToDefenderMax);
        Assert.Equal(BattleDamage.CalculateDamageToDefender(cityCombatant, defenderCombatant, city.GetCenterTile(), 0f), preview.DamageToDefenderMin);
        Assert.True(preview.Attacker!.IsCity);
    }

    [Fact]
    public void FriendlyTilesAndEmptyTilesAreNoTargets()
    {
        War();
        var attacker = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        testGame.AddUnit("Warrior", player, testGame.GetTile(1, 0));
        Assert.Equal(UnitQueryCodes.NoTarget, Execute(player, new AttackPreviewQuery(1, 0, UnitId: attacker.Id)).Code);
        Assert.Equal(UnitQueryCodes.NoTarget, Execute(player, new AttackPreviewQuery(0, 1, UnitId: attacker.Id)).Code);
    }

    [Fact]
    public void NukePreviewFollowsMayUseNuke()
    {
        testGame.AddCity(player, testGame.GetTile(0, 0));
        var nuke = testGame.AddUnit("Atomic Bomb", player, testGame.GetTile(0, 0));
        var target = testGame.GetTile(2, 0);
        var clear = Ask(player, new AttackPreviewQuery(2, 0, UnitId: nuke.Id));
        Assert.Equal("nuke", clear.Kind);
        Assert.Equal(nuke.GetNukeBlastRadius(), clear.BlastRadius);
        Assert.Equal(Nuke.MayUseNuke(new MapUnitCombatant(nuke), target), clear.CanAttack);
        Assert.True(clear.CanAttack);

        // a civ we may not attack inside the blast radius forbids it
        player.DiplomacyFunctions.MakeCivilizationsMeet(enemy);
        var treaty = new Unciv.Logic.Trade.Trade();
        treaty.OurOffers.Add(new Unciv.Logic.Trade.TradeOffer(Constants.PeaceTreaty, Unciv.Logic.Trade.TradeOfferType.Treaty, duration: 10));
        player.GetDiplomacyManager(enemy)!.Trades.Add(treaty);
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(3, 0));
        var forbidden = Ask(player, new AttackPreviewQuery(2, 0, UnitId: nuke.Id));
        Assert.False(forbidden.CanAttack);
        Assert.Equal(Nuke.MayUseNuke(new MapUnitCombatant(nuke), target), forbidden.CanAttack);
        Assert.Equal("nuke", Ask(player, new AttackableTargetsQuery(UnitId: nuke.Id)).Kind);
    }

    // ---- purity

    [Fact]
    public void NoUnitQueryChangesTheState()
    {
        War();
        testGame.SetTileTerrain(new HexCoord(1, 1), "Hill");
        var warrior = testGame.AddUnit("Warrior", player, testGame.GetTile(0, 0));
        var archer = testGame.AddUnit("Archer", player, testGame.GetTile(0, -1));
        testGame.AddUnit("Warrior", enemy, testGame.GetTile(2, 0));
        testGame.AddUnit("Worker", enemy, testGame.GetTile(0, 2));
        var city = testGame.AddCity(player, testGame.GetTile(-2, 0));
        IQuery[] queries =
        [
            new ReachableTilesQuery(warrior.Id), new PathPreviewQuery(warrior.Id, 0, 5), new PathPreviewQuery(warrior.Id, 2, 0),
            new AttackableTargetsQuery(UnitId: warrior.Id), new AttackableTargetsQuery(UnitId: archer.Id), new AttackableTargetsQuery(CityId: city.Id),
            new AttackPreviewQuery(2, 0, UnitId: warrior.Id), new AttackPreviewQuery(2, 0, UnitId: archer.Id), new AttackPreviewQuery(0, 2, UnitId: warrior.Id),
        ];
        foreach (var useAStar in new[] { false, true })
        {
            testGame.GameInfo.ClientState.UseAStarPathfinding = useAStar;
            foreach (var query in queries)
            {
                var before = Hash(testGame.GameInfo);
                Execute(player, query);
                Assert.Equal(before, Hash(testGame.GameInfo));
                var again = Execute(player, query);
                Assert.Equal(Execute(player, query).Result?.GetRawText(), again.Result?.GetRawText());
            }
        }
    }
}
