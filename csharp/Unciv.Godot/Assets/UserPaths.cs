using Godot;

namespace Unciv.GodotClient.Assets;

/// <summary>C016: the player's per-user files (settings, saves) as real file-system paths; the one place that names the user:// folder.</summary>
public static class UserPaths
{
    public static string Settings => ProjectSettings.GlobalizePath("user://settings.json");

    public static string Saves => ProjectSettings.GlobalizePath("user://saves");
}
