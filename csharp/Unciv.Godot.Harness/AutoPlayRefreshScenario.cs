using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T379: what an AutoPlay turn costs the screen, in the crowd game (ten AI civs). A run that skips animations used to redraw the whole screen (map, overlays, minimap, units) after
/// every turn: on the RTX 5060 Ti about 0.13 s of the 0.33 s a turn took (the engine's own turn is 0.19 s), more under software GL. Now it redraws every
/// <see cref="AutoPlayModel.SkipRefreshInterval"/> turns and once more when the run ends. The budget asserted here is a count, not a time (the harness reads no clock for assertions): over
/// <c>100</c> skip turns the screen is redrawn exactly as often as <see cref="AutoPlayModel.RefreshesAfter"/> says, a run of 7 turns ends with one extra redraw, a fast run redraws
/// after each turn, and in every case the unit nodes match the engine's units when the run is over. The wall time per turn is printed (report only; compare it with a build before T379
/// with <c>AUTOPLAY-REFRESH</c> lines from a real GPU).
/// </summary>
public sealed class AutoPlayRefreshScenario : Scenario
{
    public override string Name => "autoplay-refresh";

    public override bool Uses3D => true;

    public override bool Crowd => true;

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-autoplay-refresh-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var registry = AssetRegistry.ForRuleset(game.Ruleset);
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Presenter.Enabled = true;
        screen.Presenter.ManualStepping = true;
        screen.Start(localHost, registry);
        screen.TutorialsEnabled = false;
        screen.BindSettings(service);

        int startTurn = 0;
        int refreshesBefore = 0;
        ulong msBefore = 0;

        int Expected(AutoPlayAnimations mode, int turns)
        {
            int count = 0;
            for (int turn = 1; turn <= turns; turn++) count += AutoPlayModel.RefreshesAfter(turn, mode) ? 1 : 0;
            return count + (AutoPlayModel.RefreshesAfter(turns, mode) ? 0 : 1); // the end of the run redraws when the last turns were not drawn
        }

        ScenarioStep[] Run(AutoPlayAnimations mode, int turns) =>
        [
            new Act($"{mode}: {turns} turns", () =>
            {
                service.Update(s => s with { AutoPlay = s.AutoPlay with { ShowButton = true, MaxTurns = turns, Animations = mode.ToString() } });
                startTurn = game.Turns;
                refreshesBefore = screen.FullRefreshCount;
                msBefore = Time.GetTicksMsec();
                _ = screen.StartAutoPlayAsync(AutoPlayKind.Start);
            }),
            new WaitUntil($"{mode}: the run is over", () => screen.AutoPlay == null && game.Turns > startTurn, 600000),
            new WaitFrames(2),
            new Act($"{mode}: report", () => GD.Print($"AUTOPLAY-REFRESH {mode}: {turns} turns, {(Time.GetTicksMsec() - msBefore) / (double)turns:F0} ms per turn, {screen.FullRefreshCount - refreshesBefore} full redraws, adapter {RenderingServer.GetVideoAdapterName()}")),
            new AssertQuery($"{mode}: {turns} turns were played", () => game.Turns == startTurn + turns && screen.AutoPlayResult == $"AutoPlay finished after {turns} turns."),
            new AssertQuery($"{mode}: the screen was redrawn {Expected(mode, turns)} times, not once per turn", () => screen.FullRefreshCount - refreshesBefore == Expected(mode, turns)),
            new AssertQuery($"{mode}: the view matches the final state (every engine unit has its node on its tile, no other node)", () => ViewMatches(screen, localHost)),
        ];

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () => screen.Hud.HideBanner()),
            .. Run(AutoPlayAnimations.Skip, 100),
            .. Run(AutoPlayAnimations.Skip, 7),
            .. Run(AutoPlayAnimations.Fast, 6),
        ];
    }

    private static bool ViewMatches(GameScreen screen, LocalHost host)
    {
        var units = host.Snapshot.Units;
        return screen.Units.Count == units.Count && units.All(unit => screen.Units.TryGetValue(unit.Id, out var node) && node.Unit.X == unit.X && node.Unit.Y == unit.Y);
    }
}
