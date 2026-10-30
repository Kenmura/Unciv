using Godot;
using Unciv.Assets;

namespace Unciv.GodotClient.Map;

/// <summary>
/// T287: the feature and resource prop models of the map merged into one mesh per terrain chunk and kind, drawn instead of the per-tile models when the camera is zoomed far out
/// (<see cref="FarZoomLod.MergeProps"/>). A prop model is a node per tile with one mesh instance per part and its own material, so Godot cannot batch them: a Large map issued about six thousand
/// draw calls for them at the maximum zoom-out. The merge bakes each part's transform and flat colour (the material's albedo, fog dimming included, as a vertex colour) into the chunk mesh, so
/// the picture is the same; a prop whose model is not plain coloured meshes (a textured or animated model from an asset pack) is not merged and keeps being drawn as a node.
/// </summary>
public partial class FarPropLayer : Node3D
{
    private readonly Node3D features = new() { Name = "Features", Visible = false };
    private readonly Node3D resources = new() { Name = "Resources", Visible = false };
    private StandardMaterial3D? material;

    /// <summary>Props whose model was merged (their nodes are hidden while the merged meshes are drawn).</summary>
    public IReadOnlySet<Node3D> Merged
    {
        get
        {
            EnsureBuilt();
            return merged;
        }
    }

    private IReadOnlySet<Node3D> merged = new HashSet<Node3D>();
    private List<(Node3D Tile, Node3D Prop, bool Resource)>? pending;

    /// <summary>Merged meshes made (one per chunk and kind).</summary>
    public int MeshCount
    {
        get
        {
            EnsureBuilt();
            return features.GetChildCount() + resources.GetChildCount();
        }
    }

    /// <summary>Vertices of the merged meshes of features (or of resources) and the distinct vertex colours they carry; tests compare them with the prop nodes they replace.</summary>
    public (int Vertices, IReadOnlySet<Color> Colours) Baked(bool resource)
    {
        EnsureBuilt();
        var vertices = 0;
        var colours = new HashSet<Color>();
        foreach (var node in (resource ? resources : features).GetChildren().OfType<MeshInstance3D>())
        {
            var data = node.Mesh.SurfaceGetArrays(0);
            vertices += data[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length;
            colours.UnionWith(data[(int)Mesh.ArrayType.Color].AsColorArray());
        }
        return (vertices, colours);
    }

    /// <summary>Whether the merged meshes of features, and of resources, are currently drawn.</summary>
    public bool FeaturesDrawn => features.Visible;

    public bool ResourcesDrawn => resources.Visible;

    public override void _Ready()
    {
        AddChild(features);
        AddChild(resources);
    }

    /// <summary>Shows the merged meshes (<paramref name="far"/>) or hides them; the resource meshes also follow <paramref name="showResources"/>.</summary>
    public void Apply(bool far, bool showResources)
    {
        if (far) EnsureBuilt();
        features.Visible = far;
        resources.Visible = far && showResources;
    }

    /// <summary>Forgets the merged meshes and remembers <paramref name="props"/> (tile node, prop node, whether it is the tile's resource) to merge on first use: reading a mesh back for every prop is a cost the player only pays once the camera goes far out, not on every refresh after a command.</summary>
    public void Build(IEnumerable<(Node3D Tile, Node3D Prop, bool Resource)> props)
    {
        pending = [.. props];
        merged = new HashSet<Node3D>();
        foreach (var root in new[] { features, resources })
        {
            foreach (var child in root.GetChildren())
            {
                root.RemoveChild(child);
                child.QueueFree();
            }
        }
    }

    private void EnsureBuilt()
    {
        if (pending is not { } props) return;
        pending = null;
        material ??= new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 1f };
        var done = new HashSet<Node3D>();
        var cache = new Dictionary<(Mesh, int), Godot.Collections.Array>();
        var groups = new Dictionary<(bool Resource, ChunkId Id), Bake>();
        foreach (var (tile, prop, resource) in props)
        {
            if (!GodotObject.IsInstanceValid(prop) || !GodotObject.IsInstanceValid(tile) || !CanMerge(prop)) continue;
            var key = (resource, TerrainChunks.Of(tile.Position.X, tile.Position.Z));
            if (!groups.TryGetValue(key, out var bake)) groups[key] = bake = new Bake();
            Collect(bake, prop, Transform3D.Identity.Translated(tile.Position), cache, root: true);
            done.Add(prop);
        }
        foreach (var ((resource, id), bake) in groups.OrderBy(it => it.Key.Resource).ThenBy(it => it.Key.Id.X).ThenBy(it => it.Key.Id.Z))
        {
            if (bake.Positions.Count == 0) continue;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = bake.Positions.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = bake.Normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = bake.Colours.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = bake.Indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            (resource ? resources : features).AddChild(new MeshInstance3D { Name = $"{(resource ? "Resource" : "Feature")}Props_{id.X}_{id.Z}", Mesh = mesh, MaterialOverride = material });
        }
        merged = done;
    }

    private sealed class Bake
    {
        public List<Vector3> Positions { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<Color> Colours { get; } = [];
        public List<int> Indices { get; } = [];
    }

    /// <summary>Whether a prop is nothing but plain nodes and triangle meshes with flat opaque rough materials (what <see cref="Collect"/> reproduces exactly).</summary>
    private static bool CanMerge(Node3D node)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            if (instance.Skeleton != null && !instance.Skeleton.IsEmpty) return false;
            for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (!IsTriangles(mesh, surface) || MaterialOf(instance, surface) is not { } m || !IsFlat(m)) return false;
            }
        }
        else if (node.GetType() != typeof(Node3D)) return false;
        return node.GetChildren().All(it => it is Node3D child && CanMerge(child));
    }

    private static bool IsTriangles(Mesh mesh, int surface) => mesh is PrimitiveMesh || mesh is ArrayMesh array && array.SurfaceGetPrimitiveType(surface) == Mesh.PrimitiveType.Triangles;

    private static StandardMaterial3D? MaterialOf(MeshInstance3D instance, int surface) =>
        (instance.MaterialOverride ?? instance.GetSurfaceOverrideMaterial(surface) ?? instance.Mesh.SurfaceGetMaterial(surface)) as StandardMaterial3D;

    private static bool IsFlat(StandardMaterial3D m) =>
        m.AlbedoTexture == null && m.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && m.AlbedoColor.A >= 1f && !m.VertexColorUseAsAlbedo
        && m.ShadingMode == BaseMaterial3D.ShadingModeEnum.PerPixel && m.CullMode == BaseMaterial3D.CullModeEnum.Back
        && m.Metallic == 0f && m.Roughness == 1f && !m.EmissionEnabled && !m.NormalEnabled && !m.Uv1Triplanar;

    private static void Collect(Bake bake, Node3D node, Transform3D parent, Dictionary<(Mesh, int), Godot.Collections.Array> cache, bool root = false)
    {
        if (!root && !node.Visible) return; // the prop itself is shown or hidden from outside (resource toggle, far switch): bake it regardless
        var transform = parent * node.Transform;
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            var normalBasis = transform.Basis.Inverse().Transposed();
            for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (!cache.TryGetValue((mesh, surface), out var data)) cache[(mesh, surface)] = data = mesh.SurfaceGetArrays(surface);
                var positions = data[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var normals = data[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? [] : data[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var indices = data[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? [] : data[(int)Mesh.ArrayType.Index].AsInt32Array();
                if (indices.Length == 0) indices = [.. Enumerable.Range(0, positions.Length)];
                var colour = MaterialOf(instance, surface)!.AlbedoColor.SrgbToLinear();
                var offset = bake.Positions.Count;
                for (var v = 0; v < positions.Length; v++)
                {
                    bake.Positions.Add(transform * positions[v]);
                    bake.Normals.Add(v < normals.Length ? (normalBasis * normals[v]).Normalized() : Vector3.Up);
                    bake.Colours.Add(colour);
                }
                var mirrored = transform.Basis.Determinant() < 0f; // a mirrored scale flips the winding
                for (var t = 0; t + 2 < indices.Length; t += 3)
                {
                    bake.Indices.Add(offset + indices[t]);
                    bake.Indices.Add(offset + indices[mirrored ? t + 2 : t + 1]);
                    bake.Indices.Add(offset + indices[mirrored ? t + 1 : t + 2]);
                }
            }
        }
        foreach (var child in node.GetChildren()) Collect(bake, (Node3D)child, transform, cache);
    }
}
