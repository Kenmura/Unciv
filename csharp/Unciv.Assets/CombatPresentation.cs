using System.Globalization;
using Unciv.Commands;
using Unciv.Commands.Events;
using Unciv.Logic.Map;

namespace Unciv.Assets;

/// <summary>What one animation cue shows.</summary>
public enum CueKind
{
    /// <summary>A unit walks along its path.</summary>
    Move,
    /// <summary>A melee attacker lunges toward its target and back.</summary>
    Strike,
    /// <summary>A projectile flies from a ranged attacker to its target.</summary>
    Shot,
    /// <summary>A flash at a bombarding city.</summary>
    Bombard,
    /// <summary>The defender (or a hurt attacker) shakes and a damage number floats up.</summary>
    Hit,
    /// <summary>A unit shrinks and sinks away; the node is then freed.</summary>
    Death,
}

/// <summary>
/// One animated thing. <paramref name="UnitId"/> is the unit it moves or hurts (null for a city or a bare projectile). <paramref name="Delay"/> is when it starts
/// inside its beat; <paramref name="Duration"/> how long it runs; <paramref name="Amount"/> the health lost, shown as a floating number by <see cref="CueKind.Hit"/>.
/// </summary>
public sealed record Cue(CueKind Kind, int? UnitId, HexCoord From, HexCoord To, float Delay, float Duration, int Amount = 0, IReadOnlyList<HexCoord>? Path = null);

/// <summary>
/// C033: turns the engine's combat and movement events into a script of animation cues, as plain data. A <i>beat</i> is a group of cues that start together; beats play one after
/// the other. Reads only <see cref="UnitMoved"/>, <see cref="UnitAttacked"/> and <see cref="UnitDestroyed"/> (the numbers are the engine's own, never recomputed). The Godot side
/// (<c>Unciv.Godot/Play/CombatPresenter.cs</c>) plays the script through a <see cref="PresentationTimeline"/>. Pure and deterministic.
/// </summary>
public static class CombatPresentation
{
    public const float MoveSecondsPerTile = 0.18f;

    /// <summary>A melee attack: step forward, swing, hit at the marker, step back (<see cref="CuePoses.StepInEnd"/>, <see cref="StrikeHitMarker"/>, <see cref="CuePoses.StepBackStart"/>).</summary>
    public const float StrikeSeconds = 0.7f;

    /// <summary>Where in the strike the swing connects (fraction of <see cref="StrikeSeconds"/>); the clip's impact marker is expected near here.</summary>
    public const float StrikeHitMarker = 0.5f;

    /// <summary>Seconds into a melee beat at which the defender is hit.</summary>
    public const float StrikeHitDelay = StrikeSeconds * StrikeHitMarker;

    /// <summary>A ranged attacker's draw and release, before the projectile leaves (its <c>attack_ranged</c> marker).</summary>
    public const float ShotReleaseSeconds = 0.2f;

    public const float ShotSeconds = 0.35f;
    public const float BombardFlashSeconds = 0.3f;
    public const float BombardShotDelay = 0.15f;
    public const float HitSeconds = 0.8f;
    public const float DeathDelay = 0.3f;
    public const float DeathSeconds = 0.5f;

    /// <summary>The script for a batch of events, in event order. A unit destroyed by an attack of the same batch dies inside that attack's impact beat; any other destruction (disband, starvation) is a beat of its own.</summary>
    public static IReadOnlyList<IReadOnlyList<Cue>> Plan(IReadOnlyList<GameEvent> events)
    {
        var killedInBattle = events.OfType<UnitAttacked>().SelectMany(it => new[] { it.AttackerKilled ? it.AttackerUnitId : null, it.DefenderKilled ? it.DefenderUnitId : null })
            .OfType<int>().ToHashSet();
        var beats = new List<IReadOnlyList<Cue>>();
        foreach (var gameEvent in events)
        {
            switch (gameEvent)
            {
                case UnitMoved { Path.Count: >= 2 } moved:
                    beats.Add([new Cue(CueKind.Move, moved.UnitId, moved.Path[0], moved.Path[^1], 0f, (moved.Path.Count - 1) * MoveSecondsPerTile, Path: moved.Path)]);
                    break;
                case UnitAttacked attacked when IsMelee(attacked):
                    beats.Add(MeleeBeat(attacked));
                    break;
                case UnitAttacked attacked:
                    beats.Add(StrikeBeat(attacked));
                    beats.Add(ImpactBeat(attacked, 0f, 0f));
                    break;
                case UnitDestroyed destroyed when !killedInBattle.Contains(destroyed.UnitId):
                    beats.Add([new Cue(CueKind.Death, destroyed.UnitId, destroyed.Position, destroyed.Position, 0f, DeathSeconds)]);
                    break;
            }
        }
        return beats;
    }

    private static bool IsMelee(UnitAttacked e) => e.AttackerCityId == null && HexMath.GetDistance(e.AttackerPosition, e.DefenderPosition) <= 1;

    /// <summary>The swing and its consequences in one beat: the defender is hit (and may die) at the swing's marker, while the attacker is still stepping back.</summary>
    private static IReadOnlyList<Cue> MeleeBeat(UnitAttacked e) =>
        [new Cue(CueKind.Strike, e.AttackerUnitId, e.AttackerPosition, e.DefenderPosition, 0f, StrikeSeconds), .. ImpactBeat(e, StrikeHitDelay, StrikeSeconds)];

    /// <summary>The wind-up beat of a ranged or city attack (a melee attack is one beat, <see cref="MeleeBeat"/>).</summary>
    private static IReadOnlyList<Cue> StrikeBeat(UnitAttacked e)
    {
        if (e.AttackerCityId != null)
            return
            [
                new Cue(CueKind.Bombard, null, e.AttackerPosition, e.DefenderPosition, 0f, BombardFlashSeconds),
                new Cue(CueKind.Shot, null, e.AttackerPosition, e.DefenderPosition, BombardShotDelay, ShotSeconds),
            ];
        // not a city, not melee: a unit's shot, which starts with its draw and then releases the projectile
        return [new Cue(CueKind.Shot, e.AttackerUnitId, e.AttackerPosition, e.DefenderPosition, 0f, ShotReleaseSeconds + ShotSeconds)];
    }

    /// <summary>The defender reacts at <paramref name="at"/>; a counter-struck attacker only at <paramref name="attackerAt"/> (a melee attacker finishes its swing first, its clips and poses would clash).</summary>
    private static IReadOnlyList<Cue> ImpactBeat(UnitAttacked e, float at, float attackerAt)
    {
        var cues = new List<Cue> { new(CueKind.Hit, e.DefenderUnitId, e.DefenderPosition, e.DefenderPosition, at, HitSeconds, e.DamageToDefender) };
        if (e.DamageToAttacker > 0 && e.AttackerCityId == null)
            cues.Add(new Cue(CueKind.Hit, e.AttackerUnitId, e.AttackerPosition, e.AttackerPosition, attackerAt, HitSeconds, e.DamageToAttacker));
        if (e.AttackerKilled && e.AttackerUnitId is { } attacker)
            cues.Add(new Cue(CueKind.Death, attacker, e.AttackerPosition, e.AttackerPosition, attackerAt + DeathDelay, DeathSeconds));
        if (e.DefenderKilled && e.DefenderUnitId is { } defender)
            cues.Add(new Cue(CueKind.Death, defender, e.DefenderPosition, e.DefenderPosition, at + DeathDelay, DeathSeconds));
        return cues;
    }

    /// <summary>The floating number for lost health: a minus sign and the amount, in the invariant culture.</summary>
    public static string DamageText(int amount) => "-" + amount.ToString(CultureInfo.InvariantCulture);
}

/// <summary>An update for one cue: how far it is (0..1) and whether it just finished. A cue finishes exactly once.</summary>
public readonly record struct CueUpdate(Cue Cue, float Progress, bool Done);

/// <summary>
/// C033: plays a script of beats against explicit time. Nothing here reads a clock: the owner calls <see cref="Advance"/> with a step, so a test or the golden harness can step it
/// exactly and a real client feeds it frame deltas. A long backlog (an AI turn full of fights) plays faster so it never keeps the player waiting longer than <see cref="MaxBacklogSeconds"/> of
/// script time at normal speed.
/// </summary>
public sealed class PresentationTimeline
{
    /// <summary>Script time that may be queued before the timeline speeds up.</summary>
    public const float MaxBacklogSeconds = 6f;

    private sealed class Slot(Cue cue)
    {
        public Cue Cue { get; } = cue;

        public bool Done { get; set; }
    }

    private readonly List<List<Slot>> beats = [];
    private int current;
    private float beatTime;

    public bool IsIdle => current >= beats.Count;

    /// <summary>Script seconds left, at normal speed.</summary>
    public float PendingSeconds
    {
        get
        {
            var total = 0f;
            for (var i = current; i < beats.Count; i++) total += Length(beats[i]);
            return Math.Max(0f, total - beatTime);
        }
    }

    /// <summary>The cues of the beats that have not started, in script order.</summary>
    public IReadOnlyList<Cue> Upcoming => [.. beats.Skip(current + 1).SelectMany(it => it).Select(it => it.Cue)];

    /// <summary>Drops the whole script, started or not.</summary>
    public void Clear()
    {
        beats.Clear();
        current = 0;
        beatTime = 0f;
    }

    public void Enqueue(IEnumerable<IReadOnlyList<Cue>> script)
    {
        if (IsIdle)
        {
            beats.Clear();
            current = 0;
            beatTime = 0f;
        }
        foreach (var beat in script) beats.Add([.. beat.Select(it => new Slot(it))]);
    }

    /// <summary>Moves the script on by <paramref name="seconds"/> (never negative) and reports every cue that ran, in beat order. A step of zero re-reports the running cues at their current progress.</summary>
    public IReadOnlyList<CueUpdate> Advance(float seconds)
    {
        var updates = new List<CueUpdate>();
        if (IsIdle) return updates;
        var remaining = (seconds > 0f ? seconds : 0f) * Math.Max(1f, PendingSeconds / MaxBacklogSeconds); // NaN counts as no time, or the clock would stick forever
        while (!IsIdle)
        {
            var beat = beats[current];
            var length = Length(beat);
            var time = beatTime + remaining;
            if (time >= length)
            {
                Report(beat, length, updates);
                remaining = time - length;
                current++;
                beatTime = 0f;
                continue;
            }
            Report(beat, time, updates);
            beatTime = time;
            break;
        }
        return updates;
    }

    private static float Length(List<Slot> beat) => beat.Count == 0 ? 0f : beat.Max(it => it.Cue.Delay + it.Cue.Duration);

    private static void Report(List<Slot> beat, float time, List<CueUpdate> updates)
    {
        foreach (var slot in beat)
        {
            if (slot.Done || time < slot.Cue.Delay) continue;
            // finished is decided on the end time itself: (end - delay) / duration can round to just under 1 in float, which would leave the cue running forever
            var finished = time >= slot.Cue.Delay + slot.Cue.Duration;
            var progress = finished || slot.Cue.Duration <= 0f ? 1f : Math.Clamp((time - slot.Cue.Delay) / slot.Cue.Duration, 0f, 1f);
            slot.Done = finished || progress >= 1f;
            updates.Add(new CueUpdate(slot.Cue, progress, slot.Done));
        }
    }
}

/// <summary>
/// C033: the procedural tweens as pure functions of progress (0..1), so every pose is the same on every machine and a test can pin the numbers. Distances are in tile units,
/// the Godot side scales them to the map.
/// </summary>
public static class CuePoses
{
    /// <summary>How far a melee attacker lunges, as a fraction of the way to the target, at its peak.</summary>
    public const float LungeReach = 0.4f;

    /// <summary>Height of a projectile's arc at its peak, in tile units.</summary>
    public const float ArcHeight = 0.6f;

    public const float ShakeReach = 0.12f;
    public const float ShakeEnd = 0.35f;
    public const float RiseHeight = 0.9f;
    public const float FadeStart = 0.6f;
    public const float SinkDepth = 0.3f;

    /// <summary>0 at both ends, 1 in the middle: a parabola, the shape of every there-and-back motion.</summary>
    public static float Bump(float p)
    {
        p = Math.Clamp(p, 0f, 1f);
        return 4f * p * (1f - p);
    }

    /// <summary>Which leg of a path and how far along it: a path of <paramref name="points"/> tiles has points-1 legs of equal time.</summary>
    public static (int Leg, float Along) PathLeg(int points, float progress)
    {
        var legs = Math.Max(1, points - 1);
        var scaled = Math.Clamp(progress, 0f, 1f) * legs;
        var leg = Math.Min((int)scaled, legs - 1);
        return (leg, scaled - leg);
    }

    /// <summary>Strike progress at which the attacker has finished stepping in.</summary>
    public const float StepInEnd = 0.3f;

    /// <summary>Strike progress at which the attacker starts stepping back.</summary>
    public const float StepBackStart = 0.7f;

    /// <summary>Fraction of the way to the target a melee attacker has stepped: eases in, holds for the swing, eases back.</summary>
    public static float Lunge(float p)
    {
        p = Math.Clamp(p, 0f, 1f);
        if (p < StepInEnd) return LungeReach * Smooth(p / StepInEnd);
        return p < StepBackStart ? LungeReach : LungeReach * Smooth((1f - p) / (1f - StepBackStart));
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    /// <summary>
    /// Where a ranged attacker's projectile is (0..1 of its flight) at progress <paramref name="p"/> of a unit's shot cue, which starts with the draw (<see cref="CombatPresentation.ShotReleaseSeconds"/>);
    /// 0 until the release.
    /// </summary>
    public static float ShotFlight(float p)
    {
        return Math.Clamp((Math.Clamp(p, 0f, 1f) - ShotRelease) / (1f - ShotRelease), 0f, 1f);
    }

    /// <summary>Progress of a unit's shot cue at which its projectile is released (the end of the draw).</summary>
    public const float ShotRelease = CombatPresentation.ShotReleaseSeconds / (CombatPresentation.ShotReleaseSeconds + CombatPresentation.ShotSeconds);

    /// <summary>The yaw (radians about Y, 0 facing +Z, the glTF default) that turns a figure toward the horizontal vector (<paramref name="dx"/>, <paramref name="dz"/>); null when there is no direction to face.</summary>
    public static float? FacingYaw(float dx, float dz)
    {
        var length = dx * dx + dz * dz;
        return float.IsFinite(length) && length > 1e-8f ? MathF.Atan2(dx, dz) : null;
    }

    /// <summary>Where in a clip of <paramref name="length"/> seconds to stand at <paramref name="seconds"/> into it; a looping clip wraps, any other holds its last frame. NaN or a negative time is the start.</summary>
    public static float ClipTime(float seconds, float length, bool loop)
    {
        if (!(length > 0f) || !(seconds > 0f)) return 0f;
        if (float.IsNaN(seconds)) return 0f;
        if (float.IsPositiveInfinity(seconds)) return loop ? 0f : length;
        return loop ? seconds % length : Math.Min(seconds, length);
    }

    /// <summary>Height above the straight line of a projectile.</summary>
    public static float Arc(float p) => ArcHeight * Bump(p);

    /// <summary>Sideways offset of a struck unit: a quick decaying jitter that is over by <see cref="ShakeEnd"/>.</summary>
    public static float Shake(float p)
    {
        p = Math.Clamp(p, 0f, 1f);
        if (p >= ShakeEnd) return 0f;
        var sign = (int)(p * 20f) % 2 == 0 ? 1f : -1f;
        return sign * ShakeReach * (1f - p / ShakeEnd);
    }

    /// <summary>How high a damage number has floated.</summary>
    public static float Rise(float p) => RiseHeight * Math.Clamp(p, 0f, 1f);

    /// <summary>Opacity of a damage number or flash: opaque until <see cref="FadeStart"/>, then fading to nothing.</summary>
    public static float Opacity(float p)
    {
        p = Math.Clamp(p, 0f, 1f);
        return p <= FadeStart ? 1f : 1f - (p - FadeStart) / (1f - FadeStart);
    }

    /// <summary>Scale of a dying unit: full size to nothing.</summary>
    public static float DeathScale(float p) => 1f - Math.Clamp(p, 0f, 1f);

    /// <summary>How far a dying unit has sunk.</summary>
    public static float Sink(float p) => SinkDepth * Math.Clamp(p, 0f, 1f);

    /// <summary>Scale of the bombard flash: it swells as it fades.</summary>
    public static float FlashScale(float p) => 0.3f + 1.2f * Math.Clamp(p, 0f, 1f);
}
