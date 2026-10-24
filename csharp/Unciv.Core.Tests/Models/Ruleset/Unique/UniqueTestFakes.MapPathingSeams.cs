using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

// The members BFS, AStar, MapPathing, CarrierSlotMatcher and MapVisualization (T040) read from the game-state interfaces;
// tests override what they use.

internal partial class FakeGameInfo
{
    public virtual TileMap TileMap => throw new NotSupportedException();
}

internal partial class FakeCiv
{
    public virtual bool HasExplored(TileClass tile) => throw new NotSupportedException();
    public virtual bool CanBuildImprovementOn(TileClass tile, TileImprovement improvement) => throw new NotSupportedException();
}

internal partial class FakeUnit
{
    public virtual IEnumerable<HexCoord> MovementMemoryPositions => throw new NotSupportedException();
    public virtual bool IsVisibleTo(IGameContextCivilization civ) => throw new NotSupportedException();
}
