using Unciv.Client.Net.Hosting;
using Unciv.GodotClient.Setup;
using Unciv.Harness;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Lobby;
using Unciv.Net.Relay;

namespace Unciv.Tests.Godot;

// T339: the Browse games screen's Godot-free parts (rows, states, join prompt, the host's listing, the Host form flag).
[Collection("RulesetCache")]
public sealed class BrowseModelTests
{
    private const string Vanilla = "Civ V - Vanilla";
    private static readonly string Fingerprint = HostKey.Fingerprint([9, 8, 7, 6]);

    private static ListedGame Game(int players = 1, int slots = 4, string ruleset = Vanilla, string[]? mods = null, bool password = false, bool started = false, int proto = ProtocolVersion.Current, string hash = "h", long age = 30) =>
        new("K7M2-9XQ4AB", "ab", Fingerprint, new ListingInfo(1, "Friendly game", "Alice", players, slots, 2, ruleset, hash, mods ?? [], "Quick", "Continents, Small", password, "simultaneous", started, "1.0", proto), age);

    private static BrowseContext Context(bool known = false, string? localHash = null, string[]? mods = null) =>
        new(ProtocolVersion.Current, [Vanilla], mods ?? ["Mod A"], (_, _) => localHash, _ => known);

    [Fact]
    public void AnOrdinaryGameCanBeJoinedAndShowsItsFacts()
    {
        var row = BrowseModel.Describe(Game(), Context());
        Assert.True(row.CanJoin);
        Assert.Equal("Friendly game", row.Title);
        Assert.Equal("1/4", row.Trailing);
        Assert.Contains("Alice", row.Subtitle, StringComparison.Ordinal);
        Assert.Contains("Quick", row.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("password", row.Subtitle, StringComparison.Ordinal);
        Assert.Contains(BrowseModel.FirstTime, row.Detail, StringComparison.Ordinal);
        Assert.Contains("as reported by the host", row.Detail, StringComparison.Ordinal);
        Assert.False(row.KnownHost);
        Assert.Null(row.Note);
    }

    [Fact]
    public void AKnownHostAndAPasswordAreSaid()
    {
        var row = BrowseModel.Describe(Game(password: true), Context(known: true));
        Assert.True(row.KnownHost);
        Assert.Contains("password", row.Subtitle, StringComparison.Ordinal);
        Assert.Contains(BrowseModel.KnownHost, row.Detail, StringComparison.Ordinal);
        Assert.Contains("needs a password", BrowseModel.JoinPrompt(row), StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherProtocolIsGreyedOut()
    {
        var row = BrowseModel.Describe(Game(proto: ProtocolVersion.Current + 1), Context());
        Assert.False(row.CanJoin);
        Assert.Contains("another game version", row.DisabledReason, StringComparison.Ordinal);
        Assert.Contains("Cannot join", row.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingBaseRulesetAndAFullGameAreGreyedOutButAFullStartedGameIsNot()
    {
        Assert.Contains("ruleset", BrowseModel.Describe(Game(ruleset: "Custom base"), Context()).DisabledReason, StringComparison.Ordinal);
        Assert.Contains("full", BrowseModel.Describe(Game(players: 4, slots: 4), Context()).DisabledReason, StringComparison.Ordinal);
        Assert.True(BrowseModel.Describe(Game(players: 4, slots: 4, started: true), Context()).CanJoin);
    }

    [Fact]
    public void MissingModsAreANoteNotARefusal()
    {
        var row = BrowseModel.Describe(Game(mods: ["Mod B", "Mod A"]), Context());
        Assert.True(row.CanJoin);
        Assert.True(row.MissingMods);
        Assert.Contains("Mod B", row.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("Mod A,", row.Note, StringComparison.Ordinal);
        Assert.Contains("2 mods", row.Subtitle, StringComparison.Ordinal);
        Assert.Contains("1 mod", BrowseModel.Describe(Game(mods: ["Mod B"]), Context()).Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void ADifferingLocalRulesetHashIsANoteAndAMatchIsNot()
    {
        Assert.Contains("differs", BrowseModel.Describe(Game(mods: ["Mod A"], hash: "theirs"), Context(localHash: "mine")).Note, StringComparison.Ordinal);
        Assert.Null(BrowseModel.Describe(Game(mods: ["Mod A"], hash: "same"), Context(localHash: "same")).Note);
    }

    [Theory]
    [InlineData(1, 4, false, "1/4")]
    [InlineData(0, 0, false, "0")]
    [InlineData(0, 0, true, "started")]
    public void PlayersShortReadsLikeTheList(int players, int slots, bool started, string expected) =>
        Assert.Equal(expected, BrowseModel.PlayersShort(Game(players, slots, started: started).Info));

    [Theory]
    [InlineData(5, "under a minute")]
    [InlineData(120, "2 min")]
    [InlineData(3720, "1 h 2 min")]
    public void AgeTextUsesTheLargestUnit(long seconds, string expected) => Assert.Equal(expected, BrowseModel.AgeText(seconds));

    [Fact]
    public void TheJoinPromptSaysTofuOnlyForAStranger()
    {
        var stranger = BrowseModel.JoinPrompt(BrowseModel.Describe(Game(), Context()));
        Assert.Contains("trusts that key from now on", stranger, StringComparison.Ordinal);
        Assert.Contains("anyone can list a game", stranger, StringComparison.Ordinal);
        var friend = BrowseModel.JoinPrompt(BrowseModel.Describe(Game(), Context(known: true)));
        Assert.DoesNotContain("trusts that key", friend, StringComparison.Ordinal);
        Assert.Contains("Known host", friend, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStateHasItsOwnSentence()
    {
        var shown = Enum.GetValues<BrowseStatus>().Where(it => it != BrowseStatus.Ready).Select(it => BrowseModel.StateText(it)).ToList();
        Assert.Equal(shown.Count, shown.Distinct().Count());
        Assert.Equal("", BrowseModel.StateText(BrowseStatus.Ready));
        Assert.Equal("custom", BrowseModel.StateText(BrowseStatus.Failed, "custom"));
        Assert.Contains("Options", BrowseModel.StateText(BrowseStatus.NoRelay), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("relay-listing-off", BrowseStatus.ListingOff)]
    [InlineData("relay-too-old", BrowseStatus.TooOld)]
    [InlineData("relay-rate-limited", BrowseStatus.RateLimited)]
    [InlineData("relay-unreachable", BrowseStatus.Offline)]
    [InlineData("relay-certificate", BrowseStatus.Offline)]
    [InlineData("relay-no-answer", BrowseStatus.Offline)]
    [InlineData("relay-failed", BrowseStatus.Failed)]
    [InlineData("something-else", BrowseStatus.Failed)]
    public void RelayCodesMapToStates(string code, BrowseStatus expected) => Assert.Equal(expected, BrowseModel.FromRelayCode(code).Status);

    [Fact]
    public void TheRelayLineNamesTheRelayAndItsContact()
    {
        Assert.Equal("Home relay - me@example.org - 1 game listed - ping 12 ms", BrowseModel.RelayLine("Home relay", "me@example.org", 1, 12));
        Assert.Equal("R - 3 games listed - ping 0 ms", BrowseModel.RelayLine("R", "", 3, 0));
    }

    [Fact]
    public void TheListingHelpExplainsWhyItIsOffAndWhatIsShared()
    {
        Assert.Contains("set a relay address", BrowseModel.ListingHelp(false, false), StringComparison.Ordinal);
        Assert.Contains("Reach me through a relay", BrowseModel.ListingHelp(true, false), StringComparison.Ordinal);
        var on = BrowseModel.ListingHelp(true, true);
        Assert.Contains("never your address or password", on, StringComparison.Ordinal);
        Assert.Contains("Off by default", on, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInviteIsTheRelayInviteWithTheListedKey()
    {
        var invite = BrowseModel.InviteFor(Game(), new RelayEndpoint("relay.example.org", 47830));
        Assert.Contains("relay.example.org", invite, StringComparison.Ordinal);
        Assert.Contains(Fingerprint, invite, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLocalContextKnowsInstalledRulesetsAndKnownKeys()
    {
        ScenarioGame.LoadVanilla();
        var known = new KnownHosts();
        Assert.True(known.Pin("somewhere:1", Fingerprint));
        var context = BrowseModel.ContextFor(NewGameForm.FromRulesetCache(), known);
        Assert.Contains(Vanilla, context.BaseRulesets);
        Assert.True(context.IsKnownFingerprint(Fingerprint));
        Assert.False(context.IsKnownFingerprint(HostKey.Fingerprint([1, 1, 1, 1])));
        Assert.NotNull(context.LocalRulesetHash(Vanilla, []));
        Assert.Null(context.LocalRulesetHash("No such ruleset", []));
        Assert.Null(context.LocalRulesetHash(Vanilla, ["No such mod"]));
    }

    // ---- what the host announces

    private static LobbySlot Slot(int index, string kind) => new() { Slot = index, Kind = kind, Ready = true };

    [Fact]
    public void TheHostsListingCountsHumansOpenAndAiSlotsAndCarriesNoSecrets()
    {
        ScenarioGame.LoadVanilla();
        var settings = new LobbySettings { GameName = "  My  game ", Ruleset = Vanilla, Password = "secret", Mods = ["Mod A"], TurnMode = "Simultaneous" };
        var listing = ListingFactory.From(settings, "Al‮ice", [Slot(0, "Human"), Slot(1, "Human"), Slot(2, "Open"), Slot(3, "AI")]);
        Assert.NotNull(listing);
        Assert.Equal(2, listing.Players);
        Assert.Equal(3, listing.Slots);
        Assert.Equal(1, listing.Ai);
        Assert.True(listing.PasswordRequired);
        Assert.Equal("Alice", listing.HostName);
        Assert.Equal("simultaneous", listing.TurnMode);
        Assert.False(listing.Started);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(listing), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("‮‮‮")]
    public void AGameNameThatCannotBeListedGivesNoListing(string name) =>
        Assert.Null(ListingFactory.From(new LobbySettings { GameName = name }, "Host", [Slot(0, "Human")]));

    [Fact]
    public void AnEmptyHostNameFallsBackAndTooManyModsAreCut()
    {
        ScenarioGame.LoadVanilla();
        var settings = new LobbySettings { GameName = "Fine name", Mods = [.. Enumerable.Range(0, 30).Select(i => "Mod " + i)] };
        var listing = ListingFactory.From(settings, "‮", [Slot(0, "Human")]);
        Assert.NotNull(listing);
        Assert.Equal("Host", listing.HostName);
        Assert.Equal(16, listing.Mods.Count);
    }

    [Fact]
    public void ListingPubliclyIsOffByDefaultAndNeedsAListableName()
    {
        ScenarioGame.LoadVanilla();
        var form = new HostSettingsForm(NewGameForm.FromRulesetCache());
        Assert.False(form.ListPublicly);
        form.GameName = "ab";
        Assert.DoesNotContain(form.Problems(), it => it.Contains("publicly listed", StringComparison.Ordinal));
        form.ListPublicly = true;
        Assert.Contains(form.Problems(), it => it.Contains("publicly listed", StringComparison.Ordinal));
        form.GameName = "A proper name";
        Assert.DoesNotContain(form.Problems(), it => it.Contains("publicly listed", StringComparison.Ordinal));
    }
}
