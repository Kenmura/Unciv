using System.Text.Json.Serialization;
using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;
using UnitTypeObject = Unciv.Models.Ruleset.Unit.UnitType;

namespace Unciv.Models.Ruleset.Unit;

// core/src/com/unciv/models/ruleset/unit/BaseUnit.kt
// This is BaseUnit because Unit is already a base Kotlin class and to avoid mixing the two up
//
// Not ported yet, because they need Civilization, City, CityConstructions, MapUnit, Tile or the description/Civilopedia types (T046 onwards, T034):
// getShortDescription, getDescription, makeLink, getCivilopediaTextLines, getSortGroup, getSubCategory, newMapUnit, getRejectionReasons (and notMetRejections),
// canUnitEnterTile, isBuildable, shouldBeDisplayed, construct, addConstructionBonuses. Because canBePurchasedWithStat reads getRejectionReasons, its
// city overload throws NotSupportedException until then.

/// <summary>
/// This is the basic info of the units, as specified in Units.json,
/// in contrast to MapUnit, which is a specific unit of a certain type that appears on the map
/// </summary>
public class BaseUnit : RulesetObject, INonPerpetualConstruction, IRulesetUnitParameterView
{
    public override string MakeLink() => $"Unit/{Name}";

    private UnitTypeObject? type;
    private bool? isGreatPerson;
    private bool? isMilitary;
    private bool? isLandUnit;
    private bool? isWaterUnit;
    private IRulesetConstructionView? ruleset;
    private int cachedForceEvaluation = -1;
    private readonly Dictionary<string, bool> cachedMatchesFilterResult = new(StringComparer.Ordinal);

    public BaseUnit()
    {
        CostFunctions = new BaseUnitCost(this);
    }

    public int Cost { get; set; } = -1;
    public int HurryCostModifier { get; set; }
    public int Movement { get; set; }
    public int Strength { get; set; }
    public int RangedStrength { get; set; }
    public int ReligiousStrength { get; set; }
    public int Range { get; set; } = 2;
    public int InterceptRange { get; set; }
    public string UnitType { get; set; } = "";

    [JsonIgnore]
    public UnitTypeObject Type =>
        // Kotlin `by lazy` retries after an exception, so this is not a System.Lazy
        type ??= Ruleset.GetUnitType(UnitType)
            ?? throw new InvalidOperationException($"Unit {Name} has unit type {UnitType} which is not present in ruleset!");

    public string? RequiredTech { get; set; }
    public string? RequiredResource { get; set; }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Unit;

    public string ReplacementTextForUniques { get; set; } = "";
    /// <summary>Kotlin <c>HashSet&lt;String&gt;</c>: JVM hash order (see HASH_ORDER_AUDIT.md), it reaches <see cref="EvaluateForce"/>.</summary>
    public JavaHashSet<string> Promotions { get; set; } = new();

    public string? ObsoleteTech { get; set; }

    public IEnumerable<string> TechsThatObsoleteThis() => ObsoleteTech == null ? [] : [ObsoleteTech];
    public IEnumerable<string> TechsAtWhichAutoUpgradeInProduction() => TechsThatObsoleteThis();
    public IEnumerable<string> TechsAtWhichNoLongerAvailable() => TechsThatObsoleteThis();

    public string? UpgradesTo { get; set; }
    public string? Replaces { get; set; }
    public string? UniqueTo { get; set; }
    public string? AttackSound { get; set; }

    [JsonIgnore]
    public BaseUnitCost CostFunctions { get; }

    /// <summary>Kotlin <c>lateinit var ruleset</c></summary>
    [JsonIgnore]
    public IRulesetConstructionView Ruleset =>
        ruleset ?? throw new InvalidOperationException("lateinit property ruleset has not been initialized");

    [JsonIgnore]
    public IReadOnlyList<UniqueObject> RulesetUniqueObjects { get; private set; } = [];

    [JsonIgnore]
    public UniqueMap RulesetUniqueMap { get; private set; } = new();

    public void SetRuleset(IRulesetConstructionView ruleset)
    {
        this.ruleset = ruleset;
        var self = (IHasUniques)this;
        var list = new List<string>(Uniques);
        list.AddRange(ruleset.GlobalUniques.UnitUniques);
        list.AddRange(Type.Uniques);
        RulesetUniqueObjects = self.UniqueObjectsProvider(list);
        RulesetUniqueMap = self.UniqueMapProvider(RulesetUniqueObjects); // Has global uniques by the unique objects already
    }

    public override bool IsUnavailableBySettings(IGameContextGameInfo gameInfo) =>
        base.IsUnavailableBySettings(gameInfo) ||
        (!gameInfo.GameParameters.NuclearWeaponsEnabled && IsNuclearWeapon());

    public IEnumerable<string> GetUpgradeUnits(GameContext? gameContext = null)
    {
        gameContext ??= GameContext.EmptyState;
        if (UpgradesTo != null) yield return UpgradesTo;
        foreach (var unique in ((IHasUniques)this).GetMatchingUniques(UniqueType.CanUpgrade, gameContext))
            yield return unique.Params[0];
    }

    public IEnumerable<BaseUnit> GetRulesetUpgradeUnits(GameContext? gameContext = null)
    {
        foreach (var unit in GetUpgradeUnits(gameContext))
        {
            var found = Ruleset.GetUnit(unit);
            if (found != null) yield return found;
        }
    }

    // region IHasUniques overrides: allow unique functions (getMatchingUniques, hasUnique, forEachMatchingUnique) to "see" uniques from the UnitType

    bool IHasUniques.HasUnique(UniqueType uniqueType, GameContext? state)
    {
        var gameContext = state ?? GameContext.EmptyState;
        return ruleset != null ? RulesetUniqueMap.HasUnique(uniqueType, gameContext)
            : UniqueMap.HasMatchingUnique(uniqueType, gameContext);
    }

    bool IHasUniques.HasTagUnique(string uniqueTag, GameContext? state)
    {
        var gameContext = state ?? GameContext.EmptyState;
        return ruleset != null ? RulesetUniqueMap.HasUnique(uniqueTag, gameContext)
            : UniqueMap.HasMatchingTagUnique(uniqueTag, gameContext);
    }

    bool IHasUniques.HasTagUnique(string uniqueTag) =>
        ruleset != null ? RulesetUniqueMap.HasTagUnique(uniqueTag) : UniqueMap.HasTagUnique(uniqueTag);

    bool IRulesetObjectParameterView.HasTagUnique(string uniqueTag) => ((IHasUniques)this).HasTagUnique(uniqueTag);

    bool IRulesetObjectParameterView.HasTagUniqueIgnoringConditionals(string uniqueTag) =>
        ((IHasUniques)this).HasTagUnique(uniqueTag, GameContext.IgnoreConditionalsState);

    IEnumerable<UniqueObject> IHasUniques.GetMatchingUniques(UniqueType uniqueType, GameContext? state)
    {
        var gameContext = state ?? GameContext.EmptyState;
        return ruleset != null ? RulesetUniqueMap.GetMatchingUniques(uniqueType, gameContext)
            : UniqueMap.GetMatchingUniques(uniqueType, gameContext);
    }

    IEnumerable<UniqueObject> IHasUniques.GetMatchingTagUniques(string uniqueTag, GameContext? state)
    {
        var gameContext = state ?? GameContext.EmptyState;
        return ruleset != null ? RulesetUniqueMap.GetMatchingTagUniques(uniqueTag, gameContext)
            : UniqueMap.GetMatchingTagUniques(uniqueTag, gameContext);
    }

    void IHasUniques.ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Func<UniqueObject, bool> filter, Action<UniqueObject> op)
    {
        if (ruleset != null) RulesetUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, filter, op);
        else UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, filter, op);
    }

    void IHasUniques.ForEachMatchingUnique(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        if (ruleset != null) RulesetUniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
        else UniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
    }

    /// <summary>Kotlin goes through the unique map, conditionals against <c>EmptyState</c>.</summary>
    bool IRulesetUnitParameterView.HasGreatPersonUnique => ((IHasUniques)this).HasUnique(UniqueType.GreatPerson);

    // endregion

    public int GetProductionCost(IConstructionCivilization civInfo, IConstructionCity? city) => CostFunctions.GetProductionCost(civInfo, city);

    public bool CanBePurchasedWithStat(IConstructionCity? city, Stat stat)
    {
        if (city == null) return INonPerpetualConstruction.CanBePurchasedWithStatDefault(this, null, stat);
        // TODO(T106): needs getRejectionReasons(city.cityConstructions); fail loudly instead of answering wrongly.
        throw new NotSupportedException("BaseUnit.CanBePurchasedWithStat(city) needs getRejectionReasons, which is not ported yet");
    }

    public float? GetBaseBuyCost(IConstructionCity city, Stat stat)
    {
        var specificCost = CostFunctions.GetBaseBuyCosts(city, stat).Select(it => (float?)it).Min();
        if (specificCost != null) return specificCost;
        return INonPerpetualConstruction.GetBaseBuyCostDefault(this, city, stat);
    }

    public int? GetStatBuyCost(IConstructionCity city, Stat stat) => CostFunctions.GetStatBuyCost(city, stat);

    public int GetDisbandGold(IConstructionCivilization civInfo) =>
        KotlinMath.DoubleToInt(((INonPerpetualConstruction)this).GetBaseGoldCost(civInfo, null)) / 20;

    /// <summary>This returns the name of the unit this tech upgrades this unit to, or null if there is no automatic upgrade at that tech.</summary>
    public string? AutomaticallyUpgradedInProductionToUnitByTech(string techName)
    {
        foreach (var obsoleteTech in TechsAtWhichAutoUpgradeInProduction())
            if (obsoleteTech == techName)
                return UpgradesTo;
        return null;
    }

    public BaseUnit GetReplacedUnit(IRulesetConstructionView ruleset) =>
        Replaces == null ? this : ruleset.GetUnit(Replaces)!;

    /// <summary>Implements UniqueParameterType.BaseUnitFilter</summary>
    public bool MatchesFilter(string filter, GameContext? state = null, bool multiFilter = true)
    {
        var self = (IHasUniques)this;
        bool Single(string it) =>
            CachedMatchesSingleFilter(it, state) ||
            (state != null && self.HasTagUnique(it, state)) ||
            (state == null && self.HasTagUnique(it));
        return multiFilter ? MultiFilter.Filter(filter, Single) : Single(filter);
    }

    /// <summary>Kotlin <c>cachedMatchesFilterResult.getOrPut</c>: the cache is keyed by the filter alone, whatever the state.</summary>
    private bool CachedMatchesSingleFilter(string filter, GameContext? state)
    {
        if (cachedMatchesFilterResult.TryGetValue(filter, out var cached)) return cached;
        var result = MatchesSingleFilter(filter, state);
        cachedMatchesFilterResult[filter] = result;
        return result;
    }

    public bool MatchesSingleFilter(string filter, GameContext? state = null)
    {
        // all cases are constants for performance
        switch (filter)
        {
            case "all" or "All": return true;
            case "Melee": return IsMelee();
            case "Ranged": return IsRanged();
            case "Civilian": return IsCivilian();
            case "Military": return IsMilitary;
            case "Land": return IsLandUnit;
            case "Water": return IsWaterUnit;
            case "Air": return IsAirUnit();
            case "non-air": return !IsAirUnit();

            case "Nuclear Weapon": return IsNuclearWeapon();
            case "Great Person": return IsGreatPerson;
            case "Religious": return ((IHasUniques)this).HasUnique(UniqueType.ReligiousUnit);

            default:
                if (filter == UnitType) return true;
                if (filter == Name) return true;
                if (filter == Replaces) return true;

                foreach (var requiredTech in ((IHasUniques)this).RequiredTechs())
                    if (Ruleset.TechnologyMatchesFilter(requiredTech, filter, state, false) == true) return true;
                // Uniques using these kinds of filters should be deprecated and replaced with adjective-only parameters
                if (filter.EndsWith(" units", StringComparison.Ordinal)
                    // "military units" --> "Military", using invariant locale
                    && MatchesFilter(UppercaseFirst(filter[..^" units".Length].ToLowerInvariant()), multiFilter: false))
                    return true;
                return false;
        }
    }

    private static string UppercaseFirst(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Determine whether this is a City-founding unit - abstract, **without any game context**.
    /// Use other methods for MapUnits or when there is a better GameContext available.
    /// </summary>
    public bool IsCityFounder() => ((IHasUniques)this).HasUnique(UniqueType.FoundCity, GameContext.IgnoreConditionalsState);

    [JsonIgnore]
    // Kotlin `by lazy` retries after an exception, so these are not System.Lazy
    public bool IsGreatPerson => isGreatPerson ??= ((IHasUniques)this).GetMatchingUniques(UniqueType.GreatPerson).Any();

    public bool IsGreatPersonOfType(string type) =>
        ((IHasUniques)this).GetMatchingUniques(UniqueType.GreatPerson).Any(it => it.Params[0] == type);

    /// <summary>Has a MapUnit implementation that does not ignore conditionals, which should be usually used</summary>
    private bool IsNuclearWeapon() => ((IHasUniques)this).HasUnique(UniqueType.NuclearWeapon, GameContext.IgnoreConditionalsState);

    /// <summary>Returns resource requirements from both uniques and requiredResource field</summary>
    public Counter<string> GetResourceRequirementsPerTurn(GameContext? state = null)
    {
        var resourceRequirements = new Counter<string>();
        if (RequiredResource != null) resourceRequirements[RequiredResource] = 1;
        foreach (var unique in ((IHasUniques)this).GetMatchingUniques(UniqueType.ConsumesResources, state ?? GameContext.EmptyState))
            resourceRequirements.Add(unique.Params[1], ParseInt(unique.Params[0]));
        return resourceRequirements;
    }

    public bool IsRanged() => RangedStrength > 0;
    public bool IsMelee() => !IsRanged() && Strength > 0;

    [JsonIgnore]
    public bool IsMilitary => isMilitary ??= IsRanged() || IsMelee();

    public bool IsCivilian() => !IsMilitary;

    [JsonIgnore]
    public bool IsLandUnit => isLandUnit ??= Type.IsLandUnit();

    [JsonIgnore]
    public bool IsWaterUnit => isWaterUnit ??= Type.IsWaterUnit();

    public bool IsAirUnit() => Type.IsAirUnit();

    public bool IsProbablySiegeUnit() => IsRanged()
        && ((IHasUniques)this).GetMatchingUniques(UniqueType.Strength, GameContext.IgnoreConditionalsState)
            .Any(it => ParseInt(it.Params[0]) > 0 && it.HasModifier(UniqueType.ConditionalVsCity));

    public int GetForceEvaluation()
    {
        if (cachedForceEvaluation < 0)
            cachedForceEvaluation = EvaluateForce();
        return cachedForceEvaluation;
    }

    private int EvaluateForce()
    {
        if (Strength == 0 && RangedStrength == 0) return 0;

        var power = FloatPow(Strength, 1.5f);
        var rangedPower = FloatPow(RangedStrength, 1.45f);

        // Value ranged naval units less
        if (IsWaterUnit)
        {
            rangedPower /= 2;
        }
        if (rangedPower > 0)
            power = rangedPower;

        // Replicates the formula from civ V, which is a lower multiplier than probably intended, because math
        // They did fix it in BNW so it was completely bugged and always 1, again math
        power = power * FloatPow(Movement, 0.3f);

        var self = (IHasUniques)this;
        if (self.HasUnique(UniqueType.SelfDestructs))
            power /= 2;
        if (IsNuclearWeapon())
            power += 4000;

        // Uniques
        var allUniques = RulesetUniqueObjects.Concat(
            Promotions.Select(it => Ruleset.GetPromotion(it)).OfType<Promotion>().SelectMany(it => it.UniqueObjects));

        // When we have multiple conditional strength bonuses, only the highest one counts
        // Otherwise we get massive overvaluation of units with many conflicting conditionals
        var highestConditionalPowerBonus = 1f;

        foreach (var unique in allUniques)
        {
            switch (unique.Type)
            {
                case UniqueType.Strength:
                    if (ParseInt(unique.Params[0]) <= 0) continue;

                    if (unique.HasModifier(UniqueType.ConditionalVsUnits)) // Bonus vs some units - a quarter of the bonus
                    {
                        highestConditionalPowerBonus = (ParseInt(unique.Params[0]) / 4f).ToPercent();
                    }
                    else if (unique.Modifiers.Any(it =>
                                 it.Type == UniqueType.ConditionalVsCity // City Attack - half the bonus
                                 || it.Type == UniqueType.ConditionalAttacking // Attack - half the bonus
                                 || it.Type == UniqueType.ConditionalDefending // Defense - half the bonus
                                 || it.Type == UniqueType.ConditionalFightingInTiles)) // Bonus in terrain or feature - half the bonus
                    {
                        highestConditionalPowerBonus = (ParseInt(unique.Params[0]) / 2f).ToPercent();
                    }
                    else
                    {
                        highestConditionalPowerBonus = ParseInt(unique.Params[0]).ToPercent(); // Static bonus
                    }
                    break;

                case UniqueType.StrengthNearCapital:
                    if (ParseInt(unique.Params[0]) > 0)
                        power *= (ParseInt(unique.Params[0]) / 4f).ToPercent(); // Bonus decreasing with distance from capital - not worth much most of the map???
                    break;

                case UniqueType.MayParadrop: // Paradrop - 25% bonus
                    power *= 1.25f;
                    break;
                case UniqueType.MustSetUp: // Must set up - 20 % penalty
                    power /= 1.20f;
                    break;
                // Extra attacks - 20% bonus per extra attack (if sufficient movement)
                case UniqueType.AdditionalAttacks:
                    var additionalAttacks = ParseInt(unique.Params[0]);
                    var limit = IsAirUnit() ? int.MaxValue : Movement - 1;
                    power *= (20f * Math.Min(additionalAttacks, limit)).ToPercent();
                    break;
            }
        }
        power *= highestConditionalPowerBonus;

        return KotlinMath.FloatToInt(power);
    }

    /// <summary>Kotlin <c>Float.pow(Float)</c> (via <c>Math.pow</c> on doubles)</summary>
    private static float FloatPow(float value, float exponent) => (float)Math.Pow(value, exponent);

    /// <summary>Kotlin <c>String.toInt()</c></summary>
    private static int ParseInt(string text) => int.Parse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture);
}
