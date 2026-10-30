using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Unciv.Json;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Net.Turns;
using Unciv.Server.Transport;

namespace Unciv.Server;

/// <summary>
/// T265: what <c>Unciv.Server</c> does. Opens a <see cref="ServerLobby"/> on a TCP+TLS listener (the same lobby, admission and <see cref="GameHost"/> path as the in-process Host button, T205),
/// seats whoever joins, starts the game on <c>--auto-start</c> (the lobby's own start is for a host player; a dedicated server has none), and ticks the turn timer until stopped.
/// Output contract for scripts: the stdout line <c>LISTENING &lt;port&gt;</c>, then (with <c>--print-invite</c>) <c>INVITE &lt;unciv:// code&gt;</c>; everything else is a log line.
/// T270: with <c>--game-id</c> the game lives in <c>DATA-DIR/games/ID</c>: a folder that already holds a game is resumed before the port opens (the saved seats reconnect, the lobby is closed),
/// a new game is saved from its first moment. A stop (cancellation of the token, which <see cref="Program"/> ties to SIGINT/SIGTERM) closes the port, finishes the round, saves and exits 0.
/// </summary>
public static class ServerRunner
{
    private static int PendingHandshakes;

    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(24);
    /// <summary>Exit code: the saved game under <c>--game-id</c> cannot be loaded (see <see cref="ServerOptions.HelpText"/>).</summary>
    public const int ExitSavedGame = 3;

    /// <summary>Connections in the TLS handshake at once; more are dropped at once (a flood of silent sockets must not pile up tasks).</summary>
    private const int MaxPendingHandshakes = 128;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, CancellationToken stop)
    {
        ServerOptions options;
        try
        {
            options = ServerOptions.Parse(args);
        }
        catch (ServerUsageException e)
        {
            stderr.WriteLine("error: " + e.Message);
            stderr.WriteLine("Try --help.");
            return 2;
        }
        if (options.Help)
        {
            stdout.WriteLine(ServerOptions.HelpText);
            return 0;
        }

        var log = new ServerLog(stdout, options.LogLevel, SystemClock.Instance);
        try
        {
            var code = await ServeAsync(options, stdout, log, stop).ConfigureAwait(false);
            log.Info("stopped");
            return code;
        }
        catch (ServerUsageException e)
        {
            stderr.WriteLine("error: " + e.Message);
            return 2;
        }
        catch (SavedGameUnusableException e)
        {
            log.Error(e.Message);
            stderr.WriteLine("error: " + e.Message);
            return ExitSavedGame;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Error("server failed: " + e.Message);
            stderr.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    private static async Task<int> ServeAsync(ServerOptions options, TextWriter stdout, ServerLog log, CancellationToken stop)
    {
        var settings = LoadSettings(options, log);
        LoadRulesets(options, log);
        using var certificate = LoadCertificate(options.DataDir);
        var fingerprint = HostCertificate.GetFingerprint(certificate);

        // One server per game folder: two writers on one log would corrupt it. The lock file sits beside the folder so it also covers a game that does not exist yet.
        using var folderLock = options.GameId == null ? null : LockGame(options);
        var gameDirectory = options.GameId == null ? null : Path.Combine(options.DataDir, "games", options.GameId);
        OpenedGame? saved = null;
        if (gameDirectory != null && File.Exists(Path.Combine(gameDirectory, "commands.log"))) saved = OpenSaved(gameDirectory, log);

        await using var lobby = new ServerLobby(settings, hostFingerprint: fingerprint, hostName: "Dedicated server", gameDirectory: gameDirectory, hostLimits: options.ToHostLimits());
        GameHost? resumed = null;
        try
        {
            if (saved != null)
            {
                var store = saved;
                saved = null; // the host owns the store from here, even when the resume fails half-way
                try
                {
                    resumed = await lobby.ResumeGameAsync(store, warn: log.Warn).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    throw new SavedGameUnusableException($"the saved game in '{gameDirectory}' cannot be resumed: {e.Message}", e);
                }
                if (options.SettingsFile != null) log.Info("resumed game: the saved turn timer rules apply, the turn options of --settings are ignored");
            }
        }
        finally
        {
            saved?.Dispose();
        }

        await using var tcp = new TcpTlsListener(certificate, options.Port);
        try
        {
            tcp.Start();
        }
        catch (SocketException e)
        {
            throw new InvalidOperationException($"port {options.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)} cannot be used ({e.SocketErrorCode}); another program may be using it", e);
        }

        stdout.WriteLine("LISTENING " + tcp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        stdout.Flush();
        if (options.PrintInvite)
        {
            var token = settings.Password != null ? lobby.Lobby.Admission.MintInviteToken(InviteLifetime) : null;
            stdout.WriteLine("INVITE " + InviteCode.Format(options.EffectiveAddress, tcp.Port, fingerprint, token));
            stdout.Flush();
            if (options.Address == null) log.Warn("the invite names 'localhost', which only works on this machine: pass --address with the name or IP your players reach");
        }
        else if (options.Address != null)
        {
            log.Warn("--address only changes the invite text; add --print-invite to see it");
        }
        if (resumed != null)
            log.Info($"resumed game {options.GameId}: turn {resumed.Game.Turns.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {lobby.Lobby.Slots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} saved seat(s) may reconnect, state hash {Unciv.Commands.Log.ReplayVerifier.FullHash(resumed.Game).ToString("x16", System.Globalization.CultureInfo.InvariantCulture)}, host key {fingerprint}");
        else
        {
            log.Info($"lobby '{settings.GameName}' open: {settings.MaxMajorCivs.ToString(System.Globalization.CultureInfo.InvariantCulture)} seats, AI fill {(settings.AiFill ? "on" : "off")}, {(settings.Password != null ? "password required" : "no password")}, host key {fingerprint}");
            if (!options.AutoStart) log.Warn("without --auto-start nothing starts this lobby (a dedicated server has no host player and no admin console): the game will never begin");
            if (options.GameId == null) log.Warn("without --game-id the game is not saved: stopping the server ends it");
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var failure = (Exception?)null;
        var accept = AcceptLoopAsync(tcp, lobby, log, run.Token);
        var watch = (resumed != null ? RunGameAsync(resumed, log, run.Token) : WatchLobbyAsync(lobby, options, log, run.Token)).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                failure = t.Exception!.GetBaseException();
                log.Error("lobby watcher failed: " + failure.Message);
                run.Cancel();
            }
        }, TaskScheduler.Default);

        try { await Task.Delay(Timeout.Infinite, run.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        tcp.Stop(); // no new joins from here on
        await run.CancelAsync().ConfigureAwait(false);
        await accept.ConfigureAwait(false);
        await watch.ConfigureAwait(false);
        var finished = lobby.ActiveGameHost;
        log.Info(options.GameId != null && finished != null ? "stopping: finishing the round and saving the game" : "stopping");
        if (finished != null)
        {
            await lobby.DisposeAsync().ConfigureAwait(false); // the save happens here; a failure surfaces as exit code 1
            if (options.GameId != null)
                log.Info($"game saved: turn {finished.Game.Turns.ToString(System.Globalization.CultureInfo.InvariantCulture)}, state hash {Unciv.Commands.Log.ReplayVerifier.FullHash(finished.Game).ToString("x16", System.Globalization.CultureInfo.InvariantCulture)}");
        }
        if (failure != null) throw failure;
        return 0;
    }

    /// <summary>The settings file (the same <see cref="LobbySettings"/> JSON the host screen builds), then the command line over it.</summary>
    private static LobbySettings LoadSettings(ServerOptions options, ServerLog log)
    {
        var settings = new LobbySettings();
        if (options.SettingsFile != null)
        {
            if (!File.Exists(options.SettingsFile)) throw new ServerUsageException($"--settings '{options.SettingsFile}' does not exist");
            try
            {
                settings = UncivJson.Deserialize<LobbySettings>(File.ReadAllText(options.SettingsFile)) ?? throw new ServerUsageException($"--settings '{options.SettingsFile}' is empty");
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new ServerUsageException($"--settings '{options.SettingsFile}' is not valid lobby settings: {e.Message}");
            }
        }
        if (options.Password != null) settings.Password = options.Password;
        if (options.PasswordFile != null) settings.Password = ReadPasswordFile(options.PasswordFile, log);
        if (options.MaxPlayers is { } max)
        {
            settings.MaxPlayers = max;
            settings.MaxMajorCivs = max;
        }
        if (options.AiFill is { } fill) settings.AiFill = fill;
        if (settings.MaxMajorCivs < 2) throw new ServerUsageException("a lobby needs at least 2 seats (MaxMajorCivs)");
        return settings;
    }

    /// <summary>The first line of the file (a trailing newline is not part of the password). The password is never logged.</summary>
    private static string ReadPasswordFile(string path, ServerLog log)
    {
        if (!File.Exists(path)) throw new ServerUsageException($"--password-file '{path}' does not exist");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
            log.Warn($"--password-file '{path}' can be read by other users: restrict it (chmod 600)");
        string first;
        try
        {
            using var reader = new StreamReader(path);
            first = reader.ReadLine() ?? "";
        }
        catch (IOException e)
        {
            throw new ServerUsageException($"--password-file '{path}' cannot be read: {e.Message}");
        }
        if (first.Length == 0) throw new ServerUsageException($"--password-file '{path}' has an empty first line");
        return first;
    }

    private static FileStream LockGame(ServerOptions options)
    {
        var games = Path.Combine(options.DataDir, "games");
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(games);
        else Directory.CreateDirectory(games, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            return new FileStream(Path.Combine(games, "." + options.GameId + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException e)
        {
            throw new InvalidOperationException($"game '{options.GameId}' is in use by another server on this data dir ({e.Message})", e);
        }
    }

    private static OpenedGame OpenSaved(string directory, ServerLog log)
    {
        try
        {
            var opened = FileGameStore.Open(directory);
            var report = opened.Report;
            log.Info($"saved game recovered: {report.Outcome}{(report.WasCleanShutdown ? ", clean shutdown" : ", the last stop was not clean")}, {report.ReplayedCommands.ToString(System.Globalization.CultureInfo.InvariantCulture)} command(s) replayed");
            foreach (var note in report.Notes) log.Warn("recovery: " + note);
            if (report.DiscardedCommands > 0) log.Warn($"recovery: {report.DiscardedCommands.ToString(System.Globalization.CultureInfo.InvariantCulture)} accepted command(s) were lost (the log disagreed with its checkpoints); players may need to repeat their last moves");
            return opened;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new SavedGameUnusableException($"the saved game in '{directory}' cannot be loaded: {e.Message}", e);
        }
    }

    private static void LoadRulesets(ServerOptions options, ServerLog log)
    {
        // Embedded in-process (tests): somebody already loaded them and no explicit folder asks for another set.
        if (options.Jsons == null && options.ModsDir == null && RulesetCache.ContainsKey(BaseRuleset.Civ_V_Vanilla.FullName()))
        {
            log.Debug("rulesets already loaded");
            return;
        }
        var jsons = options.Jsons ?? FindJsons();
        if (!File.Exists(Path.Combine(jsons, "ModCategories.json"))) throw new ServerUsageException($"'{jsons}' is not a rulesets folder (no ModCategories.json); pass --jsons DIR");
        if (options.ModsDir != null && !Directory.Exists(options.ModsDir)) throw new ServerUsageException($"--mods-dir '{options.ModsDir}' is not a folder");
        RulesetCache.Clear();
        var errors = RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, options.ModsDir ?? Path.Combine(jsons, "no mods here")), noMods: options.ModsDir == null);
        if (errors.Count != 0) throw new IOException("the rulesets have errors: " + string.Join("; ", errors));
        log.Info($"rulesets loaded from {jsons}");
    }

    private static string FindJsons()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "data", "jsons");
        if (Directory.Exists(bundled)) return bundled;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
                if (Directory.Exists(candidate)) return candidate;
            }
        throw new ServerUsageException("android/assets/jsons not found; pass --jsons DIR");
    }

    private static X509Certificate2 LoadCertificate(string dataDir)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dataDir);
        else Directory.CreateDirectory(dataDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); // the private key's folder: owner only
        var path = Path.Combine(dataDir, "host-key.pfx");
        if (!OperatingSystem.IsWindows() && !File.Exists(path))
        {
            // Write the new key into a file that is owner-only from its creation (LoadOrGenerate would create it with the default umask first).
            var fresh = HostCertificate.Generate();
            using (var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
                stream.Write(fresh.Export(X509ContentType.Pfx));
            fresh.Dispose();
        }
        var certificate = HostCertificate.LoadOrGenerate(path);
        return certificate;
    }

    private static async Task AcceptLoopAsync(TcpTlsListener tcp, ServerLobby lobby, ServerLog log, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient client;
            try
            {
                client = await tcp.AcceptTcpAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (token.IsCancellationRequested || e is ObjectDisposedException) { return; }
            catch (Exception e) { log.Debug("accept failed: " + e.Message); continue; }
            if (Volatile.Read(ref PendingHandshakes) >= MaxPendingHandshakes)
            {
                log.Debug("too many connections in the handshake: dropped one");
                client.Dispose();
                continue;
            }
            _ = HandshakeAndAttachAsync(tcp, client, lobby, log, token); // per connection: a client that sends nothing must not stall the others
        }
    }

    private static async Task HandshakeAndAttachAsync(TcpTlsListener tcp, System.Net.Sockets.TcpClient client, ServerLobby lobby, ServerLog log, CancellationToken token)
    {
        ITransport transport;
        Interlocked.Increment(ref PendingHandshakes);
        try
        {
            transport = await tcp.HandshakeAsync(client, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            log.Debug("connection dropped before the handshake finished: " + e.Message); // a failed handshake costs only that connection
            return;
        }
        finally
        {
            Interlocked.Decrement(ref PendingHandshakes);
        }
        log.Debug("connection accepted");
        await lobby.AttachConnectionAsync(transport, token).ConfigureAwait(false);
    }

    /// <summary>Logs seat changes and, with <c>--auto-start</c>, starts the game; then ticks the turn timer. A failed start ends the server with exit code 1 (a script must not hang on a lobby that can never start).</summary>
    private static async Task WatchLobbyAsync(ServerLobby lobby, ServerOptions options, ServerLog log, CancellationToken token)
    {
        var last = "";
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var slots = lobby.Lobby.CreateLobbyState().Slots;
                var humans = slots.Where(s => s.Kind == nameof(SlotKind.Human)).ToList();
                var summary = $"{humans.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} human(s), {humans.Count(s => s.Ready).ToString(System.Globalization.CultureInfo.InvariantCulture)} ready";
                if (summary != last)
                {
                    log.Info("lobby: " + summary + (humans.Count == 0 ? "" : " [" + string.Join(", ", humans.Select(s => $"{s.PlayerName}{(s.Ready ? " (ready)" : "")}")) + "]"));
                    last = summary;
                }
                if (!options.AutoStart || humans.Count < options.MinHumans || humans.Any(s => !s.Ready)) continue;

                log.Info("auto-start: every seated human is ready");
                var game = await lobby.StartGameAsync().ConfigureAwait(false);
                game.Warn = log.Warn;
                log.Info($"game started: {game.Session.Game.Civilizations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} civs{(options.GameId != null ? ", saved as " + options.GameId : "")}");
                await RunGameAsync(game, log, token).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Ticks the turn timer once a second and logs each new round and phase, until the token is cancelled.</summary>
    private static async Task RunGameAsync(GameHost game, ServerLog log, CancellationToken token)
    {
        var turn = game.Session.Game.Turns;
        var phase = "";
        await game.RunTickLoopAsync(TimeSpan.FromSeconds(1), token, () =>
        {
            var state = game.Session.Game;
            var now = state.TurnState?.Phase.ToString() ?? "";
            if (state.Turns == turn && now == phase) return;
            turn = state.Turns;
            phase = now;
            log.Info($"turn {turn.ToString(System.Globalization.CultureInfo.InvariantCulture)} {now}".TrimEnd());
        }).ConfigureAwait(false);
    }
}

/// <summary>The saved game of <c>--game-id</c> cannot be opened: the runner maps it to <see cref="ServerRunner.ExitSavedGame"/>.</summary>
internal sealed class SavedGameUnusableException(string message, Exception inner) : Exception(message, inner);
