using Unciv.Models.Ruleset;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator.Mapregions;

// What MapRegions / Region / MinorCivPlacer read from a Civilization (T075); Civilization implements it, tests fake it.

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
