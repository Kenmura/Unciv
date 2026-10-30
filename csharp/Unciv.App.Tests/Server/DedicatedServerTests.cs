using System.Text;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.Commands.Turn;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Visibility;
using Unciv.Server;

namespace Unciv.Core.Tests.Server;

// T265: the dedicated server executable's logic. Options and settings are plain unit tests; the end-to-end test runs ServerRunner (what Program.Main calls) in this process on port 0
// and joins two scripted ClientSessions over real TCP+TLS, pinned to the fingerprint of the INVITE line.
[Collection("RulesetCache")]
public sealed partial class DedicatedServerTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private readonly string root = Directory.CreateTempSubdirectory("unciv-server-").FullName;

    public DedicatedServerTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
    }

    public void Dispose()
    {
        RulesetCache.Clear();
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    /// <summary>A writer that hands its lines to a waiter, so the test reacts to the server's stdout instead of sleeping.</summary>
    private sealed class Sink : TextWriter
    {
        private readonly StringBuilder line = new();
        private readonly List<string> lines = [];
        private readonly Lock gate = new();
        private readonly Dictionary<string, TaskCompletionSource<string>> waiters = new(StringComparer.Ordinal);

        public override Encoding Encoding => Encoding.UTF8;

        public IReadOnlyList<string> Lines
        {
            get { lock (gate) return [.. lines]; }
        }

        public override void Write(char value)
        {
            lock (gate)
            {
                if (value != '\n') { if (value != '\r') line.Append(value); return; }
                var text = line.ToString();
                line.Clear();
                lines.Add(text);
                foreach (var (fragment, tcs) in waiters) if (text.Contains(fragment, StringComparison.Ordinal)) tcs.TrySetResult(text);
            }
        }

        public Task<string> WaitForLineAsync(string fragment)
        {
            lock (gate)
            {
                var existing = lines.FirstOrDefault(l => l.Contains(fragment, StringComparison.Ordinal));
                if (existing != null) return Task.FromResult(existing);
                var tcs = waiters.TryGetValue(fragment, out var have) ? have : waiters[fragment] = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                return tcs.Task;
            }
        }
    }

    // ------------------------------------------------------------------ options

    [Fact]
    public void Parse_Defaults_AreTheDocumentedOnes()
    {
        var o = ServerOptions.Parse([]);
        Assert.Equal(47821, o.Port);
        Assert.Equal(1, o.MinHumans);
        Assert.False(o.AutoStart);
        Assert.Null(o.AiFill);
        Assert.Equal(ServerLogLevel.Info, o.LogLevel);
    }

    [Fact]
    public void ParallelViews_DefaultsOff_AndMapsToHostLimits()
    {
        var off = ServerOptions.Parse([]);
        Assert.Equal(0, off.ParallelViews);
        Assert.False(off.ToHostLimits().ParallelViewBuilding);
        Assert.False(ServerOptions.Parse(["--parallel-views", "0"]).ToHostLimits().ParallelViewBuilding);
        Assert.False(ServerOptions.Parse(["--parallel-views", "1"]).ToHostLimits().ParallelViewBuilding);
        var on = ServerOptions.Parse(["--parallel-views", "6"]).ToHostLimits();
        Assert.True(on.ParallelViewBuilding);
        Assert.Equal(6, on.MaxViewParallelism);
        Assert.Contains("--parallel-views", ServerOptions.HelpText);
    }

    [Theory]
    [InlineData("65")]
    [InlineData("-1")]
    [InlineData("x")]
    public void ParallelViews_RejectsBadValues(string value) =>
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--parallel-views", value]));

    [Fact]
    public void Parse_AllOptions()
    {
        var o = ServerOptions.Parse(["--port", "0", "--data-dir", "d", "--password", "pw", "--max-players", "5", "--no-ai-fill", "--auto-start", "--min-humans", "2",
            "--settings", "s.json", "--print-invite", "--address", "example.org", "--jsons", "j", "--mods-dir", "m", "--log-level", "DEBUG"]);
        Assert.Equal(0, o.Port);
        Assert.Equal("d", o.DataDir);
        Assert.Equal("pw", o.Password);
        Assert.Equal(5, o.MaxPlayers);
        Assert.False(o.AiFill);
        Assert.True(o.AutoStart);
        Assert.Equal(2, o.MinHumans);
        Assert.Equal("s.json", o.SettingsFile);
        Assert.True(o.PrintInvite);
        Assert.Equal("example.org", o.Address);
        Assert.Equal(ServerLogLevel.Debug, o.LogLevel);
        Assert.True(ServerOptions.Parse(["--ai-fill"]).AiFill);
        Assert.True(ServerOptions.Parse(["--port", "1", "-h"]).Help);
    }

    [Theory]
    [InlineData("--port", "70000")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "abc")]
    [InlineData("--max-players", "1")]
    [InlineData("--min-humans", "0")]
    [InlineData("--log-level", "loud")]
    [InlineData("--log-level", "7")]
    [InlineData("--password", "")]
    [InlineData("--port", null)]
    [InlineData("--nope", null)]
    [InlineData("stray", null)]
    public void Parse_BadInput_IsAUsageError(string first, string? second)
    {
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(second == null ? [first] : [first, second]));
    }

    [Fact]
    public void Parse_RepeatedOrContradictingOption_IsAUsageError()
    {
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--port", "1", "--port", "2"]));
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--ai-fill", "--no-ai-fill"]));
    }

    [Fact]
    public async Task Run_HelpAndUsageErrors_UseTheRightStreamAndExitCode()
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        Assert.Equal(0, await ServerRunner.RunAsync(["--help"], o, e, CancellationToken.None));
        Assert.Contains("LISTENING", o.ToString());
        Assert.Equal("", e.ToString());

        (o, e) = (new StringWriter(), new StringWriter());
        Assert.Equal(2, await ServerRunner.RunAsync(["--port", "x"], o, e, CancellationToken.None));
        Assert.Contains("--port", e.ToString());
        Assert.Equal("", o.ToString());
    }

    [Fact]
    public async Task Run_BadSettingsFile_IsAUsageError()
    {
        var bad = Path.Combine(root, "bad.json");
        await File.WriteAllTextAsync(bad, "{ not json");
        var e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--settings", bad, "--data-dir", root, "--port", "0"], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("not valid lobby settings", e.ToString());
        e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--settings", Path.Combine(root, "missing.json")], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("does not exist", e.ToString());
    }

    [Fact]
    public async Task Run_RulesetFolders_AreFoundOrRefused()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        foreach (var args in new[] { new[] { "--jsons", jsons }, [] }) // explicit folder, then the search upward from the working folder
        {
            RulesetCache.Clear();
            var sink = new Sink();
            using var stop = new CancellationTokenSource();
            var errors = new StringWriter();
            var server = ServerRunner.RunAsync([.. args, "--port", "0", "--data-dir", root, "--log-level", "debug"], sink, errors, stop.Token);
            var listening = sink.WaitForLineAsync("LISTENING ");
            if (await Task.WhenAny(listening, server).WaitAsync(Bound) != listening)
                Assert.Fail($"the server ended with {await server} before listening: {errors} {string.Join(" | ", sink.Lines)}");
            await sink.WaitForLineAsync("rulesets loaded from").WaitAsync(Bound);
            await stop.CancelAsync();
            Assert.Equal(0, await server.WaitAsync(Bound));
        }

        var e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--jsons", root, "--data-dir", root], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("ModCategories.json", e.ToString());
        e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--jsons", jsons, "--mods-dir", Path.Combine(root, "nope"), "--data-dir", root], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("--mods-dir", e.ToString());
    }

    [Fact]
    public async Task Run_SettingsWithFewerThanTwoSeats_IsAUsageError()
    {
        var file = Path.Combine(root, "one.json");
        await File.WriteAllTextAsync(file, UncivJson.Serialize(new LobbySettings { MaxMajorCivs = 1 }));
        var e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--settings", file, "--data-dir", root], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("at least 2 seats", e.ToString());
    }

    [Fact]
    public async Task Run_AutoStartThatCannotStart_ExitsWithFailure()
    {
        // One human, no AI fill: the lobby refuses to start a one-player game, and a script must not wait on it forever.
        var sink = new Sink();
        var errors = new StringWriter();
        var server = ServerRunner.RunAsync(["--port", "0", "--data-dir", root, "--no-ai-fill", "--auto-start", "--print-invite"], sink, errors, CancellationToken.None);
        var invite = await ReadInviteAsync(sink);
        Assert.Null(invite.Token); // no password: the invite is just host, port and fingerprint
        var settings = new LobbySettings();
        using var id = ClientIdentity.Generate();
        await using var solo = NewClient(invite, id, "Solo", settings);
        Assert.True((await solo.JoinAsync().WaitAsync(Bound)).Joined);
        await solo.SetLobbySlotAsync(0, "Rome", null, true);
        Assert.Equal(1, await server.WaitAsync(Bound));
        Assert.Contains("at least two players", errors.ToString());
        Assert.Contains(sink.Lines, l => l.Contains("lobby watcher failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_PortInUse_ExitsWithFailure()
    {
        using var taken = new System.Net.Sockets.TcpListener(System.Net.IPAddress.IPv6Any, 0);
        taken.Server.DualMode = true;
        taken.Start();
        var port = ((System.Net.IPEndPoint)taken.LocalEndpoint).Port;
        var e = new StringWriter();
        Assert.Equal(1, await ServerRunner.RunAsync(["--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--data-dir", root], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("cannot be used", e.ToString());
    }

    [Fact]
    public void LobbySettings_RoundTripThroughTheSharedJson()
    {
        var settings = new LobbySettings { GameName = "G", Speed = "Quick", MaxMajorCivs = 6, Password = "x", TurnTimerSeconds = 30, Map = { Seed = 99 } };
        var copy = UncivJson.Deserialize<LobbySettings>(UncivJson.Serialize(settings))!;
        Assert.Equal("G", copy.GameName);
        Assert.Equal("Quick", copy.Speed);
        Assert.Equal(6, copy.MaxMajorCivs);
        Assert.Equal(99, copy.Map.Seed);
        Assert.Equal(settings.ComputeRulesetHash(), copy.ComputeRulesetHash());
    }

    [Fact]
    public void Log_FiltersByLevel_AndKeepsOneLinePerMessage()
    {
        var sink = new StringWriter();
        var log = new ServerLog(sink, ServerLogLevel.Warn, new FixedClock());
        log.Debug("d");
        log.Info("i");
        log.Warn("w\nx");
        log.Error("e");
        var lines = sink.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["1970-01-01T00:00:01.500Z WARN  w x", "1970-01-01T00:00:01.500Z ERROR e"], lines);
    }

    private sealed class FixedClock : Unciv.Net.Turns.IClock
    {
        public long NowMillis => 1500;
    }

    // ------------------------------------------------------------------ the server

    private static ClientSession NewClient(InviteCode invite, ClientIdentity id, string name, LobbySettings settings, string? password = null) =>
        new(ct => TcpTlsClientTransport.ConnectAsync("127.0.0.1", invite.Port, invite.Fingerprint, cancellationToken: ct), new ClientSessionOptions
        {
            Identity = id,
            DisplayName = name,
            RulesetHash = settings.ComputeRulesetHash(),
            ExpectedFingerprint = invite.Fingerprint,
            Password = password,
            InviteToken = invite.Token,
            MaxReconnectAttempts = 0,
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            HostSilenceTimeout = Timeout.InfiniteTimeSpan,
        });

    private static async Task<InviteCode> ReadInviteAsync(Sink sink)
    {
        var port = await sink.WaitForLineAsync("LISTENING ").WaitAsync(Bound);
        var invite = await sink.WaitForLineAsync("INVITE ").WaitAsync(Bound);
        Assert.True(InviteCode.TryParse(invite["INVITE ".Length..], out var parsed));
        Assert.Equal(int.Parse(port["LISTENING ".Length..], System.Globalization.CultureInfo.InvariantCulture), parsed!.Port);
        return parsed;
    }

    [Fact]
    public async Task Server_TwoScriptedClientsJoinReadyUpAndPlayATurn_OverTcpTls()
    {
        var settingsFile = Path.Combine(root, "settings.json");
        var settings = new LobbySettings { GameName = "Scripted", MaxMajorCivs = 3, CityStateCount = 0, TurnTimerSeconds = 0, Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }, Password = "from-file-but-overridden" };
        await File.WriteAllTextAsync(settingsFile, UncivJson.Serialize(settings));
        settings.Password = "letmein";

        var sink = new Sink();
        using var stop = new CancellationTokenSource();
        var server = ServerRunner.RunAsync(["--port", "0", "--data-dir", Path.Combine(root, "data"), "--settings", settingsFile, "--password", "letmein", "--max-players", "3",
            "--auto-start", "--min-humans", "2", "--print-invite", "--log-level", "debug"], sink, new StringWriter(), stop.Token);
        var invite = await ReadInviteAsync(sink);
        Assert.NotNull(invite.Token); // a password is set, so the invite carries a token
        Assert.True(File.Exists(Path.Combine(root, "data", "host-key.pfx")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(root, "data", "host-key.pfx")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(root, "data")));
        }
        using var idle = new System.Net.Sockets.TcpClient("127.0.0.1", invite.Port); // silent connection: must not delay the real joins below

        using var aliceId = ClientIdentity.Generate();
        using var bobId = ClientIdentity.Generate();
        await using var alice = NewClient(invite, aliceId, "Alice", settings, password: "letmein");
        await using var bob = NewClient(invite with { Token = null }, bobId, "Bob", settings, password: "letmein");
        await using var intruder = NewClient(invite with { Token = null }, ClientIdentity.Generate(), "Mallory", settings, password: "wrong");

        using (var junk = new System.Net.Sockets.TcpClient("127.0.0.1", invite.Port)) // not TLS: the handshake fails and costs only that connection
        {
            await junk.GetStream().WriteAsync(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        }
        await sink.WaitForLineAsync("before the handshake finished").WaitAsync(Bound);

        Assert.False((await intruder.JoinAsync().WaitAsync(Bound)).Joined);
        Assert.True((await alice.JoinAsync().WaitAsync(Bound)).Joined);
        Assert.True((await bob.JoinAsync().WaitAsync(Bound)).Joined);

        // One human ready is not enough: --min-humans 2.
        await alice.SetLobbySlotAsync(0, "Rome", null, true);
        await sink.WaitForLineAsync("Alice (ready)").WaitAsync(Bound);
        Assert.DoesNotContain(sink.Lines, l => l.Contains("auto-start", StringComparison.Ordinal));
        await bob.SetLobbySlotAsync(1, "Babylon", null, true);

        var started = await WaitForGameAsync(alice);
        await WaitForGameAsync(bob);
        Assert.Equal(0, started.Turn);
        await Task.WhenAll(EndTurnAsync(alice), EndTurnAsync(bob));
        await Task.WhenAll(WaitForTurnAsync(alice, 1), WaitForTurnAsync(bob, 1)); // the AI civ moved too: the round resolved
        Assert.Contains(sink.Lines, l => l.Contains("game started", StringComparison.Ordinal));
        await sink.WaitForLineAsync("turn 1 Playing").WaitAsync(Bound); // the host's tick loop notices the new round

        await stop.CancelAsync();
        Assert.Equal(0, await server.WaitAsync(Bound));
    }

    private static async Task<ClientSnapshot> WaitForGameAsync(ClientSession session)
    {
        using var cts = new CancellationTokenSource(Bound);
        if (session.Game.Snapshot != null) return session.Game.Snapshot;
        await foreach (var ev in session.Events.ReadAllAsync(cts.Token))
            if (ev is SnapshotReplaced replaced) return replaced.Snapshot;
        throw new InvalidOperationException("the session ended before the game started");
    }

    private static async Task EndTurnAsync(ClientSession session)
    {
        var outcome = await session.SubmitAsync(new EndTurn()).WaitAsync(Bound);
        Assert.True(outcome.Accepted, "end " + outcome.Code);
    }

    private static async Task WaitForTurnAsync(ClientSession session, int turn)
    {
        using var cts = new CancellationTokenSource(Bound);
        while (session.Game.Snapshot is not { } s || s.Turn < turn)
        {
            if (!await session.Events.WaitToReadAsync(cts.Token)) throw new InvalidOperationException("session ended");
            session.Events.TryRead(out _);
        }
    }
}
