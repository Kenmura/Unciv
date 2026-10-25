using System.Diagnostics.CodeAnalysis;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Commands.Turn;

/// <summary>
/// An alert as the client saw it: its position in <c>PopupAlerts</c> plus its type and value, so that a reference that went stale (another alert took the
/// position) is rejected instead of dismissing the wrong one.
/// </summary>
internal static class AlertCommandChecks
{
    public static bool TryFind(CivilizationObject civ, int index, AlertType type, string value,
        [NotNullWhen(true)] out PopupAlert? alert, out CommandValidation failure)
    {
        alert = null;
        if (index < 0 || index >= civ.PopupAlerts.Count || civ.PopupAlerts[index].Type != type || civ.PopupAlerts[index].Value != value)
        {
            failure = CommandValidation.Fail(CommandErrors.StaleReference, $"There is no pending {type} alert at position {index}");
            return false;
        }
        alert = civ.PopupAlerts[index];
        failure = CommandValidation.Ok;
        return true;
    }

    /// <summary>
    /// Kotlin <c>AlertPopup</c> does not open some alerts any more (<c>shouldOpen = false</c>: the alert is only dropped), for instance when the civ that
    /// sent it is defeated or the event no longer has a choice. Such an alert needs no answer, only an acknowledgement.
    /// </summary>
    public static bool IsUnshowable(CivilizationObject civ, PopupAlert alert)
    {
        switch (alert.Type)
        {
            case AlertType.Event:
                return EventPresentation.Resolve(civ, alert) == null;
            case AlertType.RecapturedCivilian:
                var unit = civ.GameInfo.TileMap[HexCoordOf(alert.Value)].CivilianUnit;
                return unit?.OriginalOwningCiv == null || unit.OriginalOwningCiv.IsDefeated();
            case AlertType.BulliedProtectedMinor or AlertType.AttackedProtectedMinor or AlertType.AttackedAllyMinor:
                var bully = civ.GameInfo.GetCivilizationOrNull(alert.Value.Split('@')[0]);
                return bully == null || bully.IsDefeated();
            default:
                return false;
        }
    }

    public static Unciv.Logic.Map.HexCoord HexCoordOf(string value) => Unciv.Logic.Map.HexCoord.FromString(value);
}
