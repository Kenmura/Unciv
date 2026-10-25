using Godot;
using Unciv.Models.Ruleset;

namespace Unciv.GodotClient.Assets;

/// <summary>Finds the repository's ruleset jsons above the Godot project and loads Vanilla from them (the quick start, until the client ships its own copy).</summary>
public static class RulesetLocator
{
    public static Ruleset LoadVanilla()
    {
        string? dir = null;
        for (var d = ProjectSettings.GlobalizePath("res://"); dir == null && !string.IsNullOrEmpty(d); d = System.IO.Path.GetDirectoryName(d.TrimEnd('/')) ?? "")
        {
            var candidate = System.IO.Path.Combine(d, "android", "assets", "jsons");
            if (System.IO.Directory.Exists(candidate)) dir = candidate;
        }
        if (dir == null) throw new System.IO.DirectoryNotFoundException("android/assets/jsons not found above the project");
        RulesetCache.Clear();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(dir, System.IO.Path.Combine(dir, "no mods here")), noMods: true);
        return RulesetCache.GetVanillaRuleset();
    }
}
