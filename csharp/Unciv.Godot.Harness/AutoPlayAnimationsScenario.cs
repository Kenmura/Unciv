using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Harness;

namespace Unciv.GodotHarness;

/// <summary>
/// T376: AutoPlay over the real game screen in the crowd game (ten AI civs), with the combat presenter on and stepped by the run itself (one sixtieth of a second per frame, so every
/// run is the same). Before the fix the presenter queued every auto-played turn's moves and fights behind the previous ones: over 80 turns the backlog grew to 38 script seconds and
/// was still being played after the run ended. Three runs: skip (the default; nothing is queued, no sound, no particles), fast (played 6x, whatever a turn leaves is dropped) and
/// normal (the next turn waits until the animations end). Each run asserts that when a turn's events arrive nothing of the earlier turns is still queued, that the speed is back to
/// normal and the queue empty afterwards, and that the unit nodes stand on the engine's tiles. The options screen shows the animations picker and a pick is stored.
/// </summary>
public sealed class AutoPlayAnimationsScenario : Scenario
{
    public override string Name => "autoplay-animations";

    public override bool Uses3D => true;

    public override bool Crowd => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Is the AutoPlay animations option easy to find in the options, and do Skip, Fast and Normal say what they do?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        var game = session.Game;
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-autoplay-animations-" + System.Environment.ProcessId, "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var registry = AssetRegistry.ForRuleset(game.Ruleset);
        var localHost = new LocalHost(session, ScenarioGame.HumanA, () => 1000L);
        var screen = new GameScreen();
        host.AddChild(screen);
        screen.Presenter.Enabled = true;
        screen.Presenter.ManualStepping = true;
        // subscribed before the screen, so it sees the queue as it was before this batch of events is presented
        int turnOfBatch = -1;
        float backlogAtTurnStart = 0f;
        float worstBacklogAtTurnStart = 0f;
        float queuedSeconds = 0f;
        localHost.EventsReceived += batch =>
        {
            float script = CombatPresentation.Plan(batch.Events).Sum(beat => beat.Count == 0 ? 0f : beat.Max(cue => cue.Delay + cue.Duration));
            queuedSeconds += script;
            int turn = screen.AutoPlay?.TurnsPlayed ?? -2;
            if (turn == turnOfBatch) return;
            turnOfBatch = turn;
            backlogAtTurnStart = screen.Presenter.PendingSeconds;
            worstBacklogAtTurnStart = Math.Max(worstBacklogAtTurnStart, backlogAtTurnStart);
        };
        screen.Start(localHost, registry);
        screen.TutorialsEnabled = false;
        screen.BindSettings(service);

        var ui = new UiTheme(registry);
        var backdrop = new Control { Name = "OptionsBackdrop", Theme = ui.Theme, Visible = false, Position = Vector2.Zero, Size = new Vector2(640, 480) };
        var screenOptions = new OptionsScreen(service, ui);
        screenOptions.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        backdrop.AddChild(screenOptions);
        host.AddChild(backdrop);
        var options = screenOptions.Panel;

        float maxPending = 0f;
        bool sawPending = false;
        int numbersBefore = 0;
        int soundsBefore = 0;
        int burstsBefore = 0;
        bool sampling = false;
        int startTurn = 0;

        async void Sample()
        {
            sampling = true;
            while (sampling)
            {
                maxPending = Math.Max(maxPending, screen.Presenter.PendingSeconds);
                sawPending |= screen.Presenter.PendingSeconds > 0f;
                await screen.ToSignal(screen.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        ScenarioStep[] Run(AutoPlayAnimations mode, int turns, bool expectAnimation) =>
        [
            new Act($"{mode}: {turns} turns", () =>
            {
                service.Update(s => s with { AutoPlay = s.AutoPlay with { ShowButton = true, MaxTurns = turns, Animations = mode.ToString() } });
                maxPending = 0f;
                sawPending = false;
                turnOfBatch = -1;
                backlogAtTurnStart = 0f;
                worstBacklogAtTurnStart = 0f;
                queuedSeconds = 0f;
                numbersBefore = screen.Presenter.NumbersShown.Count;
                soundsBefore = screen.Presenter.SoundsPlayed.Count;
                burstsBefore = screen.Presenter.BurstsStarted;
                startTurn = game.Turns;
                Sample();
                _ = screen.StartAutoPlayAsync(AutoPlayKind.Start);
            }),
            new WaitUntil($"{mode}: the run is over", () => screen.AutoPlay == null && game.Turns > startTurn, 20000),
            new Act("stop sampling", () => sampling = false),
            new WaitFrames(2),
            new Act($"{mode}: report", () => GD.Print($"AUTOPLAY-ANIMATIONS {mode}: {turns} turns, {queuedSeconds:F1} s of script queued, at most {maxPending:F1} s pending at once, worst backlog at a turn's start {worstBacklogAtTurnStart:F2} s")),
            new AssertQuery($"{mode}: {turns} turns were played", () => game.Turns == startTurn + turns && screen.AutoPlayResult == $"AutoPlay finished after {turns} turns."),
            new AssertQuery($"{mode}: nothing of an earlier turn was queued when a turn's events arrived", () => worstBacklogAtTurnStart <= 0.001f),
            new AssertQuery($"{mode}: the queue is empty and the speed is normal again", () => screen.Presenter.IsIdle && screen.Presenter.PendingSeconds == 0f && screen.Presenter.SpeedScale == 1f
                && screen.Presenter.DyingCount == 0 && screen.Presenter.EffectCount == 0 && screen.Presenter.BurstCount == 0),
            new AssertQuery($"{mode}: every unit stands on its tile (the view matches the final state)", () => screen.Units.Values.All(it => it.Position.DistanceTo(it.RestPosition) < 0.001f) && screen.Units.Count == localHost.Snapshot.Units.Count),
            expectAnimation
                ? new AssertQuery($"{mode}: the turns were animated", () => queuedSeconds > 0f && sawPending)
                : new AssertQuery($"{mode}: nothing was queued, no number, sound or particle", () => maxPending == 0f && !sawPending && screen.Presenter.NumbersShown.Count == numbersBefore
                    && screen.Presenter.SoundsPlayed.Count == soundsBefore && screen.Presenter.BurstsStarted == burstsBefore),
        ];

        return
        [
            new WaitFrames(3),
            new Act("dismiss the banner", () => screen.Hud.HideBanner()),
            new AssertQuery("the default is skip", () => service.Current.AutoPlay.AnimationMode == AutoPlayAnimations.Skip),
            .. Run(AutoPlayAnimations.Skip, 30, expectAnimation: false),
            .. Run(AutoPlayAnimations.Fast, 20, expectAnimation: true),
            .. Run(AutoPlayAnimations.Normal, 6, expectAnimation: true),

            .. StopAndOptions(screen, service, backdrop, options),
        ];
    }

    private static ScenarioStep[] StopAndOptions(GameScreen screen, ClientSettingsService service, Control backdrop, Unciv.GodotClient.Settings.SettingsPanel options) =>
    [
            new Act("a fast run, stopped after four turns", () =>
            {
                service.Update(s => s with { AutoPlay = s.AutoPlay with { MaxTurns = 50, Animations = nameof(AutoPlayAnimations.Fast) } });
                _ = screen.StartAutoPlayAsync(AutoPlayKind.Start);
            }),
            new WaitUntil("four turns were played", () => screen.AutoPlay is { TurnsPlayed: >= 4 }, 20000),
            new Act("stop", () => _ = screen.StopAutoPlayAsync()),
            new WaitUntil("stopped", () => screen.AutoPlay == null, 3000),
            new WaitFrames(3),
            new AssertQuery("stopping restored normal speed and left no backlog", () => screen.Presenter.SpeedScale == 1f && screen.Presenter.IsIdle && screen.Units.Values.All(it => it.Position.DistanceTo(it.RestPosition) < 0.001f)),

            new Act("open the options", () => backdrop.Visible = true),
            new WaitFrames(2),
            new Act("open the Gameplay tab and scroll to the animations picker", () => options.Reveal(options.AutoPlayAnimationsPicker)),
            new WaitFrames(3),
            new AssertQuery("the picker lists skip, fast and normal and shows the stored one (fast)", () => options.AutoPlayAnimationsPicker.ItemCount == 3 && options.AutoPlayAnimationsPicker.Selected == 1),
            new Act("pick skip", () => { options.AutoPlayAnimationsPicker.Select(0); options.AutoPlayAnimationsPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 0L); }),
            new WaitFrames(2),
            new AssertQuery("the pick is stored", () => service.Current.AutoPlay.AnimationMode == AutoPlayAnimations.Skip),
            new Capture("options", "Options, Gameplay tab, AutoPlay section: the AutoPlay animations picker set to Skip, between the turns slider and Full AutoPlay AI"),
    ];
}
