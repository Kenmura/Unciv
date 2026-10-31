using Unciv.Models.Ruleset.Unit;
using Unciv.Net.Visibility;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Assets;

/// <summary>The status marks a unit can wear on the map, in drawing order.</summary>
public enum UnitStatusIcon
{
    Fortified,
    Sleeping,
    Automated,
    PromotionAvailable,
}

/// <summary>Where one figure of a unit stands: offset from the tile-top anchor in tile units, and the scale of the figure relative to a lone one.</summary>
public readonly record struct FigurePlacement(float X, float Z, float Scale);

/// <summary>A unit's health bar: the filled fraction (0..1), its colour, and whether to show it at all (hidden at full health).</summary>
public readonly record struct HealthBarSpec(bool Visible, float Fraction, Rgb Fill);

/// <summary>
/// C004: what a unit looks like on the map, as plain data. How many figures it gets (a strength tier of the <c>BaseUnit</c>), where they stand, the health bar and the status
/// marks. Reads only the viewer's snapshot row plus the ruleset's unit; the Godot side (<c>Unciv.Godot/Play/UnitNode.cs</c>) turns the answers into nodes. Pure and deterministic.
/// </summary>
public static class UnitVisuals
{
    /// <summary>Strength at which a unit gets its 2nd, 3rd and 4th figure (the tier rule). Below the first threshold, and for civilians, ships and aircraft, a unit is one figure.</summary>
    public static readonly IReadOnlyList<int> TierThresholds = [11, 26, 46];

    /// <summary>Largest figure count; equals tier thresholds + 1.</summary>
    public static int MaxFigures => TierThresholds.Count + 1;

    /// <summary>Figures for a land military unit by its better strength (melee or ranged); everything else is a single figure.</summary>
    public static int FigureCount(int strength, int rangedStrength, bool isLandMilitary)
    {
        if (!isLandMilitary) return 1;
        var power = Math.Max(strength, rangedStrength);
        var count = 1;
        foreach (var threshold in TierThresholds) if (power >= threshold) count++;
        return count;
    }

    /// <summary>Figures of <paramref name="unit"/>: land military units by strength tier, all others one. The unit type is looked up in <paramref name="ruleset"/> by name, so it does not matter whether the unit has been attached to its ruleset.</summary>
    public static int FigureCount(BaseUnit unit, RulesetClass ruleset)
    {
        var type = ruleset.UnitTypes.GetValueOrDefault(unit.UnitType);
        return FigureCount(unit.Strength, unit.RangedStrength, unit.IsMilitary && type != null && !type.IsWaterUnit() && !type.IsAirUnit());
    }

    /// <summary>T383: factor on the scale of every humanoid (skeleton) figure: foot soldiers were about 40 px high at the game's default zoom and the owner wants every unit recognised at any view. Rigid models (ships, armour, aircraft) are modelled to size and keep 1.</summary>
    public const float FootFigureBoost = 1.3f;

    /// <summary>The arrangement of <paramref name="count"/> figures (clamped to 1..<see cref="MaxFigures"/>) on a tile: a lone figure in the middle, a pair side by side, a triangle, a square. Figures shrink as they multiply so the group stays inside the tile.</summary>
    public static IReadOnlyList<FigurePlacement> Formation(int count) => Math.Clamp(count, 1, MaxFigures) switch
    {
        1 => [new FigurePlacement(0f, 0f, 1f)],
        2 => [new FigurePlacement(-0.2f, 0f, 0.8f), new FigurePlacement(0.2f, 0f, 0.8f)],
        3 => [new FigurePlacement(-0.22f, 0.14f, 0.72f), new FigurePlacement(0.22f, 0.14f, 0.72f), new FigurePlacement(0f, -0.2f, 0.72f)],
        _ => [new FigurePlacement(-0.22f, 0.2f, 0.65f), new FigurePlacement(0.22f, 0.2f, 0.65f), new FigurePlacement(-0.22f, -0.2f, 0.65f), new FigurePlacement(0.22f, -0.2f, 0.65f)],
    };

    /// <summary>Marks of <paramref name="unit"/> in fixed order. Fortified is public; sleeping, automated (the automation flag or auto-explore) and promotion available come from the owner's detail only, so foreign units never show them.</summary>
    public static IReadOnlyList<UnitStatusIcon> StatusIcons(UnitDto unit)
    {
        var icons = new List<UnitStatusIcon>();
        if (unit.IsFortified) icons.Add(UnitStatusIcon.Fortified);
        if (unit.Own is not { } own) return icons;
        if (own.Action?.StartsWith("Sleep", StringComparison.Ordinal) == true) icons.Add(UnitStatusIcon.Sleeping);
        if (own.Automated || own.Action == "Explore") icons.Add(UnitStatusIcon.Automated);
        if (own.CanBePromoted) icons.Add(UnitStatusIcon.PromotionAvailable);
        return icons;
    }

    /// <summary>Health bar from the exact health for the owner (out of 100) and from the quarter bucket for everybody else; hidden at full health. Green above two thirds, amber above one third, red below.</summary>
    public static HealthBarSpec HealthBar(UnitDto unit)
    {
        var fraction = unit.Own is { } own ? Math.Clamp(own.Health, 0, 100) / 100f : Math.Clamp(unit.HealthBucket, 0, 4) / 4f;
        var colour = fraction > 2f / 3f ? PlaceholderPalette.HealthGood : fraction > 1f / 3f ? PlaceholderPalette.HealthWarn : PlaceholderPalette.HealthBad;
        return new HealthBarSpec(fraction < 1f, fraction, colour);
    }

    /// <summary>The icon key for a status mark; the generator and any override use this one name.</summary>
    public static AssetKey IconKey(UnitStatusIcon icon) => AssetKey.Of(RulesetAssetKeys.StatusIcon, icon.ToString());
}
