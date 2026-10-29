using System.Runtime.CompilerServices;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models;

// Ported from core/src/com/unciv/models/UnitAction.kt (T079). The action lambda is an Action delegate; the UI-facing fields stay plain data.

/// <summary>
/// Asks the player to confirm before a unit action is executed. Handled by the UI, direct invocation (e.g. automation) skips it.
/// </summary>
/// <param name="question">The text shown in the popup</param>
/// <param name="confirmText">The text for the confirm button</param>
public class UnitActionConfirmation(string question, string confirmText)
{
    public string Question { get; } = question;
    public string ConfirmText { get; } = confirmText;
}

/// <summary>
/// Unit Actions - class - carries dynamic data and actual execution.
/// Static properties are in <see cref="UnitActionType"/>.
/// Note this is for the buttons offering actions, not the ongoing action stored with a <see cref="MapUnit"/>.
/// </summary>
/// <param name="type">the action type</param>
/// <param name="title">defaults to <c>type.value</c> when <c>null</c></param>
/// <param name="isCurrentAction"></param>
/// <param name="associatedUnique">The unique this action stems from, if any - the UI takes its <c>UnitActionPriority</c> modifiers from here</param>
/// <param name="confirmation">If not null, the UI asks for confirmation before invoking <see cref="Action"/></param>
/// <param name="key">Stable parameter that tells apart several actions of the same type (T189), see <see cref="Key"/></param>
/// <param name="action"><c>null</c> if this unit *can* execute the action but *not right now* - it's embarked, out of moves, etc</param>
public class UnitAction(
    UnitActionType type,
    string? title = null,
    bool isCurrentAction = false,
    UniqueObject? associatedUnique = null,
    UnitActionConfirmation? confirmation = null,
    Action? action = null,
    string? key = null)
{
    public UnitActionType Type { get; } = type;
    public string Title { get; } = title ?? type.Value();
    public bool IsCurrentAction { get; } = isCurrentAction;
    public UniqueObject? AssociatedUnique { get; } = associatedUnique;
    public UnitActionConfirmation? Confirmation { get; } = confirmation;
    public Action? Action { get; } = action;

    /// <summary>
    /// Engine addition (T189, the Kotlin has nothing like it): closures cannot be sent over the wire, so a client names an action by
    /// (<see cref="Type"/>, Key, index among equal ones). The key is the parameter that is stable across state changes:
    /// the improvement for <c>CreateImprovement</c>, the target unit for <c>Upgrade</c>, else the text of the associated unique.
    /// Never the title, which is a translation template with icons. <c>null</c> when the type has only one action per unit.
    /// </summary>
    public virtual string? Key => key ?? AssociatedUnique?.Text;

    // Kotlin compares the lambdas by reference
    public override bool Equals(object? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is not UnitAction otherAction) return false;

        if (Type != otherAction.Type) return false;
        if (IsCurrentAction != otherAction.IsCurrentAction) return false;
        if (!ReferenceEquals(Action, otherAction.Action)) return false;

        return true;
    }

    public override int GetHashCode() =>
        Randomness.HashOf((int)Type, IsCurrentAction ? 1231 : 1237, Action == null ? 0 : RuntimeHelpers.GetHashCode(Action));

    public override string ToString() => $"UnitAction(type={Type}, title='{Title}', isCurrentAction={IsCurrentAction})";
}

/// <summary>
/// Specialized <see cref="UnitAction"/> for upgrades.
/// Transports <see cref="UnitToUpgradeTo"/> from creation (<c>UnitActionsUpgrade.GetUpgradeActions</c>) to the UI.
/// </summary>
public class UpgradeUnitAction(string title, PossibleUpgrade upgrade, Action? action)
    : UnitAction(UnitActionType.Upgrade, title, associatedUnique: upgrade.Unique, action: action)
{
    public override string? Key => UnitToUpgradeTo.Name;

    public PossibleUpgrade Upgrade { get; } = upgrade;
    public BaseUnit UnitToUpgradeTo => Upgrade.UpgradedUnit;
}

/// <summary>
/// Unit Actions - generic enum with static, UI-independent properties
/// (icon, sound, key binding and page preference are in the UI layer, see <c>UiUnitActionType</c>).
/// <see cref="UnitActionTypeExtensions.Value"/> is the _default_ label to display, can be overridden in UnitAction instantiation.
/// In that case, it must be the translation template or empty.
/// </summary>
public enum UnitActionType
{
    StopEscortFormation,
    EscortFormation,
    Automate,
    StopAutomation,
    StopMovement,
    Sleep,
    SleepUntilHealed,
    Fortify,
    FortifyUntilHealed,
    Guard,
    Explore,
    StopExploration,
    Upgrade,
    Transform,
    Pillage,
    Paradrop,
    AirSweep,
    SetUp,
    FoundCity,
    Repair,
    CreateImprovement,
    HurryResearch,
    HurryPolicy,
    HurryWonder,
    HurryBuilding,
    ConductTradeMission,
    CreateGreatWork,
    ConcertTour,
    FoundReligion,
    TriggerUnique,
    SpreadReligion,
    RemoveHeresy,
    EnhanceReligion,
    GiftUnit,
    Skip,
    AddInCapital,
}

public static class UnitActionTypeExtensions
{
    /// <summary>The Kotlin <c>UnitActionType.value</c></summary>
    public static string Value(this UnitActionType type) => type switch
    {
        UnitActionType.StopEscortFormation => "Stop Escort formation",
        UnitActionType.EscortFormation => "Escort formation",
        UnitActionType.Automate => "Automate",
        UnitActionType.StopAutomation => "Stop automation",
        UnitActionType.StopMovement => "Stop movement",
        UnitActionType.Sleep => "Sleep",
        UnitActionType.SleepUntilHealed => "Sleep until healed",
        UnitActionType.Fortify => "Fortify",
        UnitActionType.FortifyUntilHealed => "Fortify until healed",
        UnitActionType.Guard => "Guard",
        UnitActionType.Explore => "Explore",
        UnitActionType.StopExploration => "Stop exploration",
        UnitActionType.Upgrade => "Upgrade to [unitType] ([goldCost] gold)",
        UnitActionType.Transform => "Transform",
        UnitActionType.Pillage => "Pillage",
        UnitActionType.Paradrop => "Paradrop",
        UnitActionType.AirSweep => "Air Sweep",
        UnitActionType.SetUp => "Set up",
        UnitActionType.FoundCity => "Found city",
        UnitActionType.Repair => Constants.Repair,
        UnitActionType.CreateImprovement => "Create",
        UnitActionType.HurryResearch => $"{{Hurry Research}} ({Fonts.Death})",
        UnitActionType.HurryPolicy => $"{{Hurry Policy}} ({Fonts.Death})",
        UnitActionType.HurryWonder => $"{{Hurry Wonder}} ({Fonts.Death})",
        UnitActionType.HurryBuilding => $"{{Hurry Construction}} ({Fonts.Death})",
        UnitActionType.ConductTradeMission => $"{{Conduct Trade Mission}} ({Fonts.Death})",
        UnitActionType.CreateGreatWork => $"{{Create Great Work}} ({Fonts.Death})",
        UnitActionType.ConcertTour => $"{{Concert Tour}} ({Fonts.Death})",
        UnitActionType.FoundReligion => "Found a Religion",
        UnitActionType.TriggerUnique => "Trigger unique",
        UnitActionType.SpreadReligion => "Spread [religionName]",
        UnitActionType.RemoveHeresy => "Remove Heresy",
        UnitActionType.EnhanceReligion => "Enhance a Religion",
        UnitActionType.GiftUnit => "Gift unit",
        UnitActionType.Skip => "Skip turn",
        UnitActionType.AddInCapital => "Add in capital",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
