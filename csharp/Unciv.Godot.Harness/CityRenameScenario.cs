using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Net.Visibility;

namespace Unciv.GodotHarness;

/// <summary>
/// C048: renaming a city through the real game screen and engine. The city screen header offers "Rename" for an own city (not for a foreign one); the popup starts with the
/// current name, refuses an empty, a markup-like or an unchanged name with the reason and a disabled OK, Escape closes the popup but not the city screen, and Enter (or OK) on
/// a good name sends RenameCity: the engine's city has the new name, and the city screen title, the banner on the map and the overview's city table all show it.
/// (Named city-rename because rename-city is the C008 runner proof.)
/// </summary>
public sealed class CityRenameScenario : Scenario
{
    private const string NewName = "Ur of the Chaldees";

    public override string Name => "city-rename";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the Rename button easy to find in the city screen header, and does it fit at 640x480?",
        "Does the popup say why a name is refused, and is it clear how to leave it?",
        "Does the new name show everywhere the city is listed?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var screen = new GameScreen();
        host.AddChild(screen);
        var local = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        screen.Start(local, AssetRegistry.ForRuleset(game.Ruleset));
        screen.TutorialsEnabled = false;
        var city = ScenarioGame.FirstCity(game, ScenarioGame.HumanA);
        string original = city.Name;
        long seqBefore = 0;

        CityDto Dto() => local.Snapshot.Cities.First(it => it.Own?.Id == city.Id);
        void Type(string text)
        {
            screen.City.RenamePopup.Field.Text = text;
            screen.City.RenamePopup.Field.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner and open the capital's city screen", () =>
            {
                screen.Hud.HideBanner();
                seqBefore = session.LastSeq;
                screen.OpenCityScreen(Dto());
            }),
            new WaitFrames(2),
            new AssertQuery("the city screen offers Rename for an own city and the popup is closed", () =>
                screen.City.RenameButton.Visible && !screen.City.RenamePopup.Visible && screen.City.TitleText.StartsWith(original, StringComparison.Ordinal)),
            new Capture("header", "The capital's city screen with the new Rename button in the header"),

            new Press("Rename"),
            new WaitFrames(2),
            new AssertQuery("the popup opened with the current name and OK is disabled (no change)", () =>
                screen.City.RenamePopup.Visible && screen.City.RenamePopup.Field.Text == original && !screen.City.RenamePopup.CanConfirm
                && screen.City.RenamePopup.ProblemText == "That is already the city's name"),
            new Act("type a name with markup characters", () => Type("[Ur]")),
            new AssertQuery("the popup refuses it and says why", () =>
                !screen.City.RenamePopup.CanConfirm && screen.City.RenamePopup.ProblemText.StartsWith("The name cannot contain", StringComparison.Ordinal)),
            new AssertQuery("the popup is the shared modal frame: centred, accent hairline, titled, buttons inside with the primary right-most", () => ModalChecks.Framed(screen.City.RenamePopup)),
            new Capture("refused", "A name with [ ] is refused: the reason is shown and OK is disabled"),
            new Act("clear the name", () => Type("   ")),
            new AssertQuery("an empty name is refused", () => !screen.City.RenamePopup.CanConfirm && screen.City.RenamePopup.ProblemText == "Enter a name"),
            new PressKey(Key.Enter),
            new AssertQuery("Enter on a refused name sends nothing and keeps the popup", () => screen.City.RenamePopup.Visible && session.LastSeq == seqBefore && city.Name == original),

            new PressKey(Key.Escape),
            new AssertQuery("Escape closes the popup but not the city screen; the name is unchanged", () =>
                !screen.City.RenamePopup.Visible && screen.IsCityPanelVisible && city.Name == original && session.LastSeq == seqBefore),

            new Press("Rename"),
            new Act("type a good name", () => Type("  " + NewName + "  ")),
            new AssertQuery("the popup accepts it", () => screen.City.RenamePopup.CanConfirm && screen.City.RenamePopup.ProblemText == ""),
            new Capture("typed", "A good name typed: no complaint, OK enabled"),
            new PressKey(Key.Enter),
            new WaitFrames(2),
            new AssertQuery("the engine renamed the city (trimmed) with one command, and the popup closed", () =>
                city.Name == NewName && session.LastSeq == seqBefore + 1 && !screen.City.RenamePopup.Visible),
            new AssertQuery("the city screen title shows the new name", () => screen.IsCityPanelVisible && screen.City.TitleText.StartsWith(NewName, StringComparison.Ordinal)),
            new Capture("renamed", "The city screen after renaming: the new name in the header"),

            new Press("Close city"),
            new WaitFrames(2),
            new AssertQuery("the banner on the map shows the new name", () =>
                screen.Overlays.Cities.Values.Any(it => it.City.Own?.Id == city.Id && it.BannerText.Contains(NewName, StringComparison.Ordinal))),
            new Act("open the overview's cities tab", () => screen.OpenOverviewScreen(OverviewModel.Cities)),
            new WaitFrames(2),
            new AssertQuery("the overview's city table lists the new name and not the old one", () =>
                screen.Overview.Tables(OverviewModel.Cities)[0].Rows.Any(it => it.Cells[0] == NewName)
                && screen.Overview.Tables(OverviewModel.Cities)[0].Rows.All(it => it.Cells[0] != original)),
            new Capture("overview", "The overview's cities tab lists the renamed city"),
        ];
    }
}
