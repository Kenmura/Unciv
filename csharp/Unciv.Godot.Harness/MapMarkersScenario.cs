using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Commands;
using Unciv.Harness;
using Unciv.Utils;
using Unciv.Logic.Map;

namespace Unciv.GodotHarness;

/// <summary>
/// C089: the player's private map markers on the real game screen (the solo game, no unit selected). A right-click on a tile opens the marker menu; "Place marker" opens the editor
/// where an icon and a label are picked; the pin and its label then stand on the tile. Ctrl+M edits the tile under the pointer, "Map markers" lists every marker and a list entry moves the camera
/// there, "Remove marker" takes one away. Markers are saved in the client settings under the game and the viewer: a second settings service on the same file reads them, another
/// game id does not show them. Nothing is sent to the game: the command log keeps its length (the runner's replay check then proves the state hash did not move).
/// </summary>
public sealed class MapMarkersScenario : Scenario
{
    public override string Name => "map-markers";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the pin read as a tack on its tile, with the label legible next to it?",
        "Is the right-click menu close to the pointer and clear about place, edit and remove?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-markers-scenario-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var registry = AssetRegistry.ForRuleset(game.Ruleset);
        var screen = new GameScreen();
        host.AddChild(screen);
        string originalId = game.GameId;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        screen.Start(localHost, registry);
        screen.TutorialsEnabled = false;
        screen.BindSettings(service);

        var human = game.GetCivilization(ScenarioGame.HumanA);
        var lands = human.Units.GetCivUnits().OrderBy(it => it.Id).First().CurrentTile.Neighbors.Where(it => it.IsLand).ToList();
        var tileA = lands[0].Position;
        var tileB = lands[^1].Position;
        string key = MapMarkerModel.Key(originalId, ScenarioGame.HumanA).NotNull();
        int logged = 0;

        MapMarkerView Editor() => screen.MarkerEditor.NotNull();
        Vector2 Pointer(HexCoord tile) => screen.Map.ScreenPositionOf(tile);
        void RightClick(HexCoord tile) => InputSimulator.SimulateMouseClick(screen.GetViewport(), Pointer(tile), MouseButton.Right);
        MapMarker? Stored(ClientSettingsService from, HexCoord tile) => MapMarkerModel.At(from.Current, key, tile.X, tile.Y);
        ClientSettingsService Reloaded() => new(new ClientSettingsStore(path));

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () =>
            {
                screen.Hud.HideBanner();
                logged = session.Log.Count;
            }),
            new AssertQuery("no marker yet, and the game has an id to key them by", () => screen.Markers.Count == 0 && screen.MarkerLayer.Shown.Count == 0 && originalId.Length > 0),
            new Act("right-click tile A", () => RightClick(tileA)),
            new AssertQuery("the marker menu is open, offering Place marker and the list but not Remove", () => screen.MarkerMenu.Visible && screen.MarkerMenu.EditButton.Text == "Place marker" && !screen.MarkerMenu.RemoveButton.Visible && screen.MarkerMenu.ListButton.Visible),
            new AssertQuery("the menu is on screen, at the pointer", () => new Rect2(Vector2.Zero, host.GetViewport().GetVisibleRect().Size).Encloses(screen.MarkerMenu.GetGlobalRect()) && screen.MarkerMenu.Position.DistanceTo(Pointer(tileA)) < 2f),
            new Capture("menu", "Right-click on a tile with no unit selected: a small menu at the pointer offers Place marker and Map markers"),
            new Press("Place marker"),
            new AssertQuery("the editor is open on tile A: form shown, plain pin, no Remove yet", () => Editor().Visible && Editor().FormShown && Editor().SelectedIcon == "Pin" && !Editor().RemoveButton.Visible && !screen.MarkerMenu.Visible),
            new Act("pick the star and type a label", () =>
            {
                Editor().IconChoice.Selected = MapMarkerModel.Icons.ToList().IndexOf("Star");
                Editor().Field.Text = "  Iron [here]  ";
            }),
            new Capture("editor", "The marker editor: tile, icon choice and label field, Save and Close; below it the list of the game's markers (empty)"),
            new Press("Save"),
            new WaitFrames(2),
            new AssertQuery("the tack stands on tile A with the cleaned label, and the editor is gone", () =>
                !Editor().Visible && screen.MarkerLayer.Shown.TryGetValue(tileA, out var shown) && shown.Icon == "Star" && screen.MarkerLayer.LabelAt(tileA) == "Iron here" && screen.MarkerLayer.PinAt(tileA) != null),
            new AssertQuery("the settings file holds it under the game and viewer", () => Stored(Reloaded(), tileA)?.Label == "Iron here" && Reloaded().Current.MapMarkers.Keys.SequenceEqual([key])),
            new Act("pointer onto tile B", () => InputSimulator.SimulateMouseMove(screen.GetViewport(), Pointer(tileB))),
            new WaitFrames(2),
            new AssertQuery("the screen knows the hovered tile", () => screen.HoveredTile == tileB),
            new PressKey(Key.M, Ctrl: true),
            new AssertQuery("Ctrl+M opened the editor on tile B", () => Editor().Visible && Editor().FormShown && Editor().ListedCount == 1),
            new Act("pick the danger icon and a label", () =>
            {
                Editor().IconChoice.Selected = MapMarkerModel.Icons.ToList().IndexOf("Danger");
                Editor().Field.Text = "Barbarians";
            }),
            new Press("Save"),
            new Act("pointer off both tiles", () => InputSimulator.SimulateMouseMove(screen.GetViewport(), new Vector2(60f, 240f))),
            new WaitFrames(2),
            new AssertQuery("both tacks stand on the map", () => screen.MarkerLayer.Shown.Count == 2 && screen.MarkerLayer.LabelAt(tileB) == "Barbarians" && screen.Markers.Select(it => it.Label).SequenceEqual(["Iron here", "Barbarians"])),
            new Capture("placed", "Two private tacks on the map: a yellow star labelled Iron here and an orange Danger pin labelled Barbarians"),
            new Act("right-click tile A again", () => RightClick(tileA)),
            new AssertQuery("the menu now offers Edit marker and Remove marker", () => screen.MarkerMenu.Visible && screen.MarkerMenu.EditButton.Text == "Edit marker" && screen.MarkerMenu.RemoveButton.Visible),
            new Press("Map markers"),
            new AssertQuery("the list opened without the form, listing both markers", () => Editor().Visible && !Editor().FormShown && Editor().ListedCount == 2 && Editor().GoButton(tileB) != null),
            new Capture("list", "The marker list: one entry per marker with icon, tile and label; pressing one moves the camera there"),
            new Act("press the entry of tile B", () => Editor().GoButton(tileB).NotNull().EmitSignal(BaseButton.SignalName.Pressed)),
            new WaitFrames(2),
            new AssertQuery("the list closed and tile B is now at the middle of the screen", () => !Editor().Visible && Pointer(tileB).DistanceTo(host.GetViewport().GetVisibleRect().Size / 2f) < 40f),
            new Act("right-click tile A and remove its marker", () => RightClick(tileA)),
            new Press("Remove marker"),
            new WaitFrames(2),
            new AssertQuery("tile A has no marker any more, tile B keeps its own, in the layer and in the file", () =>
                !screen.MarkerLayer.Shown.ContainsKey(tileA) && screen.MarkerLayer.Shown.ContainsKey(tileB) && Stored(Reloaded(), tileA) == null && Stored(Reloaded(), tileB)?.Label == "Barbarians"),
            new Act("another game is started on the screen (a different id)", () =>
            {
                game.GameId = "another-game";
                screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), registry);
            }),
            new AssertQuery("the other game shows none of these markers, and its own are separate", () => screen.Markers.Count == 0 && screen.MarkerLayer.Shown.Count == 0),
            new Act("back to the first game", () =>
            {
                game.GameId = originalId;
                screen.Start(new LocalHost(session, ScenarioGame.HumanA, () => 1000L), registry);
            }),
            new AssertQuery("its marker is back (read from the settings)", () => screen.MarkerLayer.Shown.Count == 1 && screen.MarkerLayer.LabelAt(tileB) == "Barbarians"),
            new AssertQuery("no marker action sent a command to the game", () => session.Log.Count == logged),
        ];
    }
}
