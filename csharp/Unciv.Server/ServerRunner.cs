using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Unciv.Json;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Turns;
using Unciv.Server.Transport;

namespace Unciv.Server;

/// <summary>
/// T265: what <c>Unciv.Server</c> does. Opens a <see cref="ServerLobby"/> on a TCP+TLS listener (the same lobby, admission and <see cref="GameHost"/> path as the in-process Host button, T205),
/// seats whoever joins, starts the game on <c>--auto-start</c> (the lobby's own start is for a host player; a dedicated server has none), and ticks the turn timer until stopped.
/// Output contract for scripts: the stdout line <c>LISTENING &lt;port&gt;</c>, then (with <c>--print-invite</c>) <c>INVITE &lt;unciv:// code&gt;</c>; everything else is a log line.
/// Persistence, resume and signals are the next ticket (T270); a stop here is cancellation of the token.
/// </summary>
public static class ServerRunner
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(24);
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
            return await ServeAsync(options, stdout, log, stop).ConfigureAwait(false);
        }
        catch (ServerUsageException e)
        {
            stderr.WriteLine("error: " + e.Message);
            return 2;
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
        var settings = LoadSettings(options);
        LoadRulesets(options, log);
        using var certificate = LoadCertificate(options.DataDir);
        var fingerprint = HostCertificate.GetFingerprint(certificate);

        await using var lobby = new ServerLobby(settings, hostFingerprint: fingerprint, hostName: "Dedicated server");
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
            stdout.WriteLine("INVITE " + InviteCode.Format(options.Address, tcp.Port, fingerprint, token));
            stdout.Flush();
        }
        log.Info($"lobby '{settings.GameName}' open: {settings.MaxMajorCivs.ToString(System.Globalization.CultureInfo.InvariantCulture)} seats, AI fill {(settings.AiFill ? "on" : "off")}, {(settings.Password != null ? "password required" : "no password")}, host key {fingerprint}");

        using var run = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var failure = (Exception?)null;
        var accept = AcceptLoopAsync(tcp, lobby, log, run.Token);
        var watch = WatchLobbyAsync(lobby, options, log, run.Token).ContinueWith(t =>
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

        tcp.Stop();
        await run.CancelAsync().ConfigureAwait(false);
        await accept.ConfigureAwait(false);
        await watch.ConfigureAwait(false);
        log.Info("stopping");
        if (failure != null) throw failure;
        return 0;
    }

    /// <summary>The settings file (the same <see cref="LobbySettings"/> JSON the host screen builds), then the command line over it.</summary>
    private static LobbySettings LoadSettings(ServerOptions options)
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
        if (options.MaxPlayers is { } max)
        {
            settings.MaxPlayers = max;
            settings.MaxMajorCivs = max;
        }
        if (options.AiFill is { } fill) settings.AiFill = fill;
        if (settings.MaxMajorCivs < 2) throw new ServerUsageException("a lobby needs at least 2 seats (MaxMajorCivs)");
        return settings;
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
            _ = HandshakeAndAttachAsync(tcp, client, lobby, log, token); // per connection: a client that sends nothing must not stall the others
        }
    }

    private static async Task HandshakeAndAttachAsync(TcpTlsListener tcp, System.Net.Sockets.TcpClient client, ServerLobby lobby, ServerLog log, CancellationToken token)
    {
        ITransport transport;
        try
        {
            transport = await tcp.HandshakeAsync(client, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            log.Debug("connection dropped before the handshake finished: " + e.Message); // a failed handshake costs only that connection
            return;
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
                log.Info($"game started: {game.Session.Game.Civilizations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} civs");
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
                return;
            }
        }
        catch (OperationCanceledException) { }
    }
}
