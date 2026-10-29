using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset.Tile;

// core/src/com/unciv/models/ruleset/tile/Terrain.kt
// Not ported yet: getColor (engine colour type, T050).
public class Terrain : RulesetStatsObject, IRulesetTerrainParameterView, ICivilopediaText
{
    public override string MakeLink() => $"Terrain/{Name}";

    public bool DisplayAs(TerrainType asType, Ruleset ruleset) =>
        Type == asType
        || OccursOn.Any(occursName => ruleset.Terrains.TryGetValue(occursName, out var occursTerrain) && occursTerrain.Type == asType)
        || (TurnsInto != null && ruleset.Terrains.TryGetValue(TurnsInto, out var turnsIntoTerrain) && turnsIntoTerrain.Type == asType);

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context)
    {
        var ruleset = context.Ruleset;
        var textList = new List<FormattedLine>();

        if (Type == TerrainType.NaturalWonder)
        {
            textList.Add(new FormattedLine("Natural Wonder", header: 3, color: "#3A0"));
        }

        var stats = CloneStats();
        if (!stats.IsEmpty() || OverrideStats)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine(stats.IsEmpty() ? "No yields" : stats.ToString()));
            if (OverrideStats)
                textList.Add(new FormattedLine("Overrides yields from underlying terrain"));
        }

        if (OccursOn.Count != 0 && !((IHasUniques)this).HasUnique(UniqueType.NoNaturalGeneration))
        {
            textList.Add(new FormattedLine());
            if (OccursOn.Count == 1)
            {
                var occurs = OccursOn[0];
                textList.Add(new FormattedLine($"Occurs on [{occurs}]", link: $"Terrain/{occurs}"));
            }
            else
            {
                textList.Add(new FormattedLine("Occurs on:"));
                foreach (var occurs in OccursOn)
                    textList.Add(new FormattedLine(occurs, link: $"Terrain/{occurs}", indent: 1));
            }
        }

        var improvementsThatCanBePlacedHere = ruleset.TileImprovements.Values
            .Where(it => it.TerrainsCanBeBuiltOn.Contains(Name))
            .ToList();
        if (improvementsThatCanBePlacedHere.Count != 0)
        {
            textList.Add(new FormattedLine("{Tile Improvements}:"));
            foreach (var improvement in improvementsThatCanBePlacedHere)
                textList.Add(new FormattedLine(improvement.Name, improvement.MakeLink(), indent: 1));
        }

        if (TurnsInto != null)
        {
            textList.Add(new FormattedLine($"Placed on [{TurnsInto}]", link: $"Terrain/{TurnsInto}"));
        }

        var resourcesFound = ruleset.TileResources.Values
            .Where(it => it.TerrainsCanBeFoundOn.Contains(Name))
            .ToList();
        if (resourcesFound.Count != 0)
        {
            textList.Add(new FormattedLine());
            if (resourcesFound.Count == 1)
            {
                var res = resourcesFound[0];
                textList.Add(new FormattedLine($"May contain [{res.Name}]", link: $"Resource/{res.Name}"));
            }
            else
            {
                textList.Add(new FormattedLine("May contain:"));
                foreach (var res in resourcesFound)
                    textList.Add(new FormattedLine(res.Name, link: $"Resource/{res.Name}", indent: 1));
            }
        }

        textList.Add(new FormattedLine());
        // For now, natural wonders show no "open terrain" - may change later
        if (TurnsInto == null && DisplayAs(TerrainType.Land, ruleset) && !IsRough)
            textList.Add(new FormattedLine("Open terrain")); // Rough is in uniques
        this.UniquesToCivilopediaTextLines(textList, context, noLeadingSeparator: true);

        textList.Add(new FormattedLine());
        if (Impassable) textList.Add(new FormattedLine(Constants.Impassable, color: "#A00"));
        else if (MovementCost > 0) textList.Add(new FormattedLine($"{{Movement cost}}: {MovementCost}"));

        if (DefenceBonus != 0f)
            textList.Add(new FormattedLine($"{{Defence bonus}}: {KotlinMath.FloatToInt(DefenceBonus * 100)}%"));

        var constructions = ruleset.Buildings.Values.Cast<IRulesetObject>()
            .Concat(ruleset.Units.Values)
            .Where(construction => ((IHasUniques)construction).HasUniquesMentioning(Name))
            .Select(it => new FormattedLine(it.Name, it.MakeLink(), indent: 1));

        var seeAlso = constructions.Concat(Belief.GetCivilopediaTextMatching(Name, ruleset, false, context.GameInfo)).ToList();
        if (seeAlso.Count != 0)
        {
            textList.Add(new FormattedLine());
            textList.Add(new FormattedLine("{See also}:"));
            textList.AddRange(seeAlso);
        }

        return textList;
    }

    public int GetSortGroup(Ruleset ruleset) => (int)Type;

    public string? GetSubCategory(Ruleset ruleset) => Type switch
    {
        TerrainType.Land => "Land",
        TerrainType.Water => "Water",
        TerrainType.TerrainFeature => "Features",
        TerrainType.NaturalWonder => "Natural Wonders",
        _ => null,
    };

    private readonly KotlinLazy<bool> isRough;
    private readonly KotlinLazy<bool> isMountain;
    private readonly KotlinLazy<bool> isHill;
    private readonly KotlinLazy<bool> isFreshwater;
    private readonly KotlinLazy<bool> isCoast;
    private readonly KotlinLazy<bool> isOcean;
    private readonly KotlinLazy<bool> isVegetation;
    private readonly KotlinLazy<bool> isIce;

    /// <summary>Terrain filter matching is "pure" - input always returns same output, and it's called a bajillion times</summary>
    private readonly Dictionary<string, bool> cachedMatchesFilterResult = [];

    private TerrainType? type;

    public Terrain()
    {
        var self = (IHasUniques)this;
        isRough = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.RoughTerrain));
        isMountain = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.OccursInChains));
        isHill = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.OccursInGroups));
        isFreshwater = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.FreshWater));
        isCoast = new KotlinLazy<bool>(() => Type == TerrainType.Water && !IsFreshwater
            && (self.HasUnique(UniqueType.CoastalWater) || Name == Constants.Coast));
        isOcean = new KotlinLazy<bool>(() => Type == TerrainType.Water && !IsFreshwater && !IsCoast);
        isVegetation = new KotlinLazy<bool>(() => self.HasUnique(UniqueType.Vegetation));
        isIce = new KotlinLazy<bool>(() => Type == TerrainType.TerrainFeature && Impassable && OccursOn.Contains(Constants.Ocean));
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
            return MultiFilter.Filter(filter, (terrain: this, state), static (it, s) =>
            {
                if (!s.terrain.cachedMatchesFilterResult.TryGetValue(it, out var cached))
                {
                    cached = s.terrain.MatchesSingleFilter(it);
                    s.terrain.cachedMatchesFilterResult[it] = cached;
                }
                return cached
                    || (s.state != null && ((IHasUniques)s.terrain).HasTagUnique(it, s.state))
                    || (s.state == null && ((IHasUniques)s.terrain).HasTagUnique(it));
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
