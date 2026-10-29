using Godot;
using Unciv.Assets;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Map;
using Unciv.GodotClient.Play;
using Unciv.Harness;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C033: unit animation and combat presentation on the real game screen, in the skirmish game, with the presenter stepped by hand (so every frame is the same on every run).
/// Part 1 makes a real attack through the UI: the engine's UnitAttacked event is queued, the lunge and the floating damage numbers are captured mid-way, the queue then drains,
/// the numbers shown equal the event (and the health the engine lost), and every unit is back at rest. Part 2 feeds a ranged shot, a city bombard and a unit's death through the
/// same screen entry point and asserts the projectile, the flash and the numbers, that the dying unit leaves the screen's unit table at once and its node is freed when the
/// death ends.
/// </summary>
public sealed class CombatPresentationScenario : Scenario
{
    public override string Name => "combat-presentation";

    public override bool Uses3D => true;

    public override bool Skirmish => true;

    public override IReadOnlyList<string> Checklist =>
    [
        "Does the attacker step in, turn toward its target and swing (attack clip), and does the struck unit react (hit clip), with a red damage number rising over it?",
        "Does a ranged attacker draw before the projectile leaves, and is the projectile easy to follow, and does the bombard flash read as a city firing?",
        "Do dying units shrink away instead of vanishing, and does the pace of a fight (about a second each) feel right?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session) => new Plan(host, session).Steps();

    private sealed class Plan
    {
        private readonly GameSession session;
        private readonly GameScreen screen;
        private readonly Unciv.Logic.Civilization.Civilization human;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit warrior;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit worker;
        private readonly Unciv.Logic.Map.MapUnits.MapUnit enemy;
        private readonly HexCoord warriorTile;
        private readonly HexCoord enemyTile;
        private readonly HexCoord workerTile;
        private readonly HexCoord farTile;
        private readonly int enemyHealthBefore;
        private readonly int warriorHealthBefore;
        private readonly List<UnitAttacked> attacks = [];
        private UnitNode? workerNode;
        private long seqBefore;
        private int numbersBefore;

        public Plan(Control host, GameSession session)
        {
            this.session = session;
            var game = session.Game;
            human = game.GetCivilization(ScenarioGame.HumanA);
            var ai = game.GetCivilization(ScenarioGame.Ai);
            long now = 1000;
            var localHost = new LocalHost(session, ScenarioGame.HumanA, () => now += 1000);
            localHost.EventsReceived += batch => attacks.AddRange(batch.Events.OfType<UnitAttacked>());
            screen = new GameScreen();
            host.AddChild(screen);
            screen.Presenter.Enabled = true;
            screen.Presenter.ManualStepping = true;
            screen.Start(localHost, AssetRegistry.ForRuleset(game.Ruleset));
            warrior = human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior");
            worker = human.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Worker");
            enemy = ai.Units.GetCivUnits().First(it => it.BaseUnit.Name == "Warrior" && it.CurrentTile.AerialDistanceTo(warrior.CurrentTile) == 1);
            warriorTile = warrior.CurrentTile.Position;
            enemyTile = enemy.CurrentTile.Position;
            workerTile = worker.CurrentTile.Position;
            farTile = HexMath.GetHexCoordsAtDistance(warriorTile, 2, 2, false).First(it => !it.Equals(enemyTile) && !it.Equals(workerTile));
            enemyHealthBefore = enemy.Health;
            warriorHealthBefore = warrior.Health;
        }

        // a little before the swing connects, so the attacker has stepped in and nobody is hit yet
        private const float StepBeforeHit = CombatPresentation.StrikeSeconds * 0.4f;

        private CombatPresenter Presenter => screen.Presenter;

        private Vector3 Top(HexCoord at) => MapLayout.TileTop(at.x, at.y, screen.Map.TileNodeAt(at)?.Elevation ?? 1f);

        private bool AllAtRest() => screen.Units.Values.All(it => it.Position.IsEqualApprox(it.RestPosition) && it.Scale.IsEqualApprox(Vector3.One * (it.Unit.Id == screen.SelectedUnitId ? 1.3f : 1f)));

        private void Frame3D(HexCoord at, float zoom)
        {
            screen.Map.FocusOn(Top(at) + new Vector3(-1.6f, 0f, 0f)); // the unit panel covers the left of the screen: look left of the fight so it appears to the right
            screen.Map.SetZoom(zoom);
        }

        // A click on a tile selects the next own unit there (the settler may share the warrior's tile): as many clicks as it takes to reach the warrior
        private IEnumerable<ScenarioStep> SelectWarrior()
        {
            var here = human.Units.GetCivUnits().Where(it => it.CurrentTile.Position == warriorTile).OrderBy(it => it.Id).Select(it => it.Id).ToList();
            return Enumerable.Range(0, here.IndexOf(warrior.Id) + 1).Select(_ => (ScenarioStep)new Click(() => screen.Map.ScreenPositionOf(warriorTile)));
        }

        public List<ScenarioStep> Steps() =>
        [
            new WaitFrames(3),
            new AssertQuery("the presenter is on, stepped by hand, and has nothing to play", () => Presenter.Enabled && Presenter.ManualStepping && Presenter.IsIdle && Presenter.NumbersShown.Count == 0),
            .. SelectWarrior(),
            new WaitFrames(2),
            new AssertQuery("the warrior is selected and the enemy tile is attackable", () => screen.SelectedUnitId == warrior.Id && screen.AttackOverlay.Tiles.SequenceEqual([enemyTile])),
            new Click(() => screen.Map.ScreenPositionOf(enemyTile)),
            new WaitFrames(2),
            new AssertQuery("the combat preview is open for the enemy", () => screen.CombatPreview.Visible && screen.CombatPreview.ConfirmEnabled),
            new Act("remember the log position", () => seqBefore = session.LastSeq),
            new Press("Attack"),
            new WaitFrames(1),
            new AssertQuery("one attack was logged, the engine reported one fight, and the presenter has it queued", () =>
                session.LastSeq == seqBefore + 1 && attacks.Count == 1 && !Presenter.IsIdle && Presenter.NumbersShown.Count == 0),
            new AssertQuery("the event's damage equals the health the engine took (both sides) and the units are not hit before the script reaches the impact", () =>
                attacks.Count == 1 && attacks[0].DamageToDefender == enemyHealthBefore - enemy.Health && attacks[0].DamageToAttacker == warriorHealthBefore - warrior.Health
                && attacks[0].DamageToDefender > 0 && screen.Units[warrior.Id].Position.IsEqualApprox(screen.Units[warrior.Id].RestPosition)),
            new Act("frame the fight and step to the middle of the lunge", () =>
            {
                Frame3D(warriorTile, 5f);
                Presenter.Step(StepBeforeHit);
            }),
            new WaitFrames(2),
            new AssertQuery("the attacker has stepped toward the target and the target has not moved or been hit", () =>
                screen.Units.ContainsKey(warrior.Id) && (screen.Units[warrior.Id].Position - screen.Units[warrior.Id].RestPosition).Length() > 0.1f
                && screen.Units[enemy.Id].Position.IsEqualApprox(screen.Units[enemy.Id].RestPosition) && Presenter.NumbersShown.Count == 0),
            new AssertQuery("the attacker plays its attack clip, part-way through, and faces the enemy; the enemy is still idle", () =>
                screen.Units[warrior.Id].CurrentClip == UnitModelContract.AttackClip && screen.Units[warrior.Id].ClipPosition > 0f
                && Mathf.IsEqualApprox(screen.Units[warrior.Id].FacingYaw, CuePoses.FacingYaw((Top(enemyTile) - Top(warriorTile)).X, (Top(enemyTile) - Top(warriorTile)).Z) ?? float.NaN)
                && screen.Units[enemy.Id].CurrentClip == UnitModelContract.IdleClip),
            new Capture("lunge", "Mid-swing: our warrior has stepped in, turned to the enemy warrior and plays its attack clip"),
            new Act("step into the impact", () =>
            {
                Presenter.Step(CombatPresentation.StrikeHitDelay - StepBeforeHit + 0.15f);
            }),
            new WaitFrames(2),
            new AssertQuery("the defender plays its hit clip and the engine's damage floats over it", () =>
                attacks.Count == 1 && Presenter.NumbersShown.Count >= 1 && Presenter.NumbersShown[0] == (attacks[0].DamageToDefender, enemyTile) && Presenter.EffectCount >= 1
                && screen.Units[enemy.Id].CurrentClip == UnitModelContract.HitClip && screen.Units[enemy.Id].ClipPosition > 0f),
            new AssertQuery("the attacker is still in its attack clip and has no damage number yet (counter damage waits for the end of its swing)", () =>
                screen.Units[warrior.Id].CurrentClip == UnitModelContract.AttackClip && Presenter.NumbersShown.Count == 1),
            new Capture("impact", "Impact: the enemy plays its hit clip and its damage number floats up"),
            new Act("play the rest of the script", () => Presenter.Step(10f)),
            new WaitFrames(2),
            new AssertQuery("the queue drained, only the engine's numbers were shown, no effect is left, and every unit is back at rest", () =>
            {
                if (attacks.Count != 1) return false;
                var expected = new List<(int, HexCoord)> { (attacks[0].DamageToDefender, enemyTile) };
                if (attacks[0].DamageToAttacker > 0) expected.Add((attacks[0].DamageToAttacker, warriorTile));
                return Presenter.IsIdle && Presenter.NumbersShown.SequenceEqual(expected) && Presenter.EffectCount == 0 && Presenter.DyingCount == 0 && AllAtRest();
            }),

            // Part 2: a ranged shot, a bombard and a death through the same entry point, as events a real ranged unit, a city and a lost unit would send
            new Act("queue a shot at a far tile, a bombard onto our warrior and the worker's death", () =>
            {
                workerNode = screen.Units[worker.Id];
                numbersBefore = Presenter.NumbersShown.Count;
                var shot = new UnitAttacked(ScenarioGame.HumanA, warrior.Id, null, warriorTile, ScenarioGame.Ai, null, "Cairo", farTile, 11, 0, false, false);
                var bombard = new UnitAttacked(ScenarioGame.Ai, null, "Rome", enemyTile, ScenarioGame.HumanA, warrior.Id, null, warriorTile, 8, 0, false, false);
                var lost = new UnitDestroyed(ScenarioGame.HumanA, worker.Id, "Worker", workerTile);
                screen.PresentEvents([shot, bombard, lost]);
                Frame3D(warriorTile, 6f);
                Presenter.Step(CombatPresentation.ShotReleaseSeconds / 2f);
            }),
            new WaitFrames(2),
            new AssertQuery("the archer draws (attack_ranged) and no projectile has left yet", () =>
                screen.Units[warrior.Id].CurrentClip == UnitModelContract.RangedAttackClip && Presenter.EffectCount == 0),
            new Act("step past the release marker to the middle of the flight", () => Presenter.Step(CombatPresentation.ShotReleaseSeconds / 2f + CombatPresentation.ShotSeconds / 2f)),
            new WaitFrames(2),
            new AssertQuery("the dying worker left the unit table at once but is still on screen, and a projectile is in flight", () =>
                !screen.Units.ContainsKey(worker.Id) && Presenter.DyingCount == 1 && workerNode.NotNull().GetParent() == Presenter && Presenter.EffectCount == 1),
            new Capture("shot", "A ranged shot in flight from our warrior toward a far tile"),
            new Act("step to the middle of the bombard", () => Presenter.Step(CombatPresentation.ShotSeconds / 2f + CombatPresentation.HitSeconds + 0.2f)),
            new WaitFrames(2),
            new AssertQuery("the far tile's 11 was shown, and the bombard's flash and shot are both in the air", () =>
                Presenter.NumbersShown.Count == numbersBefore + 1 && Presenter.NumbersShown[^1] == (11, farTile) && Presenter.EffectCount == 2),
            new Capture("bombard", "A city bombards: a flash at the city and a shot toward our warrior; the 11 from the earlier shot floats away"),
            new Act("step to the middle of the death", () => Presenter.Step(CombatPresentation.BombardFlashSeconds + CombatPresentation.HitSeconds + 0.3f)),
            new WaitFrames(2),
            new AssertQuery("the worker is on its death clip (C083: the worker is a model with clips, so it falls instead of shrinking and sinking)", () => workerNode.NotNull().CurrentClip == UnitModelContract.DeathClip && workerNode.NotNull().ClipPosition > 0f && workerNode.NotNull().Scale == Vector3.One),
            new Capture("death", "A dying worker plays its death clip on its tile"),
            new Act("play out the script", () => Presenter.Step(10f)),
            new WaitFrames(3),
            new AssertQuery("the script drained: both numbers shown in order, the worker node freed, nothing left", () =>
            {
                var shown = Presenter.NumbersShown.Skip(Presenter.NumbersShown.Count - 2).ToList();
                return Presenter.IsIdle && shown.SequenceEqual([(11, farTile), (8, warriorTile)]) && Presenter.DyingCount == 0 && Presenter.EffectCount == 0
                    && !GodotObject.IsInstanceValid(workerNode) && AllAtRest();
            }),
            new AssertQuery("the game itself never changed: only the one attack was logged", () => session.LastSeq == seqBefore + 1),
        ];
    }
}
