using Godot;
using Unciv.Client.Net.Hosting;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Net;
using Unciv.Net.Identity;
using Unciv.Net.Relay;
using Unciv.Server.Transport;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T339: the Browse games screen, by clicking, against an in-memory directory (no sockets). Every state it can be in is shown and asserted (no relay set, loading, offline, relay too old, listing off, empty, nothing matches the
/// search, and the list); the list shows a stranger, a known host, a game that needs a password, one that needs another protocol (greyed out and not joinable), one that is full and one that uses a mod this machine lacks; search
/// and filters narrow it; Join asks first (and for the password), says plainly that a first join trusts the listed key, and hands the invite to the join flow only when confirmed.
/// </summary>
public sealed class BrowseScenario : Scenario
{
    internal const string Vanilla = "Civ V - Vanilla";
    internal static readonly string StrangerKey = HostKey.Fingerprint([1, 2, 3, 4]);
    internal static readonly string FriendKey = HostKey.Fingerprint([5, 6, 7, 8]);
    internal static readonly string FarKey = HostKey.Fingerprint([9, 9, 9, 9]);

    public override string Name => "browse";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell a game you can join from one you cannot, and why, without opening it?",
        "Does the join dialog make it clear that a first join to someone you found here trusts their key?",
        "Is each empty or error state clear about what to do next (and different from the others)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    internal static ListedGame Game(string room, string key, string name, string hostName, int players, int slots, string[]? mods = null, bool password = false, int proto = ProtocolVersion.Current, long age = 90) =>
        new(room, "00", key, new ListingInfo(1, name, hostName, players, slots, 2, Vanilla, "hash", mods ?? [], "Quick", "Continents, Small", password, "simultaneous", false, "4.18", proto), age);

    private sealed class Flow
    {
        private readonly Control root;
        private readonly BrowseScreen screen;
        private readonly List<ListedGame> games =
        [
            Game("K7M2-9XQ4AB", StrangerKey, "Friday night", "Alice", 1, 4),
            Game("22AA-33BBCC", FriendKey, "Weekly league", "Bob", 2, 6, password: true, age: 4000),
            Game("44DD-55EEFF", StrangerKey, "Modded mayhem", "Carol", 1, 4, mods: ["Fun Mod"]),
            Game("66GG-77HHJJ", FarKey, "From the future", "Dave", 1, 4, proto: ProtocolVersion.Current + 1),
            Game("88KK-99MMNN", StrangerKey, "Already full", "Erin", 4, 4),
        ];

        private Func<Task<DirectoryResult>> next = () => throw new InvalidOperationException("not set");
        private RelayEndpoint? relay;
        private int optionsAsked;
        private int fetches;
        private (BrowseRow Row, string? Password)? joined;
        private int backs;

        public Flow(Control host, GameSession session)
        {
            var theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            root = new Control { AnchorRight = 1, AnchorBottom = 1 };
            host.AddChild(root);
            var environment = new BrowseScreenEnvironment
            {
                Relay = () => relay,
                Fetch = async (_, query, _) =>
                {
                    fetches++;
                    var result = await next();
                    var kept = result.Games.Where(query.Accepts).ToList();
                    return result with { Games = kept, Dropped = result.Dropped + (result.Games.Count - kept.Count) };
                },
                Context = () => new BrowseContext(ProtocolVersion.Current, [Vanilla], [], (_, _) => "hash", fingerprint => fingerprint == FriendKey),
            };
            screen = new BrowseScreen(theme, environment);
            screen.OptionsRequested += () => optionsAsked++;
            screen.BackRequested += () => backs++;
            screen.JoinRequested += (row, password) => joined = (row, password);
            root.AddChild(screen);
        }

        private static Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private static DirectoryResult Result(IEnumerable<ListedGame> list, int dropped = 0) =>
            new(new RelayInfo("Community relay", "admin@example.org", "open", 7, list.Count()), [.. list], dropped, false, 42);

        private static Func<Task<DirectoryResult>> Fail(string code) => () => throw new RelayException(code, "x");

        private ScenarioStep[] Reload(string what, Func<Task<DirectoryResult>> reply, BrowseStatus expected) =>
        [
            new Act(what, () => next = reply),
            new Click(() => Centre(() => screen.RefreshButton)),
            new WaitUntil("the answer is shown", () => screen.Status == expected),
        ];

        public IReadOnlyList<ScenarioStep> Steps() =>
            States().Concat(Listing()).Concat(Joining()).SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();

        private ScenarioStep[] States() =>
        [
            new AssertQuery("with no relay set there is nothing to ask and a way to the setting", () => screen.Status == BrowseStatus.NoRelay && fetches == 0 && screen.OptionsButton != null && screen.StateMessage.Contains("Options", StringComparison.Ordinal)),
            new Capture("no-relay", "No relay address is set (there is no default): the screen says so and offers to open Options, where the address goes. Nothing was sent."),
            new Click(() => Centre(() => screen.OptionsButton)),
            new AssertQuery("the Options button asked to open the settings", () => optionsAsked == 1),
            new Act("a relay is set", () => relay = new RelayEndpoint("relay.example.org", 47830)),
            .. Reload("the relay cannot be reached", Fail(RelayCodes.Unreachable), BrowseStatus.Offline),
            new AssertQuery("offline says to check the address and the connection", () => screen.StateMessage.Contains("relay address", StringComparison.OrdinalIgnoreCase) && screen.Rows.Count == 0),
            new Capture("offline", "The relay could not be reached: the sentence points at the relay address and the internet connection; Refresh tries again."),
            .. Reload("the relay is older than this game", Fail(RelayCodes.TooOld), BrowseStatus.TooOld),
            new Capture("too-old", "The relay is older than this game and cannot show its list: says so and who to tell."),
            .. Reload("the relay publishes no list", Fail(RelayCodes.ListingOff), BrowseStatus.ListingOff),
            new AssertQuery("listing-off is told apart from offline", () => screen.StateMessage.Contains("does not publish", StringComparison.Ordinal)),
            .. Reload("the relay answers nonsense", Fail(RelayCodes.Failed), BrowseStatus.Failed),
            new Capture("error", "The relay answered something that is not its protocol: a plain error sentence, no list."),
            .. Reload("nobody lists a game", () => Task.FromResult(Result([])), BrowseStatus.Empty),
            new AssertQuery("the empty list names the relay and how to list one", () => screen.StateMessage.Contains("List this game publicly", StringComparison.Ordinal) && screen.RelayHeader.Contains("Community relay", StringComparison.Ordinal)),
            new Capture("empty", "The relay is fine but nobody lists a game: says so and how to list one (Host screen option)."),
        ];

        private ScenarioStep[] Listing() =>
        [
            .. Reload("five games are listed", () => Task.FromResult(Result(games, dropped: 1)), BrowseStatus.Ready),
            new AssertQuery("all five rows, with the relay line telling the name, contact, count, ping and one hidden entry", () =>
                screen.Rows.Count == 5 && screen.RelayHeader.Contains("Community relay", StringComparison.Ordinal) && screen.RelayHeader.Contains("admin@example.org", StringComparison.Ordinal) && screen.RelayHeader.Contains("ping 42 ms", StringComparison.Ordinal) && screen.RelayHeader.Contains("1 hidden", StringComparison.Ordinal)),
            new AssertQuery("the future-protocol and the full game are greyed out, the rest are joinable", () =>
                screen.Rows.Select(it => it.CanJoin).SequenceEqual([true, true, true, false, false]) && screen.RowButton(3).NotNull().Disabled && screen.RowButton(4).NotNull().Disabled && !screen.RowButton(0).NotNull().Disabled),
            new AssertQuery("nothing is selected, so Join is off", () => screen.SelectedIndex == -1 && screen.JoinButton.Disabled),
            new Capture("list", "Five listed games: name, host, rules, speed, password flag and players/slots; the game on another protocol and the full one are greyed out with the reason; Join is off until one is picked."),
            new Click(() => Centre(() => screen.RowButton(2))),
            new AssertQuery("picking the modded game shows its facts, the stranger's key and the missing-mod note, and enables Join", () =>
                screen.SelectedIndex == 2 && screen.Detail.Contains("Fun Mod", StringComparison.Ordinal) && screen.Detail.Contains(BrowseModel.FirstTime, StringComparison.Ordinal) && screen.Detail.Contains("as reported by the host", StringComparison.Ordinal) && !screen.JoinButton.Disabled),
            new Capture("selected-mod", "A game with a mod this machine lacks: the details name the mod, say joining offers to download it, and show the host key as 'first time you see this host'."),
            new Click(() => Centre(() => screen.RowButton(1))),
            new AssertQuery("a known host is told apart, and the password flag is shown", () => screen.Detail.Contains("Known host", StringComparison.Ordinal) && screen.Detail.Contains("Password: required", StringComparison.Ordinal)),
            new Act("search for a name", () => screen.SearchField.Text = "league"),
            new Click(() => Centre(() => screen.RefreshButton)),
            new WaitUntil("the search answered", () => screen.Rows.Count == 1),
            new AssertQuery("only the matching game is left and it is still joinable", () => screen.Rows[0].Title == "Weekly league" && screen.Status == BrowseStatus.Ready),
            new Act("search for nothing that exists", () => screen.SearchField.Text = "zzzz"),
            new Click(() => Centre(() => screen.RefreshButton)),
            new WaitUntil("nothing matches", () => screen.Status == BrowseStatus.NoMatch),
            new Capture("no-match", "The search matches nothing: told apart from an empty relay, with the advice to clear the search and filters."),
            new Act("clear the search; only games with a free slot", () =>
            {
                screen.SearchField.Text = "";
                screen.FreeSlotsCheck.ButtonPressed = true;
            }),
            new WaitUntil("the full game is gone", () => screen.Status == BrowseStatus.Ready && screen.Rows.All(it => it.Game.Info.Players < it.Game.Info.Slots)),
            new AssertQuery("four games left", () => screen.Rows.Count == 4),
            new Act("untick free slots", () => screen.FreeSlotsCheck.ButtonPressed = false),
            new Click(() => Centre(() => screen.PasswordFilter.ButtonFor("Open"))),
            new WaitUntil("password games are gone", () => screen.Status == BrowseStatus.Ready && screen.Rows.All(it => !it.Game.Info.PasswordRequired)),
            new AssertQuery("four games left", () => screen.Rows.Count == 4),
            new Click(() => Centre(() => screen.PasswordFilter.ButtonFor("Any"))),
        ];

        private ScenarioStep[] Joining() =>
        [
            .. Reload("all games again", () => Task.FromResult(Result(games)), BrowseStatus.Ready),
            new Click(() => Centre(() => screen.RowButton(0))),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the dialog opens", () => screen.Dialog != null),
            new AssertQuery("it names the game, says a first join trusts the key, and asks for no password", () =>
                screen.Dialog.NotNull().PromptText.Contains("Friday night", StringComparison.Ordinal) && screen.Dialog.PromptText.Contains("trusts that key", StringComparison.Ordinal) && !screen.Dialog.PasswordField.Visible),
            new Capture("join-stranger", "Joining a game by a host you have not met: the dialog says the name and key come from whoever listed it, that joining trusts the key from now on, and to ask the host to confirm it."),
            new Click(() => Centre(() => screen.Dialog?.CancelButton)),
            new AssertQuery("cancelling sent nothing", () => screen.Dialog == null && joined == null),
            new Click(() => Centre(() => screen.RowButton(1))),
            new Click(() => Centre(() => screen.JoinButton)),
            new WaitUntil("the dialog opens for the password game", () => screen.Dialog != null),
            new AssertQuery("a known host is said to be known and the password is asked for", () => screen.Dialog.NotNull().PromptText.Contains("Known host", StringComparison.Ordinal) && !screen.Dialog.PromptText.Contains("trusts that key", StringComparison.Ordinal) && screen.Dialog.PasswordField.Visible),
            new Click(() => Centre(() => screen.Dialog?.ConfirmButton)),
            new AssertQuery("an empty password is refused in the dialog and nothing is sent", () => screen.Dialog != null && screen.Dialog.ErrorText.Contains("password", StringComparison.OrdinalIgnoreCase) && joined == null),
            new Capture("join-password", "A password game from a known host: the dialog asks for the password and refuses an empty one without sending anything."),
            new Act("type the password", () => screen.Dialog.NotNull().PasswordField.Text = "swordfish"),
            new Click(() => Centre(() => screen.Dialog?.ConfirmButton)),
            new AssertQuery("the join was handed on with the row and the password", () => screen.Dialog == null && joined is { Password: "swordfish" } j && j.Row.Game.Room == "22AA-33BBCC"),
            new Click(() => Centre(() => screen.BackButton)),
            new AssertQuery("Back asked to leave", () => backs == 1),
        ];
    }
}
