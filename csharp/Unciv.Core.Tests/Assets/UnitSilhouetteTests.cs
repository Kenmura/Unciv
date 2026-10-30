using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Unciv.Core.Tests.Assets;

/// <summary>
/// T295: the owner's top priority is that every unit is recognisable at any view, the default play zoom included (a Settler must never be taken for an Archer). The models are drawn as
/// silhouettes (every triangle of the bind pose, orthographic, seen from the game's camera pitch from four sides, one filled mask per view) and two units of different <c>style</c>
/// (attachments.json: the class of the unit) must differ by a minimum Jaccard distance, the civilian-against-military pairs by more. Colour and fine texture do not count: a mask only knows
/// the outline, which is all that survives at the default zoom. The contact sheet of the real-GPU scenario <c>unit-sheet</c> is the human check of the same property.
/// </summary>
public sealed class UnitSilhouetteTests
{
    private const int W = 40, H = 56;
    private const float Pitch = 50f * MathF.PI / 180f;
    /// <summary>Two foot units of different styles (axe, spear, bow, rifle, ...): at least this far apart (1 - intersection over union), averaged over the four sides. T310: checked for EVERY pair of units, not one
    /// representative per style.</summary>
    private const float MinDistance = 0.18f;
    /// <summary>T310: two units of one style (Warrior, Maori Warrior and Brute; Rifleman and Marine) differ by their look: another hat, load on the back, gun or shield.</summary>
    private const float MinLookDistance = 0.10f;
    /// <summary>Two riders: the mount fills most of the outline, so the rider's gear and the mount's trappings decide a smaller part. T308 0.13, raised by T310; checked for every pair of riders.</summary>
    private const float MinRiderDistance = 0.15f;
    /// <summary>T310: the three war elephants seen from the default view, above the rider threshold.</summary>
    private const float MinElephantDistance = 0.16f;
    /// <summary>A civilian (Settler, Worker, great person) against a soldier: the confusion the owner named as unacceptable. T310: every pair.</summary>
    private const float MinCivilianDistance = 0.30f;
    /// <summary>T310: the scale a figure got over the scale its body alone allows (1 = full size): no gear may shrink a unit. The musket, rifle, crew, crossbow and spear figures were 0.66 to 0.81.</summary>
    private const float MinBodyFit = 0.96f;

    private static readonly string Pack = FindPack();

    private static string FindPack()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Unciv.Godot", "Packs", "units");
            if (File.Exists(Path.Combine(candidate, "attachments.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("Unciv.Godot/Packs/units not found above " + AppContext.BaseDirectory);
    }

    private static bool IsCivilian(string style) => style is "settler" or "worker" || style.StartsWith("great_", StringComparison.Ordinal);

    private sealed record Unit(string Name, string Style, string Look, string Mount);

    /// <summary>The styled units, foot units and riders separately. A rider's class also names his mount (a horse and its trappings, a chariot, each kind and variant of elephant), since the mount fills most of the outline.</summary>
    private static (List<Unit> Foot, List<Unit> Mounted) Units()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack, "attachments.json")));
        var root = data.RootElement;
        static string MountOf(JsonElement unit)
        {
            if (!unit.TryGetProperty("mount", out var mount)) return "horse";
            var kind = mount.TryGetProperty("kind", out var k) ? k.GetString() : "horse";
            return mount.TryGetProperty("variant", out var v) ? $"{kind}/{v.GetString()}" : kind!;
        }
        List<Unit> Of(JsonElement units) => [.. units.EnumerateObject().Where(it => it.Value.TryGetProperty("style", out _)).Select(it =>
            new Unit(it.Name, it.Value.GetProperty("style").GetString()!, it.Value.TryGetProperty("look", out var l) ? l.GetString()! : "", MountOf(it.Value)))];
        return (Of(root.GetProperty("units")), Of(root.GetProperty("mounted").GetProperty("units")));
    }

    private static bool[][] Masks(string unit)
    {
        var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit + ".glb"));
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = doc.RootElement;
        var binStart = 20 + jsonLength + 8;
        var triangles = Triangles(root, glb, binStart);
        var masks = new bool[4][];
        for (var side = 0; side < 4; side++)
        {
            var yaw = Matrix4x4.CreateRotationY(side * MathF.PI / 2f);
            masks[side] = Rasterise(triangles.Select(t => (Project(Vector3.Transform(t.A, yaw)), Project(Vector3.Transform(t.B, yaw)), Project(Vector3.Transform(t.C, yaw)))));
        }
        return masks;
    }

    private static Vector2 Project(Vector3 p) => new(p.X, p.Y * MathF.Cos(Pitch) - p.Z * MathF.Sin(Pitch));

    private static bool[] Rasterise(IEnumerable<(Vector2 A, Vector2 B, Vector2 C)> tris)
    {
        // a fixed scale: one world unit is 1/1.15 of the mask's height, so a taller or wider unit really covers more of it; the origin is the middle of the feet
        var mask = new bool[W * H];
        float Sx(float x) => W / 2f + x * (H / 1.15f);
        float Sy(float y) => H * 0.78f - y * (H / 1.15f);
        foreach (var (a, b, c) in tris)
        {
            var pa = new Vector2(Sx(a.X), Sy(a.Y));
            var pb = new Vector2(Sx(b.X), Sy(b.Y));
            var pc = new Vector2(Sx(c.X), Sy(c.Y));
            var x0 = global::System.Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X))));
            var x1 = global::System.Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
            var y0 = global::System.Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y))));
            var y1 = global::System.Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var d1 = Edge(pa, pb, p);
                    var d2 = Edge(pb, pc, p);
                    var d3 = Edge(pc, pa, p);
                    var inside = !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
                    if (inside || TouchesPixel(pa, pb, pc, x, y)) mask[y * W + x] = true;
                }
        }
        return mask;
    }

    /// <summary>A sliver thinner than a pixel (a spear shaft) still marks the pixel its corner falls in.</summary>
    private static bool TouchesPixel(Vector2 a, Vector2 b, Vector2 c, int x, int y) =>
        new[] { a, b, c }.Any(v => (int)MathF.Floor(v.X) == x && (int)MathF.Floor(v.Y) == y);

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);

    private static List<(Vector3 A, Vector3 B, Vector3 C)> Triangles(JsonElement root, byte[] glb, int binStart)
    {
        var nodes = root.GetProperty("nodes");
        var meshes = root.GetProperty("meshes");
        var accessors = root.GetProperty("accessors");
        var views = root.GetProperty("bufferViews");
        var result = new List<(Vector3, Vector3, Vector3)>();
        var pending = new Stack<(int, Matrix4x4)>();
        foreach (var n in root.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray()) pending.Push((n.GetInt32(), Matrix4x4.Identity));
        while (pending.Count > 0)
        {
            var (index, parent) = pending.Pop();
            var node = nodes[index];
            var world = Local(node) * parent;
            if (node.TryGetProperty("mesh", out var mesh))
            {
                var matrix = node.TryGetProperty("skin", out _) ? parent : world; // a skinned mesh is already in the rig's space
                foreach (var prim in meshes[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    var pos = ReadVec3(accessors[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()], views, glb, binStart);
                    var idx = ReadIndices(accessors[prim.GetProperty("indices").GetInt32()], views, glb, binStart);
                    for (var i = 0; i + 2 < idx.Length; i += 3)
                        result.Add((Vector3.Transform(pos[idx[i]], matrix), Vector3.Transform(pos[idx[i + 1]], matrix), Vector3.Transform(pos[idx[i + 2]], matrix)));
                }
            }
            if (node.TryGetProperty("children", out var children)) foreach (var c in children.EnumerateArray()) pending.Push((c.GetInt32(), world));
        }
        return result;
    }

    private static int Offset(JsonElement accessor, JsonElement views, int binStart)
    {
        var view = views[accessor.GetProperty("bufferView").GetInt32()];
        return binStart + (view.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
    }

    private static Vector3[] ReadVec3(JsonElement accessor, JsonElement views, byte[] glb, int binStart)
    {
        var offset = Offset(accessor, views, binStart);
        return [.. Enumerable.Range(0, accessor.GetProperty("count").GetInt32()).Select(i => new Vector3(BitConverter.ToSingle(glb, offset + i * 12), BitConverter.ToSingle(glb, offset + i * 12 + 4), BitConverter.ToSingle(glb, offset + i * 12 + 8)))];
    }

    private static int[] ReadIndices(JsonElement accessor, JsonElement views, byte[] glb, int binStart)
    {
        var offset = Offset(accessor, views, binStart);
        var count = accessor.GetProperty("count").GetInt32();
        return accessor.GetProperty("componentType").GetInt32() == 5123
            ? [.. Enumerable.Range(0, count).Select(i => (int)BitConverter.ToUInt16(glb, offset + i * 2))]
            : [.. Enumerable.Range(0, count).Select(i => (int)BitConverter.ToUInt32(glb, offset + i * 4))];
    }

    private static Matrix4x4 Local(JsonElement node)
    {
        static Vector3 Vec(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        var scale = node.TryGetProperty("scale", out var sc) ? Vec(sc) : Vector3.One;
        var rotation = node.TryGetProperty("rotation", out var r) ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
        var translation = node.TryGetProperty("translation", out var t) ? Vec(t) : Vector3.Zero;
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
    }

    /// <summary>1 - intersection over union of two masks, averaged over the four sides.</summary>
    private static float Distance(bool[][] a, bool[][] b) => Enumerable.Range(0, 4).Average(side =>
    {
        var inter = 0;
        var union = 0;
        for (var i = 0; i < W * H; i++)
        {
            if (a[side][i] && b[side][i]) inter++;
            if (a[side][i] || b[side][i]) union++;
        }
        return union == 0 ? 0f : 1f - (float)inter / union;
    });

    private static List<string> Failures(List<Unit> units, Func<Unit, Unit, float> need)
    {
        var masks = units.ToDictionary(u => u.Name, u => Masks(u.Name));
        var failures = new List<string>();
        foreach (var a in units)
            foreach (var b in units.Where(b => string.CompareOrdinal(b.Name, a.Name) > 0))
            {
                var d = Distance(masks[a.Name], masks[b.Name]);
                var min = need(a, b);
                if (d < min) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{a.Name} ({a.Style}/{a.Look}) and {b.Name} ({b.Style}/{b.Look}) differ by {d:0.000}, need {min:0.00}"));
            }
        return failures;
    }

    private static float BodyFit(string unit)
    {
        var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit + ".glb"));
        using var doc = JsonDocument.Parse(glb.AsMemory(20, BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12))));
        return doc.RootElement.GetProperty("asset").GetProperty("extras").GetProperty("bodyFit").GetSingle();
    }

    [Fact]
    public void EveryFootUnitIsToldApartFromEveryOtherAndCiviliansAreNotSoldiers()
    {
        var (foot, _) = Units();
        Assert.True(foot.Count >= 55, "the styled foot units");
        var failures = Failures(foot, (a, b) =>
            IsCivilian(a.Style) != IsCivilian(b.Style) ? MinCivilianDistance : a.Style != b.Style ? MinDistance : MinLookDistance);
        Assert.True(failures.Count == 0, "silhouettes too alike:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void EveryUnitOfAStyleHasItsOwnLook()
    {
        var (foot, mounted) = Units();
        foreach (var group in foot.Concat(mounted).GroupBy(u => (u.Style, u.Mount)).Where(g => g.Count() > 1 && !IsCivilian(g.Key.Style)))
            Assert.True(group.Select(u => u.Look + "/" + u.Mount).Distinct().Count() == group.Count(), $"units of style {group.Key.Style} on {group.Key.Mount} share a look: {string.Join(", ", group.Select(u => u.Name))}");
    }

    [Fact]
    public void ARiderIsRecognisedByHisGearAndHisMountAmongRiders()
    {
        var (_, mounted) = Units();
        Assert.True(mounted.Count >= 15, "the styled riders");
        Assert.True(mounted.Select(u => u.Style + "/" + u.Mount).Distinct().Count() >= 15, "each trapping of a mount is a class of its own");
        var failures = Failures(mounted, (_, _) => MinRiderDistance);
        Assert.True(failures.Count == 0, "rider silhouettes too alike:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TheThreeWarElephantsAreToldApartFromTheDefaultView()
    {
        var (_, mounted) = Units();
        var elephants = mounted.Where(u => u.Mount.StartsWith("elephant", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, elephants.Count);
        Assert.Equal(3, elephants.Select(u => u.Mount).Distinct().Count());
        var failures = Failures(elephants, (_, _) => MinElephantDistance);
        Assert.True(failures.Count == 0, "elephants too alike:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void NoGearShrinksAFootFigure()
    {
        var (foot, _) = Units();
        var shrunk = foot.Where(u => BodyFit(u.Name) < MinBodyFit).Select(u => $"{u.Name} {BodyFit(u.Name):0.000}").ToList();
        Assert.True(shrunk.Count == 0, "figures below full size (the contract caps the bind-pose radius at 0.62 and the height at 0.98; shorten the gear):\n" + string.Join("\n", shrunk));
    }

    [Fact]
    public void ADecisiveFailureIsReported()
    {
        // the check can fail: a unit against itself has distance 0, which is below every threshold
        var masks = Masks("Settler");
        Assert.True(Distance(masks, masks) < MinLookDistance);
        Assert.True(Distance(masks, Masks("Archer")) > 0f);
        Assert.True(Distance(Masks("Rifleman"), Masks("Marine")) >= MinLookDistance, "two units of one style are told apart");
    }
}
