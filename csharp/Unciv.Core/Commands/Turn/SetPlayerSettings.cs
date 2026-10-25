namespace Unciv.Commands.Turn;

/// <summary>
/// Change the actor's state-affecting settings (T183, design section 3.9). Only the members that are set change; the rest keep their value.
/// Not tied to the turn: a setting is the player's own and applies whenever that civ acts.
/// </summary>
[CommandType("set-player-settings")]
public sealed class SetPlayerSettings : GameCommand
{
    public bool? AutoAssignCityProduction { get; init; }
    public bool? AutoAssignSpecialistsInNewCities { get; init; }
    public bool? AutomatedUnitsMoveOnTurnStart { get; init; }
    public bool? CitiesAutoBombardAtEndOfTurn { get; init; }
    public int? NotificationsLogMaxTurns { get; init; }
    public bool? AutomatedUnitsChoosePromotions { get; init; }
    public bool? AutoBuildingRoads { get; init; }
    public bool? AutomatedWorkersReplaceImprovements { get; init; }
    public bool? StopAutomatedWorkersRemoveVegetation { get; init; }
    public bool? HumanConquestPrompt { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequirePlayer().TryGet(out _, out var failure)) return failure;
        return NotificationsLogMaxTurns is < 0
            ? CommandValidation.Fail(CommandErrors.InvalidTarget, "NotificationsLogMaxTurns cannot be negative")
            : CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var current = context.Game.GetPlayerSettings(context.ActorId);
        context.Game.SetPlayerSettings(context.ActorId, current with
        {
            AutoAssignCityProduction = AutoAssignCityProduction ?? current.AutoAssignCityProduction,
            AutoAssignSpecialistsInNewCities = AutoAssignSpecialistsInNewCities ?? current.AutoAssignSpecialistsInNewCities,
            AutomatedUnitsMoveOnTurnStart = AutomatedUnitsMoveOnTurnStart ?? current.AutomatedUnitsMoveOnTurnStart,
            CitiesAutoBombardAtEndOfTurn = CitiesAutoBombardAtEndOfTurn ?? current.CitiesAutoBombardAtEndOfTurn,
            NotificationsLogMaxTurns = NotificationsLogMaxTurns ?? current.NotificationsLogMaxTurns,
            AutomatedUnitsChoosePromotions = AutomatedUnitsChoosePromotions ?? current.AutomatedUnitsChoosePromotions,
            AutoBuildingRoads = AutoBuildingRoads ?? current.AutoBuildingRoads,
            AutomatedWorkersReplaceImprovements = AutomatedWorkersReplaceImprovements ?? current.AutomatedWorkersReplaceImprovements,
            StopAutomatedWorkersRemoveVegetation = StopAutomatedWorkersRemoveVegetation ?? current.StopAutomatedWorkersRemoveVegetation,
            HumanConquestPrompt = HumanConquestPrompt ?? current.HumanConquestPrompt,
        });
        return [];
    }
}
