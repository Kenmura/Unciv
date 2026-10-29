using System.Net;
using Unciv.Client.Net.Hosting;
using Unciv.Client.Net.Session;
using Unciv.Client.Net.Transport;
using Unciv.GodotClient.Setup;
using Unciv.Harness;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.Net.Identity;
using Unciv.Net.Persistence;
using Unciv.Server;
using Unciv.Server.Transport;

namespace Unciv.Tests.Godot;

// C034: the host screen's Godot-free parts: the settings form, the address list, saved hosted games and the hosting session (lobby, TCP friends, resume).
[Collection("RulesetCache")]
public sealed class HostScreenTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("unciv-host-screen-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { }
    }

    private static HostSettingsForm Form()
    {
        ScenarioGame.LoadVanilla();
        return new HostSettingsForm(NewGameForm.FromRulesetCache());
    }

    // ---- settings form

    [Fact]
    public void Build_MapsEveryFieldToLobbySettings()
    {
        var form = Form();
        Assert.Null(form.Game.SetBaseRuleset("Civ V - Vanilla"));
        form.Game.Difficulty = "King";
        form.Game.Speed = "Epic";
        form.Game.Era = "Classical era";
        form.Game.Size = "Tiny";
        form.Game.SeedText = "7";
        Assert.Null(form.Game.SetPlayerCount(3));
        Assert.Null(form.Game.SetCityStates(2));
        form.Game.NoBarbarians = true;
        form.GameName = "  Friday night  ";
        form.AiFill = false;
        form.TurnTimerSeconds = 300;
        form.Password = "hunter2";

        var result = form.Build(clockMillis: 5);

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        var s = result.Settings!;
        Assert.Equal("Friday night", s.GameName);
        Assert.Equal("Civ V - Vanilla", s.Ruleset);
        Assert.Equal(("King", "Epic", "Classical era"), (s.Difficulty, s.Speed, s.Era));
        Assert.Equal(3, s.MaxMajorCivs);
        Assert.Equal(2, s.CityStateCount);
        Assert.True(s.NoBarbarians);
        Assert.False(s.AiFill);
        Assert.Equal(300, s.TurnTimerSeconds);
        Assert.Equal("hunter2", s.Password);
        Assert.Equal(7, s.Map.Seed);
        Assert.Equal("Tiny", s.Map.MapSize.Name);
        Assert.Equal(form.Game.Victories.Order(StringComparer.Ordinal), s.VictoryTypes.Order(StringComparer.Ordinal));
        Assert.False(s.ToGameSetupInfo().GameParameters.GodMode);
    }

    [Fact]
    public void Build_EmptyPasswordIsAnOpenGameAndBlankSeedTakesTheClock()
    {
        var form = Form();
        var s = form.Build(clockMillis: 99).Settings!;
        Assert.Null(s.Password);
        Assert.Equal(99, s.Map.Seed);
    }

    [Fact]
    public void Problems_NameAndPortAndGameProblemsAreRefused()
    {
        var form = Form();
        form.GameName = "  ";
        form.Port = 70000;
        form.Game.SeedText = "abc";
        var errors = form.Build(1).Errors;
        Assert.Contains("The game needs a name.", errors);
        Assert.Contains("The port must be a number from 1 to 65535.", errors);
        Assert.Contains(errors, it => it.StartsWith("The seed must be", StringComparison.Ordinal));
        Assert.Null(form.Build(1).Settings);
    }

    [Theory]
    [InlineData("47821", true, 47821)]
    [InlineData(" 80 ", true, 80)]
    [InlineData("65535", true, 65535)]
    [InlineData("0", false, 47821)]
    [InlineData("65536", false, 47821)]
    [InlineData("-5", false, 47821)]
    [InlineData("abc", false, 47821)]
    [InlineData("", false, 47821)]
    [InlineData(null, false, 47821)]
    public void TryParsePort(string? text, bool ok, int expected)
    {
        Assert.Equal(ok, HostSettingsForm.TryParsePort(text, out int port, out string? problem));
        Assert.Equal(expected, port);
        Assert.Equal(!ok, problem != null);
    }

    [Fact]
    public void TimerLabels()
    {
        Assert.Equal(["No timer", "1 min", "2 min", "5 min", "10 min"], HostSettingsForm.TimerChoices.Select(HostSettingsForm.TimerLabel));
        Assert.Equal("45 s", HostSettingsForm.TimerLabel(45));
    }

    // ---- addresses

    [Theory]
    [InlineData("192.168.1.20", AddressScope.Lan)]
    [InlineData("10.0.0.5", AddressScope.Lan)]
    [InlineData("172.16.0.1", AddressScope.Lan)]
    [InlineData("172.31.255.1", AddressScope.Lan)]
    [InlineData("100.64.0.9", AddressScope.Lan)]
    [InlineData("172.32.0.1", AddressScope.Public)]
    [InlineData("8.8.8.8", AddressScope.Public)]
    [InlineData("127.0.0.1", null)]
    [InlineData("169.254.3.4", null)]
    [InlineData("0.0.0.0", null)]
    [InlineData("224.0.0.1", null)]
    [InlineData("fe80::1", null)]
    [InlineData("2001:db8::1", null)]
    public void Classify(string address, AddressScope? expected) => Assert.Equal(expected, LocalAddresses.Classify(IPAddress.Parse(address)));

    [Fact]
    public void Enumerate_ListsOnlyUsableAddressesLanFirst()
    {
        var all = LocalAddresses.Enumerate();
        Assert.All(all, it => Assert.NotNull(LocalAddresses.Classify(IPAddress.Parse(it.Address))));
        var scopes = all.Select(it => it.Scope).ToList();
        Assert.Equal(scopes.OrderBy(it => it), scopes);
    }

    // ---- saved hosted games

    private static (GameInfo Game, GameSetupInfo Setup) NewGame()
    {
        var setup = new GameSetupInfo(
            new GameParameters
            {
                BaseRuleset = "Civ V - Vanilla",
                NumberOfCityStates = 0,
                VictoryTypes = ["Domination"],
                Players = [new("Babylon", PlayerType.Human), new("Greece", PlayerType.Human), new("Rome", PlayerType.AI)],
            },
            new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny });
        var game = GameStarter.StartNewGame(setup, 11, 1000);
        game.StartSimultaneousTurns();
        return (game, setup);
    }

    private async Task<string> SaveHostedGameAsync(string name, ClientIdentity host, ClientIdentity friend)
    {
        ScenarioGame.LoadVanilla();
        var (game, setup) = NewGame();
        var directory = Path.Combine(root, name);
        var bindings = new Dictionary<string, SlotBinding>
        {
            ["Babylon"] = new() { PlayerKey = host.PlayerKey, DisplayName = "Host" },
            ["Greece"] = new() { PlayerKey = friend.PlayerKey, DisplayName = "Friend" },
        };
        await using var started = InProcessHost.Start(game, setup, 11, 1000, directory, "Babylon", bindings, new InProcessHostOptions { TickInterval = null });
        await started.StopAsync();
        return directory;
    }

    [Fact]
    public void List_MissingRootAndForeignFoldersAreNoGames()
    {
        Assert.Empty(HostedGames.List(Path.Combine(root, "nope"), "k"));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        Directory.CreateDirectory(Path.Combine(root, "broken"));
        File.WriteAllText(Path.Combine(root, "broken", "game.json"), "{ not json");
        Assert.Empty(HostedGames.List(root, "k"));
    }

    [Fact]
    public async Task List_FindsSavedGamesAndWhetherThisPlayerHostsThem()
    {
        using var host = ClientIdentity.Generate();
        using var friend = ClientIdentity.Generate();
        var directory = await SaveHostedGameAsync("one", host, friend);

        var mine = Assert.Single(HostedGames.List(root, host.PlayerKey));
        Assert.Equal(directory, mine.Directory);
        Assert.Equal(["Host", "Friend"], mine.Players);
        Assert.True(mine.HostBound);
        Assert.False(Assert.Single(HostedGames.List(root, "somebody else")).HostBound);
    }

    [Fact]
    public async Task Resume_ReopensTheGameAndListensAndInviteIsValid()
    {
        using var host = ClientIdentity.Generate();
        using var friend = ClientIdentity.Generate();
        var directory = await SaveHostedGameAsync("two", host, friend);
        using var cert = HostCertificate.Generate();

        await using var session = await HostingSession.ResumeAsync(directory, host, cert, 0);

        Assert.NotNull(session.Resumed);
        Assert.Equal(directory, session.Directory);
        Assert.True(session.Port > 0);
        Assert.Equal(HostCertificate.GetFingerprint(cert), session.Fingerprint);
        Assert.True(InviteCode.TryParse(session.Invite("192.168.1.5"), out var invite));
        Assert.Equal(("192.168.1.5", session.Port, session.Fingerprint), (invite!.Host, invite.Port, invite.Fingerprint));
    }

    [Fact]
    public async Task Resume_AKeyThatOwnsNoCivIsRefusedWithAMessage()
    {
        using var host = ClientIdentity.Generate();
        using var friend = ClientIdentity.Generate();
        using var stranger = ClientIdentity.Generate();
        var directory = await SaveHostedGameAsync("three", host, friend);
        using var cert = HostCertificate.Generate();

        var e = await Assert.ThrowsAsync<HostingException>(() => HostingSession.ResumeAsync(directory, stranger, cert, 0));
        Assert.StartsWith("This saved game cannot be hosted", e.Message, StringComparison.Ordinal);
    }

    // ---- new lobby

    [Fact]
    public async Task StartLobby_SeatsTheHostInSlotOneAndAFriendOverTcpTakesSlotTwo()
    {
        var form = Form();
        form.Game.SetBaseRuleset("Civ V - Vanilla");
        var settings = form.Build(1).Settings!;
        using var hostIdentity = ClientIdentity.Generate();
        using var friendIdentity = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();

        await using var session = await HostingSession.StartLobbyAsync(settings, hostIdentity, cert, "Host", 0);

        Assert.Equal(settings.MaxMajorCivs, session.Slots.Count);
        Assert.Equal(("Host", "Human"), (session.Slots[0].PlayerName, session.Slots[0].Kind));
        Assert.Equal(hostIdentity.PlayerKey, session.Slots[0].PlayerKeyId);
        Assert.All(session.Slots.Skip(1), it => Assert.Equal("Open", it.Kind));

        Assert.True(InviteCode.TryParse(session.Invite("127.0.0.1"), out var invite));
        await using var friend = new ClientSession(token => TcpTlsClientTransport.ConnectAsync(invite!, cancellationToken: token), new ClientSessionOptions
        {
            Identity = friendIdentity,
            DisplayName = "Friend",
            RulesetHash = settings.ComputeRulesetHash(),
            ExpectedFingerprint = invite!.Fingerprint,
            MaxReconnectAttempts = 0,
        });
        Assert.True((await friend.JoinAsync()).Joined);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (session.Slots[1].PlayerName != "Friend") await Task.Delay(20, timeout.Token);
        Assert.Equal(("Host", "Friend"), (session.Slots[0].PlayerName, session.Slots[1].PlayerName));
    }

    [Fact]
    public async Task StartLobby_APortInUseIsRefusedWithAMessageAndNothingStays()
    {
        var form = Form();
        var settings = form.Build(1).Settings!;
        using var hostIdentity = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();
        await using var first = await HostingSession.StartLobbyAsync(settings, hostIdentity, cert, "Host", 0);

        var e = await Assert.ThrowsAsync<HostingException>(() => HostingSession.StartLobbyAsync(settings, hostIdentity, cert, "Host", first.Port));
        Assert.Contains("cannot be used", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartLobby_PasswordGameStillSeatsTheHost()
    {
        var form = Form();
        form.Password = "secret";
        var settings = form.Build(1).Settings!;
        using var hostIdentity = ClientIdentity.Generate();
        using var cert = HostCertificate.Generate();
        await using var session = await HostingSession.StartLobbyAsync(settings, hostIdentity, cert, "Host", 0);
        Assert.Equal("Host", session.Slots[0].PlayerName);
    }

    // ---- files

    [Fact]
    public void HostFiles_IdentityAndCertificateSurviveARestart()
    {
        var identityPath = Path.Combine(root, "sub", "identity.key");
        var pfxPath = Path.Combine(root, "sub", "host-key.pfx");
        using var first = HostFiles.LoadIdentity(identityPath);
        using var second = HostFiles.LoadIdentity(identityPath);
        Assert.Equal(first.PlayerKey, second.PlayerKey);
        using var cert1 = HostFiles.LoadCertificate(pfxPath);
        using var cert2 = HostFiles.LoadCertificate(pfxPath);
        Assert.Equal(HostCertificate.GetFingerprint(cert1), HostCertificate.GetFingerprint(cert2));
        Assert.Null(new FileIdentityKeyStore(Path.Combine(root, "none.key")).Load());
    }
}
