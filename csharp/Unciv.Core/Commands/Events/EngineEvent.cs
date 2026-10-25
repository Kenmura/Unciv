using System.Text.Json.Serialization;
using Unciv.Logic.Map;

namespace Unciv.Commands.Events;

/// <summary>
/// A game event defined in code: a past-tense fact with a stable wire name and an audience class
/// (<see cref="EventTypeAttribute"/>). Events carry ids, names and positions only (never object references) and enough data to
/// classify who may see them without reading a global current player: <see cref="Participants"/> and <see cref="Positions"/>.
/// Events are produced in call order and never feed back into the game state.
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
