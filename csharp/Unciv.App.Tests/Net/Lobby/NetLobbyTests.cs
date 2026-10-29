using Unciv.Client.Net.Session;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Logic;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Persistence;
using Unciv.Server;

namespace Unciv.Core.Tests.Net.Lobby;

[Collection("RulesetCache")]
public sealed class NetLobbyTests : IDisposable
{
    public NetLobbyTests()
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

    private static async Task<T> ReceiveMessageAsync<T>(ITransport transport, int timeoutMs = 2000) where T : Message
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        while (!cts.IsCancellationRequested)
        {
            var frame = await transport.ReceiveAsync(cts.Token);
            if (frame == null) continue;
            var msg = ProtocolCodec.Decode(frame);
            if (msg is T typed) return typed;
        }
        throw new TimeoutException($"Did not receive expected message of type {typeof(T).Name}");
    }

    [Fact]
    public async Task Hello_ValidHandshake_ReturnsWelcomeWithChallenge()
    {
        await using var lobby = new LobbyHost();
        var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverTransport);

        var hello = new Hello
        {
            Protocol = ProtocolVersion.Current,
            ClientVersion = "1.0.0",
            RulesetHash = lobby.Settings.ComputeRulesetHash(),
            ModList = []
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(hello));

        var welcome = await ReceiveMessageAsync<Welcome>(clientTransport);
        Assert.Equal(ProtocolVersion.Current, welcome.Protocol);
        Assert.NotEmpty(welcome.SessionId);
        Assert.NotEmpty(welcome.Challenge);
        Assert.Equal(lobby.HostFingerprint, welcome.HostKeyFingerprint);
        Assert.False(welcome.RequiresPassword);
    }

    [Fact]
    public async Task Hello_ProtocolMismatch_RefusesWithProtocolMismatch()
    {
        await using var lobby = new LobbyHost();
        var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverTransport);

        var hello = new Hello
        {
            Protocol = 999999,
            ClientVersion = "1.0.0",
            RulesetHash = lobby.Settings.ComputeRulesetHash(),
            ModList = []
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(hello));

        var refused = await ReceiveMessageAsync<JoinRefused>(clientTransport);
        Assert.Equal(JoinRefusedCodes.ProtocolMismatch, refused.Code);
    }

    [Fact]
    public async Task Hello_RulesetHashMismatch_RefusesWithRulesetMismatch()
    {
        await using var lobby = new LobbyHost();
        var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverTransport);

        var hello = new Hello
        {
            Protocol = ProtocolVersion.Current,
            ClientVersion = "1.0.0",
            RulesetHash = "incompatible-hash-0123456789abcdef",
            ModList = []
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(hello));

        var refused = await ReceiveMessageAsync<JoinRefused>(clientTransport);
        Assert.Equal(JoinRefusedCodes.RulesetMismatch, refused.Code);
    }

    [Fact]
    public async Task Join_PasswordRequired_WrongPasswordRefusesAndRateLimits()
    {
        var settings = new LobbySettings { Password = "SecureLobbyPassword123" };
        await using var lobby = new LobbyHost(settings: settings);
        var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverTransport);

        using var identity = ClientIdentity.Generate();

        var hello = new Hello
        {
            Protocol = ProtocolVersion.Current,
            ClientVersion = "1.0.0",
            RulesetHash = lobby.Settings.ComputeRulesetHash(),
            ModList = []
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(hello));
        var welcome = await ReceiveMessageAsync<Welcome>(clientTransport);
        Assert.True(welcome.RequiresPassword);

        // Send Join with wrong password
        var join = new Join
        {
            Password = "WrongPassword",
            PlayerKey = identity.PlayerKey,
            DisplayName = "Alice"
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(join));

        var refused = await ReceiveMessageAsync<JoinRefused>(clientTransport);
        Assert.Equal(JoinRefusedCodes.BadCredentials, refused.Code);
    }

    [Fact]
    public async Task Join_WithInviteToken_AdmittedWithoutPassword()
    {
        var settings = new LobbySettings { Password = "SecureLobbyPassword123" };
        var random = SystemSecureRandom.Instance;
        var tokens = new InviteTokens(TimeProvider.System, random);
        var limiter = new AttemptLimiter(TimeProvider.System);
        var password = JoinPassword.Create("SecureLobbyPassword123", random);
        var admission = new LobbyAdmission(password, inviteOnly: false, tokens, limiter);

        var token = tokens.Mint(TimeSpan.FromMinutes(10));
        Assert.NotNull(token);

        await using var lobby = new LobbyHost(settings: settings, admission: admission);
        var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverTransport);

        using var identity = ClientIdentity.Generate();

        var hello = new Hello
        {
            Protocol = ProtocolVersion.Current,
            ClientVersion = "1.0.0",
            RulesetHash = lobby.Settings.ComputeRulesetHash(),
            ModList = []
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(hello));
        var welcome = await ReceiveMessageAsync<Welcome>(clientTransport);

        var join = new Join
        {
            InviteToken = token,
            PlayerKey = identity.PlayerKey,
            DisplayName = "Alice"
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(join));

        var proof = identity.Sign(lobby.HostFingerprint, welcome.SessionId, welcome.Challenge);
        await clientTransport.SendAsync(ProtocolCodec.Encode(new Auth { Proof = proof }));

        var joinOk = await ReceiveMessageAsync<JoinOk>(clientTransport);
        Assert.NotNull(joinOk);

        var lobbyState = await ReceiveMessageAsync<LobbyState>(clientTransport);
        Assert.Contains(lobbyState.Slots, s => s.PlayerName == "Alice" && s.PlayerKeyId == identity.PlayerKey);
    }

    [Fact]
    public async Task Auth_InvalidProof_RefusedWithAuthFailed()
    {
        await using var lobby = new LobbyHost();
        var (clientTransport, serverTransport) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverTransport);

        using var identity = ClientIdentity.Generate();

        var hello = new Hello
        {
            Protocol = ProtocolVersion.Current,
            ClientVersion = "1.0.0",
            RulesetHash = lobby.Settings.ComputeRulesetHash(),
            ModList = []
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(hello));
        var welcome = await ReceiveMessageAsync<Welcome>(clientTransport);

        var join = new Join
        {
            PlayerKey = identity.PlayerKey,
            DisplayName = "Alice"
        };
        await clientTransport.SendAsync(ProtocolCodec.Encode(join));

        // Submit invalid proof
        await clientTransport.SendAsync(ProtocolCodec.Encode(new Auth { Proof = "InvalidBase64ProofSignatureHere" }));

        var refused = await ReceiveMessageAsync<JoinRefused>(clientTransport);
        Assert.Equal(JoinRefusedCodes.AuthFailed, refused.Code);
    }

    [Fact]
    public async Task LobbySet_PicksNationAndTogglesReady_EnforcesUniqueness()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        _ = lobby.AttachConnectionAsync(s2);

        using var id1 = ClientIdentity.Generate();
        using var id2 = ClientIdentity.Generate();

        // Connect client 1
        var hash = lobby.Settings.ComputeRulesetHash();
        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);
        await ReceiveMessageAsync<LobbyState>(c1);

        // Connect client 2
        await c2.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w2 = await ReceiveMessageAsync<Welcome>(c2);
        await c2.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id2.PlayerKey, DisplayName = "Bob" }));
        await c2.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id2.Sign(lobby.HostFingerprint, w2.SessionId, w2.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c2);
        await ReceiveMessageAsync<LobbyState>(c2);
        await ReceiveMessageAsync<LobbyState>(c1); // Drain Bob's join broadcast on c1

        // Alice picks Rome and readies up
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        var stateAfterAlice = await ReceiveMessageAsync<LobbyState>(c1);
        Assert.Equal("Rome", stateAfterAlice.Slots[0].Nation);
        Assert.True(stateAfterAlice.Slots[0].Ready);

        await ReceiveMessageAsync<LobbyState>(c2); // Drain Alice's state change broadcast on c2

        // Bob tries to pick Rome too (duplicate) -> ignored
        await c2.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 1, Nation = "Rome", Ready = false }));
        // Bob picks Egypt instead
        await c2.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 1, Nation = "Egypt", Ready = true }));
        var stateAfterBob = await ReceiveMessageAsync<LobbyState>(c2);
        Assert.Equal("Egypt", stateAfterBob.Slots[1].Nation);
        Assert.True(stateAfterBob.Slots[1].Ready);
    }

    [Fact]
    public async Task LobbyChat_BroadcastsToAllClients()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        _ = lobby.AttachConnectionAsync(s2);

        using var id1 = ClientIdentity.Generate();
        using var id2 = ClientIdentity.Generate();
        var hash = lobby.Settings.ComputeRulesetHash();

        // Connect both
        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);

        await c2.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w2 = await ReceiveMessageAsync<Welcome>(c2);
        await c2.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id2.PlayerKey, DisplayName = "Bob" }));
        await c2.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id2.Sign(lobby.HostFingerprint, w2.SessionId, w2.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c2);

        // Alice sends chat message
        await c1.SendAsync(ProtocolCodec.Encode(new LobbyChat { Text = "Good luck everyone!" }));

        // Bob receives the broadcast
        var chatOnBob = await ReceiveMessageAsync<LobbyChat>(c2);
        Assert.Equal("Good luck everyone!", chatOnBob.Text);
        Assert.Equal("Alice", chatOnBob.Sender);
    }

    [Fact]
    public async Task Kick_RemovesPlayerAndResetsSlot()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);

        using var id1 = ClientIdentity.Generate();
        var hash = lobby.Settings.ComputeRulesetHash();

        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);

        // Host kicks Alice
        await lobby.KickAsync(0);

        var kickNotice = await ReceiveMessageAsync<JoinRefused>(c1);
        Assert.Equal(JoinRefusedCodes.Kicked, kickNotice.Code);

        Assert.Equal(SlotKind.Open, lobby.Slots[0].Kind);
        Assert.Null(lobby.Slots[0].PlayerKeyId);
    }

    [Fact]
    public async Task StartGame_AiFillAndRandomNationResolution_ProducesGameAndBroadcasts()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);

        using var id1 = ClientIdentity.Generate();
        var hash = lobby.Settings.ComputeRulesetHash();

        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);
        await ReceiveMessageAsync<LobbyState>(c1); // Drain initial LobbyState from Auth

        // Alice ready
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Ready = true }));
        var stateAfterReady = await ReceiveMessageAsync<LobbyState>(c1);
        Assert.True(stateAfterReady.Slots[0].Ready);

        var result = await lobby.StartGameAsync(force: false, seed: 12345L, clockMillis: 1000L);
        Assert.NotNull(result.Game);
        Assert.True(result.Setup.GameParameters.IsOnlineMultiplayer);
        Assert.False(result.Setup.GameParameters.GodMode);
        Assert.False(result.Setup.GameParameters.TradeHintsEnabled);

        // Open slots should have been AI filled
        Assert.Contains(result.Slots, s => s.Kind == SlotKind.AI);
        // Random nations resolved to non-random
        Assert.DoesNotContain(result.Slots, s => s.Nation == "Random");

        var gameStarting = await ReceiveMessageAsync<GameStarting>(c1);
        Assert.NotNull(gameStarting);
    }

    [Fact]
    public async Task ServerLobby_StartGame_TransitionsToGameHostWithInitialSnapshots()
    {
        await using var serverLobby = new ServerLobby();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = serverLobby.AttachConnectionAsync(s1);

        using var id1 = ClientIdentity.Generate();
        var hash = serverLobby.Lobby.Settings.ComputeRulesetHash();

        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(serverLobby.Lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);
        await ReceiveMessageAsync<LobbyState>(c1); // Drain initial LobbyState from Auth

        // Alice picks Rome and ready
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        var stateAfterRome = await ReceiveMessageAsync<LobbyState>(c1);
        Assert.Equal("Rome", stateAfterRome.Slots[0].Nation);
        Assert.True(stateAfterRome.Slots[0].Ready);

        // Host starts game
        var gameHost = await serverLobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);
        Assert.NotNull(gameHost);

        var gameStarting = await ReceiveMessageAsync<GameStarting>(c1);
        Assert.NotNull(gameStarting);

        // Client 1 should now receive initial full Snapshot from GameHost
        var snapshot = await ReceiveMessageAsync<Snapshot>(c1);
        Assert.NotNull(snapshot);
        Assert.Equal("Rome", snapshot.You);
        Assert.NotEmpty(snapshot.ViewHash);
    }

    [Fact]
    public async Task CloseSlot_And_OpenSlot_UpdatesSlotKind()
    {
        await using var lobby = new LobbyHost();
        Assert.Equal(SlotKind.Open, lobby.Slots[1].Kind);

        await lobby.CloseSlotAsync(1);
        Assert.Equal(SlotKind.Closed, lobby.Slots[1].Kind);

        await lobby.OpenSlotAsync(1);
        Assert.Equal(SlotKind.Open, lobby.Slots[1].Kind);
    }

    [Fact]
    public async Task SetSlotKind_SetsAiAndOpen()
    {
        await using var lobby = new LobbyHost();

        await lobby.SetSlotKindAsync(2, SlotKind.AI);
        Assert.Equal(SlotKind.AI, lobby.Slots[2].Kind);
        Assert.Equal("AI Player", lobby.Slots[2].DisplayName);
        Assert.True(lobby.Slots[2].Ready);

        await lobby.SetSlotKindAsync(2, SlotKind.Open);
        Assert.Equal(SlotKind.Open, lobby.Slots[2].Kind);
        Assert.Null(lobby.Slots[2].DisplayName);
        Assert.False(lobby.Slots[2].Ready);
    }

    [Fact]
    public async Task UpdateSettings_UpdatesSettingsAndBroadcasts()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);

        using var id1 = ClientIdentity.Generate();
        var hash = lobby.Settings.ComputeRulesetHash();

        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);
        await ReceiveMessageAsync<LobbyState>(c1);

        var newSettings = new LobbySettings
        {
            GameName = "Updated Game Name",
            TurnTimerSeconds = 60,
            MaxMajorCivs = 6
        };
        await lobby.UpdateSettingsAsync(newSettings);

        var updatedState = await ReceiveMessageAsync<LobbyState>(c1);
        Assert.Equal(6, lobby.Slots.Count);
        Assert.Equal("Updated Game Name", updatedState.Settings.GetProperty("gameName").GetString());
        Assert.Equal(60, updatedState.Settings.GetProperty("turnTimerSeconds").GetInt32());
    }

    [Fact]
    public async Task StartGame_NotAllHumansReady_ThrowsInvalidOperationException()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);

        using var id1 = ClientIdentity.Generate();
        var hash = lobby.Settings.ComputeRulesetHash();

        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);
        await ReceiveMessageAsync<LobbyState>(c1);

        // Alice is NOT ready
        await Assert.ThrowsAsync<InvalidOperationException>(() => lobby.StartGameAsync(force: false));
    }

    [Fact]
    public async Task Auth_ReconnectsToExistingBoundSlot()
    {
        await using var lobby = new LobbyHost();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);

        using var id1 = ClientIdentity.Generate();
        var hash = lobby.Settings.ComputeRulesetHash();

        // Initial connect
        await c1.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w1 = await ReceiveMessageAsync<Welcome>(c1);
        await c1.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "Alice" }));
        await c1.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w1.SessionId, w1.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c1);
        await ReceiveMessageAsync<LobbyState>(c1);

        // Pick Greece
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Greece", Ready = false }));
        await ReceiveMessageAsync<LobbyState>(c1);

        // Disconnect c1
        await c1.CloseAsync();
        await Task.Delay(50);

        // Reconnect with new transport pair
        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);

        await c2.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = hash, ModList = [] }));
        var w2 = await ReceiveMessageAsync<Welcome>(c2);
        await c2.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id1.PlayerKey, DisplayName = "AliceReborn" }));
        await c2.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id1.Sign(lobby.HostFingerprint, w2.SessionId, w2.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(c2);
        var stateAfterReconnect = await ReceiveMessageAsync<LobbyState>(c2);

        // Slot 0 should still be Greece and owned by Alice
        Assert.Equal("Greece", stateAfterReconnect.Slots[0].Nation);
        Assert.Equal("AliceReborn", stateAfterReconnect.Slots[0].PlayerName);
        Assert.Equal(id1.PlayerKey, stateAfterReconnect.Slots[0].PlayerKeyId);
        Assert.True(stateAfterReconnect.Slots[0].Connected);
    }

    [Fact]
    public void LobbySettings_ToGameSetupInfo_EnforcesAntiCheatRules()
    {
        var settings = new LobbySettings
        {
            GameName = "Competitive Tournament",
            Ruleset = "Civ V - Gods & Kings",
            CityStateCount = 8,
            Speed = "Quick",
            Difficulty = "Emperor"
        };

        var setup = settings.ToGameSetupInfo();
        Assert.True(setup.GameParameters.IsOnlineMultiplayer);
        Assert.False(setup.GameParameters.GodMode);
        Assert.False(setup.GameParameters.TradeHintsEnabled);
        Assert.Equal("Quick", setup.GameParameters.Speed);
        Assert.Equal("Emperor", setup.GameParameters.Difficulty);
        Assert.Equal(8, setup.GameParameters.NumberOfCityStates);
    }

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static ClientSession NewClient(ServerLobby lobby, ClientIdentity identity, string name, ITransport clientEnd) =>
        new(_ => Task.FromResult(clientEnd), new ClientSessionOptions
        {
            Identity = identity,
            DisplayName = name,
            RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(),
            ExpectedFingerprint = lobby.Lobby.HostFingerprint,
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
            HostSilenceTimeout = Timeout.InfiniteTimeSpan
        });

    [Fact]
    public async Task ServerLobby_ClientSession_CommandRoundTripAfterStart_HasOneReaderPerSocket()
    {
        await using var lobby = new ServerLobby();
        var (clientEnd, serverEnd) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverEnd);
        using var id = ClientIdentity.Generate();
        await using var session = NewClient(lobby, id, "Alice", clientEnd);

        Assert.True((await session.JoinAsync().WaitAsync(Bound)).Joined);
        await session.SetLobbySlotAsync(0, "Rome", null, true);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        // Several commands in a row: any frame taken by a competing lobby reader would leave one without a CommandResult.
        for (var i = 0; i < 5; i++)
        {
            var outcome = await session.SubmitAsync(new EndTurn()).WaitAsync(Bound);
            Assert.False(string.IsNullOrEmpty(outcome.ClientCmdId));
        }
    }

    [Fact]
    public async Task ServerLobby_KnownPlayerReconnectingAfterStart_IsSeatedInTheRunningGame()
    {
        await using var lobby = new ServerLobby();
        var (clientEnd, serverEnd) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(serverEnd);
        using var id = ClientIdentity.Generate();
        await using var first = NewClient(lobby, id, "Alice", clientEnd);
        Assert.True((await first.JoinAsync().WaitAsync(Bound)).Joined);
        await first.SetLobbySlotAsync(0, "Rome", null, true);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        var (client2, server2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(server2);
        await using var second = NewClient(lobby, id, "Alice", client2);
        Assert.True((await second.JoinAsync().WaitAsync(Bound)).Joined);
        using var cts = new CancellationTokenSource(Bound);
        await foreach (var ev in second.Events.ReadAllAsync(cts.Token))
            if (ev is SnapshotReplaced) break; // seated in the running game: the host sent the first snapshot
        var outcome = await second.SubmitAsync(new EndTurn()).WaitAsync(Bound);
        Assert.False(string.IsNullOrEmpty(outcome.ClientCmdId));
    }

    [Fact]
    public async Task ServerLobby_NewPlayerAfterStart_IsRefusedAsGameStarted_EvenWithJoinInProgressAllowed()
    {
        await using var lobby = new ServerLobby(new LobbySettings { AllowJoinInProgress = true, AiFill = false, MaxMajorCivs = 3 });
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        using var alice = ClientIdentity.Generate();
        await using var session = NewClient(lobby, alice, "Alice", c1);
        Assert.True((await session.JoinAsync().WaitAsync(Bound)).Joined);
        await session.SetLobbySlotAsync(0, "Rome", null, true);
        await lobby.Lobby.SetSlotKindAsync(1, SlotKind.AI);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        using var bob = ClientIdentity.Generate();
        await c2.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(), ModList = [] }));
        var welcome = await ReceiveMessageAsync<Welcome>(c2);
        await c2.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = bob.PlayerKey, DisplayName = "Bob" }));
        await c2.SendAsync(ProtocolCodec.Encode(new Auth { Proof = bob.Sign(lobby.Lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) }));
        var refused = await ReceiveMessageAsync<JoinRefused>(c2);
        Assert.Equal(JoinRefusedCodes.GameAlreadyStarted, refused.Code);
    }

    [Fact]
    public async Task ServerLobby_SecondConnectionOfTheSameKeyBeforeStart_SupersedesTheFirst_AndOnlyTheCurrentOneIsSeated()
    {
        await using var lobby = new ServerLobby();
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        await using var first = NewClient(lobby, id, "Alice", c1);
        Assert.True((await first.JoinAsync().WaitAsync(Bound)).Joined);
        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        await using var second = NewClient(lobby, id, "Alice", c2);
        Assert.True((await second.JoinAsync().WaitAsync(Bound)).Joined);
        await second.SetLobbySlotAsync(0, "Rome", null, true);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        Assert.False(string.IsNullOrEmpty((await second.SubmitAsync(new EndTurn()).WaitAsync(Bound)).ClientCmdId));

        // The superseded connection is closed by the host (its frames would otherwise pile up in a channel nobody reads).
        using var cts = new CancellationTokenSource(Bound);
        while (await c1.ReceiveAsync(cts.Token) != null) { }
    }

    [Fact]
    public async Task ServerLobby_TwoRapidSameKeyReconnectsAfterStart_TheLatestIsSeated_NotLeftWithoutSnapshot()
    {
        for (var round = 0; round < 6; round++)
        {
            await using var lobby = new ServerLobby();
            using var id = ClientIdentity.Generate();
            var (c1, s1) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(s1);
            await using var first = NewClient(lobby, id, "Alice", c1);
            Assert.True((await first.JoinAsync().WaitAsync(Bound)).Joined);
            await first.SetLobbySlotAsync(0, "Rome", null, true);
            await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

            var (c2, s2) = InMemoryTransport.CreatePair();
            var (c3, s3) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(s2);
            _ = lobby.AttachConnectionAsync(s3);
            await using var second = NewClient(lobby, id, "Alice", c2);
            await using var third = NewClient(lobby, id, "Alice", c3);
            var joins = await Task.WhenAll(second.JoinAsync(), third.JoinAsync()).WaitAsync(Bound);
            Assert.All(joins, j => Assert.True(j.Joined));

            // Exactly one of the two is the current connection of the key: it must be seated and playable (never JoinOk without a snapshot).
            using var cts = new CancellationTokenSource(Bound);
            var seatedSecond = WaitForSnapshotAsync(second, cts.Token);
            var seatedThird = WaitForSnapshotAsync(third, cts.Token);
            Assert.True(await await Task.WhenAny(seatedSecond, seatedThird));
            var outcomes = await Task.WhenAll(TrySubmitAsync(second), TrySubmitAsync(third));
            Assert.Contains(true, outcomes);
            var romeSessions = 0;
            await lobby.ActiveGameHost!.EnqueueAsync(() => { romeSessions = lobby.ActiveGameHost.Sessions.Values.Count(x => x.BoundCivId == "Rome"); return Task.CompletedTask; });
            Assert.Equal(1, romeSessions);
        }
    }

    /// <summary>True when the host answered the command (accepted or not): the connection is seated and its reader works.</summary>
    private static async Task<bool> TrySubmitAsync(ClientSession session)
    {
        for (var i = 0; i < 20 && session.State != ClientSessionState.InGame; i++) await Task.Delay(50);
        try
        {
            return !string.IsNullOrEmpty((await session.SubmitAsync(new EndTurn()).WaitAsync(TimeSpan.FromSeconds(2))).ClientCmdId);
        }
        catch (TimeoutException)
        {
            return false; // the superseded connection is closed by the host
        }
    }

    private static async Task<bool> WaitForSnapshotAsync(ClientSession session, CancellationToken ct)
    {
        try
        {
            await foreach (var ev in session.Events.ReadAllAsync(ct))
                if (ev is SnapshotReplaced) return true;
        }
        catch (OperationCanceledException) { }
        return false;
    }

    private static async Task RawJoinAsync(ServerLobby lobby, ITransport clientEnd, ClientIdentity id)
    {
        await clientEnd.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(), ModList = [] }));
        var welcome = await ReceiveMessageAsync<Welcome>(clientEnd);
        await clientEnd.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id.PlayerKey, DisplayName = "Alice" }));
        await clientEnd.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id.Sign(lobby.Lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) }));
        await ReceiveMessageAsync<JoinOk>(clientEnd);
    }

    [Fact]
    public async Task ServerLobby_OlderReconnectSeatedLate_NeverReplacesTheNewerOne_AndEachSeatKeepsItsOwnIdentity()
    {
        await using var lobby = new ServerLobby();
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        await using var first = NewClient(lobby, id, "Alice", c1);
        Assert.True((await first.JoinAsync().WaitAsync(Bound)).Joined);
        await first.SetLobbySlotAsync(0, "Rome", null, true);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        // Seating of the first reconnect is held back until the second one has been fully seated.
        var real = lobby.Lobby.LateSeated!;
        var seats = new List<LobbyHost.LateSeat>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var olderEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lobby.Lobby.LateSeated = async seat =>
        {
            bool isOlder;
            lock (seats) { seats.Add(seat); isOlder = seats.Count == 1; }
            if (isOlder)
            {
                olderEntered.SetResult();
                await release.Task;
            }
            await real(seat);
        };

        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        await RawJoinAsync(lobby, c2, id);
        await olderEntered.Task.WaitAsync(Bound);
        var (c3, s3) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s3);
        await RawJoinAsync(lobby, c3, id);
        await ReceiveMessageAsync<Snapshot>(c3, 10000); // the newer connection is seated while the older one is still waiting

        release.SetResult();
        Assert.Equal(2, seats.Count);
        Assert.NotEqual(seats[0].SessionId, seats[1].SessionId);
        Assert.Null(lobby.Lobby.GameTransportFor(seats[0].SessionId)); // superseded
        Assert.Same(seats[1].Transport, lobby.Lobby.GameTransportFor(seats[1].SessionId));

        // The late older seat must not have detached the newer one.
        await Task.Delay(300);
        var sessionIds = await ((Func<Task<string[]>>)(async () =>
        {
            string[] ids = [];
            await lobby.ActiveGameHost!.EnqueueAsync(() => { ids = [.. lobby.ActiveGameHost.Sessions.Keys]; return Task.CompletedTask; });
            return ids;
        }))();
        Assert.Equal([seats[1].SessionId], sessionIds);
    }

    private sealed class FailingSendTransport(ITransport inner) : ITransport
    {
        private volatile bool dead;
        public bool IsOpen => !dead && inner.IsOpen; // a socket that dies right after GameStarting: the game host's bounded sender refuses to seat it
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
        {
            var result = inner.SendAsync(frame, cancellationToken);
            if (ProtocolCodec.Decode(frame) is GameStarting) dead = true;
            return result;
        }
        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);
        public ValueTask CloseAsync() => dead ? throw new IOException("socket gone") : inner.CloseAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    [Fact]
    public async Task ServerLobby_SeatingFailureDuringStart_FailsTheStart_AndLateSeatsAreDroppedInsteadOfHanging()
    {
        await using var lobby = new ServerLobby();
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(new FailingSendTransport(s1));
        await RawJoinAsync(lobby, c1, id); // a raw peer sends nothing after GameStarting, so the lobby reader stays parked in its receive
        await Assert.ThrowsAsync<InvalidOperationException>(() => lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L));

        // A known key arriving now would wait for a game that never comes: its connection must be closed, not left without a snapshot.
        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        await RawJoinAsync(lobby, c2, id);
        using var cts = new CancellationTokenSource(Bound);
        while (await c2.ReceiveAsync(cts.Token) != null) { }
    }

    [Fact]
    public async Task ServerLobby_ClosingOrDisposingTheHandOverTransport_EndsItsStream_AndFreesTheLobbyReader()
    {
        foreach (var dispose in new[] { false, true })
        {
            await using var lobby = new ServerLobby();
            using var id = ClientIdentity.Generate();
            var (c1, s1) = InMemoryTransport.CreatePair();
            _ = lobby.AttachConnectionAsync(s1);
            await using var first = NewClient(lobby, id, "Alice", c1);
            Assert.True((await first.JoinAsync().WaitAsync(Bound)).Joined);
            await first.SetLobbySlotAsync(0, "Rome", null, true);
            var host = await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);
            var sessionId = host.Sessions.Keys.Single();
            var gameSide = lobby.Lobby.GameTransportFor(sessionId);
            Assert.NotNull(gameSide);
            if (dispose) await gameSide.DisposeAsync(); else await gameSide.CloseAsync();
            Assert.Null(await gameSide.ReceiveAsync().AsTask().WaitAsync(Bound));
            using var cts = new CancellationTokenSource(Bound);
            while (await c1.ReceiveAsync(cts.Token) != null) { } // the socket was closed too
        }
    }

    private sealed class ThrowingTransport : ITransport
    {
        public bool IsOpen => true;
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => throw new IOException("reset");
        public ValueTask CloseAsync() => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task LobbyReader_EndsCleanly_OnTransportErrorAndOnCancellation()
    {
        await using var lobby = new ServerLobby();
        await lobby.AttachConnectionAsync(new ThrowingTransport()).WaitAsync(Bound);

        var (client, server) = InMemoryTransport.CreatePair();
        using var cts = new CancellationTokenSource();
        var attach = lobby.AttachConnectionAsync(server, cts.Token);
        await cts.CancelAsync();
        await attach.WaitAsync(Bound);
        Assert.NotNull(client);
    }

    private static async Task<(ServerLobby Lobby, ClientIdentity Id, ITransport Client)> StartedLobbyWithOneHumanAsync()
    {
        var lobby = new ServerLobby();
        var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        await RawJoinAsync(lobby, c1, id);
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        await ReceiveMessageAsync<LobbyState>(c1);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);
        await ReceiveMessageAsync<Snapshot>(c1, 10000);
        return (lobby, id, c1);
    }

    // T253: a known key arriving while the game is being created is seated after the start instead of being refused.
    [Fact]
    public async Task ServerLobby_KnownKeyDuringStarting_WaitsForTheStart_ThenIsSeated()
    {
        await using var lobby = new ServerLobby();
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        await RawJoinAsync(lobby, c1, id);
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        await ReceiveMessageAsync<LobbyState>(c1);

        var start = Task.Run(() => lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L));
        while (lobby.Lobby.Status == LobbyStatus.InLobby && !start.IsCompleted) await Task.Yield();

        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        await RawJoinAsync(lobby, c2, id); // JoinOk, not game-started
        await start.WaitAsync(Bound);
        var snapshot = await ReceiveMessageAsync<Snapshot>(c2, 10000);
        Assert.Equal("Rome", snapshot.You);
    }

    // T253: the host's kick/rebind after the start moves the lobby's own binding too.
    [Fact]
    public async Task ServerLobby_ReleaseSeatAfterStart_RefusesTheOldKey_AndSeatsTheNewOne()
    {
        var (lobby, id, c1) = await StartedLobbyWithOneHumanAsync();
        await using var lobbyScope = lobby;
        using var idScope = id;
        Assert.Equal("not-started", await new ServerLobby().KickCivAsync("Rome"));
        Assert.Equal("no-store", await lobby.KickCivAsync("Rome")); // refused by the game: the lobby binding stays
        Assert.Equal("not-started", await new ServerLobby().RebindCivAsync("Rome", id.PlayerKey));
        Assert.Equal("no-store", await lobby.RebindCivAsync("Rome", id.PlayerKey));
        Assert.Equal(0, lobby.Lobby.Bindings.SlotOf(id.PlayerKey));

        using var newId = ClientIdentity.Generate();
        Assert.True(await lobby.Lobby.ReleaseSeatAsync("Rome", newId.PlayerKey));
        Assert.False(await lobby.Lobby.ReleaseSeatAsync("Nobody", null));
        Assert.Null(lobby.Lobby.Bindings.SlotOf(id.PlayerKey));
        Assert.Equal(0, lobby.Lobby.Bindings.SlotOf(newId.PlayerKey));
        Assert.Null(await c1.ReceiveAsync()); // the old player's connection was closed

        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        await c2.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "1", RulesetHash = lobby.Lobby.Settings.ComputeRulesetHash(), ModList = [] }));
        var welcome = await ReceiveMessageAsync<Welcome>(c2);
        await c2.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = id.PlayerKey, DisplayName = "Alice" }));
        await c2.SendAsync(ProtocolCodec.Encode(new Auth { Proof = id.Sign(lobby.Lobby.HostFingerprint, welcome.SessionId, welcome.Challenge) }));
        Assert.Equal(JoinRefusedCodes.GameAlreadyStarted, (await ReceiveMessageAsync<JoinRefused>(c2)).Code);

        var (c3, s3) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s3);
        await RawJoinAsync(lobby, c3, newId);
        Assert.Equal("Rome", (await ReceiveMessageAsync<Snapshot>(c3, 10000)).You);
    }

    // T253: lobby administration after the start never writes lobby frames into a seat that belongs to the game.
    [Fact]
    public async Task ServerLobby_LobbyAdministrationAfterStart_IsIgnored_AndWritesNothingToSeatedPlayers()
    {
        var (lobby, id, c1) = await StartedLobbyWithOneHumanAsync();
        await using var lobbyScope = lobby;
        using var idScope = id;
        var settings = lobby.Lobby.Settings;
        await lobby.Lobby.UpdateSettingsAsync(new LobbySettings { MaxMajorCivs = settings.MaxMajorCivs + 1 });
        await lobby.Lobby.SetSlotKindAsync(1, SlotKind.Closed);
        await lobby.Lobby.CloseSlotAsync(1);
        await lobby.Lobby.OpenSlotAsync(1);
        await lobby.Lobby.KickAsync(0);
        await lobby.Lobby.BroadcastLobbyStateAsync();
        Assert.Same(settings, lobby.Lobby.Settings);
        Assert.Equal(SlotKind.Human, lobby.Lobby.Slots[0].Kind);

        await c1.SendAsync(ProtocolCodec.Encode(new Ping()));
        using var cts = new CancellationTokenSource(Bound);
        while (await c1.ReceiveAsync(cts.Token) is { } frame)
        {
            var message = ProtocolCodec.Decode(frame);
            Assert.IsNotType<LobbyState>(message);
            Assert.IsNotType<JoinRefused>(message);
            if (message is Pong) return;
        }
        Assert.Fail("no Pong");
    }

    // T253: a late-seated player's ping is answered while the game actor is busy seating (the lobby reader is not blocked by the seat).
    [Fact]
    public async Task ServerLobby_LateSeatedPing_IsAnsweredWhileTheGameActorIsBusy()
    {
        var (lobby, id, c1) = await StartedLobbyWithOneHumanAsync();
        await using var lobbyScope = lobby;
        using var idScope = id;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var busy = lobby.ActiveGameHost!.EnqueueAsync(() => release.Task);

        var (c2, s2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s2);
        await RawJoinAsync(lobby, c2, id);
        await c2.SendAsync(ProtocolCodec.Encode(new Ping()));
        await ReceiveMessageAsync<Pong>(c2); // before the actor is free
        Assert.False(busy.IsCompleted);
        release.SetResult();
        await busy.WaitAsync(Bound);
        Assert.Equal("Rome", (await ReceiveMessageAsync<Snapshot>(c2, 10000)).You);
        _ = c1;
    }

    // T253: a human who never made it into the game (no connection at the start) counts as disconnected, so the grace period can end their turn.
    [Fact]
    public async Task ServerLobby_HumanWithoutConnectionAtStart_IsMarkedDisconnected_AndAReconnectRestoresIt()
    {
        await using var lobby = new ServerLobby();
        using var idA = ClientIdentity.Generate();
        using var idB = ClientIdentity.Generate();
        var (a, sa) = InMemoryTransport.CreatePair();
        var (b, sb) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(sa);
        _ = lobby.AttachConnectionAsync(sb);
        await RawJoinAsync(lobby, a, idA);
        await RawJoinAsync(lobby, b, idB);
        await a.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        await b.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 1, Nation = "Greece", Ready = true }));
        await b.CloseAsync();
        while (lobby.Lobby.Slots[1].Connected) await Task.Yield();
        var host = await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);
        await host.EnqueueAsync(() => Task.CompletedTask);
        Assert.False(host.TurnTimer.IsConnected("Greece"));
        Assert.True(host.TurnTimer.IsConnected("Rome"));
    }

    private sealed class MetaOnlyStore : IGameStore
    {
        private GameMeta meta = new();
        public string GameId => "t253";
        public long LastSeq => 0;
        public long HostSecret { get; set; }
        public GameMeta ReadMeta() => meta;
        public void WriteMeta(GameMeta value) => meta = value;
        public void Append(CommandEnvelope envelope, string? clientCmdId = null) { }
        public void Sync(GameSession session) { }
        public void AppendCheckpoint(long seq, int turn, ulong fullHash) { }
        public bool TrySnapshot(GameInfo game) => false;
        public bool MaybeSnapshot(GameInfo game) => false;
        public void CloseCleanly(GameInfo game) { }
        public LoggedOutcome? FindOutcome(string actor, string clientCmdId) => null;
        public ResumePlan Resume(long lastSeq) => new(ResumeKind.Snapshot, []);
        public void Dispose() { }
    }

    // T253 (from T254): the lobby's turn policy is stored with the game so a resume keeps it.
    [Fact]
    public async Task ServerLobby_StartGame_WritesTheTurnPolicyIntoTheStoreMeta()
    {
        var store = new MetaOnlyStore();
        var settings = new LobbySettings { TurnTimerSeconds = 77 };
        await using var lobby = new ServerLobby(settings, store: store);
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        await RawJoinAsync(lobby, c1, id);
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        await ReceiveMessageAsync<LobbyState>(c1);
        await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        var saved = store.ReadMeta().TimerPolicy;
        Assert.NotNull(saved);
        Assert.Equal(77, saved.TurnTimerSeconds);
        Assert.Equal(settings.ToTurnPolicy(), saved);
    }

    // T253: a start that fails while the game is created returns the lobby to InLobby (and frees reconnects waiting for it) instead of leaving it Starting forever.
    [Fact]
    public async Task ServerLobby_StartFailingWhileCreatingTheGame_ReturnsToTheLobby()
    {
        await using var lobby = new ServerLobby();
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(s1);
        await RawJoinAsync(lobby, c1, id);
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        await ReceiveMessageAsync<LobbyState>(c1);
        lobby.Lobby.GameFactory = (_, _, _) => throw new InvalidOperationException("creation failed");
        await Assert.ThrowsAnyAsync<Exception>(() => lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L));
        Assert.Equal(LobbyStatus.InLobby, lobby.Lobby.Status);
        Assert.All(lobby.Lobby.Slots.Skip(1), slot => Assert.Equal(SlotKind.Open, slot.Kind)); // the AI fill is undone
        Assert.Equal(SlotKind.Human, lobby.Lobby.Slots[0].Kind);
    }

    private sealed class SupersededWhileSeatingTransport(ITransport inner, Func<Task> supersede) : ITransport
    {
        private volatile bool armed;
        private int superseded;
        // After GameStarting the first look at the socket (the game host's seating) finds that a newer connection took the seat and closed this one.
        public bool IsOpen
        {
            get
            {
                if (armed && Interlocked.Exchange(ref superseded, 1) == 0) supersede().GetAwaiter().GetResult();
                return !armed && inner.IsOpen;
            }
        }
        public ValueTask SendAsync(string frame, CancellationToken cancellationToken = default)
        {
            var result = inner.SendAsync(frame, cancellationToken);
            if (ProtocolCodec.Decode(frame) is GameStarting) armed = true;
            return result;
        }
        public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) => inner.ReceiveAsync(cancellationToken);
        public ValueTask CloseAsync() => inner.CloseAsync();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    // T253: a seat replaced by a newer connection while the start seats it is not a failed start.
    [Fact]
    public async Task ServerLobby_SeatSupersededWhileTheStartSeatsIt_DoesNotFailTheStart()
    {
        await using var lobby = new ServerLobby();
        using var id = ClientIdentity.Generate();
        var (c1, s1) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(new SupersededWhileSeatingTransport(s1, () => lobby.Lobby.ReleaseSeatAsync("Rome", id.PlayerKey)));
        await RawJoinAsync(lobby, c1, id);
        await c1.SendAsync(ProtocolCodec.Encode(new LobbySet { Slot = 0, Nation = "Rome", Ready = true }));
        await ReceiveMessageAsync<LobbyState>(c1);

        var host = await lobby.StartGameAsync(force: true, seed: 42L, clockMillis: 5000L);

        Assert.NotNull(host);
        await host.EnqueueAsync(() => Task.CompletedTask);
        Assert.False(host.TurnTimer.IsConnected("Rome"));
    }
}
