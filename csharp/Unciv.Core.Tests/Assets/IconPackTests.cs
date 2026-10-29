using Unciv.Assets;
using Unciv.Models.Ruleset;
using Unciv.Models.StatTypes;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C063: the bundled <c>unciv</c> icon pack covers the icon keys the client derives, missing keys fall back to distinct generated shapes, and a base pack sits behind the selected one.</summary>
[Collection("RulesetCache")]
public sealed class IconPackTests : IDisposable
{
    private static readonly string PackRoot = FindClientFolder();
    private readonly RulesetClass ruleset;

    public IconPackTests()
    {
        RulesetCache.Clear();
        var jsons = Path.Combine(Path.GetDirectoryName(PackRoot)!, "..", "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
    }

    public void Dispose() => RulesetCache.Clear();

    private static string FindClientFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Unciv.Godot");
            if (File.Exists(Path.Combine(candidate, "Packs", "unciv", "manifest.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("Unciv.Godot/Packs/unciv not found above " + AppContext.BaseDirectory);
    }

    private sealed class DiskFiles : IAssetFiles
    {
        public bool Exists(string relativePath) => File.Exists(Path.Combine(PackRoot, "Packs", relativePath["assets/packs/".Length..]));

        public string? ReadText(string relativePath) => Exists(relativePath) ? File.ReadAllText(Path.Combine(PackRoot, "Packs", relativePath["assets/packs/".Length..])) : null;
    }

    private static AssetResolver Resolver(RulesetClass ruleset) => new(new DiskFiles(), RulesetAssetKeys.FallbacksFor(ruleset), basePack: AssetResolver.BundledPack);

    [Fact]
    public void EveryStatIconResolvesFromThePack()
    {
        var resolver = Resolver(ruleset);
        foreach (var stat in Enum.GetNames<Stat>())
        {
            var resolution = resolver.Resolve(AssetKey.Of(RulesetAssetKeys.StatIcon, stat));
            Assert.Equal(AssetSourceKind.Pack, resolution.Source);
            Assert.EndsWith(".png", resolution.Path, StringComparison.Ordinal);
        }
        Assert.Empty(resolver.Diagnostics);
    }

    [Fact]
    public void EveryInterfaceIconNameIsInThePack()
    {
        var resolver = Resolver(ruleset);
        var names = RulesetAssetKeys.UiIconNames.Select(n => AssetKey.Of(RulesetAssetKeys.UiIcon, n))
            .Concat(RulesetAssetKeys.ActionIconNames.Select(n => AssetKey.Of(RulesetAssetKeys.ActionIcon, n)))
            .Concat(RulesetAssetKeys.NotificationIconNames.Select(n => AssetKey.Of(RulesetAssetKeys.NotificationIcon, n)));
        foreach (var key in names) Assert.True(resolver.Resolve(key).Source == AssetSourceKind.Pack, key + " is not in the pack");
    }

    /// <summary>Emblems whose Credits.md entry is not a proven free licence (see EXCLUDED in import-unciv-icons.py): they use the generated badge.</summary>
    private static readonly string[] NationsWithoutPackEmblem = ["China", "Inca", "Iroquois", "Songhai", "Persia"];

    [Fact]
    public void EveryVanillaNationHasAnEmblemExceptTheUnlicensedOnes()
    {
        var resolver = Resolver(ruleset);
        var missing = ruleset.Nations.Keys.Where(n => resolver.Resolve(AssetKey.Of(RulesetAssetKeys.NationIcon, n)).Source != AssetSourceKind.Pack).Order().ToList();
        Assert.Equal(NationsWithoutPackEmblem.Order(), missing);
    }

    [Fact]
    public void KeySetListsTheNewCategories()
    {
        var keys = RulesetAssetKeys.KeysFor(ruleset);
        foreach (var category in new[] { RulesetAssetKeys.UiIcon, RulesetAssetKeys.NationIcon, RulesetAssetKeys.ActionIcon, RulesetAssetKeys.NotificationIcon })
        {
            Assert.Contains(keys, k => k.Category == category && k.IsDefault);
            Assert.Contains(keys, k => k.Category == category && !k.IsDefault);
        }
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void EveryManifestFileExistsAndIsAPng()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(PackRoot, "Packs", "unciv", "manifest.json")));
        var entries = doc.RootElement.GetProperty("keys").EnumerateObject().ToList();
        Assert.True(entries.Count > 150);
        foreach (var entry in entries)
        {
            Assert.True(AssetKey.TryParse(entry.Name, out _), entry.Name);
            var file = Path.Combine(PackRoot, "Packs", "unciv", entry.Value.GetString()!);
            Assert.True(File.Exists(file), file);
            // C080: the pack also holds unit models (binary glTF, magic "glTF"), checked by UnitModelContractTests
            Assert.Equal(file.EndsWith(".glb", StringComparison.Ordinal) ? "glTF"u8.ToArray() : new byte[] { 0x89, 0x50, 0x4E, 0x47 }, File.ReadAllBytes(file).Take(4));
        }
        Assert.True(File.Exists(Path.Combine(PackRoot, "Packs", "unciv", "CREDITS.md")));
    }

    [Fact]
    public void MissingKeyFallsBackToTheGeneratedBadgeOfItsCategoryShape()
    {
        var resolver = Resolver(ruleset);
        var nation = AssetKey.Of(RulesetAssetKeys.NationIcon, "Atlantis");
        Assert.Equal(AssetSourceKind.Procedural, resolver.Resolve(nation).Source);
        var shapes = new[] { RulesetAssetKeys.NationIcon, RulesetAssetKeys.ActionIcon, RulesetAssetKeys.NotificationIcon, RulesetAssetKeys.UiIcon }
            .Select(c => ProceduralImages.ShapeFor(AssetKey.Of(c, "Nothing"))).ToList();
        Assert.Equal(4, shapes.Distinct().Count());
        Assert.Equal(ProceduralImages.IconShape.Disc, ProceduralImages.ShapeFor(nation));
        Assert.Contains(ProceduralImages.Icon(nation).Rgba.Where((_, i) => i % 4 == 3), a => a == 255);
    }

    [Fact]
    public void SelectedPackWinsOverTheBasePackAndAMissingBasePackIsSilent()
    {
        var files = new MemoryAssetFiles()
            .Add("assets/packs/mine/manifest.json", """{"keys":{"icon/stat/Gold":"g.png"}}""").Add("assets/packs/mine/g.png")
            .Add("assets/packs/unciv/manifest.json", """{"keys":{"icon/stat/Gold":"b.png","icon/stat/Food":"f.png"}}""").Add("assets/packs/unciv/b.png").Add("assets/packs/unciv/f.png");
        var resolver = new AssetResolver(files, new AssetFallbacks(), "mine", "unciv");
        Assert.Equal("assets/packs/mine/g.png", resolver.Resolve(AssetKey.Of("icon/stat", "Gold")).Path);
        Assert.Equal("assets/packs/unciv/f.png", resolver.Resolve(AssetKey.Of("icon/stat", "Food")).Path);
        var lone = new AssetResolver(new MemoryAssetFiles(), new AssetFallbacks(), null, "unciv");
        Assert.Equal(AssetSourceKind.Procedural, lone.Resolve(AssetKey.Of("icon/stat", "Gold")).Source);
        Assert.Empty(lone.Diagnostics);
        var bad = new AssetResolver(new MemoryAssetFiles(), new AssetFallbacks(), "nope", "unciv");
        bad.Resolve(AssetKey.Of("icon/stat", "Gold"));
        Assert.Single(bad.Diagnostics);
    }
}
