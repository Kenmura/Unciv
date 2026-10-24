using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/Terrain.kt
// Not ported yet: displayAs (needs Ruleset, T034), getColor (engine colour type, T050), makeLink/getCivilopediaTextLines/getSortGroup/getSubCategory (Civilopedia text types, T034).
public class Terrain : RulesetStatsObject, IRulesetTerrainParameterView
{
    private readonly Lazy<bool> isRough;
    private readonly Lazy<bool> isMountain;
    private readonly Lazy<bool> isHill;
    private readonly Lazy<bool> isFreshwater;
    private readonly Lazy<bool> isCoast;
    private readonly Lazy<bool> isOcean;
    private readonly Lazy<bool> isVegetation;
    private readonly Lazy<bool> isIce;

    /// <summary>Terrain filter matching is "pure" - input always returns same output, and it's called a bajillion times</summary>
    private readonly Dictionary<string, bool> cachedMatchesFilterResult = [];

    private TerrainType? type;

    public Terrain()
    {
        var self = (IHasUniques)this;
        isRough = new Lazy<bool>(() => self.HasUnique(UniqueType.RoughTerrain));
        isMountain = new Lazy<bool>(() => self.HasUnique(UniqueType.OccursInChains));
        isHill = new Lazy<bool>(() => self.HasUnique(UniqueType.OccursInGroups));
        isFreshwater = new Lazy<bool>(() => self.HasUnique(UniqueType.FreshWater));
        isCoast = new Lazy<bool>(() => Type == TerrainType.Water && !IsFreshwater
            && (self.HasUnique(UniqueType.CoastalWater) || Name == Constants.Coast));
        isOcean = new Lazy<bool>(() => Type == TerrainType.Water && !IsFreshwater && !IsCoast);
        isVegetation = new Lazy<bool>(() => self.HasUnique(UniqueType.Vegetation));
        isIce = new Lazy<bool>(() => Type == TerrainType.TerrainFeature && Impassable && OccursOn.Contains(Constants.Ocean));
    }

    /// <summary>Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    public TerrainType Type
    {
        get => type ?? throw new InvalidOperationException("lateinit property type has not been initialized");
        set => type = value;
    }

    /// <summary>For terrain features - indicates the stats of this terrain override those of all previous layers</summary>
    public bool OverrideStats { get; set; }

    /// <summary>If true, nothing can be built here - not even resource improvements</summary>
    public bool Unbuildable { get; set; }

    /// <summary>For terrain features</summary>
    public List<string> OccursOn { get; set; } = [];

    /// <summary>
    /// Used by Natural Wonders: it is the baseTerrain on top of which the Natural Wonder is placed.
    /// Omitting it means the Natural Wonder is placed on whatever baseTerrain the Tile already had (limited by occursOn)
    /// </summary>
    public string? TurnsInto { get; set; }

    /// <summary>Natural Wonder weight: probability to be picked</summary>
    public int Weight { get; set; } = 10;

    /// <summary>RGB color of base terrain</summary>
    [JsonPropertyName("RGB")]
    public List<int>? RGB { get; set; }

    public int MovementCost { get; set; } = 1;
    public float DefenceBonus { get; set; }
    public bool Impassable { get; set; }

    [JsonIgnore]
    public int DamagePerTurn { get; set; }

    [JsonIgnore]
    public bool IsRough => isRough.Value;

    [JsonIgnore]
    public bool IsMountain => isMountain.Value;

    [JsonIgnore]
    public bool IsHill => isHill.Value;

    [JsonIgnore]
    public bool IsFreshwater => isFreshwater.Value;

    [JsonIgnore]
    public bool IsCoast => isCoast.Value;

    [JsonIgnore]
    public bool IsOcean => isOcean.Value;

    [JsonIgnore]
    public bool IsVegetation => isVegetation.Value;

    [JsonIgnore]
    public bool IsIce => isIce.Value;

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Terrain;

    bool IRulesetTerrainParameterView.IsBaseTerrain => Type.IsBaseTerrain();

    bool IRulesetTerrainParameterView.IsTerrainFeature => Type == TerrainType.TerrainFeature;

    /// <summary>Kotlin goes through the unique map (conditionals against <c>EmptyState</c>), not the unique texts.</summary>
    bool IRulesetTerrainParameterView.HasRegionUnique =>
        ((IHasUniques)this).HasUnique(UniqueType.RegionRequirePercentSingleType)
        || ((IHasUniques)this).HasUnique(UniqueType.RegionRequirePercentTwoTypes);

    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        if (multiFilter)
        {
            return MultiFilter.Filter(filter, it =>
            {
                if (!cachedMatchesFilterResult.TryGetValue(it, out var cached))
                {
                    cached = MatchesSingleFilter(it);
                    cachedMatchesFilterResult[it] = cached;
                }
                return cached
                    || (state != null && self.HasTagUnique(it, state))
                    || (state == null && self.HasTagUnique(it));
            });
        }
        if (!cachedMatchesFilterResult.TryGetValue(filter, out var single))
        {
            single = MatchesSingleFilter(filter);
            cachedMatchesFilterResult[filter] = single;
        }
        return single
            || (state != null && self.HasTagUnique(filter, state))
            || (state == null && self.HasTagUnique(filter));
    }

    /// <summary>Implements UniqueParameterType.TerrainFilter</summary>
    public bool MatchesSingleFilter(string filter)
    {
        switch (filter)
        {
            case "all":
            case "All":
            case "Terrain":
                return true;
            case Constants.Impassable:
                return Impassable;
            case "Open terrain":
                return !IsRough;
            case "Rough terrain":
                return IsRough;
            case "Natural Wonder":
                return Type == TerrainType.NaturalWonder;
            case "Terrain Feature":
                return Type == TerrainType.TerrainFeature;
            default:
                return filter == Name || filter == Type.ToString();
        }
    }

    public void SetTransients()
    {
        var damage = 0;
        foreach (var unique in ((IHasUniques)this).GetMatchingUniques(UniqueType.DamagesContainingUnits))
            damage = unchecked(damage + int.Parse(unique.Params[0], CultureInfo.InvariantCulture));
        DamagePerTurn = damage;
    }
}
