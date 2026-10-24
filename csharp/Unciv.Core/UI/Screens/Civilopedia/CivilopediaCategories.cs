using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;

namespace Unciv.UI.Screens.Civilopedia;

// core/src/com/unciv/ui/screens/civilopediascreen/CivilopediaCategories.kt
// Stripped, client concerns: getImage (CivilopediaImageGetters, libGDX Actor) and binding (KeyboardBinding). The Godot client maps a category to its image/key.
// The enum member order is the order in the Civilopedia top bar.

/// <summary>Keys for Civilopedia "pages" (categories). Names are singular on purpose - a "link" allows both key and label.</summary>
public enum CivilopediaCategories
{
    Building,
    Wonder,
    Resource,
    Terrain,
    Improvement,
    Unit,
    UnitType,
    Nation,
    Personality,
    Technology,
    Promotion,
    Policy,
    Belief,
    Tutorial,
    Victory,
    UnitNameGroup,
    Difficulty,
    Era,
    Speed,
}

public static class CivilopediaCategoriesExtensions
{
    /// <summary>Translatable caption for the Civilopedia button</summary>
    public static string Label(this CivilopediaCategories category) => category switch
    {
        CivilopediaCategories.Building => "Buildings",
        CivilopediaCategories.Wonder => "Wonders",
        CivilopediaCategories.Resource => "Resources",
        CivilopediaCategories.Terrain => "Terrains",
        CivilopediaCategories.Improvement => "Tile Improvements",
        CivilopediaCategories.Unit => "Units",
        CivilopediaCategories.UnitType => "Unit types",
        CivilopediaCategories.Nation => "Nations",
        CivilopediaCategories.Personality => "Personalities",
        CivilopediaCategories.Technology => "Technologies",
        CivilopediaCategories.Promotion => "Promotions",
        CivilopediaCategories.Policy => "Policies",
        CivilopediaCategories.Belief => "Religions and Beliefs",
        CivilopediaCategories.Tutorial => "Tutorials",
        CivilopediaCategories.Victory => "Victory Types",
        CivilopediaCategories.UnitNameGroup => "Unit Names",
        CivilopediaCategories.Difficulty => "Difficulty levels",
        CivilopediaCategories.Era => "Eras",
        CivilopediaCategories.Speed => "Speeds",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static string HeaderIcon(this CivilopediaCategories category) => category switch
    {
        CivilopediaCategories.Building => "OtherIcons/Cities",
        CivilopediaCategories.Wonder => "OtherIcons/Wonders",
        CivilopediaCategories.Resource => "OtherIcons/Resources",
        CivilopediaCategories.Terrain => "OtherIcons/Terrains",
        CivilopediaCategories.Improvement => "OtherIcons/Improvements",
        CivilopediaCategories.Unit => "OtherIcons/Shield",
        CivilopediaCategories.UnitType => "UnitTypeIcons/UnitTypes",
        CivilopediaCategories.Nation => "OtherIcons/Nations",
        CivilopediaCategories.Personality => "OtherIcons/Personality",
        CivilopediaCategories.Technology => "TechIcons/Philosophy",
        CivilopediaCategories.Promotion => "UnitPromotionIcons/Mobility",
        CivilopediaCategories.Policy => "PolicyIcons/Constitution",
        CivilopediaCategories.Belief => "ReligionIcons/Religion",
        CivilopediaCategories.Tutorial => "OtherIcons/ExclamationMark",
        CivilopediaCategories.Victory => "OtherIcons/Score",
        CivilopediaCategories.UnitNameGroup => "OtherIcons/UnitNameGroups",
        CivilopediaCategories.Difficulty => "OtherIcons/Quickstart",
        CivilopediaCategories.Era => "OtherIcons/Tyrannosaurus",
        CivilopediaCategories.Speed => "OtherIcons/Timer",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>
    /// The entries of the category. Not ported: for <see cref="CivilopediaCategories.Belief"/> the two virtual entries
    /// <c>Belief.getCivilopediaBeliefsEntry/getCivilopediaReligionEntry</c> that Kotlin appends after the beliefs.
    /// </summary>
    public static IReadOnlyCollection<ICivilopediaText> GetCategoryIterator(this CivilopediaCategories category, Ruleset ruleset, IGameContextGameInfo? gameInfo) => category switch
    {
        CivilopediaCategories.Building => ruleset.Buildings.Values.Where(it => !it.IsAnyWonder()).ToList(),
        CivilopediaCategories.Wonder => ruleset.Buildings.Values.Where(it => it.IsAnyWonder()).ToList(),
        CivilopediaCategories.Resource => ruleset.TileResources.Values.ToList(),
        CivilopediaCategories.Terrain => ruleset.Terrains.Values.ToList(),
        CivilopediaCategories.Improvement => ruleset.TileImprovements.Values.ToList(),
        CivilopediaCategories.Unit => ruleset.Units.Values.ToList(),
        CivilopediaCategories.UnitType => UnitType.GetCivilopediaIterator(ruleset),
        CivilopediaCategories.Nation => ruleset.Nations.Values.Where(it => !it.IsSpectator).ToList(),
        // The Personalities file contains unused chaff
        CivilopediaCategories.Personality => ruleset.Personalities.Values
            .Where(personality => ruleset.Nations.Values.Any(it => it.Personality == personality.Name)).ToList(),
        CivilopediaCategories.Technology => ruleset.Technologies.Values.ToList(),
        CivilopediaCategories.Promotion => ruleset.UnitPromotions.Values.ToList(),
        CivilopediaCategories.Policy => ruleset.Policies.Values.ToList(),
        CivilopediaCategories.Belief => ruleset.Beliefs.Values.ToList(),
        CivilopediaCategories.Tutorial => GetCivilopediaTutorials(ruleset),
        CivilopediaCategories.Victory => ruleset.Victories.Values
            // Only display active victory types
            .Where(it => gameInfo?.GameParameters?.VictoryTypes?.Contains(it.Name) ?? true).ToList(),
        CivilopediaCategories.UnitNameGroup => ruleset.UnitNameGroups.Values
            .Where(it => it.UnitNames.Count != 0 && it.GetUnits(ruleset).Count != 0).ToList(),
        CivilopediaCategories.Difficulty => ruleset.Difficulties.Values.ToList(),
        CivilopediaCategories.Era => ruleset.Eras.Values.ToList(),
        CivilopediaCategories.Speed => ruleset.Speeds.Values.ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary><c>CivilopediaCategories.fromLink</c>: by member name, then by label.</summary>
    public static CivilopediaCategories? FromLink(string name)
    {
        foreach (var category in Enum.GetValues<CivilopediaCategories>())
            if (category.ToString() == name) return category;
        foreach (var category in Enum.GetValues<CivilopediaCategories>())
            if (category.Label() == name) return category;
        return null;
    }

    /// <summary>Get all Tutorials to be displayed in the Civilopedia (hiding is done later, however)</summary>
    private static List<ICivilopediaText> GetCivilopediaTutorials(Ruleset ruleset)
    {
        // Note: Not the game's merged global uniques, because this is for pedia display
        var result = ruleset.Tutorials.Values.Cast<ICivilopediaText>().ToList();
        // Add entry for Global Uniques only if they have anything interesting
        if (ruleset.GlobalUniques.HasUniques()) result.Add(ruleset.GlobalUniques);
        return result;
    }
}
