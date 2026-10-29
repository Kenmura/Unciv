using Godot;

namespace Unciv.GodotClient.Assets;

/// <summary>C016: the player's per-user files (settings, saves, local mods) as real file-system paths; the one place that names the user:// folder.</summary>
public static class UserPaths
{
    public static string Settings => ProjectSettings.GlobalizePath("user://settings.json");

    public static string Saves => ProjectSettings.GlobalizePath("user://saves");

    /// <summary>C034: the player's identity key (who they are in hosted games; never leaves this machine).</summary>
    public static string Identity => ProjectSettings.GlobalizePath("user://identity.key");

    /// <summary>C034: the host key (self-signed certificate) friends pin; created on first hosting so invites keep the same fingerprint.</summary>
    public static string HostKey => ProjectSettings.GlobalizePath("user://host-key.pfx");

    /// <summary>C006: the host fingerprints this player pinned (address to fingerprint), so a changed key is noticed.</summary>
    public static string KnownHosts => ProjectSettings.GlobalizePath("user://known-hosts.json");

    /// <summary>C006: the address typed on the join screen last time.</summary>
    public static string LastJoin => ProjectSettings.GlobalizePath("user://last-join.txt");

    /// <summary>C034: one folder per saved hosted game.</summary>
    public static string HostedGames => ProjectSettings.GlobalizePath("user://hosted");

    /// <summary>C042: one folder per local mod (same layout as Unciv's <c>mods</c> folder); created on first start, scanned by <see cref="RulesetLocator"/>.</summary>
    public static string Mods => ProjectSettings.GlobalizePath("user://mods");
}
