using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Mathematics;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/TileMapView.kt (T084). Stripped: the purity annotations.

/// <summary>Lazy cache of <see cref="TileView"/>s for a <see cref="TileMap"/> from the perspective of the viewer.</summary>
/// <remarks>The viewer is null in the map editor.</remarks>
public class TileMapView(TileMap map, CivilizationClass? viewer, bool spectatorMode = false, GameView? gameView = null)
    : View<TileMap>(map, viewer, spectatorMode)
{
    private TileMap tileMap => Wrapped;

    // Kotlin: `by lazy { arrayOfNulls(tileMap.tileList.size) }`
    private TileView?[]? tileViews;

    public GameView? GameView { get; } = gameView;

    /// <summary>
    /// Note that since this requires the original Tile, this CAN return tiles that are NOT visible to the viewing civ.
    /// This is currently required for the map since to even create the empty tiles we need to know it exists.
    /// I don't have a good solution for this data leak at the moment
    /// since calculating paths to tiles that don't exist will currently break pathfinding.
    /// </summary>
    public TileView GetTile(Tile tile)
    {
        var idx = tile.ZeroBasedIndex;
        var views = tileViews ??= new TileView?[tileMap.TileList.Count];
        return views[idx] ??= new TileView(tile, this, Viewer, SpectatorMode);
    }

    private TileView? ToViewIfExplored(Tile tile)
    {
        if (Viewer != null && !tile.IsExplored(Viewer)) return null;
        return new TileView(tile, this, Viewer, SpectatorMode);
    }

    /// <summary>Returns the <see cref="TileView"/> at <paramref name="position"/>, or <c>null</c> if it isn't explored by the viewer.</summary>
    public TileView? GetTile(HexCoord position) => ToViewIfExplored(tileMap[position]);

    /// <summary>Position of <paramref name="position"/> normalized to the map's non-wrapped bounds, for world-wrap rendering.</summary>
    public HexCoord GetUnwrappedPosition(HexCoord position) => tileMap.GetUnwrappedPosition(position);

    // Not sure if I want these as part of the API -
    // we can separate the "get coord" part and put it in HexMath,
    // And add a new function of "get tile by coord" in here
    // These are really only used for borders so IDK
    public TileView? GetLeftSharedNeighbor(TileView tile, TileView neighbor)
    {
        var clockPos = tileMap.GetNeighborTileClockPosition(tile.Unwrap(), neighbor.Unwrap());
        var n = tileMap.GetClockPositionNeighborTile(tile.Unwrap(), (clockPos - 2) % 12);
        return n == null ? null : ToViewIfExplored(n);
    }

    public TileView? GetRightSharedNeighbor(TileView tile, TileView neighbor)
    {
        var clockPos = tileMap.GetNeighborTileClockPosition(tile.Unwrap(), neighbor.Unwrap());
        var n = tileMap.GetClockPositionNeighborTile(tile.Unwrap(), (clockPos + 2) % 12);
        return n == null ? null : ToViewIfExplored(n);
    }

    public Vector2 GetNeighborTilePositionAsWorldCoords(TileView tile, TileView neighbor) =>
        tileMap.GetNeighborTilePositionAsWorldCoords(tile.Unwrap(), neighbor.Unwrap());

    /// <summary>World-coordinate offset of <paramref name="to"/> relative to <paramref name="from"/>, accounting for map wraparound.</summary>
    public Vector2 GetRelativeWorldPosition(TileView from, TileView to)
    {
        var fromTile = from.Unwrap();
        var targetPos = new Vector2(to.Unwrap().Position.ToVector2());
        if (tileMap.MapParameters.WorldWrap)
            targetPos = HexMath.GetUnwrappedNearestTo(targetPos.ToHexCoord(), fromTile.Position, tileMap.MaxLongitude);
        var targetWorld = HexMath.Hex2WorldCoords(targetPos.ToHexCoord());
        var fromWorld = HexMath.Hex2WorldCoords(fromTile.Position);
        return new Vector2(targetWorld.x - fromWorld.x, targetWorld.y - fromWorld.y);
    }

    /// <summary>Names of nations with a starting location on <paramref name="tile"/> (map editor only).</summary>
    public List<string> GetStartingLocationNationNames(TileView tile)
    {
        var rawTile = tile.Unwrap();
        return tileMap.StartingLocationsByNation.Where(it => it.Value.Contains(rawTile)).Select(it => it.Key).ToList();
    }
}
