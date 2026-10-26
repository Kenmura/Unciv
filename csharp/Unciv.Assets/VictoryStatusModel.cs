using System.Globalization;
using Unciv.Queries.Victory;

namespace Unciv.Assets;

/// <summary>One milestone of the viewer's own path (<see cref="Status"/> is the engine's Completed, Partially or Incomplete) with its numbers, e.g. "2/5", or empty.</summary>
public sealed record VictoryMilestoneLine(string Text, string Status, string Progress);

/// <summary>One rival (or the viewer) on a victory: how many milestones are done and the next one.</summary>
public sealed record VictoryCivLine(string Civ, bool IsViewer, bool IsDefeated, string Text);

public sealed record VictorySection(string Name, string Header, IReadOnlyList<VictoryMilestoneLine> Own, IReadOnlyList<VictoryCivLine> Civs);

/// <summary>
/// C044: the menu's "Victory status" page as plain data, Godot-free so it is unit-tested. The engine's <see cref="VictoryProgressResult"/> already holds every
/// number and the order (worst civ first); this only words the lines. An unmet civ is "???" as on the game-over screen.
/// </summary>
public static class VictoryStatusModel
{
    public const string Title = "Victory status";

    public const string NoVictories = "No victory conditions are enabled in this game.";

    public static IReadOnlyList<VictorySection> Build(VictoryProgressResult? result)
    {
        if (result == null) return [];
        return result.Victories.Select(victory => new VictorySection(
            victory.Name,
            victory.Header,
            victory.Own.Select(it => new VictoryMilestoneLine(it.Text, it.Status, ProgressText(it))).ToList(),
            victory.Civs.Select(it => new VictoryCivLine(it.Civ.CivName ?? GameOverModel.UnknownCiv, it.Civ.IsViewer, it.Civ.IsDefeated, CivText(it, victory.TotalMilestones))).ToList())).ToList();
    }

    private static string ProgressText(MilestoneEntry entry)
    {
        if (entry.Done is not { } done) return "";
        string total = entry.Total is { } t ? t.ToString(CultureInfo.InvariantCulture) : "?";
        return $"{done.ToString(CultureInfo.InvariantCulture)}/{total}";
    }

    private static string CivText(VictoryCivEntry entry, int totalMilestones)
    {
        string name = entry.Civ.CivName ?? GameOverModel.UnknownCiv;
        string count = $"{entry.MilestonesCompleted.ToString(CultureInfo.InvariantCulture)}/{totalMilestones.ToString(CultureInfo.InvariantCulture)}";
        return entry.Next == null ? $"{name}: {count} (all done)" : $"{name}: {count}, next: {entry.Next.Text}";
    }
}
