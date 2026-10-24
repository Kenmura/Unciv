using System.Text.Json.Serialization;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.UI.Screens.Civilopedia;

// core/src/com/unciv/ui/screens/civilopediascreen/FormattedLine.kt
// Plain data (Non-goals #7): the Godot client renders it. Stripped, client rendering: render/renderExtraImage/renderIcon, IconDisplay, align (libGDX Align),
// the pixmap crop helpers, and the layout constants (minIconSize, iconPad, indentPad, indentOneAtNumIcons, linkImage, starImage).
// Seams: the Kotlin `UncivGame.Current` ruleset/gameInfo lookup is the explicit CivilopediaContext; the validator's image checks (ImageGetter,
// RulesetValidator.uncachedImageExists) are the IFormattedLineImageCheck argument of UnsupportedReasons.
// Kotlin `data class` equals/hashCode throw, and so do these. `copy()` is a record `with`-less copy: use the constructor.

/// <summary>
/// What <see cref="FormattedLine"/> needs from "the current game": the ruleset for automatic links, and the game when one is loaded.
/// <see cref="Collator"/> and <see cref="Language"/> stand in for <c>UncivGame.Current.settings.getCollatorFromLocale()</c> and <c>locale.language</c>
/// (sorting names for display, Wikipedia links). A null <see cref="Collator"/> sorts with the invariant culture's comparer.
/// </summary>
public sealed record CivilopediaContext(Ruleset Ruleset, IGameContextGameInfo? GameInfo = null, IComparer<string>? Collator = null, string Language = "en");

/// <summary>Image existence checks <see cref="FormattedLine.UnsupportedReasons"/> delegates to the client (atlas, ExtraImages folder, mod atlases).</summary>
public interface IFormattedLineImageCheck
{
    bool ExtraImageExists(string extraImage);
}

/// <summary>Represents a decorated text line with optional linking capability.</summary>
public sealed class FormattedLine
{
    /// <summary>Link types that can be used for <see cref="Link"/></summary>
    public enum LinkTypeEnum
    {
        None,
        /// <summary>Link points to a Civilopedia entry in the form <c>category/item</c></summary>
        Internal,
        /// <summary>Link opens as URL in external App - begins with <c>https://</c>, <c>http://</c> or <c>mailto:</c></summary>
        External,
    }

    /// <summary>Array of text sizes to translate the <see cref="Header"/> attribute</summary>
    public static readonly IReadOnlyList<int> HeaderSizes = [Constants.DefaultFontSize, 36, 32, 27, 24, 21, 15, 12, 9];

    /// <summary>Default color for text and icons</summary>
    public static readonly EngineColor DefaultColor = EngineColor.White;

    // Cache to quickly match Categories to names (Kotlin: single-entry cache keyed on ruleset identity, two unsynchronised statics).
    // Here ruleset and map are one immutable entry published through a single volatile write, so a reader never pairs a map with the wrong ruleset.
    private sealed record NameMapCache(Ruleset Ruleset, Dictionary<string, CivilopediaCategories> Map);

    private static volatile NameMapCache? NameMapCacheInstance;

    private readonly KotlinLazy<LinkTypeEnum> linkType;
    private readonly KotlinLazy<EngineColor> displayColor;

    /// <summary>Kotlin primary constructor. Keep all JSON-read attributes here.</summary>
    [JsonConstructor]
    public FormattedLine(
        string text = "",
        string link = "",
        string icon = "",
        string extraImage = "",
        float imageSize = float.NaN,
        int size = int.MinValue,
        int header = 0,
        int indent = 0,
        float padding = float.NaN,
        string color = "",
        bool separator = false,
        bool starred = false,
        bool centered = false,
        bool iconCrossed = false)
    {
        Text = text;
        Link = link;
        Icon = icon;
        ExtraImage = extraImage;
        ImageSize = imageSize;
        Size = size;
        Header = header;
        Indent = indent;
        Padding = padding;
        Color = color;
        Separator = separator;
        Starred = starred;
        Centered = centered;
        IconCrossed = iconCrossed;
        linkType = new KotlinLazy<LinkTypeEnum>(() => HasProtocol(Link) ? LinkTypeEnum.External : Link.Length != 0 ? LinkTypeEnum.Internal : LinkTypeEnum.None);
        displayColor = new KotlinLazy<EngineColor>(() => ParseColor() ?? DefaultColor);
    }

    /// <summary>Looks for linkable ruleset objects in <see cref="UniqueObject"/> parameters and returns a linked <see cref="FormattedLine"/> if successful, a plain one otherwise</summary>
    public FormattedLine(UniqueObject unique, CivilopediaContext context, int indent = 0)
        : this(unique.GetDisplayText(), GetUniqueLink(unique, context), indent: indent)
    {
    }

    /// <summary>Text to display.</summary>
    public string Text { get; }

    /// <summary>Create link: Line gets a 'Link' icon and is linked to either an Unciv object (format <c>category/entryname</c>) or an external URL.</summary>
    public string Link { get; }

    /// <summary>Display an Unciv object's icon inline but do not link (format <c>category/entryname</c>).</summary>
    public string Icon { get; }

    /// <summary>Display an Image instead of text, sized by <see cref="ImageSize"/>. Can be an image path or the name of a png or jpg in ExtraImages.</summary>
    public string ExtraImage { get; }

    /// <summary>Width of the <see cref="ExtraImage"/>, height is calculated preserving aspect ratio. Defaults to available width.</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] // default is NaN, which System.Text.Json would otherwise refuse to write
    public float ImageSize { get; }

    /// <summary>Text size, defaults to <see cref="Constants.DefaultFontSize"/>. Use size or <see cref="Header"/> but not both.</summary>
    public int Size { get; }

    /// <summary>Header level. 1 means double text size and decreases from there.</summary>
    public int Header { get; }

    /// <summary>Indentation: 0 = text will follow icons with a little padding, 1 = aligned to a little more than 3 icons, each step above that adds 30f.</summary>
    public int Indent { get; }

    /// <summary>Defines vertical padding between rows, defaults to 5f.</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public float Padding { get; }

    /// <summary>Sets text color, accepts 6/3-digit web colors (e.g. #FFA040) or names as defined by <see cref="EngineColors"/>.</summary>
    public string Color { get; }

    /// <summary>Renders a separator line instead of text. Can be combined only with <see cref="Color"/> and <see cref="Size"/> (line width, default 2)</summary>
    public bool Separator { get; }

    /// <summary>Decorates text with a star icon - if set, it receives the <see cref="Color"/> instead of the text.</summary>
    public bool Starred { get; }

    /// <summary>Centers the line (independent of icons and indent)</summary>
    public bool Centered { get; }

    /// <summary>Paint a red X over the <see cref="Icon"/> or <see cref="Link"/> image</summary>
    public bool IconCrossed { get; }

    /// <summary>The type of the <see cref="Link"/>'s destination</summary>
    [JsonIgnore]
    public LinkTypeEnum LinkType => linkType.Value;

    /// <summary>Retrieves the parsed color corresponding to the <see cref="Color"/> property (String)</summary>
    [JsonIgnore]
    public EngineColor DisplayColor => displayColor.Value;

    /// <summary>The icon the client should show: <see cref="Icon"/>, else the internal <see cref="Link"/>, else empty (Kotlin private iconToDisplay)</summary>
    [JsonIgnore]
    public string IconToDisplay => Icon.Length != 0 ? Icon : LinkType == LinkTypeEnum.Internal ? Link : "";

    /// <summary>The text the client should show: <see cref="Text"/>, or the URL for an external link without text (Kotlin private textToDisplay)</summary>
    [JsonIgnore]
    public string TextToDisplay => Text.Length == 0 && LinkType == LinkTypeEnum.External ? Link : Text;

    /// <summary>Returns true if this formatted line will not display anything</summary>
    public bool IsEmpty() => Text.Length == 0 && ExtraImage.Length == 0 && !Starred && Icon.Length == 0 && Link.Length == 0 && !Separator;

    /// <summary>Self-check to support the RulesetValidator</summary>
    /// <returns>0 or more Strings naming problems - all occurrences get the same severity upstream</returns>
    public IEnumerable<string> UnsupportedReasons(IFormattedLineImageCheck imageCheck)
    {
        if (HasNormalContent() && Separator)
            yield return "separator and other options are incompatible";
        if (Link.Length != 0 && !(IsValidInternalLink(Link) || HasProtocol(Link)))
            yield return "link is invalid - use internal category/name format or a https:// URL";
        // Sic: Kotlin tests `link` here, not `icon`
        if (Icon.Length != 0 && !IsValidInternalLink(Link))
            yield return "icon is invalid - use internal category/name format";
        if (Header != 0 && Size != int.MinValue)
            yield return "use either size or header but not both";
        if (Header < 0 || Header >= HeaderSizes.Count)
            yield return $"header should be in the range 1..{HeaderSizes.Count - 1}"; // Not mentioning 0 is valid too - same as omitting it
        if (Size != int.MinValue && (Size < 1 || Size > 100)) // arbitrary
            yield return "size is out of sensible range";
        if (Indent < 0 || Indent > 100) // arbitrary
            yield return "indent is out of sensible range";
        if (Color.Length != 0)
        {
            if (ParseColor() == null)
                yield return $"unknown color \"{Color}\"";
            else if (Text.Length == 0 && TextToDisplay.Length == 0 && !Starred && !Separator)
                yield return "color set but nothing to apply it to";
        }
        if (IconCrossed && Link.Length == 0 && Icon.Length == 0)
            yield return "iconCrossed set without icon or link";
        if (ExtraImage.Length != 0)
        {
            if (HasNormalContent() || Separator)
                // not checking centered or padding - these may be implementable
                yield return "extraImage and other options except imageSize are incompatible";
            if (!imageCheck.ExtraImageExists(ExtraImage))
                yield return "extraImage not found as either atlas texture or in ExtraImages folder";
        }
        if (!float.IsNaN(ImageSize) && ExtraImage.Length == 0)
            yield return "imageSize is only valid for an extraImage";
    }

    // Not one of the exceptions - empty, separator or extraImage. For validation, independent of IsEmpty which looks for visible content
    private bool HasNormalContent() =>
        Text.Length != 0 || Link.Length != 0 || Icon.Length != 0 || Color.Length != 0 || Size != int.MinValue || Header != 0 || Starred;

    // Kotlin Regex("""^[^/]+/[^/]+$""").matches: exactly one '/', with something (anything, newlines included) on both sides
    private static bool IsValidInternalLink(string link)
    {
        var slash = link.IndexOf('/');
        return slash > 0 && slash < link.Length - 1 && link.IndexOf('/', slash + 1) < 0;
    }

    private static bool HasProtocol(string s) =>
        s.StartsWith("http://", StringComparison.Ordinal) || s.StartsWith("https://", StringComparison.Ordinal) || s.StartsWith("mailto:", StringComparison.Ordinal);

    private static bool IsHex(string s, int start, int length)
    {
        if (length == 0) return false;
        if (start + length > s.Length) return false;
        // Kotlin Char.isDigit() is Unicode-aware, hence char.IsDigit rather than an ASCII range
        return s.Substring(start, length).All(it => char.IsDigit(it) || it is >= 'a' and <= 'f' || it is >= 'A' and <= 'F');
    }

    /// <summary>Parse a json-supplied color string, null if empty or unknown.</summary>
    private EngineColor? ParseColor()
    {
        if (Color.Length == 0) return null;
        if (Color[0] == '#' && IsHex(Color, 1, 3))
        {
            if (IsHex(Color, 1, 6)) return EngineColors.ValueOf(Color);
            var hex6 = new string([Color[1], Color[1], Color[2], Color[2], Color[3], Color[3]]);
            return EngineColors.ValueOf(hex6);
        }
        return EngineColors.Get(Color.ToUpperInvariant());
    }

    private static string GetUniqueLink(UniqueObject unique, CivilopediaContext context)
    {
        var ruleSet = context.Ruleset;
        var explicitLinks = unique.GetModifiers(UniqueType.CivilopediaLink);
        if (explicitLinks.Count != 0)
            return explicitLinks[0].Params[0];
        var cache = NameMapCacheInstance;
        if (cache == null || !ReferenceEquals(cache.Ruleset, ruleSet))
            NameMapCacheInstance = cache = new NameMapCache(ruleSet, InitNamesCategoryMap(context));
        foreach (var parameter in unique.Params.Concat(unique.Modifiers.SelectMany(it => it.Params)))
        {
            if (!cache.Map.TryGetValue(parameter, out var category)) continue;
            return category + "/" + parameter;
        }
        return "";
    }

    private static Dictionary<string, CivilopediaCategories> InitNamesCategoryMap(CivilopediaContext context)
    {
        var ruleSet = context.Ruleset;
        // order these with the categories that should take precedence in case of name conflicts (e.g. Railroad) _last_
        var allObjectMaps = new (CivilopediaCategories Category, IEnumerable<KeyValuePair<string, IHasUniques>> Objects)[]
        {
            (CivilopediaCategories.Tutorial, Objs(ruleSet.Tutorials)),
            (CivilopediaCategories.Belief, Objs(ruleSet.Beliefs)),
            (CivilopediaCategories.Difficulty, Objs(ruleSet.Difficulties)),
            (CivilopediaCategories.Promotion, Objs(ruleSet.UnitPromotions)),
            (CivilopediaCategories.Policy, Objs(ruleSet.Policies)),
            (CivilopediaCategories.Terrain, Objs(ruleSet.Terrains)),
            (CivilopediaCategories.Improvement, Objs(ruleSet.TileImprovements)),
            (CivilopediaCategories.Resource, Objs(ruleSet.TileResources)),
            (CivilopediaCategories.Nation, Objs(ruleSet.Nations)),
            (CivilopediaCategories.UnitType, Objs(ruleSet.UnitTypes)),
            (CivilopediaCategories.Unit, Objs(ruleSet.Units)),
            (CivilopediaCategories.Technology, Objs(ruleSet.Technologies)),
            (CivilopediaCategories.Building, Objs(ruleSet.Buildings.Where(it => !it.Value.IsAnyWonder()))),
            (CivilopediaCategories.Wonder, Objs(ruleSet.Buildings.Where(it => it.Value.IsAnyWonder()))),
            (CivilopediaCategories.UnitNameGroup, Objs(ruleSet.UnitNameGroups)),
        };
        var result = new Dictionary<string, CivilopediaCategories>();
        foreach (var (category, objects) in allObjectMaps)
            foreach (var (key, value) in objects)
                if (!value.IsHiddenFromCivilopedia(context.GameInfo, ruleSet))
                    result[key] = category; // later categories win, like toMap
        return result;
    }

    private static IEnumerable<KeyValuePair<string, IHasUniques>> Objs<T>(IEnumerable<KeyValuePair<string, T>> map) where T : IHasUniques =>
        map.Select(it => new KeyValuePair<string, IHasUniques>(it.Key, it.Value));

    /// <summary>Debug visualization only</summary>
    public override string ToString()
    {
        if (IsEmpty()) return "(empty)";
        if (Separator) return "(separator)";
        if (ExtraImage.Length != 0) return $"(extraImage='{ExtraImage}')";
        if (Header > 0) return $"(header={Header})'{Text}'";
        if (LinkType == LinkTypeEnum.None) return $"'{Text}'";
        return $"'{Text}'->{Link}";
    }

    public override int GetHashCode() => throw new InvalidOperationException("FormattedLine isn't supposed to be compared or used as hash key");

    public override bool Equals(object? obj) => throw new InvalidOperationException("FormattedLine isn't supposed to be compared or used as hash key");
}
