using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Logic.City;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Building.kt
public class Building : RulesetStatsObject, INonPerpetualConstruction, ICivilopediaText
{
    public override string MakeLink() => IsAnyWonder() ? $"Wonder/{Name}" : $"Building/{Name}";

    public string GetShortDescription(bool multiline = false, Func<UniqueObject, bool>? uniqueInclusionFilter = null) =>
        BuildingDescriptions.GetShortDescription(this, multiline, uniqueInclusionFilter);

    public string GetDescription(City city, bool showAdditionalInfo) =>
        BuildingDescriptions.GetDescription(this, city, showAdditionalInfo);

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) =>
        BuildingDescriptions.GetBuildingCivilopediaTextLines(this, context);

    public int GetSortGroup(Ruleset ruleset) =>
        RequiredTech != null && ruleset.Technologies.TryGetValue(RequiredTech, out var tech)
            ? tech.Column != null && ruleset.Eras.TryGetValue(tech.Column.Era, out var era) ? era.EraNumber : 100
            : 100;

    public string? GetSubCategory(Ruleset ruleset) =>
        RequiredTech != null && ruleset.Technologies.TryGetValue(RequiredTech, out var tech)
            ? tech.Column != null && ruleset.Eras.TryGetValue(tech.Column.Era, out var era) ? era.Name : "Other"
            : "Other";

    public int GetCivilopediaGoldCost() =>
        KotlinMath.DoubleToInt(Math.Pow(30.0 * (double)(float)Cost, 0.75) * (double)HurryCostModifier.ToPercent() / 10) * 10;

    private IRulesetConstructionView? ruleset;
    private readonly ConcurrentDictionary<string, bool> cachedMatchesFilterResult = new(StringComparer.Ordinal); // T296: written lazily from host view building threads
    private readonly KotlinLazy<string?> improvementToCreate;

    public Building()
    {
        // This can be 'by lazy' across all rulesets because it only checks building uniques
        improvementToCreate = new KotlinLazy<string?>(() =>
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

    /// <summary>Brave New World (B007): the kind of Great Work the <see cref="GreatWorkSlots"/> of this building take; null for none.</summary>
    public GreatWorkSlotType? GreatWorkSlotType { get; set; }

    /// <summary>Brave New World (B007): how many Great Works the building holds.</summary>
    public int GreatWorkSlots { get; set; }

    /// <summary>Brave New World (B007): theming bonus rows in data order, see <see cref="ThemingBonus"/>.</summary>
    public List<ThemingBonus> ThemingBonuses { get; set; } = [];

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

    public bool ShouldBeDisplayed(CityConstructions cityConstructions)
    {
        if (cityConstructions.IsBeingConstructedOrEnqueued(Name))
            return false;
        foreach (var unique in ((IHasUniques)this).GetMatchingUniques(UniqueType.MaxNumberBuildable))
        {
            if (cityConstructions.City.Civ.CivConstructions.CountConstructedObjects(this) >= ParseInt(unique.Params[0]))
                return false;
        }

        var rejectionReasons = GetRejectionReasons(cityConstructions).ToList();

        if (((IHasUniques)this).HasUnique(UniqueType.ShowsWhenUnbuilable, cityConstructions.City.State) &&
            !rejectionReasons.Any(it => it.IsNeverVisible()))
            return true;

        if (rejectionReasons.Any(it => it.Type == RejectionReasonType.RequiresBuildingInSomeCities)
            && cityConstructions.City.Civ.GameInfo.GameParameters.OneCityChallenge)
            return false; // You will never be able to get more cities, this building is effectively disabled

        if (!rejectionReasons.Any(it => !it.ShouldShow)) return true;
        return ((INonPerpetualConstruction)this).CanBePurchasedWithAnyStat(cityConstructions.City)
            && rejectionReasons.All(it => it.Type == RejectionReasonType.Unbuildable);
    }

    public IEnumerable<RejectionReason> GetRejectionReasons(CityConstructions cityConstructions)
    {
        var city = cityConstructions.City;
        var cityCenter = city.GetCenterTile();
        var civ = city.Civ;
        var stateForConditionals = city.State;

        if (cityConstructions.IsBuilt(Name))
            yield return RejectionReasonType.AlreadyBuilt.ToInstance();

        if (civ.GameInfo.IsUnavailableBySettingsCached(this))
        {
            // Repeat the starting era test isHiddenBySettings already did to change the RejectionReasonType
            if (IsHiddenByStartingEra(civ.GameInfo))
                yield return RejectionReasonType.WonderDisabledEra.ToInstance();
            else
                yield return RejectionReasonType.DisabledBySetting.ToInstance();
        }

        foreach (var unique in UniqueObjects)
        {
            // skip uniques that don't have conditionals apply
            // EXCEPT for [UniqueType.OnlyAvailable] and [UniqueType.CanOnlyBeBuiltInCertainCities]
            // since they trigger (reject) only if conditionals ARE NOT met
            if (unique.Type != UniqueType.OnlyAvailable && unique.Type != UniqueType.CanOnlyBeBuiltWhen &&
                !unique.ConditionalsApply(stateForConditionals)) continue;

            switch (unique.Type)
            {
                // for buildings that are created as side effects of other things, and not directly built,
                // or for buildings that can only be bought
                case UniqueType.Unbuildable:
                    yield return RejectionReasonType.Unbuildable.ToInstance();
                    break;

                case UniqueType.OnlyAvailable:
                    foreach (var reason in NotMetRejections(unique, cityConstructions)) yield return reason;
                    break;

                case UniqueType.CanOnlyBeBuiltWhen:
                    foreach (var reason in NotMetRejections(unique, cityConstructions, true)) yield return reason;
                    break;

                case UniqueType.Unavailable:
                    yield return RejectionReasonType.ShouldNotBeDisplayed.ToInstance();
                    break;

                case UniqueType.RequiresPopulation:
                    if (ParseInt(unique.Params[0]) > city.Population.Population)
                        yield return RejectionReasonType.PopulationRequirement.ToInstance(unique.Text);
                    break;

                case UniqueType.MustBeOn:
                    if (!cityCenter.MatchesTerrainFilter(unique.Params[0], civ))
                        yield return RejectionReasonType.MustBeOnTile.ToInstance(unique.Text);
                    break;

                case UniqueType.MustNotBeOn:
                    if (cityCenter.MatchesTerrainFilter(unique.Params[0], civ))
                        yield return RejectionReasonType.MustNotBeOnTile.ToInstance(unique.Text);
                    break;

                case UniqueType.MustBeNextTo:
                    if (!cityCenter.IsAdjacentTo(unique.Params[0], civ) && !cityCenter.MatchesFilter(unique.Params[0], civ))
                        yield return RejectionReasonType.MustBeNextToTile.ToInstance(unique.Text);
                    break;

                case UniqueType.MustNotBeNextTo:
                    if (cityCenter.GetTilesInDistance(1).Any(it => it.MatchesFilter(unique.Params[0], civ)))
                        yield return RejectionReasonType.MustNotBeNextToTile.ToInstance(unique.Text);
                    break;

                case UniqueType.MustHaveOwnedWithinTiles:
                    if (!cityCenter.GetTilesInDistance(ParseInt(unique.Params[1]))
                        .Any(it => it.MatchesFilter(unique.Params[0], civ) && ReferenceEquals(it.GetOwner(), civ)))
                        yield return RejectionReasonType.MustOwnTile.ToInstance(unique.Text);
                    break;

                case UniqueType.ObsoleteWith:
                    if (civ.Tech.IsResearched(unique.Params[0]))
                        yield return RejectionReasonType.Obsoleted.ToInstance(unique.Text);
                    break;

                case UniqueType.MaxNumberBuildable:
                    if (civ.CivConstructions.CountConstructedObjects(this, cityConstructions.City) >= ParseInt(unique.Params[0]))
                        yield return RejectionReasonType.MaxNumberBuildable.ToInstance();
                    break;

                // To be replaced with `Only available <after [Apollo Project] has been build>`
                case UniqueType.SpaceshipPart:
                    if (!civ.HasUnique(UniqueType.EnablesConstructionOfSpaceshipParts))
                        yield return RejectionReasonType.RequiresBuildingInSomeCity.ToInstance("Apollo project not built!");
                    break;

                case UniqueType.CreatesOneImprovement:
                    var improvement = Ruleset.GetTileImprovement(unique.Params[0]);
                    if (improvement == null)
                    {
                        yield return RejectionReasonType.NoSuchImprovement.ToInstance($"Unknown improvement: {unique.Params[0]}");
                    }
                    else if (!city.GetTiles().Any(it => !it.ImprovementFunctions.CanBuildImprovement(improvement, city.State)))
                    {
                        yield return RejectionReasonType.NoTileCanContainImprovement.ToInstance();
                    }
                    break;
            }
        }

        if (UniqueTo != null && !civ.MatchesFilter(UniqueTo, stateForConditionals))
            yield return RejectionReasonType.UniqueToOtherNation.ToInstance($"Unique to {UniqueTo}");

        if (civ.Cache.UniqueBuildings.Any(it => it.Replaces == Name))
            yield return RejectionReasonType.ReplacedByOurUnique.ToInstance();

        foreach (var requiredTech in ((IHasUniques)this).RequiredTechs())
            if (!civ.Tech.IsResearched(requiredTech))
                yield return RejectionReasonType.RequiresTech.ToInstance($"{requiredTech} not researched!");

        // All Wonders
        if (IsAnyWonder())
        {
            if (civ.Cities.Any(it => it != cityConstructions.City && it.CityConstructions.IsBeingConstructedOrEnqueued(Name)))
                yield return RejectionReasonType.WonderBeingBuiltElsewhere.ToInstance();

            if (civ.IsCityState)
                yield return RejectionReasonType.CityStateWonder.ToInstance();

            if (cityConstructions.City.IsPuppet)
                yield return RejectionReasonType.PuppetWonder.ToInstance();
        }

        // World Wonders
        if (IsWonder)
        {
            if (civ.GameInfo.GetCities().Any(it => it.CityConstructions.IsBuilt(Name)))
                yield return RejectionReasonType.WonderAlreadyBuilt.ToInstance();
        }

        // National Wonders
        if (IsNationalWonder)
        {
            if (civ.Cities.Any(it => it.CityConstructions.IsBuilt(Name)))
                yield return RejectionReasonType.NationalWonderAlreadyBuilt.ToInstance();
        }

        if (RequiredBuilding != null && !cityConstructions.ContainsBuildingOrEquivalent(RequiredBuilding))
        {
            yield return RejectionReasonType.RequiresBuildingInThisCity.ToInstance($"Requires a [{civ.GetEquivalentBuilding(RequiredBuilding).Name}] in this city");
        }

        foreach (var (resourceName, requiredAmount) in GetResourceRequirementsPerTurn(stateForConditionals))
        {
            var availableAmount = cityConstructions.City.GetAvailableResourceAmount(resourceName);
            if (availableAmount < requiredAmount)
            {
                yield return RejectionReasonType.ConsumesResources.ToInstance(resourceName.GetNeedMoreAmountString(requiredAmount - availableAmount));
            }
        }

        // If we've already paid the unit costs, we don't need to pay it again
        if (cityConstructions.GetWorkDone(Name) == 0)
            foreach (var (resourceName, amount) in ((IConstruction)this).GetStockpiledResourceRequirements(stateForConditionals))
            {
                var availableResources = cityConstructions.City.GetAvailableResourceAmount(resourceName);
                if (availableResources < amount)
                    yield return RejectionReasonType.ConsumesResources.ToInstance(resourceName.GetNeedMoreAmountString(amount - availableResources));
            }

        if (RequiredNearbyImprovedResources != null)
        {
            var containsResourceWithImprovement = cityConstructions.City.GetWorkableTiles()
                .Any(it =>
                {
                    var tileResource = it.TileResource;
                    if (tileResource == null) return false;
                    var improvement = it.GetUnpillagedTileImprovement();
                    if (improvement == null) return false;
                    return RequiredNearbyImprovedResources.Contains(tileResource.Name) &&
                        ReferenceEquals(it.GetOwner(), civ) &&
                        (tileResource.IsImprovedBy(improvement.Name) ||
                            it.IsCityCenter() ||
                            (improvement.IsGreatImprovement() && tileResource.ResourceType == ResourceType.Strategic));
                });
            if (!containsResourceWithImprovement)
                yield return RejectionReasonType.RequiresNearbyResource.ToInstance($"Nearby [{string.Join(", ", RequiredNearbyImprovedResources)}] required");
        }

        foreach (var unique in civ.GetMatchingUniques(UniqueType.CannotBuildBuildings, stateForConditionals))
            if (MatchesFilter(unique.Params[0], stateForConditionals))
            {
                yield return RejectionReasonType.CannotBeBuilt.ToInstance();
            }
    }

    /// <summary>
    /// Handles inverted conditional rejections and cumulative conditional reporting
    /// See also <see cref="Unit.BaseUnit"/>.NotMetRejections
    /// </summary>
    private static IEnumerable<RejectionReason> NotMetRejections(UniqueObject unique, CityConstructions cityConstructions, bool built = false)
    {
        var civ = cityConstructions.City.Civ;
        foreach (var conditional in unique.Modifiers)
        {
            // We yield a rejection only when conditionals are NOT met
            if (Conditionals.ConditionalApplies(unique, conditional, cityConstructions.City.State))
                continue;
            switch (conditional.Type)
            {
                case UniqueType.ConditionalBuildingBuiltAmount:
                    var building = civ.GetEquivalentBuilding(conditional.Params[0]).Name;
                    var amount = ParseInt(conditional.Params[1]);
                    var cityFilter = conditional.Params[2];
                    var numberOfCities = civ.Cities.Count(it =>
                        it.CityConstructions.ContainsBuildingOrEquivalent(building) && it.MatchesFilter(cityFilter));
                    if (numberOfCities < amount)
                    {
                        yield return RejectionReasonType.RequiresBuildingInSomeCities.ToInstance(
                            $"Requires a [{building}] in at least [{amount}] of [{cityFilter}] cities" +
                            $" ({numberOfCities}/{numberOfCities})");
                    }
                    break;
                case UniqueType.ConditionalBuildingBuiltAll:
                    var allBuilding = civ.GetEquivalentBuilding(conditional.Params[0]).Name;
                    var allCityFilter = conditional.Params[1];
                    // Kotlin: `it.matchesFilter(cityFilter)` on its own line is a discarded statement, only `!it.isPuppet && ...` is the lambda result
                    if (civ.Cities.Any(it => !it.IsPuppet && !it.CityConstructions.ContainsBuildingOrEquivalent(allBuilding)))
                    {
                        yield return RejectionReasonType.RequiresBuildingInAllCities.ToInstance(
                            $"Requires a [{allBuilding}] in all [{allCityFilter}] cities");
                    }
                    break;
                default:
                    if (built)
                        yield return RejectionReasonType.CanOnlyBeBuiltInSpecificCities.ToInstance(unique.Text);
                    else
                        yield return RejectionReasonType.ShouldNotBeDisplayed.ToInstance();
                    break;
            }
        }
    }

    public bool IsBuildable(CityConstructions cityConstructions) => !GetRejectionReasons(cityConstructions).Any();

    public void Construct(CityConstructions cityConstructions)
    {
        var civInfo = cityConstructions.City.Civ;

        if (civInfo.GameInfo.SpaceResources.Contains(Name))
        {
            civInfo.VictoryManager.CurrentsSpaceshipParts.Add(Name, 1);
        }

        cityConstructions.AddBuilding(this);
    }
}
