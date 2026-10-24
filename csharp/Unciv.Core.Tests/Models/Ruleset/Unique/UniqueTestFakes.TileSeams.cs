using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueGameContext = Unciv.Models.Ruleset.Unique.GameContext;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

// The members the Tile class (T053) reads from the game-state interfaces. The unique tests never reach them; the Tile tests override what they use.

internal partial class FakeGameInfo
{
    public virtual IGameContextCivilization GetCivilization(string civId) => throw new NotSupportedException();
    public virtual Unciv.Models.ModConstants GetModConstants() => throw new NotSupportedException();
    public virtual EngineOptions EngineOptions { get; } = new();
}

internal partial class FakeCiv
{
    public virtual PlayerType PlayerType => PlayerType.AI;
    public virtual bool IsSpectator() => false;
    public virtual bool IsTileViewable(TileClass tile) => throw new NotSupportedException();
    public virtual string? GetLastSeenImprovement(HexCoord position) => throw new NotSupportedException();
    public virtual bool Knows(IGameContextCivilization otherCiv) => throw new NotSupportedException();
    public virtual bool IsAtWarWith(IGameContextCivilization otherCiv) => throw new NotSupportedException();
    public virtual bool IsConsideredFriendlyTerritoryOf(IGameContextCivilization otherCiv) => throw new NotSupportedException();
    public virtual bool CanSeeResource(TileResource? resource) => throw new NotSupportedException();
    public virtual bool CanPassThroughTiles(IGameContextCivilization otherCiv) => throw new NotSupportedException();
    public virtual bool ForestsAndJunglesAreRoads => false;
    public virtual ISet<HexCoord> NeutralRoads { get; } = new HashSet<HexCoord>();
    public virtual void CheckExploredRegionTilePosition(HexCoord tilePosition, HexCoord? explorerPosition) => throw new NotSupportedException();
    public virtual bool MatchesFilter(string filter, UniqueGameContext? state, bool multiFilter) => throw new NotSupportedException();
}

internal partial class FakeCity
{
    public virtual string Name => throw new NotSupportedException();
    public virtual HexCoord Location => throw new NotSupportedException();
    public virtual bool IsWorked(TileClass tile) => throw new NotSupportedException();
    public virtual bool IsTileLocked(HexCoord position) => throw new NotSupportedException();
    public virtual bool HasJustBeenConquered => throw new NotSupportedException();
    public virtual bool IsCapital() => throw new NotSupportedException();
    public virtual void ReassignPopulationDeferred() => throw new NotSupportedException();
}

internal partial class FakeUnit
{
    public virtual IGameContextUnit Clone() => throw new NotSupportedException();
    public virtual void SetCurrentTile(TileClass tile) => throw new NotSupportedException();
    public virtual string Owner => throw new NotSupportedException();
    public virtual void AssignOwner(IGameContextCivilization civInfo, bool updateCivInfo) => throw new NotSupportedException();
    public virtual void SetTransients(Unciv.Models.Ruleset.Ruleset ruleset) => throw new NotSupportedException();
    public virtual bool IsEmbarked() => throw new NotSupportedException();
    public virtual UniqueGameContext CacheState => throw new NotSupportedException();
}
