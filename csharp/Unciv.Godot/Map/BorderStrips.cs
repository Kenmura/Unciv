using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;

namespace Unciv.GodotClient.Map;

/// <summary>
/// T377: the territory borders of the map, one strip mesh per civ drawn twice (solid and ghost, see <see cref="BorderShader"/>). The mesh is made once per snapshot from <see cref="BorderChains"/> and
/// <see cref="BorderMesh"/>; the band width is a shader uniform in screen pixels (<see cref="BorderStyle.WidthPixels"/>), refreshed through <see cref="ApplyView"/> whenever the camera, the viewport or the UI scale changes.
/// </summary>
public partial class BorderStrips : Node3D
{
    private readonly List<ShaderMaterial> materials = [];

    /// <summary>The band width in pixels last applied (0 before the first <see cref="ApplyView"/>).</summary>
    public int BandPixels { get; private set; }

    /// <summary>Strip meshes made (two per civ with a border: solid and ghost).</summary>
    public int StripCount => GetChildCount();

    /// <summary>Adds the strips of one civ.</summary>
    public void Add(string owner, BorderMeshData data, Color outer, Color casing)
    {
        if (data.Vertices.Count == 0) return;
        var vertices = new Vector3[data.Vertices.Count];
        var uv = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length];
        var colours = new Color[vertices.Length];
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = data.Vertices[i];
            vertices[i] = new Vector3(v.X, v.Y, v.Z);
            uv[i] = new Vector2(v.InX, v.InZ);
            uv2[i] = new Vector2(v.OutX, v.OutZ);
            colours[i] = BorderShader.Attribute(v);
            min = min.Min(vertices[i]);
            max = max.Max(vertices[i]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        // The vertex stage moves vertices by up to band_px pixels after culling data is made: grow the bounds well beyond that (a few world units at the farthest zoom)
        mesh.CustomAabb = new Aabb(min - new Vector3(3f, 3f, 3f), max - min + new Vector3(6f, 6f, 6f));
        foreach (var ghost in new[] { true, false })
        {
            var material = BorderShader.Material(ghost, outer, casing);
            materials.Add(material);
            AddChild(new MeshInstance3D { Name = (ghost ? "BorderGhost_" : "Border_") + owner, Mesh = mesh, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    /// <summary>Sets the band width for the camera and viewport: a tile is two world units wide and the camera's size is the world height of the viewport.</summary>
    public void ApplyView(Camera3D camera, Vector2 viewportSize, float uiScale)
    {
        var tilePixels = camera.Size > 0f ? 2f * viewportSize.Y / camera.Size : 0f;
        BandPixels = BorderStyle.WidthPixels(tilePixels, uiScale);
        foreach (var material in materials) material.SetShaderParameter("band_px", (float)BandPixels);
    }
}
