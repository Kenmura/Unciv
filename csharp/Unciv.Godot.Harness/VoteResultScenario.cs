using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Models.Translations;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C053: the World Congress result through the real game screen and the real engine, in the vote-result game (the diplomacy game where the vote is over: Babylon voted for
/// Rome, Rome, which owns the United Nations, for Greece, a civ Babylon has not met, and Greece for Babylon). The screen lists Babylon, Rome and the city-state with the
/// engine's counts, weights and choices, never lists Greece and names it nowhere (Rome's vote for it reads as a vote for an unknown civilization) and shows the engine's verdict. Continue sends one
/// ack-diplomatic-vote-result, after which the engine no longer shows the result and the screen is gone.
/// </summary>
public sealed class VoteResultScenario : Scenario
{
    public override string Name => "vote-result";

    public override bool Uses3D => true;

    public override bool VoteResult => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell how each civ voted and how many votes it got, and which civ owns the United Nations and so votes twice?",
        "Is the verdict clear, and is it clear that the vote for an unknown civ does not name it?",
        "Is Continue the obvious next step?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var greece = game.GetCivilization(ScenarioGame.HumanB);
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        int logged = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        DiplomaticVoteBreakdownResult Ask() => localHost.QueryAsync(new DiplomaticVoteResultQuery()).GetAwaiter().GetResult().NotNull();

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner and acknowledge the new game's alerts, so only the result is left", () =>
            {
                screen.Hud.HideBanner();
                for (int guard = 0; human.PopupAlerts.Count > 0 && guard < 20; guard++)
                {
                    var alert = human.PopupAlerts[0];
                    localHost.SubmitAsync(new Unciv.Commands.Turn.AckAlert { Index = 0, AlertType = alert.Type, Value = alert.Value }).GetAwaiter().GetResult();
                }
                logged = session.Log.Count;
            }),
            new WaitFrames(3),
            new AssertQuery("the result is up with a row per civ Babylon knows and none for Greece, matching the engine's answer", () =>
            {
                var answer = Ask();
                var texts = screen.VoteResult.RowTexts;
                return screen.VoteResult.Visible && answer.Show && texts.Count == answer.Rows.Count && texts.Count == 3
                    && texts[0][1] == human.CivName && texts[1][1] == rome.CivName && texts.All(it => it[1] != greece.CivName)
                    && !string.Concat(texts.SelectMany(it => it)).Contains(greece.CivName, StringComparison.Ordinal) && !screen.VoteResult.VerdictText.Contains(greece.CivName, StringComparison.Ordinal);
            }),
            new AssertQuery("the engine's numbers: Babylon and Rome got 1 vote each; Rome's vote is worth 2 (United Nations) and goes to an unnamed civ", () =>
            {
                var texts = screen.VoteResult.RowTexts;
                return texts[0][0] == "1" && texts[0][2] == "[1] vote".Tr() && texts[0][3] == "" && texts[0][4] == "Voted for".Tr() && texts[0][5] == rome.CivName
                    && texts[1][0] == "1" && texts[1][2] == "[2] votes".Tr() && texts[1][3] != "" && texts[1][5] == DiplomaticVoteResultQuery.UnknownCiv.Tr()
                    && screen.VoteResult.VerdictText.Contains("Minimum votes", StringComparison.Ordinal);
            }),
            new Capture("result", "The World Congress result: each civ you know with its votes, what its vote is worth, whom it voted for, the verdict and Continue"),
            new Click(() => Centre(screen.VoteResult.ContinueButton)),
            new WaitFrames(3),
            new AssertQuery("Continue sent exactly one ack-diplomatic-vote-result; the engine no longer shows the result and the screen is gone", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "ack-diplomatic-vote-result" && !human.ShouldShowDiplomaticVotingResults()
                && !Ask().Show && !screen.VoteResult.Visible),
            new Capture("continued", "After Continue the world is back and the result does not return"),
        ];
    }
}
