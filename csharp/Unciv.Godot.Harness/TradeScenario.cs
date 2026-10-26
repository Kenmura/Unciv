using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Empire;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C022: the trade screen through the real game screen and the real engine, in the trade game (the diplomacy game plus a trade and a demand Rome sent the human, trade
/// hints on). The partner list is the met major civs only (never the city-state or unmet Greece). Every offer the engine lists for either side is a button, disabled
/// exactly when the engine says it is not tradable. Refusing the demand removes its alert. Accepting Rome's trade moves gold in both civs by the numbers the offer names.
/// A staged gift of gold with the plus button shows the engine's hint and, proposed, becomes Rome's pending request that retract takes back; proposed again, Rome (an AI)
/// answers on its own turn, accepts the gift and both treasuries change.
/// </summary>
public sealed class TradeScenario : Scenario
{
    public override string Name => "trade";

    public override bool Uses3D => true;

    public override bool Trade => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is it clear which column is what you give and which is what you ask for?",
        "Can you tell what is staged, and change an amount, without guessing?",
        "Are the incoming trade and the demand impossible to miss, and are the three answers to a trade understandable?",
        "Is the hint about the AI's answer useful, and is it clear when an offer waits for their turn?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var cityState = game.Civilizations.Single(it => it.IsCityState);
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var refusals = new List<string>();
        int logged = 0;
        int humanGold = 0;
        int romeGold = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        void Do(GameCommand command)
        {
            var result = localHost.SubmitAsync(command).GetAwaiter().GetResult();
            if (!result.Accepted) refusals.Add($"{command.GetType().Name}: {result.Message}");
        }
        TradeOptionsResult Options() => localHost.QueryAsync(new TradeOptionsQuery(rome.CivName)).GetAwaiter().GetResult().NotNull();
        Button? Offer(TradeSide side, string type, string name) => screen.Trade.Control(TradeScreenModel.KeyOf(side, type, name));
        void RememberTreasuries()
        {
            humanGold = human.Gold;
            romeGold = rome.Gold;
        }
        // Every engine offer of both columns is a button and is disabled exactly when the engine cannot stage it.
        bool ButtonsEqualQuery()
        {
            var options = Options();
            bool Column(TradeSide side, IReadOnlyList<TradeOfferEntry> entries) =>
                entries.All(it => Offer(side, it.Type, it.Name) is { } button && button.Disabled == !it.Tradable)
                && screen.Trade.Builder.NotNull().Groups(side).SelectMany(it => it.Choices).Select(it => it.Entry).Count() == entries.Count;
            return Column(TradeSide.Give, options.Ours) && Column(TradeSide.Ask, options.Theirs);
        }

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner, start the research, remember the command log", () =>
            {
                screen.Hud.HideBanner();
                Do(new SetResearchQueue { Techs = ["Pottery"] });
                logged = session.Log.Count;
            }),
            new Click(() => Centre(screen.Alerts.ButtonById("DecideLater"))),
            new Click(() => Centre(screen.Proposals.ButtonById("DecideLater"))),
            new Click(() => Centre(screen.TradeButton)),
            new WaitFrames(2),
            new AssertQuery("the screen is open and lists only Rome: not the city-state, not the unmet Greece", () =>
                screen.IsTradeVisible && screen.Trade.Partners.Select(it => it.CivId).SequenceEqual([rome.CivId]) && screen.Trade.PartnerButton(cityState.CivId) == null
                && screen.Trade.PartnerButton(ScenarioGame.HumanB) == null && screen.Trade.SelectedCivId == rome.CivId && screen.Trade.Builder != null),
            new AssertQuery("the offer buttons are the engine's: gold on both sides, each enabled as the engine says", () =>
                refusals.Count == 0 && ButtonsEqualQuery() && Options().Ours.Any(it => it.Type == "Gold" && it.Amount == human.Gold) && Options().Theirs.Any(it => it.Type == "Gold" && it.Amount == rome.Gold)),
            new AssertQuery("Rome's trade and its demand wait at the top, each with the answers the engine takes", () =>
                screen.Trade.IncomingTrades.Count == 1 && screen.Trade.IncomingTrades[0].Gives.SequenceEqual(["100 gold"]) && screen.Trade.IncomingTrades[0].Asks.SequenceEqual(["30 gold"])
                && screen.Trade.IncomingDemands.Count == 1 && screen.Trade.Control("Accept_" + rome.CivId) != null && screen.Trade.Control("Counter_" + rome.CivId) != null
                && screen.Trade.Control("DemandRefuse_" + rome.CivId) != null),
            new Capture("open", "Rome's trade proposal and demand wait at the top; the columns list everything each side could offer"),
            new Click(() => Centre(screen.Trade.Control("DemandRefuse_" + rome.CivId))),
            new WaitFrames(2),
            new AssertQuery("refusing the demand sent one command and the engine's alert is gone", () =>
                session.Log.Count == logged + 1 && screen.Trade.IncomingDemands.Count == 0 && human.PopupAlerts.All(it => it.Type != Unciv.Logic.Civilization.AlertType.DemandToStopSpreadingReligion)),
            new Act("remember both treasuries", RememberTreasuries),
            new Click(() => Centre(screen.Trade.Control("Accept_" + rome.CivId))),
            new WaitFrames(2),
            new AssertQuery("accepting changed both civs: we got 100 gold and paid 30, Rome the reverse; the request is gone", () =>
                human.Gold == humanGold + 70 && rome.Gold == romeGold - 70 && human.TradeRequests.Count == 0 && screen.Trade.IncomingTrades.Count == 0),
            new Capture("accepted", "After accepting Rome's trade: nothing waits and the treasuries changed"),
            new Click(() => Centre(Offer(TradeSide.Give, "Gold", "Gold"))),
            new Click(() => Centre(screen.Trade.Control("Plus_" + TradeScreenModel.KeyOf(TradeSide.Give, "Gold", "Gold")))),
            new Click(() => Centre(screen.Trade.Control("Plus_" + TradeScreenModel.KeyOf(TradeSide.Give, "Gold", "Gold")))),
            new Click(() => Centre(screen.Trade.Control("Minus_" + TradeScreenModel.KeyOf(TradeSide.Give, "Gold", "Gold")))),
            new WaitFrames(2),
            new AssertQuery("the gift is staged at 20 gold by plus and minus, the summary says so and the command is a gift of 20 gold", () =>
                screen.Trade.Builder.NotNull().StagedChoices(TradeSide.Give).Single().StagedAmount == 20 && screen.Trade.SummaryText == "You give: 20 gold\nYou ask: nothing"
                && screen.Trade.Builder.Command() is { CivId: var civ } command && civ == rome.CivId && command.OurOffers.Single().Amount == 20 && command.TheirOffers.Count == 0),
            new AssertQuery("the hint is the engine's answer for exactly this trade", () =>
                screen.Trade.HintText == TradeScreenModel.HintText(localHost.QueryAsync(new TradeHintQuery(rome.CivName, [new TradeOfferRequest("Gold", "Gold", 20)], [])).GetAwaiter().GetResult())
                && screen.Trade.HintText.Length > 0),
            new Capture("staged", "A gift of 20 gold staged: the summary and the engine's hint about their answer"),
            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Centre(screen.Trade.ProposeButton)),
            new WaitFrames(2),
            new AssertQuery("proposing sent one command; Rome holds our request, nothing moved yet, and the screen waits (retract offered, propose off)", () =>
                session.Log.Count == logged + 1 && rome.TradeRequests.Count == 1 && human.Gold == humanGold + 70 && screen.Trade.PendingText.StartsWith("Your offer waits", StringComparison.Ordinal)
                && screen.Trade.RetractButton.Visible && screen.Trade.ProposeButton.Disabled),
            new Capture("proposed", "Our offer waits for Rome's turn; it can be retracted"),
            new Click(() => Centre(screen.Trade.RetractButton)),
            new WaitFrames(2),
            new AssertQuery("retracting took the request back from Rome", () => rome.TradeRequests.Count == 0 && !screen.Trade.RetractButton.Visible && screen.Trade.PendingText == ""),
            new Click(() => Centre(Offer(TradeSide.Give, "Gold", "Gold"))),
            new Click(() => Centre(screen.Trade.Control("Plus_" + TradeScreenModel.KeyOf(TradeSide.Give, "Gold", "Gold")))),
            new Click(() => Centre(screen.Trade.Control("Plus_" + TradeScreenModel.KeyOf(TradeSide.Give, "Gold", "Gold")))),
            new Click(() => Centre(screen.Trade.ProposeButton)),
            new WaitFrames(2),
            new AssertQuery("the gift of 30 gold is proposed again", () => rome.TradeRequests.Count == 1 && rome.TradeRequests[0].Trade.TheirOffers.Single().Amount == 30),
            new Act("remember both treasuries", RememberTreasuries),
            new Press("Close trade"),
            new AssertQuery("the close button closes the screen", () => !screen.IsTradeVisible),
            new Press("Next turn"),
            new WaitFrames(2),
            new Act("dismiss the new-turn banner and open the trade screen again", () =>
            {
                screen.Hud.HideBanner();
                screen.OpenTradeScreen();
            }),
            new WaitFrames(2),
            new AssertQuery("Rome (an AI) took the gift on its turn: its request is gone and the gold moved, 30 more for Rome and about 30 less for us", () =>
                refusals.Count == 0 && rome.TradeRequests.Count == 0 && rome.Gold >= romeGold + 20 && human.Gold <= humanGold - 20 && screen.Trade.PendingText == ""),
            new Capture("answered", "The next turn: Rome answered, the offer is gone and the gold changed hands"),
            new PressKey(Key.Escape),
            new AssertQuery("Escape closes the screen", () => !screen.IsTradeVisible),
        ];
    }
}
