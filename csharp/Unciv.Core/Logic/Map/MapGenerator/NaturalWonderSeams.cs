using Unciv.Logic.Map.Tiles;

namespace Unciv.Logic.Map.MapGenerator;

// T074 seam for the Tile.setConnectedByRiver convertTerrains parameter (MapGenerator.Helpers.convertTerrains, owned by T041).
// An extension method, so a real instance overload added to Tile wins; T041 then deletes this file.
internal static class NaturalWonderSeams
{
    /// <summary>Kotlin <c>Tile.setConnectedByRiver(otherTile, newValue, convertTerrains)</c>. TODO(T041): <paramref name="convertTerrains"/> is ignored.</summary>
    internal static bool SetConnectedByRiver(this Tile tile, Tile otherTile, bool newValue, bool convertTerrains)
    {
        _ = convertTerrains;
        return tile.SetConnectedByRiver(otherTile, newValue);
    }
}
