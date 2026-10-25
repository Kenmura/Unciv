namespace Unciv.Assets;

/// <summary>Silhouette family of a terrain tile (C002). Chosen from the terrain's name only; unknown (mod) names are <see cref="Flat"/>.</summary>
public enum ReliefKind
{
    Flat,
    Hill,
    Mountain,
    Ocean,
    Coast,
    Lake,
}

/// <summary>
/// C002 displaced terrain meshes: a hexagonal lattice top face whose interior vertices are lifted (hills: a dome, mountains: a noisy peak) or, for water,
/// kept level and shaded darker toward the middle (depth) and lighter at the rim (shore). Rim vertices never get noise, so neighbouring tiles of the same
/// level meet without cracks. Pure functions of the key (a stable hash seeds the noise), no engine types.
/// </summary>
public static class TerrainRelief
{
    /// <summary>Lattice divisions per hex side; 4 gives 61 top vertices.</summary>
    public const int Divisions = 4;

    /// <summary>Height of the flat land surface and of every raised tile's rim.</summary>
    public const float LandHeight = 0.2f;

    public const string HillName = "Hill";
    public const string MountainName = "Mountain";

    public static ReliefKind KindOf(string terrainName) => terrainName switch
    {
        HillName => ReliefKind.Hill,
        MountainName => ReliefKind.Mountain,
        "Ocean" => ReliefKind.Ocean,
        "Coast" => ReliefKind.Coast,
        "Lakes" => ReliefKind.Lake,
        _ => ReliefKind.Flat,
    };

    /// <summary>Height of the tile's highest point (its centre); picking and unit placement use this.</summary>
    public static float TopHeight(ReliefKind kind) => kind switch
    {
        ReliefKind.Hill => 0.6f,
        ReliefKind.Mountain => 1.2f,
        ReliefKind.Ocean => 0.08f,
        ReliefKind.Coast or ReliefKind.Lake => 0.13f,
        _ => LandHeight,
    };

    /// <summary>Top height as a multiple of the flat land height (the client's <c>TileNode.Elevation</c>).</summary>
    public static float Elevation(ReliefKind kind) => TopHeight(kind) / LandHeight;

    /// <summary>Key of the mesh for a terrain name, so a file override can replace one terrain's silhouette.</summary>
    public static AssetKey MeshKey(string terrainName) => AssetKey.Of("mesh", "terrain-" + terrainName);

    /// <summary>The terrain name inside a <see cref="MeshKey"/>, or null when the key is not a terrain mesh key.</summary>
    public static string? TerrainOf(AssetKey key) =>
        key.Category == "mesh" && key.Name.StartsWith("terrain-", StringComparison.Ordinal) ? key.Name["terrain-".Length..] : null;

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>Signed noise in [-1, 1] for a lattice vertex of the mesh seeded by <paramref name="seed"/>.</summary>
    private static float Noise(uint seed, int a, int b) => ((int)(StableHash.Mix(seed, a, b) % 2001u) - 1000) / 1000f;

    /// <summary>Height and shade (vertex colour multiplier) of a lattice vertex; <paramref name="t"/> is 0 at the centre and 1 on the rim.</summary>
    private static (float Height, float Shade) Sample(ReliefKind kind, float t, float noise)
    {
        var rim = RimHeight(kind);
        var apex = TopHeight(kind);
        switch (kind)
        {
            case ReliefKind.Hill:
                {
                    var dome = 1f - Smooth(t);
                    var h = rim + (apex - rim) * dome + (t is > 0f and < 1f ? noise * 0.05f * (1f - t) : 0f);
                    return (h, 0.78f + 0.22f * dome);
                }
            case ReliefKind.Mountain:
                {
                    var peak = MathF.Pow(1f - t, 1.3f);
                    var h = rim + (apex - rim) * peak + (t is > 0f and < 1f ? noise * 0.16f * (1f - t) : 0f);
                    return (h, 0.62f + 0.38f * peak);
                }
            case ReliefKind.Ocean:
                return (rim, 0.45f + 0.4f * Smooth(t)); // deep in the middle, lighter toward the shore
            case ReliefKind.Coast:
            case ReliefKind.Lake:
                return (rim, 0.78f + 0.22f * Smooth(t));
            default:
                return (rim, 0.94f + (t is > 0f and < 1f ? noise * 0.05f : 0f));
        }
    }

    /// <summary>Rim height of the kind's tiles (raised tiles rise from the land level, water stays level).</summary>
    public static float RimHeight(ReliefKind kind) => kind is ReliefKind.Hill or ReliefKind.Mountain ? LandHeight : TopHeight(kind);

    /// <summary>
    /// Distance of a point (<paramref name="x"/>, <paramref name="z"/>) from the tile centre in hexagon metric: 0 at the centre, 1 on the rim, for a tile of radius 1.
    /// </summary>
    public static float RimFraction(float x, float z)
    {
        var a = x - z / 1.7320508f;
        var b = z * 2f / 1.7320508f;
        return Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(a + b)));
    }

    /// <summary>The noise-free surface height at <paramref name="t"/> (see <see cref="RimFraction"/>); the mesh adds a little jitter on top, never at the centre or the rim.</summary>
    public static float SurfaceHeight(ReliefKind kind, float t) => Sample(kind, Math.Clamp(t, 0f, 1f), 0f).Height;

    private static int Ring(int a, int b) => Math.Max(Math.Abs(a), Math.Max(Math.Abs(b), Math.Abs(a + b)));

    /// <summary>The terrain tile mesh for <paramref name="key"/> (a <see cref="MeshKey"/>; any other key gives a flat tile seeded by the key).</summary>
    public static MeshData Build(AssetKey key, float radius = 1f)
    {
        var kind = KindOf(TerrainOf(key) ?? string.Empty);
        var seed = StableHash.Of(key.ToString());
        const int n = Divisions;
        var pos = new List<float>();
        var col = new List<float>();
        var uv = new List<float>();
        var index = new Dictionary<(int, int), int>();
        var heights = new Dictionary<(int, int), float>();
        const float half = 0.8660254f;

        for (var a = -n; a <= n; a++)
        {
            for (var b = -n; b <= n; b++)
            {
                var ring = Ring(a, b);
                if (ring > n) continue;
                var t = ring / (float)n;
                var (height, shade) = Sample(kind, t, Noise(seed, a, b));
                var x = (a + b * 0.5f) / n * radius;
                var z = b * half / n * radius;
                index[(a, b)] = pos.Count / 3;
                heights[(a, b)] = height;
                pos.Add(x); pos.Add(height); pos.Add(z);
                col.Add(shade); col.Add(shade); col.Add(shade); col.Add(1f);
                uv.Add(0.5f + x / radius * 0.5f); uv.Add(0.5f + z / radius * 0.5f);
            }
        }

        var idx = new List<int>();
        for (var a = -n; a < n; a++)
        {
            for (var b = -n; b < n; b++)
            {
                if (index.TryGetValue((a, b), out var v00) && index.TryGetValue((a + 1, b), out var v10) && index.TryGetValue((a, b + 1), out var v01))
                {
                    idx.Add(v00); idx.Add(v10); idx.Add(v01);
                }
                if (index.TryGetValue((a + 1, b), out v10) && index.TryGetValue((a + 1, b + 1), out var v11) && index.TryGetValue((a, b + 1), out v01))
                {
                    idx.Add(v10); idx.Add(v11); idx.Add(v01);
                }
            }
        }

        var normals = new float[pos.Count];
        for (var i = 0; i < idx.Count; i += 3)
        {
            AddFaceNormal(pos, normals, idx[i], idx[i + 1], idx[i + 2]);
        }
        for (var i = 0; i < normals.Length; i += 3)
        {
            var len = MathF.Sqrt(normals[i] * normals[i] + normals[i + 1] * normals[i + 1] + normals[i + 2] * normals[i + 2]);
            if (len > 0f) { normals[i] /= len; normals[i + 1] /= len; normals[i + 2] /= len; }
            else normals[i + 1] = 1f;
        }

        var nrm = new List<float>(normals);
        AddSkirt(kind, radius, pos, nrm, col, uv, idx, n);
        return new MeshData([.. pos], [.. nrm], [.. uv], [.. idx], [.. col]);
    }

    private static void AddFaceNormal(List<float> pos, float[] normals, int i0, int i1, int i2)
    {
        float Get(int v, int c) => pos[v * 3 + c];
        float ux = Get(i1, 0) - Get(i0, 0), uy = Get(i1, 1) - Get(i0, 1), uz = Get(i1, 2) - Get(i0, 2);
        float vx = Get(i2, 0) - Get(i0, 0), vy = Get(i2, 1) - Get(i0, 1), vz = Get(i2, 2) - Get(i0, 2);
        // Clockwise-from-above front face: the up-facing normal is v x u.
        float nx = vy * uz - vz * uy, ny = vz * ux - vx * uz, nz = vx * uy - vy * ux;
        foreach (var v in new[] { i0, i1, i2 })
        {
            normals[v * 3] += nx; normals[v * 3 + 1] += ny; normals[v * 3 + 2] += nz;
        }
    }

    /// <summary>Six side walls from the rim down to y = 0, one quad per lattice edge, so a tile beside a lower one shows a wall instead of a hole.</summary>
    private static void AddSkirt(ReliefKind kind, float radius, List<float> pos, List<float> nrm, List<float> col, List<float> uv, List<int> idx, int n)
    {
        var rimHeight = RimHeight(kind);
        const float wall = 0.55f;
        for (var side = 0; side < 6; side++)
        {
            var (ax, az) = HexGeometry.Corners[side];
            var (bx, bz) = HexGeometry.Corners[(side + 1) % 6];
            var nx = (ax + bx) / 2f;
            var nz = (az + bz) / 2f;
            var len = MathF.Sqrt(nx * nx + nz * nz);
            nx /= len; nz /= len;
            for (var step = 0; step < n; step++)
            {
                float f0 = step / (float)n, f1 = (step + 1) / (float)n;
                float x0 = (ax + (bx - ax) * f0) * radius, z0 = (az + (bz - az) * f0) * radius;
                float x1 = (ax + (bx - ax) * f1) * radius, z1 = (az + (bz - az) * f1) * radius;
                var first = pos.Count / 3;
                void Vertex(float x, float y, float z, float u, float v)
                {
                    pos.Add(x); pos.Add(y); pos.Add(z);
                    nrm.Add(nx); nrm.Add(0f); nrm.Add(nz);
                    col.Add(wall); col.Add(wall); col.Add(wall); col.Add(1f);
                    uv.Add(u); uv.Add(v);
                }
                Vertex(x0, rimHeight, z0, 0f, 0f);
                Vertex(x1, rimHeight, z1, 1f, 0f);
                Vertex(x1, 0f, z1, 1f, 1f);
                Vertex(x0, 0f, z0, 0f, 1f);
                idx.Add(first); idx.Add(first + 2); idx.Add(first + 1);
                idx.Add(first); idx.Add(first + 3); idx.Add(first + 2);
            }
        }
    }
}

/// <summary>C002 vegetation: one grass tuft (three crossed blades) that the client instances with a MultiMesh, and the deterministic scatter of instances over a tile.</summary>
public static class VegetationGeometry
{
    public static readonly AssetKey TuftMeshKey = new("mesh", "tuft");

    /// <summary>Tufts per tile for a base terrain, 0 when the terrain carries none.</summary>
    public static int TuftsFor(string baseTerrain) => baseTerrain switch
    {
        "Grassland" => 7,
        "Plains" => 3,
        _ => 0,
    };

    /// <summary>Three blade triangles 0.22 tall at 0/60/120 degrees; dark at the root, light at the tip (vertex colours).</summary>
    public static MeshData Tuft()
    {
        var pos = new List<float>();
        var nrm = new List<float>();
        var uv = new List<float>();
        var col = new List<float>();
        var idx = new List<int>();
        const float height = 0.22f;
        const float halfWidth = 0.035f;
        foreach (var (dx, dz) in new (float, float)[] { (1f, 0f), (0.5f, 0.8660254f), (-0.5f, 0.8660254f) })
        {
            var first = pos.Count / 3;
            foreach (var (px, py, shade) in new (float, float, float)[] { (-halfWidth, 0f, 0.55f), (halfWidth, 0f, 0.55f), (0f, height, 1f) })
            {
                pos.Add(dx * px); pos.Add(py); pos.Add(dz * px);
                nrm.Add(-dz); nrm.Add(0f); nrm.Add(dx);
                uv.Add(px); uv.Add(py);
                col.Add(shade); col.Add(shade); col.Add(shade); col.Add(1f);
            }
            idx.Add(first); idx.Add(first + 2); idx.Add(first + 1);
        }
        return new MeshData([.. pos], [.. nrm], [.. uv], [.. idx], [.. col]);
    }

    /// <summary>Offset (x, z) from the tile centre, yaw in radians and scale of tuft <paramref name="i"/> on tile (<paramref name="x"/>, <paramref name="y"/>); inside 0.75 of the radius.</summary>
    public static (float X, float Z, float Yaw, float Scale) Placement(int x, int y, int i)
    {
        var seed = StableHash.Of("tuft");
        var h = StableHash.Mix(StableHash.Mix(seed, x, y), i, 17);
        var angle = (h & 0xFFFF) / 65536f * MathF.Tau;
        var dist = MathF.Sqrt(((h >> 16) & 0xFF) / 255f) * 0.72f;
        var g = StableHash.Mix(h, i, 3);
        return (MathF.Cos(angle) * dist, MathF.Sin(angle) * dist, (g & 0xFFFF) / 65536f * MathF.Tau, 0.8f + (g >> 16 & 0xFF) / 255f * 0.6f);
    }
}
