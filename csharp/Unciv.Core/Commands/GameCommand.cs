using System.Text.Json.Serialization;
using Unciv.Logic;

namespace Unciv.Commands;

/// <summary>
/// A player intent against a real <see cref="GameInfo"/>. Subclasses are plain JSON records of ids and ruleset names
/// (never object references or values the authority can compute), carry <see cref="CommandTypeAttribute"/>, and are
/// found by <see cref="GameCommandRegistry"/>.
///
/// <see cref="ValidateCommand"/> must be pure: the full state hash is identical before and after (engine predicates that
/// refresh lazy caches are checked by tests). It uses the predicate the Kotlin UI used to offer the action, not the View
/// <c>Try*</c> methods. <see cref="ApplyCommand"/> runs only after validation passed and must not fail.
/// The actor is never part of the payload; it arrives with the authenticated session.
/// </summary>
public abstract class GameCommand : ICommand<GameInfo>
{
    [JsonIgnore]
    public string Type => CommandTypeAttribute.NameOf(GetType());

    public CommandValidation Validate(GameInfo state, string actorId) => ValidateCommand(new CommandContext(state, actorId));

    /// <summary>
    /// Runs the command with a fresh <see cref="GameInfoEventSink"/> installed. The result is the events the engine reported, in call
    /// order, followed by those the command returns itself.
    /// </summary>
    public IReadOnlyList<GameEvent> Apply(GameInfo state, string actorId)
    {
        var previous = state.EventSink;
        var sink = new GameInfoEventSink();
        state.EventSink = sink;
        try
        {
            var returned = ApplyCommand(new CommandContext(state, actorId));
            var events = sink.Drain();
            return returned.Count == 0 ? events : [.. events, .. returned];
        }
        finally
        {
            state.EventSink = previous;
        }
    }

    protected abstract CommandValidation ValidateCommand(CommandContext context);

    protected abstract IReadOnlyList<GameEvent> ApplyCommand(CommandContext context);
}
