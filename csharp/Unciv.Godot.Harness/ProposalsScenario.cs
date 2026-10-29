using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Diplomacy;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C039: incoming proposal popups through the real game screen and the real engine, in the proposals game (the diplomacy game where Rome, at war with the human,
/// proposes peace and Greece proposes a trade). The popup shows the first proposal with who, what they give and ask and the answers the engine takes. Accepting
/// the peace treaty makes the two civs stop being at war (one respond-trade-request). Greece's trade is put off (nothing sent, still pending, the HUD offers to review
/// it), reviewed again and answered with "How about something else": the request is dismissed and the trade screen opens with its offers staged as our counter
/// proposal. Then the proposer's side: our own offer to Greece is listed as waiting, taking it back is reported, and after the next turn the outcome of Greece's
/// answer is reported.
/// </summary>
public sealed class ProposalsScenario : Scenario
{
    public override string Name => "proposals";

    public override bool Uses3D => true;

    public override bool Proposals => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear who proposes what, and what you would give and get?",
        "Are the three answers understandable, and is it clear that closing the popup does not decline?",
        "Can you tell which of your own offers still wait for an answer, and what became of the last ones?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var greece = game.GetCivilization(ScenarioGame.HumanB);
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var refusals = new List<string>();
        int logged = 0;
        int humanGold = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        void Do(GameCommand command)
        {
            var result = localHost.SubmitAsync(command).GetAwaiter().GetResult();
            if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
        }
        ProposeTrade Gift(int gold) => new() { CivId = greece.CivId, OurOffers = [new TradeOfferSpec("Gold", "Gold", gold)], TheirOffers = [] };

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner, remember the command log", () =>
            {
                screen.Hud.HideBanner();
                // the informational alerts of a new game (welcome, first contacts) are acknowledged so only the proposals are left to review
                for (int guard = 0; human.PopupAlerts.Count > 0 && guard < 20; guard++)
                {
                    var alert = human.PopupAlerts[0];
                    Do(new Unciv.Commands.Turn.AckAlert { Index = 0, AlertType = alert.Type, Value = alert.Value });
                }
                logged = session.Log.Count;
            }),
            new AssertQuery("Rome's peace proposal is on screen first, with the three answers and decide later", () =>
                screen.Proposals.Visible && screen.Proposals.TitleText == "Rome proposes peace" && screen.Proposals.BodyText == "They give: Peace Treaty (10 turns)\nThey ask: Peace Treaty (10 turns)"
                && screen.Proposals.ButtonIds.SequenceEqual(["Accept", "Decline", "Counter", "DecideLater"]) && human.IsAtWarWith(rome) && human.TradeRequests.Count == 2 && refusals.Count == 0),
            new AssertQuery("the popup is the shared modal frame: centred, accent hairline, titled, buttons inside with the primary right-most", () => ModalChecks.Framed(screen.Proposals)),
            new Capture("peace", "Rome's peace proposal as a popup: accept, decline, ask for something else, or decide later"),
            new Click(() => Centre(screen.Proposals.ButtonById("Accept"))),
            new WaitFrames(2),
            new AssertQuery("accepting sent one respond-trade-request and the war with Rome is over", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "respond-trade-request" && !human.IsAtWarWith(rome) && human.TradeRequests.Count == 1),
            new AssertQuery("Greece's trade proposal took its place", () =>
                screen.Proposals.Visible && screen.Proposals.TitleText == "Greece proposes a trade" && screen.Proposals.BodyText == "They give: 50 gold\nThey ask: 20 gold"),
            new Capture("trade", "Greece's trade proposal: what they give and what they ask"),
            new Act("remember the command log", () => logged = session.Log.Count),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape decides later: it sent nothing, the proposal is still pending and the HUD offers to review it", () =>
                session.Log.Count == logged && human.TradeRequests.Count == 1 && !screen.Proposals.Visible && screen.Notifications.ReviewButton.Visible
                && screen.Notifications.ReviewButton.Text == "Review alerts (1)"),
            new Click(() => Centre(screen.Notifications.ReviewButton)),
            new WaitFrames(2),
            new AssertQuery("reviewing brings the proposal back", () => screen.Proposals.Visible && screen.Proposals.TitleText == "Greece proposes a trade" && !screen.Notifications.ReviewButton.Visible),
            new Act("remember both the command log and our treasury", () =>
            {
                logged = session.Log.Count;
                humanGold = human.Gold;
            }),
            new Click(() => Centre(screen.Proposals.ButtonById("Counter"))),
            new WaitFrames(3),
            new AssertQuery("how about something else dismissed the request (no gold moved) and opened the trade screen on Greece with the offer staged the other way round", () =>
                refusals.Count == 0 && session.Log.Count == logged + 1 && session.Log[^1].Type == "dismiss-trade-request" && human.TradeRequests.Count == 0 && human.Gold == humanGold
                && !screen.Proposals.Visible && screen.IsTradeVisible && screen.Trade.SelectedCivId == greece.CivId
                && screen.Trade.Builder.NotNull().StagedChoices(TradeSide.Give).Single().StagedAmount == 20 && screen.Trade.Builder.NotNull().StagedChoices(TradeSide.Ask).Single().StagedAmount == 50),
            new Capture("counter", "How about something else: the trade screen with Greece's offer staged for us to change"),
            new Press("Close trade"),
            new Act("propose a gift of 20 gold to Greece", () => Do(Gift(20))),
            new WaitFrames(2),
            new AssertQuery("our offer is listed as waiting for Greece's answer", () =>
                screen.Notifications.OfferText == "Waiting for Greece: you give 20 gold; you ask nothing" && greece.TradeRequests.Count == 1),
            new Capture("waiting", "Our own offer to Greece waits for its answer, listed under the notifications"),
            new Click(() => Centre(screen.TradeButton)),
            new WaitFrames(2),
            new Click(() => Centre(screen.Trade.PartnerButton(greece.CivId))),
            new WaitFrames(2),
            new Click(() => Centre(screen.Trade.RetractButton)),
            new WaitFrames(2),
            new Press("Close trade"),
            new WaitFrames(2),
            new AssertQuery("taking it back is reported and nothing waits any more", () =>
                greece.TradeRequests.Count == 0 && screen.Notifications.OfferText == "You took back your offer to Greece."),
            new Act("propose the gift again", () => Do(Gift(20))),
            new Press("Next turn"),
            new WaitFrames(3),
            new Act("dismiss the new-turn banner", () => screen.Hud.HideBanner()),
            new AssertQuery("Greece (an AI) answered on its turn and the outcome is reported", () =>
                refusals.Count == 0 && greece.TradeRequests.Count == 0 && screen.Notifications.OfferText.Split('\n').Contains("Greece accepted your offer.")),
            new Capture("answered", "The next turn: Greece answered and the outcome is reported"),
        ];
    }
}
