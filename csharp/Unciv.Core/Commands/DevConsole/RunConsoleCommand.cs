using Unciv.Commands.Turn;
using Unciv.DevConsole;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Commands.DevConsole;

/// <summary>
/// One dev console line (T229). Kotlin: <c>DevConsolePopup</c> parses the text and mutates <c>GameInfo</c> directly; here the line itself is
/// the logged command, so a replay re-parses and re-runs it deterministically. The selection the Kotlin console read from the world screen
/// (tile, unit, civ) is part of the payload.
///
/// Gate: the console is a cheat. It is refused unless the game was set up with God mode (<c>GameParameters.GodMode</c>), which in a
/// multiplayer game is the host's lobby setting and cannot be changed afterwards by a command. The actor must be a human player.
/// A line that is a query (hint) or a user error is rejected, so it is never logged; the client shows the message from
/// <see cref="ConsoleRunner.Plan"/> instead.
/// </summary>
[CommandType("run-console-command")]
public sealed class RunConsoleCommand : GameCommand
{
    public string Line { get; init; } = "";
    public HexCoord? Tile { get; init; }
    public int? UnitId { get; init; }
    public string? Civ { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out _, out var failure)) return failure;
        if (!context.Game.GameParameters.GodMode)
            return CommandValidation.Fail(CommandErrors.NotAllowed, ConsoleRunner.GodModeRequired);
        if (Line.Length > ConsoleRunner.MaxLineLength)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, ConsoleRunner.LineTooLong);
        var plan = Plan(context);
        return plan.Response.Kind switch
        {
            ConsoleResponseKind.Error => CommandValidation.Fail(CommandErrors.InvalidTarget, plan.Response.Message ?? "Error"),
            ConsoleResponseKind.Hint => CommandValidation.Fail(CommandErrors.NotAllowed, plan.Response.Message ?? "Nothing to do"),
            _ => CommandValidation.Ok,
        };
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        Plan(context).Effect.NotNull()();
        return [];
    }

    private ConsolePlan Plan(CommandContext context) =>
        ConsoleRunner.Plan(context.Game, context.ActorId, Line, new ConsoleSelection(Tile, UnitId, Civ));
}
