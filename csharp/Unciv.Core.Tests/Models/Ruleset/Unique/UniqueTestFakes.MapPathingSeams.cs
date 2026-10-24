using Unciv.Logic.Map;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

// The members BFS, AStar, MapPathing, CarrierSlotMatcher and MapVisualization (T040) read from the game-state interfaces;
// T134 dropped MapUnit and CanBuildImprovementOn.
// TileMap and HasExplored remain until T046.

internal partial class FakeGameInfo
{
    public virtual TileMap TileMap => throw new NotSupportedException();
}

internal partial class FakeCiv
{
    public virtual bool HasExplored(TileClass tile) => throw new NotSupportedException();
}

