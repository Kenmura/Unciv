using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Logic;

namespace Unciv.Net.Visibility;

/// <summary>
/// C037: what one civ may see of the raw events a command produced, through the same <see cref="EventFilter"/> and <see cref="GameViewVisibility"/> the
/// server uses per recipient (<see cref="ViewDeltaBuilder.BuildEvents"/>). A single-player host runs this between the engine and its client so the
/// client stream never carries another civ's fogged moves or private events. Capture <see cref="Mark"/> before a command, call <see cref="Filter"/> after it.
/// </summary>
public sealed class ViewerEventFilter(GameInfo game, string viewerCivId)
{
    /// <summary>The viewer's state before a command, to diff reveals and stat changes against.</summary>
    public ViewDeltaBuilder.ViewMark Mark() => ViewDeltaBuilder.Capture(game, game.GetCivilization(viewerCivId));

    /// <summary>The viewer's share of <paramref name="events"/> (reveals first, redacted engine events, then stat changes). Events that are not <see cref="EngineEvent"/>s are not classifiable and never pass.</summary>
    public IReadOnlyList<GameEvent> Filter(ViewDeltaBuilder.ViewMark before, IEnumerable<GameEvent> events) =>
        ViewDeltaBuilder.BuildEvents(game, game.GetCivilization(viewerCivId), before, events.OfType<EngineEvent>());
}
