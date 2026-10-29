using System.Text;
using Unciv.Client.Net.Mods;
using Unciv.Client.Net.Session;
using Unciv.Models.Ruleset;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Mods;

namespace Unciv.Core.Tests.Net.ModSync;

/// <summary>A mod folder lookup that does not go through <see cref="RulesetCache"/>, so the host's files can differ from what the (process-wide) cache holds.</summary>
internal sealed class FixedFolders(Dictionary<string, string> directories) : IModFolders
{
    public IRulesetFolder? FolderOf(string modName) => directories.TryGetValue(modName, out var dir) ? new DirectoryRulesetFolder(dir) : null;
}

/// <summary>T247: mod sync. A host with a mod the client lacks, over in-memory transports and real folders.</summary>
[Collection("RulesetCache")]
public sealed class ModSyncTests : IDisposable
{
    private const string UnitsJson = """[{"name":"Sync Test Unit","unitType":"Melee","movement":2,"strength":6,"cost":30}]""";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    private readonly string root = Directory.CreateTempSubdirectory("unciv-modsync-").FullName;
    private readonly string jsons;
    private readonly string hostMods;
    private readonly string clientMods;
    private readonly string clientHash;
    private readonly string hostHash;
    private readonly ClientIdentity identity = ClientIdentity.Generate();

    public ModSyncTests()
    {
        jsons = Path.Combine(RepoRoot(), "android", "assets", "jsons");
        hostMods = Path.Combine(root, "host-mods");
        clientMods = Path.Combine(root, "client-mods");
        Directory.CreateDirectory(clientMods);
        WriteMod(hostMods, "TestMod", UnitsJson);
        // The client's own rules (the mod unknown to it) and the host's (the mod loaded).
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, clientMods));
        clientHash = Settings().ComputeRulesetHash();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, hostMods));
        hostHash = Settings().ComputeRulesetHash();
    }

    public void Dispose()
    {
        identity.Dispose();
        RulesetCache.Clear();
        Directory.Delete(root, recursive: true);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "android", "assets", "jsons"))) return dir.FullName;
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private static LobbySettings Settings(string? password = null) => new() { Mods = ["TestMod"], Password = password };

    private static void WriteMod(string mods, string name, string units)
    {
        var folder = Path.Combine(mods, name, "jsons");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Units.json"), units, new UTF8Encoding(false));
    }

    private DirectoryRulesetFileSource ClientSource() => new(jsons, clientMods);

    private LobbyHost NewLobby(string? password = null, Dictionary<string, string>? folders = null) =>
        new(settings: Settings(password), hostFingerprint: ClientNetFingerprint) { ModFolders = new FixedFolders(folders ?? new() { ["TestMod"] = Path.Combine(hostMods, "TestMod") }) };

    private static readonly string ClientNetFingerprint = HostKey.Fingerprint(new byte[] { 9, 8, 7, 6 });

    private ClientSessionOptions Options(IModSyncHandler? sync, string? hash = null, string? password = null, string? invite = null) => new()
    {
        Identity = identity,
        DisplayName = "Mallory",
        RulesetHash = hash ?? clientHash,
        ExpectedFingerprint = ClientNetFingerprint,
        Password = password,
        InviteToken = invite,
        ModSync = sync,
        MaxReconnectAttempts = 0,
        HandshakeTimeout = Timeout.InfiniteTimeSpan,
        KeepAliveInterval = Timeout.InfiniteTimeSpan,
        HostSilenceTimeout = Timeout.InfiniteTimeSpan,
    };

    private ModSyncService Service(Func<ModSyncPlan, Task<bool>>? confirm = null) => new(ClientSource(), clientMods, confirm ?? (_ => Task.FromResult(true)));

    private static ClientSession Connect(LobbyHost lobby, ClientSessionOptions options)
    {
        var (client, host) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(host);
        return new ClientSession(_ => Task.FromResult<ITransport>(client), options);
    }

    // ---------------------------------------------------------------- raw host conversation

    private async Task<(InMemoryTransport Client, Welcome Welcome)> HelloAsync(LobbyHost lobby, string hash, bool? modSync)
    {
        var (client, host) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(host);
        await client.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = hash, ModList = [], ModSync = modSync }));
        var first = await ReceiveAsync<Message>(client);
        return (client, first as Welcome ?? new Welcome { Protocol = 0, SessionId = "", Challenge = "", HostKeyFingerprint = "", RequiresPassword = false });
    }

    private static async Task<T> ReceiveAsync<T>(InMemoryTransport transport) where T : Message
    {
        using var cts = new CancellationTokenSource(Guard);
        var frame = await transport.ReceiveAsync(cts.Token);
        Assert.NotNull(frame);
        return Assert.IsAssignableFrom<T>(ProtocolCodec.Decode(frame));
    }

    private async Task<ModManifest> ManifestAsync(InMemoryTransport client, Welcome welcome, string? password = null)
    {
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory", Password = password }));
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        return await ReceiveAsync<ModManifest>(client);
    }

    private static async Task<Message> RequestAsync(InMemoryTransport client, string mod, string path)
    {
        await client.SendAsync(ProtocolCodec.Encode(new ModFileRequest { Mod = mod, Path = path }));
        return await ReceiveAsync<Message>(client);
    }

    [Fact]
    public async Task Host_WithoutModSyncFlag_StillRefusesAtHello()
    {
        await using var lobby = NewLobby();
        var (client, _) = await HelloAsync(lobby, clientHash, modSync: null);
        // HelloAsync reads the first reply: here the refusal
        var (c2, host2) = InMemoryTransport.CreatePair();
        _ = lobby.AttachConnectionAsync(host2);
        await c2.SendAsync(ProtocolCodec.Encode(new Hello { Protocol = ProtocolVersion.Current, ClientVersion = "t", RulesetHash = clientHash, ModList = [] }));
        Assert.Equal(JoinRefusedCodes.RulesetMismatch, (await ReceiveAsync<JoinRefused>(c2)).Code);
        await client.CloseAsync();
    }

    [Fact]
    public async Task Host_ModSyncMismatch_ServesManifestOnlyAfterAuthAndOnlyListedFiles()
    {
        await using var lobby = NewLobby();
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        Assert.NotEqual("", welcome.SessionId);

        // Before the join nothing is served, and nothing about the mods is revealed.
        Assert.Equal(ErrorCodes.ModFileRefused, ((Error)await RequestAsync(client, "TestMod", "Units.json")).Code);

        var manifest = await ManifestAsync(client, welcome);
        Assert.Equal(hostHash, manifest.RulesetHash);
        var entry = Assert.Single(manifest.Entries);
        Assert.Equal("TestMod", entry.Name);
        var file = Assert.Single(entry.Files);
        Assert.Equal("Units.json", file.Path);
        Assert.Equal(Encoding.UTF8.GetByteCount(UnitsJson), file.Size);
        Assert.Equal(ModManifestBuilder.ChecksumOf(entry.Files), entry.Checksum);
        Assert.Null(ModManifestBuilder.Validate(manifest));

        var data = Assert.IsType<ModFileData>(await RequestAsync(client, "TestMod", "Units.json"));
        Assert.Equal(UnitsJson, Encoding.UTF8.GetString(Convert.FromBase64String(data.Data)));

        // Each file once per connection; unlisted names, traversal and other mods are refused with no detail.
        foreach (var (mod, path) in new[] { ("TestMod", "Units.json"), ("TestMod", "Techs.json"), ("TestMod", "../Units.json"), ("TestMod", "jsons/Units.json"), ("TestMod", "..\\Units.json"), ("Other", "Units.json"), ("../host-mods/TestMod", "Units.json") })
        {
            var reply = Assert.IsType<Error>(await RequestAsync(client, mod, path));
            Assert.Equal(ErrorCodes.ModFileRefused, reply.Code);
            Assert.Null(reply.Detail);
        }
    }

    [Fact]
    public async Task Host_ModSync_WrongPasswordGetsNoManifest()
    {
        await using var lobby = NewLobby(password: "open sesame");
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory", Password = "nope" }));
        Assert.Equal(JoinRefusedCodes.BadCredentials, (await ReceiveAsync<JoinRefused>(client)).Code);
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        Assert.Equal(JoinRefusedCodes.AuthFailed, (await ReceiveAsync<JoinRefused>(client)).Code);
        Assert.Equal(ErrorCodes.ModFileRefused, ((Error)await RequestAsync(client, "TestMod", "Units.json")).Code);
    }

    [Fact]
    public async Task Host_ModSync_KeyProofIsStillRequired()
    {
        await using var lobby = NewLobby();
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory" }));
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = "AAAA" }));
        Assert.Equal(JoinRefusedCodes.AuthFailed, (await ReceiveAsync<JoinRefused>(client)).Code);
        _ = welcome;
    }

    [Fact]
    public async Task Host_ModSync_NothingDownloadable_RefusesWithUnavailable()
    {
        await using var lobby = NewLobby(folders: new());
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory" }));
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        Assert.Equal(JoinRefusedCodes.ModSyncUnavailable, (await ReceiveAsync<JoinRefused>(client)).Code);
    }

    [Fact]
    public async Task Host_ModSync_OversizedFile_RefusesWithUnavailable()
    {
        WriteMod(hostMods, "Big", "[" + new string(' ', ModSyncRules.MaxFileBytes) + "]");
        await using var lobby = NewLobby(folders: new() { ["Big"] = Path.Combine(hostMods, "Big") });
        lobby.Settings.Mods = ["Big"];
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory" }));
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        Assert.Equal(JoinRefusedCodes.ModSyncUnavailable, (await ReceiveAsync<JoinRefused>(client)).Code);
    }

    [Fact]
    public async Task Host_FileChangedAfterManifest_IsNotServed()
    {
        await using var lobby = NewLobby();
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        await ManifestAsync(client, welcome);
        WriteMod(hostMods, "TestMod", """[{"name":"Swapped","unitType":"Melee"}]""");
        Assert.Equal(ErrorCodes.ModFileRefused, ((Error)await RequestAsync(client, "TestMod", "Units.json")).Code);
    }

    [Fact]
    public async Task Host_ModSync_DoesNotConsumeTheInviteToken()
    {
        var tokens = new InviteTokens(TimeProvider.System, SystemSecureRandom.Instance);
        var token = tokens.Mint(TimeSpan.FromMinutes(5))!;
        var admission = new LobbyAdmission(null, inviteOnly: true, tokens, new AttemptLimiter(TimeProvider.System));
        await using var lobby = new LobbyHost(settings: Settings(), admission: admission, hostFingerprint: ClientNetFingerprint) { ModFolders = new FixedFolders(new() { ["TestMod"] = Path.Combine(hostMods, "TestMod") }) };
        var (client, welcome) = await HelloAsync(lobby, clientHash, modSync: true);
        await client.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory", InviteToken = token }));
        await client.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome.HostKeyFingerprint, welcome.SessionId, welcome.Challenge) }));
        await ReceiveAsync<ModManifest>(client);
        Assert.Equal(1, tokens.Outstanding);

        // The same token still admits the real join afterwards.
        var (again, welcome2) = await HelloAsync(lobby, hostHash, modSync: true);
        await again.SendAsync(ProtocolCodec.Encode(new Join { PlayerKey = identity.PlayerKey, DisplayName = "Mallory", InviteToken = token }));
        await again.SendAsync(ProtocolCodec.Encode(new Auth { Proof = identity.Sign(welcome2.HostKeyFingerprint, welcome2.SessionId, welcome2.Challenge) }));
        await ReceiveAsync<JoinOk>(again);
        Assert.Equal(0, tokens.Outstanding);
    }

    // ---------------------------------------------------------------- client with a real host

    [Fact]
    public async Task Client_MissingMod_DownloadsInstallsHotLoadsAndRejoins()
    {
        await using var lobby = NewLobby();
        ModSyncPlan? shown = null;
        var service = Service(plan => { shown = plan; return Task.FromResult(true); });
        await using var first = Connect(lobby, Options(service));
        var result = await first.JoinAsync().WaitAsync(Guard);
        Assert.Equal(new JoinResult(false, ModSyncCodes.Synced), result);
        Assert.Equal(new ModSyncItem("TestMod", ModSyncStatus.Missing, Encoding.UTF8.GetByteCount(UnitsJson)), Assert.Single(shown!.Items));

        var outcome = first.ModSyncResult!;
        Assert.Equal(["TestMod"], outcome.Installed);
        Assert.Equal(UnitsJson, File.ReadAllText(Path.Combine(clientMods, "TestMod", "jsons", "Units.json")));
        Assert.DoesNotContain(Directory.GetDirectories(clientMods), it => Path.GetFileName(it).StartsWith(".sync-staging-", StringComparison.Ordinal));

        Assert.Null(service.HotLoad(outcome));
        Assert.True(RulesetCache.ContainsKey("TestMod"));
        Assert.Equal(hostHash, Settings().ComputeRulesetHash());

        await using var second = Connect(lobby, Options(null, hash: outcome.Manifest!.RulesetHash));
        Assert.True((await second.JoinAsync().WaitAsync(Guard)).Joined);
    }

    [Fact]
    public async Task Client_WithoutHandler_IsRefusedAsBefore()
    {
        await using var lobby = NewLobby();
        await using var session = Connect(lobby, Options(null));
        Assert.Equal(new JoinResult(false, JoinRefusedCodes.RulesetMismatch), await session.JoinAsync().WaitAsync(Guard));
    }

    [Fact]
    public async Task Client_Declined_NothingIsWritten()
    {
        await using var lobby = NewLobby();
        await using var session = Connect(lobby, Options(Service(_ => Task.FromResult(false))));
        Assert.Equal(new JoinResult(false, ModSyncCodes.Declined), await session.JoinAsync().WaitAsync(Guard));
        Assert.Empty(Directory.GetFileSystemEntries(clientMods));
    }

    [Fact]
    public async Task Client_FailingPrompt_CountsAsNo()
    {
        await using var lobby = NewLobby();
        await using var session = Connect(lobby, Options(Service(_ => throw new InvalidOperationException("ui gone"))));
        Assert.Equal(new JoinResult(false, ModSyncCodes.Declined), await session.JoinAsync().WaitAsync(Guard));
    }

    [Fact]
    public async Task Client_DifferentMod_ReplacesItAndKeepsABackup()
    {
        WriteMod(clientMods, "TestMod", """[{"name":"Mine","unitType":"Melee"}]""");
        await using var lobby = NewLobby();
        ModSyncPlan? shown = null;
        await using var session = Connect(lobby, Options(Service(plan => { shown = plan; return Task.FromResult(true); })));
        Assert.Equal(ModSyncCodes.Synced, (await session.JoinAsync().WaitAsync(Guard)).Code);
        Assert.Equal(ModSyncStatus.Different, Assert.Single(shown!.Items).Status);
        Assert.Equal(UnitsJson, File.ReadAllText(Path.Combine(clientMods, "TestMod", "jsons", "Units.json")));
        Assert.Contains("Mine", File.ReadAllText(Path.Combine(clientMods, ".sync-backup-TestMod", "jsons", "Units.json")));
    }

    [Fact]
    public async Task Client_ModAlreadyIdentical_NeedsNoDownloadAndMismatchStaysRefused()
    {
        WriteMod(clientMods, "TestMod", UnitsJson);
        await using var lobby = NewLobby();
        var asked = false;
        await using var session = Connect(lobby, Options(Service(_ => { asked = true; return Task.FromResult(true); })));
        Assert.Equal(new JoinResult(false, JoinRefusedCodes.RulesetMismatch), await session.JoinAsync().WaitAsync(Guard));
        Assert.False(asked);
    }

    [Fact]
    public async Task Client_NameDifferingOnlyInCase_IsRefused()
    {
        WriteMod(clientMods, "testmod", UnitsJson);
        await using var lobby = NewLobby();
        await using var session = Connect(lobby, Options(Service()));
        Assert.Equal(ModSyncCodes.Failed, (await session.JoinAsync().WaitAsync(Guard)).Code);
        Assert.False(Directory.Exists(Path.Combine(clientMods, "TestMod")));
    }

    // ---------------------------------------------------------------- hostile hosts

    /// <summary>A scripted host that answers the client's Hello and join, then sends <paramref name="manifest"/> and serves <paramref name="serve"/> for each file request.</summary>
    private async Task<JoinResult> AgainstScriptedHostAsync(ModManifest manifest, Func<ModFileRequest, Message> serve, ModSyncService service)
    {
        var (client, host) = InMemoryTransport.CreatePair();
        await using var session = new ClientSession(_ => Task.FromResult<ITransport>(client), Options(service));
        var join = session.JoinAsync();
        var script = Task.Run(async () =>
        {
            Assert.IsType<Hello>(await ReceiveAsync<Message>(host));
            await host.SendAsync(ProtocolCodec.Encode(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetFingerprint, RequiresPassword = false }));
            await ReceiveAsync<Join>(host);
            await ReceiveAsync<Auth>(host);
            await host.SendAsync(ProtocolCodec.Encode(manifest));
            while (true)
            {
                using var cts = new CancellationTokenSource(Guard);
                var frame = await host.ReceiveAsync(cts.Token);
                if (frame == null) return;
                if (ProtocolCodec.Decode(frame) is ModFileRequest request) await host.SendAsync(ProtocolCodec.Encode(serve(request)));
            }
        });
        var result = await join.WaitAsync(Guard);
        await host.CloseAsync();
        await script.WaitAsync(Guard);
        return result;
    }

    private static ModManifest ManifestOf(string modName, string path, byte[] content, string? sha = null, string? checksum = null)
    {
        var files = new List<ModFileEntry> { new() { Path = path, Size = content.Length, Sha256 = sha ?? ModManifestBuilder.HashOf(content) } };
        return new ModManifest { BaseRuleset = "Civ V - Gods & Kings", Mods = [modName], RulesetHash = "0123456789abcdef", Entries = [new ModEntry { Name = modName, Files = files, Checksum = checksum ?? ModManifestBuilder.ChecksumOf(files) }] };
    }

    private static ModFileData Data(ModFileRequest request, byte[] bytes) => new() { Mod = request.Mod, Path = request.Path, Data = Convert.ToBase64String(bytes) };

    private void AssertNothingInstalled() => Assert.Empty(Directory.GetFileSystemEntries(clientMods));

    [Fact]
    public async Task Hostile_FileDoesNotMatchItsHash_IsRejected()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var manifest = ManifestOf("Evil", "Units.json", good);
        var tampered = Encoding.UTF8.GetBytes(UnitsJson.Replace("30", "99", StringComparison.Ordinal));
        var result = await AgainstScriptedHostAsync(manifest, request => Data(request, tampered), Service());
        Assert.Equal(ModSyncCodes.BadFile, result.Code);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_FileLongerThanListed_IsRejected()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var result = await AgainstScriptedHostAsync(ManifestOf("Evil", "Units.json", good), request => Data(request, [.. good, .. new byte[10]]), Service());
        Assert.Equal(ModSyncCodes.BadFile, result.Code);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_BinaryContent_IsRejectedEvenWithAMatchingHash()
    {
        byte[] binary = [0x4d, 0x5a, 0x90, 0x00, 0xff, 0xfe, 0xc0];
        var result = await AgainstScriptedHostAsync(ManifestOf("Evil", "Units.json", binary), request => Data(request, binary), Service());
        Assert.Equal(ModSyncCodes.BadFile, result.Code);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_ContentThatIsNotARuleset_IsRejectedBeforeInstalling()
    {
        var junk = Encoding.UTF8.GetBytes("{ this is not json at all");
        var result = await AgainstScriptedHostAsync(ManifestOf("Evil", "Units.json", junk), request => Data(request, junk), Service());
        Assert.Equal(ModSyncCodes.BadFile, result.Code);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_BadBase64_AndHostRefusal_AndWrongReply_FailWithoutInstalling()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var manifest = ManifestOf("Evil", "Units.json", good);
        Assert.Equal(ModSyncCodes.BadFile, (await AgainstScriptedHostAsync(manifest, request => new ModFileData { Mod = request.Mod, Path = request.Path, Data = "***not base64***" }, Service())).Code);
        Assert.Equal(ModSyncCodes.Failed, (await AgainstScriptedHostAsync(manifest, _ => new Error { Code = ErrorCodes.ModFileRefused }, Service())).Code);
        Assert.Equal(ModSyncCodes.BadFile, (await AgainstScriptedHostAsync(manifest, _ => new LobbyChat { Text = "hi" }, Service())).Code);
        AssertNothingInstalled();
    }

    [Theory]
    [InlineData("../Evil")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc")]
    [InlineData("C:\\Windows")]
    [InlineData(".hidden")]
    [InlineData("CON")]
    [InlineData("Civ V - Vanilla")]
    [InlineData("civ v - gods & kings")]
    [InlineData("")]
    public async Task Hostile_UnsafeModName_IsRejectedBeforeAnything_IsRequested(string name)
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var requested = false;
        var result = await AgainstScriptedHostAsync(ManifestOf(name, "Units.json", good), request => { requested = true; return Data(request, good); }, Service());
        Assert.Equal(ModSyncCodes.BadManifest, result.Code);
        Assert.False(requested);
        AssertNothingInstalled();
    }

    [Theory]
    [InlineData("../Units.json")]
    [InlineData("jsons/Units.json")]
    [InlineData("..\\Units.json")]
    [InlineData("Evil.dll")]
    [InlineData("units.json")]
    [InlineData("Units.json:stream")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public async Task Hostile_UnsafeFilePath_IsRejectedBeforeAnything_IsRequested(string path)
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var requested = false;
        var result = await AgainstScriptedHostAsync(ManifestOf("Evil", path, good), request => { requested = true; return Data(request, good); }, Service());
        Assert.Equal(ModSyncCodes.BadManifest, result.Code);
        Assert.False(requested);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_ManifestLies_AboutChecksumSizeOrHash_IsRejected()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var wrongChecksum = ManifestOf("Evil", "Units.json", good, checksum: new string('0', 64));
        var oversize = ManifestOf("Evil", "Units.json", good);
        oversize.Entries[0].Files[0] = oversize.Entries[0].Files[0] with { Size = ModSyncRules.MaxFileBytes + 1 };
        var badHash = ManifestOf("Evil", "Units.json", good, sha: "XYZ", checksum: new string('0', 64));
        foreach (var manifest in new[] { wrongChecksum, oversize, badHash })
            Assert.Equal(ModSyncCodes.BadManifest, (await AgainstScriptedHostAsync(manifest, request => Data(request, good), Service())).Code);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_HostClosesMidDownload_LeavesNothing()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var (client, host) = InMemoryTransport.CreatePair();
        await using var session = new ClientSession(_ => Task.FromResult<ITransport>(client), Options(Service()));
        var join = session.JoinAsync();
        await ReceiveAsync<Hello>(host);
        await host.SendAsync(ProtocolCodec.Encode(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetFingerprint, RequiresPassword = false }));
        await ReceiveAsync<Join>(host);
        await ReceiveAsync<Auth>(host);
        await host.SendAsync(ProtocolCodec.Encode(ManifestOf("Evil", "Units.json", good)));
        await ReceiveAsync<ModFileRequest>(host);
        await host.CloseAsync();
        Assert.Equal(ModSyncCodes.Failed, (await join.WaitAsync(Guard)).Code);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Hostile_SilentHost_TimesOutPerFile()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var (client, host) = InMemoryTransport.CreatePair();
        var options = Options(Service());
        await using var session = new ClientSession(_ => Task.FromResult<ITransport>(client), new ClientSessionOptions
        {
            Identity = options.Identity,
            DisplayName = "M",
            RulesetHash = "h",
            ExpectedFingerprint = ClientNetFingerprint,
            ModSync = Service(),
            ModFileTimeout = TimeSpan.FromMilliseconds(200),
            MaxReconnectAttempts = 0,
            HandshakeTimeout = Timeout.InfiniteTimeSpan,
            KeepAliveInterval = Timeout.InfiniteTimeSpan,
        });
        var join = session.JoinAsync();
        await ReceiveAsync<Hello>(host);
        await host.SendAsync(ProtocolCodec.Encode(new Welcome { Protocol = ProtocolVersion.Current, SessionId = "s", Challenge = "c", HostKeyFingerprint = ClientNetFingerprint, RequiresPassword = false }));
        await ReceiveAsync<Join>(host);
        await ReceiveAsync<Auth>(host);
        await host.SendAsync(ProtocolCodec.Encode(ManifestOf("Evil", "Units.json", good)));
        await ReceiveAsync<ModFileRequest>(host); // and never answers
        Assert.Equal(ModSyncCodes.Failed, (await join.WaitAsync(Guard)).Code);
        AssertNothingInstalled();
    }

    // ---------------------------------------------------------------- rules and builder

    [Theory]
    [InlineData("Good Mod", true)]
    [InlineData("Mod-1_x.y", true)]
    [InlineData("Dave's Mod", true)]
    [InlineData("Trailing ", false)]
    [InlineData("Dot.", false)]
    [InlineData(" Lead", false)]
    [InlineData("Ünicode", false)]
    [InlineData("tab\there", false)]
    [InlineData("nul.txt", false)]
    [InlineData("COM1", false)]
    [InlineData("Civ V - Vanilla", false)]
    [InlineData("Civ V - Brave New World", false)]
    public void ModNames_AreCheckedStrictly(string name, bool valid) => Assert.Equal(valid, ModSyncRules.IsValidModName(name));

    [Fact]
    public void ModNames_LongerThanTheLimitAreInvalid()
    {
        Assert.True(ModSyncRules.IsValidModName(new string('a', ModSyncRules.MaxNameLength)));
        Assert.False(ModSyncRules.IsValidModName(new string('a', ModSyncRules.MaxNameLength + 1)));
    }

    [Fact]
    public void AllowedFiles_AreExactlyTheRulesetJsonNames()
    {
        Assert.Contains("Units.json", ModSyncRules.AllowedFiles);
        Assert.All(ModSyncRules.AllowedFiles, name => Assert.True(name.EndsWith(".json", StringComparison.Ordinal) && !name.Contains('/', StringComparison.Ordinal)));
        Assert.False(ModSyncRules.IsAllowedFile(null));
    }

    [Fact]
    public void Builder_SkipsAbsentFiles_AndRefusesEmptyOversizedAndBadlyNamedMods()
    {
        var entry = ModManifestBuilder.Build("TestMod", new DirectoryRulesetFolder(Path.Combine(hostMods, "TestMod")))!;
        Assert.Equal(["Units.json"], entry.Files.Select(it => it.Path));
        Assert.Equal(entry.Checksum, ModManifestBuilder.ChecksumOf(entry.Files.AsEnumerable().Reverse()));

        var empty = Directory.CreateDirectory(Path.Combine(hostMods, "Empty", "jsons"));
        Assert.Null(ModManifestBuilder.Build("Empty", new DirectoryRulesetFolder(empty.Parent!.FullName)));
        Assert.Null(ModManifestBuilder.Build("../bad", new DirectoryRulesetFolder(Path.Combine(hostMods, "TestMod"))));

        var tooMany = Path.Combine(hostMods, "Many");
        Directory.CreateDirectory(Path.Combine(tooMany, "jsons"));
        foreach (var name in new[] { "Units.json", "Techs.json", "Buildings.json" })
            File.WriteAllText(Path.Combine(tooMany, "jsons", name), "[" + new string(' ', ModSyncRules.MaxFileBytes - 8) + "]");
        Assert.NotNull(ModManifestBuilder.Build("Many", new DirectoryRulesetFolder(tooMany)));
        for (var i = 0; i < 9; i++) File.WriteAllText(Path.Combine(tooMany, "jsons", ModSyncRules.AllowedFiles[i]), "[" + new string(' ', ModSyncRules.MaxFileBytes - 8) + "]");
        Assert.Null(ModManifestBuilder.Build("Many", new DirectoryRulesetFolder(tooMany)));
    }

    [Fact]
    public void Builder_ReadFile_OnlyServesAllowedNames()
    {
        var folder = new DirectoryRulesetFolder(Path.Combine(hostMods, "TestMod"));
        Assert.NotNull(ModManifestBuilder.ReadFile(folder, "Units.json"));
        Assert.Null(ModManifestBuilder.ReadFile(folder, "Techs.json"));
        Assert.Null(ModManifestBuilder.ReadFile(folder, "../TestMod/jsons/Units.json"));
    }

    [Fact]
    public void Validate_RejectsTooManyModsDuplicatesAndTotals()
    {
        var good = Encoding.UTF8.GetBytes(UnitsJson);
        var one = ManifestOf("A", "Units.json", good);
        Assert.Null(ModManifestBuilder.Validate(one));
        var many = one with { Entries = [.. Enumerable.Range(0, ModSyncRules.MaxMods + 1).Select(i => one.Entries[0] with { Name = "M" + i })] };
        Assert.Equal("too many mods", ModManifestBuilder.Validate(many));
        var dup = one with { Entries = [one.Entries[0], one.Entries[0] with { Name = "a" }] };
        Assert.Equal("duplicate mod", ModManifestBuilder.Validate(dup));
        var dupFile = one.Entries[0] with { Files = [one.Entries[0].Files[0], one.Entries[0].Files[0]] };
        Assert.Equal("duplicate file", ModManifestBuilder.Validate(one with { Entries = [dupFile] }));
        var noFiles = one.Entries[0] with { Files = [] };
        Assert.Equal("bad file count", ModManifestBuilder.Validate(one with { Entries = [noFiles] }));
        Assert.Equal("bad ruleset hash", ModManifestBuilder.Validate(one with { RulesetHash = "" }));
        Assert.Equal("bad name", ModManifestBuilder.Validate(one with { Mods = [new string('x', 65)] }));

        // each mod is within its caps but together they are over the download cap
        var big = Enumerable.Range(0, 6).Select(i =>
        {
            var files = ModSyncRules.AllowedFiles.Take(8).Select(name => new ModFileEntry { Path = name, Size = ModSyncRules.MaxFileBytes, Sha256 = new string('a', 64) }).ToList();
            return new ModEntry { Name = "Big" + i, Files = files, Checksum = ModManifestBuilder.ChecksumOf(files) };
        }).ToList();
        Assert.Equal("download too large", ModManifestBuilder.Validate(one with { Entries = big }));
        var bigOne = big[0] with { Files = [.. ModSyncRules.AllowedFiles.Take(9).Select(name => new ModFileEntry { Path = name, Size = ModSyncRules.MaxFileBytes, Sha256 = new string('a', 64) })] };
        Assert.Equal("mod too large", ModManifestBuilder.Validate(one with { Entries = [bigOne] }));
    }

    [Fact]
    public void HotLoad_WithoutAManifest_Fails()
    {
        Assert.Equal(ModSyncCodes.Failed, Service().HotLoad(new ModSyncOutcome(null, null, [])));
    }

    [Fact]
    public void HotLoad_WhenTheHostsHashStillDiffers_ReportsAMismatch()
    {
        var manifest = ManifestOf("TestMod", "Units.json", Encoding.UTF8.GetBytes(UnitsJson)) with { RulesetHash = "ffffffffffffffff", BaseRuleset = "Civ V - Gods & Kings" };
        Assert.Equal(JoinRefusedCodes.RulesetMismatch, Service().HotLoad(new ModSyncOutcome(null, manifest, [])));
    }

    [Fact]
    public void Plan_ReportsSameDifferentAndMissing()
    {
        var entry = ModManifestBuilder.Build("TestMod", new DirectoryRulesetFolder(Path.Combine(hostMods, "TestMod")))!;
        var manifest = new ModManifest { BaseRuleset = "Civ V - Gods & Kings", Mods = ["TestMod"], RulesetHash = "abc", Entries = [entry] };
        Assert.Equal(ModSyncStatus.Missing, Service().Plan(manifest).Items[0].Status);
        WriteMod(clientMods, "TestMod", "[]");
        Assert.Equal(ModSyncStatus.Different, Service().Plan(manifest).Items[0].Status);
        WriteMod(clientMods, "TestMod", UnitsJson);
        var plan = Service().Plan(manifest);
        Assert.Equal(ModSyncStatus.Same, plan.Items[0].Status);
        Assert.Empty(plan.ToDownload);
        Assert.Equal(0, plan.TotalBytes);
    }
}
