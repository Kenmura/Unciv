using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Ui;
using Unciv.Harness;
using Unciv.Models;
using Unciv.Queries.Empire;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C051: the espionage screen through the real game screen and the real engine, in the espionage game (espionage on; Babylon has a hidden spy and a set-up spy in the city-state;
/// Rome and the city-state are met, Greece is not). The tables are the engine's answer about the viewer's own spies; pressing Move only selects (nothing is sent) and offers
/// "Move here" exactly at the places the engine offers that spy; choosing one sends MoveSpy; the coup button stages and calls off a coup (SetSpyCoup); "Assign spies" opens the
/// multi-spy picker over the screen and its Confirm sends MoveSpy for each chosen spy, after which the screen shows the new places.
/// </summary>
public sealed class EspionageScenario : Scenario
{
    public override string Name => "espionage";

    public override bool Uses3D => true;

    public override bool Espionage => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Can you tell which spy is where and what it is doing?",
        "Is it clear that Move only selects, which places are valid for that spy, and how to cancel?",
        "Does the coup button say what it does and its chance, and does it change once staged?",
        "Does the assignment picker sit clearly above the screen and is it clear which spies it moves?",
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
        var cityStateCity = game.Civilizations.First(it => it.IsCityState).Cities[0];
        int logged = 0;

        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        T Ask<T>(Unciv.Queries.IQuery<T> query) where T : class => recording.QueryAsync(query).GetAwaiter().GetResult().NotNull();
        SpyEntry Entry(Spy spy) => Ask(new SpyDestinationsQuery()).Spies.Single(it => it.Name == spy.Name);

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new Click(() => Centre(screen.EspionageButton)),
            new WaitFrames(3),
            new AssertQuery("the screen lists exactly the engine's spies and places, and names only civs the viewer knows", () =>
            {
                var engine = Ask(new SpyDestinationsQuery());
                var spec = screen.Espionage.Spec.NotNull();
                var offered = engine.Spies.SelectMany(it => it.Destinations).Select(it => it.CityId).Distinct().Order(StringComparer.Ordinal).ToList();
                return screen.IsEspionageVisible && screen.Espionage.Notice == null && spec.Spies.Select(it => it.Name).SequenceEqual(engine.Spies.Select(it => it.Name))
                    && spec.Spies.Select(it => it.Location).SequenceEqual(engine.Spies.Select(it => it.Location)) && spec.Spies.Count == 2
                    && spec.Locations.Where(it => it.CityId != null).Select(it => it.CityId.NotNull()).Order(StringComparer.Ordinal).SequenceEqual(offered)
                    && spec.Locations.Any(it => it.CityId == romeCity.Id && it.Civ == ScenarioGame.Ai) && spec.Locations.All(it => it.Civ != ScenarioGame.HumanB)
                    && spec.Locations.Single(it => it.IsHideout).SpiesPresent == hidden.Name && spec.Locations.Single(it => it.CityId == null && it.Name == cityStateCity.Name).SpiesPresent == resident.Name
                    && !screen.Espionage.AssignButton.Disabled && screen.Espionage.MoveButton(hidden.Name)?.Disabled == false;
            }),
            new AssertQuery("only the spy in the city-state has a coup button: the short text with the engine's odds, the whole sentence as its tooltip", () =>
            {
                string percent = KotlinMath.RoundToInt(Entry(resident).CoupChance.NotNull() * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var button = screen.Espionage.CoupButton(resident.Name);
                return screen.Espionage.CoupButton(hidden.Name) == null && button?.Text == $"Coup {percent}%" && button.TooltipText == $"Stage a coup ({percent}% chance)";
            }),
            new AssertQuery("the spy table sits in a frame, fits the 640px screen with every row action inside it and the footer holds the primary Assign spies button", () =>
            {
                var table = screen.Espionage.FindChild("SpyData", true, false) as DataTable;
                var rect = screen.Espionage.CoupButton(resident.Name).NotNull().GetGlobalRect();
                float width = screen.GetViewportRect().Size.X;
                return table != null && table.RowCount == 2 && table.Cell(0, 1).HorizontalAlignment == HorizontalAlignment.Right && table.GetGlobalRect().End.X <= width
                    && rect.Position.X >= 0 && rect.End.X <= width && rect.Size.X > 0 && screen.Espionage.Frame.TitleText == "Espionage"
                    && screen.Espionage.Frame.FooterActions.Contains(screen.Espionage.AssignButton) && screen.Espionage.AssignButton.ThemeTypeVariation == UiVariants.ButtonPrimary;
            }),
            // C059 review, kept in C074: the coup text is short, but a long translation must still not widen the row past the screen (the button clips instead)
            new Act("give the coup button a long translated-length text", () => screen.Espionage.CoupButton(resident.Name).NotNull().Text =
                "Stage a coup (35% chance) in this city-state, a long translated label that has to be clipped instead of widening the table past the screen"),
            new WaitFrames(3),
            new AssertQuery("a long coup text is clipped: the button and the table stay inside the 640px screen", () =>
            {
                var rect = screen.Espionage.CoupButton(resident.Name).NotNull().GetGlobalRect();
                var table = screen.Espionage.FindChild("SpyData", true, false) as Control;
                return rect.Position.X >= 0 && rect.End.X <= screen.GetViewportRect().Size.X && table != null && table.GetGlobalRect().End.X <= screen.GetViewportRect().Size.X;
            }),
            new Act("restore the coup text", () => screen.Espionage.CoupButton(resident.Name).NotNull().Text = $"Coup {KotlinMath.RoundToInt(Entry(resident).CoupChance.NotNull() * 100f)}%"),
            new WaitFrames(3),
            new Capture("screen", "The espionage screen in its frame: a table of the spies with rank, location and action, a Move button and a red coup button at the end of the spy in the city-state, then the table of places and the Assign spies button in the footer"),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Centre(screen.Espionage.MoveButton(hidden.Name))),
            new WaitFrames(3),
            new AssertQuery("Move only selects: Cancel replaces it, Move here appears exactly where the engine offers the spy, nothing was sent", () =>
            {
                var destinations = Entry(hidden).Destinations.Select(it => it.CityId).Order(StringComparer.Ordinal).ToList();
                var shown = screen.Espionage.Spec.NotNull().Locations.Where(it => it.CityId != null && screen.Espionage.HereButton(it.CityId) != null).Select(it => it.CityId.NotNull()).Order(StringComparer.Ordinal).ToList();
                return session.Log.Count == logged && screen.Espionage.SelectedSpy == hidden.Name && screen.Espionage.MoveButton(hidden.Name)?.Text == "Cancel"
                    && shown.SequenceEqual(destinations) && destinations.Contains(romeCity.Id) && screen.Espionage.HereButton("") == null;
            }),
            new Capture("selecting", "Move was pressed on the first spy: Move became Cancel and every place the engine offers it shows a Move here button at the end of its row"),
            new Click(() => Centre(screen.Espionage.MoveButton(hidden.Name))),
            new WaitFrames(2),
            new AssertQuery("Cancel clears the selection and the Move here buttons", () =>
                screen.Espionage.SelectedSpy == null && screen.Espionage.HereButton(romeCity.Id) == null && screen.Espionage.MoveButton(hidden.Name)?.Text == "Move" && session.Log.Count == logged),
            new Click(() => Centre(screen.Espionage.MoveButton(hidden.Name))),
            new WaitFrames(2),
            new Act("scroll Rome's Move here into view", () => screen.Espionage.EnsureVisible(screen.Espionage.HereButton(romeCity.Id).NotNull())),
            new WaitFrames(2),
            new Click(() => Centre(screen.Espionage.HereButton(romeCity.Id))),
            new WaitFrames(3),
            new AssertQuery("Move here Rome's city sent one MoveSpy and the engine's spy is on its way there; the screen shows it", () =>
                session.Log.Count == logged + 1 && session.Log[^1].Type == "move-spy" && session.Log[^1].Payload.GetProperty("CityId").GetString() == romeCity.Id
                && hidden.GetCityOrNull() == romeCity && hidden.Action == SpyAction.Moving && screen.Espionage.SelectedSpy == null
                && screen.Espionage.Spec.NotNull().Spies.Single(it => it.Name == hidden.Name).Location == romeCity.Name
                && screen.Espionage.Spec.Locations.Single(it => it.Name == romeCity.Name).SpiesPresent == hidden.Name),
            new Capture("moved", "The first spy was sent to Rome's capital: its row and the city's Spy present column show it"),

            new Click(() => Centre(screen.Espionage.CoupButton(resident.Name))),
            new WaitFrames(3),
            new AssertQuery("the coup button staged the coup (SetSpyCoup) and now offers to call it off", () =>
                session.Log.Count == logged + 2 && session.Log[^1].Type == "set-spy-coup" && session.Log[^1].Payload.GetProperty("Stage").GetBoolean() && resident.Action == SpyAction.Coup
                && screen.Espionage.CoupButton(resident.Name)?.Text == "Call off" && screen.Espionage.CoupButton(resident.Name)?.TooltipText == "Call off the coup"),
            new Capture("coup", "The spy in the city-state is staging a coup; its button now says Call off"),
            new Click(() => Centre(screen.Espionage.CoupButton(resident.Name))),
            new WaitFrames(3),
            new AssertQuery("calling it off sent SetSpyCoup with Stage false", () =>
                session.Log.Count == logged + 3 && session.Log[^1].Type == "set-spy-coup" && !session.Log[^1].Payload.GetProperty("Stage").GetBoolean() && resident.Action == SpyAction.CounterIntelligence),

            new Click(() => Centre(screen.Espionage.AssignButton)),
            new WaitFrames(3),
            new AssertQuery("Assign spies opens the engine's multi-spy picker with one slot per living spy; nothing is sent yet", () =>
                screen.Picker.Visible && screen.Picker.Spec is { Kind: PickerKind.MoveSpies } spec && spec.Slots.Select(it => it.Id).Order(StringComparer.Ordinal).SequenceEqual(new[] { hidden.Name, resident.Name }.Order(StringComparer.Ordinal))
                && screen.Picker.ConfirmButton.Disabled && session.Log.Count == logged + 3 && screen.IsEspionageVisible),
            new Click(() => Centre(screen.Picker.OptionButton(screen.Picker.Spec.NotNull().Slots.ToList().FindIndex(it => it.Id == resident.Name), DecisionPickerModel.Hideout))),
            new WaitFrames(2),
            new Capture("assign", "The assignment picker over the screen: the second spy is chosen to return to the hideout"),
            new Press("Move spies"),
            new WaitFrames(3),
            new AssertQuery("Confirm sent one MoveSpy to the hideout; the picker closed and the screen shows the spy there", () =>
                session.Log.Count == logged + 4 && session.Log[^1].Type == "move-spy" && session.Log[^1].Payload.GetProperty("Spy").GetString() == resident.Name
                && !screen.Picker.Visible && resident.GetCityOrNull() == null && screen.IsEspionageVisible
                && screen.Espionage.Spec.NotNull().Spies.Single(it => it.Name == resident.Name).Location == Constants.SpyHideout),
            new Press("Close espionage"),
            new AssertQuery("the close button closes the screen", () => !screen.IsEspionageVisible),
        ];
    }
}
