using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.Tiles;

// Ported from core/src/com/unciv/logic/map/tile/TileNormalizer.kt (T054).

public static class TileNormalizer
{
    public static void NormalizeToRuleset(Tile tile, Ruleset ruleset)
    {
        if (tile.NaturalWonder != null && !ruleset.Terrains.ContainsKey(tile.NaturalWonder))
            tile.NaturalWonder = null;
        if (tile.NaturalWonder != null)
        {
            var wonderTerrain = tile.GetNaturalWonder();
            if (wonderTerrain.TurnsInto != null)
            {
                tile.BaseTerrain = wonderTerrain.TurnsInto;
                tile.RemoveTerrainFeatures();
            }
            else
            {
                tile.SetTerrainFeatures(tile.TerrainFeatures.Where(it => wonderTerrain.OccursOn.Contains(it)).ToList());
            }
            tile.TileResource = null;
            ClearImprovement(tile);
        }

        if (!ruleset.Terrains.ContainsKey(tile.BaseTerrain))
            tile.BaseTerrain = ruleset.Terrains.Values.First(it => it.Type == TerrainType.Land && !it.Impassable).Name;

        var newFeatures = new List<string>();
        foreach (var terrainFeature in tile.TerrainFeatures)
        {
            var terrainFeatureObject = ruleset.Terrains.GetValueOrDefault(terrainFeature);
            if (terrainFeatureObject == null)
                continue;
            if (terrainFeatureObject.OccursOn.Count > 0 && !terrainFeatureObject.OccursOn.Contains(tile.BaseTerrain))
                continue;
            newFeatures.Add(terrainFeature);
        }
        if (newFeatures.Count != tile.TerrainFeatures.Count)
            tile.SetTerrainFeatures(newFeatures);

        if (tile.Resource != null && !ruleset.TileResources.ContainsKey(tile.Resource)) tile.TileResource = null;
        var resourceObject = tile.TileResource;
        if (resourceObject != null)
        {
            if (!resourceObject.TerrainsCanBeFoundOn.Any(it => it == tile.BaseTerrain || tile.TerrainFeatures.Contains(it)))
                tile.TileResource = null;
        }

        // If we're checking this at gameInfo.setTransients, we can't check the top terrain
        if (tile.Improvement != null) NormalizeTileImprovement(tile, ruleset);
        if (tile.ImprovementInProgress != null) NormalizeImprovementQueue(tile, ruleset);
        if (tile.IsWater || tile.IsImpassible())
            tile.RemoveRoad();
    }

    private static void NormalizeTileImprovement(Tile tile, Ruleset ruleset)
    {
        var improvementObject = ruleset.TileImprovements.GetValueOrDefault(tile.Improvement.NotNull());
        if (improvementObject == null)
        {
            ClearImprovement(tile);
            return;
        }
        if (tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvementObject, gameContext: GameContext.IgnoreConditionalsState, isNormalizeCheck: true))
            return;
        ClearImprovement(tile);
    }

    private static void NormalizeImprovementQueue(Tile tile, Ruleset ruleset)
    {
        var oldImprovementQueue = tile.ImprovementQueue.ToList();
        tile.ImprovementQueue.Clear();

        // only readd them if they're still buildable
        foreach (var improvementQueueEntry in oldImprovementQueue)
        {
            var improvementObject = ruleset.TileImprovements.GetValueOrDefault(improvementQueueEntry.Improvement);
            if (improvementObject == null) continue;
            if (tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvementObject, gameContext: GameContext.IgnoreConditionalsState, isNormalizeCheck: true))
                tile.ImprovementQueue.Add(improvementQueueEntry);
        }
    }

    private static void ClearImprovement(Tile tile)
    {
        // This runs from mapgen, so don't go through the side-effect-triggering TileImprovementFunctions
        tile.Improvement = null;
        tile.StopWorkingOnImprovement();
    }
}
