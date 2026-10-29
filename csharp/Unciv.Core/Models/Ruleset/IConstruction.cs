using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using Unciv.Logic.City;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/IConstruction.kt
//
// The members the cost and buy-cost math reads from City, Civilization and CityConstructions are the
// interfaces below; the classes implement them. Member names mirror the Kotlin members.
//

/// <summary>What the construction cost math reads from <c>GameInfo</c> beyond <see cref="IGameContextGameInfo"/>.</summary>
public interface IConstructionGameInfo : IGameContextGameInfo
{
    /// <summary><c>gameInfo.getDifficulty()</c></summary>
    Difficulty GetDifficulty();
}

/// <summary>What the construction cost math reads from <c>Civilization</c>.</summary>
public interface IConstructionCivilization : IGameContextCivilization
{
    /// <summary><c>civ.state</c></summary>
    GameContext State { get; }
    /// <summary><c>civ.gameInfo</c>, never <c>null</c> for a civ that builds things.</summary>
    new IConstructionGameInfo GameInfo { get; }
    bool IsHuman();
    /// <summary><c>civ.getDifficulty()</c></summary>
    Difficulty GetDifficulty();
    /// <summary><c>civ.getEra()</c></summary>
    Era GetEra();
    /// <summary><c>civ.getEquivalentTileImprovement(improvement)</c></summary>
    Tile.TileImprovement GetEquivalentTileImprovement(Tile.TileImprovement improvement);
    /// <summary><c>civ.civConstructions.builtItemsWithIncreasingCost[name]</c></summary>
    int GetBuiltItemsWithIncreasingCost(string name);
    /// <summary><c>civ.civConstructions.boughtItemsWithIncreasingPrice[name]</c></summary>
    int GetBoughtItemsWithIncreasingPrice(string name);
}

/// <summary>What the construction cost math reads from <c>City</c>.</summary>
public interface IConstructionCity : IGameContextCity
{
    /// <summary><c>city.state</c></summary>
    GameContext State { get; }
    new IConstructionCivilization Civ { get; }
    /// <summary><c>city.getMatchingUniques(uniqueType, gameContext)</c>, including the civ's uniques; <c>null</c> is <see cref="State"/>.</summary>
    IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext? gameContext = null);
}

public interface IConstruction : INamed
{
    bool IsBuildable(CityConstructions cityConstructions);
    bool ShouldBeDisplayed(CityConstructions cityConstructions);

    /// <summary>
    /// Gets *per turn* resource requirements - does not include immediate costs for stockpiled resources.
    /// Uses <paramref name="state"/> to determine which civ or city this is built for
    /// </summary>
    Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null);

    IReadOnlySet<string> RequiredResources(GameContext? state = null);

    Counter<string> GetStockpiledResourceRequirements(GameContext state);
}

public interface INonPerpetualConstruction : IConstruction, IHasUniques
{
    int Cost { get; set; }
    int HurryCostModifier { get; }

    // Future development should not increase the role of requiredTech, and should reduce it when possible.
    // https://yairm210.github.io/Unciv/Developers/Translations%2C-mods%2C-and-modding-freedom-in-Open-Source#filters
    string? RequiredTech { get; set; }

    IEnumerable<string> IHasUniques.LegacyRequiredTechs() => RequiredTech == null ? [] : [RequiredTech];

    int GetProductionCost(IConstructionCivilization civInfo, IConstructionCity? city);

    int? GetStatBuyCost(IConstructionCity city, Stat stat);

    IEnumerable<RejectionReason> GetRejectionReasons(CityConstructions cityConstructions);

    /// <summary>Checks if the construction should be purchasable, not whether it can be bought with a stat at all</summary>
    bool IsPurchasable(CityConstructions cityConstructions) =>
        GetRejectionReasons(cityConstructions).All(it => it.Type == RejectionReasonType.Unbuildable);

    /// <summary>Only checks if it has the unique to be bought with this stat, not whether it is purchasable at all</summary>
    bool CanBePurchasedWithStat(IConstructionCity? city, Stat stat) => CanBePurchasedWithStatDefault(this, city, stat);

    /// <summary>The default implementation of <see cref="CanBePurchasedWithStat"/>; C# classes cannot call an overridden default interface method as `super`, so overrides call this.</summary>
    static bool CanBePurchasedWithStatDefault(INonPerpetualConstruction self, IConstructionCity? city, Stat stat) =>
        self.CanBePurchasedWithStatReasons(city, stat).Purchasable();

    /// <summary>Only checks if it has the unique to be bought with this stat, not whether it is purchasable at all</summary>
    PurchaseReason CanBePurchasedWithStatReasons(IConstructionCity? city, Stat stat)
    {
        var self = (IHasUniques)this;
        var gameContext = city?.State ?? GameContext.EmptyState;
        if (stat == Stat.Production || stat == Stat.Happiness) return PurchaseReason.Invalid;
        if (self.HasUnique(UniqueType.CannotBePurchased, gameContext)) return PurchaseReason.Unpurchasable;
        // Can be purchased with [Stat] [cityFilter]
        if (self.GetMatchingUniques(UniqueType.CanBePurchasedWithStat, GameContext.IgnoreConditionalsState)
            .Any(it =>
                it.Params[0] == stat.ToString() &&
                (city == null || (it.ConditionalsApply(gameContext) && city.MatchesFilter(it.Params[1], null)))))
            return PurchaseReason.UniqueAllowed;
        // Can be purchased for [amount] [Stat] [cityFilter]
        if (self.GetMatchingUniques(UniqueType.CanBePurchasedForAmountStat, GameContext.IgnoreConditionalsState)
            .Any(it =>
                it.Params[1] == stat.ToString() &&
                (city == null || (it.ConditionalsApply(gameContext) && city.MatchesFilter(it.Params[2], null)))))
            return PurchaseReason.UniqueAllowed;
        if (stat == Stat.Gold && !self.HasUnique(UniqueType.Unbuildable, gameContext)) return PurchaseReason.Allowed;
        return PurchaseReason.NotAllowed;
    }

    bool CanBePurchasedWithAnyStat(IConstructionCity city) => StatExtensions.StatsUsableToBuy.Any(it => CanBePurchasedWithStat(city, it));

    int GetCivilopediaGoldCost() =>
        // Same as getBaseGoldCost, but without game-specific modifiers
        KotlinMath.DoubleToInt(Math.Pow(30.0 * (double)(float)Cost, 0.75) * (double)HurryCostModifier.ToPercent() / 10) * 10;

    double GetBaseGoldCost(IConstructionCivilization civInfo, IConstructionCity? city) =>
        // https://forums.civfanatics.com/threads/rush-buying-formula.393892/
        Math.Pow(30.0 * GetProductionCost(civInfo, city), 0.75) * (double)HurryCostModifier.ToPercent();

    float? GetBaseBuyCost(IConstructionCity city, Stat stat) => GetBaseBuyCostDefault(this, city, stat);

    /// <summary>The default implementation of <see cref="GetBaseBuyCost"/>; C# classes cannot call an overridden default interface method as `super`, so overrides call this.</summary>
    static float? GetBaseBuyCostDefault(INonPerpetualConstruction self, IConstructionCity city, Stat stat)
    {
        var conditionalState = city.State;

        // Can be purchased for [amount] [Stat] [cityFilter]
        // Kotlin minByOrNull: the first element with the smallest key wins
        UniqueObject? lowestCostUnique = null;
        var lowestCost = 0;
        foreach (var unique in ((IHasUniques)self).GetMatchingUniques(UniqueType.CanBePurchasedForAmountStat, conditionalState))
        {
            if (unique.Params[1] != stat.ToString() || !city.MatchesFilter(unique.Params[2], null)) continue;
            var cost = ParseInt(unique.Params[0]);
            if (lowestCostUnique == null || cost < lowestCost) { lowestCostUnique = unique; lowestCost = cost; }
        }
        if (lowestCostUnique != null) return ParseInt(lowestCostUnique.Params[0]) * city.Civ.GameInfo.Speed.GetStatCostModifier(stat);

        if (stat == Stat.Gold) return (float)self.GetBaseGoldCost(city.Civ, city);

        // Can be purchased with [Stat] [cityFilter]
        if (((IHasUniques)self).GetMatchingUniques(UniqueType.CanBePurchasedWithStat, conditionalState)
            .Any(it => it.Params[0] == stat.ToString() && city.MatchesFilter(it.Params[1], null)))
            return city.Civ.GetEra().BaseUnitBuyCost * city.Civ.GameInfo.Speed.GetStatCostModifier(stat);
        return null;
    }

    int GetCostForConstructionsIncreasingInPrice(int baseCost, int increaseCost, int previouslyBought) =>
        KotlinMath.FloatToInt(baseCost + increaseCost / 2f * (previouslyBought * previouslyBought + previouslyBought));

    /// <summary>We can't call this getMatchingUniques because then it would conflict with IHasUniques</summary>
    IEnumerable<UniqueObject> GetMatchingUniquesNotConflicting(UniqueType uniqueType, GameContext gameContext) =>
        ((IHasUniques)this).GetMatchingUniques(uniqueType, gameContext);

    IReadOnlySet<string> IConstruction.RequiredResources(GameContext? state)
    {
        state ??= GameContext.EmptyState;
        var result = new LinkedHashSet<string>(GetResourceRequirementsPerTurn(state).Keys);
        foreach (var unique in ((IHasUniques)this).GetMatchingUniques(UniqueType.CostsResources, state))
            result.Add(unique.Params[1]);
        return result;
    }

    Counter<string> IConstruction.GetStockpiledResourceRequirements(GameContext state)
    {
        var counter = new Counter<string>();
        foreach (var unique in GetMatchingUniquesNotConflicting(UniqueType.CostsResources, state))
        {
            var amount = ParseInt(unique.Params[0]);
            if (unique.IsModifiedByGameSpeed()) amount = KotlinMath.FloatToInt(amount * state.GameInfo.NotNull().Speed.Modifier);
            counter.Add(unique.Params[1], amount);
        }
        return counter;
    }

    /// <summary>Kotlin <c>String.toInt()</c></summary>
    protected static int ParseInt(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
}

public enum PurchaseReason
{
    Allowed,
    Invalid,
    Unpurchasable,
    UniqueAllowed,
    Other,
    OtherAllowed,
    NotAllowed,
}

public static class PurchaseReasonExtensions
{
    /// <summary>Kotlin <c>PurchaseReason.purchasable</c></summary>
    public static bool Purchasable(this PurchaseReason reason) =>
        reason is PurchaseReason.Allowed or PurchaseReason.UniqueAllowed or PurchaseReason.OtherAllowed;
}

public class RejectionReason(RejectionReasonType type, string? errorMessage = null, bool? shouldShow = null)
{
    public RejectionReasonType Type { get; } = type;
    public string ErrorMessage { get; } = errorMessage ?? type.ErrorMessage();
    public bool ShouldShow { get; } = shouldShow ?? type.ShouldShow();

    public bool TechPolicyEraWonderRequirements() => TechPolicyEraWonderRequirementsSet.Contains(Type);

    public bool HasAReasonToBeRemovedFromQueue() => ReasonsToDefinitivelyRemoveFromQueue.Contains(Type);

    public bool IsImportantRejection() => OrderedImportantRejectionTypes.Contains(Type);

    public bool IsConstructionRejection() => ConstructionRejectionReasonType.Contains(Type);

    public bool IsNeverVisible() => NeverVisible.Contains(Type);

    /// <summary>Returns the index of <see cref="OrderedImportantRejectionTypes"/> with the smallest index having the highest precedence</summary>
    public int GetRejectionPrecedence() => OrderedImportantRejectionTypes.IndexOf(Type);

    // Used for constant variables in the functions above
    private static readonly FrozenSet<RejectionReasonType> TechPolicyEraWonderRequirementsSet =
    new HashSet<RejectionReasonType>
    {
        RejectionReasonType.Obsoleted,
        RejectionReasonType.RequiresTech,
        RejectionReasonType.RequiresPolicy,
        RejectionReasonType.MorePolicyBranches,
        RejectionReasonType.RequiresBuildingInSomeCity,
    }.ToFrozenSet();

    private static readonly FrozenSet<RejectionReasonType> ReasonsToDefinitivelyRemoveFromQueue =
    new HashSet<RejectionReasonType>
    {
        RejectionReasonType.Obsoleted,
        RejectionReasonType.WonderAlreadyBuilt,
        RejectionReasonType.NationalWonderAlreadyBuilt,
        RejectionReasonType.CannotBeBuiltWith,
        RejectionReasonType.MaxNumberBuildable,
    }.ToFrozenSet();

    private static readonly ImmutableArray<RejectionReasonType> OrderedImportantRejectionTypes =
    [
        RejectionReasonType.ShouldNotBeDisplayed,
        RejectionReasonType.WonderBeingBuiltElsewhere,
        RejectionReasonType.RequiresBuildingInAllCities,
        RejectionReasonType.RequiresBuildingInThisCity,
        RejectionReasonType.RequiresBuildingInSomeCity,
        RejectionReasonType.RequiresBuildingInSomeCities,
        RejectionReasonType.CanOnlyBeBuiltInSpecificCities,
        RejectionReasonType.PopulationRequirement,
        RejectionReasonType.ConsumesResources,
        RejectionReasonType.CanOnlyBePurchased,
        RejectionReasonType.MaxNumberBuildable,
        RejectionReasonType.NoPlaceToPutUnit,
    ];

    // Exceptions. Used for units spawned/upgrade path, not built
    private static readonly FrozenSet<RejectionReasonType> ConstructionRejectionReasonType =
    new HashSet<RejectionReasonType>
    {
        RejectionReasonType.Unbuildable,
        RejectionReasonType.CannotBeBuilt,
        RejectionReasonType.CanOnlyBeBuiltInSpecificCities,
        RejectionReasonType.NoTradeRouteSlot,
        RejectionReasonType.NoTradeRouteTarget,
    }.ToFrozenSet();

    private static readonly FrozenSet<RejectionReasonType> NeverVisible =
    new HashSet<RejectionReasonType>
    {
        RejectionReasonType.AlreadyBuilt,
        RejectionReasonType.WonderAlreadyBuilt,
        RejectionReasonType.NationalWonderAlreadyBuilt,
        RejectionReasonType.DisabledBySetting,
        RejectionReasonType.UniqueToOtherNation,
        RejectionReasonType.ReplacedByOurUnique,
        RejectionReasonType.Obsoleted,
        RejectionReasonType.WonderBeingBuiltElsewhere,
        RejectionReasonType.RequiresTech,
        RejectionReasonType.NoSettlerForOneCityPlayers,
        RejectionReasonType.WaterUnitsInCoastalCities,
    }.ToFrozenSet();
}

public enum RejectionReasonType
{
    AlreadyBuilt,
    Unbuildable,
    CanOnlyBePurchased,
    ShouldNotBeDisplayed,

    DisabledBySetting,
    HiddenWithoutVictory,

    MustBeOnTile,
    MustNotBeOnTile,
    MustBeNextToTile,
    MustNotBeNextToTile,
    MustOwnTile,
    WaterUnitsInCoastalCities,
    CanOnlyBeBuiltInSpecificCities,
    MaxNumberBuildable,

    UniqueToOtherNation,
    ReplacedByOurUnique,
    CannotBeBuilt,

    Obsoleted,
    RequiresTech,
    RequiresPolicy,
    MorePolicyBranches,

    RequiresNearbyResource,
    CannotBeBuiltWith,

    RequiresBuildingInThisCity,
    RequiresBuildingInAllCities,
    RequiresBuildingInSomeCities,
    RequiresBuildingInSomeCity,

    NoSuchImprovement,
    NoTileCanContainImprovement,

    WonderAlreadyBuilt,
    NationalWonderAlreadyBuilt,
    WonderBeingBuiltElsewhere,
    CityStateWonder,
    PuppetWonder,
    WonderDisabledEra,

    ConsumesResources,

    PopulationRequirement,

    NoSettlerForOneCityPlayers,
    NoPlaceToPutUnit,

    /// <summary>BNW (B006): every trade route slot is used or held by a trade unit.</summary>
    NoTradeRouteSlot,
    /// <summary>BNW (B006): no city could be the target of this trade unit.</summary>
    NoTradeRouteTarget,
}

public static class RejectionReasonTypeExtensions
{
    private static readonly FrozenDictionary<RejectionReasonType, (bool ShouldShow, string ErrorMessage)> Data = new Dictionary<RejectionReasonType, (bool ShouldShow, string ErrorMessage)>
    {
        [RejectionReasonType.AlreadyBuilt] = (false, "Building already built in this city"),
        [RejectionReasonType.Unbuildable] = (false, "Unbuildable"),
        [RejectionReasonType.CanOnlyBePurchased] = (true, "Can only be purchased"),
        [RejectionReasonType.ShouldNotBeDisplayed] = (false, "Should not be displayed"),

        [RejectionReasonType.DisabledBySetting] = (false, "Disabled by setting"),
        [RejectionReasonType.HiddenWithoutVictory] = (false, "Hidden because a victory type has been disabled"),

        [RejectionReasonType.MustBeOnTile] = (false, "Must be on a specific tile"),
        [RejectionReasonType.MustNotBeOnTile] = (false, "Must not be on a specific tile"),
        [RejectionReasonType.MustBeNextToTile] = (false, "Must be next to a specific tile"),
        [RejectionReasonType.MustNotBeNextToTile] = (false, "Must not be next to a specific tile"),
        [RejectionReasonType.MustOwnTile] = (false, "Must own a specific tile close by"),
        [RejectionReasonType.WaterUnitsInCoastalCities] = (false, "May only built water units in coastal cities"),
        [RejectionReasonType.CanOnlyBeBuiltInSpecificCities] = (false, "Build requirements not met in this city"),
        [RejectionReasonType.MaxNumberBuildable] = (false, "Maximum number have been built or are being constructed"),

        [RejectionReasonType.UniqueToOtherNation] = (false, "Unique to another nation"),
        [RejectionReasonType.ReplacedByOurUnique] = (false, "Our unique replaces this"),
        [RejectionReasonType.CannotBeBuilt] = (false, "Cannot be built by this nation"),

        [RejectionReasonType.Obsoleted] = (false, "Obsolete"),
        [RejectionReasonType.RequiresTech] = (false, "Required tech not researched"),
        [RejectionReasonType.RequiresPolicy] = (false, "Requires a specific policy!"),
        [RejectionReasonType.MorePolicyBranches] = (false, "Hidden until more policy branches are fully adopted"),

        [RejectionReasonType.RequiresNearbyResource] = (false, "Requires a certain resource being exploited nearby"),
        [RejectionReasonType.CannotBeBuiltWith] = (false, "Cannot be built at the same time as another building already built"),

        [RejectionReasonType.RequiresBuildingInThisCity] = (true, "Requires a specific building in this city!"),
        [RejectionReasonType.RequiresBuildingInAllCities] = (true, "Requires a specific building in all cities!"),
        [RejectionReasonType.RequiresBuildingInSomeCities] = (true, "Requires a specific building in more cities!"),
        [RejectionReasonType.RequiresBuildingInSomeCity] = (true, "Requires a specific building anywhere in your empire!"),

        [RejectionReasonType.NoSuchImprovement] = (false, "No such improvement exists in the ruleset"),
        [RejectionReasonType.NoTileCanContainImprovement] = (false, "No tile can contain this improvement"),

        [RejectionReasonType.WonderAlreadyBuilt] = (false, "Wonder already built"),
        [RejectionReasonType.NationalWonderAlreadyBuilt] = (false, "National Wonder already built"),
        [RejectionReasonType.WonderBeingBuiltElsewhere] = (true, "Wonder is being built elsewhere"),
        [RejectionReasonType.CityStateWonder] = (false, "No Wonders for city-states"),
        [RejectionReasonType.PuppetWonder] = (false, "No Wonders for Puppets"),
        [RejectionReasonType.WonderDisabledEra] = (false, "This Wonder is disabled when starting in this era"),

        [RejectionReasonType.ConsumesResources] = (true, "Consumes resources which you are lacking"),

        [RejectionReasonType.PopulationRequirement] = (true, "Requires more population"),

        [RejectionReasonType.NoSettlerForOneCityPlayers] = (false, "No settlers for city-states or one-city challengers"),
        [RejectionReasonType.NoPlaceToPutUnit] = (true, "No space to place this unit"),
        [RejectionReasonType.NoTradeRouteSlot] = (true, "No free trade route slot"),
        [RejectionReasonType.NoTradeRouteTarget] = (true, "No city this unit could trade with"),
    }.ToFrozenDictionary();

    /// <summary>Kotlin <c>defaultInstance</c></summary>
    private static readonly FrozenDictionary<RejectionReasonType, RejectionReason> DefaultInstances =
        Data.ToFrozenDictionary(it => it.Key, it => new RejectionReason(it.Key, it.Value.ErrorMessage, it.Value.ShouldShow));

    public static bool ShouldShow(this RejectionReasonType type) => Data[type].ShouldShow;

    public static string ErrorMessage(this RejectionReasonType type) => Data[type].ErrorMessage;

    public static RejectionReason ToInstance(this RejectionReasonType type) => DefaultInstances[type];

    public static RejectionReason ToInstance(this RejectionReasonType type, string errorMessage, bool? shouldShow = null) =>
        new(type, errorMessage, shouldShow ?? type.ShouldShow());
}
