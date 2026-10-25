using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Queries.Units;

// Key: Translation key of the modifier, as the engine names it
// Percent: Signed percentage
public sealed record StrengthModifier(string Key, int Percent);

// BaseStrength: Strength before modifiers
// FinalStrength: Strength with the modifiers, rounded half up as the battle table shows it
public sealed record CombatantPreview(
    string Name, string Civ, bool IsCity, bool IsRanged, int Health, int MaxHealth,
    int BaseStrength, int FinalStrength, IReadOnlyList<StrengthModifier> Modifiers);

// Kind: "battle", "nuke" or "airsweep"
// CanAttack: Whether the attack is possible right now (target in the attackable list, attacker able to attack); nuke: MayUseNuke
// Outcome: battle: "damage", "capture" (civilian taken), "occupy" (empty city taken) or "none"; otherwise null
// AttackFrom: Tile the attack is made from
// DamageToDefenderMin: Damage range as the engine rolls it (randomness factor 0 and 1, extra ranged attacks added), unclamped to the health
// BlastRadius: nuke only
public sealed record AttackPreviewResult(
    string Kind, bool CanAttack, string? Outcome, TilePos Target, TilePos? AttackFrom,
    CombatantPreview? Attacker, CombatantPreview? Defender,
    int DamageToDefenderMin, int DamageToDefenderMax, int DamageToAttackerMin, int DamageToAttackerMax,
    bool ExtraRangedAttack, bool NoXpFromBarbarians, int BlastRadius);

// T195: the battle table of the world screen as data (BattleTable.simulateBattle / simulateNuke / simulateAirsweep). Strengths,
// modifiers and damage come from BattleDamage through CombatantView, the defender is what the viewer sees on the tile.
// The damage range uses randomness factors 0 and 1 (the Kotlin table does too), so it never draws from the state RNG.
// The attacker is a unit of the viewer (UnitId) or a city of the viewer (CityId, bombard). FromX/FromY pick the tile of a
// moving melee attack; by default the tile the engine would attack from (as the Kotlin table does), else the attacker's tile.
[QueryType("units.AttackPreview")]
public sealed record AttackPreviewQuery(int X, int Y, int? UnitId = null, string? CityId = null, int? FromX = null, int? FromY = null)
    : IQuery<AttackPreviewResult>
{
    public string Type => "units.AttackPreview";

    public AttackPreviewResult Run(GameView view)
    {
        UnitQueryHelpers.RequireOneAttacker(UnitId, CityId);
        if ((FromX == null) != (FromY == null))
            throw new QueryException(UnitQueryCodes.BadRequest, "Give both of FromX and FromY or neither");
        var target = UnitQueryHelpers.ExploredTile(view, X, Y);
        var targetPos = UnitQueryHelpers.Pos(target.Position());

        MapUnitView? unit = null;
        CombatantView attacker;
        if (CityId != null)
        {
            attacker = UnitQueryHelpers.OwnCity(view, CityId).AsCombatant();
        }
        else
        {
            unit = UnitQueryHelpers.OwnUnit(view, UnitId.NotNull());
            attacker = unit.AsCombatant();
            if (unit.IsNuclearWeapon()) return Nuke(unit, target, targetPos);
            if (unit.IsPreparingAirSweep()) return AirSweep(unit, attacker, target, targetPos);
            // BattleTable.tryGetAttackerUnit shows no table for these
            if (unit.IsCivilian() || unit.HasUnique(UniqueType.CannotAttack))
                throw new QueryException(UnitQueryCodes.CannotAttack, "This unit cannot attack");
        }

        var defender = target.GetCombatant();
        if (defender == null || defender.GetCivInfo().Equals(attacker.GetCivInfo()))
            throw new QueryException(UnitQueryCodes.NoTarget, "There is no enemy combatant on that tile");
        // BattleTable shows no bombard table against a city
        if (unit == null && defender.IsCity())
            throw new QueryException(UnitQueryCodes.NoTarget, "A city cannot bombard a city");

        var (from, canAttack) = ChooseOrigin(unit, attacker, target);
        var fromPos = from.Position();
        return Battle(attacker, defender, from, canAttack, targetPos, new TilePos(fromPos.X, fromPos.Y));
    }

    private (TileView From, bool CanAttack) ChooseOrigin(MapUnitView? unit, CombatantView attacker, TileView target)
    {
        if (unit == null)
        {
            var city = (CityCombatantView)attacker;
            bool canBombard = city.GetCityView().GetBombardableTiles().Contains(target);
            return (attacker.GetTile(), canBombard && attacker.CanAttack());
        }
        var options = unit.GetAttackableEnemies().Where(it => it.GetTileToAttack().Equals(target)).ToList();
        if (FromX is { } fromX && FromY is { } fromY)
        {
            var chosen = options.FirstOrDefault(it => it.GetTileToAttackFrom().Position() == new Logic.Map.HexCoord(fromX, fromY));
            if (chosen == null) throw new QueryException(UnitQueryCodes.InvalidAttackFrom, "That target cannot be attacked from the given tile");
            return (chosen.GetTileToAttackFrom(), attacker.CanAttack());
        }
        return options.Count == 0 ? (unit.GetTile(), false) : (options[0].GetTileToAttackFrom(), attacker.CanAttack());
    }

    private static AttackPreviewResult Battle(CombatantView attacker, CombatantView defender, TileView from, bool canAttack, TilePos target, TilePos fromPos)
    {
        int maxToDefender = attacker.CalculateDamageToDefender(defender, from, 1f);
        int minToDefender = attacker.CalculateDamageToDefender(defender, from, 0f);
        int maxToAttacker = attacker.CalculateDamageToAttacker(defender, from, 1f);
        int minToAttacker = attacker.CalculateDamageToAttacker(defender, from, 0f);

        bool extraRanged = !defender.IsCity() && attacker is MapUnitCombatantView unitAttacker && unitAttacker.HasUnique(UniqueType.ExtraRangedAttack);
        if (extraRanged)
        {
            var (maxExtra, minExtra) = ((MapUnitCombatantView)attacker).GetExtraRangedAttackBonusDamage(defender, from);
            maxToDefender += maxExtra;
            minToDefender += minExtra;
        }

        var defenderUnit = (defender as MapUnitCombatantView)?.GetUnitView();
        bool defenderCivilian = defenderUnit != null && defenderUnit.IsCivilian();
        string outcome = "damage";
        if (!attacker.IsRanged() && (defenderCivilian || (defender.IsCity() && defender.IsDefeated())))
        {
            outcome = defenderCivilian ? (((MapUnitCombatantView)defender).HasUnique(UniqueType.Uncapturable) ? "none" : "capture") : "occupy";
            maxToDefender = minToDefender = maxToAttacker = minToAttacker = 0;
        }

        bool noXp = attacker is MapUnitCombatantView xpAttacker && xpAttacker.GetUnitView().HasReachedMaxXPFromBarbarians()
            && defender.GetCivInfo().Unwrap().IsBarbarian;

        return new AttackPreviewResult("battle", canAttack, outcome, target, fromPos,
            Describe(attacker, attacker, defender, from, false), Describe(defender, attacker, defender, from, true),
            minToDefender, maxToDefender, minToAttacker, maxToAttacker, extraRanged, noXp, 0);
    }

    private static CombatantPreview Describe(CombatantView subject, CombatantView attacker, CombatantView defender, TileView from, bool isDefender)
    {
        // The Kotlin table shows no defence modifiers for a city
        var counter = isDefender
            ? (subject.IsCity() ? null : subject.GetDefenceModifiers(attacker, from))
            : subject.GetAttackModifiers(defender, from);
        var modifiers = counter?.Select(it => new StrengthModifier(it.Key, it.Value)).ToList() ?? [];
        float final = isDefender ? subject.GetFinalDefendingStrength(attacker, from) : subject.GetFinalAttackingStrength(defender, from);
        int baseStrength = isDefender ? subject.GetDefendingStrength(attacker) : subject.GetAttackingStrength(defender);
        return new CombatantPreview(subject.GetCombatantName(), subject.GetCivInfo().Unwrap().CivName, subject.IsCity(), subject.IsRanged(),
            subject.GetHealth(), subject.GetMaxHealth(), baseStrength, (int)Math.Floor(final + 0.5f), modifiers);
    }

    private static AttackPreviewResult Nuke(MapUnitView unit, TileView target, TilePos targetPos)
    {
        var from = unit.GetTile();
        bool inRange = from.Unwrap().AerialDistanceTo(target.Unwrap()) <= unit.GetRange();
        return new AttackPreviewResult("nuke", inRange && unit.MayUseNuke(target) && unit.AsCombatant().CanAttack(), null, targetPos,
            UnitQueryHelpers.Pos(from.Position()), null, null, 0, 0, 0, 0, false, false, unit.GetNukeBlastRadius());
    }

    // BattleTable.simulateAirsweep: the defender is unknown ("???"), any tile can be selected, it only has to be in range.
    private static AttackPreviewResult AirSweep(MapUnitView unit, CombatantView attacker, TileView target, TilePos targetPos)
    {
        var from = unit.GetTile();
        bool inRange = unit.GetTilesInAttackRange().Contains(target);
        var modifiers = unit.GetAirSweepAttackModifiers().Select(it => new StrengthModifier(it.Key, it.Value)).ToList();
        var preview = new CombatantPreview(attacker.GetCombatantName(), attacker.GetCivInfo().Unwrap().CivName, false, attacker.IsRanged(),
            attacker.GetHealth(), attacker.GetMaxHealth(), attacker.GetAttackingStrength(), attacker.GetAttackingStrength(), modifiers);
        return new AttackPreviewResult("airsweep", inRange && attacker.CanAttack(), null, targetPos, UnitQueryHelpers.Pos(from.Position()),
            preview, null, 0, 0, 0, 0, false, false, 0);
    }
}
