using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;

namespace Unciv.Queries.Tutorial;

// C054: the engine side of Kotlin TutorialTaskTable.getCurrentTask, shared by the query and the two commands so they agree on what "the current task" is.
// The completed set is GameClientState.TutorialTasksCompleted(viewer) (per civ since C060; names only, no private game information); whether tutorials are on is the viewer's own
// client setting, which the client states in its request (the engine has no per-viewer settings).
internal static class TutorialTasks
{
    internal const string TradeRouteTask = "Create a trade route";

    private sealed class ViewerSettings(bool showTutorials, ISet<string> completed) : IConditionalsSettings
    {
        public bool ShowTutorials => showTutorials;
        public bool IsTutorialTaskCompleted(string task) => completed.Contains(task);
    }

    /// <summary>The current task and the choices that match now (RenderEvent.isValid: an event with no matching choice is not a task).</summary>
    public sealed record Task(Event Event, IReadOnlyList<EventChoice> Choices);

    /// <summary>
    /// The first ruleset Event (in ruleset order) with Floating presentation that is available to <paramref name="viewer"/>, with its matching choices, or null
    /// (tutorials off, defeated, none, or no choice matches). Choice availability is evaluated under the same viewer settings as the event's.
    /// </summary>
    public static Task? Current(Civilization viewer, bool showTutorials)
    {
        if (!showTutorials || viewer.IsDefeated()) return null;
        var game = viewer.GameInfo;
        // Deviation: Kotlin writes "Create a trade route" into the settings here; a query may not mutate, so the task counts as completed while the
        // connection exists (Kotlin also counts the capital, which is "connected" to itself, so its task would complete at once. The task would show again if the connection were lost before the player ever saw it, which Kotlin cannot do).
        var completed = new HashSet<string>(game.ClientState.TutorialTasksCompleted(viewer.CivId));
        if (viewer.Cache.CitiesConnectedToCapitalToMediums.Keys.Any(it => it.Civ == viewer && !it.IsCapital())) completed.Add(TradeRouteTask);
        using var scope = Conditionals.UseSettings(new ViewerSettings(showTutorials, completed));
        var context = new GameContext(viewer);
        var task = game.Ruleset.Events.Values.FirstOrDefault(it => it.Presentation == Event.PresentationType.Floating && ((IHasUniques)it).IsAvailable(context));
        var choices = task?.GetMatchingChoices(context);
        return task == null || choices == null ? null : new Task(task, choices.ToList());
    }

    /// <summary>
    /// C060: the choice button <paramref name="index"/> (an index into <see cref="Event.Choices"/>) of the floating Event <paramref name="eventName"/>, or null when
    /// there is no such floating Event or the choice is not offered. Unlike <see cref="Current"/> this reads no client state (the completed set is not hashed or
    /// saved, so a peer replaying a log tail or a loaded save has none): choices are judged under <see cref="NeutralSettings"/>.
    /// </summary>
    public static EventChoice? ChoiceFor(Civilization actor, string eventName, int index)
    {
        var ev = actor.GameInfo.Ruleset.Events.GetValueOrDefault(eventName);
        if (ev == null || ev.Presentation != Event.PresentationType.Floating || index < 0 || index >= ev.Choices.Count) return null;
        using var scope = Conditionals.UseSettings(NeutralSettings);
        var context = new GameContext(actor);
        var matching = ev.Choices.Where(it => ((IHasUniques)it).IsAvailable(context)).ToList();
        if (matching.Count != 0 && ((IHasUniques)ev).HasUnique(UniqueType.OnlyFirstAvailableChoiceIsChosen, context)) matching = [matching[0]];
        return matching.Contains(ev.Choices[index]) ? ev.Choices[index] : null;
    }

    /// <summary>The settings under which a command is validated and applied: tutorials on, nothing completed. Constant, so every peer decides alike.</summary>
    public static IConditionalsSettings NeutralSettings { get; } = new ViewerSettings(true, new HashSet<string>());

    /// <summary>True when some Event of the ruleset asks "if tutorial [task] is completed" or the task is one the engine itself completes: only these names may be stored.</summary>
    public static bool IsKnownTask(Ruleset ruleset, string task)
    {
        if (task == TradeRouteTask || task == "Conquer a city" || task == "Construct an improvement") return true;
        return ruleset.Events.Values.Any(ev => Mentions(ev, task) || ev.Choices.Any(c => Mentions(c, task)));
    }

    private static bool Mentions(IHasUniques source, string task) =>
        source.UniqueObjects.Any(u => u.Modifiers.Any(m => m.Type == UniqueType.ConditionalTutorialCompleted && m.Params[0] == task)
                                       || (u.Type == UniqueType.MarkTutorialComplete && u.Params[0] == task));
}
