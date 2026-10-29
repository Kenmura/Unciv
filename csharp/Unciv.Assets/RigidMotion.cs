namespace Unciv.Assets;

/// <summary>C083: the family of a unit that has no skeleton and moves as one rigid piece driven by code (design/UNIT-MODELS.md section 5).</summary>
public enum RigidKind
{
    /// <summary>A humanoid or other unit that animates with clips, or a unit whose model has no motion of its own.</summary>
    None,
    Ship,
    Siege,
    Armor,
    Air,
}

/// <summary>
/// C083: where a rigid figure stands relative to its resting spot. <paramref name="Recoil"/> pushes it backwards along its facing (tile units), <paramref name="Bob"/> lifts it, <paramref name="Pitch"/>
/// (nose up positive) and <paramref name="Roll"/> tilt it (radians), <paramref name="TurretYaw"/> turns the turret parts of the model (radians, 0 = straight ahead).
/// </summary>
public readonly record struct RigidPose(float Recoil, float Bob, float Pitch, float Roll, float TurretYaw)
{
    public static readonly RigidPose Rest = new(0f, 0f, 0f, 0f, 0f);
}

/// <summary>
/// C083: code-driven motion of ships, siege, armour and aircraft, which have no clips: a ship bobs and rolls under way and when it fires, siege and armour kick back when they shoot, armour swings
/// its turret on to the target, aircraft bank in flight and dive when they strike. Pure functions of the cue and the presenter's clock, so goldens and manual stepping stay exact.
/// </summary>
public static class RigidMotion
{
    public const float BobSeconds = 1.2f;
    public const float ShipBob = 0.03f;
    public const float ShipRoll = 0.08f;
    public const float SiegeRecoil = 0.14f;
    public const float ArmorRecoil = 0.07f;
    public const float ShipRecoil = 0.04f;
    public const float TurretSweep = 0.6f;
    public const float BankAngle = 0.35f;
    public const float DiveAngle = 0.45f;

    /// <summary>The rigid family of a unit type name (as <c>Models.Ruleset.Unit.BaseUnit.unitType</c> and the placeholder accessories name them, or an aircraft's own type name).</summary>
    public static RigidKind KindOf(string? unitType) => unitType switch
    {
        "WaterMelee" or "Melee Water" or "WaterRanged" or "Ranged Water" or "WaterCivilian" or "Civilian Water" or "WaterSubmarine" or "Submarine" or "WaterAircraftCarrier" or "Aircraft Carrier" => RigidKind.Ship,
        "Siege" => RigidKind.Siege,
        "Armor" or "Armored" => RigidKind.Armor,
        "Fighter" or "Bomber" or "AtomicBomber" or "Atomic Bomber" or "Helicopter" or "Missile" => RigidKind.Air,
        _ => RigidKind.None,
    };

    /// <summary>
    /// The pose of a <paramref name="kind"/> figure during a cue of <paramref name="cue"/> at <paramref name="progress"/> (0..1) of it, <paramref name="seconds"/> into it. Cues that do not move a rigid
    /// body (a hit, a death, a city's bombard) leave it at rest: the presenter's own shake, shrink and sink handle those.
    /// </summary>
    public static RigidPose Pose(RigidKind kind, CueKind cue, float progress, float seconds)
    {
        if (kind == RigidKind.None || !float.IsFinite(progress) || !float.IsFinite(seconds)) return RigidPose.Rest;
        var p = Math.Clamp(progress, 0f, 1f);
        var kick = CuePoses.Bump(p);
        switch (cue)
        {
            case CueKind.Move:
                return kind switch
                {
                    RigidKind.Ship => new RigidPose(0f, ShipBob * MathF.Sin(2f * MathF.PI * seconds / BobSeconds), 0f, ShipRoll * MathF.Sin(2f * MathF.PI * seconds / (BobSeconds * 1.5f)), 0f),
                    RigidKind.Air => new RigidPose(0f, 0f, 0f, BankAngle * kick, 0f),
                    _ => RigidPose.Rest,
                };
            case CueKind.Strike or CueKind.Shot:
                return kind switch
                {
                    RigidKind.Ship => new RigidPose(ShipRecoil * kick, ShipBob * kick, 0f, ShipRoll * kick, 0f),
                    RigidKind.Siege => new RigidPose(SiegeRecoil * kick, 0f, 0.1f * kick, 0f, 0f),
                    RigidKind.Armor => new RigidPose(ArmorRecoil * kick, 0f, 0f, 0f, TurretSweep * (1f - Math.Min(1f, p * 3f))),
                    RigidKind.Air => new RigidPose(0f, 0f, -DiveAngle * kick, 0f, 0f),
                    _ => RigidPose.Rest,
                };
            default:
                return RigidPose.Rest;
        }
    }
}
