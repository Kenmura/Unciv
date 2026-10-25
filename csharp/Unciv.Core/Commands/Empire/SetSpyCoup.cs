namespace Unciv.Commands.Empire;

/// <summary>
/// Stage a coup with a spy set up in a city-state, or call it off (back to counter-intelligence). Kotlin: <c>EspionageScreen.CoupButton</c>,
/// shown when <c>spy.canDoCoup()</c>; clicking while the action is not Coup does <c>setAction(Coup, 1)</c>, clicking while it is
/// does <c>setAction(CounterIntelligence, 10)</c>. The coup itself resolves in <c>Spy.endTurn</c>.
/// </summary>
[CommandType("set-spy-coup")]
public sealed class SetSpyCoup : GameCommand
{
    /// <summary>The spy's name, unique within the civ.</summary>
    public string Spy { get; init; } = "";

    /// <summary>True to stage the coup, false to call it off.</summary>
    public bool Stage { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.Game.IsEspionageEnabled())
            return CommandValidation.Fail(CommandErrors.NotAllowed, "Espionage is disabled");
        var spy = actor.EspionageManager.SpyList.FirstOrDefault(it => it.Name == Spy);
        if (spy == null)
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no spy \"{Spy}\"");
        if (!spy.CanDoCoup())
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"{Spy} cannot stage a coup");
        bool staged = spy.Action == Models.SpyAction.Coup;
        if (staged == Stage)
            return CommandValidation.Fail(CommandErrors.NotAllowed, Stage ? $"{Spy} is already staging a coup" : $"{Spy} is not staging a coup");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var spy = context.Actor.EspionageManager.SpyList.First(it => it.Name == Spy);
        if (Stage) spy.SetAction(Models.SpyAction.Coup, 1);
        else spy.SetAction(Models.SpyAction.CounterIntelligence, 10);
        return [];
    }
}
