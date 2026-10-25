namespace Unciv.Logic.Civilization;

/// <summary>T186: engine-side additions to <see cref="Civilization"/> that are not in the Kotlin class.</summary>
public static class CivilizationEventExtensions
{
    /// <summary>The one place popup alerts are added, so the event sink sees them. Kotlin calls <c>popupAlerts.add(alert)</c> directly.</summary>
    public static void AddPopupAlert(this Civilization civ, PopupAlert alert)
    {
        civ.PopupAlerts.Add(alert);
        GameEventHooks.AlertAdded(civ, alert);
    }
}
