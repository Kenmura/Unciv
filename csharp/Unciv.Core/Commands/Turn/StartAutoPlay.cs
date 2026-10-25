using Unciv.Logic;

namespace Unciv.Commands.Turn;

/// <summary>
/// Let the AI play the actor's civ for <see cref="Turns"/> turns (or until the end of the game): Kotlin <c>AutoPlay.startMultiturnAutoPlay</c> with the
/// <c>GameSettingsAutoPlay</c> values (autoPlayMaxTurns, autoPlayUntilEnd, fullAutoPlayAI) carried by the command so the effect replays. Client state that
/// changes game outcomes, hence a command. The turns themselves are played by <see cref="AutoPlayTurn"/>.
/// Refused in simultaneous games unless the host enabled it (<see cref="TurnState.AutoPlayAllowed"/>); Kotlin's menu hides it in online multiplayer.
/// </summary>
[CommandType("start-auto-play")]
public sealed class StartAutoPlay : GameCommand
{
    public int Turns { get; init; } = 10;
    public bool FullAutoPlayAI { get; init; } = true;
    public bool UntilEnd { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out var actor, out var failure)) return failure;
        if (Turns < 1) return CommandValidation.Fail(CommandErrors.InvalidTarget, "Turns must be at least 1");
        if (actor.IsDefeated()) return CommandValidation.Fail(CommandErrors.NotAllowed, $"{actor.CivId} has been defeated");
        if (context.Game.TurnState is { Mode: TurnMode.Simultaneous, AutoPlayAllowed: false })
            return CommandValidation.Fail(CommandErrors.NotAllowed, "The host has not enabled auto-play in this simultaneous game");
        if (context.Game.ClientState.AutoPlay is AutoPlayState { } current && current.IsAutoPlaying())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Auto-play is already running");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var state = context.Game.ClientState.AutoPlay as AutoPlayState ?? new AutoPlayState();
        state.StartMultiturnAutoPlay(context.ActorId, Turns, FullAutoPlayAI, UntilEnd);
        context.Game.ClientState.AutoPlay = state;
        return [];
    }
}
