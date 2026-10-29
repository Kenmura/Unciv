using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.Logic.Map;

namespace Unciv.GodotClient.Map;

/// <summary>One tile handed to the chunk layer: where it is, which silhouette it uses and its (fog-dimmed) colour as sRGB-encoded numbers.</summary>
public sealed record ChunkTile(HexCoord Coord, Mesh Mesh, Tint Colour);

/// <summary>
/// C031: the terrain surface as chunk meshes. Tiles are grouped by <see cref="TerrainChunks"/> into square ground cells, each cell is one merged
/// <see cref="ArrayMesh"/> with the tile colours baked into vertex colours (blended toward the neighbouring terrains by <see cref="TerrainBlend"/>), all sharing
/// one material. Fewer draw calls than a node per tile, and a chunk is hidden when it is outside the camera's ground footprint (<see cref="Cull"/>).
/// </summary>
public partial class TerrainChunkLayer : Node3D
{
    private sealed record Chunk(ChunkId Id, GroundRect Bounds, MeshInstance3D Node, Vector3[] Positions, Color[] Colours);

    private readonly List<Chunk> chunks = [];
    private readonly Dictionary<ChunkId, Chunk> byId = [];
    private Material? material;
    private int shaderTier;

    public int ChunkCount => chunks.Count;

    public int VisibleChunkCount => chunks.Count(it => it.Node.Visible);

    /// <summary>Triangles over all chunks.</summary>
    public int TriangleCount { get; private set; }

    /// <summary>The material tier drawing the chunks (<see cref="Unciv.GodotClient.Settings.GraphicsPreset.ShaderTier"/>): 0 the plain vertex-colour material (Low, High), 1 and up the C098 water and terrain shader (Ultra). Changing it re-materials the chunks that exist now and those built later.</summary>
    public int ShaderTier
    {
        get => shaderTier;
        set
        {
            if (value == shaderTier) return;
            shaderTier = value;
            material = null;
            if (chunks.Count == 0) return;
            material = MaterialFor(shaderTier);
            foreach (var chunk in chunks) chunk.Node.MaterialOverride = material;
        }
    }

    /// <summary>The material the chunks currently wear (tests and scenarios inspect it).</summary>
    public Material? ChunkMaterial => material;

    private static Material MaterialFor(int tier) => tier >= 1 ? MapShaders.TerrainUltra() : MapMaterials.TerrainChunks();

    public void Clear()
    {
        foreach (var chunk in chunks) chunk.Node.QueueFree();
        chunks.Clear();
        byId.Clear();
        TriangleCount = 0;
    }

    public void Build(IReadOnlyList<ChunkTile> tiles)
    {
        Clear();
        material ??= MaterialFor(shaderTier);
        var colours = tiles.ToDictionary(it => it.Coord, it => it.Colour);
        var centres = tiles.Select(it => MapLayout.TileTop(it.Coord.X, it.Coord.Y, 0f)).ToList();
        var arrays = new Dictionary<Mesh, MeshArrays>();
        foreach (var (id, members) in TerrainChunks.Group([.. centres.Select(it => (it.X, it.Z))]))
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var vertexColours = new List<Color>();
            var indices = new List<int>();
            foreach (var index in members)
            {
                var tile = tiles[index];
                var centre = centres[index];
                if (!arrays.TryGetValue(tile.Mesh, out var source)) arrays[tile.Mesh] = source = MeshArrays.Of(tile.Mesh);
                var neighbours = NeighboursOf(colours, centre);
                var own = tile.Colour;
                var offset = positions.Count;
                for (var v = 0; v < source.Positions.Length; v++)
                {
                    var local = source.Positions[v];
                    positions.Add(local + centre);
                    normals.Add(source.Normals[v]);
                    uvs.Add(source.Uvs[v]);
                    // The walls (below the rim) keep the plain tile colour so a cliff next to a lower tile stays a cliff.
                    var blended = local.Y > 0f ? TerrainBlend.Mix(own, neighbours, local.X, local.Z) : own;
                    var linear = blended.SrgbToLinear();
                    var shade = source.Shades[v];
                    vertexColours.Add(MapMaterials.VertexColour(linear, shade));
                }
                foreach (var i in source.Indices) indices.Add(offset + i);
            }
            AddChunk(id, members.Select(it => (centres[it].X, centres[it].Z)), positions, normals, uvs, vertexColours, indices);
        }
    }

    private void AddChunk(ChunkId id, IEnumerable<(float X, float Z)> memberCentres, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colours, List<int> indices)
    {
        var data = new Godot.Collections.Array();
        data.Resize((int)Mesh.ArrayType.Max);
        data[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
        data[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        data[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        data[(int)Mesh.ArrayType.Color] = colours.ToArray();
        data[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, data);
        var node = new MeshInstance3D { Name = $"Chunk_{id.X}_{id.Z}", Mesh = mesh, MaterialOverride = material };
        AddChild(node);
        var chunk = new Chunk(id, TerrainChunks.BoundsOf(memberCentres), node, [.. positions], [.. colours]);
        chunks.Add(chunk);
        byId[id] = chunk;
        TriangleCount += indices.Count / 3;
    }

    /// <summary>Colours of the six neighbours in <see cref="TerrainBlend.EdgeNormals"/> order; null where the neighbour is not drawn.</summary>
    private static Tint?[] NeighboursOf(Dictionary<HexCoord, Tint> colours, Vector3 centre)
    {
        var result = new Tint?[TerrainBlend.EdgeNormals.Length];
        for (var edge = 0; edge < result.Length; edge++)
        {
            var (nx, nz) = TerrainBlend.EdgeNormals[edge];
            var at = MapLayout.TileAt(centre + new Vector3(nx, 0f, nz) * TerrainBlend.NeighbourDistance);
            if (colours.TryGetValue(at, out var colour)) result[edge] = colour;
        }
        return result;
    }

    /// <summary>Shows the chunks whose bounds meet the camera's ground footprint (null: show everything).</summary>
    public void Cull(GroundRect? footprint)
    {
        foreach (var chunk in chunks) chunk.Node.Visible = footprint is not { } view || TerrainChunks.IsVisible(chunk.Bounds, view);
    }

    /// <summary>Whether the chunk holding the world point <paramref name="ground"/> is currently drawn.</summary>
    public bool IsChunkVisibleAt(Vector3 ground) => byId.TryGetValue(TerrainChunks.Of(ground.X, ground.Z), out var chunk) && chunk.Node.Visible;

    /// <summary>The baked (linear) colour of the top-surface vertex nearest to the world point <paramref name="ground"/>, or null when no chunk covers it; tests read the blend through this.</summary>
    public Color? ColourNear(Vector3 ground)
    {
        if (!byId.TryGetValue(TerrainChunks.Of(ground.X, ground.Z), out var chunk)) return null;
        var best = -1;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < chunk.Positions.Length; i++)
        {
            var p = chunk.Positions[i];
            if (p.Y <= 0f) continue;
            var d = (p.X - ground.X) * (p.X - ground.X) + (p.Z - ground.Z) * (p.Z - ground.Z);
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }
        return best < 0 ? null : chunk.Colours[best];
    }

    /// <summary>The arrays of a tile mesh, read once per distinct mesh.</summary>
    private sealed record MeshArrays(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, float[] Shades, int[] Indices)
    {
        public static MeshArrays Of(Mesh mesh)
        {
            var data = mesh.SurfaceGetArrays(0);
            var positions = data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = data[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var uvs = data[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var colours = data[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? [] : data[(int)Mesh.ArrayType.Color].AsColorArray();
            var indices = data[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? [] : data[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0) indices = [.. Enumerable.Range(0, positions.Length)];
            var shades = new float[positions.Length];
            for (var i = 0; i < shades.Length; i++) shades[i] = i < colours.Length ? colours[i].R : 1f;
            if (normals.Length != positions.Length) normals = [.. positions.Select(_ => Vector3.Up)];
            if (uvs.Length != positions.Length) uvs = [.. positions.Select(_ => Vector2.Zero)];
            return new MeshArrays(positions, normals, uvs, shades, indices);
        }
    }
}
