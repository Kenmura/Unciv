using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Models.Ruleset;

// T103: port of tests/src/com/unciv/logic/TutorialTranslationTests.kt: the shipped Tutorials.json of Civ_V_GnK loads and covers every trigger.
[Collection("RulesetCache")]
public sealed class TutorialTranslationTests : IDisposable
{
    private readonly Unciv.Utils.LinkedHashMap<string, Tutorial> tutorials;

    public TutorialTranslationTests()
    {
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        tutorials = RulesetCache.Get(BaseRuleset.Civ_V_GnK.FullName())!.Tutorials;
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void TutorialsFileIsDeSerializable() => Assert.NotEmpty(tutorials); // the constructor throws if loading fails

    [Fact]
    public void TutorialsFileCoversAllTriggers()
    {
        foreach (var trigger in Enum.GetValues<TutorialTrigger>())
        {
            var name = trigger.Value().Replace('_', ' ').TrimStart();
            Assert.True(tutorials.ContainsKey(name), $"TutorialTrigger {trigger} has no matching entry in Tutorials.json");
        }
    }

    [Fact]
    public void TutorialsAllHaveText()
    {
        foreach (var tutorial in tutorials.Values)
            Assert.True(tutorial.Steps is { Count: > 0 } || tutorial.CivilopediaText.Count > 0, $"Tutorial \"{tutorial.Name}\" contains no text");
    }
}
