using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.StatTypes;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>One yield icon of a tile: the stat and how much of it (whole units).</summary>
public readonly record struct YieldChip(Stat Stat, int Amount);

/// <summary>
/// C032: the in-world yield icons of a tile (the "Show tile yields" setting of C028). Computed from the viewer's snapshot tile and the static ruleset alone, so a fogged tile
/// shows what the viewer remembers of it and nothing the snapshot withholds is guessed (a resource the viewer cannot see adds nothing). These are the tile's <b>base</b> yields:
/// terrain (a natural wonder or an overriding feature replaces what lies under it), a visible resource, and an unpillaged improvement with the resource's bonus when it is the right one.
/// Deliberate deviation from Kotlin: no city- or civ-specific bonuses (uniques, buildings, policies, golden age) and no river or road yields, because those need state the snapshot does not carry.
/// </summary>
public static class TileYieldModel
{
    /// <summary>Most icons drawn on one tile; the biggest yields win.</summary>
    public const int MaxChips = 4;

    private const string CityCentre = "City center";

    private static readonly Stat[] Order = [Stat.Food, Stat.Production, Stat.Gold, Stat.Science, Stat.Culture, Stat.Faith, Stat.Happiness];

    /// <summary>The tile's base yield stats; zero stats stay zero.</summary>
    public static Stats BaseStats(Ruleset ruleset, TileDto tile)
    {
        var stats = new Stats();
        // Terrains in the engine's order: base, natural wonder, then features; an overriding one replaces what came before
        var terrains = new List<string> { tile.BaseTerrain };
        if (tile.NaturalWonder != null) terrains.Add(tile.NaturalWonder);
        terrains.AddRange(tile.Features);
        foreach (var name in terrains)
        {
            if (!ruleset.Terrains.TryGetValue(name, out var terrain)) continue;
            if (terrain.OverrideStats) stats = new Stats();
            stats.Add(terrain.CloneStats());
        }

        TileResource? resource = null;
        if (tile.Resource != null && ruleset.TileResources.TryGetValue(tile.Resource, out resource)) stats.Add(resource.CloneStats());

        if (tile.Improvement != null && tile.ImprovementPillaged != true && ruleset.TileImprovements.TryGetValue(tile.Improvement, out var improvement))
        {
            stats.Add(improvement.CloneStats());
            if (resource?.ImprovementStats != null && resource.IsImprovedBy(improvement.Name)) stats.Add(resource.ImprovementStats);
        }

        if (tile.Improvement == CityCentre)
        {
            foreach (var stat in Order)
            {
                if (stats[stat] < Stats.DefaultCityCenterMinimum[stat]) stats[stat] = Stats.DefaultCityCenterMinimum[stat];
            }
        }
        return stats;
    }

    /// <summary>
    /// The icons to draw on a tile: every non-zero yield, rounded half away from zero to whole units, biggest first (ties keep the stat order food, production, gold, science, culture, faith, happiness),
    /// at most <see cref="MaxChips"/>. Negative yields count (they matter to the player).
    /// </summary>
    public static IReadOnlyList<YieldChip> Chips(Ruleset ruleset, TileDto tile)
    {
        var stats = BaseStats(ruleset, tile);
        var chips = new List<YieldChip>();
        foreach (var stat in Order)
        {
            var amount = Whole(stats[stat]);
            if (amount != 0) chips.Add(new YieldChip(stat, amount));
        }
        return [.. chips.OrderByDescending(it => Math.Abs(it.Amount)).Take(MaxChips)];
    }

    /// <summary>Whole units of a yield, halves rounded away from zero (so 0.5 shows as 1 and -0.5 as -1).</summary>
    public static int Whole(float amount) => (int)MathF.Round(amount, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Centre of chip <paramref name="index"/> of <paramref name="count"/> in a row below the tile centre, in tile units: x across, z towards the viewer. The row is centred on the tile.
    /// </summary>
    public static (float X, float Z) ChipOffset(int index, int count)
    {
        const float spacing = 0.36f;
        return ((index - (count - 1) / 2f) * spacing, 0.55f);
    }
}
