using System.Diagnostics.CodeAnalysis;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Diplomacy;

/// <summary>Which diplomacy table of the screen a command belongs to (<c>MajorCivDiplomacyTable</c> or <c>CityStateDiplomacyTable</c>).</summary>
internal enum CivKind
{
    Any,
    Major,
    CityState,
}

/// <summary>
/// The checks every diplomacy command shares. Kotlin: <c>DiplomacyScreen</c> lists <c>getKnownCivsSorted</c> (a known, living civ other than
/// the viewer; barbarians and spectators are not listed) and disables every button when <c>isNotPlayersTurn()</c>, answered here by
/// <see cref="CommandContext.RequireActive"/>.
/// </summary>
internal static class DiplomacyCommandChecks
{
    /// <summary>The acting player and the civ the command is about, which must be on the screen's list and of the requested <paramref name="kind"/>.</summary>
    public static bool TryOtherCiv(this CommandContext context, string civId, CivKind kind,
        [NotNullWhen(true)] out CivilizationObject? actor, [NotNullWhen(true)] out CivilizationObject? other, out CommandValidation failure)
    {
        other = null;
        if (!context.RequireActive().TryGet(out actor, out failure)) return false;
        if (!context.Civ(civId).TryGet(out other, out failure)) return false;
        if (!actor.DiplomacyFunctions.GetKnownCivsSorted().Contains(other))
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"{civId} is not a civilization you can talk to");
            return false;
        }
        if (kind == CivKind.Major && !other.IsMajorCiv())
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"{civId} is not a major civilization");
            return false;
        }
        if (kind == CivKind.CityState && !other.IsCityState)
        {
            failure = CommandValidation.Fail(CommandErrors.InvalidTarget, $"{civId} is not a city-state");
            return false;
        }
        failure = CommandValidation.Ok;
        return true;
    }

    /// <summary>The real <see cref="CityStateFunctions"/> of a city-state (the precedent of the automation classes: the interface is only a test seam).</summary>
    public static CityStateFunctions CityState(CivilizationObject cityState) => (CityStateFunctions)cityState.CityStateFunctions;

    /// <summary>The civ of an already validated command.</summary>
    public static CivilizationObject ValidatedOther(this CommandContext context, string civId) => context.Civ(civId).Value.NotNull();

    /// <summary>Kotlin <c>diplomaticRelationshipsCanChange</c>: war and peace buttons are hidden when the mod forbids changes.</summary>
    public static bool RelationshipsCanChange(this CommandContext context) =>
        !((IHasUniques)context.Game.Ruleset.ModOptions).HasUnique(UniqueType.DiplomaticRelationshipsCannotChange);

    public static CommandValidation RelationshipsFixed() =>
        CommandValidation.Fail(CommandErrors.NotAllowed, "This ruleset does not allow diplomatic relationships to change");

    /// <summary>Whether <paramref name="civ"/> has a pending popup alert of <paramref name="type"/> naming <paramref name="value"/>.</summary>
    public static PopupAlert? FindAlert(CivilizationObject civ, AlertType type, string value) =>
        civ.PopupAlerts.FirstOrDefault(it => it.Type == type && it.Value == value);

    /// <summary>The Kotlin <c>AlertPopup</c> removes a handled alert from the viewer's list when it closes.</summary>
    public static void RemoveAlert(CivilizationObject civ, PopupAlert alert) => civ.PopupAlerts.Remove(alert);

    public static CommandValidation NoSuchAlert(string what) => CommandValidation.Fail(CommandErrors.StaleReference, $"There is no pending {what}");
}
