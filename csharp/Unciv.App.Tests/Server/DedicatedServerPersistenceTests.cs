using System.Diagnostics;
using System.Globalization;
using Unciv.Commands.Log;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Server;

namespace Unciv.Core.Tests.Server;

// T270: saving, resuming and stopping the dedicated server. The in-process tests drive ServerRunner with a token (what a signal cancels); the subprocess test starts the built executable,
// sends it a real SIGTERM and restarts it on the same data dir.
public sealed partial class DedicatedServerTests
{
    private int copies;

    private LobbySettings TwoPlayerSettings(string? password = null) => new()
    {
        GameName = "Saved",
        MaxMajorCivs = 3,
        CityStateCount = 0,
        TurnTimerSeconds = 0,
        Password = password,
        Map = new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny },
    };

    private static string RulesetsFolder => Path.Combine(RepoRoot(), "android", "assets", "jsons");

    [Fact]
    public void Parse_GameIdAndPasswordFile()
    {
        var o = ServerOptions.Parse(["--game-id", "Friends-1.a_b", "--password-file", "pw.txt"]);
        Assert.Equal("Friends-1.a_b", o.GameId);
        Assert.Equal("pw.txt", o.PasswordFile);
        Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--password", "x", "--password-file", "pw.txt"]));
        foreach (var bad in new[] { "", "../x", "a/b", ".hidden", "a b", new string('a', 65), "-x" })
            Assert.Throws<ServerUsageException>(() => ServerOptions.Parse(["--game-id", bad]));
        Assert.Contains("Exit codes", ServerOptions.HelpText);
        Assert.Contains("SIGTERM", ServerOptions.HelpText);
    }

    private async Task<(Sink Sink, Task<int> Server, InviteCode Invite)> StartInProcessAsync(CancellationToken stop, string[] extra)
    {
        var sink = new Sink();
        var server = ServerRunner.RunAsync(["--port", "0", "--data-dir", Path.Combine(root, "data"), "--jsons", RulesetsFolder, "--print-invite", "--address", "127.0.0.1", .. extra], sink, new StringWriter(), stop);
        var invite = await ReadInviteAsync(sink);
        return (sink, server, invite);
    }

    [Fact]
    public async Task Run_PasswordFile_SetsThePassword_AndIsNeverLogged()
    {
        var file = Path.Combine(root, "pw.txt");
        await File.WriteAllTextAsync(file, "s3cret-from-file\nignored second line\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var stop = new CancellationTokenSource();
        var (sink, server, invite) = await StartInProcessAsync(stop.Token, ["--password-file", file, "--auto-start"]);
        using var id = ClientIdentity.Generate();
        await using var wrong = NewClient(invite with { Token = null }, id, "W", TwoPlayerSettings(), password: "wrong");
        Assert.False((await wrong.JoinAsync().WaitAsync(Bound)).Joined);
        await using var right = NewClient(invite with { Token = null }, id, "R", TwoPlayerSettings(), password: "s3cret-from-file");
        Assert.True((await right.JoinAsync().WaitAsync(Bound)).Joined);
        await stop.CancelAsync();
        Assert.Equal(0, await server.WaitAsync(Bound));
        Assert.DoesNotContain(sink.Lines, l => l.Contains("s3cret", StringComparison.Ordinal));
        Assert.Contains(sink.Lines, l => l.Contains("password required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_PasswordFile_Problems_AreUsageErrors_AndLooseModesWarn()
    {
        var e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--password-file", Path.Combine(root, "nope.txt"), "--data-dir", root], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("does not exist", e.ToString());
        var empty = Path.Combine(root, "empty.txt");
        await File.WriteAllTextAsync(empty, "\nsecond\n");
        e = new StringWriter();
        Assert.Equal(2, await ServerRunner.RunAsync(["--password-file", empty, "--data-dir", root], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("empty first line", e.ToString());
        if (OperatingSystem.IsWindows()) return;
        var loose = Path.Combine(root, "loose.txt");
        await File.WriteAllTextAsync(loose, "pw\n");
        File.SetUnixFileMode(loose, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
        using var stop = new CancellationTokenSource();
        var (sink, server, _) = await StartInProcessAsync(stop.Token, ["--password-file", loose]);
        await stop.CancelAsync();
        Assert.Equal(0, await server.WaitAsync(Bound));
        Assert.Contains(sink.Lines, l => l.Contains("WARN", StringComparison.Ordinal) && l.Contains("can be read by other users", StringComparison.Ordinal));
        Assert.DoesNotContain(sink.Lines, l => l.Contains("pw\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_MistakesThatLeaveTheServerUseless_AreWarnedAbout()
    {
        var sink = new Sink();
        using var stop = new CancellationTokenSource();
        var server = ServerRunner.RunAsync(["--port", "0", "--data-dir", Path.Combine(root, "data"), "--jsons", RulesetsFolder, "--print-invite", "--address", "x"], sink, new StringWriter(), stop.Token);
        await sink.WaitForLineAsync("without --auto-start").WaitAsync(Bound);
        Assert.Contains(sink.Lines, l => l.Contains("without --game-id the game is not saved", StringComparison.Ordinal));
        await stop.CancelAsync();
        Assert.Equal(0, await server.WaitAsync(Bound));

        sink = new Sink();
        using var stop2 = new CancellationTokenSource();
        server = ServerRunner.RunAsync(["--port", "0", "--data-dir", Path.Combine(root, "data"), "--jsons", RulesetsFolder, "--print-invite", "--auto-start"], sink, new StringWriter(), stop2.Token);
        await sink.WaitForLineAsync("names 'localhost'").WaitAsync(Bound);
        await stop2.CancelAsync();
        Assert.Equal(0, await server.WaitAsync(Bound));

        sink = new Sink();
        using var stop3 = new CancellationTokenSource();
        server = ServerRunner.RunAsync(["--port", "0", "--data-dir", Path.Combine(root, "data"), "--jsons", RulesetsFolder, "--address", "x", "--auto-start"], sink, new StringWriter(), stop3.Token);
        await sink.WaitForLineAsync("--address only changes the invite").WaitAsync(Bound);
        await stop3.CancelAsync();
        Assert.Equal(0, await server.WaitAsync(Bound));
    }

    [Fact]
    public async Task Run_CorruptSavedGame_ExitsWithCode3_AndNoOtherServerMayShareAGameFolder()
    {
        var data = Path.Combine(root, "data");
        var bad = Path.Combine(data, "games", "bad");
        Directory.CreateDirectory(bad);
        await File.WriteAllTextAsync(Path.Combine(bad, "game.json"), "{ not json");
        await File.WriteAllTextAsync(Path.Combine(bad, "commands.log"), "garbage\n");
        var e = new StringWriter();
        Assert.Equal(3, await ServerRunner.RunAsync(["--port", "0", "--data-dir", data, "--jsons", RulesetsFolder, "--game-id", "bad"], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("cannot be loaded", e.ToString());

        using var stop = new CancellationTokenSource();
        var (_, first, _) = await StartInProcessAsync(stop.Token, ["--game-id", "shared"]);
        e = new StringWriter();
        Assert.Equal(1, await ServerRunner.RunAsync(["--port", "0", "--data-dir", data, "--jsons", RulesetsFolder, "--game-id", "shared"], new StringWriter(), e, CancellationToken.None));
        Assert.Contains("in use by another server", e.ToString());
        await stop.CancelAsync();
        Assert.Equal(0, await first.WaitAsync(Bound));
        // The lock is released with the first server: the game id is free again.
        using var stopAgain = new CancellationTokenSource();
        var (_, again, _) = await StartInProcessAsync(stopAgain.Token, ["--game-id", "shared"]);
        await stopAgain.CancelAsync();
        Assert.Equal(0, await again.WaitAsync(Bound));
    }

    /// <summary>The saved game's state hash as a fresh opener sees it (a copy, so the original keeps its clean marker).</summary>
    private (ulong Hash, int Turn, RecoveryReport Report) HashOfCopy(string gameDirectory)
    {
        var copy = Path.Combine(root, "copy-" + Interlocked.Increment(ref copies).ToString(CultureInfo.InvariantCulture));
        CopyDirectory(gameDirectory, copy);
        using var opened = FileGameStore.Open(copy);
        return (ReplayVerifier.FullHash(opened.Session.Game), opened.Session.Game.Turns, opened.Report);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    [Fact]
    public async Task Server_SavesTheGame_StopsCleanly_AndResumesWithTheSameSeats()
    {
        var data = Path.Combine(root, "data");
        var settings = TwoPlayerSettings();
        using var aliceId = ClientIdentity.Generate();
        using var bobId = ClientIdentity.Generate();
        string[] args = ["--game-id", "g1", "--max-players", "3", "--auto-start", "--min-humans", "2"];

        using var stop = new CancellationTokenSource();
        var settingsFile = Path.Combine(root, "settings.json");
        await File.WriteAllTextAsync(settingsFile, UncivJson.Serialize(settings));
        var (sink, server, invite) = await StartInProcessAsync(stop.Token, ["--settings", settingsFile, "--password", "pw", .. args]);
        await using (var alice = NewClient(invite, aliceId, "Alice", settings)) // joins with the invite token only
        await using (var bob = NewClient(invite with { Token = null }, bobId, "Bob", settings, password: "pw"))
        {
            Assert.True((await alice.JoinAsync().WaitAsync(Bound)).Joined);
            Assert.True((await bob.JoinAsync().WaitAsync(Bound)).Joined);
            await alice.SetLobbySlotAsync(0, "Rome", null, true);
            await bob.SetLobbySlotAsync(1, "Babylon", null, true);
            await WaitForGameAsync(alice);
            await WaitForGameAsync(bob);
            await Task.WhenAll(EndTurnAsync(alice), EndTurnAsync(bob));
            await Task.WhenAll(WaitForTurnAsync(alice, 1), WaitForTurnAsync(bob, 1));
            await sink.WaitForLineAsync("turn 1 Playing").WaitAsync(Bound);
            await stop.CancelAsync();
            Assert.Equal(0, await server.WaitAsync(Bound)); // the clients are still connected: the stop does not wait for them
        }
        Assert.Contains(sink.Lines, l => l.Contains("saving the game", StringComparison.Ordinal));
        Assert.EndsWith("stopped", sink.Lines[^1], StringComparison.Ordinal);

        var gameDirectory = Directory.GetDirectories(Path.Combine(data, "games")).Single();
        Assert.True(File.Exists(Path.Combine(gameDirectory, "clean")));
        GameMeta meta;
        using (var peek = FileGameStore.Open(CopyOf(gameDirectory))) meta = peek.Store.ReadMeta();
        Assert.DoesNotContain("\"pw\"", meta.LobbySettings!.Value.GetRawText(), StringComparison.Ordinal); // the join password is never saved
        Assert.Equal(2, meta.Slots.Count);
        Assert.Equal(["Rome", "Babylon"], meta.Slots.Keys.OrderByDescending(k => k).ToArray());
        Assert.Equal(aliceId.PlayerKey, meta.Slots["Rome"].PlayerKey);
        Assert.NotNull(meta.TimerPolicy);
        Assert.True(File.ReadLines(Path.Combine(gameDirectory, "commands.log")).First().Contains("Simultaneous", StringComparison.Ordinal)); // a replay starts the right turn mode
        var (hash, turn, report) = HashOfCopy(gameDirectory);
        Assert.Equal(1, turn);
        Assert.True(report.WasCleanShutdown);
        Assert.Equal(SavedHash(sink), hash); // what the live server held at the stop is what the folder replays to

        await AssertResumeEdgeCasesAsync(gameDirectory, bobId.PlayerKey);

        // Restart: the lobby is closed, the saved keys reconnect, the clock works (an EndTurn is accepted), a stranger is refused.
        using var stop2 = new CancellationTokenSource();
        var (sink2, server2, invite2) = await StartInProcessAsync(stop2.Token, ["--password", "other", .. args]); // another password: a saved seat's key needs none, a stranger needs the new one
        await sink2.WaitForLineAsync($"state hash {hash.ToString("x16", CultureInfo.InvariantCulture)}").WaitAsync(Bound);
        Assert.DoesNotContain(sink2.Lines, l => l.Contains("lobby '", StringComparison.Ordinal));
        await using (var alice = NewClient(invite2 with { Token = null }, aliceId, "Alice", settings))
        await using (var bob = NewClient(invite2 with { Token = null }, bobId, "Bob", settings))
        await using (var stranger = NewClient(invite2 with { Token = null }, ClientIdentity.Generate(), "Mallory", settings, password: "other"))
        {
            Assert.False((await stranger.JoinAsync().WaitAsync(Bound)).Joined);
            Assert.True((await alice.JoinAsync().WaitAsync(Bound)).Joined);
            Assert.True((await bob.JoinAsync().WaitAsync(Bound)).Joined);
            var a = await WaitForGameAsync(alice);
            await WaitForGameAsync(bob);
            Assert.Equal(1, a.Turn);
            Assert.Equal("Rome", a.You);
            await Task.WhenAll(EndTurnAsync(alice), EndTurnAsync(bob));
            await Task.WhenAll(WaitForTurnAsync(alice, 2), WaitForTurnAsync(bob, 2));
            await stop2.CancelAsync();
            Assert.Equal(0, await server2.WaitAsync(Bound));
        }
        Assert.Equal(2, HashOfCopy(gameDirectory).Turn);
    }

    // ServerLobby.ResumeGameAsync on copies of the saved folder: a seat nobody has reconnected to counts as dropped (else its absent player would hold the round forever),
    // and a seat whose key was cleared (a kicked player) does not stop the game from resuming.
    private async Task AssertResumeEdgeCasesAsync(string gameDirectory, string bobKey)
    {
        var opened = FileGameStore.Open(CopyOf(gameDirectory));
        await using (var lobby = new ServerLobby(TwoPlayerSettings()))
        {
            var host = await lobby.ResumeGameAsync(opened);
            await host.EnqueueAsync(() =>
            {
                Assert.False(host.TurnTimer.IsConnected("Rome"));
                Assert.False(host.TurnTimer.IsConnected("Babylon"));
                return Task.CompletedTask;
            });
            Assert.Equal(2, lobby.Lobby.Slots.Count);
            Assert.Equal(bobKey, lobby.Lobby.Slots.Single(x => x.Nation == "Babylon").PlayerKeyId);
        }

        var kicked = FileGameStore.Open(CopyOf(gameDirectory));
        var meta = kicked.Store.ReadMeta();
        meta.Slots["Babylon"] = new SlotBinding { PlayerKey = "", DisplayName = "gone" };
        kicked.Store.WriteMeta(meta);
        await using (var lobby = new ServerLobby(TwoPlayerSettings()))
        {
            await lobby.ResumeGameAsync(kicked);
            Assert.Equal(["Rome"], lobby.Lobby.Slots.Select(x => x.Nation).ToArray());
        }
    }

    /// <summary>The state hash the live server logged when it saved at the stop.</summary>
    private static ulong SavedHash(Sink sink)
    {
        var line = sink.Lines.Single(l => l.Contains("game saved:", StringComparison.Ordinal));
        return ulong.Parse(line[(line.IndexOf("state hash ", StringComparison.Ordinal) + "state hash ".Length)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private string CopyOf(string directory)
    {
        var copy = Path.Combine(root, "copy-" + Interlocked.Increment(ref copies).ToString(CultureInfo.InvariantCulture));
        CopyDirectory(directory, copy);
        return copy;
    }

    [Fact]
    public async Task Subprocess_Sigterm_SavesAndExitsZero_AndARestartKeepsTheStateHash()
    {
        if (!OperatingSystem.IsLinux()) return; // signals and the process tooling used here are Linux-only; Windows catches Ctrl+C, documented in --help
        var dll = Path.Combine(AppContext.BaseDirectory, "Unciv.Server.dll");
        Assert.True(File.Exists(dll), dll);
        var data = Path.Combine(root, "data");
        var settings = TwoPlayerSettings();
        var settingsFile = Path.Combine(root, "settings.json");
        await File.WriteAllTextAsync(settingsFile, UncivJson.Serialize(settings));
        var passwordFile = Path.Combine(root, "pw.txt");
        await File.WriteAllTextAsync(passwordFile, "table-talk\n");
        File.SetUnixFileMode(passwordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        settings.Password = "table-talk";
        string[] args = [dll, "--port", "0", "--data-dir", data, "--jsons", RulesetsFolder, "--settings", settingsFile, "--password-file", passwordFile, "--game-id", "sub", "--max-players", "3", "--auto-start", "--min-humans", "2", "--print-invite", "--address", "127.0.0.1"];
        using var aliceId = ClientIdentity.Generate();
        using var bobId = ClientIdentity.Generate();

        Sink firstSink;
        await using (var first = Subprocess.Start(args))
        {
            firstSink = first.Sink;
            var invite = await ReadInviteAsync(first.Sink);
            await using var alice = NewClient(invite with { Token = null }, aliceId, "Alice", settings, password: "table-talk");
            await using var bob = NewClient(invite with { Token = null }, bobId, "Bob", settings, password: "table-talk");
            Assert.True((await alice.JoinAsync().WaitAsync(Bound)).Joined);
            Assert.True((await bob.JoinAsync().WaitAsync(Bound)).Joined);
            await alice.SetLobbySlotAsync(0, "Rome", null, true);
            await bob.SetLobbySlotAsync(1, "Babylon", null, true);
            await WaitForGameAsync(alice);
            await WaitForGameAsync(bob);
            await Task.WhenAll(EndTurnAsync(alice), EndTurnAsync(bob));
            await Task.WhenAll(WaitForTurnAsync(alice, 1), WaitForTurnAsync(bob, 1));
            await first.Sink.WaitForLineAsync("turn 1 Playing").WaitAsync(Bound);
            first.Signal("TERM");
            Assert.Equal(0, await first.ExitAsync(Bound));
            Assert.Contains(first.Sink.Lines, l => l.Contains("saving the game", StringComparison.Ordinal));
            Assert.DoesNotContain(first.Sink.Lines, l => l.Contains("table-talk", StringComparison.Ordinal));
        }

        var gameDirectory = Path.Combine(data, "games", "sub");
        Assert.True(File.Exists(Path.Combine(gameDirectory, "clean")));
        var (hash, turn, report) = HashOfCopy(gameDirectory);
        Assert.Equal(1, turn);
        Assert.True(report.WasCleanShutdown);
        Assert.Equal(SavedHash(firstSink), hash);
        Assert.DoesNotContain("table-talk", await File.ReadAllTextAsync(Path.Combine(gameDirectory, "game.json")), StringComparison.Ordinal);

        await using var second = Subprocess.Start(args);
        var invite2 = await ReadInviteAsync(second.Sink);
        await second.Sink.WaitForLineAsync($"state hash {hash.ToString("x16", CultureInfo.InvariantCulture)}").WaitAsync(Bound); // the restarted server holds exactly the saved state
        await using var alice2 = NewClient(invite2 with { Token = null }, aliceId, "Alice", settings, password: "table-talk");
        await using var bob2 = NewClient(invite2 with { Token = null }, bobId, "Bob", settings, password: "table-talk");
        Assert.True((await alice2.JoinAsync().WaitAsync(Bound)).Joined);
        Assert.True((await bob2.JoinAsync().WaitAsync(Bound)).Joined);
        Assert.Equal(1, (await WaitForGameAsync(alice2)).Turn);
        await WaitForGameAsync(bob2);
        await Task.WhenAll(EndTurnAsync(alice2), EndTurnAsync(bob2));
        await Task.WhenAll(WaitForTurnAsync(alice2, 2), WaitForTurnAsync(bob2, 2));
        second.Signal("INT");
        Assert.Equal(0, await second.ExitAsync(Bound));
        Assert.Equal(2, HashOfCopy(gameDirectory).Turn);
    }

    /// <summary>The built executable as a child process: stdout lines go to a <see cref="Sink"/>.</summary>
    private sealed class Subprocess : IAsyncDisposable
    {
        private readonly Process process;

        private Subprocess(Process process, Sink sink)
        {
            this.process = process;
            Sink = sink;
        }

        public Sink Sink { get; }

        public static Subprocess Start(string[] args)
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
            var info = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            var sink = new Sink();
            var process = new Process { StartInfo = info };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) sink.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) sink.WriteLine("stderr: " + e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return new Subprocess(process, sink);
        }

        public void Signal(string name)
        {
            using var kill = Process.Start(new ProcessStartInfo("kill", ["-" + name, process.Id.ToString(CultureInfo.InvariantCulture)]) { UseShellExecute = false })!;
            kill.WaitForExit();
            Assert.Equal(0, kill.ExitCode);
        }

        public async Task<int> ExitAsync(TimeSpan bound)
        {
            await process.WaitForExitAsync().WaitAsync(bound);
            process.WaitForExit(); // flush the output handlers
            return process.ExitCode;
        }

        public ValueTask DisposeAsync()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true); // only this test's own child
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
