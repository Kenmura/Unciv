using Unciv.Logic;
using Unciv.Queries;
using Unciv.Queries.Units;
using Unciv.View;

namespace Unciv.GodotClient.Play;

/// <summary>
/// The client's questions to the engine (T185/T195): previews only, always as the human's civ and never mutating. The client renders the answer and
/// never recomputes reach, cost or path. A refused query (unknown unit, unexplored target) is reported as null, not thrown, so the view just shows nothing.
/// </summary>
public sealed class GameQueries(GameInfo game, string viewerCivId)
{
    private GameView View() => new(game, game.GetCivilization(viewerCivId));

    public ReachableTilesResult? Reachable(int unitId) => Run(new ReachableTilesQuery(unitId));

    public PathPreviewResult? Path(int unitId, int x, int y) => Run(new PathPreviewQuery(unitId, x, y));

    private TResult? Run<TResult>(IQuery<TResult> query) where TResult : class
    {
        try
        {
            return query.Run(View());
        }
        catch (QueryException)
        {
            return null;
        }
    }
}
