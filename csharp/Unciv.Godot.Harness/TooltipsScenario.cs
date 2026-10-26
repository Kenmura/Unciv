using Godot;
using Unciv.Assets;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Commands;
using Unciv.Harness;
using Unciv.Models.Translations;
using Unciv.Queries.Tiles;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C027: tooltips and the Civilopedia through the real game screen and the real engine. Hovering the capital's tile shows the engine's description of it; hovering a widget takes the tip
/// away; the Civilopedia lists exactly the ruleset's visible entries, a unit's page is the engine's assembled civilopedia text, an entry's tooltip is the ruleset's own short description,
/// a link opens the linked entry and Back returns. Nothing is sent to the game.
/// </summary>
public sealed class TooltipsScenario : Scenario
{
    public override string Name => "tooltips";

    public override bool Uses3D => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the tile tooltip say what you need to know about the tile (terrain, resource, owner, yields) without clutter?",
        "Is the Civilopedia easy to browse: categories, entry list, page, links?",
        "Is the unit tooltip in the entry list readable at a glance?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var ruleset = game.Ruleset;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        var recording = new CityAScenario.RecordingSession(new LocalHost(session, ScenarioGame.HumanA, () => 1000L));
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(recording, AssetRegistry.ForRuleset(game.Ruleset));
        var viewport = host.GetViewport();
        int logged = 0;

        var capital = human.Cities.First();
        var capitalTile = capital.GetCenterTile();
        var capitalCoord = capitalTile.Position;
        Vector2 Centre(Button? button) => button?.GetGlobalRect().GetCenter() ?? Vector2.Zero;
        Vector2 Category(CivilopediaCategories category) => Centre(screen.Pedia.CategoryButton(category));
        Vector2 Entry(string name) => Centre(screen.Pedia.EntryButton(name));
        // The engine's own tile description, independent of the screen
        string ExpectedTileTip() => TooltipModel.ToBbcode(TooltipModel.Tile(recording.QueryAsync(new TileInfoQuery(capitalCoord.X, capitalCoord.Y)).GetAwaiter().GetResult().NotNull(), ruleset), text => text.Tr());
        // The page as BBCode built from the model's lines, and (independently) the engine's own line texts
        string ExpectedPage(CivilopediaCategories category, string name) =>
            string.Join('\n', PediaModel.Page(ruleset, category, name).NotNull().Lines.Select(line => PediaModel.ToBbcode(line, text => text.Tr())));
        bool PageIsEngineText(CivilopediaCategories category, string name)
        {
            var entry = category.GetCategoryIterator(ruleset, null).Single(it => ((Unciv.Models.StatTypes.INamed)it).Name == name);
            var engine = entry.AssembleCivilopediaText(new CivilopediaContext(ruleset, null, StringComparer.Ordinal)).CivilopediaText;
            return engine.Select(it => it.Text).SequenceEqual(PediaModel.Page(ruleset, category, name).NotNull().Lines.Select(it => it.Text));
        }
        string archerLink = PediaModel.Page(ruleset, CivilopediaCategories.Unit, "Archer").NotNull().Lines.First(it => it.Link.StartsWith("Technology/", StringComparison.Ordinal)).Link;

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new Act("pointer onto the capital's tile", () => InputSimulator.SimulateMouseMove(viewport, screen.Map.ScreenPositionOf(capitalCoord))),
            new WaitFrames(4),
            new AssertQuery("the map follows the pointer: the capital tile is hovered and its tip is loaded, not yet shown", () =>
                screen.HoveredTile == capitalCoord && screen.HoverTip.Length > 0 && !screen.Tooltips.IsShowing),
            new Act("let the delay elapse", () => screen.Tooltips.Advance(1.0)),
            new WaitFrames(2),
            new AssertQuery("the tooltip is the engine's description of the tile: city, terrain and yields", () =>
            {
                var info = recording.QueryAsync(new TileInfoQuery(capitalCoord.X, capitalCoord.Y)).GetAwaiter().GetResult().NotNull();
                return screen.Tooltips.IsShowing && screen.Tooltips.ShownText == ExpectedTileTip() && info.IsCityCenter && info.OwningCity == capital.Name
                    && screen.Tooltips.ShownText.StartsWith(capital.Name, StringComparison.Ordinal) && info.Yields.Count > 0 && info.BaseTerrain == capitalTile.BaseTerrain;
            }),
            new Capture("tile", "Tile tooltip beside the pointer: the capital, its terrain, owner and yields"),
            new Act("pointer onto the top bar", () => InputSimulator.SimulateMouseMove(viewport, new Vector2(300, 8))),
            new WaitFrames(3),
            new AssertQuery("over the HUD the map tip goes away", () => screen.HoveredTile == null && !screen.Tooltips.IsShowing && screen.HoverTip.Length == 0),

            new Act("remember the command log", () => logged = session.Log.Count),
            new Click(() => Centre(screen.PediaButton)),
            new WaitFrames(3),
            new AssertQuery("the Civilopedia opens on the first category with the engine's visible entries", () =>
                screen.IsPediaVisible && screen.Pedia.Category == PediaModel.Categories(ruleset)[0].Category
                && screen.Pedia.CategoryKeys.SequenceEqual(PediaModel.Categories(ruleset).Select(it => it.Category))
                && screen.Pedia.Entries.Select(it => it.Name).SequenceEqual(PediaModel.Entries(ruleset, screen.Pedia.Category.GetValueOrDefault()).Select(it => it.Name))),
            new Click(() => Category(CivilopediaCategories.Unit)),
            new WaitFrames(3),
            new AssertQuery("the Units category lists every unit the ruleset does not hide", () =>
                screen.Pedia.Category == CivilopediaCategories.Unit && screen.Pedia.Entries.Count > 10 && screen.Pedia.Entries.All(it => ruleset.Units.ContainsKey(it.Name))
                && screen.Pedia.Entries.Count == ruleset.Units.Values.Count(it => !((Unciv.Models.Ruleset.Unique.IHasUniques)it).IsHiddenFromCivilopedia(null, ruleset))),
            new Click(() => Entry("Archer")),
            new WaitFrames(3),
            new AssertQuery("the Archer's page is the engine's assembled civilopedia text", () =>
                screen.Pedia.Current?.Link == "Unit/Archer" && screen.Pedia.PageBbcode == ExpectedPage(CivilopediaCategories.Unit, "Archer") && PageIsEngineText(CivilopediaCategories.Unit, "Archer") && screen.Pedia.PageText.Contains("Archer", StringComparison.Ordinal)),
            new Capture("unit", "Civilopedia, Units: the Archer's page as the engine words it, with its stats and links"),
            new Act("pointer onto the Bowman entry", () => InputSimulator.SimulateMouseMove(viewport, Entry("Bowman"))),
            new WaitFrames(2),
            new Act("let the delay elapse", () => screen.Tooltips.Advance(1.0)),
            new WaitFrames(2),
            new AssertQuery("the entry's tooltip is the ruleset's own short description with name and cost", () =>
            {
                var warrior = ruleset.Units["Bowman"];
                string description = BaseUnitDescriptions.GetShortDescription(warrior);
                return screen.Tooltips.IsShowing && screen.Tooltips.ShownText == TooltipModel.ToBbcode(TooltipModel.Unit(ruleset, "Bowman").NotNull(), text => text.Tr())
                    && screen.Tooltips.ShownText.Contains(TooltipModel.Escape(description), StringComparison.Ordinal) && screen.Tooltips.ShownText.StartsWith("Bowman", StringComparison.Ordinal);
            }),
            new Capture("entry-tip", "The Bowman entry's tooltip: name, cost and the ruleset's short description"),
            new Act("pointer away", () => InputSimulator.SimulateMouseMove(viewport, new Vector2(600, 460))),
            new WaitFrames(2),

            new Act("follow the Archer's technology link", () => screen.Pedia.OpenLink(archerLink)),
            new WaitFrames(3),
            new AssertQuery("the link opened the technology's entry and Back is available", () =>
                screen.Pedia.Category == CivilopediaCategories.Technology && screen.Pedia.Current?.Link == archerLink && screen.Pedia.CanGoBack),
            new Press("Back"),
            new WaitFrames(3),
            new AssertQuery("Back returned to the Archer", () => screen.Pedia.Current?.Link == "Unit/Archer" && screen.Pedia.Category == CivilopediaCategories.Unit && !screen.Pedia.CanGoBack),
            new Click(() => Category(CivilopediaCategories.Building)),
            new WaitFrames(3),
            new Click(() => Entry("Granary")),
            new WaitFrames(3),
            new AssertQuery("the Granary's page is the engine's assembled text", () =>
                screen.Pedia.Current?.Link == "Building/Granary" && screen.Pedia.PageBbcode == ExpectedPage(CivilopediaCategories.Building, "Granary") && PageIsEngineText(CivilopediaCategories.Building, "Granary")),
            new Capture("building", "Civilopedia, Buildings: the Granary's page"),
            new PressKey(Key.Escape),
            new WaitFrames(2),
            new AssertQuery("Escape closes the Civilopedia and nothing was sent to the game", () => !screen.IsPediaVisible && session.Log.Count == logged),
        ];
    }
}
