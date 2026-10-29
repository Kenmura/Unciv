using System.Text.Json;
using Unciv.Client.Net.Session;
using Unciv.Commands;
using Unciv.Commands.City;
using Unciv.Commands.Empire;
using Unciv.Commands.Log;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Net.Turns;
using Unciv.Net.Visibility;
using Unciv.Server;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Server;

// T182: the server track's acceptance. Real ClientSessions (T180) play against a real GameHost on a file store (T194/T205) over the in-memory transport; the only fake is time
// (a ManualClock the test advances) and the admission glue (the same Hello/Welcome/Join/Auth/JoinOk steps as InProcessHost, minus the TCP listener, so a restarted host can take
// the reconnecting sessions without a port). Nothing here sleeps for a duration: every wait is on a condition the sessions or the host reach (state/event driven), bounded by a
// generous guard so a regression fails instead of hanging.
//
// One long scenario, not many short ones: 3 humans + 1 AI play 24 simultaneous rounds with interleaved commands and these things happening on the way:
//   round 3  a client submits tampered commands (foreign unit, server-only command, unknown type): all refused, the log does not move
//   round 4  the turn timer fires for an idle player (logged ForceEndTurn, the round resolves)
//   round 7  a client drops, its drop grace expires (ForceEndTurn), it returns and its end is undone (UndoEndTurn)
//   round 11 the host dies in the middle of Resolving; a new host opens the crash image, finishes the round with NextResolveStep; every client reconnects (Resume)
//   round 14 a fourth person joins into the AI slot between turns
// and at the end: server fullHash == replay of the log from its start, each mirror's viewHash == the viewer's filtered snapshot, and no mirror holds an unexplored tile.
[Collection("RulesetCache")]
public sealed class ServerEndToEndTests : IDisposable
{
    private const long Seed = 21L;
    private const long StartClock = 5_000L;
    private const int TargetTurn = 25;
    private const int TimerTurn = 4;
    private const int DropTurn = 7;
    private const int CrashTurn = 11;
    private const int LateJoinTurn = 14;
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    private static readonly TurnPolicy Policy = new() { TurnTimerSeconds = 300, DropGraceSeconds = 120 };
    private static readonly string Fingerprint = HostKey.Fingerprint(new byte[] { 9, 8, 7, 6 });

    private readonly RulesetClass ruleset;
    private readonly string root = Directory.CreateTempSubdirectory("unciv-e2e-").FullName;

    public ServerEndToEndTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
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

    // ------------------------------------------------------------------ the scenario

    [Fact]
    public async Task ServerEndToEnd_ThreeHumansAndAi_SurviveTimerDropCrashAndLateJoin_WithAgreeingHashes()
    {
        using var babylonKey = ClientIdentity.Generate();
        using var greeceKey = ClientIdentity.Generate();
        using var egyptKey = ClientIdentity.Generate();
        using var romeKey = ClientIdentity.Generate();
        var world = new World(root, ruleset, Policy);
        world.Start(new Dictionary<string, ClientIdentity> { ["Babylon"] = babylonKey, ["Greece"] = greeceKey, ["Egypt"] = egyptKey });
        await using var _ = world;

        var babylon = world.Connect("Babylon", babylonKey);
        var greece = world.Connect("Greece", greeceKey);
        var egypt = world.Connect("Egypt", egyptKey);
        var humans = new[] { babylon, greece, egypt };
        foreach (var player in humans) Assert.True((await player.Session.JoinAsync().WaitAsync(Guard)).Joined);
        foreach (var player in humans) await player.WaitAsync(s => s.Turn == 0 && s.Phase == "Playing");
        Assert.Equal(4, await world.OnActorAsync(rig => rig.Host.Game.Civilizations.Count(it => it.IsMajorCiv())));

        await AssertForgedAndUnboundJoinsRefusedAsync(world, greeceKey);

        var round = 0;
        var players = new List<Seat>(humans);
        while (round < TargetTurn)
        {
            if (round == LateJoinTurn)
            {
                var rome = world.Connect("Rome", romeKey, bind: true);
                Assert.True((await rome.Session.JoinAsync().WaitAsync(Guard)).Joined);
                await rome.WaitAsync(s => s.Turn == round && s.Phase == "Playing");
                Assert.True(await world.OnActorAsync(rig => rig.Host.Game.GetCivilization("Rome").IsHuman()));
                players.Add(rome);
            }

            world.Clock.AdvanceSeconds(30); // the host's rate limiter refills on its clock
            switch (round)
            {
                case 3: await TamperedRoundAsync(world, babylon, greece, egypt); break;
                case TimerTurn: await TimerRoundAsync(world, babylon, greece, egypt); break;
                case DropTurn: await DropRoundAsync(world, babylon, greece, egypt); break;
                case CrashTurn: await CrashRoundAsync(world, babylon, greece, egypt); break;
                default: await Task.WhenAll(players.Select(it => it.PlayTurnAsync(world))); break;
            }

            round++;
            foreach (var player in players) await player.WaitAsync(s => s.Turn == round && s.Phase == "Playing");
            await AssertViewsAgreeAsync(world, players);
        }

        // ---- the acceptance: everything the host did, as the log says it, replays to the host's state
        var (fullHash, replay, log) = await world.ReplayLogAsync();
        Assert.True(replay.Matches, $"replay diverged at seq {replay.DivergedAtSeq}");
        Assert.Equal(fullHash, replay.FinalHash);
        Assert.True(replay.CheckpointsVerified >= TargetTurn - 1, $"checkpoints {replay.CheckpointsVerified}");

        var types = log.Lines.OfType<CommandLogLine.Command>().Select(it => (it.Envelope.Type, Payload: it.Envelope.Payload.GetRawText())).ToList(); // the whole log, across the restart
        Assert.Contains(types, it => it.Type == "force-end-turn" && it.Payload.Contains("Egypt"));   // the timer
        Assert.Contains(types, it => it.Type == "force-end-turn" && it.Payload.Contains("Greece"));  // the drop grace
        Assert.Contains(types, it => it.Type == "undo-end-turn");                                    // the return
        Assert.Contains(types, it => it.Type == "set-controller" && it.Payload.Contains("Rome"));    // the late join
        Assert.Equal(1, world.Restarts);
        Assert.False((await world.OnActorAsync(rig => rig.Report))!.WasCleanShutdown); // the restart really was a crash recovery
        Assert.Equal(4, players.Count);
        Assert.Empty(players.SelectMany(it => it.Forced)); // diagnostic: a player the script could not finish
        Assert.Contains(greece.Seen, it => it is Desynchronized { Reason: "reconnect" });
    }

    // ------------------------------------------------------------------ the special rounds

    private static async Task TamperedRoundAsync(World world, Seat babylon, Seat greece, Seat egypt)
    {
        var (foreignUnit, seq, hash) = await world.OnActorAsync(rig => (rig.Host.Game.GetCivilization("Greece").Units.GetCivUnits().First().Id, rig.Host.Session.LastSeq, ReplayVerifier.FullHash(rig.Host.Game)));

        var moveForeign = await babylon.Session.SubmitAsync("move-unit", JsonSerializer.SerializeToElement(new { unitId = foreignUnit, to = new { x = 0, y = 0 } })).WaitAsync(Guard);
        var serverOnly = await babylon.Session.SubmitAsync(new SetController { CivId = "Rome", Controller = PlayerType.Human, PlayerId = "x" }).WaitAsync(Guard);
        var forceOther = await babylon.Session.SubmitAsync(new ForceEndTurn { CivId = "Greece" }).WaitAsync(Guard);
        var unknownType = await babylon.Session.SubmitAsync("teleport-all-units", JsonSerializer.SerializeToElement(new { })).WaitAsync(Guard);
        var actorSpoof = await babylon.Session.SubmitAsync("end-turn", JsonSerializer.SerializeToElement(new { actorId = "Greece" })).WaitAsync(Guard);

        Assert.False(moveForeign.Accepted);
        Assert.False(serverOnly.Accepted);
        Assert.False(forceOther.Accepted);
        Assert.False(unknownType.Accepted);
        _ = actorSpoof; // whatever the host decides about it, it acts for Babylon (the session's civ) and never for Greece
        var (afterSeq, ended, actors) = await world.OnActorAsync(rig => (rig.Host.Session.LastSeq, rig.Host.Game.TurnState!.Ended.ToList(), rig.Host.Session.Log.Where(it => it.Seq > seq).Select(it => it.ActorId).ToList()));
        Assert.DoesNotContain("Greece", ended);
        Assert.All(actors, it => Assert.Equal("Babylon", it));
        Assert.True(afterSeq - seq <= 1);
        if (afterSeq == seq) Assert.Equal(hash, await world.OnActorAsync(rig => ReplayVerifier.FullHash(rig.Host.Game))); // refused commands change nothing

        // The honest round goes on around the attacker.
        await Task.WhenAll(babylon.PlayTurnAsync(world), greece.PlayTurnAsync(world), egypt.PlayTurnAsync(world));
    }

    private static async Task TimerRoundAsync(World world, Seat babylon, Seat greece, Seat egypt)
    {
        await Task.WhenAll(babylon.PlayTurnAsync(world), greece.PlayTurnAsync(world));
        Assert.Equal(["Babylon", "Greece"], (await world.OnActorAsync(rig => rig.Host.Game.TurnState!.Ended.OrderBy(it => it, StringComparer.Ordinal).ToList())));
        world.Clock.AdvanceSeconds(Policy.TurnTimerSeconds + 1);
        await world.TickAsync(); // Egypt never ended: the host logs ForceEndTurn(Egypt), resolves, starts the next round
        Assert.True(await world.OnActorAsync(rig => rig.Host.Game.Turns) > TimerTurn);
        _ = egypt;
    }

    private static async Task DropRoundAsync(World world, Seat babylon, Seat greece, Seat egypt)
    {
        await greece.DropAsync();
        while (await world.OnActorAsync(rig => rig.Host.TurnTimer.IsConnected("Greece"))) await Task.Yield();

        await babylon.PlayTurnAsync(world);
        world.Clock.AdvanceSeconds(Policy.DropGraceSeconds + 1);
        await world.TickAsync();
        Assert.True(await world.OnActorAsync(rig => rig.Host.Game.HasEndedTurn(rig.Host.Game.GetCivilization("Greece")))); // forced
        Assert.Equal(SimultaneousPhase.Playing, await world.OnActorAsync(rig => rig.Host.Game.TurnState!.Phase));

        greece.Reconnect();
        while (!await world.OnActorAsync(rig => rig.Host.Sessions.Values.Any(it => it.BoundCivId == "Greece"))) await Task.Yield();
        var seq = await world.OnActorAsync(rig => rig.Host.Session.LastSeq);
        await greece.WaitAsync(s => s.Seq >= seq && !s.TurnStatus.Ended.Contains("Greece"));
        Assert.False(await world.OnActorAsync(rig => rig.Host.Game.HasEndedTurn(rig.Host.Game.GetCivilization("Greece")))); // the end was undone

        await Task.WhenAll(greece.PlayTurnAsync(world), egypt.PlayTurnAsync(world));
    }

    private static async Task CrashRoundAsync(World world, Seat babylon, Seat greece, Seat egypt)
    {
        await Task.WhenAll(babylon.PlayTurnAsync(world), greece.PlayTurnAsync(world), egypt.PlayCommandsAsync(world));
        var crashImage = Path.Combine(world.Root, "crash-image");
        var turn = await world.OnActorAsync(rig =>
        {
            // The last human ends through the session, no auto-pump: the round is Resolving and one step has been applied when the process "dies".
            Assert.True(rig.Host.Session.Submit("Egypt", new EndTurn()).Accepted);
            Assert.Equal(SimultaneousPhase.Resolving, rig.Host.Game.TurnState!.Phase);
            rig.Host.ApplyServerCommand(ResolvePump.StepCommand(rig.Host.Game.NextResolveStep()!));
            Assert.Equal(SimultaneousPhase.Resolving, rig.Host.Game.TurnState!.Phase);
            World.CopyDirectory(rig.Store.DirectoryPath, crashImage);
            return rig.Host.Game.Turns;
        });
        Assert.False(File.Exists(Path.Combine(crashImage, "clean")));

        await world.RestartFromAsync(crashImage); // old host gone, clients wait in Reconnecting until the new one is up
        Assert.Equal(SimultaneousPhase.Playing, await world.OnActorAsync(rig => rig.Host.Game.TurnState!.Phase));
        Assert.Equal(turn + 1, await world.OnActorAsync(rig => rig.Host.Game.Turns)); // NextResolveStep continued the round
        Assert.Null(await world.OnActorAsync(rig => rig.Host.Game.NextResolveStep()));
    }

    private static async Task AssertForgedAndUnboundJoinsRefusedAsync(World world, ClientIdentity greeceKey)
    {
        using var stranger = ClientIdentity.Generate();
        // Greece's key with a signature of somebody else's.
        Assert.Equal(JoinRefusedCodes.AuthFailed, await world.RawJoinAsync(greeceKey.PlayerKey, stranger));
        // A key that owns no seat: the lobby is closed.
        Assert.Equal(JoinRefusedCodes.GameAlreadyStarted, await world.RawJoinAsync(stranger.PlayerKey, stranger));
        Assert.Equal(3, await world.OnActorAsync(rig => rig.Host.Sessions.Count));
    }

    // ------------------------------------------------------------------ the invariants

    private static async Task AssertViewsAgreeAsync(World world, IReadOnlyList<Seat> players)
    {
        foreach (var player in players)
        {
            // Wait for the mirror to reach the host's last command (the last delta of a round may still be on its way).
            var seq = await world.OnActorAsync(rig => rig.Host.Session.LastSeq);
            await player.WaitAsync(s => s.Seq >= seq);
            var (expected, unexplored) = await world.OnActorAsync(rig =>
            {
                var game = rig.Host.Game;
                var civ = game.GetCivilization(player.CivId);
                var status = new TurnStatusDto { Ended = game.TurnState?.Ended.ToList() ?? [], Deadline = rig.Host.TurnTimer.TurnDeadline, Phase = (game.TurnState?.Phase ?? SimultaneousPhase.Playing).ToString() };
                var filtered = SnapshotBuilder.Build(game, civ, rig.Host.Session.LastSeq, status);
                var mirror = player.Session.Game.Snapshot!;
                var leaked = mirror.Tiles.Where(t => !game.TileMap[t.X, t.Y].IsExplored(civ)).Select(t => (t.X, t.Y)).ToList();
                return (filtered, leaked);
            });
            var mirrored = player.Session.Game.Snapshot!;
            Assert.Empty(unexplored);
            Assert.Equal(SnapshotBuilder.ComputeViewHash(mirrored with { ViewHash = null }), mirrored.ViewHash);
            if (SnapshotBuilder.ComputeViewHash(expected) != mirrored.ViewHash)
            {
                var a = SnapshotBuilder.ToCanonicalJson(expected);
                var b = SnapshotBuilder.ToCanonicalJson(mirrored);
                var at = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i]);
                Assert.Fail($"{player.CivId} turn {mirrored.Turn} seq {mirrored.Seq}/{expected.Seq}: views differ at {at}: host ...{a.Substring(Math.Max(0, at - 150), Math.Min(300, a.Length - Math.Max(0, at - 150)))} || mirror ...{b.Substring(Math.Max(0, at - 150), Math.Min(300, b.Length - Math.Max(0, at - 150)))}");
            }
            Assert.Equal(SnapshotBuilder.ToCanonicalJson(expected), SnapshotBuilder.ToCanonicalJson(mirrored));
        }
    }

    // ------------------------------------------------------------------ lobby-side acceptance (password, host key) with the real client

    [Fact]
    public async Task ServerEndToEnd_Lobby_WrongPasswordIsRefusedAndHostKeyMismatchHardFails()
    {
        var settings = new LobbySettings { Password = "open sesame" };
        await using var lobby = new LobbyHost(settings: settings, hostFingerprint: Fingerprint);
        using var key = ClientIdentity.Generate();

        async Task<JoinResult> JoinAsync(string? password, string expectedFingerprint)
        {
            var (client, host) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(host);
            await using var session = new ClientSession(_ => Task.FromResult<ITransport>(client), new ClientSessionOptions
            {
                Identity = key,
                DisplayName = "Mallory",
                RulesetHash = settings.ComputeRulesetHash(),
                ExpectedFingerprint = expectedFingerprint,
                Password = password,
                MaxReconnectAttempts = 0,
                HandshakeTimeout = Timeout.InfiniteTimeSpan,
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
                HostSilenceTimeout = Timeout.InfiniteTimeSpan,
            });
            return await session.JoinAsync().WaitAsync(Guard);
        }

        Assert.Equal(new JoinResult(false, JoinRefusedCodes.BadCredentials), await JoinAsync("wrong", Fingerprint));
        Assert.Equal(new JoinResult(false, ClientCodes.HostKeyMismatch), await JoinAsync("open sesame", HostKey.Fingerprint(new byte[] { 1, 1, 1 })));
        Assert.True((await JoinAsync("open sesame", Fingerprint)).Joined);
    }

    // ------------------------------------------------------------------ harness

    /// <summary>The host side: one GameHost on a game directory, a manual clock, the admission glue.</summary>
    private sealed class HostRig : IAsyncDisposable
    {
        public required GameHost Host { get; init; }
        public required FileGameStore Store { get; init; }
        public RecoveryReport? Report { get; init; }

        public async Task AdmitAsync(ITransport transport)
        {
            try
            {
                if (await ReceiveAsync(transport) is not Hello) { await RefuseAsync(transport, JoinRefusedCodes.ProtocolMismatch); return; }
                var challenge = new HostChallenge(Fingerprint, SystemSecureRandom.Instance);
                await transport.SendAsync(ProtocolCodec.Encode(new Welcome { Protocol = ProtocolVersion.Current, SessionId = challenge.SessionId, Challenge = challenge.Challenge, HostKeyFingerprint = Fingerprint, RequiresPassword = false }));
                if (await ReceiveAsync(transport) is not Join join || !PlayerKeys.IsValid(join.PlayerKey)) { await RefuseAsync(transport, JoinRefusedCodes.BadKey); return; }
                if (await ReceiveAsync(transport) is not Auth auth || !challenge.Verify(join.PlayerKey, auth.Proof)) { await RefuseAsync(transport, JoinRefusedCodes.AuthFailed); return; }
                var civ = Store.ReadMeta().Slots.FirstOrDefault(x => string.Equals(x.Value.PlayerKey, join.PlayerKey, StringComparison.Ordinal)).Key;
                if (civ == null) { await RefuseAsync(transport, JoinRefusedCodes.GameAlreadyStarted); return; }
                await transport.SendAsync(ProtocolCodec.Encode(new JoinOk()));
                await Host.AttachSessionAsync(challenge.SessionId, civ, transport, join.PlayerKey);
            }
            catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or IOException)
            {
                await transport.CloseAsync(); // the peer went away mid-handshake (a restart racing a reconnect): that connection is lost, nothing else
            }
        }

        private static async Task<Message?> ReceiveAsync(ITransport transport)
        {
            using var cts = new CancellationTokenSource(Guard);
            var frame = await transport.ReceiveAsync(cts.Token);
            return frame == null ? null : ProtocolCodec.Decode(frame);
        }

        private static async Task RefuseAsync(ITransport transport, string code)
        {
            await transport.SendAsync(ProtocolCodec.Encode(new JoinRefused { Code = code }));
            await transport.CloseAsync();
        }

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private sealed class World(string root, RulesetClass ruleset, TurnPolicy policy) : IAsyncDisposable
    {
        private static readonly StoreOptions StoreSettings = new() { RotateLogBytes = 0, SnapshotEveryTurns = 5 };
        private TaskCompletionSource<HostRig> rigReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HostRig? rig;
        private readonly List<Seat> connected = [];

        public string Root { get; } = root;
        public ManualClock Clock { get; } = new(StartClock);
        public int Restarts { get; private set; }

        public void Start(IReadOnlyDictionary<string, ClientIdentity> humans)
        {
            var setup = new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = ruleset.Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = ruleset.Victories.Keys.ToList(),
                    Players = [.. humans.Keys.Select(it => new Player(it, PlayerType.Human)), new Player("Rome", PlayerType.AI)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
            var game = GameStarter.StartNewGame(setup, Seed, StartClock);
            game.StartSimultaneousTurns();
            var header = CommandLogHeader.Create(setup, Seed, StartClock) with { TurnMode = CommandLogHeader.SimultaneousTurns };
            var meta = new GameMeta { Slots = new SortedDictionary<string, SlotBinding>(humans.ToDictionary(x => x.Key, x => new SlotBinding { PlayerKey = x.Value.PlayerKey, DisplayName = x.Key }), StringComparer.Ordinal) };
            var store = FileGameStore.Create(Path.Combine(Root, "game"), game.GameId, header, meta, game, StoreSettings);
            Publish(new HostRig { Host = NewHost(new GameSession(game) { Clock = () => Clock.NowMillis }, store), Store = store });
        }

        private GameHost NewHost(GameSession session, FileGameStore store) =>
            new(session, store, new TurnTimer(session, policy, Clock, autoResolve: false), gate: new CommandGate(store, new CommandGateLimits(Capacity: 1000), Clock), clock: Clock, limits: new HostLimits { IdleTimeoutMillis = long.MaxValue / 4 });

        private void Publish(HostRig next)
        {
            rig = next;
            rigReady.TrySetResult(next);
        }

        /// <summary>Kills the running host (its clients start reconnecting), then opens <paramref name="directory"/> like InProcessHost.ResumeFromDirectory does and lets them in.</summary>
        public async Task RestartFromAsync(string directory)
        {
            rigReady = new TaskCompletionSource<HostRig>(TaskCreationOptions.RunContinuationsAsynchronously);
            var old = rig!;
            rig = null;
            await old.DisposeAsync();
            var opened = FileGameStore.Open(directory, StoreSettings);
            var session = new GameSession(opened.Session.Game, lastSeq: opened.Session.LastSeq) { Clock = () => Clock.NowMillis };
            var host = new HostRig { Host = NewHost(session, opened.Store), Store = opened.Store, Report = opened.Report };
            await host.Host.EnqueueAsync(() =>
            {
                host.Host.PumpResolving();
                return Task.CompletedTask;
            });
            Restarts++;
            Publish(host);
            // Every client is back (resumed) before the scenario goes on.
            foreach (var player in connected)
                while (!await OnActorAsync(r => r.Host.Sessions.Values.Any(it => it.BoundCivId == player.CivId))) await Task.Yield();
        }

        public Seat Connect(string civId, ClientIdentity identity, bool bind = false)
        {
            if (bind) rig!.Store.WriteMeta(rig.Store.ReadMeta() with { Slots = new SortedDictionary<string, SlotBinding>(rig.Store.ReadMeta().Slots.Append(new(civId, new SlotBinding { PlayerKey = identity.PlayerKey, DisplayName = civId })).ToDictionary(x => x.Key, x => x.Value), StringComparer.Ordinal) });
            var player = new Seat(this, civId, identity);
            connected.Add(player);
            return player;
        }

        public async Task<T> OnActorAsync<T>(Func<HostRig, T> read)
        {
            var current = await rigReady.Task.WaitAsync(Guard);
            T result = default!;
            await current.Host.EnqueueAsync(() =>
            {
                result = read(current);
                return Task.CompletedTask;
            }).WaitAsync(Guard);
            return result;
        }

        public Task TickAsync() => OnActorAsync(r =>
        {
            r.Host.Tick();
            return 0;
        });

        public async Task<(ulong FullHash, ReplayResult Replay, CommandLog Log)> ReplayLogAsync() => await OnActorAsync(r =>
        {
            r.Store.Sync(r.Host.Session);
            using var reader = new StreamReader(new FileStream(Path.Combine(r.Store.DirectoryPath, "commands.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var log = CommandLogReader.Read(reader);
            return (ReplayVerifier.FullHash(r.Host.Game), ReplayVerifier.Verify(log), log);
        });

        /// <summary>A raw join with the given key and a proof signed by <paramref name="signer"/>; the host's answer code.</summary>
        public async Task<string> RawJoinAsync(string playerKey, ClientIdentity signer)
        {
            var (client, host) = InMemoryTransport.CreatePair();
            var admitted = (await rigReady.Task).AdmitAsync(host);
            await client.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "test", RulesetHash = "", ModList = [] }));
            var welcome = Assert.IsType<Welcome>(await Next(client));
            await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = playerKey, DisplayName = "x" }));
            await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = signer.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
            var answer = Assert.IsType<JoinRefused>(await Next(client));
            await admitted.WaitAsync(Guard);
            return answer.Code;

            static async Task<Message> Next(ITransport transport)
            {
                var frame = await transport.ReceiveAsync().AsTask().WaitAsync(Guard);
                return ProtocolCodec.Decode(frame ?? throw new InvalidOperationException("closed"));
            }
        }

        public async Task<ITransport> OpenTransportAsync(CancellationToken token)
        {
            var current = await rigReady.Task.WaitAsync(token);
            var (client, host) = InMemoryTransport.CreatePair();
            _ = current.AdmitAsync(host);
            return client;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var player in connected) await player.DisposeAsync();
            if (rig != null) await rig.DisposeAsync();
        }

        public static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dst = File.Create(Path.Combine(to, Path.GetFileName(file)));
                src.CopyTo(dst);
            }
            foreach (var sub in Directory.GetDirectories(from)) CopyDirectory(sub, Path.Combine(to, Path.GetFileName(sub)));
        }
    }

    /// <summary>A real client session plus the bookkeeping a scripted player needs.</summary>
    private sealed class Seat : IAsyncDisposable
    {
        private readonly object gate = new();
        private readonly List<(Func<ClientSnapshot, bool> Pred, TaskCompletionSource Done)> waiters = [];
        private readonly Task pump;
        private TaskCompletionSource connectGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ITransport? current;

        public Seat(World world, string civId, ClientIdentity identity)
        {
            CivId = civId;
            connectGate.SetResult();
            Session = new ClientSession(async token =>
            {
                await Volatile.Read(ref connectGate).Task.WaitAsync(token);
                current = await world.OpenTransportAsync(token);
                return current;
            }, new ClientSessionOptions
            {
                Identity = identity,
                DisplayName = civId,
                RulesetHash = "",
                ExpectedFingerprint = Fingerprint,
                ReconnectDelay = (_, _) => Task.CompletedTask,
                MaxReconnectAttempts = 3,
                HandshakeTimeout = Timeout.InfiniteTimeSpan,
                KeepAliveInterval = Timeout.InfiniteTimeSpan,
                HostSilenceTimeout = Timeout.InfiniteTimeSpan,
            });
            pump = PumpAsync();
            Diagnose = () => world.OnActorAsync(rig => $"turn {rig.Host.Game.Turns} {rig.Host.Game.TurnState!.Phase} ended [{string.Join(',', rig.Host.Game.TurnState.Ended)}] seq {rig.Host.Session.LastSeq} sessions {rig.Host.Sessions.Count}; log {string.Join(' ', rig.Host.Session.Log.TakeLast(20).Select(it => $"{it.Seq}:{it.ActorId}:{it.Type}"))}").GetAwaiter().GetResult();
        }

        public string CivId { get; }
        public ClientSession Session { get; }
        public List<ClientEvent> Seen { get; } = [];
        public List<string> Forced { get; } = [];

        private async Task PumpAsync()
        {
            await foreach (var clientEvent in Session.Events.ReadAllAsync())
            {
                lock (gate)
                {
                    Seen.Add(clientEvent);
                    foreach (var waiter in waiters.Where(it => Check(it.Pred)).ToList())
                    {
                        waiters.Remove(waiter);
                        waiter.Done.TrySetResult();
                    }
                }
            }
        }

        public Func<string>? Diagnose { get; set; }

        private static string Brief(ClientEvent e) => e switch
        {
            DeltaApplied d => $"Delta{d.Seq}",
            SnapshotReplaced r => $"Snap{r.Snapshot.Seq}/t{r.Snapshot.Turn}",
            CommandCompleted c => $"Cmd({c.Outcome.Accepted},{c.Outcome.Code})",
            Desynchronized y => "Desync:" + y.Reason,
            _ => e.GetType().Name
        };

        private bool Check(Func<ClientSnapshot, bool> pred) => Session.Game.Snapshot is { } snapshot && pred(snapshot);

        /// <summary>Completes when the mirror satisfies <paramref name="pred"/> (checked now and after every client event).</summary>
        public async Task WaitAsync(Func<ClientSnapshot, bool> pred)
        {
            TaskCompletionSource done;
            lock (gate)
            {
                if (Check(pred)) return;
                done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Add((pred, done));
            }
            try { await done.Task.WaitAsync(Guard); }
            catch (TimeoutException)
            {
                var mirror = Session.Game.Snapshot;
                throw new TimeoutException($"{CivId}: state {Session.State}, mirror turn {mirror?.Turn} phase {mirror?.Phase} seq {mirror?.Seq}; last events: {string.Join(", ", Seen.TakeLast(30).Select(Brief))}; forced [{string.Join(";", Forced)}]; host: {Diagnose?.Invoke()}");
            }
        }

        public async Task DropAsync()
        {
            connectGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await current!.CloseAsync();
        }

        public void Reconnect() => connectGate.SetResult();

        /// <summary>Plays the planned commands, then ends the turn: one human's whole round.</summary>
        public async Task PlayTurnAsync(World world)
        {
            await PlayCommandsAsync(world);
            var end = await Session.SubmitAsync(new EndTurn()).WaitAsync(Guard);
            if (!end.Accepted) // a decision the planner cannot answer blocks the end: the host forces it, as ScriptedHuman does
            {
                Forced.Add(string.Join("/", end.Code, "ended=" + await world.OnActorAsync(rig => rig.Host.Game.HasEndedTurn(rig.Host.Game.GetCivilization(CivId))), await world.OnActorAsync(rig => string.Join(",", PendingDecisions.BlockingFor(rig.Host.Game.GetCivilization(CivId)).Select(it => $"{it.Kind}:{it.Subject}:{it.ResolvedBy}")))));
                await world.OnActorAsync(rig =>
                {
                    var accepted = rig.Host.ApplyServerCommand(new ForceEndTurn { CivId = CivId }).Accepted;
                    rig.Host.Tick(); // pumps the round when this was the last end
                    return accepted;
                });
            }
        }

        public async Task PlayCommandsAsync(World world)
        {
            var handled = new HashSet<int>();
            for (var guard = 0; guard < 64; guard++)
            {
                var command = await world.OnActorAsync(rig => Planner.Next(rig.Host.Game, CivId, handled));
                if (command == null) return;
                var outcome = await Session.SubmitAsync(command).WaitAsync(Guard);
                if (!outcome.Accepted)
                {
                    Forced.Add($"refused {command.Type} {outcome.Code}");
                    return; // never loop on a refusal
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            await pump.WaitAsync(Guard);
        }
    }

    /// <summary>The scripted human of T194 (ScriptedHuman) as a pure "next command" function: found a city, explore, answer the blocking decisions. Validation is a dry run.</summary>
    private static class Planner
    {
        public static GameCommand? Next(GameInfo game, string civId, ISet<int>? handled = null)
        {
            var civ = game.GetCivilization(civId);
            if (game.HasEndedTurn(civ) || !civ.IsAlive()) return null;
            foreach (var unit in civ.Units.GetCivUnits().ToList())
            {
                if (!unit.IsIdle() || handled?.Contains(unit.Id) == true) continue;
                var actions = UnitActionDescriptor.Describe(unit).Where(it => it.Action.Action != null).ToList();
                var chosen = actions.FirstOrDefault(it => it.Descriptor.ActionType == UnitActionType.FoundCity);
                if (chosen.Descriptor == null && !unit.IsCivilian()) chosen = actions.FirstOrDefault(it => it.Descriptor.ActionType == UnitActionType.Explore);
                if (chosen.Descriptor != null)
                {
                    var command = new ExecuteUnitAction { UnitId = unit.Id, Descriptor = chosen.Descriptor, Confirmed = true };
                    if (command.Validate(game, civId).IsValid)
                    {
                        handled?.Add(unit.Id); // one action per unit and round, like ScriptedHuman: an action that leaves the unit idle must not repeat forever
                        return command;
                    }
                }
            }

            foreach (var decision in PendingDecisions.BlockingFor(civ))
            {
                foreach (var candidate in Candidates(game, civ, decision))
                    if (candidate.Validate(game, civId).IsValid) return candidate;
            }
            return null;
        }

        public static IEnumerable<string> Why(GameInfo game, string civId, Decision decision) =>
            Candidates(game, game.GetCivilization(civId), decision).Take(3).Select(it => it.Validate(game, civId)).Select(it => $"{it.IsValid}{it.Code}");

        private static IEnumerable<GameCommand> Candidates(GameInfo game, Civilization civ, Decision decision)
        {
            var rules = game.Ruleset;
            switch (decision.Kind)
            {
                case DecisionKind.CityProduction:
                    var city = civ.Cities.First(it => it.Name == decision.Subject);
                    foreach (var name in new[] { "Warrior", "Scout", "Monument", "Worker", "Granary", "Library", "Barracks", "Settler" })
                        yield return new QueueConstruction { CityId = city.Id, Name = name };
                    break;
                case DecisionKind.Technology when civ.Tech.FreeTechs != 0:
                    foreach (var name in rules.Technologies.Keys) yield return new PickFreeTechnology { Tech = name };
                    break;
                case DecisionKind.Technology:
                    foreach (var name in rules.Technologies.Keys) yield return new SetResearchQueue { Techs = [name] };
                    break;
                case DecisionKind.Policy:
                    foreach (var name in rules.PolicyBranches.Keys.Concat(rules.Policies.Keys)) yield return new AdoptPolicy { Policy = name };
                    break;
                case DecisionKind.MoveAutomatedUnits:
                    yield return new MoveAutomatedUnits();
                    break;
            }
        }
    }
}
