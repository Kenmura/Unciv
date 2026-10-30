using System.Text.RegularExpressions;

namespace Unciv.GodotClient.Settings;

/// <summary>
/// T334: which profile this client instance runs as. A profile is a separate set of per-user files (identity key, settings, saves, host key, known hosts, hosted games) under
/// <c>user://profiles/NAME/</c>, so two copies of the client on one computer are two different players. No profile (the default) keeps the files where they always were, so existing players keep
/// their identity. Chosen by the user argument <c>--profile=NAME</c> (or <c>--profile NAME</c>) after Godot's <c>--</c>, else by the environment variable <c>UNCIV_PROFILE</c>.
/// A name is 1 to 32 letters, digits, '-' or '_' and starts with a letter or digit, so it can never be a path.
/// </summary>
public static class UserProfile
{
    public const string Argument = "--profile";
    public const string EnvironmentVariable = "UNCIV_PROFILE";
    public const int MaxLength = 32;

    private static readonly Regex Valid = new("^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>The command a player types to start a second copy as another player (shown in the host and join help).</summary>
    public const string SecondInstanceHelp = "Testing with two copies on one computer? Start the second copy with: Unciv -- --profile=friend (or set UNCIV_PROFILE=friend). Each profile is its own player with its own settings and saves.";

    public static bool IsValidName(string? name) => name != null && Valid.IsMatch(name);

    /// <summary>
    /// The profile asked for: the argument wins over the environment. Returns the name (null for the default profile) and, for a bad name or a missing value, an error sentence instead
    /// (the client then refuses to start rather than silently sharing the default player's files).
    /// </summary>
    public static (string? Name, string? Error) Resolve(IEnumerable<string> userArgs, string? environment)
    {
        string? requested = null;
        var args = userArgs.ToList();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == Argument)
            {
                if (i + 1 >= args.Count) return (null, $"{Argument} needs a name, for example {Argument}=friend");
                requested = args[i + 1];
                break;
            }
            if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal))
            {
                requested = args[i][(Argument.Length + 1)..];
                break;
            }
        }
        requested ??= string.IsNullOrEmpty(environment) ? null : environment;
        if (requested == null) return (null, null);
        return IsValidName(requested)
            ? (requested, null)
            : (null, $"Invalid profile name '{requested}': use 1 to {MaxLength} letters, digits, '-' or '_', starting with a letter or digit");
    }

    /// <summary>The folder (relative to <c>user://</c>, with a trailing slash, or empty for the default profile) that holds the profile's files.</summary>
    public static string FolderFor(string? name) => name == null ? "" : $"profiles/{name}/";

    /// <summary>The window title: the plain name for the default profile, else with the profile so two windows can be told apart.</summary>
    public static string WindowTitle(string? name) => name == null ? "Unciv" : $"Unciv - {name}";
}

/// <summary>T334: the per-player files of one profile under a user-data root (the globalized <c>user://</c>). Everything that is player state lives here; local mods are content and stay in the root.</summary>
public sealed class ProfileFiles(string root, string? profile)
{
    private readonly string folder = Path.Combine(root, UserProfile.FolderFor(profile));

    public string Settings => Path.Combine(folder, "settings.json");

    public string Saves => Path.Combine(folder, "saves");

    public string Identity => Path.Combine(folder, "identity.key");

    public string HostKey => Path.Combine(folder, "host-key.pfx");

    public string KnownHosts => Path.Combine(folder, "known-hosts.json");

    public string LastJoin => Path.Combine(folder, "last-join.txt");

    public string HostedGames => Path.Combine(folder, "hosted");

    /// <summary>Shared by every profile: mods are content, not player state.</summary>
    public string Mods { get; } = Path.Combine(root, "mods");
}
