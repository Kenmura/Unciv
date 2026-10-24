using System.Collections.Immutable;
using System.Globalization;

namespace Unciv.AI.Data;

// The three AIGrandStrategy_* tables of data/grand_strategy_flavors.tsv, identical in base, G&K and BNW:
//  - Flavors:    the flavor a strategy is scored against (dot product with the leader's personality)
//  - FlavorMods: how adopting the strategy shifts the leader's flavors
//  - Yields:     the yield priority the strategy implies
// Strategies and keys keep file order.
public sealed class GrandStrategyFlavors
{
    public const string FlavorsTable = "AIGrandStrategy_Flavors";
    public const string FlavorModsTable = "AIGrandStrategy_FlavorMods";
    public const string YieldsTable = "AIGrandStrategy_Yields";

    private static readonly ImmutableArray<string> Header = ["table", "strategy", "key", "value"];

    private readonly OrderedDictionary<string, OrderedDictionary<string, OrderedDictionary<string, int>>> tables = new();

    private GrandStrategyFlavors() { }

    public static GrandStrategyFlavors Parse(string tsv)
    {
        var result = new GrandStrategyFlavors();
        foreach (string[] cells in TsvTable.Rows(tsv, "grand_strategy_flavors", Header.AsSpan()))
        {
            if (cells[0] != FlavorsTable && cells[0] != FlavorModsTable && cells[0] != YieldsTable)
                throw new FormatException($"grand_strategy_flavors: unknown table '{cells[0]}'");
            if (!int.TryParse(cells[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                throw new FormatException($"grand_strategy_flavors: '{cells[1]}/{cells[2]}' has non-integer value '{cells[3]}'");

            if (!result.tables.TryGetValue(cells[0], out var strategies))
                result.tables[cells[0]] = strategies = new();
            if (!strategies.TryGetValue(cells[1], out var keys))
                strategies[cells[1]] = keys = new();
            if (!keys.TryAdd(cells[2], value))
                throw new FormatException($"grand_strategy_flavors: duplicate {cells[0]} entry {cells[1]}/{cells[2]}");
        }
        return result;
    }

    public static GrandStrategyFlavors LoadEmbedded() => Parse(TsvTable.ReadResource("grand_strategy_flavors.tsv"));

    /// <summary>Strategy names of a table, in file order.</summary>
    public IEnumerable<string> Strategies(string table) =>
        tables.TryGetValue(table, out var strategies) ? strategies.Keys : [];

    /// <summary>The (key, value) pairs a strategy has in a table, in file order; empty if it has none.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> Entries(string table, string strategy) =>
        tables.TryGetValue(table, out var strategies) && strategies.TryGetValue(strategy, out var keys)
            ? keys.ToList()
            : [];

    /// <summary>One value, or 0 when the strategy has no such entry (an absent entry contributes nothing).</summary>
    public int Get(string table, string strategy, string key) =>
        tables.TryGetValue(table, out var strategies)
        && strategies.TryGetValue(strategy, out var keys)
        && keys.TryGetValue(key, out int value) ? value : 0;
}
