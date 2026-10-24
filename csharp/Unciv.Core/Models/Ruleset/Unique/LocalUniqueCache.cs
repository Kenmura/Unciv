namespace Unciv.Models.Ruleset.Unique;

/// <summary>What <see cref="LocalUniqueCache"/> reads from <c>Civilization</c>.</summary>
public interface ILocalUniqueCacheCivilization
{
    string CivName { get; }
    /// <summary><c>civ.state</c></summary>
    GameContext State { get; }
    IEnumerable<Unique> GetMatchingUniques(UniqueType uniqueType, GameContext gameContext);
}

/// <summary>What <see cref="LocalUniqueCache"/> reads from <c>City</c>.</summary>
public interface ILocalUniqueCacheCity
{
    string Id { get; }
    ILocalUniqueCacheCivilization Civ { get; }
    /// <summary><c>city.state</c></summary>
    GameContext State { get; }
    IEnumerable<Unique> GetLocalMatchingUniques(UniqueType uniqueType, GameContext gameContext);
}

/// <summary>
/// Used to cache results of getMatchingUniques
/// Must only be used when we're sure the matching uniques will not change in the meantime.
/// Ported from core/src/com/unciv/models/ruleset/unique/LocalUniqueCache.kt (<c>timeThis</c> profiling dropped).
/// </summary>
public class LocalUniqueCache(bool cache = true)
{
    public bool Cache { get; } = cache;

    // This stores sequences *that iterate directly on a list* - that is, pre-resolved
    private readonly Dictionary<string, IEnumerable<Unique>> keyToUniques = new();

    public IEnumerable<Unique> ForCityGetMatchingUniques(ILocalUniqueCacheCity city, UniqueType uniqueType, GameContext? gameContext = null)
    {
        var state = gameContext ?? city.State;
        // City uniques are a combination of *global civ* uniques plus *city relevant* uniques (see City.getMatchingUniques())
        // We can cache the civ uniques separately, so if we have several cities using the same cache,
        //   we can cache the list of *civ uniques* to reuse between cities.

        var citySpecificUniques = Get(
            $"city-{city.Id}-{uniqueType}",
            city.GetLocalMatchingUniques(uniqueType, GameContext.IgnoreMultiplicationForCaching)
        ).Where(it => it.ConditionalsApply(state))
            .SelectMany(it => it.GetMultiplied(state));

        var civUniques = ForCivGetMatchingUniques(city.Civ, uniqueType, state);

        return citySpecificUniques.Concat(civUniques);
    }

    public IEnumerable<Unique> ForCivGetMatchingUniques(ILocalUniqueCacheCivilization civ, UniqueType uniqueType, GameContext? gameContext = null)
    {
        var state = gameContext ?? civ.State;
        var sequence = civ.GetMatchingUniques(uniqueType, GameContext.IgnoreMultiplicationForCaching);
        // The uniques CACHED are ALL civ uniques, regardless of conditional matching.
        // The uniques RETURNED are uniques AFTER conditional matching.
        // This allows reuse of the cached values, between runs with different conditionals -
        //   for example, iterate on all tiles and get StatPercentForObject uniques relevant for each tile,
        //   each tile will have different conditional state, but they will all reuse the same list of uniques for the civ
        return Get(
            $"civ-{civ.CivName}-{uniqueType}",
            sequence
        ).Where(it => it.ConditionalsApply(state))
            .SelectMany(it => it.GetMultiplied(state));
    }

    /// <summary>Get cached results as a sequence</summary>
    private IEnumerable<Unique> Get(string key, IEnumerable<Unique> sequence)
    {
        if (!Cache) return sequence;
        if (keyToUniques.TryGetValue(key, out var valueInMap)) return valueInMap;
        // Iterate the sequence, save actual results as a list, as return a sequence to that
        var results = sequence.ToList();
        keyToUniques[key] = results;
        return results;
    }
}
