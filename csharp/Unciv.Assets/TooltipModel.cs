using System.Globalization;
using System.Text;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Queries.Cities;
using Unciv.Queries.Tiles;
using Unciv.UI.ObjectDescriptions;

namespace Unciv.Assets;

/// <summary>C076: what a tooltip line is, which decides how it is drawn (see <see cref="TooltipModel.ToBbcode"/>).</summary>
public enum TipKind
{
    /// <summary>The first line: the name of what is described.</summary>
    Heading,
    /// <summary>An ordinary line.</summary>
    Body,
    /// <summary>A stat chip ("Food: 2"): drawn in the stat's colour; consecutive stat lines share one row.</summary>
    Stat,
    /// <summary>One source of a stat in a breakdown.</summary>
    Source,
    /// <summary>The name of the stat a breakdown is about.</summary>
    Section,
    /// <summary>A small muted last line (what the thing requires).</summary>
    Footer,
}

/// <summary>
/// One line of a tooltip. <see cref="Final"/> text already came out of the engine's own (translating) description code and must not be translated again.
/// <see cref="Stat"/> names the stat whose colour a <see cref="TipKind.Stat"/> line is drawn in.
/// </summary>
public sealed record TipLine(string Text, bool Final = false, TipKind Kind = TipKind.Body, string? Stat = null);

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
        // C076: the heading is the city's name on a city centre, otherwise the terrain
        if (tile.IsCityCenter && tile.OwningCity != null) lines.Add(new TipLine(tile.OwningCity, Kind: TipKind.Heading));
        lines.Add(tile.IsCityCenter && tile.OwningCity != null ? new TipLine(tile.BaseTerrain) : new TipLine(tile.BaseTerrain, Kind: TipKind.Heading));
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
        if (seen && tile.Yields.Count > 0)
        {
            lines.AddRange(tile.Yields.Select(it => new TipLine(Amount(it), Kind: TipKind.Stat, Stat: it.Stat)));
            // C056: where each yield comes from, as the engine itemised it. Seen tiles only, like the yields themselves.
            if (tile.YieldBreakdown.Count > 0)
                foreach (var yield in tile.Yields) lines.AddRange(StatBreakdown(yield.Stat, tile.YieldBreakdown, yield.Value));
        }
        return lines;
    }

    /// <summary>
    /// C088: the city-site tooltip of a settler's hover: whether a city could stand there and why not, then what its work radius holds: yields, resources, natural wonders and
    /// how much of the radius is still unexplored. All of it is the engine's answer, which already holds only what the viewer may know.
    /// </summary>
    public static IReadOnlyList<TipLine> CitySite(CitySiteResult site)
    {
        var lines = new List<TipLine> { new(site.CanSettle ? "City site" : "Cannot found a city here", Kind: TipKind.Heading) };
        foreach (var problem in site.Problems)
            lines.Add(new TipLine(problem.Code switch
            {
                CitySiteProblemCodes.Water => "Cannot found a city on water",
                CitySiteProblemCodes.Impassable => "Cannot found a city on impassable terrain",
                CitySiteProblemCodes.TooCloseToCity => $"Too close to [{problem.Subject}]",
                CitySiteProblemCodes.ForeignTerritory => "In foreign territory",
                _ => problem.Code,
            }));
        lines.AddRange(site.Yields.Select(it => new TipLine(Amount(it), Kind: TipKind.Stat, Stat: it.Stat)));
        foreach (var resource in site.Resources) lines.Add(new TipLine($"{{{resource.Name}}} ({Number(resource.Amount)})"));
        foreach (var wonder in site.NaturalWonders) lines.Add(new TipLine(wonder));
        if (site.UnexploredTiles > 0) lines.Add(new TipLine($"[{Number(site.UnexploredTiles)}] unexplored tiles", Kind: TipKind.Footer));
        return lines;
    }

    /// <summary>The name, the cost and the ruleset's own short description of a unit (null for an unknown name).</summary>
    public static IReadOnlyList<TipLine>? Unit(Ruleset ruleset, string name)
    {
        if (!ruleset.Units.TryGetValue(name, out var unit)) return null;
        var lines = new List<TipLine> { new(name, Kind: TipKind.Heading) };
        if (unit.Cost > 0) lines.Add(new TipLine($"{{Cost}}: {Number(unit.Cost)}", Kind: TipKind.Stat, Stat: "Production"));
        string description = BaseUnitDescriptions.GetShortDescription(unit);
        if (description.Length != 0) lines.Add(new TipLine(description, Final: true));
        if (unit.RequiredTech != null) lines.Add(Requires(unit.RequiredTech));
        return lines;
    }

    /// <summary>The name, the cost and the ruleset's own multi-line short description of a building or wonder (null for an unknown name).</summary>
    public static IReadOnlyList<TipLine>? Building(Ruleset ruleset, string name)
    {
        if (!ruleset.Buildings.TryGetValue(name, out var building)) return null;
        var lines = new List<TipLine> { new(name, Kind: TipKind.Heading) };
        if (building.Cost > 0) lines.Add(new TipLine($"{{Cost}}: {Number(building.Cost)}", Kind: TipKind.Stat, Stat: "Production"));
        string description = BuildingDescriptions.GetShortDescription(building, multiline: true);
        if (description.Length != 0) lines.Add(new TipLine(description, Final: true));
        if (building.RequiredTech != null) lines.Add(Requires(building.RequiredTech));
        return lines;
    }

    /// <summary>
    /// The sources of a stat, one line each, then the total of the sources. Only sources that contribute to <paramref name="stat"/> are listed; the total is the sum of the listed values,
    /// so it equals the engine's figure whenever the engine's sources add up to it (a tile or city yield). C056: a caller that has the engine's own figure passes it as
    /// <paramref name="total"/> and that is shown instead of the sum, so a bonus the sources do not itemise cannot make the tooltip disagree with the screen.
    /// </summary>
    public static IReadOnlyList<TipLine> StatBreakdown(string stat, IReadOnlyList<StatSource> sources, float? total = null)
    {
        var lines = new List<TipLine> { new($"{{{stat}}}", Kind: TipKind.Section, Stat: stat) };
        float sum = 0f;
        foreach (var source in sources)
        {
            var amount = source.Stats.FirstOrDefault(it => it.Stat == stat);
            if (amount == null || amount.Value == 0f) continue;
            lines.Add(new TipLine($"{{{source.Source}}}: {Signed(amount.Value)}", Kind: TipKind.Source));
            sum += amount.Value;
        }
        lines.Add(new TipLine($"{{Total}}: {Signed(total ?? sum)}", Kind: TipKind.Source));
        return lines;
    }

    /// <summary>
    /// The lines as BBCode for the tooltip host: engine text kept, other text translated with <paramref name="translate"/>, brackets escaped. C076: a heading is bold and
    /// primary, stat lines are bold in their stat's colour and consecutive ones share a row, a breakdown's stat name is in its colour, the footer is small and muted.
    /// </summary>
    public static string ToBbcode(IEnumerable<TipLine> lines, Func<string, string> translate)
    {
        var text = new StringBuilder();
        TipKind? previous = null;
        foreach (var line in lines)
        {
            if (previous != null) text.Append(line.Kind == TipKind.Stat && previous == TipKind.Stat ? StatGap : "\n");
            previous = line.Kind;
            string shown = Escape(line.Final ? line.Text : translate(line.Text));
            text.Append(line.Kind switch
            {
                TipKind.Heading => $"[b][color={UiText.Hex(UiTokens.TextPrimary)}]{shown}[/color][/b]",
                TipKind.Stat or TipKind.Section => $"[b][color={UiText.Hex(PlaceholderPalette.ForStat(line.Stat ?? ""))}]{shown}[/color][/b]",
                TipKind.Source => $"[color={UiText.Hex(UiTokens.TextSecondary)}]{shown}[/color]",
                TipKind.Footer => $"[font_size={FooterSize}][color={UiText.Hex(UiTokens.TextMuted)}]{shown}[/color][/font_size]",
                _ => shown,
            });
        }
        return text.ToString();
    }

    private const string StatGap = "    ";

    /// <summary>Absolute font size in points: BBCode has no relative size, so the footer does not follow the UI scale (it is smaller than the body at any scale).</summary>
    private const int FooterSize = 12;

    /// <summary>The lines as plain text for a tooltip that does not render BBCode (a control's own tooltip): engine text kept, other text translated.</summary>
    public static string ToPlain(IEnumerable<TipLine> lines, Func<string, string> translate) =>
        string.Join('\n', lines.Select(line => line.Final ? line.Text : translate(line.Text)));

    /// <summary>BBCode treats a bracket as a tag; translated text may still contain one (a name such as "[x]"), so it is written as its entity.</summary>
    public static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    private static TipLine Requires(string tech) => new($"Requires [{tech}]", Kind: TipKind.Footer);

    private static string Amount(StatAmount amount) => $"{{{amount.Stat}}}: {Number(amount.Value)}";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Signed(float value) => (value >= 0f ? "+" : "") + Number(value);
}
