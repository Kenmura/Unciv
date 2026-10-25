using Godot;

namespace Unciv.GodotClient.Assets;

/// <summary>C016: the player's per-user files (settings, saves, local mods) as real file-system paths; the one place that names the user:// folder.</summary>
public static class UserPaths
{
    public static string Settings => ProjectSettings.GlobalizePath("user://settings.json");

    public static string Saves => ProjectSettings.GlobalizePath("user://saves");

    /// <summary>C042: one folder per local mod (same layout as Unciv's <c>mods</c> folder); created on first start, scanned by <see cref="RulesetLocator"/>.</summary>
    public static string Mods => ProjectSettings.GlobalizePath("user://mods");
}
