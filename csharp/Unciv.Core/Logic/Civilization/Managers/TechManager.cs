using System.Globalization;
using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.UI.Components.Extensions;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using EraObject = Unciv.Models.Ruleset.Tech.Era;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/TechManager.kt (T056).
// Stripped: the purity annotations.
// Deviations:
// - Kotlin has both `var allTechsAreResearched` and `fun allTechsAreResearched()`, and both `var scienceFromResearchAgreements` and the
//   private `fun scienceFromResearchAgreements()`; C# cannot overload a member name by kind. The flag is the private field
//   allTechsAreResearched read by AllTechsAreResearched(); the private function is GetScienceFromResearchAgreements().
// - Kotlin HashSet/HashMap fields keep java.util iteration order (JavaHashSet/JavaHashMap).
// - Fonts.infinity (a UI class) is inlined as the constant InfinityChar.

public class TechManager : IsPartOfGameInfoSerialization
{
    /// <summary>Fonts.infinity, inlined because Fonts is a UI class.</summary>
    private const char InfinityChar = '∞';

    [JsonIgnore]
    public EraObject Era { get; set; } = new();

    private Civilization? civInfo;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <see cref="SetTransients"/>: reading before assignment throws.</summary>
    [JsonIgnore]
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    /// <summary>This is the Transient list of Technologies</summary>
    [JsonIgnore]
    public List<Technology> ResearchedTechnologies { get; set; } = [];

    [JsonIgnore]
    internal UniqueMap TechUniques { get; set; } = new();

    // MapUnit.canPassThrough is the most called function in the game, and having these extremely specific booleans is one way of improving the time cost
    [JsonIgnore]
    public bool UnitsCanEmbark { get; set; }
    [JsonIgnore]
    public bool EmbarkedUnitsCanEnterOcean { get; set; }
    [JsonIgnore]
    public bool AllUnitsCanEnterOcean { get; set; }
    [JsonIgnore]
    public bool SpecificUnitsCanEnterOcean { get; set; }

    // UnitMovementAlgorithms.getMovementCostBetweenAdjacentTiles is a close second =)
    [JsonIgnore]
    public float MovementSpeedOnRoads { get; set; } = 1f;
    [JsonIgnore]
    public bool RoadsConnectAcrossRivers { get; set; }
    private bool allTechsAreResearched;

    [JsonPropertyName("freeTechs")]
    public int FreeTechs { get; set; }
    // For calculating score
    [JsonPropertyName("repeatingTechsResearched")]
    public int RepeatingTechsResearched { get; set; }

    /// <summary>For calculating Great Scientist yields - see https://civilization.fandom.com/wiki/Great_Scientist_(Civ5)</summary>
    [JsonPropertyName("scienceOfLast8Turns")]
    public int[] ScienceOfLast8Turns { get; set; } = new int[8];
    [JsonPropertyName("scienceFromResearchAgreements")]
    public int ScienceFromResearchAgreements { get; set; }
    /// <summary>This is the list of strings, which is serialized</summary>
    [JsonPropertyName("techsResearched")]
    public JavaHashSet<string> TechsResearched { get; set; } = new();

    /// <summary>When moving towards a certain tech, the user doesn't have to manually pick every one.</summary>
    [JsonPropertyName("techsToResearch")]
    public List<string> TechsToResearch { get; set; } = [];
    [JsonPropertyName("overflowScience")]
    [JsonInclude]
    private int overflowScience;
    [JsonPropertyName("techsInProgress")]
    public JavaHashMap<string, int> TechsInProgress { get; set; } = new();

    /// <summary>In civ IV, you can auto-convert a certain percentage of gold in cities to science</summary>
    [JsonPropertyName("goldPercentConvertedToScience")]
    public float GoldPercentConvertedToScience { get; set; } = 0.6f;

    #region state-changing functions
    public TechManager Clone()
    {
        var toReturn = new TechManager();
        foreach (var techName in TechsResearched) toReturn.TechsResearched.Add(techName);
        toReturn.FreeTechs = FreeTechs;
        toReturn.RepeatingTechsResearched = RepeatingTechsResearched;
        toReturn.TechsInProgress.PutAll(TechsInProgress);
        toReturn.TechsToResearch.AddRange(TechsToResearch);
        toReturn.ScienceOfLast8Turns = (int[])ScienceOfLast8Turns.Clone();
        toReturn.ScienceFromResearchAgreements = ScienceFromResearchAgreements;
        toReturn.overflowScience = overflowScience;
        toReturn.GoldPercentConvertedToScience = GoldPercentConvertedToScience;
        return toReturn;
    }

    /// <summary>Get total number of techs researched, including repeated future tech</summary>
    public int GetNumberOfTechsResearched() => TechsResearched.Count + RepeatingTechsResearched;

    public int GetOverflowScience() => overflowScience;

    private float GetScienceModifier(string techName) // https://forums.civfanatics.com/threads/the-mechanics-of-overflow-inflation.517970/
    {
        var numberOfCivsResearchedThisTech = CivInfo.GetKnownCivs()
            .Count(it => it.IsMajorCiv() && it.Tech.IsResearched(techName));
        var numberOfCivsRemaining = CivInfo.GameInfo.Civilizations
            .Count(it => it.IsMajorCiv() && !it.IsDefeated());
        return 1 + numberOfCivsResearchedThisTech / (float)numberOfCivsRemaining * 0.3f;
    }

    private Ruleset GetRuleset() => CivInfo.GameInfo.Ruleset;

    public int CostOfTech(string techName)
    {
        var techCost = (float)GetRuleset().Technologies[techName].Cost;
        if (CivInfo.IsHuman())
            techCost *= CivInfo.GetDifficulty().ResearchCostModifier;
        techCost *= CivInfo.GameInfo.Speed.ScienceCostModifier;
        techCost /= GetScienceModifier(techName);
        var mapSizePredef = CivInfo.GameInfo.TileMap.MapParameters.MapSize.GetPredefinedOrNextSmaller();
        techCost *= mapSizePredef.GetTechCostMultiplier();
        var cityModifier = (CivInfo.Cities.Count(it => !it.IsPuppet) - 1) * mapSizePredef.GetTechCostPerCityModifier();
        CivInfo.ForEachMatchingUnique(UniqueType.LessTechCostFromCities, unique => cityModifier *= 1 - ParseFloat(unique.Params[0]) / 100);
        CivInfo.ForEachMatchingUnique(UniqueType.LessTechCost, unique => techCost *= unique.Params[0].ToPercent());
        techCost *= 1 + cityModifier;
        return KotlinMath.FloatToInt(techCost);
    }

    private static float ParseFloat(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    public Technology? CurrentTechnology()
    {
        var currentTechnologyName = CurrentTechnologyName();
        if (currentTechnologyName == null) return null;
        return GetRuleset().Technologies.GetValueOrDefault(currentTechnologyName);
    }

    public string? CurrentTechnologyName() => TechsToResearch.Count == 0 ? null : TechsToResearch[0];

    public int ResearchOfTech(string? techName) =>
        techName != null && TechsInProgress.TryGetValue(techName, out var research) ? research : 0;
    // Was once duplicated as fun scienceSpentOnTech(tech: String): Int

    public int RemainingScienceToTech(string techName)
    {
        var spareScience = CanBeResearched(techName) ? GetOverflowScience() : 0;
        return CostOfTech(techName) - ResearchOfTech(techName) - spareScience;
    }

    public string TurnsToTech(string techName)
    {
        var remainingCost = (double)RemainingScienceToTech(techName);
        if (remainingCost <= 0f) return 0.Tr();
        if (CivInfo.Stats.StatsForNextTurn.Science <= 0f) return InfinityChar.ToString();
        return Math.Max(
            1,
            KotlinMath.DoubleToInt(Math.Ceiling(remainingCost / CivInfo.Stats.StatsForNextTurn.Science))
        ).Tr();
    }

    public bool IsResearched(string techName) => TechsResearched.Contains(techName);
    public bool IsResearched(INonPerpetualConstruction construction) => construction.RequiredTechs().All(requiredTech => IsResearched(requiredTech));

    /// <summary>resources which need no research count as researched</summary>
    public bool IsRevealed(TileResource resource)
    {
        var revealedBy = resource.RevealedBy;
        if (revealedBy == null) return true;
        // B010: a resource may also be revealed by a policy (Hidden Antiquity Site, Exploration Complete)
        if (!GetRuleset().Technologies.ContainsKey(revealedBy) && GetRuleset().Policies.ContainsKey(revealedBy))
            return CivInfo.Policies.IsAdopted(revealedBy);
        return IsResearched(revealedBy);
    }

    public bool IsObsolete(BaseUnit unit) => unit.TechsThatObsoleteThis().Any(obsoleteTech => IsResearched(obsoleteTech));

    public bool IsUnresearchable(Technology tech)
    {
        if (((IHasUniques)tech).GetMatchingUniques(UniqueType.OnlyAvailable, GameContext.IgnoreConditionalsState).Any(it => !it.ConditionalsApply(CivInfo.State)))
            return true;
        if (((IHasUniques)tech).HasUnique(UniqueType.Unavailable, CivInfo.State)) return true;
        return false;
    }

    public bool CanBeResearched(string techName)
    {
        var tech = GetRuleset().Technologies[techName];

        if (IsUnresearchable(tech)) return false;
        if (IsResearched(tech.Name) && !tech.IsContinuallyResearchable()) return false;

        return tech.Prerequisites.All(it => IsResearched(it));
    }

    public bool AllTechsAreResearched() => allTechsAreResearched;

    #endregion

    /// <summary>Returns empty list if no path exists</summary>
    public List<Technology> GetRequiredTechsToDestination(Technology destinationTech)
    {
        var prerequisites = new List<Technology>();

        var checkPrerequisites = new Queue<Technology>();
        if (IsUnresearchable(destinationTech)) return [];
        checkPrerequisites.Enqueue(destinationTech);

        while (checkPrerequisites.Count != 0)
        {
            var techToCheck = checkPrerequisites.Dequeue();
            if (IsUnresearchable(techToCheck)) return [];
            // future tech can have been researched even when we're researching it,
            // so...if we skip it we'll end up with 0 techs in the "required techs", which will mean that we don't have anything to research. Yeah.
            if (!techToCheck.IsContinuallyResearchable() &&
                    (IsResearched(techToCheck.Name) || prerequisites.Contains(techToCheck)))
                continue; //no need to add or check prerequisites
            foreach (var prerequisite in techToCheck.Prerequisites)
                checkPrerequisites.Enqueue(GetRuleset().Technologies[prerequisite]);
            prerequisites.Add(techToCheck);
        }

        return prerequisites.OrderBy(it => it.Column.NotNull().ColumnNumber).ToList();
    }

    public int GetScienceFromGreatScientist()
    {
        // https://civilization.fandom.com/wiki/Great_Scientist_(Civ5)
        var sum = 0;
        foreach (var science in ScienceOfLast8Turns) sum += science;
        return KotlinMath.FloatToInt(sum * CivInfo.GameInfo.Speed.ScienceCostModifier);
    }

    private void AddCurrentScienceToScienceOfLast8Turns(int science)
    {
        ScienceOfLast8Turns[CivInfo.GameInfo.Turns % 8] = science;
    }

    private int LimitOverflowScience(int overflowScience)
    {
        // http://www.civclub.net/bbs/forum.php?mod=viewthread&tid=123976
        // Apparently yes, we care about the absolute tech cost, not the actual calculated-for-this-player tech cost,
        //  so don't change to costOfTech()
        return Math.Min(overflowScience, Math.Max(KotlinMath.FloatToInt(CivInfo.Stats.StatsForNextTurn.Science) * 5,
                GetRuleset().Technologies[CurrentTechnologyName().NotNull()].Cost));
    }

    private int GetScienceFromResearchAgreements()
    {
        // https://forums.civfanatics.com/resources/research-agreements-bnw.25568/
        var researchAgreementModifier = 0.5f;
        CivInfo.ForEachMatchingUnique(UniqueType.ScienceFromResearchAgreements, unique =>
        {
            researchAgreementModifier += ParseFloat(unique.Params[0]) / 200f;
        });
        return KotlinMath.FloatToInt(ScienceFromResearchAgreements / 3 * researchAgreementModifier);
    }

    public void EndTurn(int scienceForNewTurn)
    {
        AddCurrentScienceToScienceOfLast8Turns(scienceForNewTurn);
        if (CurrentTechnologyName() == null) return;

        var finalScienceToAdd = scienceForNewTurn;

        if (ScienceFromResearchAgreements != 0)
        {
            var scienceBoost = GetScienceFromResearchAgreements();
            finalScienceToAdd += scienceBoost;
            ScienceFromResearchAgreements = 0;
            CivInfo.AddNotification($"We gained [{scienceBoost.ToString(CultureInfo.InvariantCulture)}] Science from Research Agreement",
                NotificationCategory.General,
                NotificationIcon.Science);
        }
        if (overflowScience != 0)
        {
            finalScienceToAdd += GetOverflowScience();
            overflowScience = 0;
        }

        AddScience(finalScienceToAdd);
    }

    /// <summary>
    /// Adds science "income" e.g. from normal research (see <see cref="EndTurn"/>) or a triggered Unique.
    ///
    /// Also called from <see cref="UpdateResearchProgress"/> to _apply_ overflow.
    /// Adds the points to the <see cref="CurrentTechnology">current research</see>, or, if there is none, to overflow.
    /// </summary>
    public void AddScience(int scienceGet)
    {
        var currentTechnology = CurrentTechnologyName();
        if (currentTechnology == null)
        {
            overflowScience += scienceGet;
            return;
        }
        TechsInProgress[currentTechnology] = ResearchOfTech(currentTechnology) + scienceGet;
        if (TechsInProgress[currentTechnology] < CostOfTech(currentTechnology))
            return;

        // We finished it!
        // http://www.civclub.net/bbs/forum.php?mod=viewthread&tid=123976
        var extraScienceLeftOver = TechsInProgress[currentTechnology] - CostOfTech(currentTechnology);
        overflowScience += LimitOverflowScience(extraScienceLeftOver);
        AddTechnology(currentTechnology);
    }

    /// <summary>
    /// Checks whether the research on the current technology can be completed
    /// and, if so, completes the research.
    /// </summary>
    public void UpdateResearchProgress()
    {
        var currentTechnology = CurrentTechnologyName();
        if (currentTechnology == null) return;
        var realOverflow = GetOverflowScience();
        var scienceSpent = ResearchOfTech(currentTechnology) + realOverflow;
        if (scienceSpent >= CostOfTech(currentTechnology))
        {
            overflowScience = 0;
            AddScience(realOverflow);
        }
    }

    public void GetFreeTechnology(string techName)
    {
        FreeTechs--;
        AddTechnology(techName);
    }

    public void AddTechnology(string techName, bool showNotification = true)
    {
        var isNewTech = TechsResearched.Add(techName);

        // this is to avoid concurrent modification problems
        var newTech = GetRuleset().Technologies[techName];
        if (!newTech.IsContinuallyResearchable())
            TechsToResearch.Remove(techName);
        else
            RepeatingTechsResearched++;
        TechsInProgress.Remove(techName);
        ResearchedTechnologies = [.. ResearchedTechnologies, newTech];
        AddTechToTransients(newTech);

        MoveToNewEra(showNotification);
        if (isNewTech) Archaeology.ArchaeologyRules.OnTechResearched(CivInfo, newTech); // B010: the first Archaeology creates the sites

        if (!CivInfo.IsSpectator() && showNotification)
            CivInfo.AddNotification($"Research of [{techName}] has completed!", new TechAction(techName),
                NotificationCategory.General,
                NotificationIcon.Science);
        if (isNewTech)
        {
            CivInfo.AddPopupAlert(new PopupAlert(AlertType.TechResearched, techName));
            GameEventHooks.ResearchCompleted(CivInfo, techName);
        }

        var triggerNotificationText = $"due to researching [{techName}]";
        foreach (var unique in newTech.UniqueObjects)
        {
            if (!unique.IsTriggerable || unique.HasTriggerConditional() || !unique.ConditionalsApply(CivInfo.State))
                continue;
            var multiplier = unique.GetUniqueMultiplier(CivInfo.State); // evaluated once, like repeat()
            for (var i = 0; i < multiplier; i++)
                UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: triggerNotificationText);
        }

        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponResearch, CivInfo.State, it => newTech.MatchesFilter(it.Params[0], CivInfo.State), unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: triggerNotificationText);
        });

        var revealedResources = GetRuleset().TileResources.Values.Where(it => techName == it.RevealedBy).ToList();
        if (CivInfo.PlayerType == PlayerType.Human)
        {
            foreach (var revealedResource in revealedResources)
            {
                CivInfo.GameInfo.NotifyExploredResources(CivInfo, revealedResource, 5);
            }
        }

        UpdateTransientBooleans();

        // In the case of a player hurrying research, this civ's resource availability may now be out of date
        // - e.g. when an owned tile by luck already has an appropriate improvement or when a tech provides a resource.
        // That can be seen on WorldScreenTopBar, so better update.
        CivInfo.Cache.UpdateCivResources();

        foreach (var city in CivInfo.Cities)
        {
            city.ReassignPopulationDeferred();
        }

        ObsoleteOldUnits(techName);

        CivInfo.ForEachMatchingUnique(UniqueType.MayanGainGreatPerson, unique =>
        {
            if (unique.Params[1] == techName)
                CivInfo.AddNotification("You have unlocked [The Long Count]!",
                    new MayaLongCountAction(), NotificationCategory.General, MayaCalendar.NotificationIcon);
        });

        UpdateResearchProgress();
    }

    /// <summary>A variant of kotlin's associateBy that omits null values</summary>
    private static LinkedHashMap<K, V> AssociateByNotNull<T, K, V>(IEnumerable<T> source, Func<T, K> keySelector, Func<T, V?> valueTransform)
        where K : notnull where V : class
    {
        var destination = new LinkedHashMap<K, V>();
        foreach (var element in source)
        {
            var value = valueTransform(element);
            if (value == null) continue;
            destination.Put(keySelector(element), value);
        }
        return destination;
    }

    private void ObsoleteOldUnits(string techName)
    {
        // First build a map with obsoleted units to their (nation-specific) upgrade
        BaseUnit? GetEquivalentUpgradeOrNull(BaseUnit unit)
        {
            var unitUpgradesTo = unit.AutomaticallyUpgradedInProductionToUnitByTech(techName);
            if (unitUpgradesTo == null) return null;
            return CivInfo.GetEquivalentUnit(unitUpgradesTo);
        }
        var obsoleteUnits = AssociateByNotNull(GetRuleset().Units, it => it.Key, it => GetEquivalentUpgradeOrNull(it.Value));
        if (obsoleteUnits.Count == 0) return;

        // Apply each to all cities - and remember which cities had which obsoleted unit
        //  in their construction queues in this Map<String, MutableSet<City>>:
        var unitUpgrades = new LinkedHashMap<string, LinkedHashSet<CityObject>>();
        foreach (var key in obsoleteUnits.Keys) unitUpgrades.Put(key, new LinkedHashSet<CityObject>());
        string? TransformConstruction(string old, CityObject city)
        {
            if (!unitUpgrades.TryGetValue(old, out var entry)) return old;  // Entry OK, not obsolete
            entry.Add(city);  // Remember city has updated its queue
            return obsoleteUnits.TryGetValue(old, out var replacement) ? replacement.Name : null;  // Replacement, or pass through null to remove from queue
        }
        foreach (var city in CivInfo.Cities)
        {
            city.CityConstructions.EditQueue(queue =>
            {
                var transformed = queue.Select(it => TransformConstruction(it, city)).OfType<string>().ToList();
                queue.Clear();
                queue.AddRange(transformed);
            });
        }

        // Add notifications for obsolete units/constructions
        foreach (var (unit, cities) in unitUpgrades)
        {
            if (cities.Count == 0) continue;

            //The validation check happens again while processing start and end of turn,
            //but for mid-turn free tech picks like Oxford University, it should happen immediately
            //so the hammers from the obsolete unit are guaranteed to go to the upgraded unit
            //and players don't think they lost all their production mid turn
            foreach (var city in cities)
                city.CityConstructions.ValidateInProgressConstructions();

            var locationAction = LocationAction.Of(cities.Select(it => it.Location));
            var cityText = cities.Count == 1 ? $"[{cities.First().Name}]"
                : $"[{cities.Count.ToString(CultureInfo.InvariantCulture)}] cities";
            var newUnit = obsoleteUnits.TryGetValue(unit, out var upgraded) ? upgraded.Name : null;
            var text = newUnit == null
                ? $"[{unit}] has become obsolete and was removed from the queue in {cityText}!"
                : $"{cityText} changed production from [{unit}] to [{newUnit}]";
            var icons = newUnit == null
                ? new[] { NotificationIcon.Construction }
                : new[] { unit, NotificationIcon.Construction, newUnit };
            CivInfo.AddNotification(text, locationAction, NotificationCategory.Production, icons);
        }
    }

    private void MoveToNewEra(bool showNotification = true)
    {
        var previousEra = CivInfo.GetEra();
        UpdateEra();
        var currentEra = CivInfo.GetEra();
        if (previousEra == currentEra) return;

        if (showNotification)
        {
            if (!CivInfo.IsSpectator())
                CivInfo.AddNotification(
                    $"You have entered the [{currentEra}]!",
                    NotificationCategory.General,
                    NotificationIcon.Science
                );
            if (CivInfo.IsMajorCiv())
            {
                foreach (var knownCiv in CivInfo.GetKnownCivsWithSpectators())
                {
                    knownCiv.AddNotification(
                        $"[{CivInfo.CivName}] has entered the [{currentEra}]!",
                        NotificationCategory.General, CivInfo.CivName, NotificationIcon.Science
                    );
                }
            }

            foreach (var policyBranch in GetRuleset().PolicyBranches.Values.Where(it =>
                         it.Era == currentEra.Name && CivInfo.Policies.IsAdoptable(it)).ToList())
            {
                if (!CivInfo.IsSpectator())
                    CivInfo.AddNotification(
                        $"[{policyBranch.Name}] policy branch unlocked!",
                        new PolicyAction(policyBranch.Name),
                        NotificationCategory.General,
                        NotificationIcon.Culture
                    );
            }
        }

        var erasPassed = GetRuleset().Eras.Values
            .Where(it => it.EraNumber > previousEra.EraNumber && it.EraNumber <= currentEra.EraNumber)
            .OrderBy(it => it.EraNumber)
            .ToList();

        foreach (var era in erasPassed)
        {
            foreach (var unique in era.UniqueObjects)
            {
                if (unique.HasTriggerConditional() || !unique.ConditionalsApply(CivInfo.State)) continue;
                var multiplier = unique.GetUniqueMultiplier(CivInfo.State); // evaluated once, like repeat()
                for (var i = 0; i < multiplier; i++)
                    UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: $"due to entering the [{era.Name}]");
            }

            // B004 (bnw-spec 3.2 FreeSocialPoliciesPerEra, Poland): free policies for every era entered, also when several are skipped at once.
            foreach (var unique in CivInfo.GetMatchingUniques(UniqueType.FreePoliciesPerEra, CivInfo.State))
            {
                CivInfo.Policies.FreePolicies += int.Parse(unique.Params[0], System.Globalization.CultureInfo.InvariantCulture);
                CivInfo.Policies.ShouldOpenPolicyPicker = true;
            }

            CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponEnteringEra, CivInfo.State, it => it.Params[0] == era.Name, unique =>
            {
                UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: $"due to entering the [{era.Name}]");
            });
        }

        // The unfiltered version
        CivInfo.ForEachTriggeredUnique(UniqueType.TriggerUponEnteringEraUnfiltered, CivInfo.State, _ => true, false, unique =>
        {
            UniqueTriggerActivation.TriggerUnique(unique, CivInfo, triggerNotificationText: $"due to entering the [{currentEra.Name}]");
        });
    }

    private void UpdateEra()
    {
        var ruleset = CivInfo.GameInfo.Ruleset;
        if (ruleset.Technologies.Count == 0 || ResearchedTechnologies.Count == 0)
            return;

        // maxByOrNull / minByOrNull: the first element with the extreme value wins
        TechColumn? maxColumn = null;
        foreach (var tech in ResearchedTechnologies)
        {
            var column = tech.Column.NotNull();
            if (maxColumn == null || maxColumn.ColumnNumber < column.ColumnNumber) maxColumn = column;
        }
        var maxEraOfResearchedTechs = maxColumn.NotNull().Era;
        var maxEra = ruleset.Eras[maxEraOfResearchedTechs];

        TechColumn? minColumn = null;
        foreach (var tech in ruleset.Technologies.Values)
        {
            if (ResearchedTechnologies.Contains(tech)) continue;
            var column = tech.Column.NotNull();
            if (minColumn == null || minColumn.ColumnNumber > column.ColumnNumber) minColumn = column;
        }
        if (minColumn == null)
        {
            Era = maxEra;
            return;
        }

        var minEra = ruleset.Eras[minColumn.Era];

        Era = minEra.EraNumber <= maxEra.EraNumber ? maxEra : minEra;
    }

    private void AddTechToTransients(Technology tech)
    {
        TechUniques.AddUniques(tech.UniqueObjects);
    }

    public void SetTransients(Civilization civInfo)
    {
        CivInfo = civInfo;
        ResearchedTechnologies.AddRange(TechsResearched.Select(it => GetRuleset().Technologies[it]).ToList());
        ResearchedTechnologies.ForEach(AddTechToTransients);
        UpdateEra();  // before updateTransientBooleans so era-based conditionals can work
        UpdateTransientBooleans();
    }

    /// <summary>
    /// T208: rebuilds everything <see cref="SetTransients"/> derives from <see cref="TechsResearched"/>, in the same order, so a running game
    /// (which appends in research order, and for barbarians swaps <see cref="TechsResearched"/> wholesale) equals a reloaded one.
    /// </summary>
    public void RebuildTransients()
    {
        ResearchedTechnologies = [];
        TechUniques = new UniqueMap();
        ResearchedTechnologies.AddRange(TechsResearched.Select(it => GetRuleset().Technologies[it]).ToList());
        ResearchedTechnologies.ForEach(AddTechToTransients);
        UpdateEra();
        UpdateTransientBooleans();
    }

    private void UpdateTransientBooleans()
    {
        UnitsCanEmbark = CivInfo.HasUnique(UniqueType.LandUnitEmbarkation);
        var enterOceanUniques = CivInfo.GetMatchingUniques(UniqueType.UnitsMayEnterOcean).ToList();
        AllUnitsCanEnterOcean = enterOceanUniques.Any(it => Constants.All.Contains(it.Params[0]));
        EmbarkedUnitsCanEnterOcean = AllUnitsCanEnterOcean ||
                enterOceanUniques.Any(it => it.Params[0] == Constants.Embarked);
        SpecificUnitsCanEnterOcean = enterOceanUniques.Any(it => !Constants.All.Contains(it.Params[0]) && it.Params[0] != Constants.Embarked);

        MovementSpeedOnRoads = CivInfo.HasUnique(UniqueType.RoadMovementSpeed)
            ? RoadStatus.Road.GetMovementImproved() : RoadStatus.Road.GetMovement();
        RoadsConnectAcrossRivers = CivInfo.HasUnique(UniqueType.RoadsConnectAcrossRivers);
        allTechsAreResearched = CivInfo.GameInfo.Ruleset.Technologies.Values
            .All(it => IsResearched(it.Name) || !CanBeResearched(it.Name));
    }

    public RoadStatus GetBestRoadAvailable()
    {
        var railroadImprovement = GetRuleset().RailroadImprovement;  // May not exist in mods
        if (railroadImprovement != null && (railroadImprovement.TechRequired == null || IsResearched(railroadImprovement.TechRequired))
            && !ImprovementFunctions.GetImprovementBuildingProblems(railroadImprovement, CivInfo.State).Any())
            return RoadStatus.Railroad;

        var roadImprovement = GetRuleset().RoadImprovement;
        if (roadImprovement != null && (roadImprovement.TechRequired == null || IsResearched(roadImprovement.TechRequired))
            && !ImprovementFunctions.GetImprovementBuildingProblems(roadImprovement, CivInfo.State).Any())
            return RoadStatus.Road;

        return RoadStatus.None;
    }

    public bool CanResearchTech() => GetRuleset().Technologies.Values.Any(it => CanBeResearched(it.Name));
}
