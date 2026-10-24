using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/TileResource.kt
// Not ported yet: getImprovingImprovement (Civilization/Tile, T046/T047),
// generatesNaturallyOn (Tile/TileMap), makeLink/getCivilopediaTextLines/getSortGroup/getSubCategory (Civilopedia text types, T034's successors).
public class TileResource : RulesetStatsObject, IGameResource, IRulesetResourceParameterView
{
    private readonly Lazy<bool> isCityWide;
    private readonly Lazy<bool> isStockpiled;

    public TileResource()
    {
        var self = (IHasUniques)this;
        isCityWide = new Lazy<bool>(() => self.HasUnique(UniqueType.CityResource, GameContext.IgnoreConditionalsState));
        isStockpiled = new Lazy<bool>(() => self.HasUnique(UniqueType.Stockpiled, GameContext.IgnoreConditionalsState));
    }

    public ResourceType ResourceType { get; set; } = ResourceType.Bonus;
    public List<string> TerrainsCanBeFoundOn { get; set; } = [];

    /// <summary>
    /// Stats that this resource adds to a tile when a correct improvement has been constructed.
    /// The Stats component of this resource on the other hand is always in effect.
    /// </summary>
    public Stats? ImprovementStats { get; set; }

    /// <summary>Name of the Technology needed to see and use this resource</summary>
    public string? RevealedBy { get; set; }

    /// <summary>Legacy "which improvement will unlock this resource"</summary>
    public string? Improvement { get; set; }

    /// <summary>Defines which improvement will "unlock" this resource</summary>
    public List<string> ImprovedBy { get; set; } = [];

    public DepositAmount MajorDepositAmount { get; set; } = new();
    public DepositAmount MinorDepositAmount { get; set; } = new();

    [JsonIgnore]
    public bool IsCityWide => isCityWide.Value;

    [JsonIgnore]
    public bool IsStockpiled => isStockpiled.Value;

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Resource;

    private LinkedHashSet<string>? allImprovements;
    private Ruleset? ruleset;

    /// <summary>
    /// Collects which improvements "unlock" this resource, caches and returns the Set.
    /// The cache is cleared after ruleset load and combine, via <see cref="SetTransients"/>.
    /// </summary>
    public IReadOnlySet<string> GetImprovements()
    {
        if (allImprovements != null) return allImprovements;

        var rulesetForCache = ruleset
            ?? throw new InvalidOperationException("No ruleset on TileResource when initializing improvements");

        var allImprovementsLocal = new LinkedHashSet<string>();

        if (Improvement != null) allImprovementsLocal.Add(Improvement);
        allImprovementsLocal.AddAll(ImprovedBy);
        foreach (var improvement in rulesetForCache.TileImprovements.Values)
        {
            // Explicitly stated by the improvement, or this is a replacement improvement
            if (((IHasUniques)improvement).GetMatchingUniques(UniqueType.ImprovesResources).Any(it => MatchesFilter(it.Params[0]))
                || (improvement.Replaces != null && allImprovementsLocal.Contains(improvement.Replaces)))
            {
                allImprovementsLocal.Add(improvement.Name);
            }
        }

        allImprovements = allImprovementsLocal;
        return allImprovementsLocal;
    }

    /// <summary>
    /// Clears the cache for <see cref="GetImprovements"/> and saves the Ruleset the cache update will need.
    /// Doesn't evaluate the cache immediately, that would trip the uniqueObjects lazy before tests (and mods loading) are done manipulating uniques.
    /// </summary>
    public void SetTransients(Ruleset ruleset)
    {
        allImprovements = null;
        this.ruleset = ruleset;
    }

    public bool IsImprovedBy(string improvementName) => GetImprovements().Contains(improvementName);

    public bool IsImprovedBy(TileImprovement improvement) => GetImprovements().Contains(improvement.Name);

    public bool MatchesFilter(string filter, GameContext? state = null)
    {
        var self = (IHasUniques)this;
        return MultiFilter.Filter(filter, it =>
            MatchesSingleFilter(it)
            || (state != null && self.HasTagUnique(it, state))
            || (state == null && self.HasTagUnique(it)));
    }

    public bool MatchesSingleFilter(string filter)
    {
        if (filter == "any") return true;
        if (Constants.All.Contains(filter)) return true;
        if (filter == Name) return true;
        if (filter == ResourceType.ToString()) return true;
        return ImprovementStats?.Any(it => filter == it.Key.ToString()) == true;
    }

    public class DepositAmount
    {
        public int Sparse { get; set; } = 1;
        public int Default { get; set; } = 2;
        public int Abundant { get; set; } = 3;
    }
}
