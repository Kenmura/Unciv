using System.Text.Json;
using Unciv.Commands.Events;
using Unciv.Json;
using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using Unciv.View;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Net.Visibility;

// T175 (design/SERVER.md section 2.4): TilesRevealed and StatsChanged are not engine events. The server computes them for one recipient by
// comparing that recipient's filtered state before and after a command, so fog reveals and yield changes reach the client without
// any hidden detail riding along.

/// <summary>
/// Tiles the recipient has explored (or moved from fog to visible) since the command began, with the foreign units it sees on them,
/// and the civs it newly met or whose units it newly sighted (T214). Server-built only.
/// </summary>
[EventType("tiles-revealed", EventAudience.Participants)]
public sealed record TilesRevealed(string CivId, IReadOnlyList<TileDto> Tiles, IReadOnlyList<UnitDto> Units, IReadOnlyList<CivDto>? Civs = null) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary>
/// The recipient's own gold, happiness or per-turn yields changed (T214: per-turn gold, science, culture, faith, truncated like Kotlin Float to Int).
/// Server-built only, and only ever for the recipient's own civ.
/// </summary>
[EventType("stats-changed", EventAudience.Participants)]
public sealed record StatsChanged(string CivId, int Gold, int Happiness, int GoldPerTurn = 0, int SciencePerTurn = 0, int CulturePerTurn = 0, int FaithPerTurn = 0) : EngineEvent
{
    public override IEnumerable<string> Participants() => [CivId];
}

/// <summary><see cref="IViewerVisibility"/> answered from the View classes of one viewer.</summary>
public sealed class GameViewVisibility(GameInfo gameInfo, CivilizationClass viewer, IReadOnlySet<(string CivId, int UnitId)>? hiddenBefore = null) : IViewerVisibility
{
    private readonly IReadOnlySet<(string CivId, int UnitId)> hiddenBefore = hiddenBefore ?? new HashSet<(string, int)>();

    private readonly GameView gameView = new(gameInfo, viewer);

    public bool CanSeeTile(HexCoord position) =>
        gameInfo.TileMap.Contains(position) && gameView.TileMapView.GetTile(position) is { } tile && tile.IsVisible();

    public bool CanSeeUnit(string civId, int unitId, HexCoord position)
    {
        if (!CanSeeTile(position)) return false;
        var unit = gameInfo.GetCivilizationOrNull(civId)?.Units.GetUnitById(unitId);
        // A unit that was invisible to the viewer before the command stays unreported even when it moved or died meanwhile.
        if (hiddenBefore.Contains((civId, unitId))) return false;
        if (unit == null) return true; // destroyed and not hidden before: nothing left to ask
        return unit.GetTile().Position == position ? unit.IsVisibleTo(viewer) : !unit.HasActiveInvisibilityUnique(viewer);
    }

    public bool KnowsCiv(string civId) => civId == viewer.CivId || viewer.Knows(civId);
}

public static class ViewDeltaBuilder
{
    /// <summary>The recipient's state before a command, to diff against after it.</summary>
    public sealed record ViewMark(HashSet<HexCoord> Explored, int Gold, int Happiness, HashSet<(string CivId, int UnitId)> HiddenUnits,
        HashSet<HexCoord>? Visible = null, HashSet<string>? KnownCivs = null, YieldsPerTurn? Yields = null);

    /// <summary>Per-turn yields of the recipient's own civ.</summary>
    public sealed record YieldsPerTurn(int Gold, int Science, int Culture, int Faith);

    private static YieldsPerTurn Yields(CivView view)
    {
        var total = new Stats();
        foreach (var stats in view.GetStatMapForNextTurn().Values) total.Add(stats);
        return new YieldsPerTurn(KotlinMath.FloatToInt(total.Gold), KotlinMath.FloatToInt(total.Science),
            KotlinMath.FloatToInt(total.Culture), KotlinMath.FloatToInt(total.Faith));
    }

    private static HashSet<string> KnownSet(CivView view) => view.GetKnownCivs().Select(it => it.CivId).ToHashSet();

    public static ViewMark Capture(GameInfo gameInfo, CivilizationClass viewer)
    {
        var view = new GameView(gameInfo, viewer).CivView;
        var hidden = gameInfo.Civilizations.Where(it => it != viewer).SelectMany(it => it.Units.GetCivUnits().Select(unit => (Civ: it, Unit: unit)))
            .Where(it => it.Unit.HasActiveInvisibilityUnique(viewer) && !it.Unit.IsVisibleTo(viewer))
            .Select(it => (it.Civ.CivId, it.Unit.Id)).ToHashSet();
        return new ViewMark(ExploredSet(gameInfo, viewer), view.Gold, view.GetHappiness(), hidden, VisibleSet(gameInfo, viewer), KnownSet(view), Yields(view));
    }

    private static HashSet<HexCoord> VisibleSet(GameInfo gameInfo, CivilizationClass viewer)
    {
        var tileMapView = new GameView(gameInfo, viewer).TileMapView;
        return gameInfo.TileMap.TileList.Where(it => it.IsExplored(viewer) && tileMapView.GetTile(it).IsVisible()).Select(it => it.Position).ToHashSet();
    }

    private static HashSet<HexCoord> ExploredSet(GameInfo gameInfo, CivilizationClass viewer) =>
        gameInfo.TileMap.TileList.Where(it => it.IsExplored(viewer)).Select(it => it.Position).ToHashSet();

    /// <summary>Events in order: reveals first (so later events can refer to new tiles), the filtered engine events, then stat changes.</summary>
    public static IReadOnlyList<EngineEvent> BuildEvents(GameInfo gameInfo, CivilizationClass viewer, ViewMark before, IEnumerable<EngineEvent> events)
    {
        var visibility = new GameViewVisibility(gameInfo, viewer, before.HiddenUnits);
        var result = new List<EngineEvent>();
        var revealed = RevealedSince(gameInfo, viewer, before);
        if (revealed != null) result.Add(revealed);
        foreach (var gameEvent in events) result.AddRange(EventFilter.Classify(gameEvent, viewer.CivId, visibility));
        var view = new GameView(gameInfo, viewer).CivView;
        var yields = Yields(view);
        if (view.Gold != before.Gold || view.GetHappiness() != before.Happiness || (before.Yields != null && yields != before.Yields))
            result.Add(new StatsChanged(viewer.CivId, view.Gold, view.GetHappiness(), yields.Gold, yields.Science, yields.Culture, yields.Faith));
        return result;
    }

    public static ViewDelta Build(GameInfo gameInfo, CivilizationClass viewer, ViewMark before, IEnumerable<EngineEvent> events, long seq) => new()
    {
        Seq = seq,
        Events = BuildEvents(gameInfo, viewer, before, events)
            .Select(it => JsonSerializer.SerializeToElement(GameEventRegistry.ToEnvelope(it), UncivJson.DefaultOptions)).ToList()
    };

    /// <summary>
    /// T224: the recipient's delta for one accepted command. <see cref="ViewDelta.Diff"/> is the change between the recipient's snapshot before the command
    /// (<paramref name="before"/>, the host keeps it from the previous call) and the fresh one after it, so the mirror cannot miss a field that has no event;
    /// <see cref="ViewDelta.Events"/> are the filtered events for animation. Returns the new snapshot to pass as <paramref name="before"/> next time.
    /// </summary>
    public static (ViewDelta Delta, ClientSnapshot After) BuildWithDiff(GameInfo gameInfo, CivilizationClass viewer, ClientSnapshot before, ViewMark mark,
        IEnumerable<EngineEvent> events, long seq, TurnStatusDto turnStatus)
    {
        var after = SnapshotBuilder.Build(gameInfo, viewer, seq, turnStatus);
        var diff = SnapshotDiff.Compute(before, after);
        var delta = Build(gameInfo, viewer, mark, events, seq) with { Diff = SnapshotBuilder.ToElement(diff), ViewHash = after.ViewHash };
        return (delta, after);
    }

    private static TilesRevealed? RevealedSince(GameInfo gameInfo, CivilizationClass viewer, ViewMark before)
    {
        var gameView = new GameView(gameInfo, viewer);
        var tiles = new List<TileDto>();
        var units = new List<UnitDto>();
        foreach (var tile in gameInfo.TileMap.TileList)
        {
            var wasExplored = before.Explored.Contains(tile.Position);
            var tileView = gameView.TileMapView.GetTile(tile);
            if (!tileView.IsExplored()) continue;
            var visible = tileView.IsVisible();
            // Fog to visible: the client holds a terrain-only fogged tile and needs the live ownership and units.
            var refreshed = wasExplored && visible && before.Visible != null && !before.Visible.Contains(tile.Position);
            if (wasExplored && !refreshed) continue;
            tiles.Add(SnapshotBuilder.BuildTile(tileView, gameView));
            if (!visible) continue;
            units.AddRange(tileView.GetVisibleUnits().Select(it => SnapshotBuilder.BuildUnit(it, tileView)));
        }
        var known = KnownSet(gameView.CivView);
        var introduced = new HashSet<string>(known.Where(it => before.KnownCivs != null && !before.KnownCivs.Contains(it)));
        foreach (var unit in units) introduced.Add(unit.Owner);
        introduced.Remove(viewer.CivId);
        if (before.KnownCivs != null) introduced.ExceptWith(before.KnownCivs);
        var civs = gameInfo.Civilizations.Where(it => introduced.Contains(it.CivId)).Select(it => SnapshotBuilder.BuildCiv(gameView, it)).ToList();
        return tiles.Count == 0 && civs.Count == 0 ? null : new TilesRevealed(viewer.CivId, tiles, units, civs);
    }
}
