using Unciv.Logic.City;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.Civilization.Diplomacy;

// core/src/com/unciv/logic/civilization/diplomacy/CityStateFunctions.kt (T068). Class containing city-state-specific functions.
//
// Deviations / deferred (each names its ticket):
// - NextTurnAutomation.getForeignCityNearCapital (giveGreatPersonToPatron, giveMilitaryUnitToPatron): CityStateSeams.GetForeignCityNearCapital (T094).
// - CityCombatant(capital).getDefendingStrength() (getTributeModifiers): CityStateSeams.GetCityDefendingStrength (CityCombatant, T081).
// - The QuestManager calls (influenceGainedByGift, receiveGoldGift, getTributeModifiers' callers, cityStateBullied, cityStateAttacked,
//   makeOtherCityStatesWaryOfAttacker, cityStateDestroyed) go through the QuestManager members (T069).
// - Kotlin `Random(seed)` is GameRandom, `Sequence`s that are only iterated are IEnumerable.

public class CityStateFunctions(Civilization civInfo) : ICityStateFunctions
{
    private readonly Civilization civInfo = civInfo;

    /// <summary>Attempts to initialize the city state, returning true if successful.</summary>
    public bool InitCityState(Ruleset ruleset, string startingEra, IEnumerable<Nation> usedMajorCivs, GameRandom rng)
    {
        var allMercantileResources = ruleset.TileResources.Values.Where(it => ((IHasUniques)it).HasUnique(UniqueType.CityStateOnlyResource)).Select(it => it.Name).ToList();
        var uniqueTypes = new HashSet<UniqueType>(); // We look through these to determine what kinds of city states we have

        var cityStateType = ruleset.CityStateTypes[civInfo.Nation.CityStateType.NotNull()];
        uniqueTypes.UnionWith(cityStateType.FriendBonusUniqueMap.GetAllUniques().Select(it => it.Type).OfType<UniqueType>());
        uniqueTypes.UnionWith(cityStateType.AllyBonusUniqueMap.GetAllUniques().Select(it => it.Type).OfType<UniqueType>());

        // CS Personality
        civInfo.CityStatePersonality = CityStatePersonalityExtensions.SafeValueOf(civInfo.Nation.Personality)
            ?? Enum.GetValues<CityStatePersonality>().ToList().Random(rng);

        // Mercantile bonus resources

        if (uniqueTypes.Contains(UniqueType.CityStateUniqueLuxury))
            civInfo.CityStateResource = allMercantileResources.RandomOrNull(rng);

        List<BaseUnit> PossibleUnits()
        {
            var units = new List<BaseUnit>();
            foreach (var unit in ruleset.Units.Values)
            {
                if (((IHasUniques)unit).AvailableInEra(ruleset, startingEra)) continue; // Not from the start era or before
                var uniqueNation = unit.UniqueTo == null ? null : ruleset.Nations.GetValueOrDefault(unit.UniqueTo);
                if (uniqueNation == null || !uniqueNation.IsMajorCiv) continue; // Must be from a major civ
                if (usedMajorCivs.Contains(uniqueNation)) continue; // But not from a major civ in the game
                if (!ruleset.UnitTypes[unit.UnitType].IsLandUnit()) continue; // Must be a land unit
                if (unit.Strength <= 0 && unit.RangedStrength <= 0) continue; // Must be a military unit
                units.Add(unit);
            }
            return units;
        }

        // Unique unit for militaristic city-states
        if (uniqueTypes.Contains(UniqueType.CityStateMilitaryUnits))
        {
            var possibleUnits = PossibleUnits();
            if (possibleUnits.Count > 0)
                civInfo.CityStateUniqueUnit = possibleUnits.Random(rng).Name;
        }

        // Kotlin to-do (no ticket): return false if attempting to put a religious city-state in a game without religion

        return true;
    }

    public void HoldElections()
    {
        civInfo.AddFlag(CivFlags.TurnsTillCityStateElection.ToString(), civInfo.GameInfo.Ruleset.ModOptions.Constants.CityStateElectionTurns);
        var capital = civInfo.GetCapital();
        if (capital == null) return;

        var spies = capital.Espionage.GetAllStationedSpies().Where(it => it.Action == SpyAction.RiggingElections).ToList();
        if (spies.Count == 0) return;

        float GetVotesFromSpy(Spy? spy)
        {
            if (spy == null) return 20f;
            float votes = civInfo.GetDiplomacyManagerOrMeet(spy.CivInfo).Influence / 2;
            votes += (float)(spy.GetSkillModifierPercent() * spy.GetEfficiencyModifier()); // ranges from 30 to 90
            // BNW policies (Covert Action): +x% success chance for rigging elections, read as +x% of this spy's votes
            int riggingPercent = spy.CivInfo.GetMatchingUniques(UniqueType.ElectionRiggingModifier).Sum(it => KotlinMath.ParseInt(it.Params[0]));
            return riggingPercent == 0 ? votes : votes * (100 + riggingPercent) / 100f;
        }

        var parties = new List<Spy?>(spies);
        parties.Add(null); // Null spy is a neuteral party in the election
        float randomSeed = (float)capital.Location.X * capital.Location.Y + 123f * civInfo.GameInfo.Turns;
        Civilization? winner = parties.RandomWeighted(civInfo.GameInfo.SeededRandom(KotlinMath.FloatToInt(randomSeed)), GetVotesFromSpy)?.CivInfo;

        // There may be no winner, in that case all spies will lose 5 influence
        if (winner == null)
        {
            // No spy won the election, the civs that tried to rig the election loose influence
            foreach (var spy in spies)
            {
                civInfo.GetDiplomacyManager(spy.CivInfo).NotNull().AddInfluence(-5f);
                spy.CivInfo.AddNotification($"Your spy lost the election in [{capital.Name}]!", capital.Location, NotificationCategory.Espionage, NotificationIcon.Spy);
            }
            return;
        }

        var allyCiv = civInfo.AllyCiv;

        // Winning civ gets influence and all others loose influence
        foreach (var civ in civInfo.GetKnownCivs().ToList())
        {
            float influence = civ == winner ? 20f : -5f;
            civInfo.GetDiplomacyManager(civ).NotNull().AddInfluence(influence);
            if (civ == winner)
            {
                civ.AddNotification($"Your spy successfully rigged the election in [{civInfo.CivName}]!", capital.Location, NotificationCategory.Espionage, NotificationIcon.Spy);
            }
            else if (spies.Any(it => it.CivInfo == civ))
            {
                civ.AddNotification($"Your spy lost the election in [{civInfo.CivName}] to [{winner.CivName}]!", capital.Location, NotificationCategory.Espionage, NotificationIcon.Spy);
            }
            else if (civ == allyCiv)
            {
                // If the previous ally has no spy in the city then we should notify them
                allyCiv.AddNotification($"The election in [{civInfo.CivName}] was rigged by [{winner.CivName}]!", capital.Location, NotificationCategory.Espionage, NotificationIcon.Spy);
            }
        }
    }

    public int TurnsForGreatPersonFromCityState() =>
        KotlinMath.FloatToInt((37 + civInfo.State.StateBasedRandom("CityStateFunctions.turnsForGreatPersonFromCityState").NextInt(7)) * civInfo.GameInfo.Speed.Modifier);

    /// <summary>Gain a random great person from the city state</summary>
    public void GiveGreatPersonToPatron(Civilization receivingCiv)
    {
        // Great Prophets can't be gotten from CS
        var giftableUnits = civInfo.GameInfo.Ruleset.Units.Values.Where(it => it.IsGreatPerson
            && !((IHasUniques)it).HasUnique(UniqueType.MayFoundReligion)).ToList();
        if (giftableUnits.Count == 0) // For badly defined mods that don't have great people but do have the policy that makes city states grant them
            return;
        var rng = civInfo.GetDiplomacyManager(receivingCiv).NotNull().State.StateBasedRandom("CityStateFunctions.giveGreatPersonToPatron");
        var giftedUnit = giftableUnits.Random(rng);
        var city = CityStateSeams.GetForeignCityNearCapital(civInfo.GetCapital(), receivingCiv);
        if (city == null) return;
        var placedUnit = receivingCiv.Units.PlaceUnitNearTile(city.Location, giftedUnit);
        if (placedUnit == null) return;
        var locations = LocationAction.Of(placedUnit.GetTile().Position, civInfo.GetCapital().NotNull().Location);
        receivingCiv.AddNotification($"[{civInfo.CivName}] gave us a [{giftedUnit.Name}] as a gift!", locations,
            NotificationCategory.Units, civInfo.CivName, giftedUnit.Name);
    }

    public void GiveMilitaryUnitToPatron(Civilization receivingCiv)
    {
        var city = CityStateSeams.GetForeignCityNearCapital(civInfo.GetCapital(), receivingCiv);
        if (city == null) return;
        var rng = civInfo.GetDiplomacyManager(receivingCiv).NotNull().State.StateBasedRandom("CityStateFunctions.giveGreatPersonToPatron");

        BaseUnit? GiftableUniqueUnit()
        {
            var uniqueUnit = civInfo.CityStateUniqueUnit == null ? null : civInfo.GameInfo.Ruleset.Units.GetValueOrDefault(civInfo.CityStateUniqueUnit);
            if (uniqueUnit == null)
                return null;
            if (!receivingCiv.Tech.IsResearched(uniqueUnit))
                return null;
            if (receivingCiv.Tech.IsObsolete(uniqueUnit))
                return null;
            return uniqueUnit;
        }

        BaseUnit? RandomGiftableUnit() =>
            city.CityConstructions.GetConstructableUnits()
                .Where(it => !it.IsCivilian() && it.IsLandUnit && it.UniqueTo == null)
                // Does not make us go over any resource quota
                .Where(unit => !unit.GetResourceRequirementsPerTurn(receivingCiv.State).Any(it =>
                    it.Value > 0 && receivingCiv.GetResourceAmount(it.Key) < it.Value))
                .ToList().RandomOrNull(rng);

        var militaryUnit = GiftableUniqueUnit() // If the receiving civ has discovered the required tech and not the obsolete tech for our unique, always give them the unique
            ?? RandomGiftableUnit(); // Otherwise pick at random
        if (militaryUnit == null) return; // That filter _can_ result in no candidates, if so, quit silently

        // placing the unit may fail - in that case stay quiet
        var placedUnit = receivingCiv.Units.PlaceUnitNearTile(city.Location, militaryUnit.Name);
        if (placedUnit == null) return;

        // The unit should have bonuses from Barracks, Alhambra etc as if it was built in the CS capital
        militaryUnit.AddConstructionBonuses(placedUnit, civInfo.GetCapital().NotNull().CityConstructions);

        // Siam gets +10 XP for all CS units
        receivingCiv.ForEachMatchingUnique(UniqueType.CityStateGiftedUnitsStartWithXp, unique =>
        {
            placedUnit.Promotions.XP = unchecked(placedUnit.Promotions.XP + unique.Params[0].ToInt());
        });

        // Point to the gifted unit, then to the other places mentioned in the message
        var notificationActions = new NotificationAction[] { new MapUnitAction(placedUnit.CurrentTile.Position, placedUnit.Id), new LocationAction(city.Location) };
        receivingCiv.AddNotification(
            $"[{civInfo.CivName}] gave us a [{militaryUnit.Name}] as gift near [{city.Name}]!",
            notificationActions,
            NotificationCategory.Units,
            civInfo.CivName,
            militaryUnit.Name);
    }

    /// <summary>
    /// B034 (bnw-spec 4.11), not in Kotlin: Charitable Missions, <c>[+30]% Influence gained with City-States</c>, scales every positive influence
    /// <paramref name="gainer"/> earns with a city-state by a gift (gold or unit) or a quest reward. Losses and the per-turn resting-point drift are not scaled.
    /// </summary>
    public static float ScaleInfluenceGain(Civilization gainer, float amount)
    {
        if (amount <= 0f) return amount;
        gainer.ForEachMatchingUnique(UniqueType.CityStateInfluenceGain, unique => amount *= 1f + unique.Params[0].ToFloat() / 100f);
        return amount;
    }

    public int InfluenceGainedByGift(Civilization donorCiv, int giftAmount)
    {
        // https://github.com/Gedemon/Civ5-DLL/blob/aa29e80751f541ae04858b6d2a2c7dcca454201e/CvGameCoreDLL_Expansion1/CvMinorCivAI.cpp
        // line 8681 and below
        float influenceGained = KotlinMath.Pow((float)giftAmount, 1.01f) / 9.8f;
        var speed = civInfo.GameInfo.Speed;
        float gameProgressApproximate = Math.Min(civInfo.GameInfo.Turns / (400f * speed.Modifier), 1f);
        influenceGained *= 1 - (2 / 3f) * gameProgressApproximate;
        influenceGained *= speed.GoldGiftModifier;
        donorCiv.ForEachMatchingUnique(UniqueType.CityStateGoldGiftsProvideMoreInfluence, unique =>
        {
            influenceGained *= 1f + unique.Params[0].ToFloat() / 100f;
        });

        // Bonus due to "Invest" quests
        influenceGained *= civInfo.QuestManager.GetInvestmentMultiplier(donorCiv);

        influenceGained = ScaleInfluenceGain(donorCiv, influenceGained);

        influenceGained -= influenceGained % 5;
        if (influenceGained < 5f) influenceGained = 5f;
        return KotlinMath.FloatToInt(influenceGained);
    }

    public void ReceiveGoldGift(Civilization donorCiv, int giftAmount)
    {
        if (!civInfo.IsCityState) throw new InvalidOperationException("You can only gain influence with City-States!");
        donorCiv.AddGold(-giftAmount);
        civInfo.AddGold(giftAmount);
        civInfo.GetDiplomacyManager(donorCiv).NotNull().AddInfluence(InfluenceGainedByGift(donorCiv, giftAmount));
        civInfo.QuestManager.ReceivedGoldGift(donorCiv);
    }

    public List<Civilization> GetProtectorCivs()
    {
        if (civInfo.IsMajorCiv()) return [];
        return civInfo.Diplomacy.Values
            .Where(it => !it.OtherCiv.IsDefeated() && it.DiplomaticStatus == DiplomaticStatus.Protector)
            .Select(it => it.OtherCiv).ToList();
    }

    public void AddProtectorCiv(Civilization otherCiv)
    {
        if (!OtherCivCanPledgeProtection(otherCiv))
            return;

        var diplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        diplomacy.DiplomaticStatus = DiplomaticStatus.Protector;
        diplomacy.SetFlag(DiplomacyFlags.RecentlyPledgedProtection, 10); // Can't break for 10 turns
    }

    public void RemoveProtectorCiv(Civilization otherCiv, bool forced = false)
    {
        if (!forced && !OtherCivCanWithdrawProtection(otherCiv))
            return;

        var diplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        diplomacy.DiplomaticStatus = DiplomaticStatus.Peace;
        diplomacy.SetFlag(DiplomacyFlags.RecentlyWithdrewProtection, 20); // Can't re-pledge for 20 turns
        diplomacy.AddInfluence(-20f);
    }

    public bool OtherCivCanPledgeProtection(Civilization otherCiv)
    {
        // Must be a known city state
        if (!civInfo.IsCityState || !otherCiv.IsMajorCiv() || otherCiv.IsDefeated() || !civInfo.Knows(otherCiv))
            return false;
        var diplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        // Can't pledge too soon after withdrawing
        if (diplomacy.HasFlag(DiplomacyFlags.RecentlyWithdrewProtection))
            return false;
        // Must have at least 0 influence
        if (diplomacy.GetInfluence() < 0)
            return false;
        // can't be at war
        if (civInfo.IsAtWarWith(otherCiv))
            return false;
        // Must not be protected already
        if (diplomacy.DiplomaticStatus == DiplomaticStatus.Protector)
            return false;
        return true;
    }

    public bool OtherCivCanWithdrawProtection(Civilization otherCiv)
    {
        // Must be a known city state
        if (!civInfo.IsCityState || !otherCiv.IsMajorCiv() || otherCiv.IsDefeated() || !civInfo.Knows(otherCiv))
            return false;
        var diplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        // Can't withdraw too soon after pledging
        if (diplomacy.HasFlag(DiplomacyFlags.RecentlyPledgedProtection))
            return false;
        // Must be protected
        if (diplomacy.DiplomaticStatus != DiplomaticStatus.Protector)
            return false;
        return true;
    }

    public void UpdateAllyCivForCityState()
    {
        if (!civInfo.IsCityState) return;
        var oldAlly = civInfo.AllyCiv;
        Civilization? newAlly = null;

        // maxByOrNull: the first of equal maxima
        DiplomacyManager? maxInfluence = null;
        foreach (var entry in civInfo.Diplomacy.Values.Where(it => it.OtherCiv.IsMajorCiv() && !it.OtherCiv.IsDefeated()))
        {
            if (maxInfluence == null || entry.GetInfluence() > maxInfluence.GetInfluence())
                maxInfluence = entry;
        }
        if (maxInfluence != null && maxInfluence.GetInfluence() >= 60)
            newAlly = maxInfluence.OtherCiv;

        if (oldAlly == newAlly) return;
        civInfo.AllyCiv = newAlly;

        if (newAlly != null)
        {
            var text = $"We have allied with [{civInfo.CivName}].";
            newAlly.AddNotification(text,
                GetNotificationActions(),
                NotificationCategory.Diplomacy, civInfo.CivName,
                NotificationIcon.Diplomacy);
            newAlly.Cache.UpdateViewableTiles();
            newAlly.Cache.UpdateCivResources();
            newAlly.ForEachMatchingUnique(UniqueType.CityStateCanBeBoughtForGold, unique =>
            {
                newAlly.GetDiplomacyManager(civInfo).NotNull().SetFlag(DiplomacyFlags.MarriageCooldown, unique.Params[0].ToInt());
            });

            // Join the wars of our new ally - loop through all civs they are at war with
            foreach (var newEnemy in civInfo.GameInfo.Civilizations.Where(it => it.IsAtWarWith(newAlly) && it.IsAlive()).ToList())
            {
                if (civInfo.IsAtWarWith(newEnemy)) continue;
                if (!civInfo.Knows(newEnemy))
                    // We have to meet first (meet interesting people - and kill them!)
                    civInfo.DiplomacyFunctions.MakeCivilizationsMeet(newEnemy, warOnContact: true);
                civInfo.GetDiplomacyManager(newEnemy).NotNull().DeclareWar(new DeclareWarReason(WarType.CityStateAllianceWar, newAlly));
            }
        }

        if (oldAlly != null && civInfo.IsAlive())
        {
            var text =
                newAlly != null ? $"We have lost our alliance with [{civInfo.CivName}] to [{newAlly.CivName}]."
                : $"We have lost our alliance with [{civInfo.CivName}].";
            oldAlly.AddNotification(text,
                GetNotificationActions(),
                NotificationCategory.Diplomacy, civInfo.CivName,
                NotificationIcon.Diplomacy);
            if (newAlly != null && oldAlly.Knows(newAlly))
            {
                var diplomacyManager = oldAlly.GetDiplomacyManager(newAlly).NotNull();
                diplomacyManager.AddModifier(DiplomaticModifiers.StoleOurAlly, -10f);
            }
            oldAlly.Cache.UpdateViewableTiles();
            oldAlly.Cache.UpdateCivResources();
        }
    }

    /// <returns>a Sequence of NotificationActions for use in addNotification, showing Capital on map if any, then opening diplomacy</returns>
    public IEnumerable<NotificationAction> GetNotificationActions()
    {
        // Notification click will first point to CS location, if any, then open diplomacy.
        // That's fine for the influence notifications and for afraid too.
        //
        // If the city-state is captured by a civ, it stops being the ally of the civ it was previously an ally of.
        //  This means that it will NOT HAVE a capital at that time, so if we run getCapital()!! we'll get a crash!
        // Or, City States can get stuck with only their Settler and no cities until late into a game if city placements are rare
        // We also had `cities.asSequence() // in practice 0 or 1 entries, that's OK` before (a CS *can* have >1 cities but it will always raze conquests).
        var capital = civInfo.GetCapital();
        if (capital != null)
            yield return new LocationAction(capital.Location);
        yield return new DiplomacyAction(civInfo.CivId);
    }

    public int GetDiplomaticMarriageCost()
    {
        // https://github.com/Gedemon/Civ5-DLL/blob/master/CvGameCoreDLL_Expansion1/CvMinorCivAI.cpp, line 7812
        int cost = KotlinMath.FloatToInt(500 * civInfo.GameInfo.Speed.GoldCostModifier);
        // Plus disband value of all units
        foreach (var unit in civInfo.Units.GetCivUnits())
            cost = unchecked(cost + unit.BaseUnit.GetDisbandGold(civInfo));
        // Round to lower multiple of 5
        cost /= 5;
        cost = unchecked(cost * 5);

        return cost;
    }

    public bool CanBeMarriedBy(Civilization otherCiv) =>
        !civInfo.IsDefeated()
            && civInfo.IsCityState
            && civInfo.Cities.Any()
            && civInfo.GetDiplomacyManager(otherCiv).NotNull().IsRelationshipLevelEQ(RelationshipLevel.Ally)
            && !otherCiv.GetDiplomacyManager(civInfo).NotNull().HasFlag(DiplomacyFlags.MarriageCooldown)
            && otherCiv.GetMatchingUniques(UniqueType.CityStateCanBeBoughtForGold).Any()
            && otherCiv.Gold >= GetDiplomaticMarriageCost();

    public void DiplomaticMarriage(Civilization otherCiv)
    {
        if (!CanBeMarriedBy(otherCiv)) // Just in case
            return;

        otherCiv.AddGold(-GetDiplomaticMarriageCost());
        TakeOver(otherCiv, "married into the ruling family of");
    }

    internal void TakeOver(Civilization otherCiv, string how)
    {
        var notificationLocation = civInfo.GetCapital().NotNull().Location;
        otherCiv.AddNotification($"We have {how} [{civInfo.CivName}], bringing them under our control.",
            notificationLocation,
            NotificationCategory.Diplomacy, civInfo.CivName,
            NotificationIcon.Diplomacy, otherCiv.CivName);

        foreach (var civ in civInfo.GameInfo.Civilizations.Where(it => it != otherCiv))
            civ.AddNotification($"[{otherCiv.CivName}] has {how} [{civInfo.CivName}], bringing them under their control.",
                notificationLocation,
                NotificationCategory.Diplomacy, civInfo.CivName,
                NotificationIcon.Diplomacy, otherCiv.CivName);

        foreach (var unit in civInfo.Units.GetCivUnits().ToList())
            unit.Gift(otherCiv);

        // Make sure this CS can never be liberated
        foreach (var it in civInfo.GameInfo.GetCities().Where(it => it.FoundingCivObject == civInfo).ToList())
        {
            it.FoundingCivObject = null;
            it.IsOriginalCapital = false;
        }

        foreach (var city in civInfo.Cities.ToList())
        {
            city.Espionage.RemoveAllPresentSpies(SpyFleeReason.CityTakenOverByMarriage);
            city.MoveToCiv(otherCiv);
            city.IsPuppet = true; // Human players get a popup that allows them to annex instead
        }
        civInfo.Destroy(notificationLocation);
    }

    public int GetTributeWillingness(Civilization demandingCiv, bool demandingWorker = false) =>
        GetTributeModifiers(demandingCiv, demandingWorker).Values.Sum();

    public LinkedHashMap<string, int> GetTributeModifiers(Civilization demandingCiv, bool demandingWorker = false, bool requireWholeList = false)
    {
        var modifiers = new LinkedHashMap<string, int>(); // Linked to preserve order when presenting the modifiers table
        // Can't bully major civs or unsettled CS's
        if (!civInfo.IsCityState)
        {
            modifiers["Major Civ"] = -999;
            return modifiers;
        }
        if (civInfo.Cities.Count == 0 || civInfo.GetCapital() == null)
        {
            modifiers["No Cities"] = -999;
            return modifiers;
        }

        modifiers["Base value"] = -110;

        if (civInfo.CityStatePersonality == CityStatePersonality.Hostile)
            modifiers["Hostile"] = -10;
        if (civInfo.AllyCiv != null && civInfo.AllyCiv != demandingCiv)
            modifiers["Has Ally"] = -10;
        if (GetProtectorCivs().Any(it => it != demandingCiv))
            modifiers["Has Protector"] = -20;
        if (demandingWorker)
            modifiers["Demanding a Worker"] = -30;
        if (demandingWorker && civInfo.GetCapital().NotNull().Population.Population < 4)
            modifiers["Demanding a Worker from small City-State"] = -300;
        var recentBullying = civInfo.GetRecentBullyingCountdown();
        if (recentBullying != null && recentBullying > 10)
            modifiers["Very recently paid tribute"] = -300;
        else if (recentBullying != null && recentBullying > 0)
            modifiers["Recently paid tribute"] = -40;
        if (civInfo.GetDiplomacyManager(demandingCiv).NotNull().GetInfluence() < -30)
            modifiers["Influence below -30"] = -300;

        // Slight optimization, we don't do the expensive stuff if we have no chance of getting a >= 0 result
        if (!requireWholeList && modifiers.Values.Sum() < -200)
            return modifiers;

        // highest force is rank=0
        int forceRank = civInfo.GameInfo.GetAliveMajorCivs().OrderByDescending(it => it.GetStatForRanking(RankingType.Force)).ToList().IndexOf(demandingCiv);
        // probably not necessary, but just in case, avoid division by zero if there's some edge case where no players are alive
        int numAlivePlayers = Math.Max(civInfo.GameInfo.GetAliveMajorCivs().Count, 1);
        int globalModifier = civInfo.GameInfo.Ruleset.ModOptions.Constants.TributeGlobalModifier;
        // apparently lowest force gets some pity points
        modifiers["Military Rank"] = unchecked(globalModifier * (numAlivePlayers - forceRank)) / numAlivePlayers;

        if (!requireWholeList && modifiers.Values.Sum() < -100)
            return modifiers;

        int bullyRange = Math.Clamp(civInfo.GameInfo.TileMap.TileMatrix.Count / 10, 5, 10); // Longer range for larger maps
        var inRangeTiles = civInfo.GetCapital().NotNull().GetCenterTile().GetTilesInDistanceRange(new IntRange(1, bullyRange)).ToList();
        int forceNearCity = inRangeTiles
            .SumOf(it => it.MilitaryUnit?.Civ == demandingCiv ? it.MilitaryUnit.NotNull().GetForceEvaluation() : 0);
        int csForce = unchecked(KotlinMath.FloatToInt(KotlinMath.Pow((float)CityStateSeams.GetCityDefendingStrength(civInfo.GetCapital().NotNull()), 1.5f)) + inRangeTiles
            .SumOf(it => it.MilitaryUnit?.Civ == civInfo ? it.MilitaryUnit.NotNull().GetForceEvaluation() : 0));
        float forceRatio = (float)forceNearCity / csForce;
        int localModifier = civInfo.GameInfo.Ruleset.ModOptions.Constants.TributeLocalModifier;

        modifiers["Military near City-State"] =
            forceRatio > 3f ? localModifier
            : forceRatio > 2f ? localModifier * 4 / 5
            : forceRatio > 1.5f ? localModifier * 3 / 5
            : forceRatio > 1f ? localModifier * 2 / 5
            : forceRatio > 0.5f ? localModifier / 5
            : 0;

        return modifiers;
    }

    public int GoldGainedByTribute()
    {
        // These values are close enough, linear increase throughout the game
        int gold = KotlinMath.FloatToInt(10 * civInfo.GameInfo.Speed.GoldGiftModifier) * 5; // rounding down to nearest 5
        float turnsToIncrement = civInfo.GameInfo.Speed.CityStateTributeScalingInterval;
        gold = unchecked(gold + 5 * KotlinMath.FloatToInt(civInfo.GameInfo.Turns / turnsToIncrement));

        return gold;
    }

    public void TributeGold(Civilization demandingCiv)
    {
        if (!civInfo.IsCityState) throw new InvalidOperationException("You can only demand gold from City-States!");
        int goldAmount = GoldGainedByTribute();
        demandingCiv.AddGold(goldAmount);
        civInfo.GetDiplomacyManager(demandingCiv).NotNull().AddInfluence(-15f);
        CityStateBullied(demandingCiv);
        civInfo.AddFlag(CivFlags.RecentlyBullied.ToString(), 20);
    }

    public void TributeWorker(Civilization demandingCiv)
    {
        if (!civInfo.IsCityState) throw new InvalidOperationException("You can only demand workers from City-States!");
        var rng = civInfo.GetDiplomacyManager(demandingCiv).NotNull().State.StateBasedRandom("CityStateFunctions.tributeWorker");

        var buildableWorkerLikeUnits = civInfo.GameInfo.Ruleset.Units.Values.Where(it =>
            ((IHasUniques)it).HasUnique(UniqueType.BuildImprovements) &&
                it.IsCivilian() && it.IsBuildable(civInfo)).ToList();
        if (buildableWorkerLikeUnits.Count == 0) return; // Bad luck?
        demandingCiv.Units.PlaceUnitNearTile(civInfo.GetCapital().NotNull().Location, buildableWorkerLikeUnits.Random(rng));

        civInfo.GetDiplomacyManager(demandingCiv).NotNull().AddInfluence(-50f);
        CityStateBullied(demandingCiv);
        civInfo.AddFlag(CivFlags.RecentlyBullied.ToString(), 20);
    }

    public bool CanProvideStat(Stat statType)
    {
        if (!civInfo.IsCityState)
            return false;
        foreach (var bonus in GetCityStateBonuses(civInfo.CityStateType, RelationshipLevel.Ally))
        {
            if (bonus.Stats[statType] > 0)
                return true;
        }
        return false;
    }

    public void UpdateDiplomaticRelationshipForCityState()
    {
        // Check if city-state invaded by other civs
        if (GetNumThreateningBarbarians() > 0) return; // Assume any players are there to fight barbarians

        foreach (var otherCiv in civInfo.GetKnownCivs().Where(it => it.IsMajorCiv()).ToList())
        {
            if (civInfo.IsAtWarWith(otherCiv)) continue;
            if (otherCiv.HasUnique(UniqueType.CityStateTerritoryAlwaysFriendly)) continue;
            var diplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
            if (diplomacy.HasFlag(DiplomacyFlags.AngerFreeIntrusion)) continue; // They recently helped us

            int unitsInBorder = otherCiv.Units.GetCivUnits().Count(it => !it.IsCivilian() && ReferenceEquals(it.GetTile().GetOwner(), civInfo));
            if (unitsInBorder > 0 && diplomacy.IsRelationshipLevelLT(RelationshipLevel.Friend))
            {
                diplomacy.AddInfluence(-10f);
                if (!diplomacy.HasFlag(DiplomacyFlags.BorderConflict))
                {
                    otherCiv.AddPopupAlert(new PopupAlert(AlertType.BorderConflict, civInfo.CivId));
                    diplomacy.SetFlag(DiplomacyFlags.BorderConflict, 10);
                }
            }
        }
    }

    public void GetFreeTechForCityState()
    {
        // City-States automatically get all techs that at least half of the major civs know
        var researchableTechs = civInfo.GameInfo.Ruleset.Technologies.Values
            .Where(it => !((IHasUniques)it).HasUnique(UniqueType.ResearchableMultipleTimes) && civInfo.Tech.CanBeResearched(it.Name)).ToList();
        foreach (var tech in researchableTechs)
        {
            var aliveMajorCivs = civInfo.GameInfo.GetAliveMajorCivs();
            if (aliveMajorCivs.Count(it => it.Tech.IsResearched(tech.Name)) > aliveMajorCivs.Count / 2)
                civInfo.Tech.AddTechnology(tech.Name);
        }
    }

    public int GetNumThreateningBarbarians()
    {
        if (civInfo.GameInfo.GameParameters.NoBarbarians) return 0;
        var barbarianCiv = civInfo.GameInfo.Civilizations.FirstOrDefault(it => it.IsBarbarian);
        if (barbarianCiv == null) return 0;
        return barbarianCiv.Units.GetCivUnits().Count(it => it.ThreatensCiv(civInfo));
    }

    public void ThreateningBarbarianKilledBy(Civilization otherCiv)
    {
        var diplomacy = civInfo.GetDiplomacyManager(otherCiv).NotNull();
        if (diplomacy.DiplomaticStatus == DiplomaticStatus.War) return; // No reward for enemies

        diplomacy.AddInfluence(12f);

        if (diplomacy.HasFlag(DiplomacyFlags.AngerFreeIntrusion))
            diplomacy.SetFlag(DiplomacyFlags.AngerFreeIntrusion, diplomacy.GetFlag(DiplomacyFlags.AngerFreeIntrusion) + 5);
        else
            diplomacy.SetFlag(DiplomacyFlags.AngerFreeIntrusion, 5);

        otherCiv.AddNotification($"[{civInfo.CivName}] is grateful that you killed a Barbarian that was threatening them!",
            new DiplomacyAction(civInfo.CivId), NotificationCategory.Diplomacy, civInfo.CivName);
    }

    /// <summary>A city state was bullied. What are its protectors going to do about it???</summary>
    private void CityStateBullied(Civilization bully)
    {
        if (!civInfo.IsCityState) return; // What are we doing here?

        foreach (var protector in civInfo.CityStateFunctions.GetProtectorCivs())
        {
            if (!protector.Knows(bully)) // Who?
                continue;
            var protectorDiplomacy = protector.GetDiplomacyManager(bully).NotNull();
            if (protectorDiplomacy.HasModifier(DiplomaticModifiers.BulliedProtectedMinor)
                && protectorDiplomacy.GetFlag(DiplomacyFlags.RememberBulliedProtectedMinor) > 50)
                protectorDiplomacy.AddModifier(DiplomaticModifiers.BulliedProtectedMinor, -10f, limit: -100f); // Penalty less severe for second offence
            else
                protectorDiplomacy.AddModifier(DiplomaticModifiers.BulliedProtectedMinor, -15f, limit: -100f);
            protectorDiplomacy.SetFlag(DiplomacyFlags.RememberBulliedProtectedMinor, 75); // Reset their memory

            if (protector.PlayerType != PlayerType.Human) // Humans can have their own emotions
                bully.AddNotification($"[{protector.CivName}] is upset that you demanded tribute from [{civInfo.CivName}], whom they have pledged to protect!",
                    NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, protector.CivName);
            else // Let humans choose who to side with
                protector.AddPopupAlert(
                    new PopupAlert(
                        AlertType.BulliedProtectedMinor,
                        bully.CivName + "@" + civInfo.CivName)); // we need to pass both civs as argument, hence the horrible chimera
        }

        // Set a diplomatic flag so we remember for future quests (and not to give them any)
        civInfo.GetDiplomacyManager(bully).NotNull().SetFlag(DiplomacyFlags.Bullied, 20);

        // Notify all City-States that we were bullied (for quests)
        foreach (var it in civInfo.GameInfo.GetAliveCityStates())
            it.QuestManager.CityStateBullied(civInfo, bully);
    }

    /// <summary>A city state was attacked. What are its protectors going to do about it??? Also checks for Wary</summary>
    public void CityStateAttacked(Civilization attacker)
    {
        if (!civInfo.IsCityState) return; // What are we doing here?
        if (attacker.IsCityState) return; // City states can't be upset with each other
        var context = GameContext.ForCiv(civInfo, attacker);
        var rng = context.StateBasedRandom("CityStateFunctions.cityStateAttacked");

        // We might become wary!
        if (attacker.IsMinorCivWarmonger())
        { // They've attacked a lot of city-states
            civInfo.GetDiplomacyManager(attacker).NotNull().BecomeWary();
        }
        else if (attacker.IsMinorCivAggressor())
        { // They've attacked a few
            if (rng.NextBoolean()) // 50% chance
                civInfo.GetDiplomacyManager(attacker).NotNull().BecomeWary();
        }
        // Others might become wary!
        if (attacker.IsMinorCivAggressor()) MakeOtherCityStatesWaryOfAttacker(attacker);

        TriggerProtectorCivs(attacker);

        // Even if we aren't *technically* protectors, we *can* still be pissed you attacked our allies
        TriggerAllyCivs(attacker);

        // Set up war with major pseudo-quest
        civInfo.QuestManager.WasAttackedBy(attacker);
        civInfo.GetDiplomacyManager(attacker).NotNull().SetFlag(DiplomacyFlags.RecentlyAttacked, 2); // Reminder to ask for unit gifts in 2 turns
    }

    private void MakeOtherCityStatesWaryOfAttacker(Civilization attacker)
    {
        var context = GameContext.ForCiv(civInfo, attacker);
        var rng = context.StateBasedRandom("CityStateFunctions.makeOtherCityStatesWaryOfAttacker");
        foreach (var cityState in civInfo.GameInfo.GetAliveCityStates())
        {
            if (cityState == civInfo) // Must be a different minor
                continue;
            if (cityState.AllyCiv == attacker) // Must not be allied to the attacker
                continue;
            if (!cityState.Knows(attacker)) // Must have met
                continue;
            if (cityState.QuestManager.WantsDead(civInfo.CivId)) // Must not want us dead
                continue;

            int probability;
            if (attacker.IsMinorCivWarmonger())
            {
                // High probability if very aggressive
                probability = cityState.GetProximity(attacker) switch
                {
                    Proximity.Neighbors => 100,
                    Proximity.Close => 75,
                    Proximity.Far => 50,
                    Proximity.Distant => 25,
                    _ => 0,
                };
            }
            else
            {
                // Lower probability if only somewhat aggressive
                probability = cityState.GetProximity(attacker) switch
                {
                    Proximity.Neighbors => 50,
                    Proximity.Close => 20,
                    _ => 0,
                };
            }

            // Higher probability if already at war
            if (cityState.IsAtWarWith(attacker))
                probability += 50;

            if (rng.NextInt(100) < probability)
                cityState.GetDiplomacyManager(attacker).NotNull().BecomeWary();
        }
    }

    private void TriggerProtectorCivs(Civilization attacker)
    {
        foreach (var protector in civInfo.CityStateFunctions.GetProtectorCivs())
        {
            var protectorDiplomacy = protector.GetDiplomacyManager(attacker);
            if (protectorDiplomacy == null) continue; // Who?
            if (protectorDiplomacy.HasModifier(DiplomaticModifiers.AttackedProtectedMinor)
                && protectorDiplomacy.GetFlag(DiplomacyFlags.RememberAttackedProtectedMinor) > 50)
                protectorDiplomacy.AddModifier(DiplomaticModifiers.AttackedProtectedMinor, -15f, limit: -100f); // Penalty less severe for second offence
            else
                protectorDiplomacy.AddModifier(DiplomaticModifiers.AttackedProtectedMinor, -20f, limit: -100f);
            protectorDiplomacy.SetFlag(DiplomacyFlags.RememberAttackedProtectedMinor, 75); // Reset their memory

            if (protector.PlayerType != PlayerType.Human) // Humans can have their own emotions
                attacker.AddNotification(
                    $"[{protector.CivName}] is upset that you attacked [{civInfo.CivName}], whom they have pledged to protect!",
                    NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, protector.CivName);
            else // Let humans choose who to side with
                protector.AddPopupAlert(
                    new PopupAlert(
                        AlertType.AttackedProtectedMinor,
                        attacker.CivName + "@" + civInfo.CivName)); // we need to pass both civs as argument, hence the horrible chimera
        }
    }

    private void TriggerAllyCivs(Civilization attacker)
    {
        var allyCiv = civInfo.AllyCiv;
        if (allyCiv != null && !civInfo.CityStateFunctions.GetProtectorCivs().Contains(allyCiv) && allyCiv.Knows(attacker))
        {
            var allyDiplomacy = allyCiv.GetDiplomacyManager(attacker).NotNull();
            // Less than if we were protectors
            allyDiplomacy.AddModifier(DiplomaticModifiers.AttackedAlliedMinor, -10f);

            if (allyCiv.PlayerType != PlayerType.Human) // Humans can have their own emotions
                attacker.AddNotification(
                    $"[{allyCiv.CivName}] is upset that you attacked [{civInfo.CivName}], whom they are allied with!",
                    NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, allyCiv.CivName);
            else // Let humans choose who to side with
                allyCiv.AddPopupAlert(
                    new PopupAlert(
                        AlertType.AttackedAllyMinor,
                        attacker.CivName + "@" + civInfo.CivName));
        }
    }

    /// <summary>A city state was destroyed. Its protectors are going to be upset!</summary>
    public void CityStateDestroyed(Civilization attacker)
    {
        if (!civInfo.IsCityState) return; // What are we doing here?

        foreach (var protector in civInfo.CityStateFunctions.GetProtectorCivs())
        {
            if (!protector.Knows(attacker)) // Who?
                continue;
            var protectorDiplomacy = protector.GetDiplomacyManager(attacker).NotNull();
            if (protectorDiplomacy.HasModifier(DiplomaticModifiers.DestroyedProtectedMinor))
                protectorDiplomacy.AddModifier(DiplomaticModifiers.DestroyedProtectedMinor, -10f, limit: -100f); // Penalty less severe for second offence
            else
                protectorDiplomacy.AddModifier(DiplomaticModifiers.DestroyedProtectedMinor, -40f, limit: -100f); // Oof
            protectorDiplomacy.SetFlag(DiplomacyFlags.RememberDestroyedProtectedMinor, 125); // Reset their memory

            if (protector.PlayerType != PlayerType.Human) // Humans can have their own emotions
                attacker.AddNotification($"[{protector.CivName}] is outraged that you destroyed [{civInfo.CivName}], whom they had pledged to protect!",
                    NotificationCategory.Diplomacy, NotificationIcon.Diplomacy, protector.CivName);
            protector.AddNotification($"[{attacker.CivName}] has destroyed [{civInfo.CivName}], whom you had pledged to protect!",
                NotificationCategory.Diplomacy, attacker.CivName, NotificationIcon.Death, civInfo.CivName);
        }

        // Notify all City-States that we were killed (for quest completion)
        foreach (var it in civInfo.GameInfo.GetAliveCityStates())
            it.QuestManager.CityStateConquered(civInfo, attacker);

        CityStateBuyout.KeepFriendshipsOnDeath(civInfo);
    }

    /// <summary>Asks all met majors that haven't yet declared wor on <paramref name="attacker"/> to at least give some units</summary>
    public void AskForUnitGifts(Civilization attacker)
    {
        if (attacker.IsDefeated() || civInfo.IsDefeated()) // never mind, someone died
            return;
        if (civInfo.Cities.Count == 0) // Can't receive units with no cities
            return;

        foreach (var thirdCiv in civInfo.GetKnownCivs()
            .Where(it => it != attacker && it.IsAlive() && it.Knows(attacker) && !it.IsAtWarWith(attacker) && it.IsMajorCiv()))
        {
            thirdCiv.AddNotification(
                $"[{civInfo.CivName}] is being attacked by [{attacker.CivName}] and asks all major civilizations to help them out by gifting them military units.",
                civInfo.GetCapital().NotNull().Location,
                NotificationCategory.Diplomacy,
                civInfo.CivName,
                "OtherIcons/Present");
        }
    }

    public ResourceSupplyList GetCityStateResourcesForAlly()
    {
        var resourceSupplyList = new ResourceSupplyList();
        // Kotlin to-do (no ticket): city-states don't give allies resources from civ-wide uniques!
        foreach (var city in civInfo.Cities)
        {
            // IGNORE the fact that they consume their own resources - #4769
            resourceSupplyList.AddPositiveByResource(city.GetResourcesGeneratedByCity(), Constants.CityStates);
        }
        return resourceSupplyList;
    }

    // Kotlin @Deprecated: forEachUniqueProvidedByCityStates is faster. If not viable, then this can still be used
    public IEnumerable<UniqueObject> GetUniquesProvidedByCityStates(UniqueType uniqueType, GameContext gameContext)
    {
        if (civInfo.IsCityState) return [];
        return civInfo.Cache.CityStateBonusUniqueMaps
            .SelectMany(it => it.GetMatchingUniques(uniqueType, gameContext));
    }

    public void ForEachUniqueProvidedByCityStates(UniqueType uniqueType, GameContext gameContext, Action<UniqueObject> op)
    {
        if (civInfo.IsCityState) return;
        foreach (var uniqueMap in civInfo.Cache.CityStateBonusUniqueMaps)
            uniqueMap.ForEachMatchingUnique(uniqueType, gameContext, op);
    }

    public static IEnumerable<UniqueObject> GetCityStateBonuses(CityStateType cityStateType, RelationshipLevel relationshipLevel, UniqueType? uniqueType = null)
    {
        UniqueMap cityStateUniqueMap;
        switch (relationshipLevel)
        {
            case RelationshipLevel.Ally: cityStateUniqueMap = cityStateType.AllyBonusUniqueMap; break;
            case RelationshipLevel.Friend: cityStateUniqueMap = cityStateType.FriendBonusUniqueMap; break;
            default: return [];
        }
        return uniqueType == null ? cityStateUniqueMap.GetAllUniques()
            : cityStateUniqueMap.GetUniques(uniqueType.Value);
    }
}
