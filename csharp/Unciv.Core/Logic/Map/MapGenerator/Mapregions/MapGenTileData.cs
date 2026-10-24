using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// core/src/com/unciv/logic/map/mapgenerator/mapregions/MapGenTileData.kt
// Holds a bunch of tile info that is only interesting during map gen
public class MapGenTileData
{
    public MapGenTileData(Tile tile, Region? region, Ruleset ruleset)
    {
        Tile = tile;
        Region = region;
        Evaluate(ruleset);
    }

    public Tile Tile { get; }
    public Region? Region { get; }
    public int CloseStartPenalty { get; set; }
    public Dictionary<MapRegions.ImpactType, int> Impacts { get; } = new();
    public bool IsFood { get; private set; }
    public bool IsProd { get; private set; }
    public bool IsGood { get; private set; }
    public bool IsJunk { get; private set; }
    public bool IsTwoFromCoast { get; private set; }

    public bool IsGoodStart { get; set; } = true;
    public int StartScore { get; set; }

    public void AddCloseStartPenalty(int penalty)
    {
        if (CloseStartPenalty == 0)
            CloseStartPenalty = penalty;
        else
        {
            // Multiple overlapping values - take the higher one and add 20 %
            CloseStartPenalty = Math.Max(CloseStartPenalty, penalty);
            CloseStartPenalty = Math.Min(97, KotlinMath.FloatToInt(CloseStartPenalty * 1.2f));
        }
    }

    /// <summary>Populates all private-set fields</summary>
    private void Evaluate(Ruleset ruleset)
    {
        // Check if we are two tiles from coast (a bad starting site)
        if (!Tile.IsAdjacentToCoast() && Tile.Neighbors.Any(it => it.IsAdjacentToCoast()))
            IsTwoFromCoast = true;

        // Check first available out of unbuildable features, then other features, then base terrain
        var terrainToCheck = Tile.TerrainFeatures.Count == 0
            ? Tile.GetBaseTerrain()
            : Tile.TerrainFeatureObjects.FirstOrDefault(it => it.Unbuildable) ?? Tile.TerrainFeatureObjects.First();

        // Add all applicable qualities
        ((IHasUniques)terrainToCheck).ForEachMatchingUnique(
            UniqueType.HasQuality,
            new GameContext(region: Region),
            unique =>
            {
                switch (unique.Params[0])
                {
                    case "Food": IsFood = true; break;
                    case "Desirable": IsGood = true; break;
                    case "Production": IsProd = true; break;
                    case "Undesirable": IsJunk = true; break;
                }
            });

        // Were there in fact no explicit qualities defined for any region at all? If so let's guess at qualities to preserve mod compatibility.
        if (!((IHasUniques)terrainToCheck).UniqueObjects.Any(it => it.Type == UniqueType.HasQuality))
        {
            if (Tile.IsWater) return; // Most water type tiles have no qualities

            // is it junk???
            if (terrainToCheck.Impassable)
            {
                IsJunk = true;
                return; // Don't bother checking the rest, junk is junk
            }

            // Take possible improvements into account
            var improvements = ruleset.TileImprovements.Values.Where(it =>
                it.TerrainsCanBeBuiltOn.Contains(terrainToCheck.Name) &&
                it.UniqueTo == null &&
                !((IHasUniques)it).HasUnique(UniqueType.GreatImprovement)).ToList();

            var maxFood = terrainToCheck.Food + (improvements.Count == 0 ? 0f : improvements.Max(it => it.Food));
            var maxProd = terrainToCheck.Production + (improvements.Count == 0 ? 0f : improvements.Max(it => it.Production));
            var bestImprovementValue = improvements.Count == 0
                ? 0f
                : improvements.Max(it => it.Food + it.Production + it.Gold + it.Culture + it.Science + it.Faith);
            var maxOverall = terrainToCheck.Food + terrainToCheck.Production + terrainToCheck.Gold +
                terrainToCheck.Culture + terrainToCheck.Science + terrainToCheck.Faith + bestImprovementValue;

            if (maxFood >= 2) IsFood = true;
            if (maxProd >= 2) IsProd = true;
            if (maxOverall >= 3) IsGood = true;
        }
    }
}
