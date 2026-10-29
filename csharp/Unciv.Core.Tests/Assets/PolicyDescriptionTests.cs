using Unciv.Assets;
using Unciv.Models.Ruleset;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C071: the policy screen's effect lines from the real Brave New World ruleset.</summary>
[Collection("RulesetCache")]
public sealed class PolicyDescriptionTests : IDisposable
{
    private readonly RulesetClass ruleset;

    public PolicyDescriptionTests()
    {
        RulesetCache.Clear();
        string jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.Get("Civ V - Brave New World")!;
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void DescriptionComesFromTheRuleset()
    {
        Assert.Contains("Culture", PolicyScreenModel.DescriptionOf(ruleset, "Tradition"), StringComparison.Ordinal);
        Assert.Equal("", PolicyScreenModel.DescriptionOf(ruleset, "No such policy"));
    }

    [Fact]
    public void VolunteerArmyShowsItsSixFreeUnitsAsOneLine()
    {
        var lines = PolicyScreenModel.EffectLines(PolicyScreenModel.DescriptionOf(ruleset, "Volunteer Army"), "Volunteer Army");
        Assert.Equal(2, lines.Count);
        var free = Assert.Single(lines, it => it.Text.Contains("Foreign Legion", StringComparison.Ordinal));
        Assert.Equal(6, free.Count);
        Assert.EndsWith("(×6)", free.Display, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, it => it.Text == "Volunteer Army");
    }
}
