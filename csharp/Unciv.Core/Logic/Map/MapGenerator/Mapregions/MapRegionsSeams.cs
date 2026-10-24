using Unciv.Logic.Map.MapGenerator.ResourcePlacement;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// Open owner: T104 (final seam sweep deletes what T055/T076/T077 left).
// T075 seams. MapRegions / Region talk to Civilization (T055) and to the placement logic ported by T076 (StartNormalizer, MinorCivPlacer)
// and T077 (LuxuryResourcePlacementLogic, StrategicBonusResourcePlacementLogic) only through the members below.
// T055 makes Civilization implement IMapRegionsCivilization; T076 / T077 replace the matching MapRegionsSeams method with a call to their class.

/// <summary>What <see cref="MapRegions"/> and <see cref="Region"/> read from <c>Civilization</c>.</summary>
public interface IMapRegionsCivilization
{
    /// <summary><c>civ.civName</c></summary>
    string CivName { get; }

    /// <summary><c>civ.civID</c></summary>
    string CivId { get; }

    /// <summary><c>ruleset.nations[civ.civName]!!.getStartBias(ruleset, civ.getGameContextForStartBias())</c></summary>
    List<string> GetStartBias(Ruleset ruleset);

    /// <summary><c>civ.state.stateBasedRandom(caller)</c></summary>
    GameRandom StateBasedRandom(string caller);
}

internal static class MapRegionsSeams
{
    /// <summary><c>StartNormalizer.normalizeStart(startTile, tileMap, tileData, ruleset, isMinorCiv)</c> (T076)</summary>
    internal static void NormalizeStart(Tile startTile, TileMap tileMap, TileDataMap tileData, Ruleset ruleset, bool isMinorCiv) =>
        StartNormalizer.NormalizeStart(startTile, tileMap, tileData, ruleset, isMinorCiv);

    /// <summary><c>LuxuryResourcePlacementLogic.assignLuxuries(regions, tileData, ruleset)</c> (T077)</summary>
    internal static (List<string> CityStateLuxuries, List<string> RandomLuxuries) AssignLuxuries(
        List<Region> regions, TileDataMap tileData, Ruleset ruleset) =>
        LuxuryResourcePlacementLogic.AssignLuxuries(regions, tileData, ruleset);

    /// <summary><c>MinorCivPlacer.placeMinorCivs(regions, tileMap, minorCivs, tileData, ruleset)</c> (T076)</summary>
    internal static void PlaceMinorCivs(
        List<Region> regions, TileMap tileMap, IReadOnlyList<IMapRegionsCivilization> minorCivs, TileDataMap tileData, Ruleset ruleset) =>
        MinorCivPlacer.PlaceMinorCivs(regions, tileMap, minorCivs, tileData, ruleset);

    /// <summary><c>LuxuryResourcePlacementLogic.placeLuxuries(regions, tileMap, tileData, ruleset, cityStateLuxuries, randomLuxuries)</c> (T077)</summary>
    internal static void PlaceLuxuries(
        List<Region> regions, TileMap tileMap, TileDataMap tileData, Ruleset ruleset, List<string> cityStateLuxuries, List<string> randomLuxuries) =>
        LuxuryResourcePlacementLogic.PlaceLuxuries(regions, tileMap, tileData, ruleset, cityStateLuxuries, randomLuxuries);

    /// <summary><c>StrategicBonusResourcePlacementLogic.placeStrategicAndBonuses(tileMap, regions, tileData)</c> (T077)</summary>
    internal static void PlaceStrategicAndBonuses(TileMap tileMap, List<Region> regions, TileDataMap tileData) =>
        StrategicBonusResourcePlacementLogic.PlaceStrategicAndBonuses(tileMap, regions, tileData);
}

/// <summary>Kotlin <c>maxByOrNull</c> / <c>minByOrNull</c>: the FIRST element with the extreme key wins (strict comparison), null when empty.</summary>
internal static class MapRegionsSelection
{
    internal static T? MaxByOrNull<T>(this IEnumerable<T> source, Func<T, int> selector) where T : class
    {
        T? best = null;
        var bestValue = 0;
        foreach (var item in source)
        {
            var value = selector(item);
            if (best == null || bestValue < value)
            {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }

    internal static T? MinByOrNull<T>(this IEnumerable<T> source, Func<T, int> selector) where T : class
    {
        T? best = null;
        var bestValue = 0;
        foreach (var item in source)
        {
            var value = selector(item);
            if (best == null || value < bestValue)
            {
                best = item;
                bestValue = value;
            }
        }
        return best;
    }
}
