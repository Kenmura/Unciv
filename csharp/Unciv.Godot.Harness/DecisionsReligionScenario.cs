using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map.MapUnits;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C052: the pickers C038 left out, through the real game screen and engine, in the religion game (G&amp;K, espionage on, a pantheon, a Great Prophet in each of two cities, a
/// World Congress vote due, a "can be promoted" notification and a spy notification). Two notification rows open pickers: the spy report the move-spies picker (idle spy
/// to a city: move-spy) and the promotion notice the warrior's promotion picker (promote-unit). The next-action button then asks for the vote (vote-diplomatic). A prophet's
/// own unit action founds the religion (the found picker: icon, name, founder and follower belief; found-religion), the second prophet enhances it (the enhance picker:
/// choose-beliefs). Every Confirm sends the engine's command, End turn stays refused until the last decision is answered and is offered after it.
/// </summary>
public sealed class DecisionsReligionScenario : Scenario
{
    public override string Name => "decisions-religion";

    public override bool Uses3D => true;

    public override int Religion => ScenarioGame.ReligionFoundEnhance;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear what each picker asks for, and what the highlighted option does?",
        "Does the found-religion picker show the icon, the name and one slot per belief the engine wants?",
        "Does a notification that opens a picker say so, and does closing the picker leave the decision pending?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var kit = new Kit(host, session);
        return [.. kit.Spies(), .. kit.Found(), .. kit.Enhance()];
    }

    private sealed class Kit
    {
        private readonly Control host;
        private readonly GameSession session;
        private readonly Unciv.Logic.GameInfo game;
        private readonly Unciv.Logic.Civilization.Civilization human;
        private readonly CityAScenario.RecordingSession recording;
        private readonly GameScreen screen;
        private readonly int foundCount;
        private readonly int enhanceCount;
        private int logged;
        private string picked = "";

        public Kit(Control host, GameSession session)
        {
            this.host = host;
            this.session = session;
            game = session.Game;
            human = game.GetCivilization(ScenarioGame.HumanA);
            recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
            screen = new GameScreen();
            host.AddChild(screen);
            screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
            foundCount = human.ReligionManager.GetBeliefsToChooseAtFounding().SumValues();
            enhanceCount = human.ReligionManager.GetBeliefsToChooseAtEnhancing().SumValues();
        }

        private static Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;

        private bool Blocking() => recording.QueryAsync(new PendingDecisionsQuery()).GetAwaiter().GetResult().NotNull().Decisions.Any(it => it.Blocking);

        private string FirstEnabled(int slot) => screen.Picker.Spec.NotNull().Slots[slot].Options.First(it => it.Enabled && screen.Picker.Selected.All(sel => sel != it.Id)).Id;

        // the engine's action titles end in the faith icon, so the unit panel's buttons are found by their first words
        private Button? ActionButton(string start) => Buttons(host).FirstOrDefault(it => it.IsVisibleInTree() && !it.Disabled && it.Text.StartsWith(start, StringComparison.Ordinal));

        private Vector2 Row(string start)
        {
            var row = screen.Notifications.Rows.First(it => it.Text.StartsWith(start, StringComparison.Ordinal));
            screen.Notifications.EnsureVisible(row.Index);
            return Centre(screen.Notifications.RowButton(row.Index));
        }

        private MapUnit Prophet(int cityIndex) => human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Great Prophet" && it.CurrentTile.GetCity() == human.Cities[cityIndex]);

        private static IEnumerable<Button> Buttons(Node node) => node.GetChildren().SelectMany(it => it is Button button ? Buttons(it).Prepend(button) : Buttons(it));

        // one Act + Click per belief slot from <first>: the first option no other slot has taken
        private ScenarioStep[] ChooseBeliefs(int count, int first) => [.. Enumerable.Range(first, count).SelectMany(slot => new ScenarioStep[]
        {
            new Act($"choose a belief for slot {slot}", () => picked = FirstEnabled(slot)),
            new Click(() => Centre(screen.Picker.OptionButton(slot, picked))),
            new WaitFrames(1),
        })];

        public ScenarioStep[] Spies() =>
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the engine asks for the spies first, a decision that blocks End turn", () =>
                Blocking() && screen.Hud.ActionText == "Move spies" && !screen.Hud.EndTurnShown),
            new Capture("start", "The religion game: the notification list holds the promotion notice and the spy report; the next-action button asks for the spies"),

            new Act("open the notification list", () => { if (!screen.Notifications.IsOpen) screen.Notifications.ToggleButton?.EmitSignal(BaseButton.SignalName.Pressed); }),
            new WaitFrames(2),
            new Act("remember the log", () => logged = session.Log.Count),
            new Click(() => Row("Your spies are waiting")),
            new WaitFrames(3),
            new AssertQuery("the spy report opened the move-spies picker without sending anything; Confirm waits for a destination, saying so", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.MoveSpies } && screen.Picker.Spec.Slots.Count == 2
                && session.Log.Count == logged && screen.Picker.ConfirmButton.Disabled && screen.Picker.ProblemText == "Choose a destination for at least one spy"),
            new AssertQuery("the popup is the shared modal frame: centred, accent hairline, titled, buttons inside with the primary right-most", () => ModalChecks.Framed(screen.Picker)),
            new Capture("spies", "The move-spies picker, opened from the spy report: one slot per spy; Move spies is disabled until a destination is chosen"),
            new Act("pick Rome's capital as the first spy's destination", () => picked = game.GetCivilization(ScenarioGame.Ai).Cities[0].Id),
            new Click(() => Centre(screen.Picker.OptionButton(0, picked))),
            new WaitFrames(2),
            new AssertQuery("a destination is chosen: Confirm is enabled", () => !screen.Picker.ConfirmButton.Disabled && screen.Picker.ProblemText == ""),
            new Click(() => Centre(screen.Picker.ConfirmButton)), // not Press: the next-action button has the same text
            new WaitFrames(3),
            new AssertQuery("one move-spy was sent for the first spy and the picker closed", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "move-spy" && session.Log[^1].Payload.GetProperty("Spy").GetString() == human.EspionageManager.SpyList[0].Name
                && !screen.Picker.Visible && !human.EspionageManager.ShouldShowMoveSpies()),

            new AssertQuery("next the World Congress vote is asked for", () => Blocking() && screen.Hud.ActionText == "Vote"),
            new Press("Vote"),
            new WaitFrames(3),
            new AssertQuery("the vote picker lists the civs this one knows and abstaining; Confirm waits for a choice", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.DiplomaticVote } && screen.Picker.ConfirmButton.Disabled
                && screen.Picker.Spec.Slots[0].Options.Count == human.DiplomacyFunctions.GetKnownCivsSorted(false).Count() + 1),
            new Act("remember the log and Rome's id", () =>
            {
                logged = session.Log.Count;
                picked = game.GetCivilization(ScenarioGame.Ai).CivId;
            }),
            new Click(() => Centre(screen.Picker.OptionButton(0, picked))),
            new WaitFrames(2),
            new Capture("vote", "The World Congress picker with Rome chosen: Vote is enabled"),
            new Click(() => Centre(screen.Picker.ConfirmButton)),
            new WaitFrames(3),
            new AssertQuery("vote-diplomatic was sent for Rome and the engine recorded the vote", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "vote-diplomatic" && session.Log[^1].Payload.GetProperty("CivId").GetString() == picked
                && !screen.Picker.Visible && game.DiplomaticVictoryVotesCast.TryGetValue(human.CivId, out var voted) && voted == picked && !human.MayVoteForDiplomaticVictory()),

            new Act("remember the log", () => logged = session.Log.Count),
            new Click(() => Row("Warrior can be promoted")),
            new WaitFrames(4),
            new AssertQuery("the promotion notice selected the warrior and opened its promotion picker without sending anything", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.Promotion } && session.Log.Count == logged
                && screen.SelectedUnitId == human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior").Id),
            new Capture("promotion", "The promotion picker, opened from the 'can be promoted' notice"),
            new Act("pick the first promotion", () => picked = screen.Picker.Spec.NotNull().Slots[0].Options.First(it => it.Enabled).Id),
            new Click(() => Centre(screen.Picker.OptionButton(0, picked))),
            new WaitFrames(2),
            new Press("Take promotion"),
            new WaitFrames(3),
            new AssertQuery("promote-unit was sent and the warrior has the promotion", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "promote-unit" && !screen.Picker.Visible
                && human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior").Promotions.Promotions.Contains(picked)),

        ];

        public ScenarioStep[] Found() =>
        [
            new Act("select the capital's prophet", () => screen.SelectUnitById(Prophet(0).Id)),
            new WaitFrames(4),
            new Click(() => Centre(ActionButton("Found a Religion"))),
            new WaitFrames(3),
            new AssertQuery("the prophet's action starts the founding; the engine asks for the religion, a decision that blocks End turn", () =>
                human.ReligionManager.ReligionState == ReligionState.FoundingReligion && Blocking() && screen.Hud.ActionText == "Found a religion" && !screen.Hud.EndTurnShown),
            new Press("Found a religion"),
            new WaitFrames(3),
            new AssertQuery("the found picker has the icon slot and one belief slot per belief the engine wants, and a name that starts as the icon", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.FoundReligion } && screen.Picker.Spec.Slots.Count == 1 + human.ReligionManager.GetBeliefsToChooseAtFounding().SumValues()
                && screen.Picker.ConfirmButton.Disabled),
            new Act("remember the log and choose the icon", () =>
            {
                logged = session.Log.Count;
                picked = FirstEnabled(0);
            }),
            new Click(() => Centre(screen.Picker.OptionButton(0, picked))),
            new WaitFrames(2),
            new AssertQuery("the name field follows the chosen icon; Confirm still waits for the beliefs", () =>
                screen.Picker.NameText == picked && screen.Picker.ConfirmButton.Disabled && screen.Picker.ProblemText.StartsWith("Choose: ", StringComparison.Ordinal)),
            .. ChooseBeliefs(foundCount, 1),
            new AssertQuery("every slot is chosen: Confirm is enabled", () => !screen.Picker.ConfirmButton.Disabled && screen.Picker.ProblemText == ""),
            new Capture("found", "The found-religion picker: an icon, its name, a founder and a follower belief; Found religion is enabled"),
            new Press("Found religion"),
            new WaitFrames(3),
            new AssertQuery("found-religion was sent with the icon and the beliefs; the religion exists", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "found-religion" && session.Log[^1].Payload.GetProperty("Beliefs").GetArrayLength() == foundCount
                && !screen.Picker.Visible && human.ReligionManager.ReligionState == ReligionState.Religion && human.ReligionManager.Religion.NotNull().IsMajorReligion()),

        ];

        public ScenarioStep[] Enhance() =>
        [
            new Act("select the second city's prophet", () => screen.SelectUnitById(Prophet(1).Id)),
            new WaitFrames(4),
            new Click(() => Centre(ActionButton("Enhance "))),
            new WaitFrames(3),
            new AssertQuery("the engine asks to enhance the religion, a decision that blocks End turn", () =>
                human.ReligionManager.ReligionState == ReligionState.EnhancingReligion && Blocking() && screen.Hud.ActionText == "Enhance religion" && !screen.Hud.EndTurnShown),
            new Press("Enhance religion"),
            new WaitFrames(3),
            new AssertQuery("the enhance picker has one slot per belief the engine wants and Confirm waits for them", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.Beliefs } && screen.Picker.TitleText == "Enhance your religion"
                && screen.Picker.Spec.Slots.Count == human.ReligionManager.GetBeliefsToChooseAtEnhancing().SumValues() && screen.Picker.ConfirmButton.Disabled),
            new Act("remember the log", () => logged = session.Log.Count),
            .. ChooseBeliefs(enhanceCount, 0),
            new Capture("enhance", "The enhance picker: an enhancer and a follower belief chosen; Choose beliefs is enabled"),
            new Press("Choose beliefs"),
            new WaitFrames(3),
            new AssertQuery("choose-beliefs was sent; the religion is enhanced, nothing blocks and Next turn is offered", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "choose-beliefs" && !screen.Picker.Visible
                && human.ReligionManager.ReligionState == ReligionState.EnhancedReligion && !Blocking() && screen.Hud.EndTurnShown),
        ];
    }
}
