using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.Tiles;
using Unciv.Utils;

namespace Unciv.Logic.Map.PathingMap;

// Ported from core/src/com/unciv/logic/map/pathingmap/RouteNode.kt (T093). Stripped: the purity annotations.
// - Kotlin `require(...)` throws IllegalArgumentException: ArgumentException here.
// - The bit layout is the Kotlin one, field for field (see the table on the class).

/// <summary>
/// All the information we need about a route node, crammed into a single Long (a value class in Kotlin, a struct here).
///
/// Fields, in order from least to highest priority (the Kotlin class documents each in depth):
/// tileIdx (bits 0-19), relationshipLevel (20-22, stored as 7-ordinal), moveSinceStoppable (23-31, base-30 fixed point),
/// moveUsedThisTurn (32-40), turns (41-46), then either the PrioritizedNode's underestimatedTotal (47-60, 14 bits) or the
/// RouteNode's parentClockDir (47-49, clock direction / 2, 7 = no parent) and the endTurnWithoutMoreDamage flag (bit 50),
/// damagingTiles (61-62). The sign bit stays zero so a PrioritizedNode compares as a plain Long.
/// Tiles that cannot be pathed to at all store the maximum value in all fields except the tile index.
/// </summary>
public readonly struct RouteNode
{
    // bits 0-19 (20b = 1048576tiles) are the zeroBasedIndex of this tile (radius 500, or approx 1170x896)
    internal const int TileIdxOffset = 0;
    internal const int TileIdxBitCount = 20;
    internal const long TileIdxLoMask = (1L << TileIdxBitCount) - 1L;
    // bits 20-22 (3b = 8values) are our relationship with the owning civ
    private const int RelationshipLevelOffset = TileIdxOffset + TileIdxBitCount;
    private const int RelationshipLevelBitCount = 3;
    private const long RelationshipLevelLoMask = (1L << RelationshipLevelBitCount) - 1L;
    private const int MaxRelationshipLevel = 7;
    // bits 23-31 (9b) are the base-30 movement used since the last tile we could physically stop on at all.
    private const int MoveSinceStoppableOffset = RelationshipLevelOffset + RelationshipLevelBitCount;
    private const int MoveSinceStoppableBitCount = 9;
    private const long MoveSinceStoppableLoMask = (1L << MoveSinceStoppableBitCount) - 1L;
    // bits 32-40 (9b) are the base-30 movement used on this turn.
    private const int MoveThisTurnOffset = MoveSinceStoppableOffset + MoveSinceStoppableBitCount;
    private const int MoveThisTurnBitCount = 9;
    private const long MoveThisTurnLoMask = (1L << MoveThisTurnBitCount) - 1L;
    public static readonly FixedPointMovement MaxMoveThisTurn = FixedPointMovement.FpmFromFixedPointBits((int)MoveThisTurnLoMask);
    // bits 41-46 (6b = 63turns) are the number of turns to get to this tile. 0=This turn.
    private const int TurnsOffset = MoveThisTurnOffset + MoveThisTurnBitCount;
    private const int TurnsBitCount = 6;
    private const long TurnsLoMask = (1L << TurnsBitCount) - 1L;
    public const int MaxTurns = (int)TurnsLoMask;
    public const int MaxValidTurns = MaxTurns - 1;
    // [PrioritizedNode] bits 47-60 (14b) are the underestimated total movement from the initial tile toward the target.
    internal const int UnderestimatedTotalOffset = TurnsOffset + TurnsBitCount;
    internal const int UnderestimatedTotalBitCount = 14;
    internal const long UnderestimatedTotalLoMask = (1L << UnderestimatedTotalBitCount) - 1L;
    internal const long UnderestimatedTotalHiMask = UnderestimatedTotalLoMask << UnderestimatedTotalOffset;
    internal static readonly FixedPointMovement MaxUnderestimatedTotal = FixedPointMovement.FpmFromFixedPointBits((int)UnderestimatedTotalLoMask);
    // [RouteNode] bits 47-49 (3b) are the parent tile clock direction/2. no-Parent is "7", and "0" is never valid
    private const int ParentTileOffset = UnderestimatedTotalOffset;
    private const int ParentTileBitCount = 3;
    private const long ParentTileLoMask = (1L << ParentTileBitCount) - 1L;
    private const long NoParentTileBits = 7L;
    private const int NoParentTileValue = 14;
    // [RouteNode] bit 50 tracks whether THIS tile itself is damaging, unconditionally.
    private const int DamagingOffset = ParentTileOffset + ParentTileBitCount;
    // bits 61-62 (2b = 4turns) are the number of turns ended in damaging tiles.
    private const int DamageTilesOffset = UnderestimatedTotalOffset + UnderestimatedTotalBitCount;
    private const int DamageTilesBitCount = 2;
    private const long DamageTilesLoMask = (1L << DamageTilesBitCount) - 1L;
    internal const int MaxDamagingTiles = (int)DamageTilesLoMask;

    public long Bits { get; }

    public RouteNode(long bits = 0L) => Bits = bits;

    public RouteNode(
        Tile tile,
        RelationshipLevel relationshipLevel,
        FixedPointMovement moveSinceStoppable,
        FixedPointMovement moveThisTurn,
        int turns,
        Tile parentTile,
        int damagingTiles,
        bool damaging)
        : this(
            ToTileIdxBits(tile)
            | ToRelationshipLevelBits(relationshipLevel)
            | ToMoveSinceStoppableBits(moveSinceStoppable)
            | ToMoveThisTurnBits(moveThisTurn)
            | ToTurnsBits(turns)
            | ToParentClockDirBits(tile, parentTile)
            | ToDamagingTilesBits(damagingTiles)
            | ToDamagingBits(damaging))
    {
        if (tile.ZeroBasedIndex >= tile.TileMap.TileList.Count)
            throw new ArgumentException($"tileList {tile.ZeroBasedIndex} exceeds max {tile.TileMap.TileList.Count}");
        if (tile.TileMap.TileList.Count > TileIdxLoMask)
            throw new ArgumentException($"tileList {tile.TileMap.TileList.Count} exceeds max {TileIdxLoMask}");
        if (moveSinceStoppable < 0) throw new ArgumentException($"moveSinceStoppableThisTurn {moveSinceStoppable} must be positive");
        if (moveSinceStoppable > MaxMoveThisTurn)
            throw new ArgumentException($"moveSinceStoppableThisTurn {moveSinceStoppable} exceeds max {MaxMoveThisTurn}");
        if (moveThisTurn < 0) throw new ArgumentException($"moveThisTurn {moveThisTurn} must be positive");
        if (moveThisTurn > MaxMoveThisTurn) throw new ArgumentException($"moveThisTurn {moveThisTurn} exceeds max {MaxMoveThisTurn}");
        if (turns < 0) throw new ArgumentException($"turns {turns} must be positive");
        if (turns > MaxTurns) throw new ArgumentException($"turns {turns} exceeds max {MaxTurns}");
        if (ToParentClockDirBits(tile, parentTile) <= 0) throw new ArgumentException($"parentClockDir {ParentClockDirOf(tile, parentTile)} must be positive");
        if (damagingTiles < 0) throw new ArgumentException($"damagingTiles {damagingTiles} must be positive");
        if (damagingTiles > DamageTilesLoMask) throw new ArgumentException($"damagingTiles {moveThisTurn} exceeds max {DamageTilesLoMask}");
    }

    public int TileIdx
    {
        get
        {
            RequireInitialized();
            return (int)((Bits >> TileIdxOffset) & TileIdxLoMask);
        }
    }

    public Tile GetTile(TileMap tileMap) => tileMap.TileList[TileIdx];

    private long RelationshipLevelBits
    {
        get
        {
            RequireInitialized();
            return (Bits >> RelationshipLevelOffset) & RelationshipLevelLoMask;
        }
    }

    public RelationshipLevel RelationshipLevel => (RelationshipLevel)(MaxRelationshipLevel - (int)RelationshipLevelBits);

    public FixedPointMovement MoveSinceStoppable
    {
        get
        {
            RequireInitialized();
            return FixedPointMovement.FpmFromFixedPointBits((int)((Bits >> MoveSinceStoppableOffset) & MoveSinceStoppableLoMask));
        }
    }

    public FixedPointMovement MoveUsedThisTurn
    {
        get
        {
            RequireInitialized();
            return FixedPointMovement.FpmFromFixedPointBits((int)((Bits >> MoveThisTurnOffset) & MoveThisTurnLoMask));
        }
    }

    public int Turns
    {
        get
        {
            RequireInitialized();
            return (int)((Bits >> TurnsOffset) & TurnsLoMask);
        }
    }

    public int ParentClockDir
    {
        get
        {
            RequireInitialized();
            return (int)(((Bits >> ParentTileOffset) & ParentTileLoMask) * 2);
        }
    }

    public Tile ParentTile(TileMap tileMap)
    {
        var idx = ParentClockDir;
        if (idx == NoParentTileValue) return GetTile(tileMap);
        return tileMap.GetClockPositionNeighborTile(GetTile(tileMap), idx).NotNull();
    }

    // moveSinceStoppable resets to zero exactly when a tile is itself stoppable, so canStopOn is derived from it rather than stored as its own bit.
    public bool CanStopOn => MoveSinceStoppable == FixedPointMovement.FpmZero;

    public bool EndTurnWithoutMoreDamage
    {
        get
        {
            RequireInitialized();
            return ((Bits >> DamagingOffset) & 1L) == 0L;
        }
    }

    public int DamagingTiles
    {
        get
        {
            RequireInitialized();
            return (int)((Bits >> DamageTilesOffset) & DamageTilesLoMask);
        }
    }

    // parentClockDir can never be 0, so all zeroes means uninitialized
    public bool Initialized => Bits != 0L;

    internal bool IsNoPathingNode => MoveSinceStoppable.Bits == (int)MoveSinceStoppableLoMask;

    public override string ToString() => $"RouteNode[tile={TileIdx}, turns={Turns}, moveUsedThisTurn={MoveUsedThisTurn}]";

    public string ToString(TileMap tileMap) => $"RouteNode[tile={GetTile(tileMap)}, turns={Turns}, moveThisTurn={MoveUsedThisTurn}]";

    private void RequireInitialized()
    {
        if (!Initialized) throw new ArgumentException("Failed requirement.");
    }

    private static long ToParentClockDirBits(Tile tile, Tile parentTile) => ParentClockDirOf(tile, parentTile) << ParentTileOffset;

    // Kotlin Long division: -1 / 2L is 0, as in C#.
    private static long ParentClockDirOf(Tile tile, Tile parentTile) =>
        tile == parentTile ? NoParentTileBits : tile.TileMap.GetNeighborTileClockPosition(tile, parentTile) / 2L;

    private static long ToMoveThisTurnBits(FixedPointMovement moveThisTurn) => (long)moveThisTurn.Bits << MoveThisTurnOffset;
    private static long ToMoveSinceStoppableBits(FixedPointMovement moveSinceStoppable) => (long)moveSinceStoppable.Bits << MoveSinceStoppableOffset;
    private static long ToTileIdxBits(Tile tile) => (long)tile.ZeroBasedIndex << TileIdxOffset;
    private static long ToTurnsBits(int turns) => (long)turns << TurnsOffset;
    private static long ToDamagingTilesBits(int damagingTiles) => (long)damagingTiles << DamageTilesOffset;
    private static long ToDamagingBits(bool damaging) => (damaging ? 1L : 0L) << DamagingOffset;
    private static long ToRelationshipLevelBits(RelationshipLevel relationshipLevel) => (long)(MaxRelationshipLevel - (int)relationshipLevel) << RelationshipLevelOffset;

    public static RouteNode NoPathingNode(Tile tile, int turn) => new(
        tile,
        RelationshipLevel.Unforgivable,
        MaxMoveThisTurn,
        MaxMoveThisTurn,
        turn,
        tile,
        MaxDamagingTiles,
        false);

    public static RouteNode RootNode(Tile tile, FixedPointMovement moveThisTurn) => new(
        tile,
        RelationshipLevel.Favorable, // irrelevant since we start here
        FixedPointMovement.FpmZero,
        moveThisTurn,
        0,
        tile,
        0,
        false); // irrelevant: the root is never a candidate parentTile in pathAsListNoDamage
}
