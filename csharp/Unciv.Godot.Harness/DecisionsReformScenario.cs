using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C052: the reform picker through the real game screen and engine, in the reform game (G&amp;K, a founded religion with one free follower belief to choose, as after a
/// one-time free belief). End turn is refused while the free belief is unchosen; the next-action button opens the picker (a follower slot with the beliefs the engine
/// lists), Confirm stays disabled and says why until a belief is chosen and then sends choose-beliefs, after which End turn is offered.
/// </summary>
public sealed class DecisionsReformScenario : Scenario
{
    public override string Name => "decisions-reform";

    public override bool Uses3D => true;

    public override int Religion => ScenarioGame.ReligionReform;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the reform picker say which kind of belief you may add, and what the highlighted belief does?",
        "Does the Confirm button say why it is disabled until a belief is chosen?",
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
        string belief = "";

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        bool Blocking() => recording.QueryAsync(new PendingDecisionsQuery()).GetAwaiter().GetResult().NotNull().Decisions.Any(it => it.Blocking);

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the engine asks to reform the religion, a decision that blocks End turn", () =>
                Blocking() && screen.Hud.ActionText == "Reform religion" && !screen.Hud.EndTurnShown),
            new Press("Reform religion"),
            new WaitFrames(3),
            new AssertQuery("the picker has one follower slot with the engine's beliefs and Confirm waits for a choice, saying so", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: Unciv.Assets.PickerKind.Beliefs } && screen.Picker.TitleText == "Reform your religion"
                && screen.Picker.Spec.Slots.Count == 1 && screen.Picker.Spec.Slots[0].Title == "Follower belief" && screen.Picker.Spec.Slots[0].Options.Count > 1
                && screen.Picker.ConfirmButton.Disabled && screen.Picker.ProblemText == "Choose: Follower belief"),
            new Capture("reform", "The reform picker before a choice: one follower belief slot; Choose beliefs is disabled and says to choose first"),
            new Act("remember the log and the first belief", () =>
            {
                logged = session.Log.Count;
                belief = screen.Picker.Spec.NotNull().Slots[0].Options.First(it => it.Enabled).Id;
            }),
            new Click(() => Centre(screen.Picker.OptionButton(0, belief))),
            new WaitFrames(2),
            new AssertQuery("choosing only highlights: nothing sent, the belief's effects show and Confirm is enabled", () =>
                session.Log.Count == logged && screen.Picker.DetailText.Length > 0 && !screen.Picker.ConfirmButton.Disabled),
            new Capture("reform-chosen", "A belief chosen: its effects below the list and Choose beliefs enabled"),
            new Press("Choose beliefs"),
            new WaitFrames(3),
            new AssertQuery("choose-beliefs was sent with that belief; nothing blocks and Next turn is offered", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "choose-beliefs" && session.Log[^1].Payload.GetProperty("Beliefs")[0].GetString() == belief
                && !screen.Picker.Visible && !human.ReligionManager.HasFreeBeliefs() && human.ReligionManager.Religion!.HasBelief(belief) && !Blocking() && screen.Hud.EndTurnShown),
        ];
    }
}
