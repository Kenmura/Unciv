using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Tile;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// T076 seam: StartNormalizer calls MapRegionResources (resourceplacement, T077) only through the members below.
// T077 replaces the bodies with calls to its MapRegionResources class.
internal static class MapRegionResourcesSeams
{
    /// <summary><c>MapRegionResources.tryAddingResourceToTiles(tileData, resource, amount, tiles, ratio, respectImpacts, baseImpact, randomImpact, majorDeposit)</c>
    /// with the defaults for the parameters not listed. Returns the number of placed resources. (T077)</summary>
    internal static int TryAddingResourceToTiles(TileDataMap tileData, TileResource resource, int amount, IEnumerable<Tile> tiles, bool majorDeposit = false) =>
        throw new NotSupportedException("MapRegionResources.tryAddingResourceToTiles is ported by T077");

    /// <summary><c>MapRegionResources.placeResourcesInTiles(tileData, frequency, tileList, resourceOptions, majorDeposit = ...)</c>
    /// with the defaults for baseImpact and randomImpact; the result map is not used by StartNormalizer. (T077)</summary>
    internal static void PlaceResourcesInTiles(TileDataMap tileData, int frequency, List<Tile> tileList, List<TileResource> resourceOptions, bool majorDeposit = false) =>
        throw new NotSupportedException("MapRegionResources.placeResourcesInTiles is ported by T077");
}
