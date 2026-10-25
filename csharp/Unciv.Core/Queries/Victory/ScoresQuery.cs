using Unciv.View;

namespace Unciv.Queries.Victory;

public sealed record ScoreLine(string Name, double Value);

// Breakdown: the viewer's score by source, in the order Civilization.CalculateScoreBreakdown yields it (the double sum depends on it)
// Total: CalculateTotalScore, a Double like in Kotlin; the Score ranking is its truncation
public sealed record ScoresResult(IReadOnlyList<ScoreLine> Breakdown, double Total);

// T198: the viewer's own score. Other civs' scores are only available as the Score ranking (RankingsQuery). A spectator or
// minor civ has no score.
[QueryType("victory.Scores")]
public sealed record ScoresQuery : IQuery<ScoresResult>
{
    public string Type => "victory.Scores";

    public ScoresResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        if (!civ.IsMajorCiv()) return new ScoresResult([], 0.0);
        var lines = view.CivView.CalculateScoreBreakdown().Select(it => new ScoreLine(it.Key, it.Value)).ToList();
        return new ScoresResult(lines, civ.CalculateTotalScore());
    }
}
