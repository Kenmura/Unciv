using Unciv.Assets;
using Unciv.Commands.Events;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Assets;

/// <summary>C033: the script made from engine events, the timeline that plays it against explicit time, and the tween curves.</summary>
public sealed class CombatPresentationTests
{
    private static readonly HexCoord A = HexCoord.Of(0, 0);
    private static readonly HexCoord B = HexCoord.Of(1, 0);
    private static readonly HexCoord C = HexCoord.Of(2, 0);

    private static UnitAttacked Attack(HexCoord from, HexCoord to, int dealt = 7, int taken = 3, bool attackerKilled = false, bool defenderKilled = false,
        int? attackerUnit = 1, string? attackerCity = null, int? defenderUnit = 2, string? defenderCity = null) =>
        new("Rome", attackerUnit, attackerCity, from, "Egypt", defenderUnit, defenderCity, to, dealt, taken, attackerKilled, defenderKilled);

    private static void Near(float expected, float actual) => Assert.Equal(expected, actual, 5);

    // ---- Plan

    [Fact]
    public void A_move_is_one_beat_that_takes_a_fixed_time_per_leg()
    {
        var plan = CombatPresentation.Plan([new UnitMoved("Rome", 4, "Warrior", [A, B, C])]);
        var beat = Assert.Single(plan);
        var cue = Assert.Single(beat);
        Assert.Equal(CueKind.Move, cue.Kind);
        Assert.Equal(4, cue.UnitId);
        Assert.Equal(A, cue.From);
        Assert.Equal(C, cue.To);
        Near(0f, cue.Delay);
        Near(0.36f, cue.Duration);
        Assert.Equal([A, B, C], cue.Path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_move_with_no_leg_is_not_shown(int tiles)
    {
        var path = Enumerable.Range(0, tiles).Select(it => HexCoord.Of(it, 0)).ToList();
        Assert.Empty(CombatPresentation.Plan([new UnitMoved("Rome", 4, "Warrior", path)]));
    }

    [Fact]
    public void Other_events_are_not_presented()
    {
        Assert.Empty(CombatPresentation.Plan([new UnitCreated("Rome", 9, "Warrior", A)]));
        Assert.Empty(CombatPresentation.Plan([]));
    }

    [Fact]
    public void A_melee_attack_is_one_beat_whose_hit_lands_at_the_swings_marker_with_the_engines_numbers()
    {
        var plan = CombatPresentation.Plan([Attack(A, B, dealt: 7, taken: 3)]);
        Assert.Equal(
        [
            new Cue(CueKind.Strike, 1, A, B, 0f, CombatPresentation.StrikeSeconds),
            new Cue(CueKind.Hit, 2, B, B, CombatPresentation.StrikeHitDelay, CombatPresentation.HitSeconds, 7),
            new Cue(CueKind.Hit, 1, A, A, CombatPresentation.StrikeSeconds, CombatPresentation.HitSeconds, 3), // the attacker is counter-struck once its swing is over
        ], Assert.Single(plan));
    }

    [Fact]
    public void The_strike_timing_constants_are_the_step_swing_hit_step_back_shape()
    {
        Near(0.7f, CombatPresentation.StrikeSeconds);
        Near(0.35f, CombatPresentation.StrikeHitDelay);
        Assert.InRange(CombatPresentation.StrikeHitMarker, CuePoses.StepInEnd, CuePoses.StepBackStart);
        Near(0.2f, CombatPresentation.ShotReleaseSeconds);
    }

    [Fact]
    public void An_attacker_that_took_no_damage_does_not_flinch()
    {
        var impact = CombatPresentation.Plan([Attack(A, B, dealt: 5, taken: 0)])[0].Where(it => it.Kind != CueKind.Strike).ToList();
        Assert.Equal(CueKind.Hit, Assert.Single(impact).Kind);
        Assert.Equal(2, impact[0].UnitId);
    }

    [Fact]
    public void An_attack_from_a_distance_flies_a_projectile_from_the_attacker()
    {
        var plan = CombatPresentation.Plan([Attack(A, C)]);
        var shot = Assert.Single(plan[0]);
        Assert.Equal(new Cue(CueKind.Shot, 1, A, C, 0f, CombatPresentation.ShotReleaseSeconds + CombatPresentation.ShotSeconds), shot);
        Assert.All(plan[1], it => Assert.Equal(CueKind.Hit, it.Kind)); // the number lands once the projectile has arrived
    }

    [Fact]
    public void A_city_bombards_with_a_flash_and_a_shot_and_never_flinches()
    {
        var plan = CombatPresentation.Plan([Attack(A, C, taken: 4, attackerUnit: null, attackerCity: "Rome")]);
        Assert.Equal(
        [
            new Cue(CueKind.Bombard, null, A, C, 0f, CombatPresentation.BombardFlashSeconds),
            new Cue(CueKind.Shot, null, A, C, CombatPresentation.BombardShotDelay, CombatPresentation.ShotSeconds),
        ], plan[0]);
        Assert.Equal(CueKind.Hit, Assert.Single(plan[1]).Kind);
    }

    [Fact]
    public void A_city_that_is_attacked_shows_the_number_over_its_tile_without_a_unit()
    {
        var impact = CombatPresentation.Plan([Attack(A, B, dealt: 9, defenderUnit: null, defenderCity: "Cairo", defenderKilled: true)])[0].Where(it => it.Kind != CueKind.Strike).ToList();
        Assert.Equal(new Cue(CueKind.Hit, null, B, B, CombatPresentation.StrikeHitDelay, CombatPresentation.HitSeconds, 9), impact[0]);
        Assert.DoesNotContain(impact, it => it.Kind == CueKind.Death);
    }

    [Fact]
    public void Units_killed_in_a_fight_die_inside_the_fights_beat_after_the_shake()
    {
        var plan = CombatPresentation.Plan([new UnitDestroyed("Egypt", 2, "Warrior", B), Attack(A, B, attackerKilled: true, defenderKilled: true)]);
        Assert.Single(plan);
        var deaths = plan[0].Where(it => it.Kind == CueKind.Death).ToList();
        Assert.Equal([1, 2], deaths.Select(it => it.UnitId));
        Assert.All(deaths, it =>
        {
            Near((it.UnitId == 1 ? CombatPresentation.StrikeSeconds : CombatPresentation.StrikeHitDelay) + CombatPresentation.DeathDelay, it.Delay);
            Near(CombatPresentation.DeathSeconds, it.Duration);
        });
        Assert.Equal(A, deaths[0].From);
        Assert.Equal(B, deaths[1].From);
    }

    [Fact]
    public void A_survivor_does_not_die_and_a_killed_flag_without_a_unit_is_ignored()
    {
        Assert.DoesNotContain(CombatPresentation.Plan([Attack(A, B)])[0], it => it.Kind == CueKind.Death);
        Assert.DoesNotContain(CombatPresentation.Plan([Attack(A, B, attackerKilled: true, attackerUnit: null, attackerCity: "Rome")])[1], it => it.Kind == CueKind.Death);
    }

    [Fact]
    public void A_destruction_outside_a_fight_is_a_beat_of_its_own()
    {
        var plan = CombatPresentation.Plan([new UnitDestroyed("Rome", 5, "Settler", C), Attack(A, B, defenderKilled: true)]);
        Assert.Equal(2, plan.Count);
        Assert.Equal(new Cue(CueKind.Death, 5, C, C, 0f, CombatPresentation.DeathSeconds), Assert.Single(plan[0]));
    }

    [Fact]
    public void Beats_follow_the_event_order()
    {
        var plan = CombatPresentation.Plan([new UnitMoved("Rome", 1, "Warrior", [A, B]), Attack(B, C), new UnitMoved("Egypt", 2, "Warrior", [C, B])]);
        Assert.Equal([CueKind.Move, CueKind.Strike, CueKind.Move], plan.Select(it => it[0].Kind));
    }

    [Theory]
    [InlineData(0, "-0")]
    [InlineData(12, "-12")]
    [InlineData(1000, "-1000")]
    public void Damage_text_is_a_minus_and_the_plain_number(int amount, string expected) => Assert.Equal(expected, CombatPresentation.DamageText(amount));

    // ---- Timeline

    private static Cue Make(float delay, float duration, CueKind kind = CueKind.Hit, int amount = 0) => new(kind, 1, A, B, delay, duration, amount);

    [Fact]
    public void A_new_timeline_is_idle_and_stays_idle()
    {
        var timeline = new PresentationTimeline();
        Assert.True(timeline.IsIdle);
        Assert.Empty(timeline.Advance(1f));
        Near(0f, timeline.PendingSeconds);
        Assert.Empty(timeline.Upcoming);
    }

    [Fact]
    public void Progress_follows_the_time_and_a_cue_finishes_once()
    {
        var timeline = new PresentationTimeline();
        var cue = Make(0f, 2f);
        timeline.Enqueue([[cue]]);
        Near(2f, timeline.PendingSeconds);

        var first = Assert.Single(timeline.Advance(0.5f));
        Assert.Equal(cue, first.Cue);
        Near(0.25f, first.Progress);
        Assert.False(first.Done);
        Assert.False(timeline.IsIdle);

        var last = Assert.Single(timeline.Advance(5f));
        Near(1f, last.Progress);
        Assert.True(last.Done);
        Assert.True(timeline.IsIdle);
        Assert.Empty(timeline.Advance(1f));
    }

    [Fact]
    public void A_delayed_cue_finishes_at_its_end_time_even_when_the_float_division_would_fall_short()
    {
        var timeline = new PresentationTimeline();
        var cue = Make(CombatPresentation.StrikeHitDelay, CombatPresentation.HitSeconds);
        timeline.Enqueue([[cue]]);
        var update = Assert.Single(timeline.Advance(CombatPresentation.StrikeHitDelay + CombatPresentation.HitSeconds));
        Assert.True(update.Done);
        Near(1f, update.Progress);
        Assert.True(timeline.IsIdle);
    }

    [Fact]
    public void A_step_of_zero_reports_the_running_cue_again_and_a_negative_step_goes_nowhere()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue([[Make(0f, 1f)]]);
        timeline.Advance(0.5f);
        Near(0.5f, Assert.Single(timeline.Advance(0f)).Progress);
        Near(0.5f, Assert.Single(timeline.Advance(-3f)).Progress);
        Near(0.5f, timeline.PendingSeconds);
    }

    [Fact]
    public void A_big_step_runs_every_beat_it_crosses_in_order_and_finishes_each_cue()
    {
        var timeline = new PresentationTimeline();
        var one = Make(0f, 1f);
        var two = Make(0f, 1f, CueKind.Death);
        var three = Make(0f, 1f, CueKind.Strike);
        timeline.Enqueue([[one], [two], [three]]);
        var updates = timeline.Advance(2.5f);
        Assert.Equal([one, two], updates.Where(it => it.Done).Select(it => it.Cue));
        Assert.Equal(three, updates[^1].Cue);
        Near(0.5f, updates[^1].Progress);
        Near(0.5f, timeline.PendingSeconds);
        Assert.Equal(2, updates.Count(it => it.Done));
    }

    [Fact]
    public void A_step_that_is_not_a_number_moves_nothing_and_does_not_stick_the_clock()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue(CombatPresentation.Plan([new UnitDestroyed("A", 1, "Worker", new HexCoord(0, 0))]));
        timeline.Advance(float.NaN);
        Assert.False(timeline.IsIdle);
        timeline.Advance(10f);
        Assert.True(timeline.IsIdle);
    }

    [Fact]
    public void Clear_drops_the_script()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue(CombatPresentation.Plan([new UnitDestroyed("A", 1, "Worker", new HexCoord(0, 0))]));
        timeline.Advance(0.1f);
        timeline.Clear();
        Assert.True(timeline.IsIdle);
        Assert.Empty(timeline.Advance(1f));
    }

    [Fact]
    public void A_cue_starts_after_its_delay_and_the_beat_lasts_until_the_last_cue_ends()
    {
        var timeline = new PresentationTimeline();
        var early = Make(0f, 0.2f);
        var late = Make(0.3f, 0.5f, CueKind.Death);
        timeline.Enqueue([[early, late]]);
        Near(0.8f, timeline.PendingSeconds);

        var firstHalf = timeline.Advance(0.1f);
        Assert.Equal([early], firstHalf.Select(it => it.Cue));

        var later = timeline.Advance(0.3f); // t = 0.4: early is finished (reported once), late has run 0.1 of 0.5
        Assert.Equal([early, late], later.Select(it => it.Cue));
        Assert.True(later[0].Done);
        Near(0.2f, later[1].Progress);
        Near(0.6f, Assert.Single(timeline.Advance(0.2f)).Progress); // early is not reported again
    }

    [Fact]
    public void A_cue_that_has_not_started_yet_is_not_reported()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue([[Make(0.5f, 0.5f)]]);
        Assert.Empty(timeline.Advance(0.25f));
        Assert.False(timeline.IsIdle);
    }

    [Fact]
    public void A_cue_without_duration_is_over_as_soon_as_it_starts()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue([[Make(0f, 0f)]]);
        var update = Assert.Single(timeline.Advance(0f));
        Near(1f, update.Progress);
        Assert.True(update.Done);
        Assert.True(timeline.IsIdle);
    }

    [Fact]
    public void An_empty_beat_is_skipped()
    {
        var timeline = new PresentationTimeline();
        var cue = Make(0f, 1f);
        timeline.Enqueue([[], [cue]]);
        Assert.Equal(cue, Assert.Single(timeline.Advance(0.5f)).Cue);
    }

    [Fact]
    public void Upcoming_lists_the_cues_of_the_beats_that_have_not_started()
    {
        var timeline = new PresentationTimeline();
        var one = Make(0f, 1f);
        var two = Make(0f, 1f, CueKind.Move);
        var three = Make(0f, 1f, CueKind.Strike);
        timeline.Enqueue([[one], [two, three]]);
        Assert.Equal([two, three], timeline.Upcoming);
        timeline.Advance(1.2f);
        Assert.Empty(timeline.Upcoming);
    }

    [Fact]
    public void Enqueueing_while_running_appends_and_enqueueing_when_idle_starts_over()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue([[Make(0f, 1f)]]);
        timeline.Advance(0.5f);
        timeline.Enqueue([[Make(0f, 1f)]]);
        Near(1.5f, timeline.PendingSeconds);

        timeline.Advance(10f);
        Assert.True(timeline.IsIdle);
        var again = Make(0f, 1f, CueKind.Strike);
        timeline.Enqueue([[again]]);
        Assert.Equal(again, Assert.Single(timeline.Advance(0.1f)).Cue);
        Near(0.9f, timeline.PendingSeconds);
    }

    [Fact]
    public void A_long_backlog_plays_faster_in_proportion()
    {
        var timeline = new PresentationTimeline();
        timeline.Enqueue(Enumerable.Range(0, 12).Select(_ => (IReadOnlyList<Cue>)[Make(0f, 1f)]));
        Near(12f, timeline.PendingSeconds);
        timeline.Advance(1f); // 12 s pending against a 6 s allowance: twice the speed
        Near(10f, timeline.PendingSeconds);

        var small = new PresentationTimeline();
        small.Enqueue([[Make(0f, 4f)]]);
        small.Advance(1f);
        Near(3f, small.PendingSeconds); // under the allowance: normal speed
    }

    // ---- Curves

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.25f, 0.75f)]
    [InlineData(0.5f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 0f)]
    public void Bump_is_a_parabola_clamped_to_the_unit_range(float p, float expected) => Near(expected, CuePoses.Bump(p));

    [Theory]
    [InlineData(3, 0f, 0, 0f)]
    [InlineData(3, 0.25f, 0, 0.5f)]
    [InlineData(3, 0.5f, 1, 0f)]
    [InlineData(3, 0.75f, 1, 0.5f)]
    [InlineData(3, 1f, 1, 1f)]
    [InlineData(2, 1f, 0, 1f)]
    [InlineData(2, 5f, 0, 1f)]
    [InlineData(2, -5f, 0, 0f)]
    [InlineData(1, 0.5f, 0, 0.5f)]
    [InlineData(0, 0.5f, 0, 0.5f)]
    public void A_path_is_walked_leg_by_leg_at_equal_time(int points, float progress, int leg, float along)
    {
        var result = CuePoses.PathLeg(points, progress);
        Assert.Equal(leg, result.Leg);
        Near(along, result.Along);
    }

    [Fact]
    public void Lunge_steps_in_holds_for_the_swing_and_steps_back()
    {
        Near(0f, CuePoses.Lunge(0f));
        Assert.InRange(CuePoses.Lunge(CuePoses.StepInEnd / 2f), 0.01f, CuePoses.LungeReach - 0.01f);
        Near(CuePoses.LungeReach, CuePoses.Lunge(CuePoses.StepInEnd));
        Near(CuePoses.LungeReach, CuePoses.Lunge(CombatPresentation.StrikeHitMarker));
        Near(CuePoses.LungeReach, CuePoses.Lunge(CuePoses.StepBackStart));
        Assert.InRange(CuePoses.Lunge((1f + CuePoses.StepBackStart) / 2f), 0.01f, CuePoses.LungeReach - 0.01f);
        Near(0f, CuePoses.Lunge(1f));
        Near(0f, CuePoses.Lunge(-3f));
        Near(0f, CuePoses.Lunge(7f));
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.3636364f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void The_shot_flight_starts_at_the_release_marker(float p, float expected) => Near(expected, CuePoses.ShotFlight(p));

    [Fact]
    public void The_shot_flight_is_halfway_between_release_and_the_end()
    {
        const float release = CombatPresentation.ShotReleaseSeconds / (CombatPresentation.ShotReleaseSeconds + CombatPresentation.ShotSeconds);
        Near(0.5f, CuePoses.ShotFlight((1f + release) / 2f));
    }

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(1f, 0f, MathF.PI / 2f)]
    [InlineData(-1f, 0f, -MathF.PI / 2f)]
    [InlineData(0f, -1f, MathF.PI)]
    public void Facing_turns_toward_the_direction_with_zero_yaw_along_plus_z(float dx, float dz, float expected) => Near(expected, CuePoses.FacingYaw(dx, dz)!.Value);

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, float.PositiveInfinity)]
    public void Facing_a_missing_or_broken_direction_keeps_the_current_one(float dx, float dz) => Assert.Null(CuePoses.FacingYaw(dx, dz));

    [Theory]
    [InlineData(0.5f, 2f, false, 0.5f)]
    [InlineData(3f, 2f, false, 2f)]
    [InlineData(3f, 2f, true, 1f)]
    [InlineData(-1f, 2f, true, 0f)]
    [InlineData(float.NaN, 2f, true, 0f)]
    [InlineData(float.PositiveInfinity, 2f, true, 0f)]
    [InlineData(float.PositiveInfinity, 2f, false, 2f)]
    [InlineData(1f, 0f, true, 0f)]
    [InlineData(1f, float.NaN, false, 0f)]
    public void Clip_time_wraps_a_loop_holds_the_end_of_a_one_shot_and_survives_bad_input(float seconds, float length, bool loop, float expected) => Near(expected, CuePoses.ClipTime(seconds, length, loop));

    [Fact]
    public void Lunge_and_arc_peak_in_the_middle()
    {
        Near(0f, CuePoses.Lunge(0f));
        Near(0f, CuePoses.Arc(0f));
        Near(CuePoses.ArcHeight, CuePoses.Arc(0.5f));
        Near(0f, CuePoses.Arc(1f));
    }

    [Theory]
    [InlineData(0f, 0.12f)]
    [InlineData(0.04f, 0.12f * (1f - 0.04f / 0.35f))]
    [InlineData(0.06f, -0.12f * (1f - 0.06f / 0.35f))]
    [InlineData(0.1f, 0.12f * (1f - 0.1f / 0.35f))]
    [InlineData(0.25f, -0.12f * (1f - 0.25f / 0.35f))]
    [InlineData(0.35f, 0f)]
    [InlineData(0.9f, 0f)]
    [InlineData(5f, 0f)]
    public void Shake_jitters_and_dies_down_by_its_end(float p, float expected) => Near(expected, CuePoses.Shake(p));

    [Fact]
    public void Shake_before_the_start_is_clamped_to_the_start() => Near(0.12f, CuePoses.Shake(-4f));

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.45f)]
    [InlineData(1f, 0.9f)]
    [InlineData(3f, 0.9f)]
    public void A_number_rises_to_a_fixed_height(float p, float expected) => Near(expected, CuePoses.Rise(p));

    [Theory]
    [InlineData(-1f, 1f)]
    [InlineData(0f, 1f)]
    [InlineData(0.6f, 1f)]
    [InlineData(0.8f, 0.5f)]
    [InlineData(1f, 0f)]
    [InlineData(2f, 0f)]
    public void Opacity_holds_then_fades(float p, float expected) => Near(expected, CuePoses.Opacity(p));

    [Theory]
    [InlineData(-1f, 1f, 0f)]
    [InlineData(0f, 1f, 0f)]
    [InlineData(0.5f, 0.5f, 0.15f)]
    [InlineData(1f, 0f, 0.3f)]
    [InlineData(2f, 0f, 0.3f)]
    public void A_dying_unit_shrinks_and_sinks(float p, float scale, float sink)
    {
        Near(scale, CuePoses.DeathScale(p));
        Near(sink, CuePoses.Sink(p));
    }

    [Theory]
    [InlineData(-1f, 0.3f)]
    [InlineData(0f, 0.3f)]
    [InlineData(0.5f, 0.9f)]
    [InlineData(1f, 1.5f)]
    [InlineData(9f, 1.5f)]
    public void The_flash_swells(float p, float expected) => Near(expected, CuePoses.FlashScale(p));
}
