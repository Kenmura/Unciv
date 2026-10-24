using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.Utils;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Map.MapUnit.Actions;

// Ported from core/src/com/unciv/logic/map/mapunit/actions/UnitActionModifiers.kt (T079). Stripped: the purity annotations.
public static class UnitActionModifiers
{
    public static bool CanUse(MapUnitClass unit, UniqueObject actionUnique)
    {
        int? usagesLeft = UsagesLeft(unit, actionUnique);
        return usagesLeft == null || usagesLeft > 0;
    }

    public static IEnumerable<UniqueObject> GetUsableUnitActionUniques(MapUnitClass unit, UniqueType actionUniqueType) =>
        unit.GetMatchingUniques(actionUniqueType)
            .Where(unique => !unique.HasModifier(UniqueType.UnitActionExtraLimitedTimes))
            .Where(unique => CanUse(unit, unique));

    private static int GetMovementPointsToUse(MapUnitClass unit, UniqueObject actionUnique, bool defaultAllMovement = false)
    {
        if (actionUnique.HasModifier(UniqueType.UnitActionMovementCostAll))
            return unit.GetMaxMovement();
        int? movementCost = null;
        foreach (var modifier in actionUnique.Modifiers)
        {
            if (modifier.Type != UniqueType.UnitActionMovementCost && modifier.Type != UniqueType.UnitActionMovementCostRequired) continue;
            int cost = modifier.Params[0].ToInt();
            if (movementCost == null || cost > movementCost) movementCost = cost;
        }

        if (movementCost != null)
            return movementCost.Value;
        return defaultAllMovement ? unit.GetMaxMovement() : 1;
    }

    private static int GetMovementPointsRequired(UniqueObject actionUnique)
    {
        if (actionUnique.HasModifier(UniqueType.UnitActionMovementCostAll))
            return 1;
        int? movementCostRequired = null;
        foreach (var modifier in actionUnique.GetModifiers(UniqueType.UnitActionMovementCostRequired))
        {
            int cost = modifier.Params[0].ToInt();
            if (movementCostRequired == null || cost < movementCostRequired) movementCostRequired = cost;
        }

        return movementCostRequired ?? 1;
    }

    /// <summary>
    /// Check if the stat costs in this Action Modifier can be spent by the Civ/Closest City without going into the negatives
    /// </summary>
    private static bool CanSpendStatsCost(MapUnitClass unit, UniqueObject actionUnique)
    {
        foreach (var conditional in actionUnique.GetModifiers(UniqueType.UnitActionStatsCost))
        {
            foreach (var (stat, value) in conditional.Stats)
            {
                var closestCity = unit.GetClosestCity();
                if (closestCity != null)
                {
                    if (!closestCity.HasStatToBuy(stat, KotlinMath.FloatToInt(value)))
                        return false;
                }
                else if (StatExtensions.StatsWithCivWideField.Contains(stat))
                {
                    if (!unit.Civ.HasStatToBuy(stat, KotlinMath.FloatToInt(value)))
                        return false;
                }
                else return false; // no city to spend the Stat
            }
        }

        return true;
    }

    private static bool CanSpendStockpileCost(MapUnitClass unit, UniqueObject actionUnique)
    {
        foreach (var conditional in actionUnique.GetModifiers(UniqueType.UnitActionStockpileCost))
        {
            int amount = conditional.Params[0].ToInt();
            string resourceName = conditional.Params[1];
            if (unit.Civ.GetResourceAmount(resourceName) < amount)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if this Action Unique can be executed, based on action modifiers
    /// </summary>
    /// <param name="unit">The specific unit executing the Action</param>
    /// <param name="actionUnique">Unique that defines the Action</param>
    public static bool CanActivateSideEffects(MapUnitClass unit, UniqueObject actionUnique)
    {
        if (!CanUse(unit, actionUnique)) return false;
        if (GetMovementPointsRequired(actionUnique) > KotlinMath.FloatToInt(MathF.Ceiling(unit.CurrentMovement))) return false;
        if (!CanSpendStatsCost(unit, actionUnique)) return false;
        if (!CanSpendStockpileCost(unit, actionUnique)) return false;
        return true;
    }

    public static void ActivateSideEffects(MapUnitClass unit, UniqueObject actionUnique, bool defaultAllMovement = false)
    {
        int movementCost = GetMovementPointsToUse(unit, actionUnique, defaultAllMovement);
        unit.UseMovementPoints(movementCost);

        foreach (var conditional in actionUnique.Modifiers)
        {
            switch (conditional.Type)
            {
                case UniqueType.UnitActionConsumeUnit:
                    unit.Consume();
                    break;
                case UniqueType.UnitActionLimitedTimes:
                case UniqueType.UnitActionOnce:
                    if (UsagesLeft(unit, actionUnique) == 1
                        && actionUnique.HasModifier(UniqueType.UnitActionAfterWhichConsumed))
                    {
                        unit.Consume();
                        continue;
                    }

                    string key = actionUnique.Text.RemoveConditionals();
                    int usagesSoFar = unit.AbilityToTimesUsed.TryGetValue(key, out int used) ? used : 0;
                    unit.AbilityToTimesUsed[key] = unchecked(usagesSoFar + 1);
                    break;
                case UniqueType.UnitActionStatsCost:
                    // do Stat costs, either Civ-wide or local city
                    // should have validated this doesn't send us negative
                    foreach (var (stat, value) in conditional.Stats)
                    {
                        if (StatExtensions.StatsWithCivWideField.Contains(stat))
                            unit.Civ.AddStat(stat, unchecked(-KotlinMath.FloatToInt(value)));
                        else unit.GetClosestCity()?.AddStat(stat, unchecked(-KotlinMath.FloatToInt(value)));
                    }

                    break;
                case UniqueType.UnitActionStockpileCost:
                    int amount = conditional.Params[0].ToInt();
                    string resourceName = conditional.Params[1];
                    if (unit.Civ.GameInfo.Ruleset.TileResources.TryGetValue(resourceName, out var resource) && resource.IsStockpiled)
                        unit.Civ.GainStockpiledResource(resource, unchecked(-amount));
                    break;
                case UniqueType.UnitActionRemovingPromotion:
                    string promotionName = conditional.Params[0];
                    // if has a status, remove that instead - the promotion is 'safe'
                    if (unit.HasStatus(promotionName))
                        unit.RemoveStatus(promotionName);
                    else // check for real promotion
                        unit.Promotions.RemovePromotion(promotionName);
                    break;
                default:
                    continue;
            }
        }
    }

    /// <summary>Returns 'null' if usages are not limited</summary>
    private static int? UsagesLeft(MapUnitClass unit, UniqueObject actionUnique)
    {
        int? usagesTotal = GetMaxUsages(unit, actionUnique);
        if (usagesTotal == null) return null;
        int usagesSoFar = unit.AbilityToTimesUsed.TryGetValue(actionUnique.Text.RemoveConditionals(), out int used) ? used : 0;
        return unchecked(usagesTotal.Value - usagesSoFar);
    }

    private static int? GetMaxUsages(MapUnitClass unit, UniqueObject actionUnique)
    {
        int extraTimes = 0;
        string text = actionUnique.Text.RemoveConditionals();
        foreach (var unique in unit.GetMatchingUniques(actionUnique.Type.NotNull()))
        {
            if (unique.Text.RemoveConditionals() != text) continue;
            foreach (var modifier in unique.GetModifiers(UniqueType.UnitActionExtraLimitedTimes))
                extraTimes = unchecked(extraTimes + modifier.Params[0].ToInt());
        }

        int? times = null;
        foreach (var modifier in actionUnique.GetModifiers(UniqueType.UnitActionLimitedTimes))
        {
            int limit = modifier.Params[0].ToInt();
            if (times == null || limit > times) times = limit;
        }

        if (times != null) return unchecked(times.Value + extraTimes);
        if (actionUnique.HasModifier(UniqueType.UnitActionOnce)) return unchecked(1 + extraTimes);

        return null;
    }

    public static string ActionTextWithSideEffects(string originalText, UniqueObject actionUnique, MapUnitClass unit)
    {
        string sideEffectString = GetSideEffectString(unit, actionUnique);
        if (sideEffectString == "") return originalText;
        return $"{{{originalText}}} {sideEffectString}";
    }

    public static string GetSideEffectString(MapUnitClass unit, UniqueObject actionUnique, bool defaultAllMovement = false)
    {
        var effects = new List<string>();

        int? maxUsages = GetMaxUsages(unit, actionUnique);
        // Kotlin string templates print a null Int? as "null"
        if (maxUsages != null) effects.Add($"{UsagesLeft(unit, actionUnique)?.ToString() ?? "null"}/{maxUsages}");

        if (actionUnique.HasModifier(UniqueType.UnitActionStatsCost))
        {
            var statCost = new Stats();
            foreach (var conditional in actionUnique.GetModifiers(UniqueType.UnitActionStatsCost))
                statCost.Add(conditional.Stats);
            effects.Add(statCost.ToStringOnlyIcons(false));
        }

        if (actionUnique.HasModifier(UniqueType.UnitActionStockpileCost))
        {
            string stockpileString = "";
            foreach (var conditionals in actionUnique.GetModifiers(UniqueType.UnitActionStockpileCost))
                stockpileString += $" {conditionals.Params[0].ToInt()} {{{conditionals.Params[1]}}}";
            effects.Add(stockpileString.StartsWith(' ') ? stockpileString[1..] : stockpileString); // drop leading space
        }

        if (actionUnique.HasModifier(UniqueType.UnitActionConsumeUnit)
            || (actionUnique.HasModifier(UniqueType.UnitActionAfterWhichConsumed) && UsagesLeft(unit, actionUnique) == 1))
            effects.Add(Fonts.Death.ToString());
        else effects.Add(GetMovementPointsToUse(unit, actionUnique, defaultAllMovement).Tr() + Fonts.Movement);

        foreach (var removes in actionUnique.GetModifiers(UniqueType.UnitActionRemovingPromotion))
        {
            string promotionName = removes.Params[0];
            if (FontRulesetIcons.RulesetObjectNameToChar.TryGetValue(promotionName, out char promotionChar))
                effects.Add($"-{promotionChar}");
        }

        return effects.Count == 0 ? ""
            : $"({string.Join(", ", effects.Select(it => it.Tr()))})";
    }

    public static float GetUseFrequency(MapUnitClass unit, UniqueObject? actionUnique, float defaultValue)
    {
        var modifier = actionUnique?.GetModifiers(UniqueType.UnitActionPriority)
            .FirstOrDefault(it => it.ConditionalsApply(unit.Cache.State));
        if (modifier == null) return defaultValue;

        return modifier.Params[0].ToFloat();
    }
}
