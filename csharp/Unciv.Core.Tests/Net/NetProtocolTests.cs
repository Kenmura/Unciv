using System.Text.Json;
using Unciv.Net;

namespace Unciv.Core.Tests.Net;

public class NetProtocolTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static readonly TurnStatus Status = new() { Turn = 3, Ended = ["Rome"], Deadline = 1700000000000, Phase = "playing" };

    public static TheoryData<Message, string> Goldens => new()
    {
        { new Hello { Protocol = 1, ClientVersion = "0.1", RulesetHash = "abc", ModList = ["m1"] }, """{"type":"Hello","protocol":1,"clientVersion":"0.1","rulesetHash":"abc","modList":["m1"]}""" },
        { new Join { PlayerKey = "k", DisplayName = "Ann" }, """{"type":"Join","playerKey":"k","displayName":"Ann"}""" },
        { new Join { Password = "pw", PlayerKey = "k", DisplayName = "Ann", InviteToken = "t" }, """{"type":"Join","password":"pw","playerKey":"k","displayName":"Ann","inviteToken":"t"}""" },
        { new Auth { Proof = "sig" }, """{"type":"Auth","proof":"sig"}""" },
        { new Resume { GameId = "g", LastSeq = 9, LastViewHash = "h" }, """{"type":"Resume","gameId":"g","lastSeq":9,"lastViewHash":"h"}""" },
        { new LobbySet { Slot = 2, Nation = "Rome", Color = "red", Ready = true }, """{"type":"LobbySet","slot":2,"nation":"Rome","color":"red","ready":true}""" },
        { new LobbyChat { Text = "hi" }, """{"type":"LobbyChat","text":"hi"}""" },
        { new Hello { Protocol = 1, ClientVersion = "0.1", RulesetHash = "abc", ModList = [], ModSync = true }, """{"type":"Hello","protocol":1,"clientVersion":"0.1","rulesetHash":"abc","modList":[],"modSync":true}""" },
        { new Hello { Protocol = 1, ClientVersion = "0.1", RulesetHash = "abc", ModList = [], ModSync = true, ModChecksums = new() { ["M"] = "cc" } }, """{"type":"Hello","protocol":1,"clientVersion":"0.1","rulesetHash":"abc","modList":[],"modSync":true,"modChecksums":{"M":"cc"}}""" },
        { new ModFileRequest { Mod = "M", Path = "Units.json" }, """{"type":"ModFileRequest","mod":"M","path":"Units.json"}""" },
        { new ModManifest { BaseRuleset = "B", Mods = ["M"], RulesetHash = "ab", Entries = [new ModEntry { Name = "M", Checksum = "cc", Files = [new ModFileEntry { Path = "Units.json", Size = 2, Sha256 = "dd" }] }] },
            """{"type":"ModManifest","baseRuleset":"B","mods":["M"],"rulesetHash":"ab","entries":[{"name":"M","checksum":"cc","files":[{"path":"Units.json","size":2,"sha256":"dd"}]}]}""" },
        { new ModFileData { Mod = "M", Path = "Units.json", Data = "W10=" }, """{"type":"ModFileData","mod":"M","path":"Units.json","data":"W10="}""" },
        { new MapPing { X = 3, Y = -2, Kind = "Danger" }, """{"type":"MapPing","x":3,"y":-2,"kind":"Danger"}""" },
        { new MapPingNotice { X = 3, Y = -2, Kind = "Danger", Sender = "Bob" }, """{"type":"MapPingNotice","x":3,"y":-2,"kind":"Danger","sender":"Bob"}""" },
        { new Command { ClientCmdId = "c1", CommandType = "MoveUnit", Payload = J("""{"unit":4}""") }, """{"type":"Command","clientCmdId":"c1","commandType":"MoveUnit","payload":{"unit":4}}""" },
        { new Query { QueryId = 12, QueryType = "units.ReachableTiles", Payload = J("""{"unitId":4}""") }, """{"type":"Query","queryId":12,"queryType":"units.ReachableTiles","payload":{"unitId":4}}""" },
        { new QueryResult { QueryId = 12, Ok = true, Payload = J("""{"tiles":[]}""") }, """{"type":"QueryResult","queryId":12,"ok":true,"payload":{"tiles":[]}}""" },
        { new QueryResult { QueryId = 13, Ok = false, Code = "query-rejected" }, """{"type":"QueryResult","queryId":13,"ok":false,"code":"query-rejected"}""" },
        { new ResyncRequest(), """{"type":"ResyncRequest"}""" },
        { new Leave(), """{"type":"Leave"}""" },
        { new Ping(), """{"type":"Ping"}""" },
        { new Welcome { Protocol = 1, SessionId = "s", Challenge = "ch", HostKeyFingerprint = "fp", RequiresPassword = false }, """{"type":"Welcome","protocol":1,"sessionId":"s","challenge":"ch","hostKeyFingerprint":"fp","requiresPassword":false}""" },
        { new JoinOk(), """{"type":"JoinOk"}""" },
        { new JoinRefused { Code = "bad-password" }, """{"type":"JoinRefused","code":"bad-password"}""" },
        { new LobbyState { Settings = J("""{"map":"small"}"""), Slots = [new LobbySlot { Slot = 0, PlayerName = "Ann", Ready = false }], HostName = "Bob" }, """{"type":"LobbyState","settings":{"map":"small"},"slots":[{"slot":0,"playerName":"Ann","ready":false}],"hostName":"Bob"}""" },
        { new GameStarting(), """{"type":"GameStarting"}""" },
        { Status, """{"type":"TurnStatus","turn":3,"ended":["Rome"],"deadline":1700000000000,"phase":"playing"}""" },
        { new Snapshot { Seq = 5, Turn = 3, Phase = "playing", TurnStatus = Status, You = "Rome", Civs = [J("""{"n":1}""")], Tiles = [], Cities = [], Units = [], Diplomacy = [], PendingTrades = [], Notifications = [], ViewHash = "vh" },
            """{"type":"Snapshot","seq":5,"turn":3,"phase":"playing","turnStatus":{"turn":3,"ended":["Rome"],"deadline":1700000000000,"phase":"playing"},"you":"Rome","civs":[{"n":1}],"tiles":[],"cities":[],"units":[],"diplomacy":[],"pendingTrades":[],"notifications":[],"popupAlerts":[],"pendingDecisions":[],"viewHash":"vh"}""" },
        { new ViewDelta { Seq = 6, Events = [J("""{"e":"UnitMoved"}""")] }, """{"type":"ViewDelta","seq":6,"events":[{"e":"UnitMoved"}]}""" },
        { new ViewDelta { Seq = 8, Events = [], Diff = J("""{"seq":8}"""), ViewHash = "vh8" }, """{"type":"ViewDelta","seq":8,"events":[],"diff":{"seq":8},"viewHash":"vh8"}""" },
        { new CommandResult { ClientCmdId = "c1", Accepted = true, Seq = 7 }, """{"type":"CommandResult","clientCmdId":"c1","accepted":true,"seq":7}""" },
        { new CommandResult { ClientCmdId = "c2", Accepted = false, Code = "not-your-turn" }, """{"type":"CommandResult","clientCmdId":"c2","accepted":false,"code":"not-your-turn"}""" },
        { new HostClosing(), """{"type":"HostClosing"}""" },
        { new Error { Code = "unknown-type", Detail = "x" }, """{"type":"Error","code":"unknown-type","detail":"x"}""" },
        { new Pong(), """{"type":"Pong"}""" },
        { new StandbyOffer { KeyId = "k" }, """{"type":"StandbyOffer","keyId":"k"}""" },
        { new StandbyAccept { Endpoints = ["a.example"], Port = 47821, Fingerprint = "fp" }, """{"type":"StandbyAccept","endpoints":["a.example"],"port":47821,"fingerprint":"fp"}""" },
        { new StandbyDecline(), """{"type":"StandbyDecline"}""" },
        { new StandbyRevoke(), """{"type":"StandbyRevoke"}""" },
        { new StandbyStatus { State = "ready", LagEntries = 0 }, """{"type":"StandbyStatus","state":"ready","lagEntries":0}""" },
        { new StandbyStatus { State = "syncing", LagEntries = 3, GameId = "g" }, """{"type":"StandbyStatus","state":"syncing","lagEntries":3,"gameId":"g"}""" },
        { new StandbySubscribe { GameId = "g", HaveSeq = 4, HaveLineSha256 = "ab" }, """{"type":"StandbySubscribe","gameId":"g","haveSeq":4,"haveLineSha256":"ab"}""" },
        { new StandbySubscribe { GameId = "g", HaveSeq = -1 }, """{"type":"StandbySubscribe","gameId":"g","haveSeq":-1}""" },
        { new StandbyMeta { Epoch = 1, Header = "{}", Meta = "{}" }, """{"type":"StandbyMeta","epoch":1,"header":"{}","meta":"{}"}""" },
        { new StandbySnapshot { Epoch = 0, Seq = 9, Part = 0, Parts = 2, Sha256 = "h", Data = "AA==", Reset = true }, """{"type":"StandbySnapshot","epoch":0,"seq":9,"part":0,"parts":2,"sha256":"h","data":"AA==","reset":true}""" },
        { new StandbyEntry { Epoch = 0, Seq = 3, Line = "{\"checkpoint\":3}" }, """{"type":"StandbyEntry","epoch":0,"seq":3,"line":"{\u0022checkpoint\u0022:3}"}""" },
        { new StandbyAck { Seq = 3 }, """{"type":"StandbyAck","seq":3}""" },
        { new HostSuccessor { Epoch = 0, Status = "none", Roster = [] }, """{"type":"HostSuccessor","epoch":0,"status":"none","roster":[]}""" },
        { new HostSuccessor { Epoch = 1, Status = "ready", Successor = new SuccessorInfo { KeyId = "k", Endpoints = ["a.example:47821"], Fingerprint = "fp" }, Roster = [new SuccessorSeat { KeyId = "k", Connected = true }] },
            """{"type":"HostSuccessor","epoch":1,"successor":{"keyId":"k","endpoints":["a.example:47821"],"fingerprint":"fp"},"status":"ready","roster":[{"keyId":"k","connected":true}]}""" },
        { new JoinOk { Successor = new HostSuccessor { Epoch = 0, Status = "offered", Roster = [] } }, """{"type":"JoinOk","successor":{"epoch":0,"status":"offered","roster":[]}}""" },
        { new LobbyState { Settings = J("{}"), Slots = [], HostName = "Bob", BackupHost = 1, BackupHostState = "offered", BackupHostNotice = "n" }, """{"type":"LobbyState","settings":{},"slots":[],"hostName":"Bob","backupHost":1,"backupHostState":"offered","backupHostNotice":"n"}""" },
        { new Welcome { Protocol = 2, SessionId = "s", Challenge = "ch", HostKeyFingerprint = "fp", RequiresPassword = false, BackupHostNotice = "n" }, """{"type":"Welcome","protocol":2,"sessionId":"s","challenge":"ch","hostKeyFingerprint":"fp","requiresPassword":false,"backupHostNotice":"n"}""" },
        { new HostHandoff { GameId = "g", Epoch = 1, Successor = new SuccessorInfo { KeyId = "k", Endpoints = ["a.example:47821"], Fingerprint = "fp" }, FinalSeq = 7, FinalHash = "ab", DepartedKeyId = "d" },
            """{"type":"HostHandoff","gameId":"g","epoch":1,"successor":{"keyId":"k","endpoints":["a.example:47821"],"fingerprint":"fp"},"finalSeq":7,"finalHash":"ab","departedKeyId":"d"}""" },
        { new Snapshot { Seq = 5, Turn = 3, Phase = "playing", TurnStatus = Status, You = "Rome", Civs = [], Tiles = [], Cities = [], Units = [], Diplomacy = [], PendingTrades = [], Notifications = [], ViewHash = "vh", RolledBack = true, LostCommands = 2 },
            """{"type":"Snapshot","seq":5,"turn":3,"phase":"playing","turnStatus":{"turn":3,"ended":["Rome"],"deadline":1700000000000,"phase":"playing"},"you":"Rome","civs":[],"tiles":[],"cities":[],"units":[],"diplomacy":[],"pendingTrades":[],"notifications":[],"rolledBack":true,"lostCommands":2,"popupAlerts":[],"pendingDecisions":[],"viewHash":"vh"}""" },
        { new Welcome { Protocol = 4, SessionId = "s", Challenge = "ch", HostKeyFingerprint = "fp", RequiresPassword = false, Epoch = 1, Promotion = new PromotionProof { GameId = "g", Epoch = 1, KeyId = "k", Fingerprint = "fp", Signature = "sig" } },
            """{"type":"Welcome","protocol":4,"sessionId":"s","challenge":"ch","hostKeyFingerprint":"fp","requiresPassword":false,"epoch":1,"promotion":{"gameId":"g","epoch":1,"keyId":"k","fingerprint":"fp","signature":"sig"}}""" },
    };

    [Theory]
    [MemberData(nameof(Goldens))]
    public void EncodesToGoldenJson(Message message, string golden) =>
        Assert.Equal(golden, ProtocolCodec.Encode(message));

    [Theory]
    [MemberData(nameof(Goldens))]
    public void DecodeRoundTripsGoldenJson(Message message, string golden)
    {
        var decoded = ProtocolCodec.Decode(golden);
        Assert.Equal(message.GetType(), decoded.GetType());
        Assert.Equal(golden, ProtocolCodec.Encode(decoded));
    }

    [Fact]
    public void EveryMessageTypeHasAGolden()
    {
        var covered = Goldens.Select(d => ((Message)d[0]).GetType()).ToHashSet();
        foreach (var type in ProtocolCodec.MessageTypes) Assert.Contains(type, covered);
    }

    [Fact]
    public void DecodeIgnoresFieldOrderAndRejectsDifferentCase()
    {
        Assert.IsType<Auth>(ProtocolCodec.Decode("""{"proof":"x","type":"Auth"}"""));
        AssertError(ErrorCodes.InvalidMessage, """{"type":"Auth","Proof":"x"}""");
    }

    [Theory]
    [InlineData("""{"type":"Nope"}""", ErrorCodes.UnknownType)]
    [InlineData("""{"type":"Ping","extra":1}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"Auth"}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"Auth","proof":5}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"Query","queryType":"a","payload":{}}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"Query","queryId":1,"queryType":"a"}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"Query","queryId":1,"queryType":"a","payload":{},"viewer":"Rome"}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"QueryResult","queryId":1}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"type":"Auth","proof":null}""", ErrorCodes.InvalidMessage)]
    [InlineData("""{"proof":"x"}""", ErrorCodes.Malformed)]
    [InlineData("""{"type":7}""", ErrorCodes.Malformed)]
    [InlineData("""{"type":"Message"}""", ErrorCodes.UnknownType)]
    [InlineData("""{"type":"Ping","type":"Pong"}""", ErrorCodes.Malformed)]
    [InlineData("""{"type":"Auth","proof":"a","proof":"b"}""", ErrorCodes.Malformed)]
    [InlineData("""{"type":"Ping"} trailing""", ErrorCodes.Malformed)]
    [InlineData("not json", ErrorCodes.Malformed)]
    [InlineData("", ErrorCodes.Malformed)]
    [InlineData("[1]", ErrorCodes.Malformed)]
    [InlineData("null", ErrorCodes.Malformed)]
    public void BadFramesBecomeErrorsNotExceptions(string frame, string code) => AssertError(code, frame);

    [Fact]
    public void DeeplyNestedFrameIsMalformed() =>
        AssertError(ErrorCodes.Malformed, "{\"type\":\"Ping\",\"x\":" + new string('[', 200) + new string(']', 200) + "}");

    [Fact]
    public void OversizeFrameIsRejectedBeforeParsing()
    {
        var frame = """{"type":"LobbyChat","text":"%"}""".Replace("%", new string('a', ProtocolVersion.MaxMessageBytes));
        AssertError(ErrorCodes.FrameTooLarge, frame);
    }

    [Fact]
    public void OversizeIsMeasuredInBytesNotChars()
    {
        var frame = new string('é', ProtocolVersion.MaxMessageBytes / 2 + 1);
        AssertError(ErrorCodes.FrameTooLarge, frame);
    }

    [Fact]
    public void EncodeRefusesOversizeMessage() =>
        Assert.Throws<InvalidOperationException>(() => ProtocolCodec.Encode(new LobbyChat { Text = new string('a', ProtocolVersion.MaxMessageBytes) }));

    [Fact]
    public void ProtocolVersionCompatibility()
    {
        Assert.True(ProtocolVersion.IsCompatible(ProtocolVersion.Current));
        Assert.False(ProtocolVersion.IsCompatible(ProtocolVersion.Current + 1));
    }

    [Fact]
    public async Task InMemoryTransportDeliversInOrderBothWays()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        await a.SendAsync("1");
        await a.SendAsync("2");
        await b.SendAsync("x");
        Assert.Equal("1", await b.ReceiveAsync());
        Assert.Equal("2", await b.ReceiveAsync());
        Assert.Equal("x", await a.ReceiveAsync());
    }

    [Fact]
    public async Task InMemoryTransportCloseDrainsThenEndsAndRejectsSends()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        await a.SendAsync("last");
        await a.CloseAsync();
        await a.CloseAsync();
        Assert.False(a.IsOpen);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await a.SendAsync("late"));
        Assert.Equal("last", await b.ReceiveAsync());
        Assert.Null(await b.ReceiveAsync());
        await b.DisposeAsync();
        Assert.False(b.IsOpen);
    }

    [Fact]
    public async Task InMemoryTransportReceiveWaitsAndHonoursCancellation()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        var pending = b.ReceiveAsync().AsTask();
        Assert.False(pending.IsCompleted);
        await a.SendAsync("go");
        Assert.Equal("go", await pending);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await b.ReceiveAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await a.SendAsync("x", cts.Token));
    }

    [Fact]
    public async Task MessagesCrossTheTransportEndToEnd()
    {
        var (a, b) = InMemoryTransport.CreatePair();
        await a.SendAsync(ProtocolCodec.Encode(new Ping()));
        await a.SendAsync("garbage");
        Assert.IsType<Ping>(ProtocolCodec.Decode((await b.ReceiveAsync())!));
        Assert.Equal(ErrorCodes.Malformed, ((Error)ProtocolCodec.Decode((await b.ReceiveAsync())!)).Code);
    }

    private static void AssertError(string code, string frame)
    {
        var error = Assert.IsType<Error>(ProtocolCodec.Decode(frame));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("""{"a":1,"b":2}""", false)]
    [InlineData("""{"a":1,"a":2}""", true)]
    [InlineData("""{"x":{"a":1},"y":{"a":2}}""", false)]
    [InlineData("""{"x":{"a":1,"a":2}}""", true)]
    [InlineData("""{"x":[{"a":1},{"a":2}],"y":[1,2]}""", false)]
    [InlineData("""{"x":[{"a":1,"a":2}]}""", true)]
    public void HasDuplicateProperties_Detects_Repeated_Names_Per_Object(string json, bool expected)
        => Assert.Equal(expected, ProtocolCodec.HasDuplicateProperties(json));

    [Fact]
    public void HasDuplicateProperties_Throws_On_Malformed_Json()
        => Assert.ThrowsAny<JsonException>(() => ProtocolCodec.HasDuplicateProperties("{\"a\":"));
}
