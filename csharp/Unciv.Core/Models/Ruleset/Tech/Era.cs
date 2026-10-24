using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;

namespace Unciv.Models.Ruleset.Tech;

// core/src/com/unciv/models/ruleset/tech/Era.kt
// Not ported yet, they need Ruleset, GameInfo and the Civilopedia text types (T034, T046): makeLink, getCivilopediaTextHeader/Lines, getSortGroup,
// getEraGatedObjects, getStartingUnits, matchesFilter/matchesSingleFilter. The pure data and getColor/getHexColor are here.
public class Era : RulesetObject
{
    public int EraNumber { get; set; } = -1;
    public int ResearchAgreementCost { get; set; } = 300;
    public int StartingSettlerCount { get; set; } = 1;
    /// <summary>For mods which have differently named settlers</summary>
    public string StartingSettlerUnit { get; set; } = "Settler";
    public int StartingWorkerCount { get; set; }
    public string StartingWorkerUnit { get; set; } = "Worker";
    public int StartingMilitaryUnitCount { get; set; } = 1;
    public string StartingMilitaryUnit { get; set; } = "Warrior";
    public int StartingGold { get; set; }
    public int StartingCulture { get; set; }
    public int SettlerPopulation { get; set; } = 1;
    public List<string> SettlerBuildings { get; set; } = [];
    public List<string> StartingObsoleteWonders { get; set; } = [];
    public int BaseUnitBuyCost { get; set; } = 200;
    public int EmbarkDefense { get; set; } = 3;
    public int StartPercent { get; set; }
    public string CitySound { get; set; } = "cityClassical";

    public Dictionary<string, List<string>> FriendBonus { get; set; } = [];
    public Dictionary<string, List<string>> AllyBonus { get; set; } = [];

    public List<int>? IconRGB { get; set; }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Era;

    public EngineColor GetColor()
    {
        if (IconRGB == null) return EngineColor.White.Cpy();
        return ColorExtensions.ColorFromRGB(IconRGB[0], IconRGB[1], IconRGB[2]);
    }

    public string GetHexColor() => "#" + GetColor().ToString()[..6];
}
