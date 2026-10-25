using Unciv.Queries.Empire;
using Unciv.Queries.Units;

namespace Unciv.GodotClient.Play;

/// <summary>
/// The client's questions to the engine (T185/T195): previews only, always as the viewer and never mutating, asked through the <see cref="IGameSession"/>.
/// The client renders the answer and never recomputes reach, cost or path. A refused query is reported as null, so the view just shows nothing.
/// </summary>
public sealed class GameQueries(IGameSession session)
{
    public Task<EmpireTopBarResult?> TopBar() => session.QueryAsync(new EmpireTopBarQuery());

    public Task<PendingDecisionsResult?> Pending() => session.QueryAsync(new PendingDecisionsQuery());

    public Task<ReachableTilesResult?> Reachable(int unitId) => session.QueryAsync(new ReachableTilesQuery(unitId));

    public Task<PathPreviewResult?> Path(int unitId, int x, int y) => session.QueryAsync(new PathPreviewQuery(unitId, x, y));
}
