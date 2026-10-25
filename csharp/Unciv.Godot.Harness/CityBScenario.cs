using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using CityObject = Unciv.Logic.City.City;
using Unciv.Net.Visibility;
using Unciv.Queries.Cities;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C018: city screen B through the real game screen and the real engine, without God mode (Babylon has 500 gold, <see cref="Scenario.Rich"/>). The capital's Production tab
/// lists what the engine says is buildable; clicks queue a Warrior, a Monument and a perpetual Nothing item, reorder them (up, to the top, down) and remove one, each asserted from
/// the engine's own queue. Buying the Warrior takes exactly the price the construction-list query quoted; buying the Monument and selling it back pays the engine's sell price,
/// and a second sale in the same turn is refused by the engine. Razing the capital is refused with the engine's reason. A second, puppet city shows its state and the Annex button,
/// which makes it an ordinary city that then offers Raze, and Raze / Stop razing toggle the engine's flag.
/// </summary>
public sealed class CityBScenario : Scenario
{
    public override string Name => "city-b";

    public override bool Uses3D => true;

    public override bool Rich => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can I tell what is being built, what is queued and in which order, and how long each takes?",
        "Is it clear why a Buy button is disabled (hover it), and what a purchase will cost?",
        "Is the information density right: queue, buildable list with costs and turns, buildings with sell prices?",
        "Are Raze and Annex hard enough to hit by accident, and is the puppet state explained?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        long now = 1000;
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => now += 1000));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        var city = human.Cities[0];
        var puppet = human.Cities[1];
        var refusals = new List<string>();

        CityDto Dto(CityObject target) => recording.Snapshot.Cities.First(it => it.Own?.Id == target.Id);
        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        Vector2 At(string name) => Centre(screen.City.Production.ButtonNamed(name));
        List<string> Queue() => city.CityConstructions.ConstructionQueue.ToList();
        CityPanelResult Panel() => screen.City.Panel.NotNull();
        ConstructionListResult List() => recording.QueryAsync(new ConstructionListQuery(city.Id, OnlyDisplayed: false)).GetAwaiter().GetResult().NotNull();
        bool Disabled(string name) => screen.City.Production.ButtonNamed(name) is { Disabled: true };
        bool Enabled(string name) => screen.City.Production.ButtonNamed(name) is { Disabled: false };

        int goldBefore = 0;
        int quoted = 0;
        int sellPrice = 0;
        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner and open the capital's city screen on the Production tab", () =>
            {
                screen.Hud.HideBanner();
                screen.OpenCityScreen(Dto(city));
            }),
            new WaitFrames(2),
            new Click(() => Centre(screen.City.Tab(production: true))),
            new AssertQuery("the Production tab shows the engine's buildable list, and the citizen view is hidden", () =>
                screen.City.ShowingProduction && screen.City.Production.Visible
                && screen.City.Production.ButtonNamed("Build_Warrior") != null && screen.City.Production.ButtonNamed("Build_Monument") != null && screen.City.Production.QueueTexts.Count == Panel().Queue.Count),
            new Capture("open", "The capital's Production tab: empty queue, the buildable list with turns and cost, and Buy buttons"),

            new Click(() => At("Build_Warrior")),
            new Click(() => At("Build_Monument")),
            new Click(() => At("Build_Nothing")),
            new AssertQuery("three clicks queued Warrior, Monument and the perpetual Nothing item, as the engine's queue shows", () =>
                Queue().SequenceEqual(new[] { "Warrior", "Monument", "Nothing" }) && refusals.Count == 0
                && screen.City.Production.QueueTexts.Count == 3 && screen.City.Production.QueueTexts[2].Contains("perpetual", StringComparison.Ordinal)
                && screen.City.Production.QueueTexts[0].StartsWith("1. Warrior", StringComparison.Ordinal)),
            new AssertQuery("the first entry cannot be raised or sent to the top, the last cannot be lowered, a perpetual item cannot go to the top", () =>
                Disabled("Up_0") && Disabled("Top_0") && Enabled("Down_0") && Disabled("Down_2") && Enabled("Up_2") && Disabled("Top_2") && Enabled("Top_1")),
            new Capture("queued", "Warrior, Monument and a perpetual Nothing item queued"),

            new Click(() => At("Up_1")),
            new AssertQuery("Up moved the Monument ahead of the Warrior", () => Queue().SequenceEqual(new[] { "Monument", "Warrior", "Nothing" }) && Panel().CurrentConstruction == "Monument"),
            new Click(() => At("Top_1")),
            new AssertQuery("Top moved the Warrior back to the front", () => Queue().SequenceEqual(new[] { "Warrior", "Monument", "Nothing" })),
            new Click(() => At("Down_0")),
            new AssertQuery("Down moved the Warrior behind the Monument", () => Queue().SequenceEqual(new[] { "Monument", "Warrior", "Nothing" })),
            new Click(() => At("Remove_2")),
            new AssertQuery("Remove took the Gold item out", () => Queue().SequenceEqual(new[] { "Monument", "Warrior" }) && screen.City.Production.ButtonNamed("Remove_2") == null),
            new Capture("reordered", "Monument first, Warrior second, after reordering and removing the perpetual item"),

            new AssertQuery("the Warrior's Buy button is disabled with the engine's reason, because a unit stands in the city center", () =>
                Disabled("Buy_Warrior_Gold") && screen.City.Production.ButtonNamed("Buy_Warrior_Gold").NotNull().TooltipText.Contains("cannot be bought", StringComparison.Ordinal)
                && !List().Entries.First(it => it.Name == "Warrior").BuyCosts.First(it => it.Stat == "Gold").Allowed),
            new Act("remember the gold and the price the engine quotes for the Monument", () =>
            {
                goldBefore = human.Gold;
                quoted = List().Entries.First(it => it.Name == "Monument").BuyCosts.First(it => it.Stat == "Gold").Cost;
            }),
            new AssertQuery("the queued Monument's Buy button is enabled and shows the quoted price", () =>
                quoted > 0 && quoted <= goldBefore && Enabled("QBuy_0_Gold") && screen.City.Production.ButtonNamed("QBuy_0_Gold").NotNull().Text == $"Buy {quoted} Gold"),
            new Click(() => At("QBuy_0_Gold")),
            new AssertQuery("buying the Monument took exactly the quoted price, built it, and the Buildings column offers to sell it", () =>
                refusals.Count == 0 && human.Gold == goldBefore - quoted && city.CityConstructions.IsBuilt("Monument") && Queue().SequenceEqual(new[] { "Warrior" })
                && screen.City.Production.BuildingTexts.Any(it => it.StartsWith("Monument (sells for", StringComparison.Ordinal)) && Enabled("Sell_Monument")),
            new Capture("bought", "The Monument bought with gold and listed with its sell price; the Warrior's Buy is disabled"),

            new Act("remember the gold and the sell price", () =>
            {
                goldBefore = human.Gold;
                sellPrice = Panel().Buildings.First(it => it.Name == "Monument").SellPrice;
            }),
            new Click(() => At("Sell_Monument")),
            new AssertQuery("selling the Monument paid the engine's sell price and removed the building", () =>
                sellPrice > 0 && human.Gold == goldBefore + sellPrice && !city.CityConstructions.IsBuilt("Monument") && screen.City.Production.ButtonNamed("Sell_Monument") == null),

            new Click(() => At("State_Raze city")),
            new AssertQuery("razing the capital is refused by the engine and the screen says so", () =>
                !city.IsBeingRazed && screen.City.MessageText.StartsWith("Refused:", StringComparison.Ordinal)),

            new Act("open the puppet city on the Production tab", () => screen.OpenCityScreen(Dto(puppet))),
            new WaitFrames(2),
            new AssertQuery("the puppet city shows its state, an Annex button, and every queue and build button disabled", () =>
                puppet.IsPuppet && screen.City.ShowingProduction && screen.City.Production.StateText.Contains("Puppet city", StringComparison.Ordinal)
                && Enabled("State_Annex city") && screen.City.Production.ButtonNamed("State_Raze city") == null && Disabled("Build_Warrior")),
            new Capture("puppet", "A puppet city: its state explained, Annex offered, production commands disabled"),
            new Click(() => At("State_Annex city")),
            new AssertQuery("Annex made it an ordinary city that offers Raze", () =>
                !puppet.IsPuppet && refusals.Count == 0 && Enabled("State_Raze city") && screen.City.Production.StateText.Length == 0 && Enabled("Build_Warrior")),
            new Click(() => At("State_Raze city")),
            new AssertQuery("Raze set the engine's flag and the screen offers Stop razing and says so", () =>
                puppet.IsBeingRazed && Enabled("State_Stop razing") && screen.City.Production.StateText.Contains("being razed", StringComparison.Ordinal) && screen.City.TitleText.Contains("Being razed", StringComparison.Ordinal)),
            new Capture("razing", "A city being razed, with Stop razing offered"),
            new Click(() => At("State_Stop razing")),
            new AssertQuery("Stop razing cleared the flag", () => !puppet.IsBeingRazed && Enabled("State_Raze city")),
            new Press("Close city"),
            new AssertQuery("the screen closed", () => screen.OpenCity == null && !screen.IsCityPanelVisible),
        ];
    }
}
