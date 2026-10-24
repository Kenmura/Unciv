using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapUnits;

// Ported from core/src/com/unciv/logic/map/mapunit/UnitPromotions.kt (T066). Stripped: the purity annotations.

/// <summary>
/// A promotion that may appear in a promotion tree for a unit.
/// </summary>
/// <param name="Promotion">the promotion</param>
/// <param name="IsAdopted">The unit already has this promotion</param>
/// <param name="IsBlockedByUniques">The promotion's own uniques currently disallow picking it (<c>Unavailable</c>, or unmet <c>OnlyAvailable</c> conditionals)</param>
public sealed record AvailablePromotion(Promotion Promotion, bool IsAdopted, bool IsBlockedByUniques);

public class UnitPromotions : IsPartOfGameInfoSerialization
{
    // Having this as mandatory constructor parameter would be safer, but this class is part of a
    // saved game and as usual the json deserializer needs a default constructor.
    // Initialization occurs in setTransients() - called as part of MapUnit.setTransients,
    // or copied in clone() as part of the UnitAction `Upgrade`.
    private MapUnit? unit;
    private MapUnit Unit => unit ?? throw new InvalidOperationException("lateinit property unit has not been initialized");

    /// <summary>Experience this unit has accumulated on top of the last promotion</summary>
    [JsonPropertyName("XP")]
    public int XP { get; set; }

    /// <summary>The _names_ of the promotions this unit has acquired - see <see cref="GetPromotions"/> for object access</summary>
    [JsonPropertyName("promotions")]
    [JsonInclude]
    public JavaHashSet<string> Promotions { get; private set; } = new();

    // some promotions don't come from being promoted but from other things,
    // like from being constructed in a specific city etc.
    /// <summary>The number of times this unit has been promoted using experience, not counting free promotions</summary>
    [JsonPropertyName("numberOfPromotions")]
    public int NumberOfPromotions { get; set; }

    /// <summary>Gets this unit's promotions as objects.</summary>
    /// <param name="sorted">if <c>true</c> return the promotions in json order (<c>false</c> gives hashset order) for display.</param>
    public IEnumerable<Promotion> GetPromotions(bool sorted = false)
    {
        if (Promotions.Count == 0) yield break;
        var unitPromotions = Unit.Civ.GameInfo.Ruleset.UnitPromotions;
        if (sorted && Promotions.Count > 1)
        {
            foreach (var promotion in unitPromotions.Values)
                if (Promotions.Contains(promotion.Name)) yield return promotion;
        }
        else
        {
            foreach (var name in Promotions)
            {
                if (unitPromotions.TryGetValue(name, out var promotion)) yield return promotion;
            }
        }
    }

    public void SetTransients(MapUnit unit) => this.unit = unit;

    /// <returns>the XP points needed to "buy" the next promotion. 10, 30, 60, 100, 150,...</returns>
    public int XpForNextPromotion() => XpCostForPromotionNumber(unchecked(NumberOfPromotions + 1));

    /// <returns>the XP points needed to "buy" the next [count] promotions.</returns>
    public int XpForNextNPromotions(int count)
    {
        var sum = 0;
        for (var it = 1; it <= count; it++) sum = unchecked(sum + XpCostForPromotionNumber(unchecked(NumberOfPromotions + it)));
        return sum;
    }

    /// <returns>the final XP cost for a specific promotion number, including modifiers and rounding</returns>
    private int XpCostForPromotionNumber(int promotionNumber)
    {
        var baseXpForPromotion = unchecked(promotionNumber * 10);
        return KotlinMath.FloatToInt(unchecked(baseXpForPromotion * PromotionCostModifier()));
    }

    private float PromotionCostModifier()
    {
        var totalPromotionCostModifier = 1f;
        Unit.Civ.ForEachMatchingUnique(UniqueType.XPForPromotionModifier, unique =>
        {
            totalPromotionCostModifier *= unique.Params[0].ToPercent();
        });
        // base case if you don't have any the unique that reduce or higher the promotion cost
        return totalPromotionCostModifier;
    }

    /// <returns>Total XP including that already "spent" on promotions</returns>
    public int TotalXpProduced() => unchecked(XP + (NumberOfPromotions * (NumberOfPromotions + 1)) * 5);

    /// <returns>Combined value of all promotions and XP = Number of promotions if all xp is spent + number of free promotions + progress to next promotion</returns>
    public float ValueOfPromotionsAndXp()
    {
        /*
        Consider a unit with:
        - 2 chosen promotions
        - 1 free promotion
        - 50 unspent XP (next promotion costs 30 XP)

        Assume the unit spends all its XP on promotions:
        - 3 chosen promotions
        - 1 free promotion
        - 20 unspent XP (next promotion costs 40 XP)

        It now has 4 promotions + 50% of the way to the next promotion = a score of 4.50
         */
        var effectiveNumberOfPromotions = NumberOfPromotions;
        var remainingFreeXp = XP;
        while (true)
        {
            var nextPromotionCost = XpCostForPromotionNumber(unchecked(effectiveNumberOfPromotions + 1));
            if (remainingFreeXp < nextPromotionCost)
                break;
            effectiveNumberOfPromotions++;
            remainingFreeXp = unchecked(remainingFreeXp - nextPromotionCost);
        }
        var progressToNextPromotion = (float)remainingFreeXp / XpCostForPromotionNumber(unchecked(effectiveNumberOfPromotions + 1));
        var numberOfFreePromotions = Promotions.Count - NumberOfPromotions;
        return unchecked(effectiveNumberOfPromotions + numberOfFreePromotions + progressToNextPromotion);
    }

    public bool CanBePromoted()
    {
        if (!GetAvailablePromotions().Any()) return false;
        if (XP >= XpForNextPromotion()) return true;
        return GetAvailablePromotions().Any(it => ((IHasUniques)it).HasUnique(UniqueType.FreePromotion));
    }

    public void AddPromotion(string promotionName, bool isFree = false)
    {
        if (Promotions.Contains(promotionName)) return;
        var ruleset = Unit.Civ.GameInfo.Ruleset;
        if (!ruleset.UnitPromotions.TryGetValue(promotionName, out var promotion)) return;

        if (!isFree)
        {
            if (!((IHasUniques)promotion).HasUnique(UniqueType.FreePromotion))
            {
                XP = unchecked(XP - XpForNextPromotion());
                NumberOfPromotions++;
            }

            foreach (var unique in Unit.GetTriggeredUniques(UniqueType.TriggerUponPromotion))
                UniqueTriggerActivation.TriggerUnique(unique, Unit);
        }

        foreach (var unique in Unit.GetTriggeredUniques(UniqueType.TriggerUponPromotionGain, null, it => it.Params[0] == promotionName))
            UniqueTriggerActivation.TriggerUnique(unique, Unit);

        if (!((IHasUniques)promotion).HasUnique(UniqueType.SkipPromotion))
            Promotions.Add(promotionName);

        // If we upgrade this unit to its new version, we already need to have this promotion added,
        // so this has to go after the `promotions.add(promotionname)` line.
        DoDirectPromotionEffects(promotion);

        Unit.UpdateUniques();

        // Since some units get promotions upon construction, they will get the addPromotion from the unit.postBuildEvent
        // upon creation, BEFORE they are assigned to a tile, so the updateVisibleTiles() would crash.
        // So, if the addPromotion was triggered from there, simply don't update
        Unit.UpdateVisibleTiles(); // some promotions/uniques give the unit bonus sight
    }

    public void RemovePromotion(string promotionName)
    {
        var ruleset = Unit.Civ.GameInfo.Ruleset;
        var promotion = ruleset.UnitPromotions[promotionName];

        if (GetPromotions().Contains(promotion))
        {
            Promotions.Remove(promotionName);
            Unit.UpdateUniques();
            Unit.UpdateVisibleTiles();

            foreach (var unique in Unit.GetTriggeredUniques(UniqueType.TriggerUponPromotionLoss, null, it => it.Params[0] == promotionName))
                UniqueTriggerActivation.TriggerUnique(unique, Unit);
        }
    }

    private void DoDirectPromotionEffects(Promotion promotion)
    {
        foreach (var unique in ((IHasUniques)promotion).UniqueObjects)
        {
            if (!unique.ConditionalsApply(Unit.Cache.State) || unique.HasTriggerConditional()) continue;
            var times = unique.GetUniqueMultiplier(Unit.Cache.State);
            for (var i = 0; i < times; i++)
                UniqueTriggerActivation.TriggerUnique(unique, Unit, triggerNotificationText: $"due to our [{Unit.Name}] being promoted");
        }
    }

    /// <summary>Gets all promotions this unit could currently "buy" with enough <see cref="XP"/>. Checks unit type, already acquired promotions, prerequisites and incompatibility uniques.</summary>
    public IEnumerable<Promotion> GetAvailablePromotions() =>
        Unit.Civ.GameInfo.Ruleset.UnitPromotions.Values.Where(IsAvailable);

    /// <summary>All promotions this unit could have in its promotion tree: those matching its unit type, plus those already adopted.</summary>
    public List<AvailablePromotion> GetPromotionTreeCandidates()
    {
        var state = Unit.Cache.State;
        return Unit.Civ.GameInfo.Ruleset.UnitPromotions.Values
            .Where(it => it.UnitTypes.Contains(Unit.BaseUnit.UnitType) || Promotions.Contains(it.Name))
            .Select(promotion =>
            {
                var blocked = ((IHasUniques)promotion).HasUnique(UniqueType.Unavailable, state)
                    || ((IHasUniques)promotion).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
                        .Any(it => !it.ConditionalsApply(state));
                return new AvailablePromotion(promotion, Promotions.Contains(promotion.Name), blocked);
            })
            .ToList();
    }

    public bool CanAffordPromotions(int count) => XP >= XpForNextNPromotions(count);

    private bool IsAvailable(Promotion promotion)
    {
        if (Promotions.Contains(promotion.Name)) return false;
        if (!promotion.UnitTypes.Contains(Unit.Type.Name)) return false;
        if (promotion.Prerequisites.Count != 0 && !promotion.Prerequisites.Any(it => Promotions.Contains(it))) return false;

        var stateForConditionals = Unit.Cache.State;
        if (((IHasUniques)promotion).HasUnique(UniqueType.Unavailable, stateForConditionals)) return false;
        if (((IHasUniques)promotion).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState)
            .Any(it => !it.ConditionalsApply(stateForConditionals))) return false;
        return true;
    }

    public UnitPromotions Clone()
    {
        var toReturn = new UnitPromotions
        {
            XP = XP,
            Promotions = new JavaHashSet<string>(Promotions),
            NumberOfPromotions = NumberOfPromotions,
        };
        return toReturn;
    }

    public UnitPromotions Clone(MapUnit unit)
    {
        var toReturn = Clone();
        toReturn.unit = unit;
        return toReturn;
    }

    // For json serialization, to not serialize an empty object
    public override bool Equals(object? obj) =>
        obj is UnitPromotions other && XP == other.XP && Promotions.Count == other.Promotions.Count && Promotions.All(other.Promotions.Contains) && NumberOfPromotions == other.NumberOfPromotions;

    public override int GetHashCode() => HashCode.Combine(XP, NumberOfPromotions, Promotions.Count);
}
