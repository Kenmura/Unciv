using Unciv.Logic.Map.Tiles;

namespace Unciv.Logic.Map.MapUnits;

// T065 (MapUnit): the seams of MapUnit, which is ported BEFORE its sub-objects (seam rule, T053 notes). Each interface holds exactly the
// members MapUnit.kt / MapUnitCache.kt call on that type; the ticket that ports the type implements it, retypes the property in MapUnit to
// the concrete class and replaces the lateinit-style property by Kotlin's initializer (`= new UnitMovement(this)`).
// Member names mirror the Kotlin members.

/// <summary>What MapUnit reads from <c>UnitMovement</c> (<c>unit.movement</c>). T067 implements it and retypes <see cref="MapUnit.Movement"/>.</summary>
public interface IUnitMovement
{
    /// <summary><c>unit.movement.canMoveTo(tile)</c></summary>
    bool CanMoveTo(Tile tile);
    /// <summary><c>unit.movement.canPassThrough(tile)</c></summary>
    bool CanPassThrough(Tile tile);
    /// <summary><c>unit.movement.getCannotMoveToReason(tile)</c></summary>
    string GetCannotMoveToReason(Tile tile);
    /// <summary><c>unit.movement.teleportToClosestMoveableTile()</c></summary>
    void TeleportToClosestMoveableTile();
    /// <summary><c>unit.movement.clearPathfindingCache()</c></summary>
    void ClearPathfindingCache();
    /// <summary><c>unit.movement.getDistanceToTiles().keys</c> (the tiles reachable this turn).</summary>
    IEnumerable<Tile> GetDistanceToTilesKeys();
    /// <summary><c>unit.movement.canReach(destination)</c></summary>
    bool CanReach(Tile destination);
    /// <summary><c>unit.movement.headTowards(destination)</c>; the tile the unit got to.</summary>
    Tile HeadTowards(Tile destination);
    /// <summary><c>unit.movement.moveToTile(destination)</c></summary>
    void MoveToTile(Tile destination);
}

/// <summary>What CityConstructions reads from <c>PromotionTree</c>. TODO(T126)</summary>
public interface IPromotionTree
{
    /// <summary><c>promotionTree.possiblePromotions</c> (the promotion names, in tree order).</summary>
    IEnumerable<string> PossiblePromotionNames { get; }
    /// <summary><c>promotionTree.allNodes()</c> (the node promotion names, in order).</summary>
    IEnumerable<string> AllNodePromotionNames();
}

/// <summary>What MapUnit reads from <c>BarbarianManager</c> (<c>gameInfo.barbarians</c>). T078 implements it.</summary>
public interface IBarbarianManager
{
    /// <summary><c>barbarians.spawnBarbarian(tile, civ)</c>; <c>null</c> when no unit could be placed.</summary>
    MapUnit? SpawnBarbarian(Tile tile, Civilization.Civilization civ);
}

/// <summary>The <c>UnitActionType.value</c> strings MapUnit compares its <c>action</c> against. TODO(T079): replaced by UnitActionType.</summary>
internal static class UnitActionNames
{
    public const string Automate = "Automate";
    public const string Sleep = "Sleep";
    public const string Fortify = "Fortify";
    public const string Guard = "Guard";
    public const string Explore = "Explore";
    public const string Paradrop = "Paradrop";
    public const string AirSweep = "Air Sweep";
    public const string SetUp = "Set up";
}
