using System.Text.Json;
using Unciv.Commands.Events;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Net.Visibility;

// T175 (design/SERVER.md section 2.4): TilesRevealed and StatsChanged are not engine events. The server computes them for one recipient by
// comparing that recipient's filtered state before and after a command, so fog reveals and yield changes reach the client without
// any hidden detail riding along.

/// <summary>Tiles the recipient has explored since the command began, with the foreign units it sees on them. Server-built only.</summary>
[EventType("tiles-revealed", EventAudience.Participants)]
public sealed record TilesRevealed(string CivId, IReadOnlyList<TileDto> Tiles, IReadOnlyList<UnitDto> Units) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary>The recipient's own gold or happiness changed. Server-built only, and only ever for the recipient's own civ.</summary>
[EventType("stats-changed", EventAudience.Participants)]
public sealed record StatsChanged(string CivId, int Gold, int Happiness) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary><see cref="IViewerVisibility"/> answered from the View classes of one viewer.</summary>
public sealed class GameViewVisibility(GameInfo gameInfo, CivilizationClass viewer) : IViewerVisibility
{
    private readonly GameView gameView = new(gameInfo, viewer);

    public bool CanSeeTile(HexCoord position) =>
        gameInfo.TileMap.Contains(position) && gameView.TileMapView.GetTile(position) is { } tile && tile.IsVisible();

    public bool CanSeeUnit(string civId, int unitId, HexCoord position)
    {
        if (!CanSeeTile(position)) return false;
        var unit = gameInfo.GetCivilizationOrNull(civId)?.Units.GetUnitById(unitId);
        if (unit == null) return true; // e.g. destroyed: nothing left to be invisible
        return unit.GetTile().Position == position ? unit.IsVisibleTo(viewer) : !unit.HasActiveInvisibilityUnique(viewer);
    }

    public bool KnowsCiv(string civId) => civId == viewer.CivId || viewer.Knows(civId);
}

public static class ViewDeltaBuilder
{
    /// <summary>The recipient's state before a command, to diff against after it.</summary>
    public sealed record ViewMark(HashSet<HexCoord> Explored, int Gold, int Happiness);

    public static ViewMark Capture(GameInfo gameInfo, CivilizationClass viewer)
    {
        var view = new GameView(gameInfo, viewer).CivView;
        return new ViewMark(ExploredSet(gameInfo, viewer), view.Gold, view.GetHappiness());
    }

    private static HashSet<HexCoord> ExploredSet(GameInfo gameInfo, CivilizationClass viewer) =>
        gameInfo.TileMap.TileList.Where(it => it.IsExplored(viewer)).Select(it => it.Position).ToHashSet();

    /// <summary>Events in order: reveals first (so later events can refer to new tiles), the filtered engine events, then stat changes.</summary>
    public static IReadOnlyList<EngineEvent> BuildEvents(GameInfo gameInfo, CivilizationClass viewer, ViewMark before, IEnumerable<EngineEvent> events)
    {
        var visibility = new GameViewVisibility(gameInfo, viewer);
        var result = new List<EngineEvent>();
        var revealed = RevealedSince(gameInfo, viewer, before);
        if (revealed != null) result.Add(revealed);
        foreach (var gameEvent in events) result.AddRange(EventFilter.Classify(gameEvent, viewer.CivId, visibility));
        var view = new GameView(gameInfo, viewer).CivView;
        if (view.Gold != before.Gold || view.GetHappiness() != before.Happiness)
            result.Add(new StatsChanged(viewer.CivId, view.Gold, view.GetHappiness()));
        return result;
    }

    public static ViewDelta Build(GameInfo gameInfo, CivilizationClass viewer, ViewMark before, IEnumerable<EngineEvent> events, long seq) => new()
    {
        Seq = seq,
        Events = BuildEvents(gameInfo, viewer, before, events)
            .Select(it => JsonSerializer.SerializeToElement(GameEventRegistry.ToEnvelope(it), UncivJson.DefaultOptions)).ToList()
    };

    private static TilesRevealed? RevealedSince(GameInfo gameInfo, CivilizationClass viewer, ViewMark before)
    {
        var gameView = new GameView(gameInfo, viewer);
        var tiles = new List<TileDto>();
        var units = new List<UnitDto>();
        foreach (var tile in gameInfo.TileMap.TileList)
        {
            if (before.Explored.Contains(tile.Position)) continue;
            var tileView = gameView.TileMapView.GetTile(tile);
            if (!tileView.IsExplored()) continue;
            tiles.Add(SnapshotBuilder.BuildTile(tileView, gameView));
            if (!tileView.IsVisible()) continue;
            units.AddRange(tileView.GetVisibleUnits().Select(it => SnapshotBuilder.BuildUnit(it, tileView)));
        }
        return tiles.Count == 0 ? null : new TilesRevealed(viewer.CivId, tiles, units);
    }
}
