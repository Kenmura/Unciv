using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models;

// Ported from core/src/com/unciv/models/Spy.kt (T057).
// - Kotlin `assert(...)` (JVM assertions are off by default) is not ported: setAction's "turns > 0 for a countdown action" and
//   incrementTechStealingProgress's "canStealTech() == Yes" preconditions are comments.
// - Kotlin `Random(seed)` is a GameRandom.
// - SpyAction is an enum with per-entry overrides in Kotlin: here an enum plus the extension members below.

public enum SpyAction
{
    None,
    Moving,
    EstablishNetwork,
    Surveillance,
    StealingTech,
    RiggingElections,
    Coup,
    CounterIntelligence,
    Dead,
}

public static class SpyActionExtensions
{
    public static string GetDisplayString(this SpyAction action) => action switch
    {
        SpyAction.None => "None",
        SpyAction.Moving => "Moving",
        SpyAction.EstablishNetwork => "Establishing Network",
        SpyAction.Surveillance => "Observing City",
        SpyAction.StealingTech => "Stealing Tech",
        SpyAction.RiggingElections => "Rigging Elections",
        SpyAction.Coup => "Coup",
        SpyAction.CounterIntelligence => "Counter-intelligence",
        SpyAction.Dead => "Dead",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public static bool HasCountdownTurns(this SpyAction action) =>
        action is SpyAction.Moving or SpyAction.EstablishNetwork or SpyAction.Dead;

    public static bool ShowTurns(this SpyAction action) =>
        action is SpyAction.Moving or SpyAction.EstablishNetwork or SpyAction.StealingTech or SpyAction.RiggingElections
            or SpyAction.Coup or SpyAction.Dead;

    internal static bool IsSetUp(this SpyAction action) =>
        action is SpyAction.Surveillance or SpyAction.StealingTech or SpyAction.RiggingElections or SpyAction.Coup
            or SpyAction.CounterIntelligence;

    internal static bool IsDoingWork(this SpyAction action, Spy spy) => action switch
    {
        SpyAction.Moving or SpyAction.EstablishNetwork or SpyAction.StealingTech or SpyAction.Coup => true,
        SpyAction.RiggingElections => !spy.CivInfo.IsAtWarWith(spy.GetCity().Civ),
        SpyAction.CounterIntelligence => spy.TurnsRemainingForAction > 0,
        _ => false,
    };
}

public class Spy : IsPartOfGameInfoSerialization
{
    private string? name;
    /// <summary>Kotlin <c>lateinit var</c> with a private setter: reading before assignment throws.</summary>
    [JsonPropertyName("name")]
    [JsonInclude]
    public string Name
    {
        get => name ?? throw new InvalidOperationException("lateinit property name has not been initialized");
        private set => name = value;
    }

    [JsonPropertyName("rank")]
    [JsonInclude]
    public int Rank { get; private set; } = 1;

    /// <summary><c>location == null</c> means that the spy is in its hideout</summary>
    [JsonPropertyName("location")]
    [JsonInclude]
    private HexCoord? location;

    [JsonPropertyName("action")]
    [JsonInclude]
    public SpyAction Action { get; private set; } = SpyAction.None;

    [JsonPropertyName("turnsRemainingForAction")]
    [JsonInclude]
    public int TurnsRemainingForAction { get; private set; }
    [JsonPropertyName("progressTowardsStealingTech")]
    [JsonInclude]
    private int progressTowardsStealingTech;

    private Civilization? civInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c> with a private setter: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        private set => civInfo = value;
    }

    private EspionageManager? espionageManager;
    private EspionageManager EspionageManager =>
        espionageManager ?? throw new InvalidOperationException("lateinit property espionageManager has not been initialized");

    private CityObject? city;

    /// <summary>Kotlin <c>private constructor()</c>, used by the deserializer.</summary>
    [JsonConstructor]
    private Spy()
    {
    }

    public Spy(string name, int rank)
    {
        Name = name;
        Rank = rank;
    }

    public Spy Clone()
    {
        var toReturn = new Spy(Name, Rank)
        {
            location = location,
            Action = Action,
            TurnsRemainingForAction = TurnsRemainingForAction,
            progressTowardsStealingTech = progressTowardsStealingTech,
        };
        return toReturn;
    }

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
        espionageManager = civInfo.EspionageManager;
    }

    public void SetAction(SpyAction newAction, int turns = 0)
    {
        // Kotlin: assert(!newAction.hasCountdownTurns || turns > 0) // hasTurns==false but turns > 0 is allowed (CounterIntelligence), hasTurns==true and turns==0 is not.
        Action = newAction;
        TurnsRemainingForAction = turns;
    }

    public void EndTurn()
    {
        if (Action.HasCountdownTurns() && --TurnsRemainingForAction > 0) return;
        switch (Action)
        {
            case SpyAction.None:
                return;
            case SpyAction.Moving:
                if (ReferenceEquals(GetCity().Civ, CivInfo))
                    // Your own cities are certainly familiar surroundings, so skip establishing a network
                    SetAction(SpyAction.CounterIntelligence, 10);
                else
                    // Should depend on cultural familiarity level if that is ever implemented inter-civ
                    SetAction(SpyAction.EstablishNetwork, 3);
                break;
            case SpyAction.EstablishNetwork:
                {
                    var currentCity = GetCity(); // This should never throw an exception, as going to the hideout sets your action to None.
                    if (currentCity.Civ.IsCityState)
                        SetAction(SpyAction.RiggingElections, (GetCity().Civ.FlagsCountdown.TryGetValue(CivFlags.TurnsTillCityStateElection.ToString(), out int turns) ? turns : 1) - 1);
                    else if (ReferenceEquals(currentCity.Civ, CivInfo))
                        SetAction(SpyAction.CounterIntelligence, 10);
                    else
                        StartStealingTech();
                    break;
                }
            case SpyAction.Surveillance:
                if (!GetCity().Civ.IsMajorCiv()) return;
                if (CanStealTech() != CanStealTechResult.Yes) return; // nope
                SetAction(SpyAction.StealingTech); // There are new techs to steal!
                break;
            case SpyAction.StealingTech:
                {
                    var canStealTechResult = CanStealTech();
                    if (canStealTechResult != CanStealTechResult.Yes)
                    {
                        SetAction(SpyAction.Surveillance);
                        if (canStealTechResult == CanStealTechResult.NoTechsToSteal)
                            AddNotification($"Your spy [{Name}] cannot steal any more techs from [{GetCity().Civ}] as we've already researched all the technology they know!");
                        return;
                    }

                    TurnsRemainingForAction = IncrementTechStealingProgress();
                    if (TurnsRemainingForAction == 0) StealTech();
                    break;
                }
            case SpyAction.RiggingElections:
                // No action done here
                // Handled in CityStateFunctions.nextTurnElections()
                // (Kotlin notes the null check can go once the old flag system is no longer supported)
                // Our spies might update before the flag is created in the city-state
                TurnsRemainingForAction = (GetCity().Civ.FlagsCountdown.TryGetValue(CivFlags.TurnsTillCityStateElection.ToString(), out int electionTurns) ? electionTurns : 0) - 1;
                break;
            case SpyAction.Coup:
                InitiateCoup();
                break;
            case SpyAction.Dead:
                {
                    string oldSpyName = Name;
                    Name = EspionageManager.GetSpyName();
                    SetAction(SpyAction.None);
                    Rank = EspionageManager.GetStartingSpyRank();
                    AddNotification($"We have recruited a new spy name [{Name}] after [{oldSpyName}] was killed.");
                    break;
                }
            case SpyAction.CounterIntelligence:
                // Counter intelligence spies don't do anything here
                // However the AI will want to keep track of how long a spy has been doing counter intelligence for
                // Once turnsRemainingForAction is <= 0 the spy won't be considered to be doing work any more
                --TurnsRemainingForAction;
                return;
        }
    }

    private void StartStealingTech()
    {
        progressTowardsStealingTech = 0;
        SetAction(SpyAction.StealingTech);
    }

    /// <summary>Kotlin nested enum <c>Spy.CanStealTech</c></summary>
    public enum CanStealTechResult
    {
        Yes,
        NoTechsToSteal,
        NoScienceInCity,
    }

    private CanStealTechResult CanStealTech()
    {
        var stealableTechs = EspionageManager.GetTechsToSteal(GetCity().Civ);
        if (stealableTechs.Count == 0) return CanStealTechResult.NoTechsToSteal;

        float cityScience = GetCity().CityStats.CurrentCityStats[Stat.Science];
        if (cityScience <= 0f) return CanStealTechResult.NoScienceInCity;

        return CanStealTechResult.Yes;
    }

    /// <returns>
    /// The number of turns left to steal the technology, note that this is a guess and may change.
    /// A 0 means that we are ready to steal the technology.
    /// </returns>
    private int IncrementTechStealingProgress()
    {
        // Kotlin: assert(canStealTech() == CanStealTech.Yes)

        float progressThisTurn = GetCity().CityStats.CurrentCityStats[Stat.Science];
        // 25% spy bonus for each level
        int rankTechStealModifier = Rank * CivInfo.GameInfo.Ruleset.ModOptions.Constants.SpyRankStealPercentBonus;
        progressThisTurn *= (rankTechStealModifier + 75f) / 100f;
        progressThisTurn *= (float)GetEfficiencyModifier();
        progressTowardsStealingTech += KotlinMath.FloatToInt(progressThisTurn);

        var stealableTechs = EspionageManager.GetTechsToSteal(GetCity().Civ);
        float techStealCost = (float)stealableTechs.Max(it => CivInfo.GameInfo.Ruleset.Technologies[it].Cost);
        float techStealCostModifier = CivInfo.GameInfo.Ruleset.ModOptions.Constants.SpyTechStealCostModifier; // 1.25f in Civ5 GlobalDefines.XML
        float techSpeedModifier = CivInfo.GameInfo.Speed.ScienceCostModifier; // Modify steal cost according to game speed
        techStealCost *= techStealCostModifier * techSpeedModifier;

        float remainingCost = techStealCost - progressTowardsStealingTech;

        if (remainingCost <= 0) return 0;
        return KotlinMath.FloatToInt(MathF.Ceiling(remainingCost / progressThisTurn));
    }

    /// <summary>
    /// With the defult spy leveling:
    /// 100 units change one step in results, there are 4 such steps, and the default random spans 300 units and excludes the best result (undetected success).
    /// Thus the return value translates into (return / 3) percent chance to get the very best result, reducing the chance to get the worst result (kill) by the same amount.
    /// The same modifier from defending counter-intelligence spies goes linearly in the opposite direction.
    /// With the range of this function being hardcoded to 30..90 (and 0 for no defensive spy present), ranks cannot guarantee either best or worst outcome.
    /// Or - chance range of best result is 0% (rank 1 vs rank 3 defender) to 30% (rank 3 vs no defender), range of worst is 53% to 3%, respectively.
    /// </summary>
    private void StealTech()
    {
        var stealCity = GetCity();
        var otherCiv = stealCity.Civ;
        int randomSeedValue = RandomSeed();

        // Could be improved to for example steal the most expensive tech or the tech that has the least progress as of yet
        var stolenTech = EspionageManager.GetTechsToSteal(GetCity().Civ)
            .RandomOrNull(CivInfo.GameInfo.SeededRandom(randomSeedValue));

        // Lower is better
        int spyResult = CivInfo.GameInfo.SeededRandom(randomSeedValue).NextInt(300);
        // Add our spies experience
        spyResult -= GetSkillModifierPercent();
        // Subtract the experience of the counter intelligence spies
        var defendingSpy = stealCity.Civ.EspionageManager.GetSpyAssignedToCity(stealCity);
        spyResult += defendingSpy?.GetSkillModifierPercent() ?? 0;

        string? detectionString;
        if (spyResult >= 200)
        {
            // The spy was killed in the attempt (should be able to happen even if there's nothing to steal?)
            detectionString = defendingSpy == null
                ? $"A spy from [{CivInfo.CivName}] was found and killed trying to steal Technology in [{stealCity.Name}]!"
                : $"A spy from [{CivInfo.CivName}] was found and killed by [{defendingSpy.Name}] trying to steal Technology in [{stealCity.Name}]!";
        }
        else if (stolenTech == null) detectionString = null; // Nothing to steal
        else if (spyResult < 0) detectionString = null; // Not detected
        else if (spyResult < 100) detectionString = $"An unidentified spy stole the Technology [{stolenTech}] from [{stealCity.Name}]!";
        else detectionString = $"A spy from [{CivInfo.CivName}] stole the Technology [{stolenTech}] from [{stealCity.Name}]!";
        if (detectionString != null)
            // Not using Spy.addNotification, shouldn't open the espionage screen
            otherCiv.AddNotification(detectionString, stealCity.Location, Notification.NotificationCategory.Espionage, NotificationIcon.Spy);
        if (spyResult < 200 && stolenTech != null)
        {
            CivInfo.Tech.AddTechnology(stolenTech);
            AddNotification($"Your spy [{Name}] stole the Technology [{stolenTech}] from [{stealCity.Name}]!");
            LevelUpSpy();
        }

        if (spyResult >= 200)
        {
            AddNotification($"Your spy [{Name}] was killed trying to steal Technology in [{stealCity.Name}]!");
            defendingSpy?.LevelUpSpy();
            KillSpy();
            // if they kill your spy they should know that you are spying on them and able to demande to not be speid on.
            DemandToNotBeSpiedOn(otherCiv);
        }
        else StartStealingTech(); // reset progress

        if (spyResult >= 100) DemandToNotBeSpiedOn(otherCiv);
    }

    private void DemandToNotBeSpiedOn(Civilization otherCiv)
    {
        var otherCivDiplomacyManager = otherCiv.GetDiplomacyManager(CivInfo).NotNull();
        otherCivDiplomacyManager.AddModifier(DiplomaticModifiers.SpiedOnUs, -15f);
        otherCivDiplomacyManager.SetFlag(DiplomacyFlags.DiscoveredSpiesInOurCities, 30, true);
    }

    public bool CanDoCoup() => GetCityOrNull() != null && GetCity().Civ.IsCityState && IsSetUp()
        && !ReferenceEquals(GetCity().Civ.AllyCiv, CivInfo);

    /// <summary>
    /// Initiates a coup if this spies civ is not the ally of the city-state.
    /// The coup will only happen at the end of the Civ's turn for save scum reasons, so a play may not reload in multiplayer.
    /// If successfull the coup will
    /// </summary>
    private void InitiateCoup()
    {
        if (!CanDoCoup())
        {
            // Maybe we are the new ally of the city-state
            // However we know that we are still in the city and it hasn't been conquered
            SetAction(SpyAction.RiggingElections, 10);
            return;
        }
        var cityState = GetCity().Civ;
        var allyCiv = cityState.AllyCiv;

        float successChance = GetCoupChanceOfSuccess(true);
        float randomValue = CivInfo.GameInfo.SeededRandom(RandomSeed()).NextFloat();
        if (randomValue <= successChance)
        {
            // Success
            float previousInfluence = allyCiv != null ? cityState.GetDiplomacyManager(allyCiv).NotNull().GetInfluence() : 80f;
            cityState.GetDiplomacyManager(CivInfo).NotNull().SetInfluence(previousInfluence);

            CivInfo.AddNotification($"Your spy [{Name}] successfully staged a coup in [{cityState.CivName}]!", GetCity().Location,
                Notification.NotificationCategory.Espionage, NotificationIcon.Spy, cityState.CivName);
            if (allyCiv != null)
            {
                cityState.GetDiplomacyManagerOrMeet(allyCiv).ReduceInfluence(20f);
                allyCiv.AddNotification($"A spy from [{CivInfo.CivName}] successfully staged a coup in our former ally [{cityState.CivName}]!", GetCity().Location,
                    Notification.NotificationCategory.Espionage, CivInfo.CivName, NotificationIcon.Spy, cityState.CivName);
                allyCiv.GetDiplomacyManagerOrMeet(CivInfo).AddModifier(DiplomaticModifiers.SpiedOnUs, -15f);
            }
            foreach (var civ in cityState.GetKnownCivsWithSpectators())
            {
                if (ReferenceEquals(civ, allyCiv) || ReferenceEquals(civ, CivInfo)) continue;
                civ.AddNotification($"A spy from [{CivInfo.CivName}] successfully staged a coup in [{cityState.CivName}]!", GetCity().Location,
                    Notification.NotificationCategory.Espionage, CivInfo.CivName, NotificationIcon.Spy, cityState.CivName);
                if (civ.IsSpectator()) continue;
                cityState.GetDiplomacyManager(civ).NotNull().ReduceInfluence(10f); // Guess
            }
            SetAction(SpyAction.RiggingElections, 10);
            cityState.CityStateFunctions.UpdateAllyCivForCityState();
        }
        else
        {
            // Failure
            var spy = allyCiv?.EspionageManager.GetSpyAssignedToCity(GetCity());
            cityState.GetDiplomacyManager(CivInfo).NotNull().AddInfluence(-20f);
            allyCiv?.AddNotification($"A spy from [{CivInfo.CivName}] failed to stage a coup in our ally [{cityState.CivName}] and was killed!", GetCity().Location,
                Notification.NotificationCategory.Espionage, CivInfo.CivName, NotificationIcon.Spy, cityState.CivName);
            allyCiv?.GetDiplomacyManagerOrMeet(CivInfo).AddModifier(DiplomaticModifiers.SpiedOnUs, -10f);

            CivInfo.AddNotification($"Our spy [{Name}] failed to stage a coup in [{cityState.CivName}] and was killed!", GetCity().Location,
                Notification.NotificationCategory.Espionage, CivInfo.CivName, NotificationIcon.Spy, cityState.CivName);

            KillSpy();
            spy?.LevelUpSpy(); // Technically not in Civ V, but it's like the same thing as with counter-intelligence
        }
    }

    /// <summary>Calculates the success chance of a coup in this city state.</summary>
    public float GetCoupChanceOfSuccess(bool includeunknownFactors)
    {
        var cityState = GetCity().Civ;
        float successPercentage = 50f;

        // Influence difference should always be a positive value
        float influenceDifference = cityState.AllyCiv != null
            ? cityState.GetDiplomacyManager(cityState.AllyCiv).NotNull().GetInfluence()
            : 60f;
        influenceDifference -= cityState.GetDiplomacyManager(CivInfo).NotNull().GetInfluence();
        successPercentage -= influenceDifference / 2f;

        // If we are viewing the success chance we don't want to reveal that there is a defending spy
        var defendingSpy = includeunknownFactors
            ? cityState.AllyCiv?.EspionageManager.GetSpyAssignedToCity(GetCity())
            : null;

        int spyRanks = GetSkillModifierPercent() - (defendingSpy?.GetSkillModifierPercent() ?? 0);
        successPercentage += spyRanks / 2f; // Each rank counts for 15%

        successPercentage = Math.Clamp(successPercentage, 0f, 85f);
        return successPercentage / 100f;
    }

    public void MoveTo(CityObject? newCity)
    {
        if (newCity == null)
        {
            // Moving to spy hideout
            location = null;
            city = null;
            SetAction(SpyAction.None);
            return;
        }
        location = newCity.Location;
        city = newCity;
        SetAction(SpyAction.Moving, 1);
    }

    private bool CanDismissAgreementToNotSendSpies(CityObject targetCity)
    {
        var otherCivDiplomacyManager = targetCity.Civ.GetDiplomacyManager(CivInfo);
        if (otherCivDiplomacyManager == null) return false;
        var otherCiv = otherCivDiplomacyManager.CivInfo;

        float techSize = CivInfo.GameInfo.Ruleset.Technologies.Values.Count();
        float ourResearchTechSize = CivInfo.Tech.TechsResearched.Count;
        float otherCivResearchTechSize = otherCiv.Tech.TechsResearched.Count;

        float ourTechResearchLevel = ourResearchTechSize / techSize * 100f;
        float otherCivTechResearchLevel = otherCivResearchTechSize / techSize * 100f;
        return otherCivDiplomacyManager.HasFlag(DiplomacyFlags.AgreedToNotSendSpies) &&
               /*
               * if there is equal or more than 10% difference in tech,
               * compare to the other civ in tech we the Ai will bypass the agreement
               * */
               otherCivTechResearchLevel <= ourTechResearchLevel + 10f;
    }

    public bool CanMoveTo(CityObject targetCity)
    {
        if (CanDismissAgreementToNotSendSpies(targetCity)) return false;
        if (GetCityOrNull() == targetCity) return true;
        if (!targetCity.GetCenterTile().IsExplored(CivInfo)) return false;
        return EspionageManager.GetSpyAssignedToCity(targetCity) == null;
    }

    public bool IsSetUp() => Action.IsSetUp();

    public bool IsIdle() => Action == SpyAction.None;

    public bool IsDoingWork() => Action.IsDoingWork(this);

    /// <summary>Returns the City this Spy is in, or <c>null</c> if it is in the hideout.</summary>
    public CityObject? GetCityOrNull()
    {
        if (location == null) return null;
        city ??= CivInfo.GameInfo.TileMap[location.Value].GetCity();
        return city;
    }

    /// <summary>
    /// Non-null version of <see cref="GetCityOrNull"/> for the frequent case it is known the spy cannot be in the hideout.
    /// </summary>
    /// <exception cref="NullReferenceException">if the spy is in the hideout</exception>
    public CityObject GetCity() => GetCityOrNull() ?? throw new NullReferenceException("The spy is in the hideout");

    public string GetLocationName() => GetCityOrNull()?.Name ?? Constants.SpyHideout;

    public void LevelUpSpy(int amount = 1)
    {
        if (Rank >= CivInfo.GameInfo.Ruleset.ModOptions.Constants.MaxSpyRank) return;
        int ranksToLevelUp = Math.Min(amount, CivInfo.GameInfo.Ruleset.ModOptions.Constants.MaxSpyRank - Rank);

        if (ranksToLevelUp == 1) AddNotification($"Your spy [{Name}] has leveled up!");
        else AddNotification($"Your spy [{Name}] has leveled up [{ranksToLevelUp.ToString(CultureInfo.InvariantCulture)}] times!");
        Rank += ranksToLevelUp;
    }

    /// <summary>
    /// Rank used for skill checks, including temporary bonuses from buildings for the current spy action.
    /// Never exceeds <see cref="ModConstants.MaxSpyRank"/>. Does not change the persisted <see cref="Rank"/>.
    /// </summary>
    public int GetEffectiveRank()
    {
        int maxRank = CivInfo.GameInfo.Ruleset.ModOptions.Constants.MaxSpyRank;
        int effective = Rank;
        var currentCity = GetCityOrNull();
        if (currentCity == null) return KotlinCoerceIn(effective, 1, maxRank);
        currentCity.ForEachMatchingUnique(UniqueType.CounterIntelligenceSpyRankBonus, currentCity.State, true, unique =>
        {
            if (currentCity.MatchesFilter(unique.Params[0], CivInfo) &&
                string.Equals(unique.Params[2], Action.GetDisplayString(), StringComparison.OrdinalIgnoreCase))
                effective += KotlinMath.ParseInt(unique.Params[1]);
        });
        return KotlinCoerceIn(effective, 1, maxRank);
    }

    // Kotlin coerceIn throws when the range is empty (max < min); Math.Clamp does too
    private static int KotlinCoerceIn(int value, int min, int max) => Math.Clamp(value, min, max);

    /// <summary>Modifier of the skill bonus of the spy by percent</summary>
    public int GetSkillModifierPercent() =>
        GetEffectiveRank() * CivInfo.GameInfo.Ruleset.ModOptions.Constants.SpyRankSkillPercentBonus;

    /// <summary>Gets a friendly and enemy efficiency uniques for the spy at the location</summary>
    /// <returns>a value centered around 1.0 for the work efficiency of the spy, won't be negative</returns>
    public double GetEfficiencyModifier()
    {
        IEnumerable<UniqueObject> friendlyUniques;
        IEnumerable<UniqueObject> enemyUniques;
        var currentCity = GetCityOrNull();
        if (currentCity == null)
        {
            // Spy is in hideout - effectiveness won't matter
            friendlyUniques = CivInfo.GetMatchingUniques(UniqueType.SpyEffectiveness);
            enemyUniques = [];
        }
        else if (ReferenceEquals(currentCity.Civ, CivInfo))
        {
            // Spy is in our own city
            friendlyUniques = currentCity.GetMatchingUniques(UniqueType.SpyEffectiveness, currentCity.State, true);
            enemyUniques = [];
        }
        else
        {
            // Spy is active in a foreign city
            friendlyUniques = CivInfo.GetMatchingUniques(UniqueType.SpyEffectiveness);
            enemyUniques = currentCity.GetMatchingUniques(UniqueType.EnemySpyEffectiveness, currentCity.State, true);
        }
        double totalEfficiency = 1.0;
        totalEfficiency *= (100.0 + KotlinMath.SumOf(friendlyUniques
            .Where(it => currentCity == null || currentCity.MatchesFilter(it.Params[1], CivInfo)),
            it => KotlinMath.ParseInt(it.Params[0]))) / 100;
        totalEfficiency *= (100.0 + KotlinMath.SumOf(enemyUniques
            .Where(it => currentCity != null && currentCity.MatchesFilter(it.Params[1], null)),
            it => KotlinMath.ParseInt(it.Params[0]))) / 100;
        return Math.Max(totalEfficiency, 0.0);
    }

    private void KillSpy()
    {
        // We don't actually remove this spy object, we set them as dead and let them revive
        MoveTo(null);
        SetAction(SpyAction.Dead, 5);
        Rank = 1;
    }

    public bool IsAlive() => Action != SpyAction.Dead;

    /// <summary>Shorthand for <c>Civilization.AddNotification</c> specialized for espionage - action, category and icon are always the same</summary>
    public void AddNotification(string text) =>
        CivInfo.AddNotification(text, EspionageAction.WithLocation(location), Notification.NotificationCategory.Espionage, NotificationIcon.Spy);

    /// <summary>
    /// Anti-save-scum: Deterministic random from city and turn
    /// </summary>
    /// <exception cref="NullReferenceException">for spies in the hideout</exception>
    private int RandomSeed()
    {
        var seedCity = GetCity();
        return KotlinMath.FloatToInt(seedCity.Location.x * seedCity.Location.y + 123f * CivInfo.GameInfo.Turns) + JavaHash.HashCode(Name);
    }
}
