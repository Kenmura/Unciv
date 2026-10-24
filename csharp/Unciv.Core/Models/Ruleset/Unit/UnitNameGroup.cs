using System.Text;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/UnitNameGroup.kt
// The Wikipedia link's java.net.URLEncoder.encode is UnitNameGroup.UrlEncode; the collator and language come from CivilopediaContext.
public class UnitNameGroup : RulesetObject, ICivilopediaText
{
    public override string MakeLink() => $"Unit Names/{Name}";

    /// <summary>A list of names available for this historical figure group.</summary>
    public List<string> UnitNames { get; set; } = [];

    public UnitNameGroup Clone() => new()
    {
        // RulesetObject fields
        Name = Name,
        Uniques = Uniques,

        // UnitNameGroup fields
        UnitNames = UnitNames,
    };

    /// <summary>Retrieve a list of units that match this unit group name instance.</summary>
    public List<BaseUnit> GetUnits(IRulesetConstructionView ruleset, GameContext? gameContext = null)
    {
        gameContext ??= GameContext.IgnoreConditionalsState;
        var self = (IHasUniques)this;
        return ruleset.GetAllUnits().Where(unit =>
            ((IHasUniques)unit).GetMatchingUniques(UniqueType.OneTimeUnitGetsName).Any(unique =>
                // Match by using either the direct name, or a tag
                unique.Params[1] == Name || self.HasTagUnique(unique.Params[1], gameContext))).ToList();
    }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.EventChoice;

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var lines = new List<FormattedLine>();
        this.UniquesToCivilopediaTextLines(lines, context);

        // Units
        var units = GetUnits(ruleset);
        if (units.Count != 0)
        {
            lines.Add(new FormattedLine("Units", header: 4));
            foreach (var unit in units)
                lines.Add(new FormattedLine(unit.Name, link: unit.MakeLink()));
        }

        // Names
        if (UnitNames.Count != 0)
        {
            lines.Add(new FormattedLine());
            lines.Add(new FormattedLine("Unit Names", header: 4));
            var collator = context.GetCollator();
            var language = context.Language;
            foreach (var name in UnitNames.OrderBy(it => it, collator))
                lines.Add(new FormattedLine(name, link: $"https://{language}.wikipedia.org/w/index.php?search={UrlEncode(name.Tr())}"));
        }

        return lines;
    }

    /// <summary>Same output as <c>java.net.URLEncoder.encode(text, UTF-8)</c>: letters, digits and <c>.-*_</c> stay, space becomes '+', every other UTF-8 byte is %XX.</summary>
    private static string UrlEncode(string text)
    {
        var result = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            var c = (char)b;
            if (b < 128 && (char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '*' or '_')) result.Append(c);
            else if (c == ' ') result.Append('+');
            else result.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    public int GetSortGroup(Ruleset ruleset) =>
        GetUnits(ruleset).FirstOrDefault() is { } first ? ((ICivilopediaText)first).GetSortGroup(ruleset) : 100;

    public string? GetSubCategory(Ruleset ruleset) => GetUnits(ruleset).FirstOrDefault()?.Name ?? "Other";
}
