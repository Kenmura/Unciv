using Unciv.Client.Net.Session;
using Unciv.Commands;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Server;

namespace Unciv.Core.Tests.Net.Robustness;

/// <summary>
/// T249: frame loss injection (a deterministic lossy link between a real <see cref="ClientSession"/> and a real <see cref="ServerLobby"/>/<see cref="GameHost"/>)
/// and lobby traffic answered while the game actor is busy with an AI turn. No ports, no sleeps: every wait is on a message or a bounded task.
/// </summary>
[Collection("RulesetCache")]
public sealed class NetFrameLossTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
    private static readonly string Fingerprint = HostKey.Fingerprint(new byte[] { 9, 8, 7, 6 });

    public NetFrameLossTests()
    {
        var jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
    }

    public void Dispose() => RulesetCache.Clear();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The client's end of a link that silently loses chosen <see cref="ViewDelta"/> frames (by arrival ordinal, 1-based). Everything else passes, in order.</summary>
    private sealed class LossyLink(ITransport inner, params int[] dropDeltaOrdinals) : ITransport
    {
        private int deltasSeen;
        public int Dropped { get; private set; }
        public bool IsOpen => inner.IsOpen;
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default) => inner.SendAsync(frame, cancellationToken);

        public async ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var frame = await inner.ReceiveAsync(cancellationToken);
                if (frame == null || ProtocolCodec.Decode(frame) is not ViewDelta) return frame;
                if (!dropDeltaOrdinals.Contains(++deltasSeen)) return frame;
                Dropped++;
            }
        }

        public ValueTask CloseAsync() => inner.CloseAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static async Task<ClientEvent> NextAsync(ClientSession session, Func<ClientEvent, bool> match)
    {
        using var cts = new CancellationTokenSource(Bound);
        while (await session.Events.WaitToReadAsync(cts.Token))
            while (session.Events.TryRead(out var next))
                if (match(next)) return next;
        throw new InvalidOperationException("event stream ended");
    }

    private static async Task<GameHost> StartHostedGameAsync(ServerLobby lobby, ClientSession session)
    {
        Assert.True((await session.JoinAsync().WaitAsync(Bound)).Joined);
        await session.SetLobbySlotAsync(0, "Rome", null, true);
        var host = await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);
        await NextAsync(session, it => it is SnapshotReplaced);
        return host;
    }

    private GameInfo NewGame()
    {
        var game = GameStarter.StartNewGame(
            new GameSetupInfo(
                new GameParameters
                {
                    BaseRuleset = RulesetCache.GetVanillaRuleset().Name,
                    NumberOfCityStates = 0,
                    VictoryTypes = RulesetCache.GetVanillaRuleset().Victories.Keys.ToList(),
                    Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.AI)],
                },
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            1L, 1000L);
        game.StartSimultaneousTurns();
        return game;
    }

    /// <summary>Plays the host's half of the join handshake on <paramref name="hostEnd"/>, then seats the session on the real <paramref name="host"/> (which sends the first snapshot).</summary>
    private static async Task ServeAsync(GameHost host, ITransport hostEnd)
    {
        async Task<T> Next<T>() where T : Message => Assert.IsType<T>(ProtocolCodec.Decode((await hostEnd.ReceiveAsync().AsTask().WaitAsync(Bound))!));
        await Next<Hello>();
        await hostEnd.SendAsync(ProtocolCodec.Encode(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s1", Challenge = "c1", HostKeyFingerprint = Fingerprint, RequiresPassword = false }));
        await Next<Join>();
        await Next<Auth>();
        await hostEnd.SendAsync(ProtocolCodec.Encode(new JoinOk()));
        await hostEnd.SendAsync(ProtocolCodec.Encode(new GameStarting()));
        await host.AttachSessionAsync("s1", "Babylon", hostEnd);
    }

    [Fact]
    public async Task LostDeltas_AreDetectedAsAGap_AndHealedByOneResyncSnapshot()
    {
        var game = NewGame();
        await using var host = new GameHost(new GameSession(game));
        var (clientEnd, serverEnd) = InMemoryTransport.CreatePair();
        var link = new LossyLink(clientEnd, 2, 3); // the 2nd and 3rd deltas never arrive
        using var identity = ClientIdentity.Generate();
        await using var session = new ClientSession(_ => Task.FromResult<ITransport>(link), new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = "Alice",
            RulesetHash = "hash",
            ExpectedFingerprint = Fingerprint,
            ReconnectDelay = (_, _) => Task.CompletedTask,
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            HostSilenceTimeout = Timeout.InfiniteTimeSpan
        });
        var serving = ServeAsync(host, serverEnd);
        Assert.True((await session.JoinAsync().WaitAsync(Bound)).Joined);
        await serving.WaitAsync(Bound);
        await NextAsync(session, it => it is SnapshotReplaced);

        var tech = game.GetCivilization("Babylon").Tech;
        var techs = RulesetCache.GetVanillaRuleset().Technologies.Values
            .Where(t => !tech.IsResearched(t.Name) && !tech.IsUnresearchable(t) && t.Prerequisites.All(tech.IsResearched))
            .Select(t => t.Name).Take(4).ToList();
        Assert.Equal(4, techs.Count);
        async Task SubmitAsync(string name)
        {
            var outcome = await session.SubmitAsync(new Unciv.Commands.Empire.SetResearchQueue { Techs = [name] }).WaitAsync(Bound);
            Assert.True(outcome.Accepted, outcome.Code);
        }

        var start = session.Game.Seq;
        await SubmitAsync(techs[0]);
        await NextAsync(session, it => it is DeltaApplied);
        Assert.Equal(start + 1, session.Game.Seq);
        await SubmitAsync(techs[1]); // delta lost
        await SubmitAsync(techs[2]); // delta lost
        Assert.Equal(start + 1, session.Game.Seq);

        await SubmitAsync(techs[3]); // this delta arrives and shows the hole
        Assert.Equal("sequence-gap", ((Desynchronized)await NextAsync(session, it => it is Desynchronized)).Reason);
        var healed = (SnapshotReplaced)await NextAsync(session, it => it is SnapshotReplaced);
        Assert.Equal(2, link.Dropped);

        var hostSeq = await HostSeqAsync(host);
        Assert.Equal(start + 4, hostSeq);
        Assert.Equal(hostSeq, session.Game.Seq);
        Assert.Equal(hostSeq, healed.Snapshot.Seq);

        // The mirror is live again: the next delta applies without another resync.
        await SubmitAsync(techs[0]);
        var next = (DeltaApplied)await NextAsync(session, it => it is DeltaApplied);
        Assert.Equal(hostSeq + 1, next.Seq);
    }

    private static async Task<long> HostSeqAsync(GameHost host)
    {
        long seq = -1;
        await host.EnqueueAsync(() => { seq = host.Session.LastSeq; return Task.CompletedTask; }).WaitAsync(Bound);
        return seq;
    }

    [Fact]
    public async Task LobbyJoinAndPing_AreAnsweredWhileAnAiTurnOccupiesTheGameActor()
    {
        await using var lobby = new ServerLobby(hostFingerprint: Fingerprint);
        var (clientEnd, serverEnd) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverEnd);
        using var first = ClientIdentity.Generate();
        await using var session = new ClientSession(_ => Task.FromResult<ITransport>(clientEnd), new ClientSessionOptions
        {
            Identity = first,
            DisplayName = "Alice",
            RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(),
            ExpectedFingerprint = Fingerprint,
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            HostSilenceTimeout = Timeout.InfiniteTimeSpan
        });
        var host = await StartHostedGameAsync(lobby, session);

        var release = new TaskCompletionSource();
        var aiTurn = host.EnqueueAsync(() => release.Task); // the single-threaded actor is now busy
        Assert.False(aiTurn.IsCompleted);
        try
        {
            // A new player and a known player (the first one, on a second connection) both knock at the lobby while the actor is busy.
            async Task<Message> KnockAsync(ClientIdentity who, string name)
            {
                var (client, server) = InMemoryTransport.CreatePair();
                _ = lobby.AttachConnectionAsync(server);
                async Task<Message> Next() => ProtocolCodec.Decode((await client.ReceiveAsync().AsTask().WaitAsync(Bound))!);
                await client.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(), ModList = [] }));
                var welcome = Assert.IsType<Welcome>(await Next());
                await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = who.PlayerKey, DisplayName = name }));
                await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = who.Sign(lobby.Lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) }));
                var answer = await Next(); // JoinOk or JoinRefused, whichever the lobby decides: it must decide
                await client.SendAsync(ProtocolCodec.Encode(new Ping()));
                while (await Next() is not Pong) { } // LobbyState broadcasts may precede the Pong
                return answer;
            }

            using var stranger = ClientIdentity.Generate();
            var refused = Assert.IsType<JoinRefused>(await KnockAsync(stranger, "Bob")); // the started lobby has no free slot for a new player
            Assert.False(string.IsNullOrEmpty(refused.Code));
            Assert.IsType<JoinOk>(await KnockAsync(first, "Alice")); // a known key is seated again

            // The actor was occupied throughout (the in-game ping during a busy actor is covered by NetRobustnessTests).
            Assert.False(aiTurn.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }

        await aiTurn.WaitAsync(Bound);
    }
}
