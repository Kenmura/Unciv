using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/TileResource.kt
// Not ported yet: getImprovements/isImprovedBy/setTransients (cache over Ruleset.tileImprovements, T034), getImprovingImprovement (Civilization/Tile, T046/T047),
// generatesNaturallyOn (Tile/TileMap), makeLink/getCivilopediaTextLines/getSortGroup/getSubCategory (Civilopedia text types, T034).
public class TileResource : RulesetStatsObject, IGameResource
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
