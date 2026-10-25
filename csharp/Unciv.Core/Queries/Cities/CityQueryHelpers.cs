using Unciv.Models;
using Unciv.Models.StatTypes;

namespace Unciv.Queries.Cities;

// T196: shared building blocks of the city and tile panel queries. Dictionaries of the engine are flattened into ordered lists so the
// wire format has one stable order (the engine's own sets are hash ordered and must never leak into output).

/// <summary>A stat and its value; stats stay float where the engine computes floats.</summary>
public sealed record StatAmount(string Stat, float Value);

/// <summary>A name and a count (specialists, followers, pressures).</summary>
public sealed record NamedAmount(string Name, int Amount);

/// <summary>The stats one source (a building, a tile, the population...) adds to a city.</summary>
public sealed record StatSource(string Source, IReadOnlyList<StatAmount> Stats);

internal static class CityQueryHelpers
{
    /// <summary>The nonzero stats in declaration order.</summary>
    public static List<StatAmount> StatList(Stats stats) =>
        stats.AsSequence().Where(it => it.Value != 0f).Select(it => new StatAmount(it.Key.ToString(), it.Value)).ToList();

    public static List<NamedAmount> Named(Counter<string> counter) =>
        counter.Where(it => it.Value != 0).Select(it => new NamedAmount(it.Key, it.Value)).OrderBy(it => it.Name, StringComparer.Ordinal).ToList();
}
