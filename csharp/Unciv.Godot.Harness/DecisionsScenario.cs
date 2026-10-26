using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C038: the decision pickers through the real game screen and the real engine, in the decisions game (a capital, a free great person, a free technology, enough faith
/// for a pantheon, a warrior with experience for a promotion). End turn is refused while a blocking decision is pending, so each one is answered in the order the
/// engine lists them through the next-action button: the free great person picker (options are the engine's, Confirm sends choose-free-great-person), the tech screen in
/// free mode (pick-free-technology), the pantheon picker (found-pantheon), after which the button offers Next turn again. The promotion picker is opened from the unit
/// panel's Promote button and sends promote-unit. Every Confirm stays disabled and says why until the player has chosen.
/// </summary>
public sealed class DecisionsScenario : Scenario
{
    public override string Name => "decisions";

    public override bool Uses3D => true;

    public override bool Decisions => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear what each picker asks for, and what the highlighted option does?",
        "Does the Confirm button say why it is disabled until a choice is made?",
        "Can you leave a picker with Decide later without losing the decision?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        int logged = 0;
        string person = "";
        string tech = "";
        string belief = "";

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        bool Blocking() => recording.QueryAsync(new PendingDecisionsQuery()).GetAwaiter().GetResult().NotNull().Decisions.Any(it => it.Blocking);
        string FirstOption() => screen.Picker.Spec?.Slots[0].Options.FirstOrDefault(it => it.Enabled)?.Id ?? "";

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the engine asks for a free great person first, a decision that blocks End turn", () =>
                Blocking() && screen.Hud.ActionText == "Choose a free great person" && !screen.Hud.EndTurnShown),
            new Press("Choose a free great person"),
            new WaitFrames(2),
            new AssertQuery("the picker lists the engine's options and Confirm waits for a choice, saying so", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.FreeGreatPerson } && screen.Picker.ConfirmButton.Disabled
                && screen.Picker.ProblemText == "Choose: Great person" && screen.Picker.Spec.Slots[0].Options.Count == human.GreatPeople.GetFreeGreatPersonOptions().Count()),
            new Capture("great-person", "The free great person picker: one button per great person the engine offers; Confirm is disabled and says to choose first"),
            new Act("remember the log and the first option", () =>
            {
                logged = session.Log.Count;
                person = screen.Picker.Spec.NotNull().Slots[0].Options.First(it => it.Id != "Great Admiral").Id; // a great admiral needs a coast, and choosing one the engine cannot place changes nothing
            }),
            new Click(() => Centre(screen.Picker.OptionButton(0, person))),
            new WaitFrames(2),
            new AssertQuery("choosing only highlights: nothing sent, Confirm is enabled", () => session.Log.Count == logged && !screen.Picker.ConfirmButton.Disabled && screen.Picker.ProblemText == ""),
            new Capture("great-person-chosen", "A great person chosen: its description shows and Confirm is enabled"),
            new Press("Choose"),
            new WaitFrames(3),
            new AssertQuery("Confirm sent one choose-free-great-person with that unit; the picker closed and no free great person is left", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "choose-free-great-person" && session.Log[^1].Payload.GetProperty("Unit").GetString() == person
                && !screen.Picker.Visible && human.GreatPeople.FreeGreatPeople == 0),

            new AssertQuery("next the engine asks for the free technology, and the button opens the tech screen", () => screen.Hud.ActionText == "Pick a technology"),
            new Press("Pick a technology"),
            new WaitFrames(3),
            new AssertQuery("the tech screen is open in free mode", () => screen.IsTechVisible && human.Tech.FreeTechs == 1),
            new Act("choose the first technology the engine lets us research", () =>
            {
                tech = game.Ruleset.Technologies.Keys.Where(it => human.Tech.CanBeResearched(it) && !human.Tech.IsResearched(it)).Order(StringComparer.Ordinal).First();
                logged = session.Log.Count;
                screen.Tech.EnsureVisible(tech);
            }),
            new WaitFrames(2),
            new Click(() => Centre(screen.Tech.NodeButton(tech))),
            new WaitFrames(1),
            new Click(() => Centre(screen.Tech.ActionButton("TakeFree"))),
            new WaitFrames(3),
            new AssertQuery("the free technology was taken with pick-free-technology", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "pick-free-technology" && human.Tech.IsResearched(tech) && human.Tech.FreeTechs == 0),

            new Act("close the tech screen", () => screen.Tech.Close()),
            new WaitFrames(2),
            new AssertQuery("then the pantheon", () => screen.Hud.ActionText == "Choose a pantheon"),
            new Press("Choose a pantheon"),
            new WaitFrames(3),
            new AssertQuery("the pantheon picker lists unowned pantheon beliefs from the engine", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.Pantheon } && screen.Picker.Spec.Slots[0].Options.Count > 3 && screen.Picker.ConfirmButton.Disabled),
            new Act("remember the log and the first belief", () =>
            {
                logged = session.Log.Count;
                belief = FirstOption();
            }),
            new Click(() => Centre(screen.Picker.OptionButton(0, belief))),
            new WaitFrames(2),
            new AssertQuery("the belief's effects are shown", () => screen.Picker.DetailText.Length > 0 && !screen.Picker.ConfirmButton.Disabled),
            new Capture("pantheon", "The pantheon picker with a belief chosen: its effects below the list and Found pantheon enabled"),
            new Press("Found pantheon"),
            new WaitFrames(3),
            new AssertQuery("found-pantheon sent with that belief; no blocking decision is left and Next turn is offered", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "found-pantheon" && session.Log[^1].Payload.GetProperty("Belief").GetString() == belief
                && !screen.Picker.Visible && human.ReligionManager.Religion != null && !Blocking()),

            new Act("select the warrior", () =>
            {
                var warrior = human.Units.GetCivUnits().OrderBy(it => it.Id).First(it => it.BaseUnit.Name == "Warrior");
                screen.SelectUnitById(warrior.Id);
            }),
            new WaitFrames(4),
            new AssertQuery("the warrior is promotable, so the Promote button is shown", () => screen.PromoteButton.Visible),
            new Click(() => Centre(screen.PromoteButton)),
            new WaitFrames(3),
            new AssertQuery("the promotion picker lists the promotions the engine offers, with the experience line", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.Promotion } && screen.Picker.PromptText.StartsWith("Experience: 30 / ", StringComparison.Ordinal)
                && screen.Picker.Spec.Slots[0].Options.Count > 0),
            new Act("remember the log and the first promotion", () =>
            {
                logged = session.Log.Count;
                person = FirstOption();
            }),
            new Click(() => Centre(screen.Picker.OptionButton(0, person))),
            new WaitFrames(2),
            new Capture("promotion", "The promotion picker for the warrior: experience line, promotions it may take, Promote enabled"),
            new Press("Take promotion"),
            new WaitFrames(3),
            new AssertQuery("promote-unit was sent and the unit has the promotion", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "promote-unit" && !screen.Picker.Visible
                && human.Units.GetCivUnits().Any(it => it.Promotions.Promotions.Contains(person))),
        ];
    }
}
