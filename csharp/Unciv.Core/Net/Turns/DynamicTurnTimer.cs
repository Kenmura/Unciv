using Unciv.Logic;

namespace Unciv.Net.Turns;

/// <summary>
/// T248: host policy that sizes the simultaneous-turn timer from the game state: later eras, more units and more active wars give players more time.
/// A pure function of the game state (no clock, no randomness, integer arithmetic only); the timer expiry still ends turns through <c>ForceEndTurn</c>, so the command log and replay are unchanged.
/// </summary>
public static class DynamicTurnTimer
{
    /// <summary>Load points per era number of the most advanced living human civ.</summary>
    public const int PointsPerEra = 60;

    /// <summary>Load points per unit of all living major civs.</summary>
    public const int PointsPerUnit = 2;

    /// <summary>Load points per active war between two living major civs.</summary>
    public const int PointsPerWar = 50;

    /// <summary>Load at or above which the timer is at its maximum.</summary>
    public const int FullLoad = 1000;

    /// <summary>The load of <paramref name="game"/> in points, 0 to <see cref="FullLoad"/>.</summary>
    public static int Load(GameInfo game)
    {
        long era = 0, units = 0, wars = 0;
        var majors = game.Civilizations.Where(c => c.IsMajorCiv() && !c.IsSpectator() && !c.IsDefeated()).ToList();
        foreach (var civ in majors)
        {
            if (civ.IsHuman()) era = Math.Max(era, civ.GetEraNumber());
            units += civ.Units.GetCivUnitsSize();
        }
        for (int i = 0; i < majors.Count; i++)
            for (int j = i + 1; j < majors.Count; j++)
                if (majors[i].IsAtWarWith(majors[j])) wars++;
        return (int)Math.Min(FullLoad, era * PointsPerEra + units * PointsPerUnit + wars * PointsPerWar);
    }

    /// <summary>The timer in seconds for the current state: <paramref name="minSeconds"/> at no load up to <paramref name="maxSeconds"/> at <see cref="FullLoad"/>. A maximum below the minimum wins (the lobby maximum is a hard cap).</summary>
    public static int Compute(GameInfo game, int minSeconds, int maxSeconds)
    {
        minSeconds = Math.Max(0, minSeconds);
        if (maxSeconds <= minSeconds) return Math.Max(0, maxSeconds);
        return (int)(minSeconds + (long)(maxSeconds - minSeconds) * Load(game) / FullLoad);
    }
}
