using Unciv.Assets;

namespace Unciv.Core.Tests.Assets;

public class AssetResolverTests
{
    private static readonly AssetKey Warrior = new("unit", "Warrior");
    private static readonly AssetKey Melee = new("unitType", "Melee");

    private static AssetFallbacks Fallbacks() => new AssetFallbacks().Add(Warrior, Melee);

    [Fact]
    public void ChainIsKeyParentsThenDefault()
    {
        var chain = AssetFallbacks.Chain(Warrior, Fallbacks());
        Assert.Equal(["unit/Warrior", "unitType/Melee", "unit/_default"], chain.Select(k => k.ToString()));
    }

    [Fact]
    public void ChainWithoutFallbacksIsKeyThenDefault() =>
        Assert.Equal(["terrain/Grassland", "terrain/_default"], AssetFallbacks.Chain(new AssetKey("terrain", "Grassland"), AssetFallbacks.None).Select(k => k.ToString()));

    [Fact]
    public void ChainOfDefaultKeyHasNoDuplicate() =>
        Assert.Single(AssetFallbacks.Chain(new AssetKey("terrain", "_default"), AssetFallbacks.None));

    [Fact]
    public void ChainSurvivesCycles()
    {
        var a = new AssetKey("x", "a");
        var b = new AssetKey("x", "b");
        var chain = AssetFallbacks.Chain(a, new AssetFallbacks().Add(a, b).Add(b, a));
        Assert.Equal(["x/a", "x/b", "x/_default"], chain.Select(k => k.ToString()));
    }

    [Fact]
    public void ChainStopsAfterEightHops()
    {
        var fb = new AssetFallbacks();
        for (var i = 0; i < 30; i++) fb.Add(new AssetKey("x", "n" + i), new AssetKey("x", "n" + (i + 1)));
        Assert.Equal(10, AssetFallbacks.Chain(new AssetKey("x", "n0"), fb).Count);
    }

    [Fact]
    public void NoFilesIsProcedural()
    {
        var r = new AssetResolver(new MemoryAssetFiles(), Fallbacks()).Resolve(Warrior);
        Assert.Equal(AssetSourceKind.Procedural, r.Source);
        Assert.Equal(Warrior, r.Matched);
        Assert.Null(r.Path);
    }

    [Fact]
    public void OverrideFileWins()
    {
        var files = new MemoryAssetFiles().Add("assets/override/unit/Warrior.glb");
        var r = new AssetResolver(files, Fallbacks()).Resolve(Warrior);
        Assert.Equal(AssetSourceKind.Override, r.Source);
        Assert.Equal("assets/override/unit/Warrior.glb", r.Path);
    }

    [Fact]
    public void ExtensionOrderIsTresGlbPngOgg()
    {
        var files = new MemoryAssetFiles().Add("assets/override/unit/Warrior.ogg").Add("assets/override/unit/Warrior.png");
        Assert.EndsWith(".png", new AssetResolver(files, Fallbacks()).Resolve(Warrior).Path, StringComparison.Ordinal);
    }

    [Fact]
    public void OverrideBeatsPackForSameKey()
    {
        var files = new MemoryAssetFiles()
            .Add("assets/packs/p/manifest.json", """{"keys":{"unit/Warrior":"w.png"}}""")
            .Add("assets/packs/p/w.png")
            .Add("assets/override/unit/Warrior.png");
        Assert.Equal(AssetSourceKind.Override, new AssetResolver(files, Fallbacks(), "p").Resolve(Warrior).Source);
    }

    [Fact]
    public void PackUsedWhenNoOverride()
    {
        var files = new MemoryAssetFiles()
            .Add("assets/packs/p/manifest.json", """{"keys":{"unit/Warrior":"models/w.glb"}}""")
            .Add("assets/packs/p/models/w.glb");
        var resolver = new AssetResolver(files, Fallbacks(), "p");
        var r = resolver.Resolve(Warrior);
        Assert.Equal(AssetSourceKind.Pack, r.Source);
        Assert.Equal("assets/packs/p/models/w.glb", r.Path);
        Assert.Empty(resolver.Diagnostics);
    }

    [Fact]
    public void PackIsIgnoredWhenNotSelected()
    {
        var files = new MemoryAssetFiles()
            .Add("assets/packs/p/manifest.json", """{"keys":{"unit/Warrior":"w.png"}}""")
            .Add("assets/packs/p/w.png");
        Assert.Equal(AssetSourceKind.Procedural, new AssetResolver(files, Fallbacks()).Resolve(Warrior).Source);
    }

    [Fact]
    public void PackEntryWithMissingFileFallsThrough()
    {
        var files = new MemoryAssetFiles().Add("assets/packs/p/manifest.json", """{"keys":{"unit/Warrior":"w.png"}}""");
        Assert.Equal(AssetSourceKind.Procedural, new AssetResolver(files, Fallbacks(), "p").Resolve(Warrior).Source);
    }

    [Fact]
    public void FallbackParentFileBeatsProceduralForChild()
    {
        var files = new MemoryAssetFiles().Add("assets/override/unitType/Melee.png");
        var r = new AssetResolver(files, Fallbacks()).Resolve(Warrior);
        Assert.Equal(AssetSourceKind.Override, r.Source);
        Assert.Equal(Melee, r.Matched);
        Assert.Equal(Warrior, r.Requested);
    }

    [Fact]
    public void CategoryDefaultFileIsLastResort()
    {
        var files = new MemoryAssetFiles().Add("assets/override/unit/_default.png");
        Assert.Equal(new AssetKey("unit", "_default"), new AssetResolver(files, Fallbacks()).Resolve(Warrior).Matched);
    }

    [Fact]
    public void ExactKeyBeatsParentBeatsDefault()
    {
        var files = new MemoryAssetFiles().Add("assets/override/unit/_default.png").Add("assets/override/unitType/Melee.png");
        Assert.Equal(Melee, new AssetResolver(files, Fallbacks()).Resolve(Warrior).Matched);
        files.Add("assets/override/unit/Warrior.png");
        Assert.Equal(Warrior, new AssetResolver(files, Fallbacks()).Resolve(Warrior).Matched);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"keys":[]}""")]
    public void BrokenManifestIsReportedNotThrown(string manifest)
    {
        var files = new MemoryAssetFiles().Add("assets/packs/p/manifest.json", manifest).Add("assets/packs/p/w.png");
        var resolver = new AssetResolver(files, Fallbacks(), "p");
        Assert.Equal(AssetSourceKind.Procedural, resolver.Resolve(Warrior).Source);
        Assert.Single(resolver.Diagnostics);
    }

    [Fact]
    public void MissingManifestIsReported()
    {
        var resolver = new AssetResolver(new MemoryAssetFiles(), Fallbacks(), "nope");
        Assert.Equal(AssetSourceKind.Procedural, resolver.Resolve(Warrior).Source);
        Assert.Contains("not found", Assert.Single(resolver.Diagnostics), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("")]
    public void UnsafePackNameIsRejected(string pack)
    {
        var resolver = new AssetResolver(new MemoryAssetFiles(), Fallbacks(), pack);
        resolver.Resolve(Warrior);
        if (pack.Length > 0) Assert.Single(resolver.Diagnostics);
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("/etc/passwd.png")]
    [InlineData("res://x.png")]
    [InlineData("C:/x.png")]
    [InlineData("a\\b.png")]
    [InlineData("a//b.png")]
    [InlineData("noextension")]
    [InlineData("script.cs")]
    public void ManifestRejectsUnsafePaths(string path)
    {
        Assert.False(AssetResolver.IsSafeRelativePath(path));
        var manifest = "{\"keys\":{\"unit/Warrior\":\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"}}";
        var files = new MemoryAssetFiles().Add("assets/packs/p/manifest.json", manifest);
        var resolver = new AssetResolver(files, Fallbacks(), "p");
        Assert.Equal(AssetSourceKind.Procedural, resolver.Resolve(Warrior).Source);
        Assert.Single(resolver.Diagnostics);
    }

    [Fact]
    public void ManifestReportsBadKeysAndNonStringValues()
    {
        var files = new MemoryAssetFiles().Add("assets/packs/p/manifest.json", """{"keys":{"nokey":"a.png","unit/Warrior":5,"unit/Archer":"a.png"}}""").Add("assets/packs/p/a.png");
        var resolver = new AssetResolver(files, Fallbacks(), "p");
        Assert.Equal(AssetSourceKind.Pack, resolver.Resolve(new AssetKey("unit", "Archer")).Source);
        Assert.Equal(2, resolver.Diagnostics.Count);
    }

    [Fact]
    public void UnsafeManifestEntryIgnoredEvenWhenFileExists()
    {
        var files = new MemoryAssetFiles()
            .Add("assets/packs/p/manifest.json", """{"keys":{"unit/Warrior":"../../override/secret.png"}}""")
            .Add("assets/packs/p/../../override/secret.png");
        Assert.Equal(AssetSourceKind.Procedural, new AssetResolver(files, Fallbacks(), "p").Resolve(Warrior).Source);
    }

    [Fact]
    public void SafeRelativePathAcceptsNormalPaths() => Assert.True(AssetResolver.IsSafeRelativePath("models/w.GLB"));

    [Fact]
    public void MemoryFilesReadBack()
    {
        var files = new MemoryAssetFiles().Add("a", "text");
        Assert.Equal("text", files.ReadText("a"));
        Assert.Null(files.ReadText("b"));
    }
}
