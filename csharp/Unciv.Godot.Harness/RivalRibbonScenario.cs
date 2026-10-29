using Godot;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Queries.Empire;
using Unciv.Assets;
using Unciv.Queries.Victory;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C087: the rival stats ribbon on the real game screen, in the diplomacy game (Rome and a city-state are met, Greece is not). The ribbon starts collapsed to its header and
/// lists exactly one rival, Rome: never the city-state and never the unmet Greece. Opened, Rome's science, culture, gold and military figures equal the engine's Rankings page values,
/// the luxury tooltip equals the engine's trade list, and no HUD rectangle overlaps another at 640x480 and at 1280x720 (the ribbon included, and clear of the map's middle).
/// </summary>
public sealed class RivalRibbonScenario : Scenario
{
    public override string Name => "rival-ribbon";

    public override bool Uses3D => true;

    public override bool Diplomacy => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the collapsed header unobtrusive, and does opening it read as Rome's stats rather than yours?",
        "Are the four figures easy to tell apart (science, culture, gold, military)?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var rome = game.GetCivilization(ScenarioGame.Ai);
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var ribbon = screen.World.Rivals;

        // the Rankings page's own answer (this turn's snapshot for another civ, never live)
        string Figure(RankingType type) => RivalRibbonModel.Compact(localHost.QueryAsync(new RankingsQuery()).GetAwaiter().GetResult().NotNull()
            .Single(it => it.Type == type.ToString()).Entries.Single(it => it.Civ.CivName == rome.CivName).Value);
        bool FiguresEqualEngine() =>
            ribbon.FigureText(rome.CivName, "Science") == Figure(RankingType.Technologies)
            && ribbon.FigureText(rome.CivName, "Culture") == Figure(RankingType.Culture)
            && ribbon.FigureText(rome.CivName, "Gold") == Figure(RankingType.Gold)
            && ribbon.FigureText(rome.CivName, "Military") == Figure(RankingType.Force);
        bool LuxuriesEqualEngine()
        {
            var options = localHost.QueryAsync(new TradeOptionsQuery(rome.CivName)).GetAwaiter().GetResult();
            var luxuries = options == null ? [] : RivalRibbonModel.Luxuries(options);
            string expected = luxuries.Count == 0 ? "No tradable luxuries" : "Tradable luxuries: " + string.Join(", ", luxuries);
            return ribbon.LuxuryTooltip(rome.CivName) == expected;
        }

        // every HUD element that shows (the ribbon among them) has its own rectangle: none overlaps another, all lie on screen, the map's middle is uncovered
        string Layout()
        {
            var screenRect = new Rect2(Vector2.Zero, host.GetViewport().GetVisibleRect().Size);
            var rects = screen.World.Rects().ToList();
            if (screen.Panel.IsVisibleInTree()) rects.Add(("UnitPanel", screen.Panel.GetGlobalRect()));
            if (rects.All(it => it.Name != "Rivals")) return "the ribbon is not among the HUD rectangles";
            foreach (var (name, rect) in rects)
                if (!screenRect.Encloses(rect)) return $"{name} {rect} leaves the screen {screenRect}";
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].Rect.Intersects(rects[j].Rect)) return $"{rects[i].Name} {rects[i].Rect} overlaps {rects[j].Name} {rects[j].Rect}";
            var middle = new Rect2(screenRect.Size * 0.4f, screenRect.Size * 0.2f);
            foreach (var (name, rect) in rects)
                if (rect.Intersects(middle)) return $"{name} {rect} covers the middle of the map {middle}";
            var ribbonRect = ribbon.GetGlobalRect();
            if (ribbon.FigureControls(rome.CivName)?.Values.Any(it => !ribbonRect.Encloses(it.GetGlobalRect())) == true) return "a figure is clipped by the ribbon";
            return "";
        }

        bool Report(string problem)
        {
            if (problem.Length != 0) GD.PrintErr("[rival-ribbon] " + problem);
            return problem.Length == 0;
        }

        return
        [
            new WaitFrames(3),
            new Act("dismiss the start banner", () => screen.Hud.HideBanner()),
            new WaitFrames(2),
            new AssertQuery("the ribbon shows, collapsed, with one rival: Rome (not the city-state, not the unmet Greece)", () =>
                ribbon.Visible && !ribbon.Expanded && ribbon.Rows.Select(it => it.CivName).SequenceEqual([rome.CivName]) && ribbon.Toggle.Text.Contains("Rivals 1", StringComparison.Ordinal)),
            new AssertQuery("collapsed, no rival block shows and the header overlaps nothing", () => !ribbon.Blocks.Any() && Report(Layout())),
            new Capture("collapsed", "Collapsed ribbon: a small Rivals 1 header under the research pill, the rest of the map free"),
            new Click(() => ribbon.Toggle.GetGlobalRect().GetCenter()),
            new WaitUntil("the ribbon opens and shows Rome's figures", () => ribbon.Expanded && ribbon.FigureText(rome.CivName, "Gold") != null),
            new WaitFrames(2),
            new AssertQuery("Rome's science, culture, gold and military equal the engine's Rankings page values", FiguresEqualEngine),
            new AssertQuery("the luxury tooltip equals the engine's trade list for Rome", LuxuriesEqualEngine),
            new AssertQuery("at 640x480 (Compact) the open ribbon overlaps no HUD rectangle, stays on screen and clear of the map's middle", () => Report(Layout())),
            new Capture("open", "Open ribbon at 640x480: Rome with science, culture, gold and military in a two-by-two grid"),
            new Act("widen the window to 1280x720", () => host.GetWindow().Size = new Vector2I(1280, 720)),
            new WaitFrames(4),
            new AssertQuery("at 1280x720 (Regular) the four figures sit in one row and nothing overlaps", () => Report(Layout()) && ribbon.Blocks.Any()),
            new Capture("wide", "1280x720: the same ribbon with the four figures in one row"),
            new Click(() => ribbon.Toggle.GetGlobalRect().GetCenter()),
            new WaitFrames(2),
            new AssertQuery("pressing the header again collapses it", () => !ribbon.Expanded && !ribbon.Blocks.Any()),
        ];
    }
}
