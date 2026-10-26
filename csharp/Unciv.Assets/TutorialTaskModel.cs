using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Units;
using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>
/// C054: which tutorial tasks of the ruleset's Events the player has done, as far as the client can tell. Kotlin completes them from UI code
/// (<c>settings.addCompletedTutorialTask</c>); here the client reports each one once (CompleteTutorialTask) from what it sees in the viewer's
/// <see cref="ClientSnapshot"/>, from the commands it sends and from the screens it opens. Godot-free so it is unit-tested.
/// </summary>
public static class TutorialTaskModel
{
    public const string MoveUnitTask = "Move unit";
    public const string FoundCity = "Found city";
    public const string EnterCityScreen = "Enter city screen";
    public const string PickTechnology = "Pick technology";
    public const string PickConstruction = "Pick construction";
    public const string PassATurn = "Pass a turn";
    public const string ReassignWorkedTiles = "Reassign worked tiles";
    public const string OpenOptions = "Open the options table";

    /// <summary>Tasks the snapshot of the viewer already proves: a city is owned, a technology is being researched, the turn moved on.</summary>
    public static IReadOnlyList<string> FromSnapshot(ClientSnapshot snapshot)
    {
        var done = new List<string>();
        if (snapshot.Cities.Any(it => it.Owner == snapshot.You)) done.Add(FoundCity);
        if (snapshot.Civs.FirstOrDefault(it => it.CivId == snapshot.You)?.Own?.CurrentTech != null) done.Add(PickTechnology);
        if (snapshot.Turn > 1) done.Add(PassATurn);
        return done;
    }

    /// <summary>The task a command the player just sent completes, or null.</summary>
    public static string? FromCommand(GameCommand command) => command switch
    {
        MoveUnit => MoveUnitTask,
        QueueConstruction or PurchaseConstruction => PickConstruction,
        WorkTile or StopWorkingTile or ReassignPopulation or LockTile => ReassignWorkedTiles,
        _ => null,
    };
}
