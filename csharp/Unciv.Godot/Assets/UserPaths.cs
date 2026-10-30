using Godot;
using Unciv.GodotClient.Settings;

namespace Unciv.GodotClient.Assets;

/// <summary>C016: the player's per-user files (settings, saves, local mods) as real file-system paths; the one place that names the user:// folder.</summary>
public static class UserPaths
{
    private static readonly (string? Name, string? Error) Chosen = UserProfile.Resolve(OS.GetCmdlineUserArgs(), System.Environment.GetEnvironmentVariable(UserProfile.EnvironmentVariable));

    /// <summary>T334: the profile this instance runs as (null = the default player, files where they always were).</summary>
    public static string? Profile => Chosen.Name;

    /// <summary>T334: why the requested profile is unusable (a bad name); the app then refuses to start instead of sharing the default player's files.</summary>
    public static string? ProfileError => Chosen.Error;

    private static readonly ProfileFiles Files = new(ProjectSettings.GlobalizePath("user://"), Chosen.Name);

    public static string Settings => Files.Settings;

    public static string Saves => Files.Saves;

    /// <summary>C034: the player's identity key (who they are in hosted games; never leaves this machine).</summary>
    public static string Identity => Files.Identity;

    /// <summary>C034: the host key (self-signed certificate) friends pin; created on first hosting so invites keep the same fingerprint.</summary>
    public static string HostKey => Files.HostKey;

    /// <summary>C006: the host fingerprints this player pinned (address to fingerprint), so a changed key is noticed.</summary>
    public static string KnownHosts => Files.KnownHosts;

    /// <summary>C006: the address typed on the join screen last time.</summary>
    public static string LastJoin => Files.LastJoin;

    /// <summary>C034: one folder per saved hosted game.</summary>
    public static string HostedGames => Files.HostedGames;

    /// <summary>C042: one folder per local mod (T334: shared by every profile, mods are content, not player state) (same layout as Unciv's <c>mods</c> folder); created on first start, scanned by <see cref="RulesetLocator"/>.</summary>
    public static string Mods => Files.Mods;
}
