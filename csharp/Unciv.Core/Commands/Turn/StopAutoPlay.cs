using Unciv.Logic;

namespace Unciv.Commands.Turn;

/// <summary>Stop the actor's auto-play: Kotlin <c>AutoPlay.stopAutoPlay</c> (the auto-play button toggles it off).</summary>
[CommandType("stop-auto-play")]
public sealed class StopAutoPlay : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out _, out var failure)) return failure;
        return context.Game.ClientState.AutoPlay is AutoPlayState state && state.IsAutoPlayingFor(context.ActorId)
            ? CommandValidation.Ok
            : CommandValidation.Fail(CommandErrors.NotAllowed, "Auto-play is not running");
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.ClientState.AutoPlay?.StopAutoPlay();
        return [];
    }
}
