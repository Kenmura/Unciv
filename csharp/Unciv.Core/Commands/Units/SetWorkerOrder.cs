using Unciv.Logic;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;
using UnitAutomationClass = Unciv.Logic.Automation.Unit.UnitAutomation;

namespace Unciv.Commands.Units;

public enum WorkerOrderKind
{
    /// <summary>Start working on <c>Improvement</c> here (and queue <c>SecondImprovement</c> after it)</summary>
    Build,
    /// <summary>The picker's "Cancel improvement order"</summary>
    Cancel,
    /// <summary>The overlay button on a tile: automate a road connection to <c>To</c></summary>
    ConnectRoad,
}

/// <summary>
/// Orders of builder units (T189). Build and Cancel are <c>ImprovementPickerScreen.accept</c>: the improvement must have no
/// building problems on the tile and the unit must be able to build it; an improvement already in progress is not restarted, and the
/// unit is woken up (<c>tryResetAction</c>). ConnectRoad is <c>ConnectRoadOverlayButtonData.connectRoadToTargetTile</c>, offered
/// when the unit has a BuildImprovements unique and the civ knows some road.
/// </summary>
[CommandType("set-worker-order")]
public sealed class SetWorkerOrder : GameCommand
{
    public int UnitId { get; init; }
    public WorkerOrderKind Order { get; init; }
    public string Improvement { get; init; } = "";
    public string? SecondImprovement { get; init; }
    public HexCoord To { get; init; }

    protected override CommandValidation ValidateCommand(CommandContext context)
    {
        if (!context.RequireActive().TryGet(out var actor, out var failure)) return failure;
        if (!context.OwnUnit(actor, UnitId).TryGet(out var unit, out failure)) return failure;
        var tile = unit.GetTile();
        switch (Order)
        {
            case WorkerOrderKind.Build:
                if (tile.IsMarkedForCreatesOneImprovement())
                    return CommandValidation.Fail(CommandErrors.NotAllowed, "This tile is reserved for a one-time improvement");
                var problem = CheckBuildable(unit, Improvement);
                if (!problem.IsValid) return problem;
                return SecondImprovement == null ? CommandValidation.Ok : CheckBuildable(unit, SecondImprovement);
            case WorkerOrderKind.Cancel:
                return tile.IsMarkedForCreatesOneImprovement()
                    ? CommandValidation.Fail(CommandErrors.NotAllowed, "This tile is reserved for a one-time improvement")
                    : CommandValidation.Ok;
            case WorkerOrderKind.ConnectRoad:
                if (!unit.HasUnique(UniqueType.BuildImprovements) || unit.Civ.Tech.GetBestRoadAvailable() == RoadStatus.None)
                    return CommandValidation.Fail(CommandErrors.NotAllowed, "The unit cannot connect roads");
                return context.TileAt(To).TryGet(out _, out failure) ? CommandValidation.Ok : failure;
            default:
                return CommandValidation.Fail(CommandErrors.InvalidTarget, "Unknown worker order");
        }
    }

    private static CommandValidation CheckBuildable(Logic.Map.MapUnits.MapUnit unit, string name)
    {
        if (!unit.Civ.GameInfo.Ruleset.TileImprovements.TryGetValue(name, out var improvement))
            return CommandValidation.Fail(CommandErrors.InvalidTarget, $"There is no improvement \"{name}\"");
        var tile = unit.GetTile();
        if (!unit.CanBuildImprovement(improvement) || !tile.ImprovementFunctions.CanBuildImprovement(improvement, unit.Cache.State))
            return CommandValidation.Fail(CommandErrors.NotAllowed, $"\"{name}\" cannot be built here by this unit");
        return CommandValidation.Ok;
    }

    protected override IReadOnlyList<GameEvent> ApplyCommand(CommandContext context)
    {
        var unit = context.OwnUnit(context.Actor, UnitId).Value.NotNull();
        var tile = unit.GetTile();
        var view = new GameView((GameInfo)unit.Civ.GameInfo, unit.Civ).GetMapUnitView(unit);
        var ruleset = tile.Ruleset;
        switch (Order)
        {
            case WorkerOrderKind.Cancel:
                view.TryStopWorkingOnImprovement(); // no wake-up: the worker can stay as it is
                break;
            case WorkerOrderKind.Build:
                if (Improvement != tile.ImprovementInProgress)
                    view.TryStartWorkingOnImprovement(ruleset.TileImprovements[Improvement],
                        SecondImprovement == null ? null : ruleset.TileImprovements[SecondImprovement]);
                view.TryResetAction();
                break;
            case WorkerOrderKind.ConnectRoad:
                unit.AutomatedRoadConnectionDestination = To;
                unit.AutomatedRoadConnectionPath = null;
                unit.Action = Constants.ConnectRoadAction;
                unit.Automated = true;
                UnitAutomationClass.AutomateUnitMoves(view);
                break;
        }
        return [];
    }
}
