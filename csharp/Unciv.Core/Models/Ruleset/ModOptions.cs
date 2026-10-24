using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/ModOptions.kt
public class ModOptions : IHasUniques
{
    private readonly Lazy<IReadOnlyList<UniqueObject>> uniqueObjects;
    private readonly Lazy<UniqueMap> uniqueMap;

    public ModOptions()
    {
        uniqueObjects = new Lazy<IReadOnlyList<UniqueObject>>(() => ((IHasUniques)this).UniqueObjectsProvider());
        uniqueMap = new Lazy<UniqueMap>(() => ((IHasUniques)this).UniqueMapProvider());
    }

    #region Modder choices
    public bool IsBaseRuleset { get; set; }

    public HashSet<string> BeliefsToRemove { get; set; } = [];
    public HashSet<string> BuildingsToRemove { get; set; } = [];
    public HashSet<string> CityStateTypesToRemove { get; set; } = [];
    public HashSet<string> DifficultiesToRemove { get; set; } = [];
    public HashSet<string> EventsToRemove { get; set; } = [];
    public HashSet<string> ImprovementsToRemove { get; set; } = [];
    public HashSet<string> NationsToRemove { get; set; } = [];
    public HashSet<string> PersonalitiesToRemove { get; set; } = [];
    public HashSet<string> PolicyBranchesToRemove { get; set; } = [];
    public HashSet<string> PoliciesToRemove { get; set; } = [];
    public HashSet<string> QuestsToRemove { get; set; } = [];
    public HashSet<string> ReligionsToRemove { get; set; } = [];
    public HashSet<string> ResourcesToRemove { get; set; } = [];
    public HashSet<string> RuinRewardsToRemove { get; set; } = [];
    public HashSet<string> SpecialistsToRemove { get; set; } = [];
    public HashSet<string> SpeedsToRemove { get; set; } = [];
    public HashSet<string> TechsToRemove { get; set; } = [];
    public HashSet<string> TerrainsToRemove { get; set; } = [];
    public HashSet<string> TutorialsToRemove { get; set; } = [];
    public HashSet<string> UnitsToRemove { get; set; } = [];
    public HashSet<string> UnitNameGroupsToRemove { get; set; } = [];
    public HashSet<string> UnitPromotionsToRemove { get; set; } = [];
    public HashSet<string> UnitTypesToRemove { get; set; } = [];
    public HashSet<string> VictoriesToRemove { get; set; } = [];

    // Kotlin `val`: json may still assign it
    public ModConstants Constants { get; set; } = new();
    public string? Unitset { get; set; }
    public string? Tileset { get; set; }
    #endregion

    #region Metadata, automatic
    public string ModUrl { get; set; } = "";
    public string DefaultBranch { get; set; } = "master";
    public string Author { get; set; } = "";
    public string LastUpdated { get; set; } = "";
    public int ModSize { get; set; }
    public List<string> Topics { get; set; } = [];
    #endregion

    #region IHasUniques
    public string Name { get; set; } = "ModOptions";
    public List<string> Uniques { get; set; } = [];

    [JsonIgnore]
    public IReadOnlyList<UniqueObject> UniqueObjects => uniqueObjects.Value;

    [JsonIgnore]
    public UniqueMap UniqueMap => uniqueMap.Value;

    public UniqueTarget GetUniqueTarget() => UniqueTarget.ModOptions;
    #endregion
}
