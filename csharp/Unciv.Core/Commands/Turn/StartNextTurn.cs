using Unciv.Logic;

namespace Unciv.Commands.Turn;

/// <summary>
/// Simultaneous turns, step 3 of resolving a round (T202): everybody has played, the ranking stats are recorded and <c>Turns</c> advances once
/// (<see cref="GameInfo.StartNextTurn"/>). Valid only when <see cref="GameInfo.NextResolveStep"/> names it (<c>out-of-sequence</c> otherwise).
/// Takes no time: nothing in it reads the clock.
/// </summary>
[CommandType("start-next-turn")]
public sealed class StartNextTurn : GameCommand
{
    protected override CommandValidation ValidateCommand(CommandContext context) => context.RequireStep(new ResolveStep.StartNextTurn());

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        context.Game.StartNextTurn();
        return [];
    }
}
