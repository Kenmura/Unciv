using System.Globalization;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Assets;

/// <summary>One top-bar button of the Civilopedia: a category that has at least one visible entry.</summary>
public sealed record PediaCategory(CivilopediaCategories Category, string Label, int Count);

/// <summary>One entry of a category's list. <see cref="Link"/> is the Civilopedia link form <c>Category/Name</c>; <see cref="SubCategory"/> is the group heading the engine gives some entries (or null).</summary>
public sealed record PediaEntry(CivilopediaCategories Category, string Name, string Link, string? SubCategory, AssetKey? Icon);

/// <summary>
/// One line of an entry page, taken from the engine's <see cref="FormattedLine"/>. <see cref="Text"/> is an untranslated key or composed template (the client calls <c>Tr()</c>);
/// <see cref="Color"/> is a <c>#rrggbb</c> value; <see cref="Link"/> is the internal <c>Category/Name</c> target or an external URL (told apart by <see cref="IsExternal"/>).
/// </summary>
public sealed record PediaLine(string Text, string Link, bool IsExternal, string Icon, int Header, int Size, int Indent, string Color, bool Separator, bool Starred, bool Centered, string ExtraImage);

public sealed record PediaPage(PediaEntry Entry, IReadOnlyList<PediaLine> Lines);

/// <summary>C076: one chip of an entry's header card: the stat whose icon it carries and its value ("40" production).</summary>
public sealed record PediaChip(string Stat, string Text);

/// <summary>C076: the header card of an entry page: the name, the category it belongs to, its icon and the cost chips.</summary>
public sealed record PediaHeader(string Name, string CategoryLabel, AssetKey? Icon, IReadOnlyList<PediaChip> Chips);

/// <summary>
/// C027: the Civilopedia as plain data, Godot-free so it is unit-tested. Categories, entries, sort order and every page's lines come from the engine's own
/// <see cref="CivilopediaCategories"/> iterators and <see cref="ICivilopediaText.AssembleCivilopediaText"/> (the ruleset objects' <c>getDescription</c>/unique text); this class
/// hides what the engine hides (<c>HiddenFromCivilopedia</c>), orders entries (engine sort group, then the translated name, ordinal), resolves links and words the
/// <see cref="FormattedLine"/> as BBCode. The client has no GameInfo, so the pedia is the ruleset's view, as Kotlin's pedia opened from the main menu.
/// </summary>
public static class PediaModel
{
    /// <summary>Ordinal, so the order of a page never depends on the machine's culture.</summary>
    private static CivilopediaContext Context(Ruleset ruleset) => new(ruleset, null, StringComparer.Ordinal);

    /// <summary>The categories with at least one visible entry, in the engine's top-bar order.</summary>
    public static IReadOnlyList<PediaCategory> Categories(Ruleset ruleset)
    {
        var result = new List<PediaCategory>();
        foreach (var category in Enum.GetValues<CivilopediaCategories>())
        {
            int count = Visible(ruleset, category).Count;
            if (count > 0) result.Add(new PediaCategory(category, category.Label(), count));
        }
        return result;
    }

    /// <summary>The visible entries of a category, sorted by the engine's sort group and then by translated name; sub-category headings are filled where the engine gives them.</summary>
    public static IReadOnlyList<PediaEntry> Entries(Ruleset ruleset, CivilopediaCategories category) =>
        Visible(ruleset, category)
            .Select(it => (Entry: it, Name: NameOf(it), Group: it.GetSortGroup(ruleset)))
            .OrderBy(it => it.Group)
            .ThenBy(it => it.Name.Tr(), StringComparer.Ordinal)
            .Select(it => MakeEntry(ruleset, category, it.Entry, it.Name))
            .ToList();

    /// <summary>The most entries a search lists (the list stays short enough to read; refine the text to narrow it).</summary>
    public const int MaxSearchResults = 50;

    /// <summary>
    /// C055: the visible entries of every category whose translated name contains <paramref name="query"/> (case-insensitive, ordinal, surrounding spaces ignored), best first:
    /// names that start with the query, then the others; within a group by category order and then by name. An empty query finds nothing. At most <see cref="MaxSearchResults"/>.
    /// </summary>
    public static IReadOnlyList<PediaEntry> Search(Ruleset ruleset, string? query)
    {
        string needle = (query ?? "").Trim();
        if (needle.Length == 0) return [];
        var hits = new List<(PediaEntry Entry, string Name, bool Prefix)>();
        foreach (var category in Enum.GetValues<CivilopediaCategories>())
        {
            foreach (var entry in Entries(ruleset, category))
            {
                string name = entry.Name.Tr();
                if (name.Contains(needle, StringComparison.OrdinalIgnoreCase)) hits.Add((entry, name, name.StartsWith(needle, StringComparison.OrdinalIgnoreCase)));
            }
        }
        return hits.OrderByDescending(it => it.Prefix).ThenBy(it => it.Entry.Category).ThenBy(it => it.Name, StringComparer.Ordinal)
            .Take(MaxSearchResults).Select(it => it.Entry).ToList();
    }

    /// <summary>The page of one entry (null when the category has no visible entry of that name): the engine's assembled lines.</summary>
    public static PediaPage? Page(Ruleset ruleset, CivilopediaCategories category, string name)
    {
        var entry = Visible(ruleset, category).FirstOrDefault(it => NameOf(it) == name);
        if (entry == null) return null;
        var lines = entry.AssembleCivilopediaText(Context(ruleset)).CivilopediaText.Select(ToLine).ToList();
        return new PediaPage(MakeEntry(ruleset, category, entry, name), lines);
    }

    /// <summary>C076: the header card of an entry. Units, buildings and wonders carry their production cost, a technology its science cost; other entries have no chips.</summary>
    public static PediaHeader Header(Ruleset ruleset, PediaEntry entry)
    {
        var chips = new List<PediaChip>();
        switch (entry.Category)
        {
            case CivilopediaCategories.Unit when ruleset.Units.TryGetValue(entry.Name, out var unit) && unit.Cost > 0:
                chips.Add(new PediaChip("Production", Number(unit.Cost)));
                break;
            case CivilopediaCategories.Building or CivilopediaCategories.Wonder when ruleset.Buildings.TryGetValue(entry.Name, out var building) && building.Cost > 0:
                chips.Add(new PediaChip("Production", Number(building.Cost)));
                break;
            case CivilopediaCategories.Technology when ruleset.Technologies.TryGetValue(entry.Name, out var tech) && tech.Cost > 0:
                chips.Add(new PediaChip("Science", Number(tech.Cost)));
                break;
        }
        return new PediaHeader(entry.Name, entry.Category.Label(), entry.Icon, chips);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The page a link points to (<c>Unit/Warrior</c>, category by name or label); null for an unknown category or entry, and for an external URL.</summary>
    public static PediaPage? Resolve(Ruleset ruleset, string link)
    {
        int slash = link.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == link.Length - 1) return null;
        var category = CivilopediaCategoriesExtensions.FromLink(link[..slash]);
        return category == null ? null : Page(ruleset, category.Value, link[(slash + 1)..]);
    }

    /// <summary>C076: the interface icon of a category, for the sidebar and for an entry that has no icon of its own.</summary>
    public static AssetKey CategoryIcon(CivilopediaCategories category) => AssetKey.Of(RulesetAssetKeys.UiIcon, category switch
    {
        CivilopediaCategories.Building => "Capital",
        CivilopediaCategories.Wonder => "Wonders",
        CivilopediaCategories.Resource => "Resources",
        CivilopediaCategories.Terrain => "Terrains",
        CivilopediaCategories.Improvement => "Improvements",
        CivilopediaCategories.Unit => "Shield",
        CivilopediaCategories.UnitType => "Hexagon",
        CivilopediaCategories.Nation => "Nations",
        CivilopediaCategories.Personality => "Chat",
        CivilopediaCategories.Technology => "Increase",
        CivilopediaCategories.Promotion => "Star",
        CivilopediaCategories.Policy => "Policies",
        CivilopediaCategories.Belief => "Link",
        CivilopediaCategories.Tutorial => "Question",
        CivilopediaCategories.Victory => "Score",
        CivilopediaCategories.UnitNameGroup => "Pencil",
        CivilopediaCategories.Difficulty => "ExclamationMark",
        CivilopediaCategories.Era => "HexagonOutline",
        _ => "ForwardArrow",
    });

    /// <summary>The icon an entry list shows next to the name, for the categories the asset registry has icons for.</summary>
    public static AssetKey? IconFor(CivilopediaCategories category, string name) => category switch
    {
        CivilopediaCategories.Unit => AssetKey.Of(RulesetAssetKeys.UnitIcon, name),
        CivilopediaCategories.Building or CivilopediaCategories.Wonder => AssetKey.Of(RulesetAssetKeys.BuildingIcon, name),
        CivilopediaCategories.Resource => AssetKey.Of(RulesetAssetKeys.ResourceIcon, name),
        CivilopediaCategories.Improvement => AssetKey.Of(RulesetAssetKeys.ImprovementIcon, name),
        CivilopediaCategories.Technology => AssetKey.Of(RulesetAssetKeys.TechIcon, name),
        CivilopediaCategories.Policy => AssetKey.Of(RulesetAssetKeys.PolicyIcon, name),
        CivilopediaCategories.Promotion => AssetKey.Of(RulesetAssetKeys.PromotionIcon, name),
        _ => null,
    };

    /// <summary>
    /// A line as BBCode for a RichTextLabel. <paramref name="translate"/> turns the line's text into the player's language. An internal link becomes <c>[url=Category/Name]</c>,
    /// an external one <c>[url=https://...]</c>; the client opens the former in the pedia and ignores the latter. C076: a separator is a small gap, not a row of dashes, and a link
    /// the page did not colour itself is drawn in the info colour.
    /// </summary>
    public static string ToBbcode(PediaLine line, Func<string, string> translate)
    {
        if (line.Separator) return "[font_size=" + SeparatorSize.ToString(CultureInfo.InvariantCulture) + "] [/font_size]";
        string text = TooltipModel.Escape(translate(line.Text.Length == 0 && line.IsExternal ? line.Link : line.Text));
        if (line.Starred) text = "* " + text;
        if (line.Link.Length != 0 && text.Length != 0) text = "[url=" + line.Link + "]" + text + "[/url]";
        string colour = line.Color == "#ffffff" && line.Link.Length != 0 && !line.IsExternal ? UiText.Hex(UiTokens.Info).ToLowerInvariant() : line.Color;
        if (colour != "#ffffff" || line.Starred) text = "[color=" + colour + "]" + text + "[/color]";
        int size = line.Header > 0 && line.Header < FormattedLine.HeaderSizes.Count ? FormattedLine.HeaderSizes[line.Header] : line.Size;
        if (size > 0 && size != Constants.DefaultFontSize) text = "[font_size=" + size.ToString(CultureInfo.InvariantCulture) + "]" + text + "[/font_size]";
        if (line.Header > 0) text = "[b]" + text + "[/b]";
        if (line.Indent > 0) text = new string(' ', line.Indent * 4) + text;
        return line.Centered ? "[center]" + text + "[/center]" : text;
    }

    private const int SeparatorSize = 6;

    private static List<ICivilopediaText> Visible(Ruleset ruleset, CivilopediaCategories category) =>
        category.GetCategoryIterator(ruleset, null, Context(ruleset))
            .Where(it => it is not IHasUniques unique || !unique.IsHiddenFromCivilopedia(null, ruleset))
            .Where(it => NameOf(it).Length != 0)
            .ToList();

    private static string NameOf(ICivilopediaText entry) => entry is INamed named ? named.Name : "";

    private static PediaEntry MakeEntry(Ruleset ruleset, CivilopediaCategories category, ICivilopediaText entry, string name) =>
        new(category, name, category + "/" + name, entry.GetSubCategory(ruleset), IconFor(category, name));

    private static PediaLine ToLine(FormattedLine line) => new(
        line.Text, line.Link, line.LinkType == FormattedLine.LinkTypeEnum.External, line.Icon, line.Header,
        line.Size == int.MinValue ? 0 : line.Size, line.Indent, Hex(line.DisplayColor), line.Separator, line.Starred, line.Centered, line.ExtraImage);

    private static string Hex(EngineColor color) => string.Create(CultureInfo.InvariantCulture,
        $"#{(int)(color.R * 255f):x2}{(int)(color.G * 255f):x2}{(int)(color.B * 255f):x2}");
}
