using Godot;
using Unciv.Assets.Packaging;
using Unciv.Models.Ruleset;

namespace Unciv.GodotClient.Assets;

/// <summary>
/// Loads the rulesets: Vanilla and Gods and Kings from the jsons found by <see cref="ContentLocator"/> (bundled next to an exported build, or the checkout's
/// <c>android/assets/jsons</c>), plus every local mod in the per-user mods folder (<see cref="UserPaths.Mods"/>, C042).
/// </summary>
public static class RulesetLocator
{
    public static Ruleset LoadVanilla()
    {
        var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        var dir = ContentLocator.FindJsons(exeDir, ProjectSettings.GlobalizePath("res://"), System.IO.Directory.Exists)
            ?? throw new System.IO.DirectoryNotFoundException($"No ruleset jsons: expected {ContentLocator.BundledJsons(exeDir)} or android/assets/jsons above the project");
        var mods = UserPaths.Mods;
        System.IO.Directory.CreateDirectory(mods);
        RulesetCache.Clear();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(dir, mods));
        return RulesetCache.GetVanillaRuleset();
    }
}
