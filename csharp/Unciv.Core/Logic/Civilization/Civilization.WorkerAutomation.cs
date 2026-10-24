namespace Unciv.Logic.Civilization;

// Kotlin Civilization.getWorkerAutomation / workerAutomationCache (T092), in its own file to keep Civilization.cs within its size baseline.
public partial class Civilization
{
    private Automation.Unit.WorkerAutomation? workerAutomationCache;

    /// <summary>
    /// Returns an instance of WorkerAutomation valid for the duration of the current turn.
    /// This instance carries cached data common for all Workers of this civ.
    /// </summary>
    public Automation.Unit.WorkerAutomation GetWorkerAutomation()
    {
        var currentTurn = GameInfo.Turns;
        if (workerAutomationCache == null || workerAutomationCache.CachedForTurn != currentTurn)
            workerAutomationCache = new Automation.Unit.WorkerAutomation(this, currentTurn);
        return workerAutomationCache;
    }
}
