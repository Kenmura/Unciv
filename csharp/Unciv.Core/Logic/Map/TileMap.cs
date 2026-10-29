using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map.MapGenerator;
using Unciv.Logic.Map.Tiles;
using Unciv.Mathematics;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;

namespace Unciv.Logic.Map;

// Ported from core/src/com/unciv/logic/map/TileMap.kt (T039), against the seams in GameContextTileMap.cs / GameContextTile.cs.
//
// Stripped: the @Readonly purity annotations.
// T107 wired the Gdx-compatible JSON: TileMapJsonConverter, StartingLocationJsonConverter and PreviewJsonConverter at the end of the class.
// Deviations:
// - assignContinents flood-fills with a private helper instead of BFS (T040, which depends on this ticket). BFS only fed a set and a
//   count, so visiting order is irrelevant; T040 may switch it to BFS.
// - switchPlayersNation is not ported: Kotlin marks it "currently unreachable" (@Suppress("unused"), no caller), and it would read the
//   lateinit gameInfo of a freshly constructed Civilization.
// - startingLocationsByNation stays a Dictionary<string, HashSet<Tile>> (Tile has identity hashing in Kotlin too, so set order was
//   never deterministic there); continentSizes uses JavaHashMap because continentsSortedBySize is built from its iteration order.
// - Kotlin Sequence results are IEnumerable; the pure-function overloads with a default `filter` are separate overloads.

/// <summary>
/// An Unciv map with all properties as produced by the map editor or MapGenerator; or as part of a running game.
/// </summary>
[JsonConverter(typeof(TileMap.TileMapJsonConverter))]
public class TileMap : IsPartOfGameInfoSerialization
{
    #region Fields, Serialized

    public MapParameters MapParameters { get; set; } = new();

    public List<Tile> TileList { get; set; }

    /// <summary>Structure geared for simple serialization.</summary>
    /// <param name="Position">Location</param>
    /// <param name="Nation">Name of the nation</param>
    /// <param name="Usage">How the starting location may be used; the default is for maps saved prior to this feature</param>
    [JsonConverter(typeof(StartingLocationJsonConverter))]
    public sealed record StartingLocation(HexCoord Position = default, string Nation = "", StartingLocation.UsageType Usage = StartingLocation.UsageType.Player)
        : IsPartOfGameInfoSerialization
    {
        /// <summary>How a starting location may be used when the map is loaded for a new game</summary>
        public enum UsageType
        {
            /// <summary>Starting location only</summary>
            Normal,
            /// <summary>Use for "Select players from starting locations"</summary>
            Player,
            /// <summary>Use as first Human player</summary>
            Human,
        }

        public static string GetLabel(UsageType usage) => usage switch
        {
            UsageType.Normal => "None",
            UsageType.Player => "Player",
            UsageType.Human => "Human",
            _ => throw new ArgumentOutOfRangeException(nameof(usage)),
        };

        /// <summary>Kotlin <c>Usage.default</c></summary>
        public static UsageType DefaultUsage => UsageType.Player;
    }

    public List<StartingLocation> StartingLocations { get; } = [];

    /// <summary>Optional freeform text a mod map creator can set for their "customers"</summary>
    public string Description { get; set; } = "";

    #endregion
    #region Fields, Transient

    private IGameContextGameInfo? gameInfo;
    /// <summary>Attention: Kotlin <c>lateinit</c> will _stay uninitialized_ while in MapEditorScreen! Reading it before assignment throws.</summary>
    public IGameContextGameInfo GameInfo
    {
        get => gameInfo ?? throw new InvalidOperationException("lateinit property gameInfo has not been initialized");
        set => gameInfo = value;
    }

    /// <summary>Keep a copy of the <see cref="Models.Ruleset.Ruleset"/> object passed to setTransients, for now only to allow subsequent setTransients without. Copied on <see cref="Clone"/>.</summary>
    public Ruleset? Ruleset { get; set; }

    /// <summary>Kotlin <c>data class TerrainListData</c>: the combined uniques of a list of terrains, cached per terrain-name list.</summary>
    public sealed record TerrainListData(UniqueMap Uniques, IReadOnlySet<string> TerrainNameSet)
    {
        public static readonly TerrainListData Empty = new(UniqueMap.Empty, new HashSet<string>());
    }

    /// <summary>Kotlin keys it by <c>List&lt;String&gt;</c>, which compares and hashes by content.</summary>
    private sealed class TerrainNameListComparer : IEqualityComparer<IReadOnlyList<string>>
    {
        public static readonly TerrainNameListComparer Instance = new();

        public bool Equals(IReadOnlyList<string>? x, IReadOnlyList<string>? y) =>
            ReferenceEquals(x, y) || (x != null && y != null && x.SequenceEqual(y));

        public int GetHashCode(IReadOnlyList<string> obj)
        {
            var hash = 1;
            foreach (var name in obj) hash = unchecked(31 * hash + JavaHash.HashCode(name));
            return hash;
        }
    }

    public ConcurrentDictionary<IReadOnlyList<string>, TerrainListData> TileUniqueMapCache { get; set; } = new(TerrainNameListComparer.Instance);

    // this works several times faster than a hashmap, the performance difference is really astounding
    private List<List<Tile?>> tileMatrix = [];
    public IReadOnlyList<IReadOnlyList<Tile?>> TileMatrix => tileMatrix;

    public int LeftX { get; set; }
    public int BottomY { get; set; }
    public int Width { get; set; }

    private readonly KotlinLazy<int> maxLatitude;
    public int MaxLatitude => maxLatitude.Value;

    private readonly KotlinLazy<int> maxLongitude;
    public int MaxLongitude => maxLongitude.Value;

    private readonly KotlinLazy<LinkedHashSet<string>> naturalWonders;
    public IReadOnlySet<string> NaturalWonders => naturalWonders.Value;

    private readonly KotlinLazy<LinkedHashSet<string>> resources;
    public IReadOnlySet<string> Resources => resources.Value;

    private readonly KotlinLazy<LinkedHashSet<TileResource>> resourceObjects;
    public IReadOnlySet<TileResource> ResourceObjects => resourceObjects.Value;

    // Excluded from Serialization by having no own backing field
    public IReadOnlyList<Tile> Values => TileList;

    public Dictionary<string, HashSet<Tile>> StartingLocationsByNation { get; } = [];

    /// <summary>Continent ID to Continent size</summary>
    public JavaHashMap<int, int> ContinentSizes { get; } = new();

    /// <summary>Continent IDs sorted by size descending</summary>
    public List<int> ContinentsSortedBySize { get; } = [];

    #endregion
    #region Constructors

    /// <param name="initialCapacity">Passed to constructor of <see cref="TileList"/></param>
    public TileMap(int initialCapacity = 10)
    {
        TileList = new List<Tile>(initialCapacity);
        maxLatitude = new KotlinLazy<int>(() => Values.Count == 0 ? 0 : Values.Max(it => Math.Abs(it.Latitude)));
        maxLongitude = new KotlinLazy<int>(() => Values.Count == 0 ? 0 : Values.Max(it => Math.Abs(it.Longitude)));
        naturalWonders = new KotlinLazy<LinkedHashSet<string>>(() =>
            new LinkedHashSet<string>(TileList.Where(it => it.IsNaturalWonder()).Select(it => it.NaturalWonder.NotNull())));
        resources = new KotlinLazy<LinkedHashSet<string>>(() =>
            new LinkedHashSet<string>(TileList.Select(it => it.Resource).OfType<string>()));
        resourceObjects = new KotlinLazy<LinkedHashSet<TileResource>>(() =>
            new LinkedHashSet<TileResource>(TileList.Select(it => it.TileResource).OfType<TileResource>()));
    }

    /// <summary>
    /// creates a hexagonal map of given radius (filled with grassland)
    ///
    /// The rules are simple if you think about your X and Y axis as diagonal w.r.t. a standard carthesian plane. As such:
    /// moving "up": increase both X and Y by one; moving "down": decrease both X and Y by one;
    /// moving "up-right" and "down-left": moving along Y axis; moving "up-left" and "down-right": moving along X axis.
    ///
    /// Tip: you can always use the in-game map editor if you have any doubt,
    /// and the "secret" options can turn on coordinate display on the main map.
    /// </summary>
    public TileMap(int radius, Ruleset ruleset, bool worldWrap = false)
        : this(HexMath.GetNumberOfTilesInHexagon(radius))
    {
        StartingLocations.Clear();
        var firstAvailableLandTerrain = MapLandmassGenerator.GetInitializationTerrain(ruleset, TerrainType.Land);
        foreach (var vector in HexMath.GetHexCoordsInDistance(HexCoord.Zero, radius, worldWrap))
            TileList.Add(new Tile { Position = vector.AsSerializable(), BaseTerrain = firstAvailableLandTerrain });
        SetTransients(ruleset);
    }

    /// <summary>creates a rectangular map of given width and height (filled with grassland)</summary>
    public TileMap(int width, int height, Ruleset ruleset, bool worldWrap = false)
        : this(width * height)
    {
        StartingLocations.Clear();
        var firstAvailableLandTerrain = MapLandmassGenerator.GetInitializationTerrain(ruleset, TerrainType.Land);

        // world-wrap maps must always have an even width, so round down
        var wrapAdjustedWidth = worldWrap && width % 2 != 0 ? width - 1 : width;

        // Even widths will have coordinates ranging -x..(x-1), not -x..x, which is always an odd-sized range
        // e.g. w=4 -> -2..1, w=5 -> -2..2, w=6 -> -3..2, w=7 -> -3..3
        for (var column = -wrapAdjustedWidth / 2; column <= (wrapAdjustedWidth - 1) / 2; column++)
        {
            for (var row = -height / 2; row <= (height - 1) / 2; row++)
            {
                TileList.Add(new Tile
                {
                    Position = HexMath.GetTileCoordsFromColumnRow(column, row).AsSerializable(),
                    BaseTerrain = firstAvailableLandTerrain,
                });
            }
        }

        SetTransients(ruleset);
    }

    #endregion
    #region Operators and Standards

    /// <returns>a deep-copy clone of the serializable fields, no transients initialized</returns>
    public TileMap Clone()
    {
        var toReturn = new TileMap(TileList.Count);
        toReturn.TileList.AddRange(TileList.Select(it => it.Clone()));
        toReturn.MapParameters = MapParameters.Clone(); // T219: Kotlin shares the object
        toReturn.Ruleset = Ruleset;

        // Note during normal play this is empty. Supported for MapEditorScreen.getMapCloneForSave.
        toReturn.StartingLocations.Clear();
        toReturn.StartingLocations.EnsureCapacity(StartingLocations.Count);
        toReturn.StartingLocations.AddRange(StartingLocations);

        toReturn.Description = Description;
        toReturn.TileUniqueMapCache = TileUniqueMapCache;

        return toReturn;
    }

    public bool Contains(Vector2 vector) => Contains(KotlinMath.FloatToInt(vector.x), KotlinMath.FloatToInt(vector.y));
    public bool Contains(HexCoord hexCoord) => Contains(hexCoord.x, hexCoord.y);
    public bool Contains(int x, int y) => GetOrNull(x, y) != null;

    public Tile this[Vector2 vector] => this[KotlinMath.FloatToInt(vector.x), KotlinMath.FloatToInt(vector.y)];
    public Tile this[HexCoord hexCoord] => this[hexCoord.x, hexCoord.y];
    public Tile this[int x, int y] =>
        tileMatrix[x - LeftX][y - BottomY] ?? throw new InvalidOperationException($"No tile at ({x},{y})");

    /// <returns>tile at hex coordinates (<paramref name="x"/>,<paramref name="y"/>) or null if they are outside the map. Does *not* respect world wrap, use <see cref="GetIfTileExistsOrNull"/> for that.</returns>
    public Tile? GetOrNull(int x, int y)
    {
        var column = x - LeftX;
        if (column < 0 || column >= tileMatrix.Count) return null;
        var row = y - BottomY;
        var tiles = tileMatrix[column];
        return row < 0 || row >= tiles.Count ? null : tiles[row];
    }

    #endregion
    #region Pure Functions

    /// <summary>Can we access <see cref="GameInfo"/>? e.g. for MapEditor use where there is a map but no game</summary>
    public bool HasGameInfo() => gameInfo != null;

    /// <returns>All tiles in a hexagon of radius <paramref name="distance"/>, including the tile at <paramref name="origin"/> and all up to <paramref name="distance"/> steps away.
    /// Respects map edges and world wrap.</returns>
    // Kotlin: deprecated, forEachTileInDistance is faster. If not viable, then this can still be used.
    public IEnumerable<Tile> GetTilesInDistance(HexCoord origin, int distance) =>
        GetTilesInDistanceRange(origin, new IntRange(0, distance));

    /// <returns>All tiles in a hexagonal ring around <paramref name="origin"/> with the distances in <paramref name="range"/>. Excludes the origin tile unless range starts at 0.
    /// Respects map edges and world wrap.</returns>
    // Kotlin: deprecated, forEachTileInDistanceRange is faster. If not viable, then this can still be used.
    public IEnumerable<Tile> GetTilesInDistanceRange(HexCoord origin, IntRange range)
    {
        for (var i = range.First; i <= range.Last; i++)
        {
            foreach (var tile in GetTilesAtDistance(origin, i)) yield return tile;
        }
    }

    /// <returns>All tiles in a hexagonal ring 1 tile wide around <paramref name="origin"/> with the <paramref name="distance"/>. Contains the origin if and only if distance is &lt;= 0.
    /// Respects map edges and world wrap.</returns>
    // Kotlin: deprecated, forEachTileAtDistance is faster. If not viable, then this can still be used.
    public IEnumerable<Tile> GetTilesAtDistance(HexCoord origin, int distance) =>
        distance <= 0 // silently take negatives.
            ? [this[origin]]
            : GetTilesAtPositiveDistance(origin, distance);

    private IEnumerable<Tile> GetTilesAtPositiveDistance(HexCoord origin, int distance)
    {
        var centerX = origin.x;
        var centerY = origin.y;

        // Start from 6 O'clock point which means (-distance, -distance) away from the center point
        var currentX = centerX - distance;
        var currentY = centerY - distance;

        for (var n = 0; n < distance; n++) // From 6 to 8
        {
            var tile = GetIfTileExistsOrNull(currentX, currentY);
            if (tile != null) yield return tile;
            // We want to get the tile on the other side of the clock,
            // so if we're at current = origin-delta we want to get to origin+delta.
            // The simplest way to do this is 2*origin - current = 2*origin- (origin - delta) = origin+delta
            tile = GetIfTileExistsOrNull(2 * centerX - currentX, 2 * centerY - currentY);
            if (tile != null) yield return tile;
            currentX += 1; // we're going upwards to the left, towards 8 o'clock
        }
        for (var n = 0; n < distance; n++) // 8 to 10
        {
            var tile = GetIfTileExistsOrNull(currentX, currentY);
            if (tile != null) yield return tile;
            tile = GetIfTileExistsOrNull(2 * centerX - currentX, 2 * centerY - currentY);
            if (tile != null) yield return tile;
            currentX += 1;
            currentY += 1; // we're going up the left side of the hexagon so we're going "up" - +1,+1
        }
        for (var n = 0; n < distance; n++) // 10 to 12
        {
            var tile = GetIfTileExistsOrNull(currentX, currentY);
            if (tile != null) yield return tile;
            tile = GetIfTileExistsOrNull(2 * centerX - currentX, 2 * centerY - currentY);
            if (tile != null) yield return tile;
            currentY += 1; // we're going up the top left side of the hexagon so we're heading "up and to the right"
        }
    }

    /// <summary>Applies <paramref name="op"/> to all tiles in a hexagon of radius <paramref name="distance"/>, including the tile at <paramref name="origin"/>.
    /// Respects map edges and world wrap.</summary>
    public void ForEachTileInDistance(HexCoord origin, int distance, Action<Tile> op) =>
        ForEachTileInDistance(origin, distance, _ => true, op);

    public void ForEachTileInDistance(HexCoord origin, int distance, Func<Tile, bool> filter, Action<Tile> op)
    {
        for (var i = 0; i <= distance; i++)
            ForEachTileAtDistance(origin, i, filter, op);
    }

    /// <summary>Applies <paramref name="op"/> to all tiles in a hexagonal ring around <paramref name="origin"/> with the distances in <paramref name="range"/>.
    /// Excludes the origin tile unless range starts at 0. Respects map edges and world wrap.</summary>
    public void ForEachTileInDistanceRange(HexCoord origin, IntRange range, Action<Tile> op) =>
        ForEachTileInDistanceRange(origin, range, _ => true, op);

    public void ForEachTileInDistanceRange(HexCoord origin, IntRange range, Func<Tile, bool> filter, Action<Tile> op)
    {
        for (var i = range.First; i <= range.Last; i++)
            ForEachTileAtDistance(origin, i, filter, op);
    }

    /// <summary>Applies <paramref name="op"/> to all tiles in a hexagonal ring 1 tile wide around <paramref name="origin"/> with the <paramref name="distance"/>.
    /// Contains the origin if and only if distance is &lt;= 0. Respects map edges and world wrap.</summary>
    public void ForEachTileAtDistance(HexCoord origin, int distance, Action<Tile> op) =>
        ForEachTileAtDistance(origin, distance, _ => true, op);

    public void ForEachTileAtDistance(HexCoord origin, int distance, Func<Tile, bool> filter, Action<Tile> op)
    {
        if (distance <= 0)
        {
            var tile = this[origin];
            if (filter(tile)) op(tile);
        }
        else if (distance == 1)
        {
            foreach (var tile in this[origin].Neighbors)
            {
                if (filter(tile))
                    op(tile);
            }
        }
        else
        {
            var centerX = origin.x;
            var centerY = origin.y;

            // Start from 6 O'clock point which means (-distance, -distance) away from the center point
            var currentX = centerX - distance;
            var currentY = centerY - distance;

            Tile? tile;
            for (var n = 0; n < distance; n++) // From 6 to 8
            {
                tile = GetIfTileExistsOrNull(currentX, currentY);
                if (tile != null && filter(tile)) op(tile);
                // We want to get the tile on the other side of the clock,
                // so if we're at current = origin-delta we want to get to origin+delta.
                // The simplest way to do this is 2*origin - current = 2*origin- (origin - delta) = origin+delta
                tile = GetIfTileExistsOrNull(2 * centerX - currentX, 2 * centerY - currentY);
                if (tile != null && filter(tile)) op(tile);
                currentX += 1; // we're going upwards to the left, towards 8 o'clock
            }
            for (var n = 0; n < distance; n++) // 8 to 10
            {
                tile = GetIfTileExistsOrNull(currentX, currentY);
                if (tile != null && filter(tile)) op(tile);
                tile = GetIfTileExistsOrNull(2 * centerX - currentX, 2 * centerY - currentY);
                if (tile != null && filter(tile)) op(tile);
                currentX += 1;
                currentY += 1; // we're going up the left side of the hexagon so we're going "up" - +1,+1
            }
            for (var n = 0; n < distance; n++) // 10 to 12
            {
                tile = GetIfTileExistsOrNull(currentX, currentY);
                if (tile != null && filter(tile)) op(tile);
                tile = GetIfTileExistsOrNull(2 * centerX - currentX, 2 * centerY - currentY);
                if (tile != null && filter(tile)) op(tile);
                currentY += 1; // we're going up the top left side of the hexagon so we're heading "up and to the right"
            }
        }
    }

    /// <returns>all tiles within <paramref name="rectangle"/>, respecting world edges and wrap.
    /// The rectangle will be "straight" ie parallel with rectangular map edges.</returns>
    public IEnumerable<Tile> GetTilesInRectangle(Rectangle rectangle)
    {
        var x = KotlinMath.FloatToInt(rectangle.X);
        var y = KotlinMath.FloatToInt(rectangle.Y);
        var width = KotlinMath.FloatToInt(rectangle.Width);
        var height = KotlinMath.FloatToInt(rectangle.Height);
        // `x until x + width` is evaluated once, with Int wrap-around like Kotlin
        var endColumn = unchecked(x + width);
        var endRow = unchecked(y + height);
        for (var worldColumnNumber = x; worldColumnNumber < endColumn; worldColumnNumber++)
        {
            for (var worldRowNumber = y; worldRowNumber < endRow; worldRowNumber++)
            {
                var hexCoords = HexMath.GetTileCoordsFromColumnRow(worldColumnNumber, worldRowNumber);
                var tile = GetIfTileExistsOrNull(hexCoords.x, hexCoords.y);
                if (tile != null) yield return tile;
            }
        }
    }

    /// <returns>tile at hex coordinates (<paramref name="x"/>,<paramref name="y"/>) or null if they are outside the map. Respects map edges and world wrap.</returns>
    public Tile? GetIfTileExistsOrNull(int x, int y)
    {
        var tile = GetOrNull(x, y);
        if (tile != null) return tile;

        if (!MapParameters.WorldWrap)
            return null;

        var radius = MapParameters.MapSize.Radius;
        if (MapParameters.Shape == MapShape.Rectangular)
            radius = MapParameters.MapSize.Width / 2;

        // Maybe tile is "outside of the map" in world wrap.

        // A. Get tile wrapped around from right to left
        var rightSideTile = GetOrNull(x + radius, y - radius);
        if (rightSideTile != null) return rightSideTile;

        // B. Get tile wrapped around from left to right
        var leftSideTile = GetOrNull(x - radius, y + radius);
        if (leftSideTile != null) return leftSideTile;

        return null;
    }

    /// <summary>
    /// Returns the clock position of <paramref name="otherTile"/> seen from <paramref name="tile"/>'s position
    /// Returns -1 if not neighbors
    /// </summary>
    public int GetNeighborTileClockPosition(Tile tile, Tile otherTile)
    {
        var radius = MapParameters.Shape == MapShape.Rectangular
            ? MapParameters.MapSize.Width / 2
            : MapParameters.MapSize.Radius;
        var x1 = tile.Position.x;
        var y1 = tile.Position.y;
        var x2 = otherTile.Position.x;
        var y2 = otherTile.Position.y;

        var xDifference = x1 - x2;
        var yDifference = y1 - y2;
        var xWrapDifferenceBottom = radius < 3 ? 0 : x1 - (x2 - radius);
        var yWrapDifferenceBottom = radius < 3 ? 0 : y1 - (y2 - radius);
        var xWrapDifferenceTop = radius < 3 ? 0 : x1 - (x2 + radius);
        var yWrapDifferenceTop = radius < 3 ? 0 : y1 - (y2 + radius);

        if (xDifference == 1 && yDifference == 1) return 6; // otherTile is below
        if (xDifference == -1 && yDifference == -1) return 12; // otherTile is above
        if (xDifference == 1 || xWrapDifferenceBottom == 1) return 4; // otherTile is bottom-right
        if (yDifference == 1 || yWrapDifferenceBottom == 1) return 8; // otherTile is bottom-left
        if (xDifference == -1 || xWrapDifferenceTop == -1) return 10; // otherTile is top-left
        if (yDifference == -1 || yWrapDifferenceTop == -1) return 2; // otherTile is top-right
        return -1;
    }

    /// <summary>
    /// Returns the neighbor tile of <paramref name="tile"/> at <paramref name="clockPosition"/>, if it exists.
    /// Takes world wrap into account
    /// Returns null if there is no such neighbor tile or if <paramref name="clockPosition"/> is not a valid clock position
    /// </summary>
    public Tile? GetClockPositionNeighborTile(Tile tile, int clockPosition)
    {
        var difference = HexMath.GetClockPositionToHexcoord(clockPosition);
        if (difference == HexCoord.Zero) return null;
        var possibleNeighborPosition = tile.Position.Plus(difference);
        return GetIfTileExistsOrNull(possibleNeighborPosition.x, possibleNeighborPosition.y);
    }

    /// <summary>Convert relative direction of <paramref name="otherTile"/> seen from <paramref name="tile"/>'s position into a vector
    /// in world coordinates of length sqrt(3), so that it can be used to go from tile center to
    /// the edge of the hex in that direction (meaning the center of the border between the hexes)</summary>
    public Vector2 GetNeighborTilePositionAsWorldCoords(Tile tile, Tile otherTile) =>
        HexMath.GetClockPositionToWorldVector(GetNeighborTileClockPosition(tile, otherTile));

    /// <summary>
    /// Returns the closest position to (0, 0) outside the map which can be wrapped
    /// to the position of the given vector
    /// </summary>
    public HexCoord GetUnwrappedPosition(HexCoord position)
    {
        if (!Contains(position))
            return position; // The position is outside the map so its unwrapped already

        var radius = MapParameters.Shape == MapShape.Rectangular
            ? MapParameters.MapSize.Width / 2
            : MapParameters.MapSize.Radius;

        var vectorUnwrappedLeft = HexCoord.Of(position.x + radius, position.y - radius);
        var vectorUnwrappedRight = HexCoord.Of(position.x - radius, position.y + radius);

        static int SquareSum(HexCoord hexCoord)
        {
            var x = hexCoord.x;
            var y = hexCoord.y;
            return x * x + y * y;
        }

        return SquareSum(vectorUnwrappedRight) < SquareSum(vectorUnwrappedLeft)
            ? vectorUnwrappedRight
            : vectorUnwrappedLeft;
    }

    public sealed record ViewableTile(Tile Tile, int MaxHeightSeenToTile, bool IsVisible, bool IsAttackable);

    /// <returns>List of tiles visible from location <paramref name="position"/> for a unit with sight range <paramref name="sightDistance"/></returns>
    public List<Tile> GetViewableTiles(HexCoord position, int sightDistance, bool forAttack = false)
    {
        var aUnitHeight = this[position].UnitHeight;
        var viewableTiles = new List<ViewableTile>
        {
            new(this[position], aUnitHeight, IsVisible: true, IsAttackable: false),
        };
        // T262: the lowest MaxHeightSeenToTile per tile of the layers done so far. The "tiles of viewableTiles one step from cTile" of the
        // original are exactly the neighbours of cTile that are in there, so a lookup per neighbour replaces a scan of every tile seen so far.
        var seenHeight = new Dictionary<Tile, int> { [viewableTiles[0].Tile] = aUnitHeight };
        var tilesToAddInDistanceI = new List<ViewableTile>();

        for (var i = 1; i <= sightDistance + 1; i++) // in each layer,
        {
            // This is so we don't use tiles in the same distance to "see over",
            // that is to say, the "viewableTiles.contains(it) check will return false for neighbors from the same distance
            tilesToAddInDistanceI.Clear();
            var layer = i;

            ForEachTileAtDistance(position, i, cTile => // for each tile in that layer,
            {
                var cTileHeight = cTile.TileHeight;

                // For the sightdistance+1 layer - that's "one out of sight" - it's only visible if it's higher than the current tile
                if (layer == sightDistance + 1 && (cTileHeight <= aUnitHeight || forAttack))
                    return;

                /*
            Okay so, if we're looking at a tile from height a to one with height c with a MAXIMUM HEIGHT of b in the middle,
            we have several scenarios:
            1. a>=b - I can see everything, b does not hide c (equals is 'flat plain' or 'string of hills' or 'hill viewing over forests')
            3. a<b
                3.1 b>=c - b hides c (hills hide other hills, forests, etc)
                3.2 b<c - c is tall enough I can see it over b (hill+forest, mountain)

            This can all be summed up as "I can see c if a=>b || c>b"
            */
                var bMinimumHighestSeenTerrainSoFar = int.MaxValue;
                foreach (var neighbor in cTile.Neighbors)
                    if (seenHeight.TryGetValue(neighbor, out var height) && height < bMinimumHighestSeenTerrainSoFar)
                        bMinimumHighestSeenTerrainSoFar = height;
                if (bMinimumHighestSeenTerrainSoFar == int.MaxValue) throw new InvalidOperationException("Sequence contains no elements");

                tilesToAddInDistanceI.Add(new ViewableTile(
                    cTile,
                    Math.Max(cTileHeight, bMinimumHighestSeenTerrainSoFar),
                    aUnitHeight >= bMinimumHighestSeenTerrainSoFar || cTileHeight > bMinimumHighestSeenTerrainSoFar,
                    aUnitHeight >= bMinimumHighestSeenTerrainSoFar || cTile.UnitHeight > bMinimumHighestSeenTerrainSoFar));
            });
            viewableTiles.AddRange(tilesToAddInDistanceI);
            foreach (var added in tilesToAddInDistanceI)
                seenHeight[added.Tile] = seenHeight.TryGetValue(added.Tile, out var known) ? Math.Min(known, added.MaxHeightSeenToTile) : added.MaxHeightSeenToTile;
        }

        if (forAttack) return viewableTiles.Where(it => it.IsAttackable).Select(it => it.Tile).ToList();

        return viewableTiles.Where(it => it.IsVisible).Select(it => it.Tile).ToList();
    }

    /// <summary>Build a list of incompatibilities of a map with a ruleset for the new game loader
    ///
    /// Is run before setTransients, so make do without startingLocationsByNation</summary>
    public JavaHashSet<string> GetRulesetIncompatibility(Ruleset ruleset)
    {
        var rulesetIncompatibilities = new JavaHashSet<string>();
        foreach (var set in Values.Select(it => it.GetRulesetIncompatibility(ruleset)).ToList())
        {
            foreach (var item in set) rulesetIncompatibilities.Add(item);
        }

        // All the rest is to find missing nations
        try
        { // This can fail if the map contains a resource that isn't in the ruleset, in Tile.tileResource
            SetTransients(ruleset);
        }
#pragma warning disable CA1031 // JUSTIFY: T039 Kotlin catches every Exception here; any failure of setTransients means "stop checking"
        catch (Exception)
#pragma warning restore CA1031
        {
            return rulesetIncompatibilities;
        }
        SetStartingLocationsTransients();
        foreach (var (_, nationName, _) in StartingLocations)
        {
            if (!ruleset.Nations.ContainsKey(nationName))
                rulesetIncompatibilities.Add($"Nation [{nationName}] does not exist in ruleset!");
        }
        rulesetIncompatibilities.Remove("");
        return rulesetIncompatibilities;
    }

    public bool IsWaterMap()
    {
        AssignContinents(AssignContinentsMode.Ensure);
        var bigIslands = ContinentSizes.Count(it => it.Value > 20);
        var players = GameInfo.GameParameters.Players.Count;
        return bigIslands >= players;
    }

    public bool UsingArchipelagoRegions()
    {
        var totalLand = (float)ContinentSizes.Values.Aggregate(0, (a, b) => unchecked(a + b));
        var largestContinent = (float)ContinentSizes.Values.Max();
        return largestContinent / totalLand < 0.25f;
    }

    #endregion
    #region State-Changing Methods

    /// <summary>Initialize transients - without, most operations, like <c>this[...]</c> from coordinates, will fail.</summary>
    /// <param name="ruleset">Required unless this is a clone of an initialized TileMap including one</param>
    /// <param name="setUnitCivTransients">when false Civ-specific parts of unit initialization are skipped, for the map editor.</param>
    public void SetTransients(Ruleset? ruleset = null, bool setUnitCivTransients = true)
    {
        if (ruleset != null) Ruleset = ruleset;
        if (Ruleset == null) throw new InvalidOperationException("TileMap.setTransients called without ruleset");
        if (TileList.Count == 0) throw new InvalidOperationException("No tiles were found in the save?!");

        if (tileMatrix.Count == 0)
        {
            var topY = TileList.Max(it => it.Position.y);
            BottomY = TileList.Min(it => it.Position.y);
            var rightX = TileList.Max(it => it.Position.x);
            LeftX = TileList.Min(it => it.Position.x);

            // Initialize arrays with enough capacity to avoid re-allocations (+Arrays.copyOf).
            // We have just calculated the dimensions above, so we know the final size.
            tileMatrix.EnsureCapacity(rightX - LeftX + 1);
            for (var n = 0; n < rightX - LeftX + 1; n++)
            {
                var row = new List<Tile?>(topY - BottomY + 1);
                for (var m = 0; m < topY - BottomY + 1; m++) row.Add(null);
                tileMatrix.Add(row);
            }
        }
        else
        {
            // Yes the map generator calls this repeatedly, and we don't want to end up with an oversized tileMatrix
            // rightX is between -leftX - 1 (e.g. 105x90 map thanks @ravignir) and -leftX + 2
            if (tileMatrix.Count < -2 * LeftX || tileMatrix.Count > 3 - 2 * LeftX)
                throw new InvalidOperationException("TileMap.setTransients called on existing tileMatrix of different size");
        }

        foreach (var tileInfo in Values)
        {
            tileMatrix[tileInfo.Position.x - LeftX][tileInfo.Position.y - BottomY] = tileInfo;
        }
        var index = 0;
        foreach (var tileInfo in Values)
        {
            tileInfo.TileMap = this;
            tileInfo.ZeroBasedIndex = index++;
            tileInfo.Ruleset = Ruleset;
        }
        foreach (var tileInfo in Values)
        {
            // Do ***NOT*** call Tile.setTerrainTransients before the tileMatrix is complete -
            // setting transients might trigger the neighbors lazy (e.g. thanks to convertHillToTerrainFeature).
            // When that lazy runs, some directions might be omitted because getIfTileExistsOrNull
            // looks at tileMatrix. Thus filling Tiles into tileMatrix and setting their
            // transients in the same loop will leave incomplete cached `neighbors`.
            // Similarly, ruleset must be set on ALL tiles before setTerrainTransients runs on ANY tile,
            // since transient setup can evaluate unique conditionals (e.g. isAdjacentTo) that read
            // ruleset on neighbor tiles not yet reached in iteration order.
            tileInfo.SetTerrainTransients();
            tileInfo.SetUnitTransients(setUnitCivTransients);
        }

        var minColumn = TileList.Min(it => HexMath.GetColumn(it.Position));
        var maxColumn = TileList.Max(it => HexMath.GetColumn(it.Position));
        Width = maxColumn - minColumn + 1;
    }

    /// <summary>Initialize Civilization.neutralRoads based on Tile.roadOwner
    ///  - which Civ owns roads on which neutral tiles</summary>
    public void SetNeutralTransients()
    {
        foreach (var tileInfo in Values)
        {
            tileInfo.SetOwnerTransients();
        }
    }

    public void RemoveMissingTerrainModReferences(Ruleset ruleSet)
    {
        // This will run before setTransients, so do not rely e.g. on Tile.ruleset being available.
        // That rules out Tile.removeTerrainFeature, which refreshes object/unique caches
        foreach (var tile in Values)
        {
            tile.RemoveMissingTerrainModReferences(ruleSet);
        }
        foreach (var startingLocation in StartingLocations.ToList())
        {
            if (!ruleSet.Nations.ContainsKey(startingLocation.Nation))
                StartingLocations.Remove(startingLocation);
        }
    }

    /// <summary>Tries to place the unit named <paramref name="unitName"/> into the <see cref="Tile"/> closest to the given <paramref name="position"/></summary>
    /// <param name="position">where to try to place the unit (or close - max 10 tiles distance)</param>
    /// <param name="unitName">name of the BaseUnit to create and place</param>
    /// <param name="civInfo">civilization to assign unit to</param>
    /// <param name="unitId"></param>
    /// <returns>created MapUnit or null if no suitable location was found</returns>
    public IGameContextUnit? PlaceUnitNearTile(HexCoord position, string unitName, IGameContextCivilization civInfo, int? unitId = null)
    {
        var unit = GameInfo.GetBaseUnit(unitName);
        return PlaceUnitNearTile(position, unit, civInfo, unitId);
    }

    /// <summary>Tries to place the <paramref name="baseUnit"/> into the <see cref="Tile"/> closest to the given <paramref name="position"/></summary>
    /// <param name="position">where to try to place the unit (or close - max 10 tiles distance)</param>
    /// <param name="baseUnit">BaseUnit to create and place</param>
    /// <param name="civInfo">civilization to assign unit to</param>
    /// <param name="unitId"></param>
    /// <returns>created MapUnit or null if no suitable location was found</returns>
    public IGameContextUnit? PlaceUnitNearTile(HexCoord position, BaseUnit baseUnit, IGameContextCivilization civInfo, int? unitId = null)
    {
        var unit = civInfo.NewMapUnit(baseUnit, unitId);

        List<Tile> GetPassableNeighbours(Tile tile) =>
            tile.Neighbors.Where(unit.CanPassThrough).ToList();

        // both the civ name and actual civ need to be in here in order to calculate the canMoveTo...Darn
        unit.AssignOwner(civInfo, updateCivInfo: false);
        // remember our first owner
        unit.SetOriginalOwner(civInfo.CivId);

        Tile? unitToPlaceTile = null;
        // try to place at the original point (this is the most probable scenario)
        var currentTile = this[position];
        unit.SetCurrentTile(currentTile); // temporary
        unit.SetCacheState(GameContext.ForUnit(unit));
        if (unit.CanMoveTo(currentTile)) unitToPlaceTile = currentTile;

        // if it's not suitable, try to find another tile nearby
        if (unitToPlaceTile == null)
        {
            var isRestrictedLandUnit = unit.BaseUnit.IsLandUnit && !unit.CanMoveOnWater;
            var tryCount = 0;
            var checkedCandidates = new LinkedHashSet<Tile>();
            var potentialCandidates = new TileCandidates(isRestrictedLandUnit, GetPassableNeighbours(currentTile));
            while (unitToPlaceTile == null && tryCount++ < 10)
            {
                unitToPlaceTile = potentialCandidates.Primary.FirstOrDefault(it => unit.CanMoveTo(it)) // Land units should prefer to go into land tiles
                    ?? potentialCandidates.Secondary.FirstOrDefault(it => unit.CanMoveTo(it));
                if (unitToPlaceTile != null) continue;
                // if it's not found yet, let's check their neighbours
                var allTiles = potentialCandidates.AllTiles.ToList();
                checkedCandidates.AddAll(allTiles);
                var newCandidates = new LinkedHashSet<Tile>(
                    allTiles.SelectMany(it => GetPassableNeighbours(it).Where(n => !checkedCandidates.Contains(n))));
                potentialCandidates = new TileCandidates(isRestrictedLandUnit, newCandidates);
            }
        }

        if (unitToPlaceTile == null)
        {
            civInfo.RemoveUnit(unit, updateCivInfo: false); // since we added it to the civ units in the previous assignOwner
            return null; // we didn't actually create a unit...
        }

        // only once we know the unit can be placed do we add it to the civ's unit list
        unit.PutInTile(unitToPlaceTile);
        unit.SetCurrentMovement((float)unit.GetMaxMovement());
        unit.AddMovementMemory();
        GameEventHooks.UnitCreated(this, civInfo, unit, unitToPlaceTile);

        // Only once we add the unit to the civ we can activate addPromotion, because it will try to update civ viewable tiles
        foreach (var promotion in unit.BaseUnit.Promotions)
            unit.AddPromotion(promotion, true);

        civInfo.ForEachMatchingUnique(UniqueType.UnitsGainPromotion, unique =>
        {
            if (unit.MatchesFilter(unique.Params[0]))
                unit.AddPromotion(unique.Params[1], true);
        });

        // And update civ stats, since the new unit changes both unit upkeep and resource consumption
        civInfo.UpdateStatsForNextTurn();

        if (unit.HasResourceRequirementsPerTurn())
            civInfo.UpdateCivResources();

        return unit;
    }

    private sealed class TileCandidates
    {
        public List<Tile> Primary { get; } = [];
        public List<Tile> Secondary { get; } = [];

        public TileCandidates(bool isRestrictedLandUnit, IEnumerable<Tile> tiles)
        {
            if (!isRestrictedLandUnit)
            {
                Primary.AddRange(tiles);
            }
            else
            {
                foreach (var tile in tiles)
                {
                    if (tile.IsLand) Primary.Add(tile);
                    else Secondary.Add(tile);
                }
            }
        }

        public IEnumerable<Tile> AllTiles => Primary.Concat(Secondary);
    }

    /// <summary>Strips all units and starting locations from <see cref="TileMap"/> for specified <see cref="Player"/>
    /// Operation in place
    ///
    /// Currently unreachable code</summary>
    /// <param name="player">units of this player will be removed</param>
    public void StripPlayer(Player player)
    {
        TileList.ForEach(it =>
        {
            foreach (var unit in it.GetUnits())
            {
                if (unit.Owner == player.ChosenCiv) unit.RemoveFromTile();
            }
        });
        StartingLocations.RemoveAll(it => it.Nation == player.ChosenCiv);
        StartingLocationsByNation.Remove(player.ChosenCiv);
    }

    // switchPlayersNation(player, newNation) is deliberately not ported: dead code upstream (see the file header).

    /// <summary>Initialize startingLocations transients</summary>
    public void SetStartingLocationsTransients()
    {
        StartingLocationsByNation.Clear();
        foreach (var (position, nationName, _) in StartingLocations)
        {
            StartingLocationsByNation.AddToMapOfSets(nationName, this[position]);
        }
    }

    /// <summary>Adds a starting position, maintaining the transients
    ///
    /// Note: Will not replace an existing StartingLocation to update its usage</summary>
    /// <returns>true if the starting position was not already stored as per Collection's add</returns>
    public bool AddStartingLocation(string nationName, Tile tile, StartingLocation.UsageType usage = StartingLocation.UsageType.Player)
    {
        if (StartingLocationsByNation.Contains(nationName, tile)) return false;
        StartingLocations.Add(new StartingLocation(tile.Position, nationName, usage));
        return StartingLocationsByNation.AddToMapOfSets(nationName, tile);
    }

    /// <summary>Removes a starting position, maintaining the transients</summary>
    /// <returns>true if the starting position was removed as per Collection's remove</returns>
    public bool RemoveStartingLocation(string nationName, Tile tile)
    {
        if (!StartingLocationsByNation.Contains(nationName, tile)) return false;
        StartingLocations.RemoveAll(it => it.Position == tile.Position && it.Nation == nationName);
        return StartingLocationsByNation[nationName].Remove(tile);
        // we do not clean up an empty startingLocationsByNation[nationName] set - not worth it
    }

    /// <summary>Removes all starting positions for <paramref name="nationName"/>, maintaining the transients</summary>
    public void RemoveStartingLocations(string nationName)
    {
        if (!StartingLocationsByNation.TryGetValue(nationName, out var tiles)) return;
        StartingLocations.RemoveAll(it => it.Nation == nationName);
        tiles.Clear();
    }

    /// <summary>Removes all starting positions for <paramref name="position"/>, rebuilding the transients</summary>
    public void RemoveStartingLocations(HexCoord position)
    {
        StartingLocations.RemoveAll(it => it.Position == position);
        SetStartingLocationsTransients();
    }

    /// <summary>Clears starting positions, e.g. after GameStarter is done with them. Does not clear the pseudo-improvements.</summary>
    public void ClearStartingLocations()
    {
        StartingLocations.Clear();
        StartingLocationsByNation.Clear();
    }

    /// <summary>Behaviour of <see cref="AssignContinents"/></summary>
    public enum AssignContinentsMode { Assign, Reassign, Ensure, Clear }

    /// <summary>Set a continent id for each tile, so we can quickly see which tiles are connected.
    ///  Can also be called on saved maps.</summary>
    /// <param name="mode">As follows:
    ///  Assign = initial assign, throw if tiles have continents.
    ///  Reassign = clear continent data and redo for map editor.
    ///  Ensure = regenerate continent sizes from tile data, and if that is empty, Assign.
    ///  Clear = remove all continent data.</param>
    /// <exception cref="InvalidOperationException">when <c>mode==Assign</c> and any land tile already has a continent ID</exception>
    public void AssignContinents(AssignContinentsMode mode)
    {
        if (mode == AssignContinentsMode.Clear)
        {
            foreach (var tile in Values) tile.ClearContinent();
            ContinentSizes.Clear();
            ContinentsSortedBySize.Clear();
            return;
        }

        if (mode == AssignContinentsMode.Ensure)
        {
            if (!ContinentSizes.IsEmpty) return;
            foreach (var tile in Values)
            {
                var continent = tile.GetContinent();
                if (continent == -1) continue;
                ContinentSizes[continent] = 1 + (ContinentSizes.TryGetValue(continent, out var size) ? size : 0);
            }
            if (!ContinentSizes.IsEmpty)
            {
                UpdateContinentsSortedBySize();
                return;
            }
        }

        var landTiles = Values.Where(it => it.IsLand && !it.IsImpassible()).ToList();
        var currentContinent = 0;
        ContinentSizes.Clear();

        if (mode == AssignContinentsMode.Reassign)
        {
            foreach (var tile in Values) tile.ClearContinent();
        }

        while (landTiles.Count > 0)
        {
            var continent = GetConnectedLandTiles(landTiles[landTiles.Count / 2]);
            foreach (var tile in continent) tile.SetContinent(currentContinent);
            ContinentSizes[currentContinent] = continent.Count;

            currentContinent++;
            landTiles = landTiles.Where(it => !continent.Contains(it)).ToList();
        }
        UpdateContinentsSortedBySize();
    }

    /// <summary>Stands in for <c>BFS(start) { it.isLand &amp;&amp; !it.isImpassible() }.stepToEnd().getReachedTiles()</c> (T040).</summary>
    private static HashSet<Tile> GetConnectedLandTiles(Tile start)
    {
        var reached = new HashSet<Tile> { start };
        var toCheck = new Queue<Tile>();
        toCheck.Enqueue(start);
        while (toCheck.Count > 0)
        {
            var current = toCheck.Dequeue();
            foreach (var neighbor in current.Neighbors)
            {
                if (reached.Contains(neighbor) || !(neighbor.IsLand && !neighbor.IsImpassible())) continue;
                reached.Add(neighbor);
                toCheck.Enqueue(neighbor);
            }
        }
        return reached;
    }

    private void UpdateContinentsSortedBySize()
    {
        ContinentsSortedBySize.Clear();
        // sortedByDescending is stable, like OrderByDescending
        ContinentsSortedBySize.AddRange(ContinentSizes.OrderByDescending(it => it.Value).Select(it => it.Key));
    }

    #endregion

    /// <summary>Class to parse only the parameters and starting locations out of a map file</summary>
    [JsonConverter(typeof(PreviewJsonConverter))]
    public class Preview
    {
        public MapParameters MapParameters { get; internal set; } = new();
        internal readonly List<StartingLocation> startingLocations = [];

        public IEnumerable<string> GetDeclaredNations() => startingLocations
            .Where(it => it.Usage != StartingLocation.UsageType.Normal)
            .Select(it => it.Nation)
            .Distinct();

        public IEnumerable<string> GetNationsForHumanPlayer() => startingLocations
            .Where(it => it.Usage == StartingLocation.UsageType.Human)
            .Select(it => it.Nation)
            .Distinct();
    }

    #region Json

    /// <summary>Kotlin <c>StartingLocation</c> as the libGDX Json writes it: fields equal to their defaults are omitted.</summary>
    internal sealed class StartingLocationJsonConverter : JsonConverter<StartingLocation>
    {
        public override StartingLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for StartingLocation, got {reader.TokenType}");
            var position = default(HexCoord);
            var nation = "";
            var usage = StartingLocation.DefaultUsage;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "position":
                        position = new HexCoordConverter().Read(ref reader, typeof(HexCoord), options);
                        break;
                    case "nation":
                        nation = reader.GetString() ?? "";
                        break;
                    case "usage":
                        var text = reader.GetString();
                        if (text == null || !Enum.TryParse(text, true, out usage) || !Enum.IsDefined(usage))
                            throw new JsonException($"Unknown StartingLocation usage '{text}'");
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return new StartingLocation(position, nation, usage);
        }

        public override void Write(Utf8JsonWriter writer, StartingLocation value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value.Position != default)
            {
                writer.WritePropertyName("position");
                new HexCoordConverter().Write(writer, value.Position, options);
            }
            if (value.Nation != "") writer.WriteString("nation", value.Nation);
            if (value.Usage != StartingLocation.DefaultUsage) writer.WriteString("usage", value.Usage.ToString());
            writer.WriteEndObject();
        }
    }

    /// <summary>The serialized fields of Kotlin <c>TileMap</c>: mapParameters, tileList, startingLocations, description (omitted when empty).</summary>
    internal sealed class TileMapJsonConverter : JsonConverter<TileMap>
    {
        public override TileMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for TileMap, got {reader.TokenType}");
            var map = new TileMap();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                var isNull = reader.TokenType == JsonTokenType.Null;
                switch (name)
                {
                    case "mapParameters":
                        if (!isNull) map.MapParameters = JsonSerializer.Deserialize<MapParameters>(ref reader, options).NotNull();
                        break;
                    case "tileList":
                        map.TileList = isNull ? [] : JsonSerializer.Deserialize<List<Tile>>(ref reader, options).NotNull();
                        break;
                    case "startingLocations":
                        map.StartingLocations.Clear();
                        if (!isNull) map.StartingLocations.AddRange(JsonSerializer.Deserialize<List<StartingLocation>>(ref reader, options).NotNull());
                        break;
                    case "description":
                        map.Description = reader.GetString() ?? "";
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return map;
        }

        public override void Write(Utf8JsonWriter writer, TileMap value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("mapParameters");
            JsonSerializer.Serialize(writer, value.MapParameters, options);
            if (value.TileList.Count > 0)
            {
                writer.WritePropertyName("tileList");
                JsonSerializer.Serialize(writer, value.TileList, options);
            }
            if (value.StartingLocations.Count > 0)
            {
                writer.WritePropertyName("startingLocations");
                JsonSerializer.Serialize(writer, value.StartingLocations, options);
            }
            if (value.Description != "") writer.WriteString("description", value.Description);
            writer.WriteEndObject();
        }
    }

    /// <summary>Reads only mapParameters and startingLocations and skips everything else (tileList in particular).</summary>
    internal sealed class PreviewJsonConverter : JsonConverter<Preview>
    {
        public override Preview Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for TileMap.Preview, got {reader.TokenType}");
            var preview = new Preview();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                var isNull = reader.TokenType == JsonTokenType.Null;
                switch (name)
                {
                    case "mapParameters":
                        if (!isNull) preview.MapParameters = JsonSerializer.Deserialize<MapParameters>(ref reader, options).NotNull();
                        break;
                    case "startingLocations":
                        preview.startingLocations.Clear();
                        if (!isNull) preview.startingLocations.AddRange(JsonSerializer.Deserialize<List<StartingLocation>>(ref reader, options).NotNull());
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }
            return preview;
        }

        public override void Write(Utf8JsonWriter writer, Preview value, JsonSerializerOptions options) =>
            // JUSTIFY: T118 Preview is only ever read (permanent)
            throw new NotSupportedException("TileMap.Preview is only ever read");
    }

    #endregion
}
