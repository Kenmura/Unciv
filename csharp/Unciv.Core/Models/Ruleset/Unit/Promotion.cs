using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/Promotion.kt
// Not ported yet, they need the translation and Civilopedia text types (T034): getDescription, makeLink, getCivilopediaTextLines, getSubCategory,
// getSortGroup.
public class Promotion : RulesetObject
{
    public List<string> Prerequisites { get; set; } = [];

    /// <summary>The json parser wouldn't agree to deserialize this as a list of UnitTypes. =(</summary>
    public List<string> UnitTypes { get; set; } = [];

    public List<int>? InnerColor { get; set; }
    public List<int>? OuterColor { get; set; }

    /// <summary>
    /// Used as **column** hint in the current PromotionPickerScreen.
    /// This is no longer a direct position, it is used to sort before an automatic distribution.
    /// -1 determines that the modder has not set a position
    /// </summary>
    public int Row { get; set; } = -1;

    /// <summary>
    /// Used as **row** hint in the current PromotionPickerScreen.
    /// This is no longer a direct position, it is used to sort before an automatic distribution.
    /// </summary>
    public int Column { get; set; }

    public EngineColor? GetInnerColorObject() => InnerColor == null ? null : ColorExtensions.ColorFromRGB(InnerColor);
    public EngineColor? GetOuterColorObject() => OuterColor == null ? null : ColorExtensions.ColorFromRGB(OuterColor);

    public Promotion Clone() => new()
    {
        // RulesetObject fields
        Name = Name,
        Uniques = Uniques,

        // Promotion fields
        Prerequisites = Prerequisites,
        UnitTypes = UnitTypes,
        Row = Row,
        Column = Column,
    };

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Promotion;

    public readonly record struct PromotionBaseNameAndLevel(string NameWithoutBrackets, int Level, string BasePromotionName);

    /// <summary>
    /// Split a promotion name into base and level, e.g. "Drill II" -> 2 to "Drill".
    /// Used by Portrait (where it only has the string, the Promotion object is forgotten) and PromotionPickerScreen.
    /// </summary>
    public static PromotionBaseNameAndLevel GetBaseNameAndLevel(string promotionName)
    {
        var nameWithoutBrackets = promotionName.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal);
        var level = nameWithoutBrackets.EndsWith(" I", StringComparison.Ordinal) ? 1
            : nameWithoutBrackets.EndsWith(" II", StringComparison.Ordinal) ? 2
            : nameWithoutBrackets.EndsWith(" III", StringComparison.Ordinal) ? 3
            : 0;
        // Kotlin dropLast(n) never throws for n > length
        var drop = level == 0 ? 0 : level + 1;
        return new PromotionBaseNameAndLevel(nameWithoutBrackets, level,
            nameWithoutBrackets[..Math.Max(0, nameWithoutBrackets.Length - drop)]);
    }
}
