using Godot;
using Unciv.Commands;
using Unciv.Commands.Turn;
using Unciv.Commands.Units;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Utils;
using Unciv.Logic.Civilization;
using Unciv.Assets;

namespace Unciv.GodotHarness;

/// <summary>
/// C011: the HUD on the real game screen. Asserts that every top bar number equals the engine's own value (<c>CivInfoStatsForNextTurn</c>, gold, happiness, the
/// turn and year), that the turn banner is shown when the game starts and when a turn begins, and that the next-action button changes as the viewer's units are
/// ordered (Next unit while a unit is due, Next turn once none is) and with Next turn ends the turn. Plays the solo game (a settler and a warrior, no city).
/// </summary>
public sealed class HudScenario : Scenario
{
    public override string Name => "hud";

    public override bool Uses3D => true;

    public override bool Solo => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the top bar read like a Civ 5 bar: stat icons with numbers, turn and year on the right?",
        "Is the turn banner readable on top of the map, and is the next-action button where you would look for it?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        var human = game.GetCivilization(ScenarioGame.HumanA);
        long now = 1000;
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
        var hud = screen.Hud;
        hud.HoldBanner = true;
        int startTurn = game.Turns;

        string Signed(float value)
        {
            int rounded = (int)MathF.Round(value, MidpointRounding.AwayFromZero);
            return (rounded >= 0 ? "+" : "-") + Math.Abs(rounded).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        bool BarEqualsEngine()
        {
            var stats = human.Stats.StatsForNextTurn;
            return hud.StatText("Gold") == $"{human.Gold} ({Signed(stats.Gold)})"
                && hud.StatText("Science") == Signed(stats.Science)
                && hud.StatText("Culture") == Signed(stats.Culture)
                && hud.StatText("Faith") == Signed(stats.Faith)
                && hud.StatText("Happiness") == human.GetHappiness().ToString(System.Globalization.CultureInfo.InvariantCulture)
                && hud.StatText("Turn") == HudModel.TurnAndYear(game.Turns, game.GetYear(0));
        }

        // C067: every HUD element that shows has its own rectangle: none overlaps another, all lie on screen, and the middle of the map is uncovered
        string Layout() => LayoutOf(screen);

        string LayoutOf(GameScreen shown)
        {
            var screenRect = new Rect2(Vector2.Zero, host.GetViewport().GetVisibleRect().Size);
            var rects = shown.World.Rects().ToList();
            if (shown.Panel.IsVisibleInTree()) rects.Add(("UnitPanel", shown.Panel.GetGlobalRect()));
            foreach (var (name, rect) in rects)
                if (!screenRect.Encloses(rect)) return $"{name} {rect} leaves the screen {screenRect}";
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].Rect.Intersects(rects[j].Rect)) return $"{rects[i].Name} {rects[i].Rect} overlaps {rects[j].Name} {rects[j].Rect}";
            var middle = new Rect2(screenRect.Size * 0.4f, screenRect.Size * 0.2f);
            foreach (var (name, rect) in rects)
                if (rect.Intersects(middle)) return $"{name} {rect} covers the middle of the map {middle}";
            if (MathF.Abs(shown.Hud.BarRect.Size.Y - shown.Hud.TopBarHeight) > 0.5f) return $"the bar is {shown.Hud.BarRect.Size.Y} high, not the {shown.Hud.TopBarHeight} the screens below it reckon with";
            if (shown.Minimap.Size.X < 140f || shown.Minimap.Size.Y < 100f) return $"the minimap is only {shown.Minimap.Size}";
            if (shown.Hud.Entries.Values.Any(it => !shown.Hud.BarRect.Encloses(it.GetGlobalRect()))) return "a top bar entry is clipped by the bar";
            return "";
        }

        bool Report(string problem)
        {
            if (problem.Length != 0) GD.PrintErr("[hud] " + problem);
            return problem.Length == 0;
        }

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(3),
            new AssertQuery("the top bar's numbers equal the engine's stats for next turn, gold, happiness, turn and year", BarEqualsEngine),
            new AssertQuery("the top bar sits along the top edge and spans the screen", () => hud.BarRect.Position.Y <= 0.5f && hud.BarRect.Size.X >= host.GetViewport().GetVisibleRect().Size.X - 1f),
            new AssertQuery("the turn banner is up and names the turn and year", () => hud.BannerVisible && hud.BannerText.Contains("Turn " + startTurn, StringComparison.Ordinal) && hud.BannerText.Contains(HudModel.YearText(game.GetYear(0)), StringComparison.Ordinal)),
            new AssertQuery("with units waiting the button says Next unit and a separate Next turn is offered", () => hud.ActionText == "Next unit" && hud.Action.Kind == HudActionKind.NextUnit && hud.EndTurnShown),
            new Capture("start", "Turn start: top bar with the empire's numbers, turn banner, Next unit button with a separate Next turn"),
            new Act("hide the banner", hud.HideBanner),
            new WaitFrames(2),
            new AssertQuery("at 640x480 (Compact) no HUD rectangle overlaps another, all are on screen, the map's middle is free and the minimap is at least 140x100", () => Report(Layout())),
            new Press("Next unit"),
            new AssertQuery("Next unit selected one of the viewer's units", () => screen.SelectedUnitId is { } id && human.Units.GetCivUnits().Any(it => it.Id == id)),
        };

        // Order every unit (skip it for this turn) one command at a time: the button must stay Next unit until the last one is ordered.
        var units = human.Units.GetCivUnits().OrderBy(it => it.Id).ToList();
        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            bool last = i == units.Count - 1;
            steps.Add(new Act($"order unit {unit.Id} ({unit.BaseUnit.Name}) to skip its turn", () =>
                localHost.SubmitAsync(new SetUnitDue { UnitId = unit.Id, Due = false }).GetAwaiter().GetResult()));
            steps.Add(new WaitFrames(1));
            steps.Add(last
                ? new AssertQuery("after the last unit is ordered the engine's next pending decision, the start alert, names the button", () => hud.ActionText == "Review alert" && hud.EndTurnShown)
                : new AssertQuery("units still waiting: the button still says Next unit", () => hud.ActionText == "Next unit"));
        }
        steps.AddRange(
        [
            new Act("acknowledge the start alert", () => localHost.SubmitAsync(new AckAlert { Index = 0, AlertType = AlertType.StartIntro }).GetAwaiter().GetResult()),
            new WaitFrames(1),
            new AssertQuery("with nothing left to decide the button says Next turn and the extra button is gone", () => hud.ActionText == HudModel.EndTurnLabel && hud.Action.Kind == HudActionKind.EndTurn && !hud.EndTurnShown),
            new Capture("ordered", "All units ordered: the button now reads Next turn"),
            new Press("Next turn"),
            new WaitFrames(1),
            new AssertQuery("the turn advanced", () => game.Turns == startTurn + 1 && screen.Turn == startTurn + 1),
            new AssertQuery("the banner announces the new turn", () => hud.BannerVisible && hud.BannerText.Contains("Turn " + (startTurn + 1), StringComparison.Ordinal)),
            new AssertQuery("the top bar follows the engine on the new turn", BarEqualsEngine),
            new AssertQuery("the new turn's units are due again: Next unit", () => hud.ActionText == "Next unit"),
            new Capture("next-turn", "Next turn: banner for the new turn, top bar updated, units due again"),
            new Act("widen the window to 1280x720", () => host.GetWindow().Size = new Vector2I(1280, 720)),
            new WaitFrames(4),
            new AssertQuery("the layout follows: at 1280x720 the breakpoint is Regular, still no overlap, minimap 200x140", () =>
                Report(Layout()) && Report(screen.World.MinimapSize == new Vector2(200f, 140f) && screen.Minimap.Size == new Vector2(200f, 140f) ? "" : $"minimap {screen.World.MinimapSize} {screen.Minimap.Size} {screen.Theme?.DefaultFontSize}")),
            new Capture("wide", "1280x720: top bar, research and policy pills, nav strip, minimap and next-action dock; same structure as at 640x480"),
            LayoutChecks.Assert("at 1280x720 no HUD text is cut off, leaves the window or overlaps another", host, () => screen),
            new Act("widen the window to 1920x1080", () => host.GetWindow().Size = new Vector2I(1920, 1080)),
            new WaitFrames(4),
            new AssertQuery("at 1920x1080 still no overlap, minimap grows no smaller", () => Report(Layout())),
            LayoutChecks.Assert("at 1920x1080 no HUD text is cut off, leaves the window or overlaps another", host, () => screen),
            new Capture("fhd", "1920x1080: the same HUD at full HD, every element in its own place and the middle of the map free"),
            new Act("back to 640x480", () => host.GetWindow().Size = new Vector2I(640, 480)),
            new WaitFrames(4),
            .. Localized("German", "de", "German at 640x480: the longer words of the bar, the nav tooltips' targets, the banner and the buttons fit without clipping or overlap"),
            .. Localized("Russian", "ru", "Russian at 640x480: Cyrillic text in the bar, the banner and the buttons, every element in its own place"),
        ]);
        return steps;

        // C079: the HUD again in a long-string language: a fresh game screen (text is built when the screen is) over the same session, checked with the same rectangle asserts
        IEnumerable<ScenarioStep> Localized(string language, string step, string caption)
        {
            GameScreen? built = null;
            return
            [
                LocaleSteps.Use(language),
                new Act("rebuild the game screen in " + language, () =>
                {
                    foreach (var old in host.GetChildren().OfType<GameScreen>().ToList())
                    {
                        host.RemoveChild(old);
                        old.QueueFree();
                    }
                    built = new GameScreen();
                    host.AddChild(built);
                    built.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
                    built.Hud.HoldBanner = true;
                }),
                new WaitFrames(4),
                new AssertQuery($"{language}: no HUD rectangle overlaps another or leaves the screen", () => Report(LayoutOf(built.NotNull()))),
                LayoutChecks.Assert($"{language}: no HUD text is cut off, leaves the window or overlaps another", host, () => built.NotNull()),
                new Capture(step, caption),
            ];
        }
    }
}
