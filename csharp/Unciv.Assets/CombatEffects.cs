using Unciv.Logic.Map;

namespace Unciv.Assets;

/// <summary>A short-lived particle burst on the map.</summary>
public enum EffectKind
{
    /// <summary>Bright sparks thrown outward where a blow lands.</summary>
    Spark,
    /// <summary>Grey puffs rising from the ground (footsteps, a fall).</summary>
    Dust,
    /// <summary>A quick flash at a weapon's muzzle or release point.</summary>
    Muzzle,
}

/// <summary>
/// Something that happens at <paramref name="At"/> (0..1 of its cue's progress): a particle burst of <paramref name="Effect"/> at <paramref name="Tile"/>, a sound named
/// <paramref name="Sound"/> (<see cref="ProceduralSounds"/>, overridable by <c>assets/override/sound/&lt;name&gt;.ogg</c>), or both.
/// </summary>
public sealed record CueMoment(float At, HexCoord Tile, EffectKind? Effect, string? Sound);

/// <summary>
/// C086: the polish of the combat presentation as pure data. <see cref="For"/> lists, per animation cue, the sparks, dust, muzzle flashes and sounds and the moment of the cue
/// each belongs to, timed to the same markers the poses use (the swing's step-in, a ranged unit's release, the defender's <see cref="CueKind.Hit"/> at impact). The Godot side
/// (<c>CombatPresenter</c>) fires each moment once when the cue's progress reaches it. Nothing here reads a clock or a random number.
/// </summary>
public static class CombatEffects
{
    public const string SwingSound = "combat_swing";
    public const string ShootSound = "combat_shoot";
    public const string HitSound = "combat_hit";
    public const string DeathSound = "combat_death";

    /// <summary>The bombard sound of <see cref="Unciv.Models.UncivSound.Bombard"/>, which a city's flash plays.</summary>
    public const string BombardSound = "bombard";

    /// <summary>Every sound name <see cref="For"/> can produce.</summary>
    public static IReadOnlyList<string> SoundNames { get; } = [SwingSound, ShootSound, HitSound, DeathSound, BombardSound];

    /// <summary>Particles in a burst.</summary>
    public static int ParticleCount(EffectKind kind) => kind switch { EffectKind.Spark => 7, EffectKind.Dust => 4, _ => 1 };

    /// <summary>How long a burst lives, in seconds.</summary>
    public static float Lifetime(EffectKind kind) => kind switch { EffectKind.Spark => 0.35f, EffectKind.Dust => 0.7f, _ => 0.25f };

    /// <summary>The moments of one cue, in time order. A blow that does no damage makes no sparks.</summary>
    public static IReadOnlyList<CueMoment> For(Cue cue)
    {
        switch (cue.Kind)
        {
            case CueKind.Move when cue.Path is { Count: >= 2 } path:
                // a puff of dust behind the walker as each step begins
                return [.. Enumerable.Range(0, path.Count - 1).Select(leg => new CueMoment((float)leg / (path.Count - 1), path[leg], EffectKind.Dust, null))];
            case CueKind.Strike:
                return
                [
                    new CueMoment(0.05f, cue.From, EffectKind.Dust, null),
                    new CueMoment(CuePoses.StepInEnd, cue.From, null, SwingSound),
                ];
            case CueKind.Shot when cue.UnitId != null:
                return [new CueMoment(CuePoses.ShotRelease, cue.From, EffectKind.Muzzle, ShootSound)];
            case CueKind.Shot:
                return []; // a city's projectile: its flash (Bombard) made the noise
            case CueKind.Bombard:
                return [new CueMoment(0f, cue.From, EffectKind.Muzzle, BombardSound)];
            case CueKind.Hit when cue.Amount > 0:
                return [new CueMoment(0f, cue.To, EffectKind.Spark, HitSound)];
            case CueKind.Death:
                return
                [
                    new CueMoment(0f, cue.To, null, DeathSound),
                    new CueMoment(0.4f, cue.To, EffectKind.Dust, null),
                ];
            default:
                return [];
        }
    }

    /// <summary>
    /// Where particle <paramref name="index"/> of a burst of <paramref name="kind"/> is at progress <paramref name="p"/> (0..1 of its lifetime), as an offset in tile units from the
    /// burst's origin (X and Z on the ground, Y up), and how far it has faded (opacity 1..0) and scaled. Spread is a fixed fan by index, so a burst looks the same every time.
    /// </summary>
    public static (float X, float Y, float Z, float Opacity, float Scale) Particle(EffectKind kind, int index, float p)
    {
        p = float.IsNaN(p) ? 0f : Math.Clamp(p, 0f, 1f);
        var count = ParticleCount(kind);
        var angle = 2f * MathF.PI * ((index % count) + 0.5f * (index / 2 % 2)) / count;
        switch (kind)
        {
            case EffectKind.Spark:
                {
                    var reach = 0.45f * (1f - (1f - p) * (1f - p)); // fast out, easing
                    return (MathF.Cos(angle) * reach, 0.35f + 0.5f * reach - 0.45f * p * p, MathF.Sin(angle) * reach, 1f - p, 1f - 0.6f * p);
                }
            case EffectKind.Dust:
                {
                    var reach = 0.25f * p;
                    return (MathF.Cos(angle) * reach, 0.1f + 0.35f * p, MathF.Sin(angle) * reach, 0.6f * (1f - p), 0.5f + 1.2f * p);
                }
            default:
                return (0f, 0.6f, 0f, 1f - p, 0.6f + 1.4f * p);
        }
    }
}
