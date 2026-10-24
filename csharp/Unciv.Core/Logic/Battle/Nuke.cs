using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/Nuke.kt (T083). Purity annotations dropped.
// The Kotlin function NUKE keeps its name (the C# analyzers accept it; the Kotlin comment says it should stand out).
public static class Nuke
{
    /// <summary>
    /// Checks whether <paramref name="nuke"/> is allowed to nuke <paramref name="targetTile"/>
    /// - Not if we would need to declare war on someone we can't.
    /// - Disallow nuking the tile the nuke is in, as per Civ5 (but not nuking your own tiles/units otherwise)
    ///
    /// Both BattleTable.simulateNuke and AirUnitAutomation.automateNukes check range, so that check is omitted here.
    /// </summary>
    public static bool MayUseNuke(MapUnitCombatant nuke, TileClass targetTile)
    {
        var attackerCiv = nuke.GetCivInfo();
        var launchTile = nuke.GetTile();

        if (launchTile == targetTile) return false;
        if (!targetTile.IsExplored(attackerCiv)) return false;
        // Can only nuke in unit's range, visibility (line of sight) doesn't matter
        if (launchTile.AerialDistanceTo(targetTile) > nuke.Unit.GetRange()) return false;

        bool canNuke = true;

        void CheckDefenderCiv(CivilizationObject? defenderCiv)
        {
            if (defenderCiv == null) return;
            // Allow nuking yourself! (Civ5 source: CvUnit::isNukeVictim)
            if (defenderCiv == attackerCiv || defenderCiv.IsDefeated()) return;
            if (defenderCiv.IsBarbarian) return;
            // Gleaned from Civ5 source - this disallows nuking unknown civs even in invisible tiles
            // https://github.com/Gedemon/Civ5-DLL/blob/master/CvGameCoreDLL_Expansion1/CvUnit.cpp#L5056
            // https://github.com/Gedemon/Civ5-DLL/blob/master/CvGameCoreDLL_Expansion1/CvTeam.cpp#L986
            if (attackerCiv.GetDiplomacyManager(defenderCiv)?.CanAttack() == true) return;
            canNuke = false;
        }

        int blastRadius = nuke.Unit.GetNukeBlastRadius();
        targetTile.ForEachTileInDistance(blastRadius, tile =>
        {
            CheckDefenderCiv((CivilizationObject?)tile.GetOwner());
            CheckDefenderCiv(Battle.GetMapCombatantOfTile(tile)?.GetCivInfo());
        });
        return canNuke;
    }

    public static void NUKE(MapUnitCombatant attacker, TileClass targetTile)
    {
        var attackingCiv = attacker.GetCivInfo();
        var nukeUnique = attacker.Unit.GetMatchingUniques(UniqueType.NuclearWeapon).FirstOrDefault();
        if (nukeUnique == null) return;
        int nukeStrength = nukeUnique.Params[0].ToInt();

        var blastRadiusUnique = attacker.Unit.GetMatchingUniques(UniqueType.BlastRadius).FirstOrDefault();
        int blastRadius = blastRadiusUnique != null ? blastRadiusUnique.Params[0].ToInt() : 2;

        var hitTiles = targetTile.GetTilesInDistance(blastRadius);

        var (hitCivsTerritory, notifyDeclaredWarCivs) =
            DeclareWarOnHitCivs(attackingCiv, hitTiles, attacker, targetTile);

        AddNukeNotifications(targetTile, attacker, notifyDeclaredWarCivs, attackingCiv, hitCivsTerritory);

        if (attacker.IsDefeated()) return;

        attacker.Unit.AttacksSinceTurnStart.Add(targetTile.Position);

        foreach (var tile in hitTiles)
        {
            // Handle complicated effects
            DoNukeExplosionForTile(attacker, tile, nukeStrength, targetTile == tile);
        }

        // Instead of postBattleAction() just destroy the unit, all other functions are not relevant
        if (attacker.Unit.HasUnique(UniqueType.SelfDestructs)) attacker.Unit.Destroy();

        // It's unclear whether using nukes results in a penalty with all civs, or only affected civs.
        // For now I'll make it give a diplomatic penalty to all known civs, but some testing for this would be appreciated
        foreach (var civ in attackingCiv.GetKnownCivs())
        {
            civ.GetDiplomacyManager(attackingCiv).NotNull().SetModifier(DiplomaticModifiers.UsedNuclearWeapons, -50f);
        }

        if (!attacker.IsDefeated())
        {
            attacker.Unit.AttacksThisTurn += 1;
        }
    }

    private static void AddNukeNotifications(
        TileClass targetTile,
        MapUnitCombatant attacker,
        List<CivilizationObject> notifyDeclaredWarCivs,
        CivilizationObject attackingCiv,
        List<CivilizationObject> hitCivsTerritory)
    {
        // A sequence in Kotlin, but it is only ever enumerated by the Notification, so a list is the same
        var nukeNotificationAction = new List<NotificationAction>
        {
            new LocationAction(targetTile.Position),
            new CivilopediaAction("Units/" + attacker.GetName()),
        };

        // If the nuke has been intercepted and destroyed then it fails to detonate
        if (attacker.IsDefeated())
        {
            // Notify attacker that they are now at war for the attempt
            foreach (var defendingCiv in notifyDeclaredWarCivs)
                attackingCiv.AddNotification(
                    $"After an attempted attack by our [{attacker.GetName()}], [{defendingCiv}] has declared war on us!",
                    nukeNotificationAction,
                    NotificationCategory.Diplomacy,
                    defendingCiv.CivName,
                    NotificationIcon.War,
                    attacker.GetName());
            return;
        }

        // Notify attacker that they are now at war
        foreach (var defendingCiv in notifyDeclaredWarCivs)
            attackingCiv.AddNotification(
                $"After being hit by our [{attacker.GetName()}], [{defendingCiv}] has declared war on us!",
                nukeNotificationAction, NotificationCategory.Diplomacy, defendingCiv.CivName, NotificationIcon.War, attacker.GetName());

        // Message all other civs
        foreach (var otherCiv in attackingCiv.GameInfo.Civilizations)
        {
            if (!otherCiv.IsAlive() || otherCiv == attackingCiv) continue;
            if (hitCivsTerritory.Contains(otherCiv))
                otherCiv.AddNotification(
                    $"A(n) [{attacker.GetName()}] from [{attackingCiv.CivName}] has exploded in our territory!",
                    nukeNotificationAction, NotificationCategory.War, attackingCiv.CivName, NotificationIcon.War, attacker.GetName());
            else if (otherCiv.Knows(attackingCiv))
                otherCiv.AddNotification(
                    $"A(n) [{attacker.GetName()}] has been detonated by [{attackingCiv.CivName}]!",
                    nukeNotificationAction, NotificationCategory.War, attackingCiv.CivName, NotificationIcon.War, attacker.GetName());
            else
                otherCiv.AddNotification(
                    $"A(n) [{attacker.GetName()}] has been detonated by [an unknown civilization]!",
                    nukeNotificationAction, NotificationCategory.War, NotificationIcon.War, attacker.GetName());
        }
    }

    private static (List<CivilizationObject> HitCivsTerritory, List<CivilizationObject> NotifyDeclaredWarCivs) DeclareWarOnHitCivs(
        CivilizationObject attackingCiv,
        IEnumerable<TileClass> hitTiles,
        MapUnitCombatant attacker,
        TileClass targetTile)
    {
        // Declare war on the owners of all hit tiles
        var notifyDeclaredWarCivs = new List<CivilizationObject>();

        void TryDeclareWar(CivilizationObject civSuffered)
        {
            if (civSuffered != attackingCiv
                && civSuffered.Knows(attackingCiv)
                && civSuffered.GetDiplomacyManager(attackingCiv).NotNull().DiplomaticStatus != DiplomaticStatus.War)
            {
                attackingCiv.GetDiplomacyManager(civSuffered).NotNull().DeclareWar();
                if (!notifyDeclaredWarCivs.Contains(civSuffered)) notifyDeclaredWarCivs.Add(civSuffered);
            }
        }

        var hitCivsTerritory = new List<CivilizationObject>();
        foreach (var hitCiv in hitTiles.Select(it => it.GetOwner()).OfType<CivilizationObject>().Distinct())
        {
            hitCivsTerritory.Add(hitCiv);
            TryDeclareWar(hitCiv);
        }

        // Declare war on all potentially hit units. They'll try to intercept the nuke before it drops
        // (lazy like the Kotlin sequence: interceptions in the loop body can change the units of tiles not yet visited)
        foreach (var civWhoseUnitWasAttacked in hitTiles
                     .SelectMany(it => it.GetUnits())
                     .Select(it => it.Civ).Distinct()
                     .Where(it => it != attackingCiv))
        {
            TryDeclareWar(civWhoseUnitWasAttacked);
            if (attacker.Unit.BaseUnit.IsAirUnit() && !attacker.IsDefeated())
            {
                AirInterception.TryInterceptAirAttack(
                    attacker,
                    targetTile,
                    civWhoseUnitWasAttacked,
                    null);
            }
        }
        return (hitCivsTerritory, notifyDeclaredWarCivs);
    }

    private static void DoNukeExplosionForTile(
        MapUnitCombatant attacker,
        TileClass tile,
        int nukeStrength,
        bool isGroundZero)
    {
        // https://forums.civfanatics.com/resources/unit-guide-modern-future-units-g-k.25628/
        // https://www.carlsguides.com/strategy/civilization5/units/aircraft-nukes.ph
        // Testing done by Ravignir
        // original source code: GenerateNuclearExplosionDamage(), ApplyNuclearExplosionDamage()
        var tileContext = attacker.Unit.Cache.State.Copy(tile: new GameContext.CopyArg<IGameContextTile>(tile));

        float damageModifierFromMissingResource = 1f;
        var civResources = attacker.GetCivInfo().GetCivResourcesByName();
        foreach (var resource in attacker.Unit.GetResourceRequirementsPerTurn().Keys)
        {
            if (civResources[resource] < 0 && !attacker.GetCivInfo().IsBarbarian)
                damageModifierFromMissingResource *= 0.5f; // I could not find a source for this number, but this felt about right
            // - Original Civ5 does *not* reduce damage from missing resource, from source inspection
        }

        float buildingModifier = 1f; // Strange, but in Civ5 a bunker mitigates damage to garrison, even if the city is destroyed by the nuke

        // Damage city and reduce its population
        var city = (CityObject?)tile.GetCity();
        if (city != null && tile.Position == city.Location)
        {
            var attackContext = new GameContext(attacker.GetCivInfo(), ourCombatant: attacker, theirCombatant: new CityCombatant(city), tile: tile);
            buildingModifier = GetAggregateModifier(city, UniqueType.GarrisonDamageFromNukes);
            DoNukeExplosionDamageToCity(city, nukeStrength, damageModifierFromMissingResource, attackContext);
            Battle.PostBattleNotifications(attacker, new CityCombatant(city), city.GetCenterTile());
            Battle.DestroyIfDefeated(city.Civ, attacker.GetCivInfo(), city.Location);
        }

        // Damage and/or destroy units on the tile
        foreach (var unit in tile.GetUnits().ToList()) // toList so if it's destroyed there's no concurrent modification
        {
            var defender = new MapUnitCombatant(unit);
            var attackContext = new GameContext(attacker.GetCivInfo(), ourCombatant: attacker, theirCombatant: defender, tile: tile);
            var rng = attackContext.StateBasedRandom("Nuke.doNukeExplosionForTile");
            int baseDamage;
            if (isGroundZero || nukeStrength >= 2) baseDamage = 100;
            // The following constants are NUKE_UNIT_DAMAGE_BASE / NUKE_UNIT_DAMAGE_RAND_1 / NUKE_UNIT_DAMAGE_RAND_2 in Civ5
            else if (nukeStrength == 1) baseDamage = 30 + rng.NextInt(40) + rng.NextInt(40);
            // Level 0 does not exist in Civ5 (it treats units same as level 2)
            else baseDamage = 20 + rng.NextInt(30);
            int damage = KotlinMath.FloatToInt((baseDamage * buildingModifier * damageModifierFromMissingResource) + (MathF.BitIncrement(1f) - 1f));
            if (unit.IsCivilian())
            {
                if (unit.Health - damage <= 40) unit.Destroy(); // Civ5: NUKE_NON_COMBAT_DEATH_THRESHOLD = 60
            }
            else
            {
                defender.TakeDamage(damage);
            }
            Battle.PostBattleNotifications(attacker, defender, defender.GetTile());
            Battle.DestroyIfDefeated(defender.GetCivInfo(), attacker.GetCivInfo());
        }

        // Pillage improvements, pillage roads, add fallout
        if (tile.IsCityCenter()) return; // Never touch city centers - if they survived

        var tileRng = tileContext.StateBasedRandom("Nuke.doExplosionForTile");
        if (tile.TerrainHasUnique(UniqueType.DestroyableByNukesChance))
        {
            // Note: Safe from concurrent modification exceptions only because removeTerrainFeature
            // *replaces* terrainFeatureObjects and the loop will continue on the old one
            foreach (var terrainFeature in tile.TerrainFeatureObjects)
            {
                ((IHasUniques)terrainFeature).ForEachMatchingUnique(UniqueType.DestroyableByNukesChance, GameContext.EmptyState, unique =>
                {
                    float chance = unique.Params[0].ToFloat() / 100f;
                    if ((chance > 0f && isGroundZero) || tileRng.NextFloat() < chance)
                    {
                        tile.RemoveTerrainFeature(terrainFeature.Name);
                        ApplyPillageAndFallout(tile);
                    }
                });
            }
        }
        else if (isGroundZero || tileRng.NextFloat() < 0.5f) // Civ5: NUKE_FALLOUT_PROB
        {
            ApplyPillageAndFallout(tile);
        }
    }

    private static void ApplyPillageAndFallout(TileClass tile)
    {
        var improvement = tile.GetUnpillagedTileImprovement();
        if (improvement != null && !((IHasUniques)improvement).HasUnique(UniqueType.Irremovable))
        {
            if (((IHasUniques)improvement).HasUnique(UniqueType.Unpillagable)) tile.RemoveImprovement();
            else tile.SetPillaged();
        }
        if (tile.GetUnpillagedRoad() != RoadStatus.None)
            tile.SetPillaged();
        if (tile.IsWater || tile.IsImpassible() || tile.TerrainFeatures.Contains("Fallout")) return;
        tile.AddTerrainFeature("Fallout");
    }

    /// <summary>Damages the city and reduces its population; the "protection" modifier from buildings (Bomb Shelter, UniqueType.PopulationLossFromNukes) applies.</summary>
    private static void DoNukeExplosionDamageToCity(CityObject targetedCity, int nukeStrength, float damageModifierFromMissingResource, GameContext context)
    {
        // Original Capitals must be protected, `canBeDestroyed` is responsible for that check.
        // The `justCaptured = true` parameter is what allows other Capitals to suffer normally.
        if ((nukeStrength > 2 || (nukeStrength > 1 && targetedCity.Population.Population < 5))
            && targetedCity.CanBeDestroyed(true))
        {
            targetedCity.DestroyCity();
            return;
        }

        var cityCombatant = new CityCombatant(targetedCity);
        var rng = context.StateBasedRandom("Nuke.doNukeExplosionDamageToCity");
        cityCombatant.TakeDamage(KotlinMath.FloatToInt(cityCombatant.GetHealth() * 0.5f * damageModifierFromMissingResource));

        // Difference to original: Civ5 rounds population loss down twice - before and after bomb shelters
        float lossFraction;
        switch (nukeStrength)
        {
            case 0: lossFraction = 0f; break;
            case 1: lossFraction = (30 + rng.NextInt(20) + rng.NextInt(20)) / 100f; break;
            case 2: lossFraction = (60 + rng.NextInt(10) + rng.NextInt(10)) / 100f; break;
            default: lossFraction = 1f; break; // hypothetical nukeStrength 3 -> always to 1 pop
        }
        int populationLoss = KotlinMath.FloatToInt(
            targetedCity.Population.Population * GetAggregateModifier(targetedCity, UniqueType.PopulationLossFromNukes) * lossFraction);
        targetedCity.Population.AddPopulation(-populationLoss);
    }

    private static float GetAggregateModifier(CityObject city, UniqueType uniqueType)
    {
        float modifier = 1f;
        city.ForEachMatchingUnique(uniqueType, GameContext.EmptyState, unique =>
        {
            if (city.MatchesFilter(unique.Params[1]))
                modifier *= unique.Params[0].ToPercent();
        });
        return modifier;
    }
}
