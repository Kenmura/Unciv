using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;

namespace Unciv.Logic.Map.MapGenerator;

// core/src/com/unciv/logic/map/mapgenerator/RiverGenerator.kt
// Handles River generation for MapGenerator, UniqueType.OneTimeChangeTerrain and console.
// Map generation follows the vertices of map hexes (SpawnRivers); in-game new rivers work on edges (ContinueRiverOn).
// Deviation: Kotlin lazy Sequences are materialised in the same order (no side effects inside them).
public class RiverGenerator(TileMap tileMap, MapGenerationRandomness randomness, Ruleset ruleset)
{
    private readonly float riverCountMultiplier = ruleset.ModOptions.Constants.RiverCountMultiplier;
    private readonly int minRiverLength = ruleset.ModOptions.Constants.MinRiverLength;
    private readonly int maxRiverLength = ruleset.ModOptions.Constants.MaxRiverLength;

    public void SpawnRivers(ISet<Tile>? resultingTiles = null)
    {
        if (maxRiverLength <= 0) return;
        if (!tileMap.Values.Any(it => it.IsWater)) return;
        int numberOfRivers = KotlinMath.RoundToInt(tileMap.Values.Count(it => it.IsLand) * riverCountMultiplier);

        var optionalTiles = tileMap.Values
            .Where(it => it.GetBaseTerrain().IsMountain && IsFarEnoughFromWater(it))
            .ToList();
        if (optionalTiles.Count < numberOfRivers)
            optionalTiles.AddRange(tileMap.Values.Where(it => it.IsHill() && IsFarEnoughFromWater(it)));
        if (optionalTiles.Count < numberOfRivers)
            optionalTiles = tileMap.Values.Where(it => it.IsLand && IsFarEnoughFromWater(it)).ToList();

        int mapRadius = tileMap.MapParameters.MapSize.Radius;
        var riverStarts = randomness.ChooseSpreadOutLocations(numberOfRivers, optionalTiles, mapRadius);
        foreach (var tile in riverStarts) SpawnRiver(tile, resultingTiles);
    }

    private bool IsFarEnoughFromWater(Tile tile)
    {
        for (int distance = 1; distance < minRiverLength; distance++)
        {
            if (tile.GetTilesAtDistance(distance).Any(it => it.IsWater)) return false;
        }
        return true;
    }

    public Tile? GetClosestWaterTile(Tile tile)
    {
        for (int distance = 1; distance <= maxRiverLength; distance++)
        {
            var waterTiles = tile.GetTilesAtDistance(distance).Where(it => it.IsWater).ToList();
            if (waterTiles.Count != 0)
                return waterTiles.Random(randomness.RNG);
        }
        return null;
    }

    private void SpawnRiver(Tile initialPosition, ISet<Tile>? resultingTiles)
    {
        var endPosition = GetClosestWaterTile(initialPosition)
            ?? throw new InvalidOperationException("No water found for river destination");
        SpawnRiver(initialPosition, endPosition, resultingTiles);
    }

    /// <summary>Spawns a river from <paramref name="initialPosition"/> to <paramref name="endPosition"/>.
    /// If <paramref name="resultingTiles"/> is supplied, it will contain all affected tiles, for map editor.</summary>
    public void SpawnRiver(Tile initialPosition, Tile endPosition, ISet<Tile>? resultingTiles)
    {
        // Recommendation: Draw a bunch of hexagons on paper before trying to understand this, it's super helpful!
        var sides = Enum.GetValues<RiverCoordinate.BottomRightOrLeft>();
        var riverCoordinate = new RiverCoordinate(tileMap, initialPosition.Position, sides.Random(randomness.RNG));

        for (int i = 0; i < maxRiverLength; i++) // Arbitrary max on river length, otherwise this will go in circles - rarely
        {
            if (riverCoordinate.GetAdjacentTiles().Any(it => it.IsWater)) return;
            var possibleCoordinates = riverCoordinate.GetAdjacentPositions().ToList();
            if (possibleCoordinates.Count == 0) return; // end of the line
            var keyed = possibleCoordinates
                .Select(coordinate => (coordinate, key: coordinate.GetAdjacentTiles().Select(it => it.AerialDistanceTo(endPosition)).Min()))
                .ToList();
            int minKey = keyed.Min(it => it.key);
            var newCoordinate = keyed.Where(it => it.key == minKey).Select(it => it.coordinate).ToList().Random(randomness.RNG);

            // set one new river edge in place
            riverCoordinate.PaintTo(newCoordinate, resultingTiles);
            // Move on
            riverCoordinate = newCoordinate;
        }
        Log.Debug("River reached max length!");
    }

    /// <summary>Describes a _Vertex_ on our hexagonal grid via a neighboring hex and clock direction, normalized
    /// such that always the north-most hex and one of the two clock directions 5 / 7 o'clock are used.</summary>
    public class RiverCoordinate
    {
        public enum BottomRightOrLeft
        {
            /// <summary>7 O'Clock of the tile</summary>
            BottomLeft,

            /// <summary>5 O'Clock of the tile</summary>
            BottomRight
        }

        private readonly TileMap tileMap;
        private readonly HexCoord position;
        private readonly BottomRightOrLeft bottomRightOrLeft;
        private readonly Tile myTile;
        private readonly Tile? myTopLeft;
        private readonly Tile? myBottomLeft;
        private readonly Tile? myTopRight;
        private readonly Tile? myBottomRight;
        private readonly Tile? myBottomCenter;

        public RiverCoordinate(TileMap tileMap, HexCoord position, BottomRightOrLeft bottomRightOrLeft)
        {
            this.tileMap = tileMap;
            this.position = position;
            this.bottomRightOrLeft = bottomRightOrLeft;
            int x = position.x;
            int y = position.y;
            myTile = tileMap[position];
            myTopLeft = tileMap.GetIfTileExistsOrNull(x + 1, y);
            myBottomLeft = tileMap.GetIfTileExistsOrNull(x, y - 1);
            myTopRight = tileMap.GetIfTileExistsOrNull(x, y + 1);
            myBottomRight = tileMap.GetIfTileExistsOrNull(x - 1, y);
            myBottomCenter = tileMap.GetIfTileExistsOrNull(x - 1, y - 1);
        }

        /// <summary>Lists the three neighboring vertices which have their anchor hex on the map
        /// (yes some positions on the map's outer border will be included, some not)</summary>
        public IEnumerable<RiverCoordinate> GetAdjacentPositions()
        {
            // What's nice is that adjacents are always the OPPOSITE in terms of right-left - rights are adjacent to only lefts, and vice-versa
            // This means that a lot of obviously-wrong assignments are simple to spot
            if (bottomRightOrLeft == BottomRightOrLeft.BottomLeft)
            {
                yield return new RiverCoordinate(tileMap, position, BottomRightOrLeft.BottomRight); // same tile, other side
                if (myTopLeft != null)
                    yield return new RiverCoordinate(tileMap, myTopLeft.Position, BottomRightOrLeft.BottomRight); // tile to MY top-left, take its bottom right corner
                if (myBottomLeft != null)
                    yield return new RiverCoordinate(tileMap, myBottomLeft.Position, BottomRightOrLeft.BottomRight); // Tile to MY bottom-left, take its bottom right
            }
            else
            {
                yield return new RiverCoordinate(tileMap, position, BottomRightOrLeft.BottomLeft); // same tile, other side
                if (myTopRight != null)
                    yield return new RiverCoordinate(tileMap, myTopRight.Position, BottomRightOrLeft.BottomLeft); // tile to MY top-right, take its bottom left
                if (myBottomRight != null)
                    yield return new RiverCoordinate(tileMap, myBottomRight.Position, BottomRightOrLeft.BottomLeft); // tile to MY bottom-right, take its bottom left
            }
        }

        /// <summary>Lists the three neighboring hexes to this vertex which are on the map</summary>
        public IEnumerable<Tile> GetAdjacentTiles()
        {
            yield return myTile;
            if (myBottomCenter != null) yield return myBottomCenter; // tile directly below us,
            if (bottomRightOrLeft == BottomRightOrLeft.BottomLeft)
            {
                if (myBottomLeft != null) yield return myBottomLeft; // tile to our bottom-left
            }
            else if (myBottomRight != null)
                yield return myBottomRight; // tile to our bottom-right
        }

        public void PaintTo(RiverCoordinate newCoordinate, ISet<Tile>? resultingTiles)
        {
            if (newCoordinate.position == position) // same tile, switched right-to-left
                PaintBottom(resultingTiles);
            else if (bottomRightOrLeft == BottomRightOrLeft.BottomRight)
            {
                if (newCoordinate.GetAdjacentTiles().Contains(myTile)) // moved from our 5 O'Clock to our 3 O'Clock
                    PaintBottomRight(resultingTiles);
                else // moved from our 5 O'Clock down in the 5 O'Clock direction - this is the 8 O'Clock river of the tile to our 4 O'Clock!
                    newCoordinate.PaintBottomLeft(resultingTiles);
            }
            else // bottomRightOrLeft == BottomRightOrLeft.BottomLeft
            {
                if (newCoordinate.GetAdjacentTiles().Contains(myTile)) // moved from our 7 O'Clock to our 9 O'Clock
                    PaintBottomLeft(resultingTiles);
                else // moved from our 7 O'Clock down in the 7 O'Clock direction
                    newCoordinate.PaintBottomRight(resultingTiles);
            }
        }

        private void PaintBottom(ISet<Tile>? resultingTiles)
        {
            myTile.HasBottomRiver = true;
            if (resultingTiles == null) return;
            resultingTiles.Add(myTile);
            if (myBottomCenter != null) resultingTiles.Add(myBottomCenter);
        }

        private void PaintBottomLeft(ISet<Tile>? resultingTiles)
        {
            myTile.HasBottomLeftRiver = true;
            if (resultingTiles == null) return;
            resultingTiles.Add(myTile);
            if (myBottomLeft != null) resultingTiles.Add(myBottomLeft);
        }

        private void PaintBottomRight(ISet<Tile>? resultingTiles)
        {
            myTile.HasBottomRightRiver = true;
            if (resultingTiles == null) return;
            resultingTiles.Add(myTile);
            if (myBottomRight != null) resultingTiles.Add(myBottomRight);
        }

        /// <summary>Count edges with a river from this vertex (kept as how-to, unused)</summary>
        public int NumberOfConnectedRivers()
        {
            var edges = new List<bool> { myTile.HasBottomRiver };
            if (bottomRightOrLeft == BottomRightOrLeft.BottomLeft)
            {
                edges.Add(myTile.HasBottomLeftRiver);
                edges.Add(myBottomLeft?.HasBottomRightRiver == true);
            }
            else
            {
                edges.Add(myTile.HasBottomRightRiver);
                edges.Add(myBottomRight?.HasBottomLeftRiver == true);
            }
            return edges.Count(it => it);
        }
    }

    public enum RiverDirections
    {
        North = 12,
        NorthEast = 2,
        SouthEast = 4,
        South = 6,
        SouthWest = 8,
        NorthWest = 10
    }

    // Kotlin: enum entries in declaration order (the numeric values are the clock positions, not the ordinals)
    public static IReadOnlyList<RiverDirections> RiverDirectionEntries { get; } =
        [RiverDirections.North, RiverDirections.NorthEast, RiverDirections.SouthEast, RiverDirections.South, RiverDirections.SouthWest, RiverDirections.NorthWest];

    public static IReadOnlyList<string> RiverDirectionNames => RiverDirectionEntries.Select(it => it.ToString()).ToList();

    public static Tile? GetNeighborTile(RiverDirections direction, Tile selectedTile) =>
        selectedTile.TileMap.GetClockPositionNeighborTile(selectedTile, (int)direction);

    /// <summary>UniqueType.OneTimeChangeTerrain tries to place a "River" feature.
    /// Operates on *edges* - while SpawnRiver hops from vertex to vertex!
    /// Placed here to make comparison easier, even though the implementation has nothing else in common.</summary>
    /// <returns>success - one edge of <paramref name="tile"/> has a new river</returns>
    public static bool ContinueRiverOn(Tile tile)
    {
        if (!tile.IsLand) return false;
        var tileMap = tile.TileMap;

        // Collect data (includes tiles we already have a river edge with - need the stats)
        var viableNeighbors = tile.Neighbors.Where(it => it.IsLand).Select(it => new NeighborData(tile, tileMap, it)).ToList();
        if (viableNeighbors.All(it => it.IsConnectedByRiver)) return false; // cannot place another river

        // Greatly encourage connecting to sea unless the tile already has a river to sea, in which case slightly discourage another one
        int edgeToSeaPriority = !viableNeighbors.Any(it => it.IsConnectedByRiver && it.EdgeLeadsToSea) ? 9 : -1;

        var rng = new GameContext(tile: tile).StateBasedRandom("RiverGenerator.continueRiverOn");
        var groups = viableNeighbors.Select(it => (it, priority: it.GetPriority(edgeToSeaPriority))).ToList(); // Assign and group by priorities
        int best = groups.Max(it => it.priority);
        var choice = groups.Where(it => it.priority == best).Select(it => it.it).ToList().Random(rng); // the List with best priority - can't be empty

        return tile.SetConnectedByRiver(choice.OtherTile, newValue: true, convertTerrains: true);
    }

    /// <summary>Helper to prioritize a tile edge for river placement - considers the edge common with <see cref="OtherTile"/>
    /// in direction <see cref="ClockPosition"/>, two additional tiles (those neighbor to both) and four other edges (those connecting to "our" edge).</summary>
    private sealed class NeighborData
    {
        public Tile OtherTile { get; }
        public int ClockPosition { get; }
        public bool IsConnectedByRiver { get; }
        public bool EdgeLeadsToSea { get; }
        private readonly int connectedRiverCount;
        private readonly int verticesFormYCount;

        public NeighborData(Tile tile, TileMap tileMap, Tile otherTile)
        {
            OtherTile = otherTile;
            ClockPosition = tileMap.GetNeighborTileClockPosition(tile, otherTile);
            IsConnectedByRiver = tile.IsConnectedByRiver(otherTile);
            // Similar: private fun Tile.getLeftSharedNeighbor in TileLayerBorders
            var leftSharedNeighbor = tileMap.GetClockPositionNeighborTile(tile, (ClockPosition - 2) % 12);
            var rightSharedNeighbor = tileMap.GetClockPositionNeighborTile(tile, (ClockPosition + 2) % 12);
            EdgeLeadsToSea = leftSharedNeighbor?.IsWater == true || rightSharedNeighbor?.IsWater == true;
            connectedRiverCount = new[]
            {
                leftSharedNeighbor?.IsConnectedByRiver(tile),
                leftSharedNeighbor?.IsConnectedByRiver(otherTile),
                rightSharedNeighbor?.IsConnectedByRiver(tile),
                rightSharedNeighbor?.IsConnectedByRiver(otherTile),
            }.Count(it => it == true);
            verticesFormYCount = new[]
            {
                leftSharedNeighbor == null ? (bool?)null : leftSharedNeighbor.IsConnectedByRiver(tile) && leftSharedNeighbor.IsConnectedByRiver(otherTile),
                rightSharedNeighbor == null ? (bool?)null : rightSharedNeighbor.IsConnectedByRiver(tile) && rightSharedNeighbor.IsConnectedByRiver(otherTile),
            }.Count(it => it == true);
        }

        public int GetPriority(int edgeToSeaPriority)
        {
            // choose a priority - only order matters, not magnitude
            if (IsConnectedByRiver) return -9; // ensures this isn't chosen, otherwise "cannot place another river" would have bailed
            if (EdgeLeadsToSea) return edgeToSeaPriority + connectedRiverCount - 3 * verticesFormYCount;
            // Just 6 possible cases left:
            //  * Connect two bends = -2
            //  * Connect a bend to nothing = -1 // debatable!
            //  * Connect nothing = 0
            //  * Connect a bend with an open end = 1
            //  * Connect to one open end = 2
            //  * Connect two open ends = 3 (make that 4 to simplify)
            if (verticesFormYCount == 2) return -2;
            if (verticesFormYCount == 1) return connectedRiverCount * 2 - 5;
            return connectedRiverCount * 2;
        }
    }
}
