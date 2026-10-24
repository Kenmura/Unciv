using Unciv.Logic.Battle;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/MotivationToAttackAutomation.kt (T088). Purity annotations dropped (T027 policy).
// - Kotlin sums Float lists in Float; LINQ Sum(float) accumulates in double, so the modifier sum is a plain float loop.
// - Kotlin Float.pow is a Double pow narrowed to Float: KotlinMath.Pow.
// - getAttackPathsModifier is in MotivationToAttackAutomation.AttackPaths.cs (a partial class: one file per at most 12 namespaces).

public static partial class MotivationToAttackAutomation
{
    /// <summary>
    /// Will return the motivation to attack, but might short circuit if the value is guaranteed to
    /// be lower than <paramref name="atLeast"/>. So any values below <paramref name="atLeast"/> should not be used for comparison.
    /// </summary>
    public static float HasAtLeastMotivationToAttack(CivilizationClass civInfo, ForeignCivView targetCivView, float atLeast)
    {
        var targetCiv = targetCivView.GetCiv();
        var diplomacyManager = civInfo.GetDiplomacyManager(targetCiv).NotNull();
        var personality = civInfo.GetPersonality();

        var civView = new GameView((GameInfo)civInfo.GameInfo, civInfo).CivView;
        var targetCitiesWithOurCity = ThreatManager.GetNeighboringCitiesOfOtherCivs(civView)
            .Where(it => it.Item2.OwningCiv().GetCiv() == targetCiv)
            // Temporary - the logic below still works on City
            .Select(pair => (civInfo.Cities.First(it => it.Location == pair.Item1.Location()), targetCiv.Cities.First(it => it.Location == pair.Item2.Location())))
            .ToList();
        var targetCities = targetCitiesWithOurCity.Select(it => it.Item2).ToList();

        if (targetCitiesWithOurCity.Count == 0) return 0f;

        if (targetCities.All(it => HasNoUnitsThatCanAttackCityWithoutDying(civInfo, it)))
            return 0f;

        const float baseForce = 100f;

        var ourCombatStrength = CalculateSelfCombatStrength(civInfo, baseForce);
        var theirCombatStrength = CalculateCombatStrengthWithProtectors(targetCiv, baseForce, civInfo);

        var modifiers = new List<(string, float)>();

        // If our personality is to declare war more then we should have a higher base motivation (a negative number closer to 0)
        modifiers.Add(("Base motivation", -(15f * personality.InverseScaledFocus(PersonalityValue.DeclareWar))));

        modifiers.Add(("Relative combat strength", GetCombatStrengthModifier(civInfo, targetCiv, ourCombatStrength, theirCombatStrength + 0.8f * ThreatManager.GetCombinedForceOfWarringCivs(civView))));
        // Kotlin note: for now this will be a very high value because the AI can't handle multiple fronts, this should be changed later though
        modifiers.Add(("Concurrent wars", -civInfo.GetCivsAtWarWith().Count(it => it.IsMajorCiv() && it != targetCiv) * 20f));
        modifiers.Add(("Their concurrent wars", targetCiv.GetCivsAtWarWith().Count(it => it.IsMajorCiv()) * 3f));

        modifiers.Add(("Their allies", GetDefensivePactAlliesScore(targetCiv, civInfo, baseForce, ourCombatStrength)));

        if (!ThreatManager.GetNeighboringCivilizations(civView).Select(it => it.GetCiv()).Any(it => it != targetCiv && it.IsMajorCiv()
                && civInfo.GetDiplomacyManager(it).NotNull().IsRelationshipLevelLT(RelationshipLevel.Friend)))
            modifiers.Add(("No other threats", 10f));

        if (targetCiv.IsMajorCiv())
        {
            var scoreRatioModifier = GetScoreRatioModifier(targetCiv, civInfo);
            modifiers.Add(("Relative score", scoreRatioModifier));

            if (civInfo.Stats.GetUnitSupplyDeficit() != 0)
            {
                modifiers.Add(("Over unit supply", Math.Min(civInfo.Stats.GetUnitSupplyDeficit() * 2f, 20f)));
            }
            else if (targetCiv.Stats.GetUnitSupplyDeficit() == 0 && !targetCiv.IsCityState)
            {
                modifiers.Add(("Relative production", GetProductionRatioModifier(civInfo, targetCiv)));
            }
        }

        var minTargetCityDistance = targetCitiesWithOurCity.Min(it => it.Item2.GetCenterTile().AerialDistanceTo(it.Item1.GetCenterTile()));
        // Defensive civs should avoid fighting civilizations that are farther away and don't pose a threat
        modifiers.Add(("Far away cities", (minTargetCityDistance > 20 ? -10f
            : minTargetCityDistance > 14 ? -8f
            : minTargetCityDistance > 10 ? -3f
            : 0f) * personality.InverseScaledFocus(PersonalityValue.Aggressive)));

        // Defensive civs want to deal with potential nearby cities to protect themselves
        if (minTargetCityDistance < 6)
            modifiers.Add(("Close cities", 5f * personality.InverseScaledFocus(PersonalityValue.Aggressive)));

        AddDiplomacyModifiers(modifiers, civInfo, diplomacyManager, personality);

        if (diplomacyManager.ResourcesFromTrade().Any(it => it.Amount > 0))
            modifiers.Add(("Receiving trade resources", -8f * personality.ScaledFocus(PersonalityValue.Commerce)));

        // If their cities don't have any nearby cities that are also targets to us and it doesn't include their capital
        // Then there cities are likely isolated and a good target.
        if ((targetCiv.GetCapital(true) is not { } theirCapital || !targetCities.Contains(theirCapital))
                && targetCities.All(theirCity => !theirCity.GetNeighboringCities().Any(it => !targetCities.Contains(it))))
        {
            modifiers.Add(("Isolated city", 10f * personality.ScaledFocus(PersonalityValue.Aggressive)));
        }

        if (targetCiv.IsCityState)
            AddCityStateModifiers(modifiers, civInfo, targetCiv, personality);

        modifiers.AddRange(GetWonderBasedMotivations(targetCiv));

        modifiers.Add(("War with allies", GetAlliedWarMotivation(civInfo, targetCiv)));

        // Purely for debugging, remove modifiers that don't have an effect
        modifiers.RemoveAll(it => it.Item2 == 0f);
        var motivationSoFar = 0f;
        foreach (var modifier in modifiers) motivationSoFar += modifier.Item2;

        // Short-circuit to avoid A-star
        if (motivationSoFar < atLeast) return motivationSoFar;

        motivationSoFar += GetAttackPathsModifier(civInfo, targetCiv, targetCitiesWithOurCity);

        return motivationSoFar;
    }

    /// <summary>The agreement, relationship, denunciation and war-planning modifiers (contiguous in the Kotlin function, same order).</summary>
    private static void AddDiplomacyModifiers(List<(string, float)> modifiers, CivilizationClass civInfo, DiplomacyManager diplomacyManager, Personality personality)
    {
        if (diplomacyManager.HasFlag(DiplomacyFlags.ResearchAgreement))
            modifiers.Add(("Research Agreement", -5f * personality.ScaledFocus(PersonalityValue.Science) * personality.ScaledFocus(PersonalityValue.Commerce)));

        if (diplomacyManager.HasFlag(DiplomacyFlags.DeclarationOfFriendship))
            modifiers.Add(("Declaration of Friendship", -10f * personality.ScaledFocus(PersonalityValue.Loyal)));

        if (diplomacyManager.HasFlag(DiplomacyFlags.DefensivePact))
            modifiers.Add(("Defensive Pact", -15f * personality.ScaledFocus(PersonalityValue.Loyal)));

        modifiers.Add(("Relationship", GetRelationshipModifier(diplomacyManager)));

        if (diplomacyManager.HasFlag(DiplomacyFlags.Denunciation))
        {
            modifiers.Add(("Denunciation", 5f * personality.InverseScaledFocus(PersonalityValue.Diplomacy)));
        }

        if (diplomacyManager.HasFlag(DiplomacyFlags.WaryOf) && diplomacyManager.GetFlag(DiplomacyFlags.WaryOf) < 0)
        {
            // Completely defensive civs will plan defensively and have a 0 here
            modifiers.Add(("PlanningAttack", -diplomacyManager.GetFlag(DiplomacyFlags.WaryOf) * personality.ScaledFocus(PersonalityValue.Aggressive) / 2));
        }
        else
        {
            var attacksPlanned = civInfo.Diplomacy.Values.Count(it => it.HasFlag(DiplomacyFlags.WaryOf) && it.GetFlag(DiplomacyFlags.WaryOf) < 0);
            modifiers.Add(("PlanningAttackAgainstOtherCivs", -attacksPlanned * 5f * personality.InverseScaledFocus(PersonalityValue.Aggressive)));
        }
    }

    private static void AddCityStateModifiers(List<(string, float)> modifiers, CivilizationClass civInfo, CivilizationClass targetCiv, Personality personality)
    {
        modifiers.Add(("Protectors", -targetCiv.CityStateFunctions.GetProtectorCivs().Count * 3f * personality.ScaledFocus(PersonalityValue.Diplomacy)));
        //The more potential friends of this CS, the more times the friend bonus is shared and the utilitarian option is to leave it alive
        modifiers.Add(("Influence", -targetCiv.GetDiplomacyManager(civInfo).NotNull().GetInfluence() / 10f * personality.ScaledFocus(PersonalityValue.Diplomacy)));
        // The more we invested into the city state already, the less likely we're going to attack it, and vice versa
        if (targetCiv.AllyCiv == civInfo)
            modifiers.Add(("Allied City-state", -20 * personality.ScaledFocus(PersonalityValue.Diplomacy))); // There had better be a DAMN good reason
    }

    private static float CalculateCombatStrengthWithProtectors(CivilizationClass otherCiv, float baseForce, CivilizationClass civInfo)
    {
        var theirCombatStrength = CalculateSelfCombatStrength(otherCiv, baseForce);

        //for city-states, also consider their protectors
        if (otherCiv.IsCityState && otherCiv.CityStateFunctions.GetProtectorCivs().Count != 0)
        {
            var protectorForce = 0;
            foreach (var protector in otherCiv.CityStateFunctions.GetProtectorCivs().Where(it => it != civInfo))
                protectorForce = unchecked(protectorForce + protector.GetStatForRanking(RankingType.Force));
            theirCombatStrength += protectorForce;
        }
        return theirCombatStrength;
    }

    private static float CalculateSelfCombatStrength(CivilizationClass civInfo, float baseForce)
    {
        var ourCombatStrength = civInfo.GetStatForRanking(RankingType.Force) + baseForce;
        if (civInfo.GetCapital() != null) ourCombatStrength += new CityCombatant(civInfo.GetCapital().NotNull()).GetCityStrength();
        return ourCombatStrength;
    }

    private static List<(string, float)> GetWonderBasedMotivations(CivilizationClass otherCiv)
    {
        var wonderCount = 0;
        var modifiers = new List<(string, float)>();
        foreach (var city in otherCiv.Cities)
        {
            var construction = city.CityConstructions.GetCurrentConstruction();
            if (construction is Building building && ((IHasUniques)building).HasUnique(UniqueType.TriggersCulturalVictory))
                modifiers.Add(("About to win", 15f));
            if (construction is BaseUnit unit && ((IHasUniques)unit).HasUnique(UniqueType.AddInCapital))
                modifiers.Add(("About to win", 15f));
            wonderCount += city.CityConstructions.GetBuiltBuildings().Count(it => it.IsWonder);
        }

        // The more wonders they have, the more beneficial it is to conquer them
        // Civs need an army to protect thier wonders which give the most score
        if (wonderCount > 0)
            modifiers.Add(("Owned Wonders", (float)wonderCount));
        return modifiers;
    }

    /// <summary>If they are at war with our allies, then we should join in</summary>
    private static float GetAlliedWarMotivation(CivilizationClass civInfo, CivilizationClass otherCiv)
    {
        var alliedWarMotivation = 0f;
        foreach (var thirdCiv in civInfo.GetDiplomacyManager(otherCiv).NotNull().GetCommonKnownCivs())
        {
            var thirdCivDiploManager = civInfo.GetDiplomacyManager(thirdCiv).NotNull();
            if (thirdCivDiploManager.IsRelationshipLevelLT(RelationshipLevel.Friend)) continue;

            if (thirdCiv.GetDiplomacyManager(otherCiv).NotNull().HasFlag(DiplomacyFlags.Denunciation))
                alliedWarMotivation += 2f;

            if (thirdCiv.IsAtWarWith(otherCiv))
            {
                alliedWarMotivation += thirdCivDiploManager.HasFlag(DiplomacyFlags.DefensivePact) ? 15f
                    : thirdCivDiploManager.HasFlag(DiplomacyFlags.DeclarationOfFriendship) ? 5f
                    : 2f;
            }
        }
        return alliedWarMotivation * civInfo.GetPersonality().ScaledFocus(PersonalityValue.Loyal);
    }

    private static float GetRelationshipModifier(DiplomacyManager diplomacyManager)
    {
        var relationshipModifier = diplomacyManager.RelationshipIgnoreAfraid() switch
        {
            RelationshipLevel.Unforgivable => 10f,
            RelationshipLevel.Enemy => 5f,
            RelationshipLevel.Competitor => 2f,
            RelationshipLevel.Favorable => -2f,
            RelationshipLevel.Friend => -5f,
            RelationshipLevel.Ally => -10f, // this is so that ally + DoF is not too unbalanced -
            // still possible for AI to declare war for isolated city
            _ => 0f,
        };
        return relationshipModifier * diplomacyManager.CivInfo.GetPersonality().ScaledFocus(PersonalityValue.Loyal);
    }

    private static float GetProductionRatioModifier(CivilizationClass civInfo, CivilizationClass otherCiv)
    {
        // If either of our Civs are suffering from a supply deficit, our army must be too large
        // There is no easy way to check the raw production if a civ has a supply deficit
        // We might try to divide the current production by the getUnitSupplyProductionPenalty()
        // but it only is true for our turn and not the previous turn and might result in odd values

        var productionRatio = (float)civInfo.GetStatForRanking(RankingType.Production) / otherCiv.GetStatForRanking(RankingType.Production);
        return productionRatio > 2f ? 10f
            : productionRatio > 1.5f ? 5f
            : productionRatio > 1.2 ? 3f
            : productionRatio > .8f ? 0f
            : productionRatio > .5f ? -5f
            : productionRatio > .25f ? -10f
            : -15f;
    }

    private static float GetScoreRatioModifier(CivilizationClass otherCiv, CivilizationClass civInfo)
    {
        // Civs with more score are more threatening to our victory
        // Bias towards attacking civs with a high score and low military
        // Bias against attacking civs with a low score and a high military
        // Designed to mitigate AIs declaring war on weaker civs instead of their rivals
        var scoreRatio = (float)otherCiv.GetStatForRanking(RankingType.Score) / civInfo.GetStatForRanking(RankingType.Score);
        return scoreRatio > 2f ? 15f
            : scoreRatio > 1.5f ? 10f
            : scoreRatio > 1.25f ? 5f
            : scoreRatio > 1f ? 2f
            : scoreRatio > .8f ? 0f
            : scoreRatio > .5f ? -2f
            : scoreRatio > .25f ? -5f
            : -10f;
    }

    private static float GetDefensivePactAlliesScore(CivilizationClass otherCiv, CivilizationClass civInfo, float baseForce, float ourCombatStrength)
    {
        var theirAlliesValue = 0f;
        foreach (var thirdCiv in otherCiv.Diplomacy.Values.Where(it => it.HasFlag(DiplomacyFlags.DefensivePact) && it.OtherCiv != civInfo).ToList())
        {
            // Deterrence comes from the ally's own force, not the target's
            var thirdCivCombatStrengthRatio = (thirdCiv.OtherCiv.GetStatForRanking(RankingType.Force) + baseForce) / ourCombatStrength;
            theirAlliesValue += thirdCivCombatStrengthRatio > 5 ? -15f
                : thirdCivCombatStrengthRatio > 2.5 ? -10f
                : thirdCivCombatStrengthRatio > 2 ? -8f
                : thirdCivCombatStrengthRatio > 1.5 ? -5f
                : thirdCivCombatStrengthRatio > .8 ? -2f
                : 0f;
        }
        return theirAlliesValue;
    }

    private static float GetCombatStrengthModifier(CivilizationClass civInfo, CivilizationClass targetCiv, float ourCombatStrength, float theirCombatStrength)
    {
        var combatStrengthRatio = ourCombatStrength / theirCombatStrength;

        // At higher difficulty levels the AI gets a unit production boost.
        // In that case while we may have more units than them, we don't nessesarily want to be more aggressive.
        // This is to reduce the amount that the AI targets players at these higher levels somewhat.
        if (civInfo.IsAI() && targetCiv.IsHuman())
        {
            combatStrengthRatio *= KotlinMath.Pow(civInfo.GameInfo.GetDifficulty().AiUnitCostModifier, 1.5f);
        }
        return combatStrengthRatio > 5f ? 20f
            : combatStrengthRatio > 4f ? 15f
            : combatStrengthRatio > 3f ? 12f
            : combatStrengthRatio > 2f ? 10f
            : combatStrengthRatio > 1.8f ? 8f
            : combatStrengthRatio > 1.6f ? 6f
            : combatStrengthRatio > 1.4f ? 4f
            : combatStrengthRatio > 1.2f ? 2f
            : combatStrengthRatio > .8f ? -5f
            : combatStrengthRatio > .6f ? -10f
            : combatStrengthRatio > .4f ? -20f
            : -40f;
    }

    private static bool HasNoUnitsThatCanAttackCityWithoutDying(CivilizationClass civInfo, CityObject theirCity) =>
        !civInfo.Units.GetCivUnits().Where(it => it.IsMilitary()).Any(it =>
        {
            var damageReceivedWhenAttacking = BattleDamage.CalculateDamageToAttacker(new MapUnitCombatant(it), new CityCombatant(theirCity));
            return damageReceivedWhenAttacking < 100;
        });
}
