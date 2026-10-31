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
    /// <summary>T315: two riders whose outlines differ by less than <see cref="OutlineEnough"/> (the horse fills most of the mask, so most riders differ little) must have mounts of clearly different coats (distance of the
    /// linear colour factors): the tall tan riders of the T310 sheet (Horseman, Horse Archer, Cavalry, Lancer, Hussar, Cossack) were told apart only by a hat.</summary>
    private const float MinCoatDistance = 0.2f;
    private const float OutlineEnough = 0.30f;
    /// <summary>T315: the pike reaches this much further than the spear (the bind pose lays a held spear forward; before T315 the contract caps made it only 1.18 times).</summary>
    private const float PikeOverSpear = 1.3f;

    /// <summary>T307: two rigid units (ships, siege, armour, aircraft, missiles) seen from the default view: the same bar as the foot units. Measured minimum when set: 0.188 (Zero and Great War Bomber); T324 raised the bar from 0.18 to 0.185 (the aircraft stay close in outline, colour tells them apart).</summary>
    private const float MinRigidDistance = 0.185f;
    /// <summary>T307: a siege engine fills at least this many pixels of the 40x56 mask (the old Cannon and Catapult were small bars of 300 to 400 pixels; the new ones 550 to 1100).</summary>
    private const int MinSiegeCoverage = 500;
    /// <summary>T309: the factor of <see cref="Masks"/>' resolution for the fine checks (120x168): thin props (a spear tip, an antenna, the Mohawk) survive it.</summary>
    private const int FineScale = 3;
    /// <summary>T309: a foot unit against any rider or rigid model (measured minimum 0.348: Battering Ram against the African Forest Elephant; foot against rider 0.378).</summary>
    private const float MinKindDistance = 0.34f;
    /// <summary>T309: two rigid units at the fine resolution (measured minimum 0.182 after T324).</summary>
    private const float MinFineRigidDistance = 0.17f;
    /// <summary>T309: the outline distance (fine masks) plus the distance of the mean material colours (civ colour left out): the mask is colour-blind, so two rigid units alike in outline must differ in colour.</summary>
    private const float MinOutlinePlusColour = 0.23f;
    /// <summary>T307: the civ's colour may cover at most this share of a rigid model's surface (the old Battering Ram was a flat team-coloured roof seen from above).</summary>
    private const float MaxTeamColourShare = 0.35f;
    /// <summary>T369: any two foot units as pictures (z-buffered vertex colours at 40x56, the civ colour neutral; measured minimum 0.238, Archer and Composite Bowman).</summary>
    private const float MinPictureDistance = 0.23f;
    /// <summary>T369: inside the groups the owner named as look-alikes (the barbarian raiders, the armoured shield infantry, the great people, the modern riflemen, the five foot infantry of similar eras).</summary>
    private const float MinGroupPictureDistance = 0.27f;
    /// <summary>T369: the five ancient galleys: outline plus mean colour at 120x168.</summary>
    private const float MinGalleyDistance = 0.30f;
    /// <summary>T373: any two aircraft (Zero, Great War Bomber, Fighter, Bomber, B17, Triplane, jets) at 120x168: outline alone (measured minimum 0.182, Zero and Bomber; the four were 0.17 apart before) and outline plus mean colour (measured minimum 0.43, Bomber and Stealth Bomber).</summary>
    private const float MinAircraftOutline = 0.18f;
    private const float MinAircraftOutlinePlusColour = 0.42f;
    /// <summary>T373: the mean colour of the Destroyer, Helicopter Gunship, Panzer and Fighter at least this far (linear RGB) from the grass tile colour (measured minimum 0.26, Panzer).</summary>
    private const float MinGrassContrast = 0.25f;
    private static readonly Vector3 GrassLinear = new(0.2f, 0.5f, 0.2f);
    private const float MinPanzerColourDistance = 0.15f;
    /// <summary>T373: Maori Warrior, Spearman, Samurai, Rifleman, Great Merchant and Minuteman as pictures (T382: bar raised from 0.45 to 0.48, measured minimum 0.496 Rifleman and Minuteman after the Samurai turned magenta; before: the same hues, bar 0.27).</summary>
    private const float MinT373PictureDistance = 0.48f;
    private static readonly string[] Siege = ["Battering Ram", "Catapult", "Ballista", "Trebuchet", "Hwach'a", "Cannon"];

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

    private sealed record Unit(string Name, string Style, string Look, string Mount, Vector3 Horse = default);

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
        // T315: the colour of the mount's coat (the generated horse's default when attachments.json gives none)
        static Vector3 HorseOf(JsonElement unit) =>
            unit.TryGetProperty("mount", out var mount) && mount.TryGetProperty("colour", out var c) ? new Vector3(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle()) : new Vector3(0.3f, 0.2f, 0.13f);
        List<Unit> Of(JsonElement units) => [.. units.EnumerateObject().Where(it => it.Value.TryGetProperty("style", out _)).Select(it =>
            new Unit(it.Name, it.Value.GetProperty("style").GetString()!, it.Value.TryGetProperty("look", out var l) ? l.GetString()! : "", MountOf(it.Value), HorseOf(it.Value)))];
        return (Of(root.GetProperty("units")), Of(root.GetProperty("mounted").GetProperty("units")));
    }

    private static readonly Dictionary<(string, int), bool[][]> MaskCache = [];

    /// <summary>The four masks of a unit, at <paramref name="scale"/> times the 40x56 base resolution (T309: the 3x masks see the thin props, spear tips, antennas and the Mohawk, that 40x56 smears away). Cached: the tests share them.</summary>
    private static bool[][] Masks(string unit, int scale = 1)
    {
        lock (MaskCache)
        {
            if (!MaskCache.TryGetValue((unit, scale), out var masks)) MaskCache[(unit, scale)] = masks = BuildMasks(unit, scale);
            return masks;
        }
    }

    private static bool[][] BuildMasks(string unit, int scale)
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
            masks[side] = Rasterise(triangles.Select(t => (Project(Vector3.Transform(t.A, yaw)), Project(Vector3.Transform(t.B, yaw)), Project(Vector3.Transform(t.C, yaw)))), scale);
        }
        return masks;
    }

    private static Vector2 Project(Vector3 p) => new(p.X, p.Y * MathF.Cos(Pitch) - p.Z * MathF.Sin(Pitch));

    private static bool[] Rasterise(IEnumerable<(Vector2 A, Vector2 B, Vector2 C)> tris, int scale)
    {
        int w = W * scale, h = H * scale;
        // a fixed scale: one world unit is 1/1.15 of the mask's height, so a taller or wider unit really covers more of it; the origin is the middle of the feet
        var mask = new bool[w * h];
        float Sx(float x) => w / 2f + x * (h / 1.15f);
        float Sy(float y) => h * 0.78f - y * (h / 1.15f);
        foreach (var (a, b, c) in tris)
        {
            var pa = new Vector2(Sx(a.X), Sy(a.Y));
            var pb = new Vector2(Sx(b.X), Sy(b.Y));
            var pc = new Vector2(Sx(c.X), Sy(c.Y));
            var x0 = global::System.Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X))));
            var x1 = global::System.Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
            var y0 = global::System.Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y))));
            var y1 = global::System.Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var d1 = Edge(pa, pb, p);
                    var d2 = Edge(pb, pc, p);
                    var d3 = Edge(pc, pa, p);
                    var inside = !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
                    if (inside || TouchesPixel(pa, pb, pc, x, y)) mask[y * w + x] = true;
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

    private static readonly Dictionary<string, (Vector3[] Colour, float[] Depth)[]> ColourCache = [];

    /// <summary>T369: what the eye sees at the default zoom is colour first. The unit's four views as z-buffered pictures at 40x56: the vertex colour (COLOR_0 times the material factor) of the nearest surface, the civ colour
    /// (TeamColor, every civ differs) as neutral grey, and the depth along the camera; an empty pixel has depth -1.</summary>
    private static (Vector3[] Colour, float[] Depth)[] ColourViews(string unit)
    {
        lock (ColourCache)
        {
            if (ColourCache.TryGetValue(unit, out var cached)) return cached;
            var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit + ".glb"));
            var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
            using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
            var root = doc.RootElement;
            var binStart = 20 + jsonLength + 8;
            var accessors = root.GetProperty("accessors");
            var views = root.GetProperty("bufferViews");
            var tris = new List<(Vector3 A, Vector3 B, Vector3 C, Vector3 Ca, Vector3 Cb, Vector3 Cc)>();
            var pending = new Stack<(int, Matrix4x4)>();
            foreach (var n in root.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray()) pending.Push((n.GetInt32(), Matrix4x4.Identity));
            while (pending.Count > 0)
            {
                var (index, parent) = pending.Pop();
                var node = root.GetProperty("nodes")[index];
                var world = Local(node) * parent;
                if (node.TryGetProperty("mesh", out var mesh))
                {
                    var matrix = node.TryGetProperty("skin", out _) ? parent : world;
                    foreach (var prim in root.GetProperty("meshes")[mesh.GetInt32()].GetProperty("primitives").EnumerateArray())
                    {
                        var material = root.GetProperty("materials")[prim.GetProperty("material").GetInt32()];
                        var isTeam = material.GetProperty("name").GetString() == "TeamColor";
                        var factor = material.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.TryGetProperty("baseColorFactor", out var f) ? new Vector3(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle()) : Vector3.One;
                        var attributes = prim.GetProperty("attributes");
                        var pos = ReadVec3(accessors[attributes.GetProperty("POSITION").GetInt32()], views, glb, binStart);
                        var colours = isTeam || !attributes.TryGetProperty("COLOR_0", out var ca) ? null : ReadColours(accessors[ca.GetInt32()], views, glb, binStart);
                        Vector3 ColourOf(int i) => isTeam ? new Vector3(0.5f) : (colours == null ? Vector3.One : colours[i]) * factor;
                        var idx = ReadIndices(accessors[prim.GetProperty("indices").GetInt32()], views, glb, binStart);
                        for (var i = 0; i + 2 < idx.Length; i += 3)
                            tris.Add((Vector3.Transform(pos[idx[i]], matrix), Vector3.Transform(pos[idx[i + 1]], matrix), Vector3.Transform(pos[idx[i + 2]], matrix), ColourOf(idx[i]), ColourOf(idx[i + 1]), ColourOf(idx[i + 2])));
                    }
                }
                if (node.TryGetProperty("children", out var children)) foreach (var c in children.EnumerateArray()) pending.Push((c.GetInt32(), world));
            }
            var result = new (Vector3[], float[])[4];
            for (var side = 0; side < 4; side++)
            {
                var yaw = Matrix4x4.CreateRotationY(side * MathF.PI / 2f);
                var colour = new Vector3[W * H];
                var depth = new float[W * H];
                Array.Fill(depth, -1f);
                var seen = new float[W * H];
                Array.Fill(seen, float.NegativeInfinity);
                foreach (var t in tris)
                {
                    var a = Vector3.Transform(t.A, yaw);
                    var b = Vector3.Transform(t.B, yaw);
                    var c = Vector3.Transform(t.C, yaw);
                    Vector2 Screen(Vector3 v) { var p = Project(v); return new Vector2(W / 2f + p.X * (H / 1.15f), H * 0.78f - p.Y * (H / 1.15f)); }
                    float Near(Vector3 v) => v.Z * MathF.Cos(Pitch) + v.Y * MathF.Sin(Pitch);
                    var (sa, sb, sc) = (Screen(a), Screen(b), Screen(c));
                    var area = Edge(sa, sb, sc);
                    if (MathF.Abs(area) < 1e-6f) continue;
                    var x0 = global::System.Math.Max(0, (int)MathF.Floor(MathF.Min(sa.X, MathF.Min(sb.X, sc.X))));
                    var x1 = global::System.Math.Min(W - 1, (int)MathF.Ceiling(MathF.Max(sa.X, MathF.Max(sb.X, sc.X))));
                    var y0 = global::System.Math.Max(0, (int)MathF.Floor(MathF.Min(sa.Y, MathF.Min(sb.Y, sc.Y))));
                    var y1 = global::System.Math.Min(H - 1, (int)MathF.Ceiling(MathF.Max(sa.Y, MathF.Max(sb.Y, sc.Y))));
                    for (var y = y0; y <= y1; y++)
                        for (var x = x0; x <= x1; x++)
                        {
                            var p = new Vector2(x + 0.5f, y + 0.5f);
                            var w0 = Edge(sb, sc, p) / area;
                            var w1 = Edge(sc, sa, p) / area;
                            var w2 = 1f - w0 - w1;
                            if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                            var near = w0 * Near(a) + w1 * Near(b) + w2 * Near(c);
                            if (near <= seen[y * W + x]) continue;
                            seen[y * W + x] = near;
                            depth[y * W + x] = near;
                            colour[y * W + x] = w0 * t.Ca + w1 * t.Cb + w2 * t.Cc;
                        }
                }
                result[side] = (colour, depth);
            }
            return ColourCache[unit] = result;
        }
    }

    private static Vector3[] ReadColours(JsonElement accessor, JsonElement views, byte[] glb, int binStart)
    {
        var offset = Offset(accessor, views, binStart);
        var components = accessor.GetProperty("type").GetString() == "VEC4" ? 4 : 3;
        var bytes = accessor.GetProperty("componentType").GetInt32() == 5121; // normalised unsigned bytes (what the unit builders write) or floats
        var stride = components * (bytes ? 1 : 4);
        float Channel(int i, int k) => bytes ? glb[offset + i * stride + k] / 255f : BitConverter.ToSingle(glb, offset + i * stride + k * 4);
        return [.. Enumerable.Range(0, accessor.GetProperty("count").GetInt32()).Select(i => new Vector3(Channel(i, 0), Channel(i, 1), Channel(i, 2)))];
    }

    /// <summary>T369: the distance of two units as the eye sees them: over the pixels either unit covers (four views, 40x56), the mean colour difference (0 to 1; the grass behind a pixel only one covers counts as a full 1).</summary>
    private static float PictureDistance(string a, string b)
    {
        var (va, vb) = (ColourViews(a), ColourViews(b));
        return Enumerable.Range(0, 4).Average(side =>
        {
            var sum = 0f;
            var union = 0;
            for (var i = 0; i < W * H; i++)
            {
                var (ina, inb) = (va[side].Depth[i] >= 0f ? 1 : 0, vb[side].Depth[i] >= 0f ? 1 : 0);
                if (ina + inb == 0) continue;
                union++;
                sum += ina + inb == 2 ? MathF.Min(1f, Vector3.Distance(va[side].Colour[i], vb[side].Colour[i]) / 1.2f) : 1f;
            }
            return union == 0 ? 0f : sum / union;
        });
    }

    /// <summary>1 - intersection over union of two masks, averaged over the four sides.</summary>
    private static float Distance(bool[][] a, bool[][] b) => Enumerable.Range(0, 4).Average(side =>
    {
        var inter = 0;
        var union = 0;
        for (var i = 0; i < a[side].Length; i++)
        {
            if (a[side][i] && b[side][i]) inter++;
            if (a[side][i] || b[side][i]) union++;
        }
        return union == 0 ? 0f : 1f - (float)inter / union;
    });

    private static List<string> Failures(List<Unit> units, Func<Unit, Unit, float> need, int scale = 1)
    {
        var masks = units.ToDictionary(u => u.Name, u => Masks(u.Name, scale));
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
        Assert.Equal(57, foot.Count);
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
        Assert.Equal(21, mounted.Count);
        Assert.True(mounted.Select(u => u.Style + "/" + u.Mount).Distinct().Count() >= 15, "each trapping of a mount is a class of its own");
        var failures = Failures(mounted, (_, _) => MinRiderDistance);
        Assert.True(failures.Count == 0, "rider silhouettes too alike:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void RidersWithAlikeOutlinesHaveMountsOfDifferentCoats()
    {
        var (_, mounted) = Units();
        var masks = mounted.ToDictionary(u => u.Name, u => Masks(u.Name));
        var failures = new List<string>();
        foreach (var a in mounted)
            foreach (var b in mounted.Where(b => string.CompareOrdinal(b.Name, a.Name) > 0))
            {
                var outline = Distance(masks[a.Name], masks[b.Name]);
                var coat = Vector3.Distance(a.Horse, b.Horse);
                if (outline < OutlineEnough && coat < MinCoatDistance)
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{a.Name} and {b.Name}: outlines differ by {outline:0.000} and coats by {coat:0.000}"));
            }
        Assert.True(failures.Count == 0, "riders told apart by neither outline nor coat:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ThePikeIsMuchLongerThanTheSpear()
    {
        Assert.True(Reach("Pikeman") >= Reach("Spearman") * PikeOverSpear, $"pike {Reach("Pikeman"):0.00} against spear {Reach("Spearman"):0.00}");
        Assert.True(Reach("Pikeman") >= Reach("Hoplite") * PikeOverSpear, "pike against the hoplite's spear");
    }

    [Fact]
    public void CrewGunsStandOnTheirOwnCarriageAndAreNotHeld()
    {
        var (foot, _) = Units();
        var crew = foot.Where(u => u.Style == "crew").ToList();
        Assert.True(crew.Count >= 5, "the crew weapons");
        foreach (var unit in crew)
        {
            var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit.Name + ".glb"));
            using var doc = JsonDocument.Parse(glb.AsMemory(20, BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12))));
            var names = doc.RootElement.GetProperty("nodes").EnumerateArray().Select(n => n.TryGetProperty("name", out var nm) ? nm.GetString() : null).ToList();
            Assert.DoesNotContain("Gun", names);
            Assert.True(Reach(unit.Name) > 0.45f, $"{unit.Name}: the carriage stands out in front of the gunner");
        }
    }

    [Theory]
    [InlineData("Mohawk Warrior", "Pictish Warrior", 0.25f)]
    [InlineData("Longbowman", "Samurai", 0.30f)]
    [InlineData("Hussar", "Cossack", 0.25f)]
    [InlineData("Infantry", "Rifleman", 0.12f)]
    public void TheConfusablePairsOfTheT310ReviewAreWellApart(string a, string b, float need) =>
        Assert.True(Distance(Masks(a), Masks(b)) >= need, $"{a} and {b} differ by {Distance(Masks(a), Masks(b)):0.000}, need {need:0.00}");

    /// <summary>The farthest a vertex of the bind pose stands from the unit's origin, sideways (what the contract's radius measures).</summary>
    private static float Reach(string unit)
    {
        var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit + ".glb"));
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        return Triangles(doc.RootElement, glb, 20 + jsonLength + 8).Max(t => new[] { t.A, t.B, t.C }.Max(v => MathF.Max(MathF.Abs(v.X), MathF.Abs(v.Z))));
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

    /// <summary>T307: every unit with a rigid model (attachments.json "rigid").</summary>
    private static List<Unit> RigidUnits()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack, "attachments.json")));
        return [.. data.RootElement.GetProperty("rigid").GetProperty("units").EnumerateObject().Select(it => new Unit(it.Name, it.Value.TryGetProperty("build", out var b) ? b.GetString()! : "", "", ""))];
    }

    /// <summary>Surface area per material name of a model (a triangle's area does not depend on its node's translation).</summary>
    private static Dictionary<string, float> MaterialAreas(string unit)
    {
        var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit + ".glb"));
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = doc.RootElement;
        var binStart = 20 + jsonLength + 8;
        var areas = new Dictionary<string, float>();
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var pos = ReadVec3(root.GetProperty("accessors")[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()], root.GetProperty("bufferViews"), glb, binStart);
                var idx = ReadIndices(root.GetProperty("accessors")[prim.GetProperty("indices").GetInt32()], root.GetProperty("bufferViews"), glb, binStart);
                var name = root.GetProperty("materials")[prim.GetProperty("material").GetInt32()].GetProperty("name").GetString()!;
                var area = 0f;
                for (var i = 0; i + 2 < idx.Length; i += 3)
                    area += Vector3.Cross(pos[idx[i + 1]] - pos[idx[i]], pos[idx[i + 2]] - pos[idx[i]]).Length() / 2f;
                areas[name] = areas.GetValueOrDefault(name) + area;
            }
        return areas;
    }

    /// <summary>T309: the area-weighted mean base colour of a model's materials without the civ colour (a colour-aware companion of the colour-blind mask).</summary>
    private static Vector3 MeanColour(string unit)
    {
        var glb = File.ReadAllBytes(Path.Combine(Pack, "models", "unit", unit + ".glb"));
        var jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        using var doc = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = doc.RootElement;
        var binStart = 20 + jsonLength + 8;
        var sum = Vector3.Zero;
        var total = 0f;
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
            foreach (var prim in mesh.GetProperty("primitives").EnumerateArray())
            {
                var material = root.GetProperty("materials")[prim.GetProperty("material").GetInt32()];
                if (material.GetProperty("name").GetString() == "TeamColor") continue;
                var factor = material.TryGetProperty("pbrMetallicRoughness", out var pbr) && pbr.TryGetProperty("baseColorFactor", out var f) ? new Vector3(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle()) : Vector3.One;
                var pos = ReadVec3(root.GetProperty("accessors")[prim.GetProperty("attributes").GetProperty("POSITION").GetInt32()], root.GetProperty("bufferViews"), glb, binStart);
                var idx = ReadIndices(root.GetProperty("accessors")[prim.GetProperty("indices").GetInt32()], root.GetProperty("bufferViews"), glb, binStart);
                var area = 0f;
                for (var i = 0; i + 2 < idx.Length; i += 3) area += Vector3.Cross(pos[idx[i + 1]] - pos[idx[i]], pos[idx[i + 2]] - pos[idx[i]]).Length() / 2f;
                sum += factor * area;
                total += area;
            }
        return total == 0f ? Vector3.Zero : sum / total;
    }

    [Fact]
    public void EveryRigidUnitIsToldApartFromEveryOther()
    {
        var rigid = RigidUnits();
        Assert.Equal(45, rigid.Count);
        var failures = Failures(rigid, (_, _) => MinRigidDistance);
        Assert.True(failures.Count == 0, "rigid silhouettes too alike:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void NoRigidModelIsAFlatBlockOfTheCivColour()
    {
        var failures = new List<string>();
        foreach (var unit in RigidUnits())
        {
            var areas = MaterialAreas(unit.Name);
            var share = areas.GetValueOrDefault("TeamColor") / areas.Values.Sum();
            if (share > MaxTeamColourShare) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{unit.Name}: the civ colour covers {share:0.00} of the surface"));
            if (areas.Keys.Count(k => k != "TeamColor") < 2) failures.Add($"{unit.Name}: a single plain material");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EverySiegeEngineIsBigAndHasItsOwnColour()
    {
        var failures = new List<string>();
        var dominant = new Dictionary<string, string>();
        foreach (var name in Siege)
        {
            var pixels = Masks(name).Average(m => m.Count(on => on));
            if (pixels < MinSiegeCoverage) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name} covers only {pixels:0} pixels, need {MinSiegeCoverage}"));
            var areas = MaterialAreas(name);
            dominant[name] = areas.Where(it => it.Key != "TeamColor").MaxBy(it => it.Value).Key;
        }
        foreach (var group in dominant.GroupBy(it => it.Value).Where(g => g.Count() > 1))
            failures.Add($"{string.Join(" and ", group.Select(it => it.Key))} have the same main colour {group.Key}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void ADecisiveFailureIsReported()
    {
        // the check can fail: a unit against itself has distance 0, which is below every threshold
        var masks = Masks("Settler");
        Assert.True(Distance(masks, masks) < MinLookDistance);
        Assert.True(Distance(masks, Masks("Archer")) > 0f);
        Assert.True(Distance(Masks("Rifleman"), Masks("Marine")) >= MinLookDistance, "two units of one style are told apart");
        // the fine and foot-against-rider checks can fail too: a unit is as far from itself as a flat colour from itself, and the thin-prop masks differ from the coarse ones
        Assert.True(Distance(Masks("Settler", FineScale), Masks("Settler", FineScale)) + Vector3.Distance(MeanColour("Settler"), MeanColour("Settler")) < MinOutlinePlusColour);
        Assert.True(Distance(Masks("Settler"), Masks("Settler")) < MinKindDistance);
        Assert.NotEqual(Masks("Mohawk Warrior")[0].Length, Masks("Mohawk Warrior", FineScale)[0].Length);
    }

    /// <summary>T309: the contents of attachments.json by group (humanoids, riders, rigid models).</summary>
    private static (JsonElement Foot, JsonElement Mounted, JsonElement Rigid) Groups(JsonDocument data)
    {
        var root = data.RootElement;
        return (root.GetProperty("units"), root.GetProperty("mounted").GetProperty("units"), root.GetProperty("rigid").GetProperty("units"));
    }

    [Fact]
    public void EveryHumanoidAndMountedUnitHasAStyleAndEveryModelBelongsToOneUnit()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Pack, "attachments.json")));
        var (foot, mounted, rigid) = Groups(data);
        var unstyled = foot.EnumerateObject().Concat(mounted.EnumerateObject()).Where(it => !it.Value.TryGetProperty("style", out var st) || string.IsNullOrEmpty(st.GetString())).Select(it => it.Name).ToList();
        Assert.True(unstyled.Count == 0, "humanoid or mounted units without a style (the silhouette tests would skip them): " + string.Join(", ", unstyled));
        Assert.Equal(57, foot.EnumerateObject().Count());
        Assert.Equal(21, mounted.EnumerateObject().Count());
        Assert.Equal(45, rigid.EnumerateObject().Count());
        var named = foot.EnumerateObject().Concat(mounted.EnumerateObject()).Concat(rigid.EnumerateObject()).Select(it => it.Name).ToList();
        Assert.Equal(named.Count, named.Distinct().Count());
        var models = Directory.GetFiles(Path.Combine(Pack, "models", "unit"), "*.glb").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal([.. named.OrderBy(n => n, StringComparer.Ordinal)], models);
    }

    [Fact]
    public void EveryFootUnitIsToldApartFromEveryRiderAndRigidUnit()
    {
        var (foot, mounted) = Units();
        var failures = new List<string>();
        foreach (var a in foot)
            foreach (var b in mounted.Concat(RigidUnits()))
            {
                var d = Distance(Masks(a.Name), Masks(b.Name));
                if (d < MinKindDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{a.Name} (on foot) and {b.Name} differ by {d:0.000}, need {MinKindDistance:0.00}"));
            }
        Assert.True(failures.Count == 0, "a foot unit that looks like a rider or a machine:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ThinPropsStillTellUnitsApartAtThreeTimesTheResolution()
    {
        var (foot, mounted) = Units();
        var failures = Failures(foot, (a, b) => IsCivilian(a.Style) != IsCivilian(b.Style) ? MinCivilianDistance : a.Style != b.Style ? MinDistance : MinLookDistance, FineScale);
        failures.AddRange(Failures(mounted, (_, _) => MinRiderDistance, FineScale));
        failures.AddRange(Failures(RigidUnits(), (_, _) => MinFineRigidDistance, FineScale));
        Assert.True(failures.Count == 0, "silhouettes too alike at 120x168:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void RigidUnitsAreToldApartByOutlineAndColourTogether()
    {
        var rigid = RigidUnits();
        var failures = new List<string>();
        foreach (var a in rigid)
            foreach (var b in rigid.Where(b => string.CompareOrdinal(b.Name, a.Name) > 0))
            {
                var outline = Distance(Masks(a.Name, FineScale), Masks(b.Name, FineScale));
                var colour = Vector3.Distance(MeanColour(a.Name), MeanColour(b.Name));
                if (outline + colour < MinOutlinePlusColour) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{a.Name} and {b.Name}: outline {outline:0.000} plus colour {colour:0.000}, need {MinOutlinePlusColour:0.00}"));
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryFootUnitIsToldApartFromEveryOtherByColourAsWellAsOutline()
    {
        var (foot, _) = Units();
        var failures = new List<string>();
        foreach (var a in foot)
            foreach (var b in foot.Where(b => string.CompareOrdinal(b.Name, a.Name) > 0))
            {
                var d = PictureDistance(a.Name, b.Name);
                if (d < MinPictureDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{a.Name} and {b.Name} look alike as pictures: {d:0.000}, need {MinPictureDistance:0.00}"));
            }
        Assert.True(failures.Count == 0, "units too alike in colour and shape:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void TheLookAlikeGroupsOfT369AreToldApartAtFirstSight()
    {
        // Warrior, Marauder, Brute, Jaguar, Pictish, Maori and Mohawk; the armoured shield infantry; the modern riflemen; the great people; the ancient galleys: at least 0.27 as pictures (rigid: outline plus colour)
        string[][] groups =
        [
            ["Warrior", "Marauder", "Brute", "Jaguar", "Pictish Warrior", "Maori Warrior", "Mohawk Warrior"],
            ["Spearman", "Hoplite", "Legion", "Persian Immortal", "Swordsman", "Longswordsman", "Samurai", "Landsknecht", "Pikeman"],
            ["Great Artist", "Great Merchant", "Great Engineer", "Great Prophet", "Great General", "Great Scientist", "Missionary", "Inquisitor", "Settler"],
            ["Mehal Sefari", "Foreign Legion", "Paratrooper", "Great War Infantry", "Marine", "Infantry", "Rifleman", "Mechanized Infantry"],
            ["Musketman", "Minuteman", "Rifleman", "Swordsman", "Longswordsman"],
        ];
        var failures = new List<string>();
        foreach (var group in groups)
            for (var i = 0; i < group.Length; i++)
                for (var j = i + 1; j < group.Length; j++)
                {
                    var d = PictureDistance(group[i], group[j]);
                    if (d < MinGroupPictureDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{group[i]} and {group[j]}: {d:0.000}, need {MinGroupPictureDistance:0.00}"));
                }
        string[] galleys = ["Trireme", "Galley", "Quinquereme", "Dromon", "Galleass"];
        for (var i = 0; i < galleys.Length; i++)
            for (var j = i + 1; j < galleys.Length; j++)
            {
                var d = Distance(Masks(galleys[i], FineScale), Masks(galleys[j], FineScale)) + Vector3.Distance(MeanColour(galleys[i]), MeanColour(galleys[j]));
                if (d < MinGalleyDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{galleys[i]} and {galleys[j]}: outline plus colour {d:0.000}, need {MinGalleyDistance:0.00}"));
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TheAircraftAreToldApartByOutlineAndByColour()
    {
        // T373: Zero (white ellipse), Great War Bomber (orange biplane), Fighter (sky-blue twin boom, an H from above), Bomber (dark green), B17 (silver, four engines), Triplane (red), Jet Fighter, Stealth Bomber
        string[] air = ["Zero", "Great War Bomber", "Fighter", "Bomber", "B17", "Triplane", "Jet Fighter", "Stealth Bomber"];
        var failures = new List<string>();
        for (var i = 0; i < air.Length; i++)
            for (var j = i + 1; j < air.Length; j++)
            {
                var outline = Distance(Masks(air[i], FineScale), Masks(air[j], FineScale));
                var colour = Vector3.Distance(MeanColour(air[i]), MeanColour(air[j]));
                if (outline < MinAircraftOutline || outline + colour < MinAircraftOutlinePlusColour)
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{air[i]} and {air[j]}: outline {outline:0.000} (need {MinAircraftOutline:0.00}), outline plus colour {outline + colour:0.000} (need {MinAircraftOutlinePlusColour:0.00})"));
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TheWeakUnitsOfT373StandOutFromGrassAndFromTheirNeighbours()
    {
        var failures = new List<string>();
        foreach (var unit in new[] { "Destroyer", "Helicopter Gunship", "Panzer", "Fighter" })
        {
            var d = Vector3.Distance(MeanColour(unit), GrassLinear);
            if (d < MinGrassContrast) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{unit} is too close to the grass: {d:0.000}, need {MinGrassContrast:0.00}"));
        }
        // the Panzer was olive like the Rocket Artillery, the Destroyer pale blue like nothing else but low contrast
        var panzer = Vector3.Distance(MeanColour("Panzer"), MeanColour("Rocket Artillery"));
        if (panzer < MinPanzerColourDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"Panzer and Rocket Artillery share a colour: {panzer:0.000}, need {MinPanzerColourDistance:0.00}"));
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void TheSameHueUnitsOfT373AreToldApartAsPictures()
    {
        // Maori Warrior against Spearman were both cyan; Samurai, Rifleman and the Great Merchant all purplish; Rifleman and Minuteman small and low contrast
        string[] group = ["Maori Warrior", "Spearman", "Samurai", "Rifleman", "Great Merchant", "Minuteman"];
        var failures = new List<string>();
        for (var i = 0; i < group.Length; i++)
            for (var j = i + 1; j < group.Length; j++)
            {
                var d = PictureDistance(group[i], group[j]);
                if (d < MinT373PictureDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{group[i]} and {group[j]}: {d:0.000}, need {MinT373PictureDistance:0.00}"));
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>T382: Rifleman, Minuteman and Destroyer (small figures, a thin needle) cover at least this many mask pixels (40x56, mean of four views).</summary>
    private const int MinT382FootCoverage = 520;
    private const int MinT382ShipCoverage = 320;

    [Fact]
    public void TheSmallFiguresOfT382AreBigEnough()
    {
        var failures = new List<string>();
        foreach (var name in new[] { "Rifleman", "Minuteman", "Destroyer" })
        {
            var pixels = Masks(name).Average(m => m.Count(on => on));
            var need = name == "Destroyer" ? MinT382ShipCoverage : MinT382FootCoverage;
            if (pixels < need) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name} covers only {pixels:0} pixels, need {need}"));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>T383: the Panzer's mean colour at least this far (linear RGB) from the other tanks (Tank, Modern Armor, Landship) and from Rocket Artillery (measured minimum 0.129, Landship).</summary>
    private const float MinT383PanzerColourDistance = 0.12f;

    /// <summary>T383: a Panzer is built of at least this many materials (hull, turret, tracks, wheels, deck: the T373 slab had two).</summary>
    private const int MinT383PanzerMaterials = 5;

    [Fact]
    public void ThePanzerOfT383IsABrownTankWithDetailNotAGreyAndSandSlab()
    {
        var failures = new List<string>();
        foreach (var other in new[] { "Tank", "Modern Armor", "Landship", "Rocket Artillery" })
        {
            var d = Vector3.Distance(MeanColour("Panzer"), MeanColour(other));
            if (d < MinT383PanzerColourDistance) failures.Add(string.Create(CultureInfo.InvariantCulture, $"Panzer and {other} share a colour: {d:0.000}, need {MinT383PanzerColourDistance:0.00}"));
        }
        var materials = MaterialAreas("Panzer").Count;
        if (materials < MinT383PanzerMaterials) failures.Add($"Panzer has only {materials} materials, need {MinT383PanzerMaterials}");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>T383: foot figures (skeleton, no mount) are drawn at least this much larger than their modelled size (UnitNode applies it; the harness unit-visuals scenario checks the nodes).</summary>
    [Fact]
    public void FootFiguresAreDrawnLargerThanTheyAreModelled() => Assert.True(Unciv.Assets.UnitVisuals.FootFigureBoost >= 1.25f);

    /// <summary>T383: every foot figure, as drawn (mask pixels of the model times the square of <see cref="Unciv.Assets.UnitVisuals.FootFigureBoost"/>), covers at least this many pixels of the 40x56 frame (measured minimum 634, Crossbowman).</summary>
    private const int MinT383DrawnFootCoverage = 600;

    [Fact]
    public void EveryFootFigureAsDrawnIsBigEnough()
    {
        var failures = new List<string>();
        foreach (var name in Units().Foot.Select(it => it.Name).Concat(["Rifleman", "Minuteman", "Musketman"]).Distinct())
        {
            var pixels = Masks(name).Average(m => m.Count(on => on)) * Unciv.Assets.UnitVisuals.FootFigureBoost * Unciv.Assets.UnitVisuals.FootFigureBoost;
            if (pixels < MinT383DrawnFootCoverage) failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name} covers {pixels:0} pixels as drawn, need {MinT383DrawnFootCoverage}"));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }
}
