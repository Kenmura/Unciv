using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Building.kt
// Not ported yet, because they need CityConstructions, Civilization, MapUnit, Tile or the description/Civilopedia types (T046 onwards, T034):
// getShortDescription, getDescription, makeLink, getCivilopediaTextLines, getSortGroup, getSubCategory, shouldBeDisplayed, getRejectionReasons
// (and notMetRejections), isBuildable, construct.
public class Building : RulesetStatsObject, INonPerpetualConstruction
{
    private IRulesetConstructionView? ruleset;
    private readonly Dictionary<string, bool> cachedMatchesFilterResult = new(StringComparer.Ordinal);
    private readonly Lazy<string?> improvementToCreate;

    public Building()
    {
        // This can be 'by lazy' across all rulesets because it only checks building uniques
        improvementToCreate = new Lazy<string?>(() =>
            ((IHasUniques)this).GetMatchingUniques(UniqueType.CreatesOneImprovement).FirstOrDefault()?.Params[0]);
    }

    public string? RequiredTech { get; set; }
    public int Cost { get; set; } = -1;

    public int Maintenance { get; set; }

    /// <summary>Kotlin <c>private var</c>, which json still sets.</summary>
    public Stats? PercentStatBonus { get; set; }

    public Counter<string> SpecialistSlots { get; set; } = new();
    public Counter<string> NewSpecialists() => SpecialistSlots;

    public Counter<string> GreatPersonPoints { get; set; } = new();

    /// <summary>Extra cost percentage when purchasing</summary>
    public int HurryCostModifier { get; set; }
    public bool IsWonder { get; set; }
    public bool IsNationalWonder { get; set; }
    public bool IsAnyWonder() => IsWonder || IsNationalWonder;
    public string? RequiredBuilding { get; set; }

    /// <summary>A strategic resource that will be consumed by this building</summary>
    public string? RequiredResource { get; set; }

    /// <summary>This Building can only be built if one of these resources is nearby - it must be improved!</summary>
    public List<string>? RequiredNearbyImprovedResources { get; set; }
    public double CityStrength { get; set; }
    public int CityHealth { get; set; }
    public string? Replaces { get; set; }
    public string? UniqueTo { get; set; }
    public string Quote { get; set; } = "";
    public string ReplacementTextForUniques { get; set; } = "";

    /// <summary>Kotlin <c>lateinit var ruleset</c></summary>
    [JsonIgnore]
    public IRulesetConstructionView Ruleset
    {
        get => ruleset ?? throw new InvalidOperationException("lateinit property ruleset has not been initialized");
        set => ruleset = value;
    }

    public override UniqueTarget GetUniqueTarget() => IsAnyWonder() ? UniqueTarget.Wonder : UniqueTarget.Building;

    public override bool IsUnavailableBySettings(IGameContextGameInfo gameInfo)
    {
        if (base.IsUnavailableBySettings(gameInfo)) return true;
        if (!gameInfo.GameParameters.NuclearWeaponsEnabled && ((IHasUniques)this).HasUnique(UniqueType.EnablesNuclearWeapons)) return true;
        return IsHiddenByStartingEra(gameInfo);
    }

    private bool IsHiddenByStartingEra(IGameContextGameInfo gameInfo)
    {
        if (!IsWonder) return false;
        // do not rely on this.ruleset or unit tests break
        var startingEra = gameInfo.Ruleset.GetEra(gameInfo.GameParameters.StartingEra);
        if (startingEra == null) return false;
        return startingEra.StartingObsoleteWonders.Contains(Name);
    }

    public Stats GetStats(IConstructionCity city)
    {
        // Calls the clone function of the NamedStats this class is derived from, not a clone function of this class
        var stats = CloneStats();
        var self = (IHasUniques)this;

        var conditionalState = city.State;

        city.ForEachMatchingUnique(UniqueType.StatsFromObject, null, unique =>
        {
            if (!MatchesFilter(unique.Params[1], conditionalState)) return;
            stats.Add(unique.Stats);
        });

        self.ForEachMatchingUnique(UniqueType.Stats, conditionalState, unique =>
        {
            stats.Add(unique.Stats);
        });

        if (!IsWonder)
            city.ForEachMatchingUnique(UniqueType.StatsFromBuildings, city.State, unique =>
            {
                if (MatchesFilter(unique.Params[1], conditionalState))
                    stats.Add(unique.Stats);
            });
        return stats;
    }

    public Stats GetStatPercentageBonuses(IConstructionCity? city)
    {
        var stats = PercentStatBonus?.Clone() ?? new Stats();
        if (city == null) return stats; // initial stats

        var conditionalState = city.State;

        city.ForEachMatchingUnique(UniqueType.StatPercentFromObject, conditionalState, unique =>
        {
            if (MatchesFilter(unique.Params[2], conditionalState))
                stats.Add(StatExtensions.ValueOf(unique.Params[1]), ParseFloat(unique.Params[0]));
        });

        city.ForEachMatchingUnique(UniqueType.AllStatsPercentFromObject, conditionalState, unique =>
        {
            if (!MatchesFilter(unique.Params[1], conditionalState)) return;
            foreach (var stat in StatExtensions.Entries)
            {
                stats.Add(stat, ParseFloat(unique.Params[0]));
            }
        });

        return stats;
    }

    public int GetProductionCost(IConstructionCivilization civInfo, IConstructionCity? city)
    {
        var self = (IHasUniques)this;
        var productionCost = (float)Cost;
        var stateForConditionals = city?.State ?? civInfo.State;

        self.ForEachMatchingUnique(UniqueType.CostIncreasesWhenBuilt, stateForConditionals, unique =>
        {
            productionCost += civInfo.GetBuiltItemsWithIncreasingCost(Name) * ParseInt(unique.Params[0]);
        });

        self.ForEachMatchingUnique(UniqueType.CostIncreasesPerCity, stateForConditionals, unique =>
        {
            productionCost += civInfo.Cities.Count * ParseInt(unique.Params[0]);
        });

        self.ForEachMatchingUnique(UniqueType.CostPercentageChange, stateForConditionals, unique =>
        {
            productionCost *= unique.Params[0].ToPercent();
        });

        if (civInfo.IsCityState)
            productionCost *= 1.5f;
        else if (civInfo.IsHuman())
        {
            if (!IsWonder)
                productionCost *= civInfo.GetDifficulty().BuildingCostModifier;
        }
        else
        {
            productionCost *= IsWonder
                ? civInfo.GameInfo.GetDifficulty().AiWonderCostModifier
                : civInfo.GameInfo.GetDifficulty().AiBuildingCostModifier;
        }

        productionCost *= civInfo.GameInfo.Speed.ProductionCostModifier;

        return KotlinMath.FloatToInt(productionCost);
    }

    public bool CanBePurchasedWithStat(IConstructionCity? city, Stat stat)
    {
        var purchaseReason = ((INonPerpetualConstruction)this).CanBePurchasedWithStatReasons(null, stat);
        if (purchaseReason != PurchaseReason.UniqueAllowed && stat == Stat.Gold && IsAnyWonder()) return false;
        if (city == null) return purchaseReason.Purchasable();

        var conditionalState = city.State;
        var statName = stat.ToString();
        return city.GetMatchingUniques(UniqueType.BuyBuildingsIncreasingCost, conditionalState)
                .Any(it =>
                    it.Params[2] == statName
                    && MatchesFilter(it.Params[0], conditionalState)
                    && city.MatchesFilter(it.Params[3], null))
            || city.GetMatchingUniques(UniqueType.BuyBuildingsByProductionCost, conditionalState)
                .Any(it => it.Params[1] == statName && MatchesFilter(it.Params[0], conditionalState))
            || city.GetMatchingUniques(UniqueType.BuyBuildingsWithStat, conditionalState)
                .Any(it =>
                    it.Params[1] == statName
                    && MatchesFilter(it.Params[0], conditionalState)
                    && city.MatchesFilter(it.Params[2], null))
            || city.GetMatchingUniques(UniqueType.BuyBuildingsForAmountStat, conditionalState)
                .Any(it =>
                    it.Params[2] == statName
                    && MatchesFilter(it.Params[0], conditionalState)
                    && city.MatchesFilter(it.Params[3], null))
            || INonPerpetualConstruction.CanBePurchasedWithStatDefault(this, city, stat);
    }

    private float? GetSpecificBuyCost(IConstructionCity city, Stat stat)
    {
        var conditionalState = city.State;
        var statName = stat.ToString();
        var self = (INonPerpetualConstruction)this;
        var costs = new List<float>();

        foreach (var unique in city.GetMatchingUniques(UniqueType.BuyBuildingsIncreasingCost, conditionalState)
                     .Where(it =>
                         it.Params[2] == statName
                         && MatchesFilter(it.Params[0], conditionalState)
                         && city.MatchesFilter(it.Params[3], null)))
        {
            costs.Add(self.GetCostForConstructionsIncreasingInPrice(
                ParseInt(unique.Params[1]),
                ParseInt(unique.Params[4]),
                city.Civ.GetBoughtItemsWithIncreasingPrice(Name)) * city.Civ.GameInfo.Speed.GetStatCostModifier(stat));
        }

        foreach (var unique in city.GetMatchingUniques(UniqueType.BuyBuildingsByProductionCost, conditionalState)
                     .Where(it => it.Params[1] == statName && MatchesFilter(it.Params[0], conditionalState)))
            costs.Add((float)(GetProductionCost(city.Civ, city) * ParseInt(unique.Params[2])));

        if (city.GetMatchingUniques(UniqueType.BuyBuildingsWithStat, conditionalState)
            .Any(it =>
                it.Params[1] == statName
                && MatchesFilter(it.Params[0], conditionalState)
                && city.MatchesFilter(it.Params[2], null)))
        {
            costs.Add(city.Civ.GetEra().BaseUnitBuyCost * city.Civ.GameInfo.Speed.GetStatCostModifier(stat));
        }

        foreach (var unique in city.GetMatchingUniques(UniqueType.BuyBuildingsForAmountStat, conditionalState)
                     .Where(it =>
                         it.Params[2] == statName
                         && MatchesFilter(it.Params[0], conditionalState)
                         && city.MatchesFilter(it.Params[3], null)))
            costs.Add(ParseInt(unique.Params[1]) * city.Civ.GameInfo.Speed.GetStatCostModifier(stat));

        return costs.Count == 0 ? null : costs.Min();
    }

    public float? GetBaseBuyCost(IConstructionCity city, Stat stat)
    {
        // Specific
        var specificCost = GetSpecificBuyCost(city, stat);
        if (specificCost != null) return specificCost;
        return INonPerpetualConstruction.GetBaseBuyCostDefault(this, city, stat);
    }

    public int? GetStatBuyCost(IConstructionCity city, Stat stat)
    {
        var baseBuyCost = GetBaseBuyCost(city, stat);
        if (baseBuyCost == null) return null;
        double cost = baseBuyCost.Value;
        var conditionalState = city.State;

        city.ForEachMatchingUnique(UniqueType.BuyItemsDiscount, null, unique =>
        {
            if (stat.ToString() == unique.Params[0])
                cost *= unique.Params[1].ToPercent();
        });

        city.ForEachMatchingUnique(UniqueType.BuyBuildingsDiscount, null, unique =>
        {
            if (stat.ToString() == unique.Params[0] && MatchesFilter(unique.Params[1], conditionalState))
                cost *= unique.Params[2].ToPercent();
        });

        return KotlinMath.DoubleToInt(cost / 10f) * 10;
    }

    /// <summary>Implements UniqueParameterType.BuildingFilter</summary>
    public bool MatchesFilter(string filter, GameContext? state = null)
    {
        var self = (IHasUniques)this;
        return MultiFilter.Filter(filter, it =>
            CachedMatchesSingleFilter(it) ||
            (state != null && self.HasTagUnique(it, state)) ||
            (state == null && self.HasTagUnique(it)));
    }

    /// <summary>Kotlin <c>cachedMatchesFilterResult.getOrPut</c>: the cache is keyed by the filter alone.</summary>
    private bool CachedMatchesSingleFilter(string filter)
    {
        if (cachedMatchesFilterResult.TryGetValue(filter, out var cached)) return cached;
        var result = MatchesSingleFilter(filter);
        cachedMatchesFilterResult[filter] = result;
        return result;
    }

    private bool MatchesSingleFilter(string filter)
    {
        // all cases are constants for performance
        switch (filter)
        {
            case "all" or "All": return true;
            case "Building" or "Buildings": return !IsAnyWonder();
            case "Wonder" or "Wonders": return IsAnyWonder();
            case "National Wonder" or "National": return IsNationalWonder;
            case "World Wonder" or "World": return IsWonder;
            default:
                if (filter == Name) return true;
                if (filter == Replaces) return true;
                if (ruleset != null) // False when loading ruleset and checking buildingsToRemove
                    foreach (var requiredTech in ((IHasUniques)this).RequiredTechs())
                        if (ruleset.TechnologyMatchesFilter(requiredTech, filter, null, false) == true) return true;
                var stat = StatExtensions.SafeValueOf(filter);
                return stat != null && IsStatRelated(stat.Value);
        }
    }

    public bool IsStatRelated(Stat stat, IConstructionCity? city = null)
    {
        var self = (IHasUniques)this;
        if (city != null)
        {
            if (GetStats(city)[stat] > 0) return true;
            if (GetStatPercentageBonuses(city)[stat] > 0) return true;
        }
        else
        {
            if (this[stat] > 0) return true;
            if (self.GetMatchingUniques(UniqueType.Stats).Any(it => it.Stats[stat] > 0)) return true;
            if (GetStatPercentageBonuses(null)[stat] > 0) return true;
        }
        if (self.GetMatchingUniques(UniqueType.StatsFromTiles).Any(it => it.Stats[stat] > 0)) return true;
        if (self.GetMatchingUniques(UniqueType.StatsPerPopulation).Any(it => it.Stats[stat] > 0)) return true;
        if (stat == Stat.Happiness && self.HasUnique(UniqueType.RemovesAnnexUnhappiness)) return true;
        return false;
    }

    public bool HasCreateOneImprovementUnique() => improvementToCreate.Value != null;

    // Only the name can be cached across rulesets.
    // The improvement itself CANNOT be cached because it's ruleset-dependent.
    private Tile.TileImprovement? GetImprovementToCreate(IRulesetConstructionView ruleset)
    {
        if (improvementToCreate.Value == null) return null;
        return ruleset.GetTileImprovement(improvementToCreate.Value);
    }

    public Tile.TileImprovement? GetImprovementToCreate(IRulesetConstructionView ruleset, IConstructionCivilization civInfo)
    {
        var improvement = GetImprovementToCreate(ruleset);
        if (improvement == null) return null;
        return civInfo.GetEquivalentTileImprovement(improvement);
    }

    public bool IsSellable() => !IsAnyWonder() && !((IHasUniques)this).HasUnique(UniqueType.Unsellable);

    public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null)
    {
        var uniques = ((IHasUniques)this).GetMatchingUniques(UniqueType.ConsumesResources, state ?? GameContext.EmptyState).ToList();
        if (uniques.Count == 0 && RequiredResource == null) return Counter.ZERO;

        var resourceRequirements = new Counter<string>();
        if (RequiredResource != null) resourceRequirements[RequiredResource] = 1;
        foreach (var unique in uniques)
            resourceRequirements.Add(unique.Params[1], ParseInt(unique.Params[0]));
        return resourceRequirements;
    }

    /// <summary>Kotlin <c>String.toInt()</c></summary>
    private static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>Kotlin <c>String.toFloat()</c></summary>
    private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
