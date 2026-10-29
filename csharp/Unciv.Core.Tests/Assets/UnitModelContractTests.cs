using System.Text;
using System.Text.Json;
using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

/// <summary>C080: every <c>.glb</c> shipped in a pack satisfies the unit model contract, the bundled Warrior is one of them and is what <c>unit/Warrior</c> resolves to, and the checker rejects each way of breaking the contract.</summary>
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

    [Fact]
    public void TheBundledWarriorIsInTheManifestAndKeepsItsClips()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientFolder, "Packs", "unciv", "manifest.json")));
        var relative = manifest.RootElement.GetProperty("keys").GetProperty("unit/Warrior").GetString()!;
        var bytes = File.ReadAllBytes(Path.Combine(ClientFolder, "Packs", "unciv", relative));
        Assert.Empty(UnitModelContract.Check(bytes));
        var animations = Glb(bytes).GetProperty("animations").EnumerateArray().Select(it => it.GetProperty("name").GetString()!).ToList();
        Assert.Equal(UnitModelContract.ClipNames, animations);
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
