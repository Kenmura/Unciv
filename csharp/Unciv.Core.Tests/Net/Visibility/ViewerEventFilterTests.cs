using Unciv.Commands.Events;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Net.Visibility;

namespace Unciv.Core.Tests.Net.Visibility;

// C037: the single-player host forwards only what the viewer may see (privacy of IGameSession.EventsReceived / SubmitResult.Events / TurnOutcome.Events).
[Collection("RulesetCache")]
public sealed class ViewerEventFilterTests
{
    [Fact]
    public void AnotherCivsFoggedMoveAndPrivateEvent_NeverReachTheViewer_OwnEventsDo()
    {
        var session = ScenarioGame.Create(solo: true);
        var game = session.Game;
        var snapshot = SnapshotBuilder.Build(game, game.GetCivilization(ScenarioGame.HumanA), 0, new TurnStatusDto { Ended = [], Phase = "Playing" });
        var seen = snapshot.Tiles.Where(it => it.Visible).Select(it => new HexCoord(it.X, it.Y)).ToHashSet();
        var fogged = game.TileMap.TileList.Select(it => it.Position).First(it => !seen.Contains(it));
        var foggedNeighbour = game.TileMap.TileList.Select(it => it.Position).Last(it => !seen.Contains(it));

        var filter = new ViewerEventFilter(game, ScenarioGame.HumanA);
        var mark = filter.Mark();
        var visible = filter.Filter(mark,
        [
            new UnitMoved(ScenarioGame.Ai, 99, "Warrior", [fogged, foggedNeighbour]),
            new ResearchCompleted(ScenarioGame.Ai, "Pottery"),
            new ResearchCompleted(ScenarioGame.HumanA, "Pottery"),
        ]);

        Assert.DoesNotContain(visible, it => it is UnitMoved or UnitAppeared or UnitDisappeared);
        Assert.DoesNotContain(visible, it => it is ResearchCompleted { CivId: ScenarioGame.Ai });
        Assert.Contains(visible, it => it is ResearchCompleted { CivId: ScenarioGame.HumanA });
    }

    [Fact]
    public void Snapshot_ContainsNoFoggedOrForeignHiddenUnits()
    {
        var session = ScenarioGame.Create(solo: true);
        var game = session.Game;
        var snapshot = SnapshotBuilder.Build(game, game.GetCivilization(ScenarioGame.HumanA), 0, new TurnStatusDto { Ended = [], Phase = "Playing" });
        var seen = snapshot.Tiles.Where(it => it.Visible).Select(it => (it.X, it.Y)).ToHashSet();
        Assert.All(snapshot.Units.Where(it => it.Own == null), unit => Assert.Contains((unit.X, unit.Y), seen));
        Assert.True(snapshot.Tiles.Count < game.TileMap.TileList.Count);
    }
}
