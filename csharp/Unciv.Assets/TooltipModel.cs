using System.Globalization;
using System.Text;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Queries.Cities;
using Unciv.Queries.Tiles;
using Unciv.UI.ObjectDescriptions;

namespace Unciv.Assets;

/// <summary>One line of a tooltip. <see cref="Final"/> text already came out of the engine's own (translating) description code and must not be translated again.</summary>
public sealed record TipLine(string Text, bool Final = false);

/// <summary>
/// C027: tooltip content as plain data, Godot-free so it is unit-tested. The tile tooltip words the engine's <see cref="TileInfoResult"/> (the viewer's answer, so a
/// tile the viewer cannot see is never described); the unit and building tooltips are the ruleset's own short descriptions; the stat breakdown lists each source of a
/// yield exactly as the engine computed it. Nothing is computed here beyond sums of the engine's numbers for display.
/// </summary>
public static class TooltipModel
{
    /// <summary>Lines of the hover tooltip of a tile; the first is the terrain, then features, resource, improvement, owner, units, the yields and their breakdown.</summary>
    public static IReadOnlyList<TipLine> Tile(TileInfoResult tile, Ruleset ruleset)
    {
        var lines = new List<TipLine>();
        // The engine's tile answer is the live state of any explored tile. A tile in the fog shows only what the viewer can still know of it (terrain, the
        // improvement last seen, ownership); what is going on there now (work in progress, pillaging, who works it, the yields) would leak.
        bool seen = tile.IsVisible;
        if (tile.IsCityCenter && tile.OwningCity != null) lines.Add(new TipLine(tile.OwningCity));
        lines.Add(new TipLine(tile.BaseTerrain));
        lines.AddRange(tile.TerrainFeatures.Select(it => new TipLine(it)));
        if (tile.NaturalWonder != null) lines.Add(new TipLine(tile.NaturalWonder));
        if (tile.Resource != null)
        {
            bool strategic = ruleset.TileResources.TryGetValue(tile.Resource, out var resource) && resource.ResourceType == ResourceType.Strategic;
            lines.Add(new TipLine(strategic && seen ? $"{{{tile.Resource}}} ({Number(tile.ResourceAmount)})" : tile.Resource));
        }
        if (tile.Improvement != null) lines.Add(new TipLine(seen && tile.ImprovementIsPillaged ? $"[{tile.Improvement}] (Pillaged!)" : tile.Improvement));
        if (seen && tile.ImprovementInProgress != null)
            lines.Add(new TipLine(tile.TurnsToImprovement > 0 ? $"{{{tile.ImprovementInProgress}}} - {Number(tile.TurnsToImprovement)}" : $"{{{tile.ImprovementInProgress}}} ({{Under construction}})"));
        if (tile.Road != "None" && !tile.IsCityCenter) lines.Add(new TipLine(seen && tile.RoadIsPillaged ? $"[{tile.Road}] (Pillaged!)" : tile.Road));
        if (tile.HasRiver) lines.Add(new TipLine("River"));
        if (tile.Owner != null) lines.Add(new TipLine(tile.OwningCity != null ? $"Owned by [{tile.OwningCity}]" : tile.Owner));
        if (seen && tile.WorkedBy != null) lines.Add(new TipLine($"Worked by [{tile.WorkedBy}]"));
        foreach (var unit in tile.Units) lines.Add(new TipLine($"{unit.Name} - {unit.Civ}"));
        if (seen && tile.Yields.Count > 0) lines.Add(new TipLine(string.Join(", ", tile.Yields.Select(Amount))));
        return lines;
    }

    /// <summary>The name, the cost and the ruleset's own short description of a unit (null for an unknown name).</summary>
    public static IReadOnlyList<TipLine>? Unit(Ruleset ruleset, string name)
    {
        if (!ruleset.Units.TryGetValue(name, out var unit)) return null;
        var lines = new List<TipLine> { new(name) };
        if (unit.Cost > 0) lines.Add(new TipLine($"{{Cost}}: {Number(unit.Cost)}"));
        string description = BaseUnitDescriptions.GetShortDescription(unit);
        if (description.Length != 0) lines.Add(new TipLine(description, Final: true));
        return lines;
    }

    /// <summary>The name, the cost and the ruleset's own multi-line short description of a building or wonder (null for an unknown name).</summary>
    public static IReadOnlyList<TipLine>? Building(Ruleset ruleset, string name)
    {
        if (!ruleset.Buildings.TryGetValue(name, out var building)) return null;
        var lines = new List<TipLine> { new(name) };
        if (building.Cost > 0) lines.Add(new TipLine($"{{Cost}}: {Number(building.Cost)}"));
        string description = BuildingDescriptions.GetShortDescription(building, multiline: true);
        if (description.Length != 0) lines.Add(new TipLine(description, Final: true));
        return lines;
    }

    /// <summary>
    /// The sources of a stat, one line each, then the total of the sources. Only sources that contribute to <paramref name="stat"/> are listed; the total is the sum of the listed values,
    /// so it equals the engine's figure whenever the engine's sources add up to it (a tile or city yield).
    /// </summary>
    public static IReadOnlyList<TipLine> StatBreakdown(string stat, IReadOnlyList<StatSource> sources)
    {
        var lines = new List<TipLine> { new($"{{{stat}}}") };
        float total = 0f;
        foreach (var source in sources)
        {
            var amount = source.Stats.FirstOrDefault(it => it.Stat == stat);
            if (amount == null || amount.Value == 0f) continue;
            lines.Add(new TipLine($"{{{source.Source}}}: {Signed(amount.Value)}"));
            total += amount.Value;
        }
        lines.Add(new TipLine($"{{Total}}: {Signed(total)}"));
        return lines;
    }

    /// <summary>The lines as BBCode for the tooltip host: engine text kept, other text translated with <paramref name="translate"/>, brackets escaped.</summary>
    public static string ToBbcode(IEnumerable<TipLine> lines, Func<string, string> translate)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (text.Length != 0) text.Append('\n');
            text.Append(Escape(line.Final ? line.Text : translate(line.Text)));
        }
        return text.ToString();
    }

    /// <summary>BBCode treats a bracket as a tag; translated text may still contain one (a name such as "[x]"), so it is written as its entity.</summary>
    public static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    private static string Amount(StatAmount amount) => $"{{{amount.Stat}}}: {Number(amount.Value)}";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Signed(float value) => (value >= 0f ? "+" : "") + Number(value);
}
