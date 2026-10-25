using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Event.kt
// Not ported yet: the keyShortcut parsing (UI).
public class Event : RulesetObject
{
    /// <summary>
    /// Controls how an Event is shown to the player when triggered (see <c>UniqueTriggerActivation.triggerUnique</c> for <see cref="UniqueType.TriggerEvent"/>).
    /// Note: AI civs always behave as <see cref="None"/> (weighted random) for <see cref="Alert"/>/<see cref="Floating"/> presentations, since they can't see a popup.
    /// See also <see cref="UniqueType.OnlyFirstAvailableChoiceIsChosen"/>, which restricts the choice pool to a single entry regardless of presentation.
    /// </summary>
    public enum PresentationType
    {
        /// <summary>No popup. A choice is picked immediately via weighted random and triggered right away.</summary>
        None,
        /// <summary>Queues a blocking modal alert the player must resolve before continuing.</summary>
        Alert,
        /// <summary>Kotlin throws NotImplementedError for this; the engine queues it as an Event alert like <see cref="Alert"/> (T171), the client decides how to show it.</summary>
        Floating,
    }

    public PresentationType Presentation { get; set; } = PresentationType.Alert;
    public string Text { get; set; } = "";

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Event;
    public override string MakeLink() => $"Event/{Name}";

    // todo: add unrepeatable events

    public List<EventChoice> Choices { get; set; } = [];

    /// <returns><c>null</c> when no choice passes the condition tests, so client code can easily bail using <c>??</c>.
    ///          An empty list is possible when the Event definition contains no choices and the event's conditions are fulfilled.</returns>
    public IReadOnlyCollection<EventChoice>? GetMatchingChoices(GameContext gameContext)
    {
        var self = (IHasUniques)this;
        if (!self.IsAvailable(gameContext)) return null;
        if (Choices.Count == 0) return [];
        var matchingChoices = Choices.Where(it => ((IHasUniques)it).IsAvailable(gameContext)).ToList();
        if (matchingChoices.Count == 0) return null;
        if (self.HasUnique(UniqueType.OnlyFirstAvailableChoiceIsChosen, gameContext))
            return [matchingChoices[0]];
        return matchingChoices;
    }
}

public class EventChoice : RulesetObject
{
    public string Text { get; set; } = "";
    public override UniqueTarget GetUniqueTarget() => UniqueTarget.EventChoice;
    public override string MakeLink() => "";

    /// <summary>Keyboard support - not user-rebindable, mod control only. Will be parsed by the client.</summary>
    public string KeyShortcut { get; set; } = "";

    public bool TriggerChoice(Civilization civ, MapUnit? unit = null)
    {
        var success = false;
        var gameContext = new GameContext(civ, unit: unit);
        foreach (var unique in UniqueObjects)
        {
            if (!unique.IsTriggerable || !unique.ConditionalsApply(gameContext)) continue;
            int repeats = unique.GetUniqueMultiplier(gameContext);
            for (int i = 0; i < repeats; i++)
                if (UniqueTriggerActivation.TriggerUnique(unique, civ, unit: unit)) success = true;
        }
        return success;
    }
}
