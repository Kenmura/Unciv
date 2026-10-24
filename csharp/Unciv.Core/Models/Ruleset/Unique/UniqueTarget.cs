namespace Unciv.Models.Ruleset.Unique;

/// <summary>
/// Whether a <c>UniqueType</c> is allowed in the <c>&lt;conditional or trigger&gt;</c> part — or not.
/// <see cref="None"/> ensures use only as leading Unique; <see cref="Conditional"/> and <see cref="Other"/>
/// disallow use as leading Unique.
/// </summary>
public enum UniqueTargetModifierType
{
    None = 0,
    Conditional = 1,
    Other = 2,
}

/// <summary>
/// Minimal view of a <c>Unique</c> needed by <see cref="UniqueTargetExtensions.IsAcceptableModifierFor"/>.
/// Implemented by <c>Unique</c> in T027.
/// </summary>
public interface IUniqueTargetView
{
    bool IsTriggerable { get; }
    UniqueTarget? SourceObjectType { get; }
    IReadOnlyCollection<UniqueTarget>? TargetTypes { get; }
}

/// <summary>
/// Expresses which RulesetObject types a UniqueType is applicable to.
/// Ported from <c>core/src/com/unciv/models/ruleset/unique/UniqueTarget.kt</c>.
/// </summary>
public enum UniqueTarget
{
    Triggerable = 0,
    UnitTriggerable = 1,
    Global = 2,

    // Civilization-specific
    Nation = 3,
    Personality = 4,
    Era = 5,
    Tech = 6,
    Policy = 7,
    FounderBelief = 8,
    FollowerBelief = 9,

    // City-specific
    Building = 10,
    Wonder = 11,

    // Unit-specific
    UnitAction = 12,
    Unit = 13,
    UnitType = 14,
    Promotion = 15,

    // Tile-specific
    Terrain = 16,
    Improvement = 17,
    Resource = 18,
    Ruins = 19,

    // Other
    GlobalUniques = 20,
    Speed = 21,
    Difficulty = 22,
    Tutorial = 23,
    CityState = 24,
    ModOptions = 25,
    Event = 26,
    EventChoice = 27,
    Victory = 28,

    // Modifiers
    Conditional = 29,
    TriggerCondition = 30,
    UnitTriggerCondition = 31,
    UnitActionModifier = 32,
    MetaModifier = 33,
}

public static class UniqueTargetExtensions
{
    /// <summary>All targets that can display their Uniques.</summary>
    public static readonly IReadOnlyList<UniqueTarget> Displayable =
    [
        UniqueTarget.Building,
        UniqueTarget.Unit,
        UniqueTarget.UnitType,
        UniqueTarget.Improvement,
        UniqueTarget.Tech,
        UniqueTarget.FollowerBelief,
        UniqueTarget.FounderBelief,
        UniqueTarget.Terrain,
        UniqueTarget.Resource,
        UniqueTarget.Policy,
        UniqueTarget.Promotion,
        UniqueTarget.Nation,
        UniqueTarget.Ruins,
        UniqueTarget.Speed,
        UniqueTarget.EventChoice,
        UniqueTarget.Difficulty,
    ];

    public static readonly IReadOnlyList<UniqueTarget> CanIncludeSuppression =
    [
        UniqueTarget.Triggerable,
        UniqueTarget.Terrain,
        UniqueTarget.Speed,
        UniqueTarget.ModOptions,
        UniqueTarget.MetaModifier,
    ];

    public static string DocumentationString(this UniqueTarget target) => target switch
    {
        UniqueTarget.Triggerable =>
            "Uniques that have immediate, one-time effects. " +
            "These can be added to techs to trigger when researched, to policies to trigger when adopted, " +
            "to eras to trigger when reached, to buildings to trigger when built. " +
            "Alternatively, you can add a TriggerCondition to them to make them into Global uniques that activate upon a specific event." +
            "They can also be added to units to grant them the ability to trigger this effect as an action, " +
            "which can be modified with UnitActionModifier and UnitTriggerCondition conditionals.",
        UniqueTarget.UnitTriggerable =>
            "Uniques that have immediate, one-time effects on a unit." +
            "They can be added to units (on unit, unit type, or promotion) to grant them the ability to trigger this effect as an action, " +
            "which can be modified with UnitActionModifier and UnitTriggerCondition conditionals.",
        UniqueTarget.Global =>
            "Uniques that apply globally. " +
            "Civs gain the abilities of these uniques from nation uniques, reached eras, researched techs, adopted policies, " +
            "built buildings, religion 'founder' uniques, owned resources, and ruleset-wide global uniques.",
        UniqueTarget.FounderBelief =>
            "Uniques for Founder and Enhancer type Beliefs, that will apply to the founder of this religion",
        UniqueTarget.FollowerBelief =>
            "Uniques for Pantheon and Follower type beliefs, that will apply to each city where the religion is the majority religion",
        UniqueTarget.UnitAction =>
            "Uniques that affect a unit's actions, and can be modified by UnitActionModifiers",
        UniqueTarget.Unit =>
            "Uniques that can be added to units, unit types, or promotions",
        UniqueTarget.Speed =>
            "Speed uniques will be treated as part of GlobalUniques for the Speed selected in a game",
        UniqueTarget.Difficulty =>
            "Difficulty uniques will be treated as part of GlobalUniques for the Difficulty selected in a game",
        UniqueTarget.Victory =>
            "Uniques that determine whether a victory can be achieved, and by whom",
        UniqueTarget.Conditional =>
            "Modifiers that can be added to other uniques to limit when they will be active",
        UniqueTarget.TriggerCondition =>
            "Special conditionals that can be added to Triggerable uniques, to make them activate upon specific actions.",
        UniqueTarget.UnitTriggerCondition =>
            "Special conditionals that can be added to UnitTriggerable uniques, to make them activate upon specific actions.",
        UniqueTarget.UnitActionModifier =>
            "Modifiers that can be added to UnitAction uniques as conditionals",
        UniqueTarget.MetaModifier =>
            "Modifiers that can be added to other uniques changing user experience, not their behavior",
        _ => "",
    };

    public static UniqueTarget? InheritsFrom(this UniqueTarget target) => target switch
    {
        UniqueTarget.UnitTriggerable => UniqueTarget.Triggerable,
        UniqueTarget.Global => UniqueTarget.Triggerable,
        UniqueTarget.Nation => UniqueTarget.Global,
        UniqueTarget.Era => UniqueTarget.Global,
        UniqueTarget.Tech => UniqueTarget.Global,
        UniqueTarget.Policy => UniqueTarget.Global,
        UniqueTarget.FounderBelief => UniqueTarget.Global,
        UniqueTarget.FollowerBelief => UniqueTarget.Triggerable,
        UniqueTarget.Building => UniqueTarget.Global,
        UniqueTarget.Wonder => UniqueTarget.Building,
        UniqueTarget.UnitAction => UniqueTarget.UnitTriggerable,
        UniqueTarget.Unit => UniqueTarget.UnitAction,
        UniqueTarget.UnitType => UniqueTarget.Unit,
        UniqueTarget.Promotion => UniqueTarget.Unit,
        UniqueTarget.Improvement => UniqueTarget.Triggerable,
        UniqueTarget.Resource => UniqueTarget.Global,
        UniqueTarget.Ruins => UniqueTarget.UnitTriggerable,
        UniqueTarget.GlobalUniques => UniqueTarget.Global,
        UniqueTarget.Speed => UniqueTarget.GlobalUniques,
        UniqueTarget.Difficulty => UniqueTarget.GlobalUniques,
        UniqueTarget.CityState => UniqueTarget.Global,
        UniqueTarget.EventChoice => UniqueTarget.UnitTriggerable,
        _ => null,
    };

    public static UniqueTargetModifierType ModifierType(this UniqueTarget target) => target switch
    {
        UniqueTarget.Conditional => UniqueTargetModifierType.Conditional,
        UniqueTarget.TriggerCondition
            or UniqueTarget.UnitTriggerCondition
            or UniqueTarget.UnitActionModifier
            or UniqueTarget.MetaModifier => UniqueTargetModifierType.Other,
        _ => UniqueTargetModifierType.None,
    };

    /// <summary>
    /// Checks whether a specific <see cref="UniqueTarget"/> <paramref name="target"/> works with
    /// <paramref name="uniqueTarget"/> as declared in <c>UniqueType</c>.
    /// For example, <c>Building.CanAcceptUniqueTarget(Global) == true</c>,
    /// <c>Global.CanAcceptUniqueTarget(Building) == false</c>.
    /// </summary>
    public static bool CanAcceptUniqueTarget(this UniqueTarget target, UniqueTarget uniqueTarget)
    {
        if (target == uniqueTarget) return true;
        var parent = target.InheritsFrom();
        return parent != null && parent.Value.CanAcceptUniqueTarget(uniqueTarget);
    }

    public static bool IsAcceptableModifierFor(this UniqueTarget target, IUniqueTargetView unique) => target switch
    {
        UniqueTarget.TriggerCondition => unique.IsTriggerable,
        UniqueTarget.UnitTriggerCondition => IsAcceptableUnitTriggerCondition(unique),
        UniqueTarget.UnitActionModifier => IsAcceptableUnitActionModifier(unique),
        _ => true,
    };

    private static bool IsAcceptableUnitTriggerCondition(IUniqueTargetView unique)
    {
        var targetTypes = unique.TargetTypes;
        if (targetTypes == null) return false;

        if (unique.IsTriggerable && unique.SourceObjectType?.CanAcceptUniqueTarget(UniqueTarget.Unit) == true)
            return true;

        return targetTypes.Any(it => UniqueTarget.UnitTriggerable.CanAcceptUniqueTarget(it));
    }

    private static bool IsAcceptableUnitActionModifier(IUniqueTargetView unique)
    {
        var targetTypes = unique.TargetTypes;
        if (targetTypes == null) return false;

        return targetTypes.Any(it => UniqueTarget.UnitAction.CanAcceptUniqueTarget(it));
    }
}
