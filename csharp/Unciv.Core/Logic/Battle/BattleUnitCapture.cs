using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/BattleUnitCapture.kt (T082). Purity annotations dropped.
// Battle.triggerDefeatUniques / Battle.destroyIfDefeated are Battle.TriggerDefeatUniques / Battle.DestroyIfDefeated (T083).
public static class BattleUnitCapture
{
    public static bool TryCaptureMilitaryUnit(ICombatant attacker, ICombatant defender, TileClass attackedTile)
    {
        // https://forums.civfanatics.com/threads/prize-ships-for-land-units.650196/
        // https://civilization.fandom.com/wiki/Module:Data/Civ5/GK/Defines\
        // There are 3 ways of capturing a unit, we separate them for cleaner code but we also need to ensure a unit isn't captured twice

        if (defender is not MapUnitCombatant mapDefender || attacker is not MapUnitCombatant mapAttacker) return false;
        if (mapDefender.HasUnique(UniqueType.Uncapturable, new GameContext(unit: mapDefender.Unit,
                ourCombatant: mapDefender, theirCombatant: mapAttacker, attackedTile: attackedTile)))
            return false;

        if (!mapDefender.IsDefeated() || mapDefender.Unit.IsCivilian()) return false;

        // Due to the way OR operators short-circuit, calling just A() || B() means B isn't called if A is true.
        // Therefore we run all functions before checking if one is true.
        bool wasUnitCaptured = new[]
        {
            UnitCapturedPrizeShipsUnique(mapAttacker, mapDefender),
            UnitGainFromDefeatingUnit(mapAttacker, mapDefender),
        }.Any(it => it);

        if (!wasUnitCaptured) return false;

        // This is called after takeDamage and so the defeated defender is already destroyed and
        // thus removed from the tile - but MapUnit.destroy() will not clear the unit's currentTile.
        // Therefore placeUnitNearTile _will_ place the new unit exactly where the defender was
        return SpawnCapturedUnit(mapDefender, mapAttacker);
    }

    private static bool UnitCapturedPrizeShipsUnique(MapUnitCombatant attacker, MapUnitCombatant defender)
    {
        if (!attacker.Unit.GetMatchingUniques(UniqueType.KillUnitCapture)
                .Any(it => defender.MatchesFilter(it.Params[0])))
            return false;

        float captureChance = MathF.Min(
            0.8f,
            0.1f + (float)attacker.GetAttackingStrength(defender) / (float)defender.GetDefendingStrength(attacker) * 0.4f);
        /* Between 0 and 1.  Defaults to turn and location-based random to avoid save scumming */
        // Kotlin: (turns * hashCode).toLong() multiplies as Int (wrapping) before widening
        var random = attacker.GetCivInfo().GameInfo.SeededRandom(unchecked((long)unchecked(attacker.GetCivInfo().GameInfo.Turns * defender.GetTile().Position.ToVector2().GetHashCode())));
        return random.NextFloat() <= captureChance;
    }

    private static bool UnitGainFromDefeatingUnit(MapUnitCombatant attacker, MapUnitCombatant defender)
    {
        if (!attacker.IsMelee()) return false;
        bool unitCaptured = false;
        var state = new GameContext(attacker.GetCivInfo(), ourCombatant: attacker, theirCombatant: defender);
        foreach (var unique in attacker.GetMatchingUniques(UniqueType.GainFromDefeatingUnit, state, true))
        {
            if (defender.Unit.MatchesFilter(unique.Params[0]))
            {
                attacker.GetCivInfo().AddGold(unique.Params[1].ToInt());
                unitCaptured = true;
            }
        }
        return unitCaptured;
    }

    /// <summary>
    /// Places a unitName unit near tile after being attacked by <paramref name="attacker"/>.
    /// Adds a notification to <paramref name="attacker"/>'s civInfo and returns whether the captured unit could be placed
    /// </summary>
    private static bool SpawnCapturedUnit(MapUnitCombatant defender, MapUnitCombatant attacker)
    {
        var defenderTile = defender.GetTile();
        var addedUnit = attacker.GetCivInfo().Units.PlaceUnitNearTile(defenderTile.Position, defender.GetName());
        if (addedUnit == null) return false;
        addedUnit.CurrentMovement = 0f;
        addedUnit.Health = 50;
        attacker.GetCivInfo().AddNotification($"An enemy [{defender.GetName()}] has joined us!", new MapUnitAction(addedUnit.GetTile().Position, addedUnit.Id), NotificationCategory.War, defender.GetName());

        defender.GetCivInfo().AddNotification(
            $"An enemy [{attacker.GetName()}] has captured our [{defender.GetName()}]",
            defender.GetTile().Position, NotificationCategory.War, attacker.GetName(),
            NotificationIcon.War, defender.GetName());

        var civilianUnit = defenderTile.CivilianUnit;
        // placeUnitNearTile might not have spawned the unit in exactly this tile, in which case no capture would have happened on this tile. So we need to do that here.
        if (addedUnit.GetTile() != defenderTile && civilianUnit != null)
            CaptureCivilianUnit(attacker, new MapUnitCombatant(civilianUnit));
        return true;
    }

    /// <exception cref="ArgumentException">if the <paramref name="attacker"/> and <paramref name="defender"/> belong to the same civ.</exception>
    public static void CaptureCivilianUnit(ICombatant attacker, MapUnitCombatant defender, bool checkDefeat = true)
    {
        if (attacker.GetCivInfo() == defender.GetCivInfo())
            throw new ArgumentException("Can't capture our own unit!");

        // need to save this because if the unit is captured its owner will be overwritten
        var defenderCiv = defender.GetCivInfo();

        var capturedUnit = defender.Unit;
        // Stop current action
        capturedUnit.Action = null;
        capturedUnit.Automated = false;

        var capturedUnitTile = capturedUnit.GetTile();
        var originalOwner = capturedUnit.OriginalOwningCiv;

        bool wasDestroyedInstead = false;
        if (defender.Unit.HasUnique(UniqueType.Uncapturable))
        {
            // Uncapturable units are destroyed
            capturedUnit.Destroy();
            wasDestroyedInstead = true;
        }
        else if (attacker.GetCivInfo().IsCityState && (capturedUnit.HasUnique(UniqueType.FoundCity, GameContext.IgnoreConditionalsState)
                 || capturedUnit.HasUnique(UniqueType.FoundPuppetCity, GameContext.IgnoreConditionalsState)))
        {
            // City states can never capture settlers at all
            // Same with puppet city sttlers
            capturedUnit.Destroy();
            wasDestroyedInstead = true;
        }
        else if (attacker.GetCivInfo() == originalOwner)
        {
            // Is it our old unit? Then it is recaptured without converting settlers to workers
            capturedUnit.CapturedBy(attacker.GetCivInfo());
        }
        else if (defender.GetCivInfo().IsBarbarian
                 && originalOwner != null
                 && !originalOwner.IsBarbarian
                 && attacker.GetCivInfo() != originalOwner
                 && attacker.GetCivInfo().Knows(originalOwner)
                 && originalOwner.IsAlive()
                 && !attacker.GetCivInfo().IsAtWarWith(originalOwner)
                 && attacker.GetCivInfo().PlayerType == PlayerType.Human) // Only humans get the choice
        {
            // Return captured civilian to its original owner?
            capturedUnit.CapturedBy(attacker.GetCivInfo());
            attacker.GetCivInfo().AddPopupAlert(
                new PopupAlert(
                    AlertType.RecapturedCivilian,
                    capturedUnit.CurrentTile.Position.ToPrettyString()));
        }
        else if (CaptureOrConvertToWorker(capturedUnit, attacker.GetCivInfo()) == null)
            wasDestroyedInstead = true;

        if (!wasDestroyedInstead)
        {
            defenderCiv.AddNotification(
                $"An enemy [{attacker.GetName()}] has captured our [{defender.GetName()}]",
                defender.GetTile().Position, NotificationCategory.War, attacker.GetName(),
                NotificationIcon.War, defender.GetName());
        }
        else
        {
            defenderCiv.AddNotification(
                $"An enemy [{attacker.GetName()}] has destroyed our [{defender.GetName()}]",
                defender.GetTile().Position, NotificationCategory.War, attacker.GetName(),
                NotificationIcon.War, defender.GetName());
            Battle.TriggerDefeatUniques(defender, attacker, capturedUnitTile);
        }

        if (checkDefeat)
            Battle.DestroyIfDefeated(defenderCiv, attacker.GetCivInfo());
        capturedUnit.UpdateVisibleTiles();
    }

    /// <summary>
    /// Capture wrapper that also implements the rule that non-barbarians get a Worker as replacement for a captured Settler.
    /// </summary>
    /// <returns>position the captured unit is in afterwards - can rarely be a different tile if the unit is no longer allowed where it originated.
    /// Returns <c>null</c> if there is no Worker replacement for a Settler in the ruleset or placeUnitNearTile couldn't place it.</returns>
    /// <seealso cref="MapUnitClass.CapturedBy"/>
    public static HexCoord? CaptureOrConvertToWorker(MapUnitClass capturedUnit, CivilizationObject capturingCiv)
    {
        // Captured settlers are converted to workers unless captured by barbarians (so they can be returned later).
        if (!capturedUnit.HasUnique(UniqueType.FoundCity, GameContext.IgnoreConditionalsState) || capturingCiv.IsBarbarian)
        {
            capturedUnit.CapturedBy(capturingCiv);
            return capturedUnit.CurrentTile.Position; // if capturedBy has moved the unit, this is updated
        }

        capturedUnit.Destroy();
        // This is so that future checks which check if a unit has been captured are caught give the right answer
        //  For example, in postBattleMoveToAttackedTile
        capturedUnit.Civ = capturingCiv;
        capturedUnit.Cache.State = GameContext.ForUnit(capturedUnit);

        var workerTypeUnit = capturingCiv.GameInfo.Ruleset.Units.Values
            .FirstOrDefault(it => it.IsCivilian() && ((IHasUniques)it).GetMatchingUniques(UniqueType.BuildImprovements, GameContext.IgnoreConditionalsState)
                .Any(unique => unique.Params[0] == "Land"));
        if (workerTypeUnit == null) return null;
        return capturingCiv.Units.PlaceUnitNearTile(capturedUnit.CurrentTile.Position, workerTypeUnit, capturedUnit.Id)
            ?.CurrentTile.Position;
    }
}
