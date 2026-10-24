using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.View;
using Tile = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.View;

// T103: port of tests/src/com/unciv/view/GameViewMovementHistoryTest.kt over TestGame.
[Collection("RulesetCache")]
public class GameViewMovementHistoryTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization player;
    private readonly Civilization enemy;
    private readonly GameView playerView;
    private readonly GameView spectatorView;

    public GameViewMovementHistoryTests()
    {
        testGame.MakeHexagonalMap(2);
        player = testGame.AddCiv(testGame.Ruleset.Nations["Germany"], isPlayer: true);
        enemy = testGame.AddCiv(testGame.Ruleset.Nations["Egypt"]);
        var spectator = testGame.AddCiv(testGame.Ruleset.Nations[Constants.Spectator]);
        spectator.ViewableTiles = testGame.TileMap.Values.ToHashSet();
        playerView = new GameView(testGame.GameInfo, player, spectatorMode: true);
        spectatorView = new GameView(testGame.GameInfo, spectator, spectatorMode: true);
    }

    private static HashSet<Tile> Tiles(params Tile[] tiles) => [.. tiles];

    [Fact]
    public void ForeignMovementHistoryRequiresEveryRememberedAndCurrentTileToBeVisible()
    {
        var origin = testGame.GetTile(0, 0);
        var previous = testGame.GetTile(0, 1);
        var current = testGame.GetTile(1, 1);
        var unit = testGame.AddUnit("Warrior", enemy, current);
        unit.MovementMemories =
        [
            new MapUnit.UnitMovementMemory(origin.Position, UnitMovementMemoryType.UnitMoved),
            new MapUnit.UnitMovementMemory(previous.Position, UnitMovementMemoryType.UnitMoved),
        ];
        var pathTiles = Tiles(origin, previous, current);
        foreach (var tile in pathTiles) tile.SetExplored(player, true);

        foreach (var foggedTile in pathTiles)
        {
            player.ViewableTiles = pathTiles.Where(it => it != foggedTile).ToHashSet();
            Assert.Empty(playerView.GetUnitsWithVisibleMovementHistory());
            Assert.Equal(unit.Id, Assert.Single(spectatorView.GetUnitsWithVisibleMovementHistory()).Id);
        }

        player.ViewableTiles = pathTiles;
        var visibleUnit = Assert.Single(playerView.GetUnitsWithVisibleMovementHistory());
        Assert.Equal(unit.Id, visibleUnit.Id);
        Assert.Same(playerView, visibleUnit.GameView);
        Assert.True(visibleUnit.SpectatorMode);
    }

    [Fact]
    public void InvisibleForeignMovementHistoryRequiresDetectionOnTheCurrentTile()
    {
        var origin = testGame.GetTile(0, 0);
        var current = testGame.GetTile(1, 0);
        var unit = testGame.AddDefaultMeleeUnitWithUniques(enemy, current, UniqueType.Invisible.Text());
        unit.MovementMemories = [new MapUnit.UnitMovementMemory(origin.Position, UnitMovementMemoryType.UnitMoved)];
        player.ViewableTiles = Tiles(origin, current);

        Assert.Empty(playerView.GetUnitsWithVisibleMovementHistory());
        Assert.Equal(unit.Id, Assert.Single(spectatorView.GetUnitsWithVisibleMovementHistory()).Id);

        player.ViewableInvisibleUnitsTiles = new Dictionary<Tile, IReadOnlySet<string>> { [origin] = new HashSet<string> { Constants.UppercaseAll } };
        Assert.Empty(playerView.GetUnitsWithVisibleMovementHistory());

        player.ViewableInvisibleUnitsTiles = new Dictionary<Tile, IReadOnlySet<string>> { [current] = new HashSet<string> { Constants.UppercaseAll } };
        Assert.Equal(unit.Id, Assert.Single(playerView.GetUnitsWithVisibleMovementHistory()).Id);
    }

    [Fact]
    public void OwnMovementHistoryStaysVisibleAfterItsOldTilesLeaveSight()
    {
        var origin = testGame.GetTile(0, 0);
        var current = testGame.GetTile(1, 0);
        var unit = testGame.AddUnit("Warrior", player, current);
        unit.MovementMemories = [new MapUnit.UnitMovementMemory(origin.Position, UnitMovementMemoryType.UnitMoved)];
        player.ViewableTiles = Tiles(current);

        Assert.Equal(unit.Id, Assert.Single(playerView.GetUnitsWithVisibleMovementHistory()).Id);
    }

    [Fact]
    public void ForeignLiveAndStoredAttacksAreVisibleWhenEitherEndpointIsInSight()
    {
        var unitSource = testGame.GetTile(0, 0);
        var unitTarget = testGame.GetTile(1, 0);
        var storedSource = testGame.GetTile(0, 1);
        var storedTarget = testGame.GetTile(1, 1);
        var unit = testGame.AddUnit("Warrior", enemy, unitSource);
        unit.AttacksSinceTurnStart.Add(unitTarget.Position);
        enemy.AttacksSinceTurnStart.Add(new Civilization.HistoricalAttackMemory(null, storedSource.Position, storedTarget.Position));
        var liveAttack = (unitSource.Position, unitTarget.Position);
        var storedAttack = (storedSource.Position, storedTarget.Position);
        foreach (var tile in new[] { unitSource, unitTarget, storedSource, storedTarget }) tile.SetExplored(player, true);

        player.ViewableTiles = new HashSet<Tile>();
        Assert.Empty(playerView.GetVisibleAttacks());
        Assert.Equal([liveAttack, storedAttack], spectatorView.GetVisibleAttacks().ToHashSet());

        foreach (var visibleTile in new[] { unitSource, unitTarget })
        {
            player.ViewableTiles = Tiles(visibleTile);
            Assert.Equal([liveAttack], playerView.GetVisibleAttacks().ToList());
        }
        foreach (var visibleTile in new[] { storedSource, storedTarget })
        {
            player.ViewableTiles = Tiles(visibleTile);
            Assert.Equal([storedAttack], playerView.GetVisibleAttacks().ToList());
        }
    }

    [Fact]
    public void OwnLiveAndStoredAttacksStayVisibleWhenNeitherEndpointIsInSight()
    {
        var unitSource = testGame.GetTile(0, 0);
        var unitTarget = testGame.GetTile(1, 0);
        var storedSource = testGame.GetTile(0, 1);
        var storedTarget = testGame.GetTile(1, 1);
        var unit = testGame.AddUnit("Warrior", player, unitSource);
        unit.AttacksSinceTurnStart.Add(unitTarget.Position);
        player.AttacksSinceTurnStart.Add(new Civilization.HistoricalAttackMemory("Archer", storedSource.Position, storedTarget.Position));
        player.ViewableTiles = new HashSet<Tile>();

        Assert.Equal(
            [(unitSource.Position, unitTarget.Position), (storedSource.Position, storedTarget.Position)],
            playerView.GetVisibleAttacks().ToHashSet());
    }
}
