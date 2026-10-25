using Godot;
using Unciv.Logic.Map;
using Unciv.GodotClient.Assets;
using CoreVector2 = Unciv.Mathematics.Vector2;

namespace Unciv.GodotClient.Map;

/// <summary>
/// Tile coordinate to world position and back, through the engine's own <see cref="HexMath"/> (so the client never re-derives the hex grid).
/// World x is the engine's world x, world z is minus the engine's world y (north is up on screen). Tile radius is 1, matching the hex prism.
/// </summary>
public static class MapLayout
{
    /// <summary>The centre of a tile's top face for a tile whose prism is stretched vertically by <paramref name="elevation"/>.</summary>
    public static Vector3 TileTop(int x, int y, float elevation = 1f)
    {
        var world = HexMath.Hex2WorldCoords(new HexCoord(x, y));
        return new Vector3(world.x, ProceduralAssets.HexHeight * elevation, -world.y);
    }

    /// <summary>The tile a point on the ground plane lies in (rounding as the engine does for its own picking).</summary>
    public static HexCoord TileAt(Vector3 ground)
    {
        var hex = HexMath.RoundHexCoords(HexMath.World2HexCoords(new CoreVector2(ground.X, -ground.Z)));
        return new HexCoord((int)hex.x, (int)hex.y);
    }
}
