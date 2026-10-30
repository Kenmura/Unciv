using System.Globalization;

namespace Unciv.Server;

/// <summary>Thrown for a command line the operator got wrong; the runner prints the message and exits with code 2.</summary>
public sealed class ServerUsageException(string message) : Exception(message);

/// <summary>Log levels of <see cref="ServerLog"/>, quietest last.</summary>
public enum ServerLogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>The parsed command line of the dedicated server (T265). <see cref="Parse"/> throws <see cref="ServerUsageException"/>; nothing here touches the disk or the network.</summary>
public sealed record ServerOptions
{
    public const int DefaultPort = 47821;

    public const string HelpText = """
        Unciv.Server - dedicated host: opens a lobby on a TCP+TLS port, then hosts the game it starts.

        Usage: Unciv.Server [options]

          --port N            TCP port to listen on (default 47821; 0 picks a free one). The bound port is printed as a stdout line 'LISTENING <port>'.
          --data-dir DIR      Where the host key (host-key.pfx) lives (default ./unciv-server-data). Keep it: its fingerprint is what players pin.
          --password TEXT     Players must give this password to join (visible in the process list: prefer --password-file).
          --password-file F   Read the join password from the first line of file F (keep it readable by you only). Excludes --password.
          --game-id ID        Save this game under DATA-DIR/games/ID (letters, digits, '.', '_', '-'). If that folder holds a game, the server resumes it at once: the seated players
                              reconnect with their keys, the lobby is closed, and the saved turn timer rules apply (the lobby options --max-players, --min-humans, --ai-fill and the turn options of --settings are ignored; the password is applied, but a saved seat's key needs no password to return). Otherwise the game the lobby starts is saved there. Without --game-id
                              nothing is saved: a stop ends the game.
          --max-players N     Number of major civs (seats) in the lobby, at least 2 (default: the settings file's, else 4).
          --ai-fill           Fill the seats nobody takes with AI when the game starts (default). --no-ai-fill leaves them out.
          --auto-start        Start the game by itself once every seated human is ready and at least --min-humans have joined.
          --min-humans N      Humans needed before --auto-start fires (default 1).
          --settings FILE     Lobby settings as JSON (the LobbySettings fields: map, speed, difficulty, era, victoryTypes, ruleset, mods, turnTimerSeconds, ...). Options above override it.
          --print-invite      Print 'INVITE unciv://host:port/fingerprint[/token]' (a token only with a password) after LISTENING.
          --address HOST      The host name or address written into the invite (default localhost).
          --jsons DIR         The rulesets folder (default: data/jsons next to the executable, else android/assets/jsons above the current folder).
          --mods-dir DIR      A folder of mods to offer.
          --parallel-views N  Build the per-player views of a round on up to N tasks (2 to 64; 0, 1 or absent: off, the default). Frames are byte-identical either way; it only pays on a host with spare cores and several players.
          --log-level LEVEL   debug, info (default), warn or error. Log lines go to stdout.
          --help, -h          Show this help and exit.

        Stopping: SIGINT (Ctrl+C) or SIGTERM (Windows: Ctrl+C is the supported way) closes the port to new joins, finishes the running command (and a round that is resolving), saves the game, tells the
        players the host is closing and exits 0. A second signal while it saves ends the process at once without saving further (the exit code is then the runtime's, 130 or 143).

        Exit codes: 0 stopped normally (also after a signal), 1 failure while running (port in use, a lobby that cannot start, an internal error), 2 bad command line or settings,
        3 the saved game under --game-id cannot be loaded (corrupt or from missing rulesets): fix or remove it; a supervisor should not restart on 3.
        """;

    public bool Help { get; init; }

    public int Port { get; init; } = DefaultPort;

    public string DataDir { get; init; } = "unciv-server-data";

    public string? Password { get; init; }

    public string? PasswordFile { get; init; }

    public string? GameId { get; init; }

    public int? MaxPlayers { get; init; }

    /// <summary>Null: whatever the settings say (true unless the file says otherwise).</summary>
    public bool? AiFill { get; init; }

    public bool AutoStart { get; init; }

    public int MinHumans { get; init; } = 1;

    public string? SettingsFile { get; init; }

    public bool PrintInvite { get; init; }

    /// <summary>Null when not given (the invite then names <c>localhost</c>, which only works on this machine).</summary>
    public string? Address { get; init; }

    public string EffectiveAddress => Address ?? "localhost";

    public string? Jsons { get; init; }

    public string? ModsDir { get; init; }

    public ServerLogLevel LogLevel { get; init; } = ServerLogLevel.Info;

    /// <summary>T302: <c>--parallel-views N</c>; 0 (default) keeps the sequential view building. Reaches the game host through <see cref="ToHostLimits"/>.</summary>
    public int ParallelViews { get; init; }

    /// <summary>The host limits this command line asks for: the defaults, plus parallel view building when <see cref="ParallelViews"/> is 2 or more.</summary>
    public HostLimits ToHostLimits() => ParallelViews >= 2 ? new HostLimits { ParallelViewBuilding = true, MaxViewParallelism = ParallelViews } : new HostLimits();

    public static ServerOptions Parse(IReadOnlyList<string> args)
    {
        var result = new ServerOptions();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h") return result with { Help = true };
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2) throw new ServerUsageException($"unexpected argument '{arg}'");
            var name = arg[2..];
            if (!seen.Add(name)) throw new ServerUsageException($"option --{name} given twice");
            switch (name)
            {
                case "ai-fill": result = result with { AiFill = true }; continue;
                case "no-ai-fill": result = result with { AiFill = false }; continue;
                case "auto-start": result = result with { AutoStart = true }; continue;
                case "print-invite": result = result with { PrintInvite = true }; continue;
            }

            if (name is not ("port" or "data-dir" or "password" or "password-file" or "game-id" or "max-players" or "min-humans" or "settings" or "address" or "jsons" or "mods-dir" or "log-level" or "parallel-views"))
                throw new ServerUsageException($"unknown option --{name} (see --help)");
            if (i + 1 >= args.Count) throw new ServerUsageException($"option --{name} needs a value");
            var value = args[++i];
            result = name switch
            {
                "port" => result with { Port = ParseInt(name, value, 0, 65535) },
                "password-file" => result with { PasswordFile = value },
                "game-id" => result with { GameId = IsValidGameId(value) ? value : throw new ServerUsageException("--game-id must be 1 to 64 letters, digits, '.', '_' or '-' and start with a letter or digit") },
                "data-dir" => result with { DataDir = value },
                "password" => result with { Password = value.Length == 0 ? throw new ServerUsageException("--password must not be empty") : value },
                "max-players" => result with { MaxPlayers = ParseInt(name, value, 2, 64) },
                "min-humans" => result with { MinHumans = ParseInt(name, value, 1, 64) },
                "settings" => result with { SettingsFile = value },
                "address" => result with { Address = value },
                "jsons" => result with { Jsons = value },
                "mods-dir" => result with { ModsDir = value },
                "parallel-views" => result with { ParallelViews = ParseInt(name, value, 0, 64) },
                _ => result with { LogLevel = ParseLevel(value) },
            };
        }
        if (seen.Contains("ai-fill") && seen.Contains("no-ai-fill")) throw new ServerUsageException("--ai-fill and --no-ai-fill exclude each other");
        if (seen.Contains("password") && seen.Contains("password-file")) throw new ServerUsageException("--password and --password-file exclude each other");
        return result;
    }

    /// <summary>The id becomes a folder name, so it can never hold a separator or climb out of the data dir.</summary>
    public static bool IsValidGameId(string id) =>
        id.Length is >= 1 and <= 64 && char.IsAsciiLetterOrDigit(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static int ParseInt(string name, string text, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw new ServerUsageException($"--{name} must be a whole number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}, got '{text}'");
        return value;
    }

    private static ServerLogLevel ParseLevel(string text) =>
        Enum.TryParse<ServerLogLevel>(text, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : throw new ServerUsageException($"--log-level must be debug, info, warn or error, got '{text}'");
}

/// <summary>Levelled log lines on a writer (stdout): <c>2026-10-29T12:00:00.000Z INFO message</c>. Thread-safe.</summary>
public sealed class ServerLog(TextWriter output, ServerLogLevel minimum, Unciv.Net.Turns.IClock clock)
{
    private readonly Lock gate = new();

    public void Debug(string message) => Write(ServerLogLevel.Debug, message);

    public void Info(string message) => Write(ServerLogLevel.Info, message);

    public void Warn(string message) => Write(ServerLogLevel.Warn, message);

    public void Error(string message) => Write(ServerLogLevel.Error, message);

    private void Write(ServerLogLevel level, string message)
    {
        if (level < minimum) return;
        var stamp = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMillis).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var line = $"{stamp} {level.ToString().ToUpperInvariant(),-5} {message.ReplaceLineEndings(" ")}";
        lock (gate)
        {
            output.WriteLine(line);
            output.Flush();
        }
    }
}
