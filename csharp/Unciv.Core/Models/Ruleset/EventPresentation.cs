using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset;

// No Kotlin file: engine-side replacement for the UI half of an Event popup
// (core/src/com/unciv/ui/screens/worldscreen/AlertPopup.kt addEvent + RenderEvent.kt), as plain data a client or AI can read.

/// <summary>One selectable choice of a pending <see cref="EventPresentation"/>. <see cref="Index"/> addresses it in <see cref="EventPresentation.Choose"/>.</summary>
public sealed record EventChoicePresentation(int Index, string Name, string Text, string KeyShortcut);

/// <summary>
/// A pending Event (an <see cref="AlertType.Event"/> <see cref="PopupAlert"/>) resolved to its texts and the currently matching choices.
/// Without UI types: a client renders it, an AI or a command picks a choice by index.
/// </summary>
public sealed class EventPresentation
{
    public PopupAlert Alert { get; }
    public Event Event { get; }
    public MapUnit? Unit { get; }
    public string Text => Event.Text;
    public IReadOnlyList<EventChoicePresentation> Choices { get; }
    private readonly List<EventChoice> choices;

    private EventPresentation(PopupAlert alert, Event eventObj, MapUnit? unit, List<EventChoice> choices)
    {
        Alert = alert;
        Event = eventObj;
        Unit = unit;
        this.choices = choices;
        Choices = choices.Select((it, i) => new EventChoicePresentation(i, it.Name, it.Text, it.KeyShortcut)).ToList();
    }

    /// <summary>
    /// AlertPopup.addEvent + RenderEvent: the alert value is "eventName" + (StringSplitCharacter + "unitId=1234")*.
    /// Returns null when the alert is not an Event alert, the event is unknown, or no choice matches any more (RenderEvent.isValid false).
    /// </summary>
    public static EventPresentation? Resolve(Civilization civ, PopupAlert alert)
    {
        if (alert.Type != AlertType.Event) return null;
        var splitString = alert.Value.Split(Constants.StringSplitCharacter);
        var eventName = splitString[0];
        MapUnit? unit = null;
        for (int i = 1; i < splitString.Length; i++)
        {
            if (!splitString[i].StartsWith("unitId=", StringComparison.Ordinal)) continue;
            unit = civ.Units.GetUnitById(int.Parse(splitString[i]["unitId=".Length..], System.Globalization.CultureInfo.InvariantCulture));
        }
        var eventObj = civ.GameInfo.Ruleset.Events.GetValueOrDefault(eventName);
        if (eventObj == null) return null;
        var matching = eventObj.GetMatchingChoices(new GameContext(civ, unit: unit));
        if (matching == null) return null;
        return new EventPresentation(alert, eventObj, unit, matching.ToList());
    }

    /// <summary>All pending Event alerts of <paramref name="civ"/> that still resolve, in queue order.</summary>
    public static List<EventPresentation> GetPending(Civilization civ)
        => civ.PopupAlerts.Select(it => Resolve(civ, it)).OfType<EventPresentation>().ToList();

    /// <summary>
    /// Picks a choice: removes the alert (AlertPopup.close) then triggers the choice (RenderEvent.addChoice).
    /// Returns false (and leaves the alert queued) when <paramref name="index"/> is out of range.
    /// </summary>
    public bool Choose(Civilization civ, int index)
    {
        if (index < 0 || index >= choices.Count) return false;
        civ.PopupAlerts.Remove(Alert);
        choices[index].TriggerChoice(civ, Unit);
        return true;
    }
}
