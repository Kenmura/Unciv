using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using MapUnitObject = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/ReligionManager.kt (T057).
// - City, MapUnit and GameInfo are used through their contracts (CivilizationContracts.cs).
// - Kotlin's `Random(seed)` is a GameRandom.
// - The 'remainingFoundableReligionsBreakdown' Sequence of Pair is an iterator of tuples.
public class ReligionManager : IsPartOfGameInfoSerialization
{
    private Civilization? civInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    [JsonPropertyName("storedFaith")]
    public int StoredFaith { get; set; }

    /// <summary>
    /// Transient.
    /// You might ask why this is the transient variable, and not the one in GameInfo.
    /// After all, filling a hashmap is much easier than later distributing its contents over multiple classes.
    /// There is, however, a problem.
    /// When founding a religion, the religion of your pantheon doesn't immediately disappear.
    /// It just stops growing. Your new religion will then have to spread out from your holy city
    /// and convert these cities. This means, that civilizations can have multiple active religions
    /// in some cases. We only save one of them in this class to reduce the amount of logic necessary.
    /// But the other one should still be _somewhere_. So our only option is to have the GameInfo
    /// contain the master list, and the ReligionManagers retrieve it from there every time the game loads.
    /// </summary>
    [JsonIgnore]
    public Religion? Religion { get; set; }

    [JsonPropertyName("religionState")]
    [JsonInclude]
    public ReligionState ReligionState { get; private set; } = ReligionState.None;

    /// <summary>
    /// Counter containing the number of free beliefs types that this civ can add to its religion this turn.
    /// Uses String instead of BeliefType enum for serialization reasons
    /// </summary>
    [JsonPropertyName("freeBeliefs")]
    public Counter<string> FreeBeliefs { get; set; } = new();

    // These cannot be transient, as saving and loading after using a great prophet but before
    // founding a religion would break :(
    // Only used for keeping track of the city a prophet was used when founding a religion
    [JsonPropertyName("foundingCityId")]
    [JsonInclude]
    private string? foundingCityId;

    [JsonPropertyName("shouldChoosePantheonBelief")]
    [JsonInclude]
    private bool shouldChoosePantheonBelief;

    /// <summary>The city the founding prophet was used in; read by the FoundReligion command to validate before <see cref="FoundReligion(string, string)"/> looks it up.</summary>
    [JsonIgnore]
    internal string? FoundingCityId => foundingCityId;

    public ReligionManager Clone()
    {
        var clone = new ReligionManager
        {
            foundingCityId = foundingCityId,
            shouldChoosePantheonBelief = shouldChoosePantheonBelief,
            StoredFaith = StoredFaith,
            ReligionState = ReligionState,
        };
        clone.FreeBeliefs.PutAll(FreeBeliefs);
        return clone;
    }

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
        // Find our religion from the map of founded religions.
        // First check if there is any major religion
        Religion = civInfo.GameInfo.Religions.Values.FirstOrDefault(it => ReferenceEquals(it.FoundingCiv, civInfo) && it.IsMajorReligion());
        // If there isn't, check for just pantheons.
        if (Religion != null) return;
        Religion = civInfo.GameInfo.Religions.Values.FirstOrDefault(it => ReferenceEquals(it.FoundingCiv, civInfo));
    }

    public void StartTurn()
    {
        if (CanGenerateProphet()) GenerateProphet();
    }

    public void EndTurn(int faithFromNewTurn)
    {
        StoredFaith += faithFromNewTurn;
    }

    public bool IsMajorityReligionForCiv(Religion religion) =>
        CivInfo.Cities.Count(it => it.Religion.GetMajorityReligion() == religion) > CivInfo.Cities.Count / 2;

    /// <summary>
    /// This helper function makes it easy to interface the Counter&lt;String&gt; <see cref="FreeBeliefs"/> with functions
    /// that use Counter&lt;BeliefType&gt;
    /// </summary>
    public Counter<BeliefType> FreeBeliefsAsEnums()
    {
        var toReturn = new Counter<BeliefType>();
        foreach (var entry in FreeBeliefs)
            toReturn.Add(ParseBeliefType(entry.Key), entry.Value);
        return toReturn;
    }

    public bool HasFreeBeliefs() => FreeBeliefs.SumValues() > 0;

    public bool UsingFreeBeliefs() =>
        (ReligionState == ReligionState.None && StoredFaith < FaithForPantheon()) // first pantheon is free
        || ReligionState == ReligionState.Pantheon // any subsequent pantheons before founding a religion
        || ReligionState == ReligionState.Religion || ReligionState == ReligionState.EnhancedReligion; // any belief adding outside of great prophet use

    public int FaithForPantheon(int additionalCivs = 0)
    {
        var gameInfo = CivInfo.GameInfo;
        int numCivs = additionalCivs + gameInfo.Civilizations.Count(it => it.IsMajorCiv() && it.ReligionManager.Religion != null);
        int cost = gameInfo.Ruleset.ModOptions.Constants.PantheonBase + numCivs * gameInfo.Ruleset.ModOptions.Constants.PantheonGrowth;
        return KotlinMath.RoundToInt(cost * gameInfo.Speed.FaithCostModifier);
    }

    /// <summary>
    /// Used for founding the pantheon and for each time the player gets additional pantheon beliefs
    /// before forming a religion
    /// </summary>
    public bool CanFoundOrExpandPantheon()
    {
        if (!CivInfo.GameInfo.IsReligionEnabled()) return false;
        if (ReligionState > ReligionState.Pantheon) return false;
        if (!CivInfo.IsMajorCiv()) return false;
        if (NumberOfBeliefsAvailable(BeliefType.Pantheon) == 0)
            return false; // no more available pantheons
        if (CivInfo.GameInfo.Civilizations.Any(it => it.ReligionManager.ReligionState == ReligionState.EnhancedReligion)
            && CivInfo.GameInfo.Civilizations.Count(it => it.ReligionManager.ReligionState >= ReligionState.Pantheon) >= MaxNumberOfReligions())
        {
            return false;
        }
        return (ReligionState == ReligionState.None && StoredFaith >= FaithForPantheon()) // earned pantheon
            || FreeBeliefs[BeliefType.Pantheon.ToString()] > 0; // free pantheon belief
    }

    private void FoundPantheon(string beliefName, bool useFreeBelief)
    {
        if (!useFreeBelief)
        {
            // paid for the initial pantheon using faith
            StoredFaith -= FaithForPantheon();
        }
        var religion = new Religion(beliefName, CivInfo.GameInfo, CivInfo, CivInfo.GameInfo.Turns);
        Religion = religion;
        CivInfo.GameInfo.Religions[beliefName] = religion;
        foreach (var city in CivInfo.Cities)
            city.Religion.AddPressure(beliefName, 200 * city.Population.Population);

        var humanPlayers = CivInfo.GameInfo.Civilizations.Where(it => it.IsHuman() && !ReferenceEquals(it, CivInfo)).ToList();
        foreach (var civ in humanPlayers)
        {
            string text = civ.GetKnownCivs().Contains(CivInfo) ? $"[{CivInfo.CivName}]" : "[An unknown civilization]";

            civ.AddNotification(text + $" has founded pantheon [{beliefName}]!",
                new ReligionAction(beliefName), Notification.NotificationCategory.Religion, NotificationIcon.Faith);
        }
    }

    public int GreatProphetsEarned() =>
        CivInfo.CivConstructions.GetBoughtItemsWithIncreasingPrice(GetGreatProphetEquivalent()?.Name ?? "");

    // https://www.reddit.com/r/civ/comments/2m82wu/can_anyone_detail_the_finer_points_of_great/
    // Game files (globaldefines.xml)
    public int FaithForNextGreatProphet()
    {
        int greatProphetsEarned = GreatProphetsEarned();

        float faithCost =
            (200 + 100 * greatProphetsEarned * (greatProphetsEarned + 1) / 2f) *
            CivInfo.GameInfo.Speed.FaithCostModifier;

        CivInfo.ForEachMatchingUnique(UniqueType.FaithCostOfGreatProphetChange, unique =>
        {
            faithCost *= unique.Params[0].ToPercent();
        });

        return KotlinMath.FloatToInt(faithCost);
    }

    public bool CanGenerateProphet(bool ignoreFaithAmount = false)
    {
        if (!CivInfo.GameInfo.IsReligionEnabled()) return false; // No religion, no prophets
        if (Religion == null || ReligionState == ReligionState.None) return false; // First get a pantheon, then we'll talk about a real religion
        if (GetGreatProphetEquivalent() == null) return false;
        if (!ignoreFaithAmount && StoredFaith < FaithForNextGreatProphet()) return false;
        if (!CivInfo.IsMajorCiv()) return false;
        if (CivInfo.HasUnique(UniqueType.MayNotGenerateGreatProphet)) return false;
        if (ReligionState == ReligionState.Pantheon && RemainingFoundableReligions() == 0) return false; // too many have been founded
        return true;
    }

    public BaseUnit? GetGreatProphetEquivalent()
    {
        var baseUnit = CivInfo.GameInfo.Ruleset.Units.Values.FirstOrDefault(it => ((IHasUniques)it).HasUnique(UniqueType.MayFoundReligion));
        return baseUnit == null ? null : CivInfo.GetEquivalentUnit(baseUnit);
    }

    private void GenerateProphet()
    {
        var prophetUnit = GetGreatProphetEquivalent();
        if (prophetUnit == null) return; // No prophet units in this mod
        int prophetCost = FaithForNextGreatProphet();
        float prophetSpawnChance = (5f + StoredFaith - prophetCost) / 100f;

        if (CivInfo.GameInfo.SeededRandom(CivInfo.GameInfo.Turns).NextFloat() < prophetSpawnChance)
        {
            CityObject? birthCity;
            if (ReligionState <= ReligionState.Pantheon) birthCity = CivInfo.GetCapital();
            else
            {
                var holyCity = CivInfo.ReligionManager.GetHolyCity();
                if (holyCity != null && ReferenceEquals(holyCity.Civ, CivInfo)) birthCity = holyCity; // If we have a holy city, use it
                else birthCity = CivInfo.GetCapital(); // default to capital
            }
            var prophet = CivInfo.Units.AddUnit(prophetUnit, birthCity);
            if (prophet == null) return;
            if (birthCity != null) // e.g. mosque of djenne bonuses
                prophetUnit.AddConstructionBonuses(prophet, birthCity.CityConstructions);
            prophet.Religion = Religion.NotNull().Name;
            StoredFaith -= prophetCost;
            CivInfo.CivConstructions.BoughtItemsWithIncreasingPrice.Add(prophetUnit.Name, 1);
        }
    }

    private int MaxNumberOfReligions()
    {
        var gameInfo = CivInfo.GameInfo;
        var ruleset = gameInfo.Ruleset;
        float multiplier = ruleset.ModOptions.Constants.ReligionLimitMultiplier;
        int baseValue = ruleset.ModOptions.Constants.ReligionLimitBase;
        int civCount = gameInfo.Civilizations.Count(it => it.IsMajorCiv());
        return Math.Min(ruleset.Religions.Count, baseValue + KotlinMath.FloatToInt(civCount * multiplier));
    }

    /// <summary>Calculates the number of religions that are already founded</summary>
    private int FoundedReligionsCount() => CivInfo.GameInfo.Civilizations.Count(
        it => it.ReligionManager.Religion != null && it.ReligionManager.ReligionState >= ReligionState.Religion);

    /// <summary>Calculates the amount of religions that can still be founded</summary>
    public int RemainingFoundableReligions()
    {
        // count the number of foundable religions left given defined ruleset religions and number of civs in game
        int maxNumberOfAdditionalReligions = MaxNumberOfReligions() - FoundedReligionsCount();

        int availableBeliefsToFound = Math.Min(
            NumberOfBeliefsAvailable(BeliefType.Follower),
            NumberOfBeliefsAvailable(BeliefType.Founder));

        return Math.Min(maxNumberOfAdditionalReligions, availableBeliefsToFound);
    }

    /// <summary>Get info breaking down the reasons behind the result of <see cref="RemainingFoundableReligions"/></summary>
    public IEnumerable<(string, int)> RemainingFoundableReligionsBreakdown()
    {
        var gameInfo = CivInfo.GameInfo;
        var ruleset = gameInfo.Ruleset;
        yield return ("Available religion symbols", ruleset.Religions.Count);

        float multiplier = ruleset.ModOptions.Constants.ReligionLimitMultiplier;
        int baseValue = ruleset.ModOptions.Constants.ReligionLimitBase;
        int civCount = gameInfo.Civilizations.Count(it => it.IsMajorCiv());
        bool hideCivCount = CivInfo.ShouldHideCivCount();
        if (hideCivCount)
        {
            int knownCivs = 1 + CivInfo.GetKnownCivs().Count(it => it.IsMajorCiv());
            int estimatedCivCount = (
                    Math.Max(gameInfo.GameParameters.MinNumberOfPlayers, knownCivs) +
                    gameInfo.GameParameters.MaxNumberOfPlayers - 1
                ) / 2 + 1;
            int civsAndBase = baseValue + KotlinMath.FloatToInt(estimatedCivCount * multiplier);
            yield return ($"Estimated number of civilizations * [{KotlinFloatToString(multiplier)}] + [{baseValue.ToString(CultureInfo.InvariantCulture)}]", civsAndBase);
        }
        else
        {
            int civsAndBase = baseValue + KotlinMath.FloatToInt(civCount * multiplier);
            yield return ($"Number of civilizations * [{KotlinFloatToString(multiplier)}] + [{baseValue.ToString(CultureInfo.InvariantCulture)}]", civsAndBase);
        }

        yield return ("Religions already founded", FoundedReligionsCount());
        yield return ("Available founder beliefs", NumberOfBeliefsAvailable(BeliefType.Founder));
        yield return ("Available follower beliefs", NumberOfBeliefsAvailable(BeliefType.Follower));
    }

    /// <summary>Kotlin <c>Float.toString()</c> for the values that can occur here: shortest round-trip digits with a ".0" for integral values.</summary>
    private static string KotlinFloatToString(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('N') || text.Contains('I') ? text : text + ".0";
    }

    public int NumberOfBeliefsAvailable(BeliefType type)
    {
        var gameInfo = CivInfo.GameInfo;
        int numberOfBeliefs = type == BeliefType.Any
            ? gameInfo.Ruleset.Beliefs.Values.Count()
            : gameInfo.Ruleset.Beliefs.Values.Count(it => it.Type == type);
        return numberOfBeliefs - gameInfo.Religions.SelectMany(it => it.Value.GetBeliefs(type)).Distinct().Count();
        // We need to do the distinct above, as pantheons and religions founded out of those pantheons might share beliefs
    }

    public Religion? GetReligionWithBelief(Belief belief) =>
        CivInfo.GameInfo.Religions.Values.FirstOrDefault(it => it.HasBelief(belief.Name));

    public bool MayFoundReligionAtAll()
    {
        if (!CivInfo.GameInfo.IsReligionEnabled()) return false; // No religion
        if (ReligionState >= ReligionState.Religion) return false; // Already created a major religion

        if (!CivInfo.IsMajorCiv()) return false; // Only major civs may use religion

        if (RemainingFoundableReligions() == 0)
            return false; // Too bad, too many religions have already been founded

        return true;
    }

    public bool MayFoundReligionHere(Tile tile)
    {
        if (!MayFoundReligionAtAll()) return false;
        if (!tile.IsCityCenter()) return false;
        if (tile.GetCity().NotNull().IsHolyCity()) return false;
        // No double holy cities. Not sure if these were allowed in the base game
        return true;
    }

    public void FoundReligion(IMapUnitState prophet)
    {
        if (!MayFoundReligionHere(prophet.GetTile())) return; // How did you do this?
        if (ReligionState == ReligionState.None) shouldChoosePantheonBelief = true;
        ReligionState = ReligionState.FoundingReligion;
        CivInfo.ReligionManager.foundingCityId = prophet.GetTile().GetCity().NotNull().Id;
    }

    public bool MayEnhanceReligionAtAll()
    {
        if (!CivInfo.GameInfo.IsReligionEnabled()) return false;
        if (Religion == null) return false; // First found a pantheon
        if (ReligionState != ReligionState.Religion) return false; // First found an actual religion
        if (!CivInfo.IsMajorCiv()) return false; // Only major civs

        if (NumberOfBeliefsAvailable(BeliefType.Follower) == 0)
            return false; // Mod maker did not provide enough follower beliefs

        if (NumberOfBeliefsAvailable(BeliefType.Enhancer) == 0)
            return false; // Mod maker did not provide enough enhancer beliefs

        return true;
    }

    public bool MayEnhanceReligionHere(Tile tile)
    {
        if (!MayEnhanceReligionAtAll()) return false;
        if (!tile.IsCityCenter()) return false;
        return true;
    }

    public void UseProphetForEnhancingReligion(IMapUnitState prophet)
    {
        var currentTile = prophet.GetTile();
        if (!MayEnhanceReligionHere(currentTile)) return; // How did you do this?
        ReligionState = ReligionState.EnhancingReligion;

        var humanPlayers = CivInfo.GameInfo.Civilizations.Where(it => it.IsHuman() && !ReferenceEquals(it, CivInfo)).ToList();
        var religion = CivInfo.ReligionManager.Religion.NotNull();

        foreach (var civ in humanPlayers)
        {
            string text = civ.GetKnownCivs().Contains(CivInfo) ? $"[{CivInfo.CivName}]" : "[An unknown civilization]";

            civ.AddNotification(text + $" has enhanced [{religion.DisplayName}]!",
                new ReligionAction(religion.DisplayName), Notification.NotificationCategory.Religion, NotificationIcon.Faith);
        }
    }

    /// <summary>
    /// Unifies the selection of what beliefs are available for when a great prophet is expended. Also
    /// accounts for the number of remaining beliefs of each type so that the player is not given a
    /// larger number of beliefs to select than there are available for selection (<see cref="MayFoundReligionAtAll"/>
    /// and <see cref="MayEnhanceReligionAtAll"/> only check if there is 1 founder/enhancer and 1 follower belief
    /// available but the civ may be given more beliefs through uniques or a missing pantheon belief)
    /// </summary>
    private Counter<BeliefType> GetBeliefsToChooseAtProphetUse(bool enhancingReligion)
    {
        string action = enhancingReligion ? "enhancing" : "founding";
        var beliefsToChoose = new Counter<BeliefType>();

        // Counter of the number of available beliefs of each type
        var availableBeliefs = new Counter<BeliefType>();
        foreach (var type in Enum.GetValues<BeliefType>())
        {
            if (type == BeliefType.None) continue;
            availableBeliefs[type] = NumberOfBeliefsAvailable(type);
        }

        // function to help with bookkeeping
        void ChooseBeliefToAdd(BeliefType type, int number)
        {
            int numberToAdd = Math.Min(number, availableBeliefs[type]);
            beliefsToChoose.Add(type, numberToAdd);
            availableBeliefs[type] = availableBeliefs[type] - numberToAdd;
            if (type != BeliefType.Any)
            {
                // deduct from BeliefType.Any as well
                availableBeliefs[BeliefType.Any] = availableBeliefs[BeliefType.Any] - numberToAdd;
            }
        }

        if (enhancingReligion)
        {
            ChooseBeliefToAdd(BeliefType.Enhancer, 1);
        }
        else
        {
            ChooseBeliefToAdd(BeliefType.Founder, 1);
            if (shouldChoosePantheonBelief)
                ChooseBeliefToAdd(BeliefType.Pantheon, 1);
        }
        ChooseBeliefToAdd(BeliefType.Follower, 1);

        CivInfo.ForEachMatchingUnique(UniqueType.FreeExtraBeliefs, unique =>
        {
            if (unique.Params[2] == action)
            {
                var type = ParseBeliefType(unique.Params[1]);
                ChooseBeliefToAdd(type, KotlinMath.ParseInt(unique.Params[0]));
            }
        });
        CivInfo.ForEachMatchingUnique(UniqueType.FreeExtraAnyBeliefs, unique =>
        {
            if (unique.Params[1] == action)
                ChooseBeliefToAdd(BeliefType.Any, KotlinMath.ParseInt(unique.Params[0]));
        });

        foreach (var type in FreeBeliefsAsEnums())
            ChooseBeliefToAdd(type.Key, type.Value);

        return beliefsToChoose;
    }

    /// <summary>Kotlin <c>BeliefType.valueOf</c>: exact enum names only (Enum.Parse would also accept numbers and comma lists).</summary>
    private static BeliefType ParseBeliefType(string name) =>
        Enum.TryParse<BeliefType>(name, out var type) && Enum.GetName(type) == name
            ? type
            : throw new ArgumentException($"No enum constant BeliefType.{name}");

    public Counter<BeliefType> GetBeliefsToChooseAtFounding() => GetBeliefsToChooseAtProphetUse(false);
    public Counter<BeliefType> GetBeliefsToChooseAtEnhancing() => GetBeliefsToChooseAtProphetUse(true);

    public void ChooseBeliefs(List<Belief> beliefs, bool useFreeBeliefs = false)
    {
        // Remove the free beliefs in case we had them
        // Must be done first in case when gain more later
        FreeBeliefs.Clear();

        if (ReligionState == ReligionState.None)
            FoundPantheon(beliefs[0].Name, useFreeBeliefs); // makes religion non-null
        // add beliefs (religion exists at this point)
        Religion.NotNull().AddBeliefs(beliefs);

        switch (ReligionState)
        {
            case ReligionState.None:
                ReligionState = ReligionState.Pantheon;
                CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponFoundingPantheon, CivInfo.State, _ => true, false,
                    unique => UniqueTriggerActivation.TriggerUnique(unique, CivInfo));
                break;
            case ReligionState.FoundingReligion:
                ReligionState = ReligionState.Religion;
                CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponFoundingReligion, CivInfo.State, _ => true, false,
                    unique => UniqueTriggerActivation.TriggerUnique(unique, CivInfo));
                break;
            case ReligionState.EnhancingReligion:
                ReligionState = ReligionState.EnhancedReligion;
                CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponEnhancingReligion, CivInfo.State, _ => true, false,
                    unique => UniqueTriggerActivation.TriggerUnique(unique, CivInfo));
                break;
        }

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponAdoptingPolicyOrBelief, CivInfo.State, _ => true, false, unique =>
        {
            foreach (var belief in beliefs)
                if (unique.GetModifiers(UniqueType.TriggerUponAdoptingPolicyOrBelief).Any(it => it.Params[0] == belief.Name))
                    UniqueTriggerActivation.TriggerUnique(unique, CivInfo,
                        triggerNotificationText: $"due to adopting [{belief.Name}]");
        });

        foreach (var belief in beliefs)
        {
            foreach (var unique in belief.UniqueObjects)
            {
                if (unique.HasTriggerConditional() || !unique.ConditionalsApply(CivInfo.State)) continue;
                int repeats = unique.GetUniqueMultiplier(CivInfo.State);
                for (int i = 0; i < repeats; i++) UniqueTriggerActivation.TriggerUnique(unique, CivInfo);
            }
        }

        CivInfo.UpdateStatsForNextTurn(); // a belief can have an immediate effect on stats
    }

    internal void FoundReligion(string displayName, string name)
    {
        int foundingTurn = Religion?.FoundingTurn // use the turn when we founded our pantheon
            ?? CivInfo.GameInfo.Turns;
        var newReligion = new Religion(name, CivInfo.GameInfo, CivInfo, foundingTurn)
        {
            DisplayName = displayName,
        };
        if (Religion != null)
        {
            newReligion.AddBeliefs(Religion.GetAllBeliefsOrdered());
        }

        Religion = newReligion;
        CivInfo.GameInfo.Religions[name] = newReligion;

        var holyCity = CivInfo.Cities.First(it => it.Id == foundingCityId);
        holyCity.Religion.ReligionThisIsTheHolyCityOf = newReligion.Name;
        holyCity.Religion.AddPressure(name, holyCity.Population.Population * 500);

        foundingCityId = null;
        shouldChoosePantheonBelief = false;

        foreach (var unit in CivInfo.Units.GetCivUnits())
            if (unit.HasUnique(UniqueType.ReligiousUnit) && unit.HasUnique(UniqueType.TakeReligionOverBirthCity))
                unit.Religion = newReligion.Name;

        var humanPlayers = CivInfo.GameInfo.Civilizations.Where(it => it.IsHuman() && !ReferenceEquals(it, CivInfo)).ToList();
        foreach (var civ in humanPlayers)
        {
            if (civ.GetKnownCivs().Contains(CivInfo))
            {
                if (civ.HasExplored(holyCity.GetCenterTile()))
                    civ.AddNotification($"[{CivInfo.CivName}] has founded [{displayName}] in [{holyCity.Name}]!",
                        ReligionAction.WithLocation(holyCity.Location, name),
                        Notification.NotificationCategory.Religion, NotificationIcon.Faith);
                else civ.AddNotification($"[{CivInfo.CivName}] has founded [{displayName}]!",
                    new ReligionAction(name), Notification.NotificationCategory.Religion, NotificationIcon.Faith);
            }
            else civ.AddNotification($"[An unknown civilization] has founded [{displayName}]!",
                new ReligionAction(name), Notification.NotificationCategory.Religion, NotificationIcon.Faith);
        }
    }

    public bool MaySpreadReligionAtAll(MapUnitObject missionary)
    {
        if (!CivInfo.IsMajorCiv()) return false; // Only major civs
        if (!CivInfo.GameInfo.IsReligionEnabled()) return false; // No religion, no spreading

        if (missionary.Religion == null || !missionary.Civ.GameInfo.Religions.TryGetValue(missionary.Religion, out var religion)) return false;
        if (religion.IsPantheon()) return false;
        if (!UnitActionModifiers.GetUsableUnitActionUniques(missionary, UniqueType.CanSpreadReligion).Any()) return false;
        return true;
    }

    public bool MaySpreadReligionNow(MapUnitObject missionary)
    {
        if (!MaySpreadReligionAtAll(missionary)) return false;
        if (missionary.GetTile().GetOwner() == null) return false;
        if (missionary.GetTile().OwningCity?.Religion.GetMajorityReligion()?.Name == missionary.Religion)
            return false;
        if (missionary.GetTile().GetCity().NotNull().Religion.IsProtectedByInquisitor(missionary.Religion)) return false;
        return true;
    }

    public int NumberOfCitiesFollowingThisReligion()
    {
        if (Religion == null) return 0;
        return CivInfo.GameInfo.GetCities()
            .Count(it => it.Religion.GetMajorityReligion() == Religion);
    }

    public int NumberOfFollowersFollowingThisReligion(string cityFilter)
    {
        if (Religion == null) return 0;
        return KotlinMath.SumOf(
            CivInfo.GameInfo.GetCities().Where(it => it.MatchesFilter(cityFilter, CivInfo)),
            it => it.Religion.GetFollowersOf(Religion.Name));
    }

    public CityObject? GetHolyCity()
    {
        if (Religion == null) return null;
        return CivInfo.GameInfo.GetCities().FirstOrDefault(it => it.IsHolyCityOf(Religion.Name));
    }

    public Religion? GetMajorityReligion()
    {
        // let's count for each religion (among those actually presents in civ's cities)
        var religionCounter = new Counter<Religion>();
        foreach (var city in CivInfo.Cities)
        {
            // if city's majority Religion is null, let's just continue to next loop iteration
            var cityMajorityReligion = city.Religion.GetMajorityReligion();
            if (cityMajorityReligion == null) continue;
            // if not yet continued to next iteration from previous line, let's add the Religion to religionCounter
            religionCounter.Add(cityMajorityReligion, 1);
        }
        // let's get the max-counted Religion if there is one, null otherwise; if null, return null
        // (maxByOrNull: the first entry with the maximal value)
        KeyValuePair<Religion, int>? maxReligionCounterEntry = null;
        foreach (var entry in religionCounter)
            if (maxReligionCounterEntry == null || entry.Value > maxReligionCounterEntry.Value.Value)
                maxReligionCounterEntry = entry;
        if (maxReligionCounterEntry == null) return null;
        // if not returned null from prev. line, check if the maxReligion is in most of the cities
        return maxReligionCounterEntry.Value.Value > CivInfo.Cities.Count / 2
            // if maxReligionCounterEntry > half-cities-count we return the Religion of maxReligionCounterEntry
            ? maxReligionCounterEntry.Value.Key
            // if maxReligionCounterEntry <= half-cities-count we just return null
            : null;
    }
}

// Ported from the enum at the end of ReligionManager.kt. Declaration order matters: Conditionals compares states with < and >=.
public enum ReligionState
{
    None,
    Pantheon,
    FoundingReligion, // Great prophet used, but religion has not yet been founded
    Religion,
    EnhancingReligion, // Great prophet used, but religion has not yet been enhanced
    EnhancedReligion,
}

public static class ReligionStateExtensions
{
    /// <summary>
    /// Kotlin <c>ReligionState.toString()</c>: camel case converted to human readable text.
    /// Yes, this is the ugliest way to convert camel case to human readable format I've seen as well, but it works.
    /// </summary>
    public static string ToDisplayString(this ReligionState state)
    {
        var text = string.Concat(state.ToString().Select(letter => (char.IsUpper(letter) ? " " : "") + char.ToLowerInvariant(letter)));
        if (text.StartsWith(' ')) text = text[1..];
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
