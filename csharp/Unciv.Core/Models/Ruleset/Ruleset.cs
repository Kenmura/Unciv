using Unciv.Json;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Ruleset.kt
// getErrorList is the extension method RulesetValidatorExtensions.GetErrorList (T036). Not ported yet: allICivilopediaText and the Civilopedia text types, the `GameInfo.getGlobalUniques` doc link.
// Deviations:
// - libGDX FileHandle/Gdx.files become IRulesetFolder (see RulesetFiles.cs); Load takes the global jsons folder as a parameter, null being "console mode".
// - the fallback for the CityStateTypes of a base ruleset filters the uniques with UniqueValidator.checkUnique(...).isEmpty() through the
//   Ruleset.IsUniqueValid hook (default: the real validator).
// - the json is read with System.Text.Json plus Json/LenientJson (libGDX's unquoted keys); other libGDX leniencies (unquoted values, missing commas) are not
//   supported, the shipped rulesets do not use them.
// - BackwardCompatibility.updateDeprecations for ModOptions has an empty body upstream, so Load does not call anything.
// - `createHashmap`'s "missing a name" exception cannot happen: a C# Name is never null.
// - Kotlin's `by lazy` caches are KotlinLazy<T>; the maps are LinkedHashMap (ordered), exposed as read-only dictionaries to everything but the loader.

/// <summary>
/// A Ruleset instance can represent a built-in ruleset, a mod or a combined ruleset.
/// <c>Name</c> will be the built-in's fullName, the mod's name as displayed (same as folder name), or in the case of combined rulesets it will be empty.
/// </summary>
public class Ruleset : IRulesetParameterView, IRulesetConstructionView, IRulesetFeatureView, IRulesetTechView, IGameContextRuleset
{
    /// <summary>
    /// Kotlin <c>UniqueValidator(ruleset).checkUnique(Unique(text), null).isEmpty()</c> in the fallback CityStateTypes of base rulesets; a seam so that tests can replace it.
    /// </summary>
    public static Func<Ruleset, string, bool> IsUniqueValid { get; set; } =
        (ruleset, text) => new Validation.UniqueValidator(ruleset).CheckUnique(new UniqueObject(text), null).Count == 0;

    /// <summary>If (and only if) this Ruleset is a mod, this will be the source folder. In other words, this is <c>null</c> for built-in and combined rulesets.</summary>
    public IRulesetFolder? FolderLocation { get; set; }

    /// <summary>
    /// A Ruleset instance can represent a built-in ruleset, a mod or a combined ruleset.
    /// <c>Name</c> will be the built-in's fullName, the mod's name as displayed (same as folder name), or in the case of combined rulesets it will be empty.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>The list of mods that made up this Ruleset, including the base ruleset.</summary>
    public LinkedHashSet<string> Mods { get; } = [];

    // region Json fields
    public LinkedHashMap<string, Belief> Beliefs { get; } = new();
    public LinkedHashMap<string, Building> Buildings { get; } = new();
    public LinkedHashMap<string, Difficulty> Difficulties { get; } = new();
    public LinkedHashMap<string, Era> Eras { get; } = new();
    public LinkedHashMap<string, Speed> Speeds { get; } = new();

    /// <summary>Only <see cref="Load"/>, GameInfo, BaseUnit and RulesetValidator should access this directly. All other uses should call <c>GameInfo.getGlobalUniques</c> instead.</summary>
    public GlobalUniques GlobalUniques { get; internal set; } = new();

    public LinkedHashMap<string, NationObject> Nations { get; } = new();
    public LinkedHashMap<string, Policy> Policies { get; } = new();
    public LinkedHashMap<string, PolicyBranch> PolicyBranches { get; } = new();
    public List<string> Religions { get; } = [];
    public LinkedHashMap<string, RuinReward> RuinRewards { get; } = new();
    public LinkedHashMap<string, Quest> Quests { get; } = new();
    public LinkedHashMap<string, Specialist> Specialists { get; } = new();
    public LinkedHashMap<string, GreatWork> GreatWorks { get; } = new();
    public LinkedHashMap<string, Technology> Technologies { get; } = new();
    public List<TechColumn> TechColumns { get; } = [];
    public LinkedHashMap<string, Terrain> Terrains { get; } = new();
    public LinkedHashMap<string, TileImprovement> TileImprovements { get; } = new();
    public LinkedHashMap<string, TileResource> TileResources { get; } = new();
    public LinkedHashMap<string, Tutorial> Tutorials { get; } = new();
    public LinkedHashMap<string, BaseUnit> Units { get; } = new();
    public LinkedHashMap<string, Promotion> UnitPromotions { get; } = new();
    public LinkedHashMap<string, UnitNameGroup> UnitNameGroups { get; } = new();
    public LinkedHashMap<string, UnitType> UnitTypes { get; } = new();
    public LinkedHashMap<string, Victory> Victories { get; set; } = new();
    public LinkedHashMap<string, CityStateType> CityStateTypes { get; set; } = new();
    public LinkedHashMap<string, Personality> Personalities { get; } = new();
    public LinkedHashMap<string, Event> Events { get; } = new();
    public ModOptions ModOptions { get; set; } = new();
    // endregion

    // region cache fields
    private readonly KotlinLazy<IReadOnlyList<BaseUnit>> greatGeneralUnits;
    private readonly KotlinLazy<IReadOnlyList<TileImprovement>> tileRemovals;
    private readonly KotlinLazy<IReadOnlyList<TileImprovement>> nonRoadTileRemovals;
    private readonly KotlinLazy<IReadOnlySet<int>> allHappinessLevelsThatAffectUniques;
    private readonly KotlinLazy<bool> hasCountableModifiers;
    private readonly KotlinLazy<TileImprovement?> roadImprovement;
    private readonly KotlinLazy<TileImprovement?> railroadImprovement;

    public IReadOnlyList<BaseUnit> GreatGeneralUnits => greatGeneralUnits.Value;

    public IReadOnlyList<TileImprovement> TileRemovals => tileRemovals.Value;

    public IReadOnlyList<TileImprovement> NonRoadTileRemovals => nonRoadTileRemovals.Value;

    /// <summary>Contains all happiness levels that moving *from* them, to one *below* them, can change uniques that apply</summary>
    public IReadOnlySet<int> AllHappinessLevelsThatAffectUniques => allHappinessLevelsThatAffectUniques.Value;

    /// <summary>
    /// T262: whether any unique carries a countable conditional or a for-every-countable modifier that names a [stat/resource] Per Turn countable. Those read the
    /// per-turn stats of the civ's cities, which change between the two city loops, so the tile stat skip in <c>GameInfo.UpdateCivilizationState</c> is only taken in rulesets without them.
    /// </summary>
    public bool HasCountableModifiers => hasCountableModifiers.Value;

    public TileImprovement? RoadImprovement => roadImprovement.Value;

    public TileImprovement? RailroadImprovement => railroadImprovement.Value;
    // endregion

    public Ruleset()
    {
        greatGeneralUnits = new(() => Units.Values
            .Where(it => ((IHasUniques)it).HasUnique(UniqueType.GreatPersonFromCombat, GameContext.IgnoreConditionalsState)).ToList());
        tileRemovals = new(() => TileImprovements.Values
            .Where(it => it.Name.StartsWith(Constants.Remove, StringComparison.Ordinal)).ToList());
        nonRoadTileRemovals = new(() => TileRemovals
            .Where(rulesetImprovement => Enum.GetValues<RoadStatus>().All(it => it.GetRemoveAction() != rulesetImprovement.Name)).ToList());
        allHappinessLevelsThatAffectUniques = new(ComputeHappinessLevels);
        hasCountableModifiers = new(() => AllUniques().Any(unique => unique.Modifiers.Any(it => (it.Type is UniqueType.ConditionalCountableEqualTo
            or UniqueType.ConditionalCountableDifferentThan or UniqueType.ConditionalCountableMoreThan or UniqueType.ConditionalCountableLessThan
            or UniqueType.ConditionalCountableBetween or UniqueType.ForEveryCountable or UniqueType.ForEveryAmountCountable)
            && it.Params.Any(param => param.Contains("Per Turn", StringComparison.Ordinal)))));
        roadImprovement = new(() => RoadStatus.Road.Improvement(this));
        railroadImprovement = new(() => RoadStatus.Railroad.Improvement(this));
    }

    private LinkedHashSet<int> ComputeHappinessLevels()
    {
        var result = new LinkedHashSet<int>();
        foreach (var unique in AllUniques())
        {
            foreach (var conditional in unique.Modifiers)
            {
                if (conditional.Type == UniqueType.ConditionalWhenBelowAmountStatResource && conditional.Params[1] == "Happiness")
                    result.Add(ParseInt(conditional.Params[0]));
                if (conditional.Type == UniqueType.ConditionalWhenAboveAmountStatResource && conditional.Params[1] == "Happiness")
                    result.Add(ParseInt(conditional.Params[0]));
                if (conditional.Type == UniqueType.ConditionalWhenBetweenStatResource && conditional.Params[2] == "Happiness")
                {
                    result.Add(ParseInt(conditional.Params[0]));
                    result.Add(unchecked(ParseInt(conditional.Params[1]) + 1));
                }
                if (conditional.Type == UniqueType.ConditionalHappy) result.Add(0);
            }
        }
        return result;
    }

    private static int ParseInt(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    public Ruleset Clone()
    {
        var newRuleset = new Ruleset();
        newRuleset.Add(this);
        // Make sure the clone is recognizable - e.g. startNewGame fallback when a base mod was removed needs this
        newRuleset.Name = Name;
        newRuleset.ModOptions.IsBaseRuleset = ModOptions.IsBaseRuleset;
        return newRuleset;
    }

    /// <returns>A <see cref="Stat"/>, a <see cref="SubStat"/> or a <see cref="TileResource"/>; Kotlin's <c>GameResource</c>, which C# enums cannot implement (see GameResource.cs).</returns>
    public object? GetGameResource(string resourceName) =>
        (object?)StatExtensions.SafeValueOf(resourceName)
        ?? (object?)SubStatExtensions.SafeValueOf(resourceName)
        ?? TileResources.GetValueOrDefault(resourceName);

    /// <summary>The victories offered as checkboxes in the new game options, and enabled by the "all victories" default.</summary>
    public List<Victory> SelectableVictories() =>
        Victories.Values.Where(it => !((IHasUniques)it).HasUnique(UniqueType.WillNotBeChosenForNewGames)).ToList();

    private void LoadNamed<T>(IRulesetFolder folderHandle, RulesetFile file, LinkedHashMap<string, T> target) where T : class, INamed
    {
        var jsonFile = folderHandle.Child(file.Filename);
        if (jsonFile.Exists) target.PutAll(CreateHashmap(ReadArray<T>(jsonFile)));
    }

    private LinkedHashMap<string, T> CreateHashmap<T>(IReadOnlyCollection<T> items) where T : INamed
    {
        var hashMap = new LinkedHashMap<string, T>();
        foreach (var item in items)
        {
            hashMap[item.Name] = item;
            if (item is IRulesetObject rulesetObject) rulesetObject.OriginRuleset = Name; // RULESET name
        }
        return hashMap;
    }

    public void Add(Ruleset ruleset)
    {
        SelectiveRemove(Beliefs, ruleset.ModOptions.BeliefsToRemove, (it, f) => it.MatchesFilter(f));
        Beliefs.PutAll(ruleset.Beliefs);
        SelectiveRemove(Buildings, ruleset.ModOptions.BuildingsToRemove, (it, f) => it.MatchesFilter(f));
        Buildings.PutAll(ruleset.Buildings);
        SelectiveRemove(Difficulties, ruleset.ModOptions.DifficultiesToRemove);
        Difficulties.PutAll(ruleset.Difficulties);
        Eras.PutAll(ruleset.Eras);
        SelectiveRemove(Speeds, ruleset.ModOptions.SpeedsToRemove);
        Speeds.PutAll(ruleset.Speeds);
        GlobalUniques = GlobalUniques.Combine(GlobalUniques, ruleset.GlobalUniques);
        SelectiveRemove(Nations, ruleset.ModOptions.NationsToRemove, (it, f) => it.MatchesFilter(f));
        Nations.PutAll(ruleset.Nations);
        MergePolicies(ruleset);
        SelectiveRemove(Quests, ruleset.ModOptions.QuestsToRemove);
        Quests.PutAll(ruleset.Quests);

        // Remove associated Religions, including when they're favored by Nations
        Religions.RemoveAll(it => ruleset.ModOptions.ReligionsToRemove.Contains(it));
        Religions.AddRange(ruleset.Religions);
        foreach (var nation in Nations.Values.Where(it => it.FavoredReligion == null || !Religions.Contains(it.FavoredReligion)).ToList())
            nation.FavoredReligion = null;

        SelectiveRemove(RuinRewards, ruleset.ModOptions.RuinRewardsToRemove);
        RuinRewards.PutAll(ruleset.RuinRewards);
        SelectiveRemove(Specialists, ruleset.ModOptions.SpecialistsToRemove);
        Specialists.PutAll(ruleset.Specialists);
        GreatWorks.PutAll(ruleset.GreatWorks);
        MergeTechnologies(ruleset);
        SelectiveRemove(Terrains, ruleset.ModOptions.TerrainsToRemove, (it, f) => it.MatchesFilter(f));
        Terrains.PutAll(ruleset.Terrains);
        SelectiveRemove(TileImprovements, ruleset.ModOptions.ImprovementsToRemove, (it, f) => it.MatchesFilter(f));
        TileImprovements.PutAll(ruleset.TileImprovements);
        SelectiveRemove(TileResources, ruleset.ModOptions.ResourcesToRemove, (it, f) => it.MatchesFilter(f));
        TileResources.PutAll(ruleset.TileResources);
        SelectiveRemove(Tutorials, ruleset.ModOptions.TutorialsToRemove);
        Tutorials.PutAll(ruleset.Tutorials);
        SelectiveRemove(UnitTypes, ruleset.ModOptions.UnitTypesToRemove, (it, f) => it.MatchesFilter(f));
        UnitTypes.PutAll(ruleset.UnitTypes);
        SelectiveRemove(Victories, ruleset.ModOptions.VictoriesToRemove);
        Victories.PutAll(ruleset.Victories);
        SelectiveRemove(CityStateTypes, ruleset.ModOptions.CityStateTypesToRemove);
        CityStateTypes.PutAll(ruleset.CityStateTypes);
        SelectiveRemove(Units, ruleset.ModOptions.UnitsToRemove, (it, f) =>
        {
            it.SetRuleset(this);
            return it.MatchesFilter(f);
        });
        Units.PutAll(ruleset.Units);
        SelectiveRemove(Personalities, ruleset.ModOptions.PersonalitiesToRemove);
        Personalities.PutAll(ruleset.Personalities);
        SelectiveRemove(Events, ruleset.ModOptions.EventsToRemove);
        Events.PutAll(ruleset.Events);
        SelectiveRemove(UnitPromotions, ruleset.ModOptions.UnitPromotionsToRemove);
        UnitPromotions.PutAll(ruleset.UnitPromotions);
        SelectiveRemove(UnitNameGroups, ruleset.ModOptions.UnitNameGroupsToRemove);
        UnitNameGroups.PutAll(ruleset.UnitNameGroups);

        ModOptions.Uniques.AddRange(ruleset.ModOptions.Uniques);
        ModOptions.Constants.Merge(ruleset.ModOptions.Constants);

        Mods.AddAll(ruleset.Mods);
    }

    /// <summary>Helper for the "ToRemove" lists of ModOptions; the default <paramref name="filter"/> matches <c>Constants.all</c> and the item's name.</summary>
    private static void SelectiveRemove<T>(LinkedHashMap<string, T> map, IEnumerable<string> removals, Func<T, string, bool>? filter = null)
        where T : INamed
    {
        var removalList = removals.ToList();
        if (removalList.Count == 0) return;
        filter ??= (it, item) => Constants.All.Contains(item) || it.Name == item;
        var toRemove = new LinkedHashSet<string>(removalList.SelectMany(item => map.Where(it => filter(it.Value, item)).Select(it => it.Key).ToList()));
        foreach (var key in toRemove) map.Remove(key);
    }

    private void MergePolicies(Ruleset ruleset)
    {
        var noRemovals = ruleset.ModOptions.PoliciesToRemove.Count == 0 && ruleset.ModOptions.PolicyBranchesToRemove.Count == 0;
        if (noRemovals && ruleset.Policies.Count == 0 && ruleset.PolicyBranches.Count == 0) return;

        // We must remove all policies from a policy branch otherwise we have policies that cannot be picked
        // but are still considered "available"
        void RemovePolicyBranch(PolicyBranch policyBranch)
        {
            PolicyBranches.Remove(policyBranch.Name);
            foreach (var policy in policyBranch.Policies)
                Policies.Remove(policy.Name);
        }

        if (!noRemovals)
        {
            var branchesToRemove = ruleset.ModOptions.PolicyBranchesToRemove
                .SelectMany(policyBranchToRemove => PolicyBranches.Values.Where(it => it.MatchesFilter(policyBranchToRemove)).ToList())
                .Distinct().ToList();
            foreach (var branch in branchesToRemove) RemovePolicyBranch(branch);

            var overriddenPolicyBranches = PolicyBranches.Where(it => ruleset.PolicyBranches.ContainsKey(it.Key)).Select(it => it.Value).ToList();
            foreach (var policyBranch in overriddenPolicyBranches) RemovePolicyBranch(policyBranch);
        }

        PolicyBranches.PutAll(ruleset.PolicyBranches);

        if (!noRemovals)
        {
            // Remove the policies
            SelectiveRemove(Policies, ruleset.ModOptions.PoliciesToRemove, (it, f) => it.MatchesFilter(f));

            // Remove the policies if they exist in the policy branches too
            foreach (var policyToRemove in ruleset.ModOptions.PoliciesToRemove)
                foreach (var branch in PolicyBranches.Values)
                    branch.Policies.RemoveAll(it => it.MatchesFilter(policyToRemove));
        }

        Policies.PutAll(ruleset.Policies);
    }

    private void MergeTechnologies(Ruleset ruleset)
    {
        // Kotlin: "this is the old fragile way"
        SelectiveRemove(Technologies, ruleset.ModOptions.TechsToRemove, (it, f) => it.MatchesFilter(f));
        Technologies.PutAll(ruleset.Technologies);
        TechColumns.AddRange(ruleset.TechColumns);
    }

    public void Clear()
    {
        Beliefs.Clear();
        Buildings.Clear();
        Difficulties.Clear();
        Eras.Clear();
        Speeds.Clear();
        GlobalUniques = new GlobalUniques();
        Mods.Clear();
        Nations.Clear();
        Policies.Clear();
        PolicyBranches.Clear();
        Quests.Clear();
        Religions.Clear();
        RuinRewards.Clear();
        Specialists.Clear();
        GreatWorks.Clear();
        Technologies.Clear();
        TechColumns.Clear();
        Terrains.Clear();
        TileImprovements.Clear();
        TileResources.Clear();
        Tutorials.Clear();
        UnitPromotions.Clear();
        UnitNameGroups.Clear();
        Units.Clear();
        UnitTypes.Clear();
        Victories.Clear();
        CityStateTypes.Clear();
        Personalities.Clear();
        Events.Clear();
    }

    public IEnumerable<IRulesetObject> AllRulesetObjects() => RulesetFile.Values.SelectMany(it => it.GetRulesetObjects(this));

    public IEnumerable<ICivilopediaText> AllICivilopediaText() => AllRulesetObjects();

    public IEnumerable<UniqueObject> AllUniques() => RulesetFile.Values.SelectMany(it => it.GetUniques(this));

    /// <summary>Returns all non-empty ruleset names that can act as translation keys, with their source metadata.</summary>
    public IEnumerable<RulesetName> AllNames() =>
        RulesetFile.Values.SelectMany(it => it.GetNames(this)).Where(it => it.Name.Length != 0);

    private static List<T> ReadArray<T>(IRulesetFolder file) =>
        UncivJson.Deserialize<List<T>>(LenientJson.QuoteBareKeys(file.ReadString())) ?? throw new InvalidDataException($"{file.Name} does not contain a json array");

    /// <summary>
    /// Loads the json files of one ruleset folder (<c>jsons/Civ V - Vanilla</c> or <c>mods/X/jsons</c>), in Kotlin's order.
    /// </summary>
    /// <param name="folderHandle">The folder with the ruleset files.</param>
    /// <param name="globalJsonsFolder">The game's <c>jsons</c> folder with the global Tutorials.json; <c>null</c> when "running in console mode", where Kotlin has no <c>Gdx.files</c>.</param>
    public void Load(IRulesetFolder folderHandle, IRulesetFolder? globalJsonsFolder = null)
    {
        IRulesetFolder FileOf(RulesetFile file) => folderHandle.Child(file.Filename);

        // Note: Most files are loaded using createHashmap, which sets originRuleset automatically.
        // For other files containing IRulesetObject's we'll have to remember to do so manually - e.g. Tech.
        var modOptionsFile = FileOf(RulesetFile.ModOptions);
        if (modOptionsFile.Exists)
        {
            try
            {
                ModOptions = UncivJson.Deserialize<ModOptions>(LenientJson.QuoteBareKeys(modOptionsFile.ReadString())) ?? throw new InvalidDataException("ModOptions.json is null");
                // BackwardCompatibility.updateDeprecations: empty upstream
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to get modOptions from json file; Mod name: {folderHandle.Name}", ex);
            }
        }

        var techFile = FileOf(RulesetFile.Techs);
        if (techFile.Exists)
        {
            foreach (var techColumn in ReadArray<TechColumn>(techFile))
            {
                TechColumns.Add(techColumn);
                foreach (var tech in techColumn.Techs)
                {
                    if (tech.Cost == 0) tech.Cost = techColumn.TechCost;
                    tech.Column = techColumn;
                    tech.OriginRuleset = Name;
                    Technologies[tech.Name] = tech;
                }
            }
        }

        var buildingsFile = FileOf(RulesetFile.Buildings);
        if (buildingsFile.Exists) Buildings.PutAll(CreateHashmap(ReadArray<Building>(buildingsFile)));

        var terrainsFile = FileOf(RulesetFile.Terrains);
        if (terrainsFile.Exists)
        {
            Terrains.PutAll(CreateHashmap(ReadArray<Terrain>(terrainsFile)));
            foreach (var terrain in Terrains.Values)
            {
                terrain.OriginRuleset = Name;
                terrain.SetTransients();
            }
        }

        var resourcesFile = FileOf(RulesetFile.TileResources);
        if (resourcesFile.Exists) TileResources.PutAll(CreateHashmap(ReadArray<TileResource>(resourcesFile)));

        var improvementsFile = FileOf(RulesetFile.TileImprovements);
        if (improvementsFile.Exists) TileImprovements.PutAll(CreateHashmap(ReadArray<TileImprovement>(improvementsFile)));

        var erasFile = FileOf(RulesetFile.Eras);
        if (erasFile.Exists) Eras.PutAll(CreateHashmap(ReadArray<Era>(erasFile)));
        var eraIndex = 0;
        foreach (var era in Eras.Values) era.EraNumber = eraIndex++;

        var speedsFile = FileOf(RulesetFile.Speeds);
        if (speedsFile.Exists) Speeds.PutAll(CreateHashmap(ReadArray<Speed>(speedsFile)));

        var unitTypesFile = FileOf(RulesetFile.UnitTypes);
        if (unitTypesFile.Exists) UnitTypes.PutAll(CreateHashmap(ReadArray<UnitType>(unitTypesFile)));

        var unitsFile = FileOf(RulesetFile.Units);
        if (unitsFile.Exists) Units.PutAll(CreateHashmap(ReadArray<BaseUnit>(unitsFile)));

        var promotionsFile = FileOf(RulesetFile.UnitPromotions);
        if (promotionsFile.Exists) UnitPromotions.PutAll(CreateHashmap(ReadArray<Promotion>(promotionsFile)));

        var unitNameGroupsFile = FileOf(RulesetFile.UnitNameGroup);
        if (unitNameGroupsFile.Exists) UnitNameGroups.PutAll(CreateHashmap(ReadArray<UnitNameGroup>(unitNameGroupsFile)));

        LoadNamed(folderHandle, RulesetFile.Quests, Quests);

        LoadNamed(folderHandle, RulesetFile.Specialists, Specialists);

        LoadNamed(folderHandle, RulesetFile.GreatWorks, GreatWorks);

        var policiesFile = FileOf(RulesetFile.Policies);
        if (policiesFile.Exists) LoadPolicies(ReadArray<PolicyBranch>(policiesFile));

        var beliefsFile = FileOf(RulesetFile.Beliefs);
        if (beliefsFile.Exists)
            Beliefs.PutAll(CreateHashmap(ReadArray<Belief>(beliefsFile)));

        var religionsFile = FileOf(RulesetFile.Religions);
        if (religionsFile.Exists)
            Religions.AddRange(ReadArray<string>(religionsFile));

        var ruinRewardsFile = FileOf(RulesetFile.Ruins);
        if (ruinRewardsFile.Exists)
            RuinRewards.PutAll(CreateHashmap(ReadArray<RuinReward>(ruinRewardsFile)));

        var nationsFile = FileOf(RulesetFile.Nations);
        if (nationsFile.Exists)
        {
            Nations.PutAll(CreateHashmap(ReadArray<NationObject>(nationsFile)));
            foreach (var nation in Nations.Values) nation.SetTransients();
        }

        var difficultiesFile = FileOf(RulesetFile.Difficulties);
        if (difficultiesFile.Exists)
            Difficulties.PutAll(CreateHashmap(ReadArray<Difficulty>(difficultiesFile)));

        var globalUniquesFile = FileOf(RulesetFile.GlobalUniques);
        if (globalUniquesFile.Exists)
        {
            GlobalUniques = UncivJson.Deserialize<GlobalUniques>(LenientJson.QuoteBareKeys(globalUniquesFile.ReadString())) ?? throw new InvalidDataException("GlobalUniques.json is null");
            GlobalUniques.OriginRuleset = Name;
        }

        var victoryTypesFile = FileOf(RulesetFile.VictoryTypes);
        if (victoryTypesFile.Exists)
            Victories.PutAll(CreateHashmap(ReadArray<Victory>(victoryTypesFile)));

        var cityStateTypesFile = FileOf(RulesetFile.CityStateTypes);
        if (cityStateTypesFile.Exists)
            CityStateTypes.PutAll(CreateHashmap(ReadArray<CityStateType>(cityStateTypesFile)));

        var personalitiesFile = FileOf(RulesetFile.Personalities);
        if (personalitiesFile.Exists)
            Personalities.PutAll(CreateHashmap(ReadArray<Personality>(personalitiesFile)));

        var eventsFile = FileOf(RulesetFile.Events);
        if (eventsFile.Exists)
            Events.PutAll(CreateHashmap(ReadArray<Event>(eventsFile)));

        // Tutorials exist per builtin ruleset or mod, but there's also a global file that should always be loaded in a game.
        // Only load them when not running console mode, and only for base rulesets.
        // (games with extensions should get them from the base, and having them loaded in all extension mods would have the
        // extension as originRuleset in combined rulesets, not to mention wasting some memory).
        if (ModOptions.IsBaseRuleset && globalJsonsFolder != null) // we're not running console mode
        {
            var globalTutorialsFile = globalJsonsFolder.Child(RulesetFile.Tutorials.Filename);
            if (globalTutorialsFile.Exists)
                Tutorials.PutAll(CreateHashmap(ReadArray<Tutorial>(globalTutorialsFile)));
        }

        var tutorialsFile = FileOf(RulesetFile.Tutorials);
        if (tutorialsFile.Exists)
            Tutorials.PutAll(CreateHashmap(ReadArray<Tutorial>(tutorialsFile)));

        // Add objects that might not be present in base ruleset mods, but are required
        if (ModOptions.IsBaseRuleset)
        {
            var fallbackLazy = new KotlinLazy<Ruleset>(RulesetCache.GetVanillaRuleset); // clone at most once
            // This one should be temporary
            if (UnitTypes.Count == 0)
                UnitTypes.PutAll(fallbackLazy.Value.UnitTypes);

            // These should be permanent
            if (!ruinRewardsFile.Exists)
                RuinRewards.PutAll(fallbackLazy.Value.RuinRewards);

            if (!globalUniquesFile.Exists)
                GlobalUniques = fallbackLazy.Value.GlobalUniques;

            // If we have no victories, add all the default victories
            if (Victories.Count == 0) Victories.PutAll(fallbackLazy.Value.Victories);

            if (Speeds.Count == 0) Speeds.PutAll(fallbackLazy.Value.Speeds);
            if (Difficulties.Count == 0) Difficulties.PutAll(fallbackLazy.Value.Difficulties);

            if (CityStateTypes.Count == 0)
            {
                foreach (var cityStateType in fallbackLazy.Value.CityStateTypes.Values.ToList())
                {
                    CityStateTypes[cityStateType.Name] = new CityStateType
                    {
                        Name = cityStateType.Name,
                        Color = cityStateType.Color,
                        Uniques = cityStateType.Uniques.Where(it => IsUniqueValid(this, it)).ToList(),
                        FriendBonusUniques = cityStateType.FriendBonusUniques.Where(it => IsUniqueValid(this, it)).ToList(),
                        AllyBonusUniques = cityStateType.AllyBonusUniques.Where(it => IsUniqueValid(this, it)).ToList(),
                    };
                }
            }

            UpdateResourceTransients();
        }
    }

    private void LoadPolicies(List<PolicyBranch> branches)
    {
        PolicyBranches.PutAll(CreateHashmap(branches));
        foreach (var branch in PolicyBranches.Values)
        {
            // Setup this branch
            branch.Requires = [];
            branch.Branch = branch;
            foreach (var victoryType in Victories.Values)
            {
                if (!branch.Priorities.ContainsKey(victoryType.Name))
                    branch.Priorities[victoryType.Name] = 0;
            }
            Policies[branch.Name] = branch;

            // Append child policies of this branch
            foreach (var policy in branch.Policies)
            {
                policy.Branch = branch;
                policy.OriginRuleset = Name;
                policy.Requires ??= branch.PurchaseByLevel ? [] : [branch.Name]; // B013: tenets have no prerequisite, the ideology is checked by the manager

                if (!ReferenceEquals(policy, branch.Policies[^1]))
                {
                    // If mods override a previous policy's location, we don't want that policy to stick around,
                    // because it leads to softlocks on the policy picker screen
                    var conflictingLocationPolicy = Policies.Values.FirstOrDefault(it =>
                        it.Branch.Name == policy.Branch.Name
                        && it.Column == policy.Column
                        && it.Row == policy.Row);
                    if (conflictingLocationPolicy != null)
                        Policies.Remove(conflictingLocationPolicy.Name);
                }
                Policies[policy.Name] = policy;
            }

            // Add a finisher (B013: an ideology has none, its last tenet stays a tenet)
            if (branch.PurchaseByLevel) continue;
            branch.Policies[^1].Name = branch.Name + Policy.BranchCompleteSuffix;
        }
    }

    /// <summary>
    /// Building costs are unique in that they are dependant on info in the technology part.
    /// This means that if you add a building in a mod, you want it to depend on the original tech values.
    /// Alternatively, if you edit a tech column's building costs, you want it to affect all buildings in that column.
    /// This deals with that
    /// </summary>
    internal void UpdateBuildingCosts()
    {
        foreach (var building in Buildings.Values)
        {
            if (building.Cost != -1) continue;
            if (((IHasUniques)building).GetMatchingUniques(UniqueType.Unbuildable).Any(it => it.Modifiers.Count == 0)) continue;
            var column = ((IHasUniques)building).TechColumn(this);
            if (column == null) continue;
            building.Cost = building.IsAnyWonder() ? column.WonderCost : column.BuildingCost;
        }
    }

    /// <summary>Introduced to support UniqueType.ImprovesResources: gives a resource the chance to scan improvements</summary>
    internal void UpdateResourceTransients()
    {
        foreach (var resource in TileResources.Values)
            resource.SetTransients(this);
    }

    /// <summary>For use by tests. Use only before triggering the globalUniques.uniqueObjects lazy.</summary>
    public void AddGlobalUniques(params string[] uniques) => GlobalUniques.Uniques.AddRange(uniques);

    /// <summary>Used for displaying a RuleSet's name</summary>
    public override string ToString()
    {
        if (Name.Length != 0) return Name;
        if (Mods.Count == 1 && RulesetCache.Get(Mods.First()).NotNull().ModOptions.IsBaseRuleset) return Mods.First();
        return $"Combined RuleSet ({JavaFormat.ToJavaString(Mods)})";
    }

    public string GetSummary()
    {
        var stringList = new List<string>();
        if (ModOptions.IsBaseRuleset) stringList.Add("Base Ruleset");
        if (Technologies.Count != 0) stringList.Add($"[{Technologies.Count}] Techs");
        if (Nations.Count != 0) stringList.Add($"[{Nations.Count}] Nations");
        if (Units.Count != 0) stringList.Add($"[{Units.Count}] Units");
        if (Buildings.Count != 0) stringList.Add($"[{Buildings.Count}] Buildings");
        if (TileResources.Count != 0) stringList.Add($"[{TileResources.Count}] Resources");
        if (TileImprovements.Count != 0) stringList.Add($"[{TileImprovements.Count}] Improvements");
        if (Religions.Count != 0) stringList.Add($"[{Religions.Count}] Religions");
        if (Beliefs.Count != 0) stringList.Add($"[{Beliefs.Count}] Beliefs");
        return string.Join(", ", stringList.Select(it => it.Tr()));
    }

    // region interface views (the explicit implementations keep the Kotlin-named maps above as the public API)

    private static MapView<T, TView> View<TView, T>(LinkedHashMap<string, T> map)
        where T : class, TView
        where TView : class => new(map);

    /// <summary>IReadOnlyDictionary is not covariant, so this adapts the ordered map to the covariant <see cref="IRulesetMapView{TValue}"/>.</summary>
    private sealed class MapView<T, TView>(LinkedHashMap<string, T> map) : IRulesetMapView<TView>
        where T : class, TView
        where TView : class
    {
        public IEnumerable<string> Keys => map.Keys;
        public IEnumerable<TView> Values => map.Values;
        public bool ContainsKey(string key) => map.ContainsKey(key);
        public TView? this[string key] => map.GetValueOrDefault(key);
    }

    IRulesetMapView<IRulesetUnitParameterView> IRulesetParameterView.Units => View<IRulesetUnitParameterView, BaseUnit>(Units);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.UnitTypes => View<IRulesetObjectParameterView, UnitType>(UnitTypes);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.UnitPromotions => View<IRulesetObjectParameterView, Promotion>(UnitPromotions);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.Nations => View<IRulesetObjectParameterView, NationObject>(Nations);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.Buildings => View<IRulesetObjectParameterView, Building>(Buildings);
    IRulesetMapView<IRulesetTerrainParameterView> IRulesetParameterView.Terrains => View<IRulesetTerrainParameterView, Terrain>(Terrains);
    IRulesetMapView<IRulesetResourceParameterView> IRulesetParameterView.TileResources => View<IRulesetResourceParameterView, TileResource>(TileResources);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.TileImprovements => View<IRulesetObjectParameterView, TileImprovement>(TileImprovements);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.Beliefs => View<IRulesetObjectParameterView, Belief>(Beliefs);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.Technologies => View<IRulesetObjectParameterView, Technology>(Technologies);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.Policies => View<IRulesetObjectParameterView, Policy>(Policies);
    IRulesetMapView<IRulesetObjectParameterView> IRulesetParameterView.UnitNameGroups => View<IRulesetObjectParameterView, UnitNameGroup>(UnitNameGroups);
    IRulesetMapView<object> IRulesetParameterView.Specialists => View<object, Specialist>(Specialists);
    IRulesetMapView<object> IRulesetParameterView.GreatWorks => View<object, GreatWork>(GreatWorks);
    IRulesetMapView<object> IRulesetParameterView.Eras => View<object, Era>(Eras);
    IRulesetMapView<object> IRulesetParameterView.Speeds => View<object, Speed>(Speeds);
    IRulesetMapView<object> IRulesetParameterView.Difficulties => View<object, Difficulty>(Difficulties);
    IRulesetMapView<object> IRulesetParameterView.Events => View<object, Event>(Events);
    IRulesetMapView<object> IRulesetParameterView.PolicyBranches => View<object, PolicyBranch>(PolicyBranches);
    IRulesetMapView<object> IRulesetParameterView.Victories => View<object, Victory>(Victories);
    IReadOnlyCollection<string> IRulesetParameterView.Religions => Religions;

    // IRulesetConstructionView
    UnitType? IRulesetConstructionView.GetUnitType(string name) => UnitTypes.GetValueOrDefault(name);
    BaseUnit? IRulesetConstructionView.GetUnit(string name) => Units.GetValueOrDefault(name);
    IEnumerable<BaseUnit> IRulesetConstructionView.GetAllUnits() => Units.Values;
    IEnumerable<UnitType> IRulesetConstructionView.GetAllUnitTypes() => UnitTypes.Values;
    TileImprovement? IRulesetConstructionView.GetTileImprovement(string name) => TileImprovements.GetValueOrDefault(name);
    Promotion? IRulesetConstructionView.GetPromotion(string name) => UnitPromotions.GetValueOrDefault(name);

    bool? IRulesetConstructionView.TechnologyMatchesFilter(string techName, string filter, GameContext? state, bool multiFilter) =>
        Technologies.GetValueOrDefault(techName)?.MatchesFilter(filter, state, multiFilter);

    // IRulesetFeatureView
    bool IRulesetFeatureView.HasBeliefs => Beliefs.Count != 0;
    bool IRulesetFeatureView.HasSpyNames => Nations.Values.Any(it => it.SpyNames.Count != 0);

    // IRulesetTechView
    public Technology? GetTechnology(string name) => Technologies.GetValueOrDefault(name);
    public Era? GetEra(string? name) => name == null ? null : Eras.GetValueOrDefault(name);

    // IGameContextRuleset
    public int? GetEraNumber(string eraName) => Eras.GetValueOrDefault(eraName)?.EraNumber;
    public IReadOnlyList<string> DifficultyNames => Difficulties.Keys.ToList();
    public IEnumerable<string> GetBuildingNamesMatchingFilter(string filter) =>
        Buildings.Values.Where(it => it.MatchesFilter(filter)).Select(it => it.Name).ToList();
    public IEnumerable<string> GetTileResourceNamesMatchingFilter(string filter, GameContext state) =>
        TileResources.Values.Where(it => it.MatchesFilter(filter, state)).Select(it => it.Name).ToList();
    public bool? TechnologyMatchesFilter(string techName, string filter, GameContext state) =>
        Technologies.GetValueOrDefault(techName)?.MatchesFilter(filter, state);

    // endregion
}
