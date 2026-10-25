using Unciv.Logic;
using Unciv.View;

namespace Unciv.Queries;

// Resolves a session's viewer id to the GameView a query runs against. Views are built per request so they see the live state.
public sealed class QueryContext(GameInfo game)
{
    public GameInfo Game { get; } = game;

    public bool TryCreateView(string viewerId, out GameView? view)
    {
        view = null;
        var civ = Game.GetCivilizationOrNull(viewerId);
        if (civ == null) return false;
        view = new GameView(Game, civ, spectatorMode: civ.IsSpectator());
        return true;
    }
}
