using Unciv.Models.Ruleset;
using Unciv.Models.Translations;
using Unciv.Utils;
using Trans = Unciv.Models.Translations.Translations;

namespace Unciv.Core.Tests.Models.Ruleset.ModLoading;

// T170: loading mod folders through RulesetCache and the mods' jsons/translations/*.properties through Translations,
// using the small bundled mods in Mods/ (TestMod, LateMod, a BrokenMod with invalid JSON and a dot-folder that must be skipped).
[Collection("RulesetCache")]
public sealed class ModLoadingTests : IDisposable
{
    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static string ModsPath() => Path.Combine(AppContext.BaseDirectory, "Models", "Ruleset", "ModLoading", "Mods");

    private static DirectoryRulesetFileSource Source() => new(JsonsPath(), ModsPath());

    public void Dispose() => RulesetCache.Clear();

    [Fact]
    public void ModFoldersAreLoadedAfterTheBaseRulesetsAndBrokenOnesReported()
    {
        var errors = RulesetCache.LoadRulesets(Source());

        // '.hidden' is skipped, BrokenMod is reported and left out; the cache is a hash map, so compare as a set.
        Assert.Equal(["Civ V - Brave New World", "Civ V - Gods & Kings", "Civ V - Vanilla", "LateMod", "TestMod"], RulesetCache.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.False(RulesetCache.ContainsKey("BrokenMod"));
        Assert.False(RulesetCache.ContainsKey(".hidden"));
        Assert.Equal("Exception loading mod 'BrokenMod':", errors[0]);
        Assert.True(errors.Count >= 2);

        var testMod = RulesetCache.Get("TestMod")!;
        Assert.Equal(["Test Hut"], testMod.Buildings.Keys);
        Assert.Equal(60, testMod.Buildings["Test Hut"].Cost);
        Assert.Equal("TestMod", testMod.Buildings["Test Hut"].OriginRuleset);
        Assert.Equal("TestMod", testMod.FolderLocation!.Name);
        Assert.False(testMod.ModOptions.IsBaseRuleset);
        Assert.Null(RulesetCache.Get("Civ V - Vanilla")!.FolderLocation);
    }

    [Fact]
    public void NoModsSkipsTheModsFolder()
    {
        Assert.Empty(RulesetCache.LoadRulesets(Source(), noMods: true));
        Assert.Equal(3, RulesetCache.Count);
    }

    [Fact]
    public void ReloadSingleRulesetReplacesAndReportsMissing()
    {
        RulesetCache.LoadRulesets(Source());
        var before = RulesetCache.Get("TestMod");
        Assert.Empty(RulesetCache.ReloadSingleRuleset(Source(), "TestMod"));
        Assert.NotSame(before, RulesetCache.Get("TestMod"));
        Assert.Equal(["No such mod"], RulesetCache.ReloadSingleRuleset(Source(), "NoSuchMod"));
        Assert.NotEmpty(RulesetCache.ReloadSingleRuleset(Source(), "BrokenMod"));
    }

    [Fact]
    public void ComplexRulesetCombinesBaseAndModInOrder()
    {
        RulesetCache.LoadRulesets(Source());
        var combined = RulesetCache.GetComplexRuleset(new LinkedHashSet<string>(["LateMod", "TestMod", "Missing"]), "Civ V - Gods & Kings");

        Assert.Equal(["Civ V - Gods & Kings", "LateMod", "TestMod"], combined.Mods.ToArray());
        Assert.Contains("Test Hut", combined.Buildings.Keys);
        Assert.Contains("Late Hut", combined.Buildings.Keys);
        Assert.Contains("Test mod unique", combined.ModOptions.Uniques);
        Assert.Contains("Monument", combined.Buildings.Keys);
    }

    [Fact]
    public void ModTranslationsAreReadPerModFolderAndSearchedInActiveModOrder()
    {
        RulesetCache.LoadRulesets(Source());
        var game = new DirectoryRulesetFolder(Path.Combine(ModsPath(), "NoSuchGameFolder"));
        var translations = new Trans();

        // The game's own file does not exist below this folder: nothing is read, as in Kotlin.
        translations.TryReadTranslationForLanguage("English", game);
        Assert.Empty(translations.ModsWithTranslations);
        Assert.Equal(0, translations.Count);

        // The real jsons folder has the game's files: android/assets/jsons/translations/English.properties.
        var assets = new DirectoryRulesetFolder(Path.GetDirectoryName(JsonsPath())!);
        translations.TryReadTranslationForLanguage("English", assets);
        translations.TryReadTranslationForLanguage("German", assets);

        Assert.Equal(["LateMod", "TestMod"], translations.ModsWithTranslations.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.True(translations.Count > 100);

        var late = new LinkedHashSet<string>(["LateMod", "TestMod"]);
        var test = new LinkedHashSet<string>(["TestMod", "LateMod"]);
        Assert.Equal("Late [name]", translations.GetText("Greetings []", "English", late));
        Assert.Equal("Hi [name]", translations.GetText("Greetings []", "English", test));
        Assert.Equal("Greetings []", translations.GetText("Greetings []", "English"));
        Assert.Equal("Testhaus", translations.GetText("Test Hut", "German", test));
        Assert.Equal("Test Hut!", translations.GetText("Test Hut", "English", test));
        // LateMod has no German file, and an empty value in a .properties line is dropped.
        Assert.Equal("Hallo [name]", translations.GetText("Greetings []", "German", late));
        Assert.Equal("Zzz Empty Key", translations.GetText("Zzz Empty Key", "German", test));
        Assert.Equal("Test Hut", translations.GetText("Test Hut", "German"));
    }

    [Fact]
    public void CompletionPercentagesAreReadFromTheGameFolder()
    {
        var translations = new Trans();
        translations.LoadPercentageCompleteOfLanguages(new DirectoryRulesetFolder(Path.GetDirectoryName(JsonsPath())!));
        Assert.Contains("German", translations.PercentCompleteOfLanguages.Keys);
        Assert.True(translations.PercentCompleteOfLanguages.Count > 5);

        var missing = new Trans();
        missing.LoadPercentageCompleteOfLanguages(new DirectoryRulesetFolder(ModsPath()));
        Assert.Empty(missing.PercentCompleteOfLanguages);
    }

    [Fact]
    public void FolderHandleExposesNameAndChildren()
    {
        var handle = new RulesetFolderTranslationFileHandle(new DirectoryRulesetFolder(ModsPath()));
        Assert.True(handle.Exists);
        Assert.Equal("Mods", handle.Name);
        var file = handle.Child("TestMod/jsons/translations/English.properties");
        Assert.True(file.Exists);
        using var reader = file.OpenReader();
        Assert.StartsWith("Test Hut", reader.ReadLine());
        Assert.False(handle.Child("nope.properties").Exists);
    }
}
