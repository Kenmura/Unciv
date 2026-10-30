using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Unciv.Assets;

/// <summary>
/// C080: the contract of a unit model file (design/UNIT-MODELS.md section 1). Pure, so a test can check every <c>.glb</c> of a pack without Godot: a binary glTF 2.0 file whose figure
/// stands on y = 0, is about <see cref="MinHeight"/> to <see cref="MaxHeight"/> tile units tall inside <see cref="MaxRadius"/> of its origin, has a material named <see cref="TeamColorMaterial"/>
/// (tinted with the civ's colour at load) and, when it has animations, only the clip names of <see cref="ClipNames"/>. Bounds come from the meshes' POSITION extents carried through the
/// node transforms of the bind pose, so a skinned character is measured as it stands before any animation plays.
/// </summary>
public static class UnitModelContract
{
    public const string TeamColorMaterial = "TeamColor";
    public const string IdleClip = "idle";
    public const string WalkClip = "walk";
    public const string AttackClip = "attack";
    public const string RangedAttackClip = "attack_ranged";
    public const string HitClip = "hit";
    public const string DeathClip = "death";

    /// <summary>The fixed clip names an <c>AnimationPlayer</c> of a unit model may hold.</summary>
    public static readonly IReadOnlyList<string> ClipNames = [IdleClip, WalkClip, AttackClip, RangedAttackClip, HitClip, DeathClip];

    public const float MinHeight = 0.4f;
    /// <summary>T315: 1.35 (was 1.0) so a spear, pike or lance can be longer than the man (a figure stands 0.85 high); the body itself is still 0.85.</summary>
    public const float MaxHeight = 1.35f;
    /// <summary>Wider than the 0.25 of the plan because a rig stands in its bind pose (arms out, a crossbow held out front) here (0.5 until C083 added the crossbow bodies; T315: 1.1, because in the bind pose a held spear or pike lies forward at full length: it stands up in the idle pose, so only the bind pose is this wide); the footprint of the playing animation is the art pipeline's concern.</summary>
    public const float MaxRadius = 1.1f;
    public const float FeetTolerance = 0.02f;

    /// <summary>Everything wrong with the file, empty when it satisfies the contract.</summary>
    public static IReadOnlyList<string> Check(byte[] glb)
    {
        var problems = new List<string>();
        JsonElement root;
        try
        {
            root = ReadJson(glb);
        }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        {
            return [e.Message];
        }

        var materials = Names(root, "materials");
        if (!materials.Contains(TeamColorMaterial)) problems.Add($"no material named {TeamColorMaterial} (has: {string.Join(", ", materials)})");
        foreach (var clip in Names(root, "animations").Where(it => !ClipNames.Contains(it, StringComparer.Ordinal)))
            problems.Add($"clip '{clip}' is not one of {string.Join(", ", ClipNames)}");

        (Vector3 Min, Vector3 Max)? bounds;
        try
        {
            bounds = Bounds(root);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException or ArgumentOutOfRangeException)
        {
            problems.Add("cannot measure the model (a mesh, accessor or node is malformed or lacks POSITION min/max): " + e.Message);
            return problems;
        }
        if (bounds is not var (min, max))
        {
            problems.Add("no mesh in the default scene");
            return problems;
        }
        if (MathF.Abs(min.Y) > FeetTolerance) problems.Add(string.Create(CultureInfo.InvariantCulture, $"feet at y = {min.Y:0.###}, expected 0"));
        var height = max.Y - min.Y;
        if (height is < MinHeight or > MaxHeight) problems.Add(string.Create(CultureInfo.InvariantCulture, $"height {height:0.###} outside {MinHeight}..{MaxHeight}"));
        var radius = MathF.Max(MathF.Max(MathF.Abs(min.X), MathF.Abs(max.X)), MathF.Max(MathF.Abs(min.Z), MathF.Abs(max.Z)));
        if (radius > MaxRadius) problems.Add(string.Create(CultureInfo.InvariantCulture, $"radius {radius:0.###} beyond {MaxRadius}"));
        return problems;
    }

    private static JsonElement ReadJson(byte[] glb)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != 0x46546C67u) throw new InvalidDataException("not a binary glTF (missing glTF magic)");
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        if (length < 0 || 20 + length > glb.Length || BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)) != 0x4E4F534Au) throw new InvalidDataException("first chunk is not JSON");
        using var doc = JsonDocument.Parse(glb.AsMemory(20, length));
        return doc.RootElement.Clone();
    }

    private static List<string> Names(JsonElement root, string array) =>
        root.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray().Select(it => it.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "")]
            : [];

    private static (Vector3 Min, Vector3 Max)? Bounds(JsonElement root)
    {
        if (!root.TryGetProperty("nodes", out var nodes) || !root.TryGetProperty("scenes", out var scenes) || scenes.GetArrayLength() == 0) return null;
        var meshes = root.TryGetProperty("meshes", out var m) ? m : default;
        var accessors = root.TryGetProperty("accessors", out var a) ? a : default;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        var scene = scenes[root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0];
        var pending = new Stack<(int Node, Matrix4x4 Parent, int Depth)>();
        if (scene.TryGetProperty("nodes", out var top)) foreach (var n in top.EnumerateArray().Reverse()) pending.Push((n.GetInt32(), Matrix4x4.Identity, 0));
        while (pending.Count > 0)
        {
            var (index, parent, depth) = pending.Pop();
            if (depth > 64 || index < 0 || index >= nodes.GetArrayLength()) continue;
            var node = nodes[index];
            var world = Local(node) * parent;
            if (node.TryGetProperty("mesh", out var meshIndex) && meshes.ValueKind == JsonValueKind.Array)
            {
                // a skinned mesh ignores its own node transform (glTF 2.0 section 3.7.3.3): its vertices are already in the rig's space. Assumes the joints sit under the mesh node's parent (as the
                // bundled rigs do), so joint world times inverse bind is the identity at bind; a rig whose armature is a sibling subtree with its own transform would be mis-measured.
                var matrix = node.TryGetProperty("skin", out _) ? parent : world;
                foreach (var prim in meshes[meshIndex.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    var accessor = accessors[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()];
                    var lo = Vec(accessor.GetProperty("min"));
                    var hi = Vec(accessor.GetProperty("max"));
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var p = Vector3.Transform(new Vector3((corner & 1) == 0 ? lo.X : hi.X, (corner & 2) == 0 ? lo.Y : hi.Y, (corner & 4) == 0 ? lo.Z : hi.Z), matrix);
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                        any = true;
                    }
                }
            }
            if (node.TryGetProperty("children", out var children)) foreach (var c in children.EnumerateArray().Reverse()) pending.Push((c.GetInt32(), world, depth + 1));
        }
        return any ? (min, max) : null;
    }

    private static Vector3 Vec(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());

    private static Matrix4x4 Local(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrix))
        {
            var v = matrix.EnumerateArray().Select(it => it.GetSingle()).ToArray();
            return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
        }
        var scale = node.TryGetProperty("scale", out var sc) ? Vec(sc) : Vector3.One;
        var rotation = node.TryGetProperty("rotation", out var r) ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
        var translation = node.TryGetProperty("translation", out var t) ? Vec(t) : Vector3.Zero;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }
}
