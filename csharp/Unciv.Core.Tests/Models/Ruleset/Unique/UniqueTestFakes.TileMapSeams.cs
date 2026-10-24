using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

// The members TileMap (T039) reads from the game-state interfaces; tests override what they use.

internal partial class FakeGameInfo
{
    public virtual BaseUnit GetBaseUnit(string unitName) => throw new NotSupportedException();
}

internal partial class FakeCiv
{
    public virtual IGameContextUnit NewMapUnit(BaseUnit baseUnit, int? unitId) => throw new NotSupportedException();
    public virtual void RemoveUnit(IGameContextUnit unit, bool updateCivInfo) => throw new NotSupportedException();
    public virtual void ForEachMatchingUnique(UniqueType uniqueType, Action<Unciv.Models.Ruleset.Unique.Unique> action) => throw new NotSupportedException();
    public virtual void UpdateStatsForNextTurn() => throw new NotSupportedException();
    public virtual void UpdateCivResources() => throw new NotSupportedException();
}

internal partial class FakeUnit
{
    public virtual BaseUnit BaseUnit => throw new NotSupportedException();
    public virtual bool CanMoveOnWater => throw new NotSupportedException();
    public virtual bool CanPassThrough(TileClass tile) => throw new NotSupportedException();
    public virtual bool CanMoveTo(TileClass tile) => throw new NotSupportedException();
    public virtual void SetOriginalOwner(string civId) => throw new NotSupportedException();
    public virtual void SetCacheState(GameContext state) => throw new NotSupportedException();
    public virtual void PutInTile(TileClass tile) => throw new NotSupportedException();
    public virtual void RemoveFromTile() => throw new NotSupportedException();
    public virtual int GetMaxMovement() => throw new NotSupportedException();
    public virtual void SetCurrentMovement(float movement) => throw new NotSupportedException();
    public virtual void AddMovementMemory() => throw new NotSupportedException();
    public virtual void AddPromotion(string promotionName, bool isFree) => throw new NotSupportedException();
    public virtual bool HasResourceRequirementsPerTurn() => throw new NotSupportedException();
}
