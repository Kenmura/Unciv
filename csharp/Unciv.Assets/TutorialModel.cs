using Unciv.GodotClient.Settings;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>One page of a tutorial popup: <see cref="Text"/> is a step of the ruleset's Tutorials.json entry (a translation key; the client translates it).</summary>
public sealed record TutorialPage(TutorialTrigger Trigger, string Title, string Text, int Step, int StepCount)
{
    public bool IsLast => Step == StepCount - 1;
}

/// <summary>One row of the tutorial list: <see cref="Seen"/> comes from the client settings, <see cref="Text"/> is every step joined by a blank line.</summary>
public sealed record TutorialListEntry(string Name, string Text, bool Seen);

/// <summary>What <see cref="TutorialQueue.Enqueue"/> did with a trigger.</summary>
public enum TutorialEnqueue
{
    /// <summary>Tutorials are off, the tutorial was seen before, or it is already waiting.</summary>
    Ignored,

    /// <summary>It will be shown.</summary>
    Queued,

    /// <summary>The ruleset has no steps for it (a mod removed it, or it is Civilopedia-only): the caller marks it seen so it is not asked again, as Kotlin does.</summary>
    NothingToShow,
}

/// <summary>
/// C026: tutorial text, trigger detection and the popup queue as plain data, Godot-free so it is unit-tested. Kotlin keeps these in <c>TutorialController</c> and
/// <c>WorldScreen.displayTutorialsOnUpdate</c>; the triggers here are evaluated on the viewer's <see cref="ClientSnapshot"/> only (what the player can know), never on
/// the full game. The "seen" flags are client settings (<see cref="ClientSettings.TutorialsSeen"/>, keyed by the <see cref="TutorialTrigger"/> name as in Kotlin), never engine state.
/// Triggers whose condition needs data the snapshot does not carry (conquest flags, happiness sources, detailed resource lists) are left out and listed in the ticket notes.
/// </summary>
public static class TutorialModel
{
    /// <summary>The key under which the tutorial is stored in <see cref="ClientSettings.TutorialsSeen"/>.</summary>
    public static string SeenKey(TutorialTrigger trigger) => trigger.ToString();

    /// <summary>The Tutorials.json entry name of a trigger: the value with underscores as blanks and the leading one dropped.</summary>
    public static string NameOf(TutorialTrigger trigger) => trigger.Value().Replace('_', ' ').TrimStart();

    public static bool IsSeen(ClientSettings settings, TutorialTrigger trigger) => settings.TutorialsSeen.Contains(SeenKey(trigger), StringComparer.Ordinal);

    /// <summary>The steps of the trigger's tutorial in the ruleset; empty when it has none.</summary>
    public static IReadOnlyList<string> StepsOf(Ruleset ruleset, TutorialTrigger trigger) =>
        ruleset.Tutorials.TryGetValue(NameOf(trigger), out var tutorial) ? tutorial.Steps ?? [] : [];

    /// <summary>The tutorials for the list in the options: every entry of the ruleset that has text, in the ruleset's order, those with no step shown through their Civilopedia text.</summary>
    public static IReadOnlyList<TutorialListEntry> List(Ruleset ruleset, ClientSettings settings)
    {
        var entries = new List<TutorialListEntry>();
        foreach (var (name, tutorial) in ruleset.Tutorials)
        {
            var lines = tutorial.Steps is { Count: > 0 } steps ? steps : tutorial.CivilopediaText.Select(it => it.Text).Where(it => !string.IsNullOrEmpty(it)).ToList();
            if (lines.Count == 0) continue;
            var trigger = Enum.GetValues<TutorialTrigger>().Cast<TutorialTrigger?>().FirstOrDefault(it => NameOf(it!.Value) == name);
            entries.Add(new TutorialListEntry(name, string.Join("\n\n", lines), trigger != null && IsSeen(settings, trigger.Value)));
        }
        return entries;
    }

    /// <summary>
    /// The triggers whose condition holds for the viewer now, in Kotlin's order. Pure: it does not look at the seen flags (the queue does) and the order is fixed.
    /// <paramref name="ruleset"/> classifies units and resources by name; an unknown name never triggers anything.
    /// </summary>
    public static IReadOnlyList<TutorialTrigger> Detect(ClientSnapshot snapshot, Ruleset ruleset)
    {
        var result = new List<TutorialTrigger> { TutorialTrigger.Introduction };
        var me = snapshot.Civs.FirstOrDefault(it => it.CivId == snapshot.You);
        if (me == null || me.IsDefeated) return [];
        var ownUnits = snapshot.Units.Where(it => it.Own != null && it.Owner == snapshot.You).ToList();
        int ownCities = snapshot.Cities.Count(it => it.Own != null && it.Owner == snapshot.You);

        if (ownUnits.Any(it => it.Own!.Health < 100)) result.Add(TutorialTrigger.InjuredUnits);
        if (ownUnits.Any(it => ruleset.Units.TryGetValue(it.Name, out var unit) && unit.IsCivilian() && !unit.IsGreatPerson
                && ((IHasUniques)unit).HasUnique(UniqueType.BuildImprovements, GameContext.IgnoreConditionalsState)))
            result.Add(TutorialTrigger.Workers);

        if (snapshot.Turn >= 1) result.Add(TutorialTrigger.SlowStart);
        if (ownCities > 2) result.Add(TutorialTrigger.RoadsAndRailroads);
        if (me.Own is { } own)
        {
            if (own.Happiness < 5) result.Add(TutorialTrigger.Happiness);
            if (own.Happiness < 0) result.Add(TutorialTrigger.Unhappiness);
        }
        if (snapshot.Turn >= 50) result.Add(TutorialTrigger.IdleUnits);
        if (snapshot.Turn >= 100) result.Add(TutorialTrigger.ContactMe);

        var barbarians = snapshot.Civs.Where(it => it.IsBarbarian).Select(it => it.CivId).ToHashSet(StringComparer.Ordinal);
        var visible = snapshot.Tiles.Where(it => it.Visible).Select(it => (it.X, it.Y)).ToHashSet();
        if (snapshot.Units.Any(it => barbarians.Contains(it.Owner) && visible.Contains((it.X, it.Y)))) result.Add(TutorialTrigger.BarbarianEncountered);
        if (snapshot.Civs.Any(it => it.CivId != snapshot.You && it.IsMajor && snapshot.Diplomacy.Any(d => d.Status != null && (d.A == it.CivId || d.B == it.CivId))))
            result.Add(TutorialTrigger.OtherCivEncountered);

        var owned = snapshot.Tiles.Where(it => it.Owner == snapshot.You && it.Resource != null)
            .Select(it => ruleset.TileResources.GetValueOrDefault(it.Resource!)?.ResourceType).ToList();
        if (owned.Contains(ResourceType.Luxury)) result.Add(TutorialTrigger.LuxuryResource);
        if (owned.Contains(ResourceType.Strategic)) result.Add(TutorialTrigger.StrategicResource);
        if (snapshot.Tiles.Any(it => it.NaturalWonder != null)) result.Add(TutorialTrigger.NaturalWonders);
        return result;
    }
}

/// <summary>
/// The tutorials waiting to be shown, one popup at a time, a step at a time (Kotlin <c>TutorialController</c>). Each trigger is queued once; the owner marks a tutorial seen
/// when <see cref="Dismiss"/> returns it, so a tutorial is shown once per player and never again.
/// </summary>
public sealed class TutorialQueue(Ruleset ruleset)
{
    private readonly List<TutorialTrigger> pending = [];
    private int step;

    public bool IsShowing => pending.Count > 0;

    public int PendingCount => pending.Count;

    /// <summary>Adds the trigger unless tutorials are off, it was seen, or it already waits.</summary>
    public TutorialEnqueue Enqueue(TutorialTrigger trigger, ClientSettings settings)
    {
        if (!settings.ShowTutorials || TutorialModel.IsSeen(settings, trigger) || pending.Contains(trigger)) return TutorialEnqueue.Ignored;
        if (TutorialModel.StepsOf(ruleset, trigger).Count == 0) return TutorialEnqueue.NothingToShow;
        pending.Add(trigger);
        return TutorialEnqueue.Queued;
    }

    /// <summary>The page to show now, or null when the queue is empty.</summary>
    public TutorialPage? Current
    {
        get
        {
            if (pending.Count == 0) return null;
            var trigger = pending[0];
            var steps = TutorialModel.StepsOf(ruleset, trigger);
            return new TutorialPage(trigger, TutorialModel.NameOf(trigger), steps[step], step, steps.Count);
        }
    }

    /// <summary>Goes to the next step; after the last one the tutorial leaves the queue and is returned (the caller marks it seen).</summary>
    public TutorialTrigger? Dismiss()
    {
        if (Current is not { } page) return null;
        if (!page.IsLast)
        {
            step++;
            return null;
        }
        pending.RemoveAt(0);
        step = 0;
        return page.Trigger;
    }

    /// <summary>Forgets everything waiting (a new game starts).</summary>
    public void Clear()
    {
        pending.Clear();
        step = 0;
    }
}
