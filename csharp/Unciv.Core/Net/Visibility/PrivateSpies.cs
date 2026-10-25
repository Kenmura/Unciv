using Unciv.View;

namespace Unciv.Net.Visibility;

// T181 (design/SERVER.md section 3, espionage row): spies are private to the owner. Only the viewer's own spies are ever described, and
// only through SpyView; there is deliberately no path from a foreign civ to a SpyDto (default deny: no ruleset reveals foreign spies in the MVP).
// Steal results already travel as notifications, which are delivered to the owning civ only (EventFilter.ClassifyNotification).

/// <summary>One of the recipient's own spies.</summary>
public sealed record SpyDto
{
    public required string Name { get; init; }
    public required int Rank { get; init; }

    /// <summary>The city the spy is in, or null in the hideout.</summary>
    public string? Location { get; init; }

    public required string Action { get; init; }
}

public static class PrivateSpies
{
    /// <summary>The viewer's own spies, in the order the civilization keeps them.</summary>
    public static List<SpyDto> Build(GameView gameView)
    {
        var result = new List<SpyDto>();
        foreach (var spy in gameView.CivView.GetCiv().EspionageManager.SpyList)
        {
            var view = gameView.GetSpyView(spy);
            if (!view.IsOwnedByViewer()) continue;
            result.Add(new SpyDto
            {
                Name = view.Name,
                Rank = view.GetEffectiveRank(),
                Location = view.GetCityViewOrNull()?.Name,
                Action = spy.Action.ToString()
            });
        }
        return result;
    }
}
