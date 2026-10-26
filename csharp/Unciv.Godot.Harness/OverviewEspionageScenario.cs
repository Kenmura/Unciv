using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Models;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C050: the overview's espionage and global politics tabs through the real game screen and the real engine, in the espionage game (espionage on; Babylon has a hidden spy and a
/// set-up spy in the city-state; Rome and the city-state are met, Greece is not). The Move buttons are exactly the destinations the engine offers and send MoveSpy; the coup
/// button shows the engine's odds, stages the coup and calls it off (SetSpyCoup). The politics tab has one row per met civ with the engine's diplomacy panel and never lists
/// the unmet civ; the diplomatic vote line says no vote is open.
/// </summary>
public sealed class OverviewEspionageScenario : Scenario
{
    public override string Name => "overview-espionage";

    public override bool Uses3D => true;

    public override bool Espionage => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell which spy is where and what it is doing, and which buttons move it?",
        "Does the coup button say what it does and its chance, and does it change when the coup is staged?",
        "Does the politics tab make each met civ's relation to you clear, and is the vote line understandable?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        var hidden = human.EspionageManager.SpyList[0];
        var resident = human.EspionageManager.SpyList[1];
        var romeCity = rome.Cities[0];
        int logged = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        T Ask<T>(Unciv.Queries.IQuery<T> query) where T : class => recording.QueryAsync(query).GetAwaiter().GetResult().NotNull();
        SpyEntry Entry(Spy spy) => Ask(new SpyDestinationsQuery()).Spies.Single(it => it.Name == spy.Name);
        int RomeMove(Spy spy) => Entry(spy).Destinations.ToList().FindIndex(it => it.CityId == romeCity.Id) + (Entry(spy).CanMoveToHideout ? 1 : 0);

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new Act("open the overview on the espionage tab", () => screen.OpenOverviewScreen(OverviewModel.Espionage)),
            new WaitFrames(3),
            new AssertQuery("the Move buttons are exactly the destinations the engine offers (hideout first, then cities), nothing else", () =>
            {
                var spies = Ask(new SpyDestinationsQuery()).Spies;
                bool buttonsMatch = spies.All(spy =>
                {
                    int expected = spy.Destinations.Count + (spy.CanMoveToHideout ? 1 : 0);
                    return Enumerable.Range(0, expected).All(i => screen.Overview.SpyButton(spy.Name, i) != null) && screen.Overview.SpyButton(spy.Name, expected) == null;
                });
                return screen.Overview.SelectedTab == OverviewModel.Espionage && screen.Overview.Notice(OverviewModel.Espionage) == null && spies.Count == 2 && buttonsMatch
                    && !Entry(hidden).CanMoveToHideout && Entry(resident).CanMoveToHideout;
            }),
            new AssertQuery("only the spy in the city-state can stage a coup, and its button shows the engine's odds", () =>
            {
                var entry = Entry(resident);
                string expected = $"Stage a coup ({KotlinMath.RoundToInt(entry.CoupChance.NotNull() * 100f)}% chance)";
                return screen.Overview.SpyButton(hidden.Name, -1) == null && screen.Overview.SpyButton(resident.Name, -1)?.Text == expected;
            }),
            new Capture("espionage", "Espionage tab: two spies with their moves, and a coup button for the spy in the city-state"),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Centre(screen.Overview.SpyButton(hidden.Name, RomeMove(hidden)))),
            new WaitFrames(3),
            new AssertQuery("'Move to' Rome's city sends one MoveSpy and the engine's spy is on its way there", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "move-spy" && session.Log[^1].Payload.GetProperty("CityId").GetString() == romeCity.Id
                && hidden.GetCityOrNull() == romeCity && hidden.Action == SpyAction.Moving && Entry(hidden).Location == romeCity.Name
                && screen.Overview.MessageText == ""),
            new Capture("moved", "The first spy is on its way to Rome's capital; the page shows its new location"),

            new Click(() => Centre(screen.Overview.SpyButton(resident.Name, -1))),
            new WaitFrames(3),
            new AssertQuery("the coup button stages the coup (SetSpyCoup) and then offers to call it off", () =>
                session.Log.Count == logged + 2 && session.Log[^1].Type == "set-spy-coup" && session.Log[^1].Payload.GetProperty("Stage").GetBoolean() && resident.Action == SpyAction.Coup
                && screen.Overview.SpyButton(resident.Name, -1)?.Text == "Call off the coup"),
            new Capture("coup", "The resident spy is staging a coup; the button now calls it off"),
            new Click(() => Centre(screen.Overview.SpyButton(resident.Name, -1))),
            new WaitFrames(3),
            new AssertQuery("calling it off sends SetSpyCoup with Stage false and the stage button returns", () =>
                session.Log.Count == logged + 3 && session.Log[^1].Type == "set-spy-coup" && !session.Log[^1].Payload.GetProperty("Stage").GetBoolean() && resident.Action == SpyAction.CounterIntelligence
                && screen.Overview.SpyButton(resident.Name, -1)?.Text.StartsWith("Stage a coup", StringComparison.Ordinal) == true),

            new Click(() => Centre(screen.Overview.TabButton(OverviewModel.Politics))),
            new WaitFrames(3),
            new AssertQuery("the politics tab has one row per met civ (never the unmet one) with the engine's status and attitude; no vote is open", () =>
            {
                var overview = Ask(new EmpireOverviewQuery());
                var table = screen.Overview.Tables(OverviewModel.Politics)[0];
                bool rowsMatch = table.Rows.All(row =>
                {
                    var panel = Ask(new DiplomacyPanelQuery(row.Key));
                    return row.Cells[2] == panel.Status && row.Cells[3] == panel.Relationship && row.Cells[1] == (panel.IsCityState ? "City-state" : "Major");
                });
                return screen.Overview.SelectedTab == OverviewModel.Politics && table.Rows.Select(it => it.Key).SequenceEqual(overview.KnownCivs.Select(it => it.CivName))
                    && table.Count == 2 && table.Rows.All(it => it.Key != ScenarioGame.HumanB) && rowsMatch && !Ask(new DiplomaticVoteQuery()).CanVote;
            }),
            new Capture("politics", "Politics tab: the relation with each civ you have met, and the diplomatic vote line"),
            new Press("Close overview"),
            new AssertQuery("the close button closes the screen; only the three spy commands were sent", () => !screen.IsOverviewVisible && session.Log.Count == logged + 3),
        ];
    }
}
