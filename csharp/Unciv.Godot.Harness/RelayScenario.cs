using Godot;
using Unciv.Client.Net.Hosting;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Setup;
using Unciv.GodotClient.Ui;
using Unciv.Net.Identity;
using Unciv.Net.Relay;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// T371: the relay settings and the relay trust prompt, by clicking. Options, Multiplayer tab: a fresh install shows empty fields and a disabled "Reach me through the relay" switch; a malformed address or fingerprint says why and writes
/// nothing; a valid pair is saved the moment it is valid (to a temp relay.json, never the player's); a pasted address with ?fp= splits into the two fields; the switch is saved; a relay agreed to earlier is listed and Forget removes it.
/// Then a join with an invite naming a relay that is not the configured one asks first (nothing is dialled and nothing remembered on No), and Yes remembers that relay so the same invite does not ask again.
/// </summary>
public sealed class RelayScenario : Scenario
{
    private static readonly string Fingerprint = HostKey.Fingerprint([1, 2, 3, 4]);
    private static readonly string RelayPin = HostKey.Fingerprint([7, 7, 7, 7]);
    private const string Vanilla = "Civ V - Vanilla";

    public override string Name => "relay";

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear that there is no default relay and where to put the address?",
        "Does the relay prompt say what the relay's operator can see and that the answer does not change your relay setting?",
        "Is a rejected address or fingerprint explained next to the field, without losing what was typed?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Flow(host, session).Steps();

    private sealed class Flow
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "unciv-relay-scenario-" + System.Environment.ProcessId, "relay.json");
        private readonly UiTheme theme;
        private readonly OptionsScreen options;
        private readonly JoinScreen join;
        private readonly RelaySettings accepted = new();
        private readonly string invite = InviteCode.FormatRelay("relay.other.net", 47830, "K7M2-9XQ4AB", Fingerprint);
        private int connects;
        private int remembered;

        public Flow(Control host, GameSession session)
        {
            if (File.Exists(path)) File.Delete(path);
            theme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
            host.AddChild(new ColorRect { Color = new Color(0.12f, 0.14f, 0.2f), AnchorRight = 1, AnchorBottom = 1 });
            var root = new Control { AnchorRight = 1, AnchorBottom = 1 };
            host.AddChild(root);
            var service = new ClientSettingsService(new ClientSettingsStore(Path.Combine(Path.GetDirectoryName(path).NotNull(), "settings.json")));
            options = new OptionsScreen(service, theme, null, ["Retro", "Neon"], null, new RelaySettingsStore(() => RelaySettings.Load(path), settings => settings.Save(path)));
            options.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(options);
            var form = NewGameForm.FromRulesetCache();
            if (form.SetBaseRuleset(Vanilla) is { } problem) throw new InvalidOperationException(problem);
            accepted.Url = "unciv-relay://relay.example.org";
            join = new JoinScreen(theme, new JoinScreenEnvironment
            {
                Identity = ClientIdentity.Generate(),
                DisplayName = "Friend",
                RulesetHash = () => "0",
                KnownHosts = new KnownHosts(),
                PersistKnownHosts = _ => { },
                Relay = () => accepted.Endpoint,
                Connect = (_, _) =>
                {
                    connects++;
                    throw new InvalidOperationException("no host in this scenario");
                },
                RelayNeedsPrompt = relay => accepted.Choose(relay.Host, relay.Port).NeedsPrompt,
                RememberRelay = relay =>
                {
                    remembered++;
                    accepted.Remember(relay);
                },
            }) { Visible = false };
            root.AddChild(join);
        }

        private RelaySettingsEditor Editor => options.Panel.Relay.NotNull();

        private static Vector2 Centre(Func<Control?> find) => find()?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private static void Type(LineEdit field, string text)
        {
            field.Text = text;
            field.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        public IReadOnlyList<ScenarioStep> Steps() =>
            Settings().Concat(Prompt()).SelectMany(step => step is Press or Click or Act ? new[] { step, new WaitFrames(2) } : [step]).ToList();

        private ScenarioStep[] Settings() =>
        [
            new Act("open the Multiplayer tab", () => options.Panel.SelectTab(options.Panel.TabNames.ToList().IndexOf("Multiplayer"))),
            new AssertQuery("a fresh install has the tab, empty fields, the switch off limits and nothing on disk", () =>
                options.Panel.SelectedTab >= 0 && options.Panel.TabNames[options.Panel.SelectedTab] == "Multiplayer" && Editor.UrlField.Text == "" && Editor.FingerprintField.Text == "" && Editor.ReachToggle.Disabled && !File.Exists(path)),
            new Capture("settings-empty", "Options, Multiplayer tab on a fresh install: no default relay, empty address and fingerprint fields with an explanation, the Reach me through the relay switch disabled, what the operator can see."),
            new Act("a bare host name is not a relay address", () => Type(Editor.UrlField, "relay.example.org")),
            new AssertQuery("it says what an address looks like and nothing was written", () => Editor.UrlProblem.Contains("unciv-relay://", StringComparison.Ordinal) && !File.Exists(path) && Editor.UrlField.Text == "relay.example.org"),
            new Capture("settings-invalid", "A bare host name is refused next to the field with an example of a relay address; the typed text stays and nothing is saved."),
            new Act("a good address", () => Type(Editor.UrlField, "unciv-relay://relay.example.org:47831")),
            new AssertQuery("it is saved at once and the switch can be used", () => Editor.UrlProblem == "" && RelaySettings.Load(path).Url == "unciv-relay://relay.example.org:47831" && !Editor.ReachToggle.Disabled && Editor.ReachToggle.ButtonPressed),
            new Act("a fingerprint that is not one", () => Type(Editor.FingerprintField, "abc")),
            new AssertQuery("the fingerprint is explained and the saved relay is unchanged", () => Editor.FingerprintProblem.Contains("fingerprint", StringComparison.Ordinal) && RelaySettings.Load(path).Url == "unciv-relay://relay.example.org:47831"),
            new Act("the relay's fingerprint", () => Type(Editor.FingerprintField, RelayPin)),
            new AssertQuery("address and fingerprint are saved together as one relay URL", () =>
                Editor.FingerprintProblem == "" && RelaySettings.Load(path).Endpoint is { Host: "relay.example.org", Port: 47831 } relay && HostKey.Matches(relay.Fingerprint.NotNull(), RelayPin)),
            new Capture("settings-saved", "A valid address and the relay's fingerprint: both are saved as soon as they are valid, no problem text, the switch is usable."),
            new Act("paste a whole relay URL with ANOTHER fingerprint over the filled fields, without leaving the field (T380)", () => Type(Editor.UrlField, "unciv-relay://other.example.net?fp=" + BrowseScenario.StrangerKey)),
            new AssertQuery("the pasted fingerprint replaced the old one at paste time and was saved, not the old one", () =>
                Editor.UrlField.Text == "unciv-relay://other.example.net" && HostKey.Matches(Editor.FingerprintField.Text, BrowseScenario.StrangerKey) && RelaySettings.Load(path).Endpoint is { Host: "other.example.net" } other && HostKey.Matches(other.Fingerprint.NotNull(), BrowseScenario.StrangerKey)),
            new Click(() => Centre(() => Editor.ReachToggle)),
            new AssertQuery("turning the switch off is saved", () => !RelaySettings.Load(path).ReachMeThroughRelay && !RelaySettings.Load(path).HostThroughRelay),
            new Act("an invite once named another relay and the player agreed", () =>
            {
                var settings = RelaySettings.Load(path);
                settings.Remember(new RelayEndpoint("friend.example.net", 47830));
                settings.Save(path);
                Editor.Reload();
            }),
            new AssertQuery("the relay is listed with a Forget button", () => Editor.Accepted.Select(it => it.Relay).SequenceEqual(["friend.example.net:47830"])),
            new Act("scroll the list into view", () => options.Panel.Reveal(Editor.Accepted[0].Forget)),
            new WaitFrames(3),
            new Capture("settings-accepted", "The relays the player agreed to use for an invite are listed, each with Forget; the switch is off."),
            new Click(() => Centre(() => Editor.Accepted[0].Forget)),
            new AssertQuery("Forget removes it from the list and the file", () => Editor.Accepted.Count == 0 && RelaySettings.Load(path).AcceptedRelays.Count == 0),
        ];

        private ScenarioStep[] Prompt() =>
        [
            new Act("go to the join screen", () =>
            {
                options.Visible = false;
                join.Visible = true;
                join.AddressField.Text = invite;
            }),
            new Click(() => Centre(() => join.JoinButton)),
            new WaitUntil("the relay prompt opens", () => join.RelayPrompt.Length > 0),
            new AssertQuery("it names the relay and nothing was dialled or remembered", () => join.RelayPrompt.Contains("relay.other.net:47830", StringComparison.Ordinal) && connects == 0 && remembered == 0),
            new Capture("prompt", "An invite names a relay that is not yours: the prompt says which relay, what its operator can see, and that yes is remembered for that relay only; nothing has been sent."),
            new Click(() => Centre(() => join.RelayNoButton)),
            new WaitUntil("the attempt ended", () => !join.IsBusy && join.Message.Length > 0),
            new AssertQuery("No dialled nothing and remembered nothing", () => join.Message.Contains("did not agree", StringComparison.Ordinal) && connects == 0 && remembered == 0 && accepted.AcceptedRelays.Count == 0),
            new Click(() => Centre(() => join.JoinButton)),
            new WaitUntil("the prompt opens again", () => join.RelayPrompt.Length > 0),
            new Click(() => Centre(() => join.RelayYesButton)),
            new WaitUntil("the connection was tried", () => connects == 1 && !join.IsBusy),
            new AssertQuery("Yes remembered that relay only, and the relay setting is untouched", () => remembered == 1 && accepted.AcceptedRelays.SequenceEqual(["relay.other.net:47830"]) && accepted.Endpoint is { Host: "relay.example.org" }),
            new Click(() => Centre(() => join.JoinButton)),
            new WaitUntil("it connects without asking", () => connects == 2 && !join.IsBusy),
            new AssertQuery("the second join did not ask again", () => join.RelayPrompt == "" && remembered == 1),
        ];
    }
}
