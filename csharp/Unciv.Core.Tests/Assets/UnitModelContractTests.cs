using System.Text;
using System.Text.Json;
using Unciv.Assets;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C080: every <c>.glb</c> shipped in a pack satisfies the unit model contract, the bundled Warrior is one of them and is what <c>unit/Warrior</c> resolves to, and the checker rejects each way of breaking the contract.</summary>
[Collection("RulesetCache")]
public sealed class UnitModelContractTests
{
    private static readonly string ClientFolder = FindClientFolder();

    private static string FindClientFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Unciv.Godot");
            if (File.Exists(Path.Combine(candidate, "Packs", "unciv", "manifest.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("Unciv.Godot/Packs/unciv not found above " + AppContext.BaseDirectory);
    }

    private static IEnumerable<string> ShippedModels() =>
        new[] { Path.Combine(ClientFolder, "Packs"), Path.Combine(ClientFolder, "assets"), Path.Combine(ClientFolder, "..", "Unciv.Godot.Harness", "assets") }
            .Where(Directory.Exists).SelectMany(it => Directory.EnumerateFiles(it, "*.glb", SearchOption.AllDirectories)).Order(StringComparer.Ordinal);

    [Fact]
    public void EveryShippedModelSatisfiesTheContract()
    {
        var models = ShippedModels().ToList();
        Assert.NotEmpty(models);
        foreach (var path in models) Assert.True(UnitModelContract.Check(File.ReadAllBytes(path)).Count == 0, path + ": " + string.Join("; ", UnitModelContract.Check(File.ReadAllBytes(path))));
    }

    private static JsonElement UnitsManifestKeys()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Packs", "units", "manifest.json")));
        return manifest.RootElement.GetProperty("keys").Clone();
    }

    /// <summary>C095: the units of the pack whose model is rigid (no skeleton, no clips; they move through <c>RigidMotion</c>).</summary>
    private static List<string> RigidUnits()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Packs", "units", "attachments.json")));
        return data.RootElement.GetProperty("rigid").GetProperty("units").EnumerateObject().Select(it => it.Name).ToList();
    }

    /// <summary>T256: the rigid units built by <c>build-rigid-models.py</c> (the entries with a <c>build</c> archetype; the ram is C095's).</summary>
    private static List<string> GeneratedRigidUnits()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Packs", "units", "attachments.json")));
        return data.RootElement.GetProperty("rigid").GetProperty("units").EnumerateObject().Where(it => it.Value.TryGetProperty("build", out _)).Select(it => it.Name).ToList();
    }

    [Fact]
    public void EveryUnitOfTheUnitsPackIsInTheManifestAndKeepsTheSixClips()
    {
        var keys = UnitsManifestKeys().EnumerateObject().ToList();
        Assert.Contains(keys, it => it.Name == "unit/Warrior");
        foreach (var key in keys)
        {
            var bytes = File.ReadAllBytes(Path.Combine(ClientFolder, "Packs", "units", key.Value.GetString()!));
            Assert.Empty(UnitModelContract.Check(bytes));
            var doc = Glb(bytes);
            if (RigidUnits().Contains(key.Name["unit/".Length..]))
            {
                Assert.False(doc.TryGetProperty("animations", out _) || doc.TryGetProperty("skins", out _), key.Name + ": a rigid model has no clips and no skeleton (C095)");
                continue;
            }
            var animations = doc.GetProperty("animations").EnumerateArray().Select(it => it.GetProperty("name").GetString()!).ToList();
            Assert.Equal(UnitModelContract.ClipNames, animations);
        }
    }

    [Fact]
    public void TheAttachmentDataListsExactlyTheManifestUnitsAndTheirAttachmentsExist()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Packs", "units", "attachments.json")));
        var root = data.RootElement;
        var listed = root.GetProperty("units").EnumerateObject().Concat(root.GetProperty("mounted").GetProperty("units").EnumerateObject()).ToList();
        Assert.Equal(listed.Select(it => "unit/" + it.Name).Concat(RigidUnits().Select(it => "unit/" + it)).Order(StringComparer.Ordinal), UnitsManifestKeys().EnumerateObject().Select(it => it.Name).Order(StringComparer.Ordinal));
        foreach (var unit in listed)
        {
            var file = File.ReadAllBytes(Path.Combine(ClientFolder, "Packs", "units", "models", "unit", unit.Name + ".glb"));
            var meshNodes = Glb(file).GetProperty("nodes").EnumerateArray().Where(it => it.TryGetProperty("mesh", out _)).Select(it => it.GetProperty("name").GetString()).ToList();
            foreach (var keep in unit.Value.GetProperty("keep").EnumerateArray()) Assert.Contains(keep.GetString(), meshNodes);
        }
    }

    [Fact]
    public void AMountedUnitHasHorseAndRiderUnderOneSkeletonWithMergedClips()
    {
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Packs", "units", "attachments.json")));
        var mounted = data.RootElement.GetProperty("mounted").GetProperty("units").EnumerateObject().Select(it => it.Name).ToList();
        Assert.Contains("Cataphract", mounted);
        foreach (var name in mounted)
        {
            var doc = Glb(File.ReadAllBytes(Path.Combine(ClientFolder, "Packs", "units", "models", "unit", name + ".glb")));
            var nodes = doc.GetProperty("nodes").EnumerateArray().Select(it => it.GetProperty("name").GetString()).ToList();
            Assert.Contains("saddle", nodes);
            Assert.Contains("leg_fl", nodes);
            Assert.Contains("hips", nodes); // the rider's own skeleton
            Assert.Equal(2, doc.GetProperty("skins").GetArrayLength());
            Assert.Equal(["Horse", "TeamColor"], doc.GetProperty("materials").EnumerateArray().Select(it => it.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
            // every clip animates both the horse and the rider
            foreach (var clip in doc.GetProperty("animations").EnumerateArray())
            {
                var targets = clip.GetProperty("channels").EnumerateArray().Select(it => nodes[it.GetProperty("target").GetProperty("node").GetInt32()]).ToHashSet();
                Assert.True(targets.Overlaps(["body", "neck", "root", "leg_fl"]), clip.GetProperty("name").GetString());
                Assert.True(targets.Overlaps(["hips", "upperarm.l", "upperarm.r", "chest"]), clip.GetProperty("name").GetString());
            }
        }
    }

    /// <summary>Every Gods &amp; Kings unit, the great people, ships, siege, armour, aircraft, missiles and the helicopter included (C084, C093, C094, T256), has art; only the spaceship parts stay generated blocks; the Battering Ram has a rigid model (C095).</summary>
    [Fact]
    public void EveryHumanoidAndMountedUnitHasAModel()
    {
        RulesetCache.Clear();
        var jsons = Path.Combine(ClientFolder, "..", "..", "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        try
        {
            var ruleset = RulesetCache.Get("Civ V - Gods & Kings")!;
            var keys = UnitsManifestKeys().EnumerateObject().Select(it => it.Name).ToHashSet();
            var wanted = ruleset.Units.Values
                .Where(it => !it.Name.StartsWith("SS ", StringComparison.Ordinal)).Select(it => it.Name).Order(StringComparer.Ordinal).ToList();
            foreach (var rigid in new[] { "Trireme", "Catapult", "Tank", "Fighter", "Destroyer", "Helicopter Gunship", "Guided Missile", "Carrier", "Great Admiral" }) Assert.Contains(rigid, wanted);
            Assert.Contains("Settler", wanted);
            Assert.Contains("War Elephant", wanted);
            Assert.Contains("Marauder", wanted);
            Assert.Contains("Samurai", wanted);
            Assert.Contains("Hakkapeliitta", wanted);
            Assert.Contains("Naresuan's Elephant", wanted);
            Assert.Contains("Mechanized Infantry", wanted);
            Assert.Contains("Great Prophet", wanted);
            Assert.Contains("Cossack", wanted);
            Assert.Equal(wanted, wanted.Where(it => keys.Contains("unit/" + it)).ToList());
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    private static (List<string?> Nodes, JsonElement Doc) UnitModel(string name)
    {
        var doc = Glb(File.ReadAllBytes(Path.Combine(ClientFolder, "Packs", "units", "models", "unit", name + ".glb")));
        return (doc.GetProperty("nodes").EnumerateArray().Select(it => it.GetProperty("name").GetString()).ToList(), doc);
    }

    private static HashSet<string?> Animated(List<string?> nodes, JsonElement doc, string clip) =>
        doc.GetProperty("animations").EnumerateArray().Single(it => it.GetProperty("name").GetString() == clip).GetProperty("channels").EnumerateArray()
            .Select(it => nodes[it.GetProperty("target").GetProperty("node").GetInt32()]).ToHashSet();

    /// <summary>C095: the elephants have a trunk and ears of their own (moving in the clips) and the chariots a cart with two wheels that turn in the walk; the horse units have neither.</summary>
    [Theory]
    [InlineData("War Elephant", "trunk", "idle")]
    [InlineData("African Forest Elephant", "ear_l", "idle")]
    [InlineData("African Forest Elephant", "trunk", "attack")]
    [InlineData("War Chariot", "wheel_l", "walk")]
    [InlineData("Chariot Archer", "wheel_r", "walk")]
    public void ElephantsAndChariotsHaveTheirOwnAnimatedParts(string unit, string joint, string clip)
    {
        var (nodes, doc) = UnitModel(unit);
        Assert.Contains(joint, nodes);
        Assert.Contains(joint, Animated(nodes, doc, clip));
    }

    [Theory]
    [InlineData("Horseman")]
    [InlineData("Cataphract")]
    public void HorseUnitsHaveNoElephantOrChariotParts(string unit)
    {
        var (nodes, _) = UnitModel(unit);
        Assert.DoesNotContain("trunk", nodes);
        Assert.DoesNotContain("wheel_l", nodes);
    }

    /// <summary>C095: the Battering Ram is a model file with no skeleton and no clips, tinted by a TeamColor roof, so the client moves it as siege.</summary>
    [Fact]
    public void TheBatteringRamIsARigidModel()
    {
        Assert.Contains("Battering Ram", RigidUnits());
        Assert.Contains("unit/Battering Ram", UnitsManifestKeys().EnumerateObject().Select(it => it.Name));
        var (nodes, doc) = UnitModel("Battering Ram");
        Assert.Single(nodes);
        Assert.False(doc.TryGetProperty("skins", out _));
        Assert.False(doc.TryGetProperty("animations", out _));
        Assert.Contains("TeamColor", doc.GetProperty("materials").EnumerateArray().Select(it => it.GetProperty("name").GetString()));

    }

    /// <summary>T256: the rigid models have a TeamColor surface, no clips, and armour models expose the top-level turret <c>Part1</c> and gun <c>Part2</c> that <c>UnitNode</c> turns; the unit's rigid family is not <c>None</c>.</summary>
    [Fact]
    public void RigidModelsHaveNoClipsAndArmourExposesItsTurret()
    {
        RulesetCache.Clear();
        var jsons = Path.Combine(ClientFolder, "..", "..", "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        try
        {
            var ruleset = RulesetCache.Get("Civ V - Gods & Kings")!;
            var rigid = GeneratedRigidUnits();
            Assert.Equal(44, rigid.Count);
            foreach (var name in rigid)
            {
                var type = ruleset.Units[name].UnitType;
                var kind = RigidMotion.KindOf(type);
                Assert.True(kind != RigidKind.None, name + " (" + type + ") is not rigid");
                var doc = Glb(File.ReadAllBytes(Path.Combine(ClientFolder, "Packs", "units", "models", "unit", name + ".glb")));
                Assert.False(doc.TryGetProperty("animations", out _), name);
                Assert.False(doc.TryGetProperty("skins", out _), name);
                Assert.Contains("TeamColor", doc.GetProperty("materials").EnumerateArray().Select(it => it.GetProperty("name").GetString()));
                var top = doc.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(it => doc.GetProperty("nodes")[it.GetInt32()].GetProperty("name").GetString()).ToList();
                Assert.Equal(kind == RigidKind.Armor, top.Contains("Part1") && top.Contains("Part2"));
                if (kind != RigidKind.Armor) continue;
                // the turret and the gun turn about the vertical axis through the model origin (UnitNode.ShowRigid orbits a part's translation about it): the turret sits on it
                var part1 = doc.GetProperty("nodes").EnumerateArray().Single(it => it.GetProperty("name").GetString() == "Part1").GetProperty("translation");
                var part2 = doc.GetProperty("nodes").EnumerateArray().Single(it => it.GetProperty("name").GetString() == "Part2").GetProperty("translation");
                Assert.Equal(0f, part1[0].GetSingle());
                Assert.Equal(0f, part1[2].GetSingle());
                Assert.Equal(0f, part2[0].GetSingle());
            }
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    [Fact]
    public void TheUnitsPackResolvesUnderTheBundledRegistryOrder()
    {
        var files = new DiskPackFiles(ClientFolder);
        var resolver = new AssetResolver(files, new AssetFallbacks(), null, AssetResolver.BundledPack, [AssetResolver.UnitsPack]);
        var resolution = resolver.Resolve(new AssetKey("unit", "Horseman"));
        Assert.Equal(AssetSourceKind.Pack, resolution.Source);
        Assert.Equal("assets/packs/units/models/unit/Horseman.glb", resolution.Path);
        Assert.Empty(resolver.Diagnostics);
    }

    private sealed class DiskPackFiles(string client) : IAssetFiles
    {
        private string Full(string relativePath) => Path.Combine(client, "Packs", relativePath["assets/packs/".Length..]);

        public bool Exists(string relativePath) => relativePath.StartsWith("assets/packs/", StringComparison.Ordinal) && File.Exists(Full(relativePath));

        public string? ReadText(string relativePath) => Exists(relativePath) ? File.ReadAllText(Full(relativePath)) : null;
    }

    private static JsonElement Glb(byte[] glb)
    {
        var length = BitConverter.ToInt32(glb, 12);
        return JsonDocument.Parse(glb.AsMemory(20, length)).RootElement.Clone();
    }

    /// <summary>A minimal glb: one mesh node with a POSITION box, the given material and clip names, node transform and skin flag.</summary>
    private static byte[] Make(float[] min, float[] max, string material = "TeamColor", string[]? clips = null, string nodeExtra = "", bool skin = false)
    {
        string F(float[] v) => string.Join(",", v.Select(it => it.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        var animations = clips == null ? "" : ",\"animations\":[" + string.Join(",", clips.Select(c => $"{{\"name\":\"{c}\",\"channels\":[],\"samplers\":[]}}")) + "]";
        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],"
            + $"\"nodes\":[{{\"mesh\":0{(skin ? ",\"skin\":0" : "")}{nodeExtra}}}],\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":0}}}}]}}],"
            + $"\"accessors\":[{{\"componentType\":5126,\"count\":2,\"type\":\"VEC3\",\"min\":[{F(min)}],\"max\":[{F(max)}]}}],\"materials\":[{{\"name\":\"{material}\"}}]{animations}}}";
        var body = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
        var file = new byte[20 + body.Length];
        BitConverter.GetBytes(0x46546C67u).CopyTo(file, 0);
        BitConverter.GetBytes(2u).CopyTo(file, 4);
        BitConverter.GetBytes((uint)file.Length).CopyTo(file, 8);
        BitConverter.GetBytes((uint)body.Length).CopyTo(file, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(file, 16);
        body.CopyTo(file, 20);
        return file;
    }

    private static readonly float[] Feet = [-0.1f, 0f, -0.1f];
    private static readonly float[] Head = [0.1f, 0.7f, 0.1f];

    [Fact]
    public void AWellFormedModelPasses() => Assert.Empty(UnitModelContract.Check(Make(Feet, Head, clips: [.. UnitModelContract.ClipNames])));

    [Fact]
    public void AModelWithoutClipsPasses() => Assert.Empty(UnitModelContract.Check(Make(Feet, Head)));

    [Fact]
    public void FeetAboveOrBelowZeroFail()
    {
        Assert.Contains("feet", Assert.Single(UnitModelContract.Check(Make([-0.1f, 0.2f, -0.1f], [0.1f, 0.9f, 0.1f]))));
        Assert.Contains("feet", Assert.Single(UnitModelContract.Check(Make([-0.1f, -0.2f, -0.1f], [0.1f, 0.5f, 0.1f]))));
    }

    [Fact]
    public void TooTallOrTooShortFail()
    {
        Assert.Contains("height", Assert.Single(UnitModelContract.Check(Make(Feet, [0.1f, 1.8f, 0.1f]))));
        Assert.Contains("height", Assert.Single(UnitModelContract.Check(Make(Feet, [0.1f, 0.1f, 0.1f]))));
    }

    [Fact]
    public void TooWideFails() => Assert.Contains("radius", Assert.Single(UnitModelContract.Check(Make([-0.9f, 0f, -0.1f], [0.1f, 0.7f, 0.1f]))));

    [Fact]
    public void MissingTeamColorMaterialFails() => Assert.Contains("TeamColor", Assert.Single(UnitModelContract.Check(Make(Feet, Head, material: "Skin"))));

    [Fact]
    public void UnknownClipNameFails() => Assert.Contains("Walking_A", Assert.Single(UnitModelContract.Check(Make(Feet, Head, clips: ["idle", "Walking_A"]))));

    [Fact]
    public void ANodeScaleCountsForAnUnskinnedMeshButNotForItsOwnSkinnedOne()
    {
        // 2 units tall box scaled by 0.35 is 0.7 tall; as a skinned mesh the node's own scale is ignored, so it is 2 tall
        float[] tall = [0.1f, 2f, 0.1f];
        const string scale = ",\"scale\":[0.35,0.35,0.35]";
        Assert.Empty(UnitModelContract.Check(Make(Feet, tall, nodeExtra: scale)));
        Assert.Contains("height", Assert.Single(UnitModelContract.Check(Make(Feet, tall, nodeExtra: scale, skin: true))));
    }

    [Fact]
    public void MatrixAndRotationNodesAreMeasured()
    {
        Assert.Contains("feet", Assert.Single(UnitModelContract.Check(Make(Feet, Head, nodeExtra: ",\"translation\":[0,0.5,0]"))));
        Assert.Contains("feet", Assert.Single(UnitModelContract.Check(Make(Feet, Head, nodeExtra: ",\"matrix\":[1,0,0,0,0,1,0,0,0,0,1,0,0,0.5,0,1]"))));
        // a quarter turn about X lays the standing figure down: it is no longer 0.7 tall
        Assert.NotEmpty(UnitModelContract.Check(Make(Feet, Head, nodeExtra: ",\"rotation\":[0.7071068,0,0,0.7071068]")));
    }

    [Fact]
    public void MissingAccessorBoundsAreReportedNotThrown()
    {
        var noBounds = Make(Feet, Head);
        var text = Encoding.UTF8.GetString(noBounds, 20, noBounds.Length - 20).Replace("\"min\"", "\"mix\"", StringComparison.Ordinal);
        var file = new byte[20 + text.Length];
        noBounds.AsSpan(0, 20).CopyTo(file);
        BitConverter.GetBytes((uint)text.Length).CopyTo(file, 12);
        Encoding.UTF8.GetBytes(text).CopyTo(file, 20);
        Assert.NotEmpty(UnitModelContract.Check(file));
    }

    [Fact]
    public void GarbageIsReportedNotThrown()
    {
        Assert.Contains("magic", Assert.Single(UnitModelContract.Check([1, 2, 3])));
        var notJson = Make(Feet, Head);
        notJson[20] = (byte)'?';
        Assert.Single(UnitModelContract.Check(notJson));
        var wrongChunk = Make(Feet, Head);
        wrongChunk[16] = 0;
        Assert.Contains("JSON", Assert.Single(UnitModelContract.Check(wrongChunk)));
        var noScene = Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"}}   ".PadRight(32));
        var file = new byte[20 + noScene.Length];
        BitConverter.GetBytes(0x46546C67u).CopyTo(file, 0);
        BitConverter.GetBytes((uint)noScene.Length).CopyTo(file, 12);
        BitConverter.GetBytes(0x4E4F534Au).CopyTo(file, 16);
        noScene.CopyTo(file, 20);
        Assert.Contains(UnitModelContract.Check(file), it => it.StartsWith("no mesh", StringComparison.Ordinal));
    }
}
