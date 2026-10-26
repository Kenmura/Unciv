namespace Unciv.Assets;

/// <summary>Linear float colour used by the blend maths (the client turns it into a Godot colour). Components are not clamped.</summary>
public readonly record struct Tint(float R, float G, float B)
{
    public static Tint Of(Rgb colour) => new(colour.R / 255f, colour.G / 255f, colour.B / 255f);

    public static Tint operator *(Tint a, float k) => new(a.R * k, a.G * k, a.B * k);

    public static Tint operator *(Tint a, Tint b) => new(a.R * b.R, a.G * b.G, a.B * b.B);

    public static Tint operator +(Tint a, Tint b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    /// <summary>Linear interpolation from this colour toward <paramref name="other"/> by <paramref name="t"/> (0 gives this, 1 gives other).</summary>
    public Tint Lerp(Tint other, float t) => this * (1f - t) + other * t;

    /// <summary>The sRGB-encoded colour as linear light (the standard transfer function), which is what a vertex colour is read as without the sRGB flag.</summary>
    public Tint SrgbToLinear() => new(Channel(R), Channel(G), Channel(B));

    private static float Channel(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}

/// <summary>
/// C031 terrain blending: each tile's top-face vertices near an edge are tinted toward the neighbour across that edge, so adjacent terrains melt into each
/// other instead of meeting at a hard seam. The rule is symmetric: both tiles that share a point compute the same colour there (the mean of the tiles
/// meeting at it), so the seam is invisible. A neighbour that is not drawn (unexplored or off the map) fades the edge to <see cref="MissingShare"/> of the
/// tile's own colour, which gives explored land a soft fog edge against the black backdrop. Pure functions, no engine types.
/// </summary>
public static class TerrainBlend
{
    /// <summary>Outward unit normals (x, z) of the six hexagon edges, at 30, 90, ... degrees (the hex has corners at 0, 60, ...).</summary>
    public static readonly (float X, float Z)[] EdgeNormals =
    [
        (0.8660254f, 0.5f), (0f, 1f), (-0.8660254f, 0.5f), (-0.8660254f, -0.5f), (0f, -1f), (0.8660254f, -0.5f),
    ];

    /// <summary>Distance from the tile centre to an edge midpoint for a tile of radius 1.</summary>
    public const float Apothem = 0.8660254f;

    /// <summary>Where the blend starts, as the fraction of the apothem along an edge normal (an edge midpoint is 1; the next edge's midpoint is only 0.5).</summary>
    public const float StartFraction = 0.6f;

    /// <summary>Own colour multiplier at the fully faded rim next to a tile that is not drawn.</summary>
    public const float MissingShare = 0.55f;

    /// <summary>Distance in world units between the centres of adjacent tiles of radius 1.</summary>
    public const float NeighbourDistance = 1.7320508f;

    /// <summary>
    /// How strongly the point (<paramref name="x"/>, <paramref name="z"/>) (tile-local, radius 1) belongs to the edge <paramref name="edge"/> (0..5):
    /// 0 up to <see cref="StartFraction"/> of the apothem, a smooth rise to 1 at the edge itself.
    /// </summary>
    public static float EdgeWeight(float x, float z, int edge)
    {
        var (nx, nz) = EdgeNormals[edge];
        var along = (x * nx + z * nz) / Apothem;
        var t = Math.Clamp((along - StartFraction) / (1f - StartFraction), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The colour of the point (<paramref name="x"/>, <paramref name="z"/>) of a tile of colour <paramref name="own"/> whose neighbour across edge i has colour
    /// <paramref name="neighbours"/>[i] (null: not drawn). With weights r_i the result is (own + sum r_i * n_i) / (1 + sum r_i): an edge midpoint is the mean of
    /// the two tiles, a corner the mean of the three, the centre and everything inside <see cref="StartFraction"/> stays <paramref name="own"/>.
    /// </summary>
    public static Tint Mix(Tint own, IReadOnlyList<Tint?> neighbours, float x, float z)
    {
        if (neighbours.Count != EdgeNormals.Length) throw new ArgumentException("exactly six neighbour slots are required", nameof(neighbours));
        var sum = own;
        var total = 1f;
        for (var edge = 0; edge < EdgeNormals.Length; edge++)
        {
            var w = EdgeWeight(x, z, edge);
            if (w <= 0f) continue;
            sum += (neighbours[edge] ?? own * MissingShare) * w;
            total += w;
        }
        return sum * (1f / total);
    }
}

/// <summary>Integer cell of the chunk grid a tile belongs to.</summary>
public readonly record struct ChunkId(int X, int Z);

/// <summary>Axis-aligned rectangle on the ground plane (world x, z).</summary>
public readonly record struct GroundRect(float MinX, float MinZ, float MaxX, float MaxZ)
{
    public bool Intersects(GroundRect other) => MinX <= other.MaxX && MaxX >= other.MinX && MinZ <= other.MaxZ && MaxZ >= other.MinZ;

    public GroundRect Expand(float margin) => new(MinX - margin, MinZ - margin, MaxX + margin, MaxZ + margin);

    /// <summary>The smallest rectangle holding all <paramref name="points"/> (x, z); an empty list gives an empty rectangle at the origin.</summary>
    public static GroundRect Around(IReadOnlyList<(float X, float Z)> points)
    {
        if (points.Count == 0) return new GroundRect(0f, 0f, 0f, 0f);
        return new GroundRect(points.Min(it => it.X), points.Min(it => it.Z), points.Max(it => it.X), points.Max(it => it.Z));
    }
}

/// <summary>
/// C031 chunking: tiles are grouped by the square cell of the ground plane their centre falls into, one merged mesh per cell. A chunk is drawn only when its
/// (tile-radius padded) bounds meet the camera's ground footprint; the footprint is padded by a whole chunk so relief and the camera's tilt never clip an edge.
/// </summary>
public static class TerrainChunks
{
    /// <summary>Side of a chunk cell in world units (about 4 by 4 tiles).</summary>
    public const float CellSize = 7f;

    /// <summary>Tile radius: a chunk's bounds reach this far beyond its outermost tile centres.</summary>
    public const float TileRadius = 1f;

    public static ChunkId Of(float worldX, float worldZ) => new((int)MathF.Floor(worldX / CellSize), (int)MathF.Floor(worldZ / CellSize));

    /// <summary>Groups tile centres by chunk; chunks are returned in ascending (x, z) order and tiles keep their input order, so the result is deterministic.</summary>
    public static IReadOnlyList<(ChunkId Id, IReadOnlyList<int> Tiles)> Group(IReadOnlyList<(float X, float Z)> centres)
    {
        var map = new SortedDictionary<(int, int), List<int>>();
        for (var i = 0; i < centres.Count; i++)
        {
            var id = Of(centres[i].X, centres[i].Z);
            if (!map.TryGetValue((id.X, id.Z), out var list)) map[(id.X, id.Z)] = list = [];
            list.Add(i);
        }
        return [.. map.Select(it => (new ChunkId(it.Key.Item1, it.Key.Item2), (IReadOnlyList<int>)it.Value))];
    }

    /// <summary>Ground bounds of a chunk holding tiles at <paramref name="centres"/>.</summary>
    public static GroundRect BoundsOf(IEnumerable<(float X, float Z)> centres)
    {
        var list = centres.ToList();
        var rect = GroundRect.Around(list);
        return rect.Expand(TileRadius);
    }

    /// <summary>Whether a chunk with <paramref name="bounds"/> may be seen from a camera whose ground footprint is <paramref name="footprint"/>.</summary>
    public static bool IsVisible(GroundRect bounds, GroundRect footprint) => bounds.Intersects(footprint.Expand(CellSize));
}

/// <summary>Frame-time budget check of the terrain scenario.</summary>
public static class FrameBudget
{
    /// <summary>Mean of the frame durations in milliseconds; an empty list is not a measurement and throws.</summary>
    public static double MeanMs(IReadOnlyList<double> frameMs) =>
        frameMs.Count == 0 ? throw new ArgumentException("no frames measured", nameof(frameMs)) : frameMs.Sum() / frameMs.Count;

    /// <summary>True when the mean frame time is at most <paramref name="budgetMs"/>.</summary>
    public static bool WithinBudget(IReadOnlyList<double> frameMs, double budgetMs) => MeanMs(frameMs) <= budgetMs;
}
