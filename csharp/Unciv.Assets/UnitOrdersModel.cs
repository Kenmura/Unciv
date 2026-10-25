using System.Globalization;
using Unciv.Commands.Units;
using Unciv.Models.Ruleset.Unit;
using Unciv.Net.Visibility;
using Unciv.Queries.Tiles;
using Unciv.Queries.Units;

namespace Unciv.Assets;

/// <summary>The unit panel's text lines (C012). <see cref="Order"/> is the standing order, empty when the unit has none.</summary>
public sealed record UnitPanelInfo(string Title, string Strength, string Moves, string Health, string Xp, IReadOnlyList<string> Promotions, string Order);

/// <summary>Where an entry of the action menu comes from; the client sends a different command for each.</summary>
public enum MenuEntryKind
{
    /// <summary>One of the unit-actions query's actions: sent as <see cref="ExecuteUnitAction"/> with its descriptor.</summary>
    Engine,

    /// <summary>A tile improvement the unit can place here (tile query, <see cref="SetWorkerOrder"/>); <see cref="MenuEntry.Argument"/> is its name.</summary>
    Improve,

    /// <summary>A promotion the unit may take now (promotion query, <see cref="PromoteUnit"/>); <see cref="MenuEntry.Argument"/> is its name.</summary>
    Promote,

    /// <summary>Disband (<see cref="DisbandUnit"/>), always last.</summary>
    Disband,
}

/// <summary>One button of the action menu. <see cref="Action"/> is set for <see cref="MenuEntryKind.Engine"/>; <see cref="Reason"/> says why a disabled entry is.</summary>
public sealed record MenuEntry(MenuEntryKind Kind, string Label, bool Enabled, UnitActionInfo? Action = null, string? Argument = null, string? Reason = null);

/// <summary>The battle table as text lines. <see cref="ConfirmLabel"/> names the confirm button; <see cref="CanConfirm"/> false leaves only Cancel.</summary>
public sealed record CombatPreviewText(string Title, IReadOnlyList<string> Lines, bool CanConfirm, string ConfirmLabel);

/// <summary>What a click on a tile means to the selected unit.</summary>
public enum TileClickIntent
{
    None,
    SelectOwn,
    AttackPreview,
    PathPreview,
}

/// <summary>
/// C012: the unit panel, the action menu and the combat preview as plain data, Godot-free so it is unit-tested. Every number comes from an engine query (unit
/// actions, battle preview, attackable targets, reachable tiles, tile info, promotion choices); this class lists, orders and words them and computes no game value.
/// The one derived number is the health range after the fight, which is plain subtraction of the query's damage range from the query's health.
/// </summary>
public static class UnitOrdersModel
{
    public const string DisbandLabel = "Disband unit";
    public const string PromoteLabelPrefix = "Promote: ";
    public const string ImproveLabelPrefix = "Build: ";

    /// <summary>The panel for one of the viewer's units. <paramref name="baseUnit"/> (static ruleset) supplies the strengths; null when the ruleset does not know the name.</summary>
    public static UnitPanelInfo Panel(UnitDto unit, BaseUnit? baseUnit)
    {
        var own = unit.Own;
        string strength = "";
        if (baseUnit != null && (baseUnit.Strength > 0 || baseUnit.RangedStrength > 0))
        {
            strength = baseUnit.RangedStrength > 0
                ? $"Ranged {Int(baseUnit.RangedStrength)}, range {Int(baseUnit.Range)}, melee {Int(baseUnit.Strength)}"
                : $"Combat {Int(baseUnit.Strength)}";
        }
        string moves = own == null ? "" : baseUnit == null ? $"Moves {Float(own.CurrentMovement)}" : $"Moves {Float(own.CurrentMovement)}/{Int(baseUnit.Movement)}";
        string health = own == null ? $"Health {Int(unit.HealthBucket * 25)}%" : $"Health {Int(own.Health)}/100";
        string xp = own == null ? "" : $"XP {Int(own.Xp)}";
        string order = own?.Action is { Length: > 0 } action ? action : unit.IsFortified ? "Fortified" : "";
        return new UnitPanelInfo(unit.Name, strength, moves, health, xp, own?.Promotions ?? [], order);
    }

    /// <summary>
    /// The action menu: the unit-actions query's actions in the engine's order (so the menu equals the query), then the "UI-only" actions Kotlin builds in the screen:
    /// improvements the unit could place here (<paramref name="tile"/> asked with the unit), selectable promotions, and Disband while the unit has movement.
    /// </summary>
    public static IReadOnlyList<MenuEntry> Menu(UnitActionsResult actions, TileInfoResult? tile, PromotionChoicesResult? promotions, bool hasMovement)
    {
        var entries = new List<MenuEntry>();
        foreach (var action in actions.Actions)
            entries.Add(new MenuEntry(MenuEntryKind.Engine, Clean(action.Title), action.IsEnabled, action));
        foreach (var option in tile?.ImprovementOptions ?? [])
        {
            bool possible = option.Problems.Count == 0;
            entries.Add(new MenuEntry(MenuEntryKind.Improve, ImproveLabelPrefix + option.Name, possible, null, option.Name,
                possible ? null : string.Join(", ", option.Problems)));
        }
        foreach (var promotion in promotions?.Promotions ?? [])
            entries.Add(new MenuEntry(MenuEntryKind.Promote, PromoteLabelPrefix + promotion.Name, promotion.Selectable, null, promotion.Name,
                promotion.Selectable ? null : "Not enough experience"));
        if (hasMovement) entries.Add(new MenuEntry(MenuEntryKind.Disband, DisbandLabel, true));
        return entries;
    }

    /// <summary>A translation template as plain text: the placeholder brackets and braces are dropped ("Upgrade to [Swordsman] ([50] gold)" reads "Upgrade to Swordsman (50 gold)").</summary>
    public static string Clean(string title) =>
        title.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal)
            .Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal).Trim();

    /// <summary>The tiles the unit can end a move on this turn (the reachable-tiles overlay).</summary>
    public static IReadOnlyList<(int X, int Y)> ReachableTiles(ReachableTilesResult reachable) =>
        reachable.Tiles.Where(it => it.CanEnter).Select(it => (it.X, it.Y)).ToList();

    /// <summary>The tiles the unit could attack (the attack-range overlay), empty when it cannot attack now.</summary>
    public static IReadOnlyList<(int X, int Y)> AttackTiles(AttackableTargetsResult targets) =>
        targets.CanAttack ? targets.Targets.Select(it => (it.X, it.Y)).ToList() : [];

    /// <summary>
    /// What a click on <paramref name="tile"/> means. Own units win (a click on a stack cycles through it), then an attackable enemy gets the battle preview; any other
    /// tile gets a path preview when a unit is selected.
    /// </summary>
    public static TileClickIntent ClickIntent((int X, int Y) tile, bool ownUnitHere, bool unitSelected, IReadOnlySet<(int X, int Y)> attackTiles)
    {
        if (ownUnitHere) return TileClickIntent.SelectOwn;
        if (!unitSelected) return TileClickIntent.None;
        return attackTiles.Contains(tile) ? TileClickIntent.AttackPreview : TileClickIntent.PathPreview;
    }

    /// <summary>The battle table of the world screen as text: both fighters with their modifiers, the damage ranges and the outcome.</summary>
    public static CombatPreviewText Combat(AttackPreviewResult preview)
    {
        var lines = new List<string>();
        string title = preview.Kind switch
        {
            "nuke" => $"Nuclear strike (blast radius {Int(preview.BlastRadius)})",
            "airsweep" => "Air sweep",
            _ => $"{preview.Attacker?.Name ?? "?"} attacks {preview.Defender?.Name ?? "?"}",
        };
        if (preview.Attacker != null) AddFighter(lines, preview.Attacker);
        if (preview.Defender != null) AddFighter(lines, preview.Defender);
        if (preview.Kind == "battle" && preview.Attacker != null && preview.Defender != null) AddBattleOutcome(lines, preview, preview.Attacker, preview.Defender);
        if (!preview.CanAttack) lines.Add("Cannot attack right now");
        string confirm = preview.Kind switch { "nuke" => "Launch", "airsweep" => "Sweep", _ => "Attack" };
        return new CombatPreviewText(title, lines, preview.CanAttack, confirm);
    }

    private static void AddFighter(List<string> lines, CombatantPreview fighter)
    {
        lines.Add($"{fighter.Name} ({fighter.Civ}): strength {Int(fighter.FinalStrength)}, health {Int(fighter.Health)}/{Int(fighter.MaxHealth)}");
        foreach (var modifier in fighter.Modifiers)
            lines.Add($"    {(modifier.Percent >= 0 ? "+" : "-")}{Int(Math.Abs(modifier.Percent))}% {modifier.Key}");
    }

    private static void AddBattleOutcome(List<string> lines, AttackPreviewResult preview, CombatantPreview attacker, CombatantPreview defender)
    {
        string att = Label(attacker);
        string def = Label(defender);
        switch (preview.Outcome)
        {
            case "capture":
                lines.Add($"Captures {def}");
                return;
            case "occupy":
                lines.Add($"Takes {def}");
                return;
            case "none":
                lines.Add($"{def} cannot be captured");
                return;
        }
        lines.Add($"Damage to {def}: {Range(preview.DamageToDefenderMin, preview.DamageToDefenderMax)}");
        if (preview.DamageToAttackerMax > 0) lines.Add($"Damage to {att}: {Range(preview.DamageToAttackerMin, preview.DamageToAttackerMax)}");
        lines.Add($"{def} health afterwards: {Range(Math.Max(0, defender.Health - preview.DamageToDefenderMax), Math.Max(0, defender.Health - preview.DamageToDefenderMin))}");
        if (preview.DamageToDefenderMin >= defender.Health) lines.Add($"{def} will be destroyed");
        else if (preview.DamageToDefenderMax >= defender.Health) lines.Add($"{def} may be destroyed");
        if (preview.DamageToAttackerMin >= attacker.Health) lines.Add($"{att} will be destroyed");
        else if (preview.DamageToAttackerMax >= attacker.Health) lines.Add($"{att} may be destroyed");
    }

    // The civ is part of the name: both fighters can be the same unit type
    private static string Label(CombatantPreview fighter) => $"{fighter.Name} ({fighter.Civ})";

    private static string Range(int min, int max) => min == max ? Int(min) : $"{Int(min)}-{Int(max)}";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Float(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
