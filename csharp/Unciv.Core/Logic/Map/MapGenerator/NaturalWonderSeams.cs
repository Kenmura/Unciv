using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;

namespace Unciv.Logic.Map.MapGenerator;

// T074 seams for Tile members owned by T054 (TileNormalizer / TileImprovementFunctions) and T041 (MapGenerator.Helpers.convertTerrains).
// They are extension methods so that the real instance members T054 adds to Tile win overload resolution; T054/T041 then delete this file.
internal static class NaturalWonderSeams
{
    /// <summary>Kotlin <c>Tile.setBaseTerrain</c>. TODO(T054): TileNormalizer.normalizeToRuleset is not ported, so it is skipped.</summary>
    internal static void SetBaseTerrain(this Tile tile, Terrain baseTerrainObject)
    {
        tile.BaseTerrain = baseTerrainObject.Name;
        tile.SetTerrainTransients();
    }

    /// <summary>Kotlin <c>Tile.removeImprovement</c>. TODO(T054): improvementFunctions.setImprovement(null) also updates visibility/city/encampment quests.</summary>
    internal static void RemoveImprovement(this Tile tile)
    {
        tile.ImprovementIsPillaged = false;
        tile.SetImprovementBasic((TileImprovement?)null);
    }

    /// <summary>Kotlin <c>Tile.setConnectedByRiver(otherTile, newValue, convertTerrains)</c>. TODO(T041): <paramref name="convertTerrains"/> is ignored.</summary>
    internal static bool SetConnectedByRiver(this Tile tile, Tile otherTile, bool newValue, bool convertTerrains)
    {
        _ = convertTerrains;
        return tile.SetConnectedByRiver(otherTile, newValue);
    }
}
