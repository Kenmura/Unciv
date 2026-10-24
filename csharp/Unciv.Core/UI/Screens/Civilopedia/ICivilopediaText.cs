using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;

namespace Unciv.UI.Screens.Civilopedia;

// core/src/com/unciv/ui/screens/civilopediascreen/ICivilopediaText.kt and SimpleCivilopediaText.kt
// Stripped, client rendering: renderCivilopediaText (MarkupRenderer builds a libGDX Table).
// Default members are interface default methods (call them through the interface type, as with IHasUniques).

/// <summary>
/// Addon common to most ruleset game objects managing civilopedia display.
/// 1. Let the <see cref="Ruleset"/> object implement this. 2. Add <c>"civilopediaText": ["",…],</c> in the json for these objects.
/// 3. Optionally override <see cref="GetCivilopediaTextHeader"/>, <see cref="GetCivilopediaTextLines"/> or <see cref="AssembleCivilopediaText"/>.
/// </summary>
public interface ICivilopediaText
{
    /// <summary>List of strings supporting simple formatting rules (<see cref="FormattedLine"/>) that the Civilopedia can render.</summary>
    List<FormattedLine> CivilopediaText { get; set; }

    /// <summary>Generate header line from object metadata. Default takes the name and renders it in 150% normal font size with an icon from <see cref="MakeLink"/>.</summary>
    FormattedLine? GetCivilopediaTextHeader() =>
        this is INamed named ? new FormattedLine(named.Name, icon: MakeLink(), header: 2) : null;

    /// <summary>
    /// Generate automatic lines from object metadata. Must not rely on a game being loaded, the info is about the ruleset.
    /// Default implementation is empty. <paramref name="context"/> carries the ruleset (and the game if loaded) that the Kotlin version read from <c>UncivGame.Current</c>
    /// when it built <see cref="FormattedLine"/>s from Uniques.
    /// </summary>
    List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) => [];

    /// <summary>
    /// Assemble json-supplied lines with automatically generated ones. The default implementation will insert
    /// <see cref="GetCivilopediaTextLines"/> before the first linked <see cref="CivilopediaText"/> line and <see cref="GetCivilopediaTextHeader"/> on top.
    /// </summary>
    ICivilopediaText AssembleCivilopediaText(CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var newLines = new List<FormattedLine>();
        var middleDone = false;
        var outerNotEmpty = false;
        var header = GetCivilopediaTextHeader();
        if (header != null)
        {
            newLines.Add(header);
            newLines.Add(new FormattedLine(separator: true));
        }
        foreach (var next in CivilopediaText)
        {
            if (!middleDone && !next.IsEmpty() && next.LinkType != FormattedLine.LinkTypeEnum.None)
            {
                middleDone = true;
                if (outerNotEmpty) newLines.Add(new FormattedLine());
                newLines.AddRange(GetCivilopediaTextLines(context));
                newLines.Add(new FormattedLine());
            }
            outerNotEmpty = true;
            newLines.Add(next);
        }
        if (!middleDone)
        {
            if (outerNotEmpty) newLines.Add(new FormattedLine());
            newLines.AddRange(GetCivilopediaTextLines(context));
        }
        if (this is IRulesetObject rulesetObject && ruleset.Mods.Count > 1 && rulesetObject.OriginRuleset.Length != 0)
        {
            newLines.Add(new FormattedLine());
            newLines.Add(new FormattedLine($"Mod: [{rulesetObject.OriginRuleset}]", starred: true, color: "#daa520"));
        }
        return new SimpleCivilopediaText(newLines);
    }

    /// <summary>
    /// Create the correct string for a Civilopedia link: <c>category/entryname</c> where <c>category</c> must correspond exactly to either name or label of
    /// the correct <see cref="CivilopediaCategories"/> member, and <c>entryname</c> equals the ruleset object name.
    /// </summary>
    string MakeLink();

    /// <summary>Overrides alphabetical sorting in Civilopedia</summary>
    int GetSortGroup(Ruleset ruleset) => 0;

    /// <summary>
    /// This can be overridden to add "Subcategory" labels between groups of Civilopedia entries.
    /// A new label will be inserted every time this changes and is not null; therefore, in most cases it will be necessary to override <see cref="GetSortGroup"/> as well.
    /// </summary>
    string? GetSubCategory(Ruleset ruleset) => null;

    /// <summary>Overrides Icon used for Civilopedia entry list (where you select the instance). This will still be passed to the category-specific image getter.</summary>
    string GetIconName() => this is INamed named ? named.Name : "";
}

/// <summary>Storage class for instantiation of the simplest form containing only the lines collection</summary>
public class SimpleCivilopediaText(List<FormattedLine> civilopediaText) : ICivilopediaText
{
    public SimpleCivilopediaText(IEnumerable<string> strings)
        : this(strings.Select(it => new FormattedLine(it)).ToList())
    {
    }

    public SimpleCivilopediaText(IEnumerable<FormattedLine> first, IEnumerable<string> strings)
        : this(first.Concat(strings.Select(it => new FormattedLine(it))).ToList())
    {
    }

    public List<FormattedLine> CivilopediaText { get; set; } = civilopediaText;

    public string MakeLink() => "";
}
