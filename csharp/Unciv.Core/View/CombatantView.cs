using Unciv.Logic.Battle;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using CityClass = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.View;

// Ported from core/src/com/unciv/view/CombatantView.kt (T085). Stripped: the purity annotations.
// Kotlin's `sealed class CombatantView` is an abstract class with an internal constructor here (only MapUnitCombatantView and
// CityCombatantView, in this assembly, derive from it). The Kotlin comment on getCombatantName ("named to avoid clashing with the
// JVM getter of the `name` property") does not apply to C#, but the name is kept.

/// <summary>
/// View of an <see cref="ICombatant"/> from the perspective of the viewer via the <see cref="GameView"/>. Get one via
/// <see cref="ForeignMapUnitView.AsCombatant"/> or <see cref="ForeignCityView.AsCombatant"/> - which return the richer
/// <see cref="MapUnitCombatantView"/>/<see cref="CityCombatantView"/> subclasses when the underlying view carries enough
/// information to also dispatch battle actions (movement, nuke, air sweep, bombard).
/// </summary>
public abstract class CombatantView : GameBasedView<ICombatant>
{
    private readonly ICombatant combatant;

    internal CombatantView(ICombatant combatant, CivilizationClass viewer, bool spectatorMode, GameView gameView)
        : base(combatant, viewer, spectatorMode, gameView)
    {
        this.combatant = combatant;
    }

    public ForeignCivView GetCivInfo() => GameView.GetForeignCivView(combatant.GetCivInfo());

    public TileView GetTile() => GameView.GetTile(combatant.GetTile());

    public string GetCombatantName() => combatant.GetName();

    public int GetHealth() => combatant.GetHealth();

    public int GetMaxHealth() => combatant.GetMaxHealth();

    public UnitType GetUnitType() => combatant.GetUnitType();

    public int GetAttackingStrength(CombatantView? defender = null) => combatant.GetAttackingStrength(defender?.Unwrap());

    public int GetDefendingStrength(CombatantView? attacker = null) => combatant.GetDefendingStrength(attacker?.Unwrap());

    public bool IsDefeated() => combatant.IsDefeated();

    public bool CanAttack() => combatant.CanAttack();

    public UncivSound GetAttackSound() => combatant.GetAttackSound();

    public bool IsRanged() => combatant.IsRanged();

    public bool IsCity() => combatant.IsCity();

    // Battle math - identical for unit and city combatants
    public Counter<string> GetAttackModifiers(CombatantView defender, TileView tileToAttackFrom) =>
        BattleDamage.GetAttackModifiers(combatant, defender.Unwrap(), tileToAttackFrom.Unwrap());

    public Counter<string> GetDefenceModifiers(CombatantView attacker, TileView tileToAttackFrom) =>
        BattleDamage.GetDefenceModifiers(attacker.Unwrap(), combatant, tileToAttackFrom.Unwrap());

    /// <summary>Attacking strength including modifiers - as opposed to <see cref="GetAttackingStrength"/>, which is the base strength.</summary>
    public float GetFinalAttackingStrength(CombatantView defender, TileView tileToAttackFrom) =>
        BattleDamage.GetAttackingStrength(combatant, defender.Unwrap(), tileToAttackFrom.Unwrap());

    /// <summary>Defending strength including modifiers - as opposed to <see cref="GetDefendingStrength"/>, which is the base strength.</summary>
    public float GetFinalDefendingStrength(CombatantView attacker, TileView tileToAttackFrom) =>
        BattleDamage.GetDefendingStrength(attacker.Unwrap(), combatant, tileToAttackFrom.Unwrap());

    /// <summary>Between 0f (min damage) and 1f (max damage) via <paramref name="randomnessFactor"/>.</summary>
    public int CalculateDamageToDefender(CombatantView defender, TileView tileToAttackFrom, float randomnessFactor) =>
        BattleDamage.CalculateDamageToDefender(combatant, defender.Unwrap(), tileToAttackFrom.Unwrap(), randomnessFactor);

    /// <summary>Between 0f (min damage) and 1f (max damage) via <paramref name="randomnessFactor"/>.</summary>
    public int CalculateDamageToAttacker(CombatantView defender, TileView tileToAttackFrom, float randomnessFactor) =>
        BattleDamage.CalculateDamageToAttacker(combatant, defender.Unwrap(), tileToAttackFrom.Unwrap(), randomnessFactor);
}

/// <summary>
/// A <see cref="CombatantView"/> of a unit - carries the <see cref="ForeignMapUnitView"/> it was built from. Use <see cref="ForeignMapUnitView.TryGetMapUnitView"/>
/// on <see cref="GetUnitView"/> to dispatch battle actions (movement, attack, nuke, air sweep) - only possible for our own units.
/// </summary>
public sealed class MapUnitCombatantView : CombatantView
{
    private readonly ForeignMapUnitView unitView;
    private readonly MapUnit unit;

    internal MapUnitCombatantView(ForeignMapUnitView unitView, MapUnit unit, CivilizationClass viewer, bool spectatorMode, GameView gameView)
        : base(new MapUnitCombatant(unit), viewer, spectatorMode, gameView)
    {
        this.unitView = unitView;
        this.unit = unit;
    }

    public ForeignMapUnitView GetUnitView() => unitView;

    public bool HasUnique(UniqueType uniqueType) => unitView.HasUnique(uniqueType);

    /// <summary>(max, min) bonus damage dealt to <paramref name="defender"/> from an additional <see cref="UniqueType.ExtraRangedAttack"/> - <c>(0, 0)</c> if not applicable.</summary>
    public (int, int) GetExtraRangedAttackBonusDamage(CombatantView defender, TileView tileToAttackFrom) =>
        BattleDamage.GetExtraRangedAttackBonusDamage(new MapUnitCombatant(unit), defender.Unwrap(), tileToAttackFrom.Unwrap());
}

/// <summary>A <see cref="CombatantView"/> of a city - carries the full <see cref="ForeignCityView"/>, so battle actions (bombard) can be dispatched directly.</summary>
public sealed class CityCombatantView : CombatantView
{
    private readonly ForeignCityView cityView;

    internal CityCombatantView(ForeignCityView cityView, CityClass city, CivilizationClass viewer, bool spectatorMode, GameView gameView)
        : base(new CityCombatant(city), viewer, spectatorMode, gameView)
    {
        this.cityView = cityView;
    }

    public ForeignCityView GetCityView() => cityView;

    /// <summary>Builds the <see cref="AttackableTileView"/> this city bombard needs (no movement, so no tileToAttackFrom calculation required).</summary>
    public AttackableTileView BuildBombardAttackableTile(
        TileView toTile, CombatantView defender,
        CivilizationClass viewer, bool spectatorMode, GameView gameView) =>
        new(new AttackableTile(cityView.GetCenterTile().Unwrap(), toTile.Unwrap(), 0f, defender.Unwrap()),
            viewer, spectatorMode, gameView);
}
