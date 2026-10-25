using System.Text.Json.Serialization;
using Unciv.Logic.Map;

namespace Unciv.Commands.Events;

/// <summary>
/// A game event defined in code: a past-tense fact with a stable wire name and an audience class
/// (<see cref="EventTypeAttribute"/>). Events carry ids, names and positions only (never object references) and enough data to
/// classify who may see them without reading a global current player: <see cref="Participants"/> and <see cref="Positions"/>.
/// Events are produced in call order and never feed back into the game state. An effect can precede the summary that caused it
/// (UnitDestroyed of the loser, then UnitAttacked). A sink exists only while one command applies (<c>GameCommand.Apply</c>), so
/// the commands of one game must be applied one at a time (GameSession does); transient rebuilds (load) run without a sink.
/// Known gaps, tracked in T209: cargo/swap/withdrawal/teleport moves, unit capture, city destruction and trades emit only their tile
/// owner changes; the sink keeps the first events when full.
/// </summary>
public abstract record EngineEvent : GameEvent
{
    [JsonIgnore]
    public sealed override string Type => EventTypeAttribute.NameOf(GetType());

    /// <summary>The civilization ids that always receive this event in full.</summary>
    public virtual IEnumerable<string> Participants() => [];

    /// <summary>The map positions the event happened at, for the visibility filter.</summary>
    public virtual IEnumerable<HexCoord> Positions() => [];
}
