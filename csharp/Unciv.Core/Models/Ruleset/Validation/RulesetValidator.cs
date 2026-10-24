using System.Globalization;
using System.Text.RegularExpressions;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components.Extensions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/RulesetValidator.kt
// Not ported (graphics/UI-bound, and the types they need have no engine equivalent yet):
// - the texture checks: AtlasPreview (initTextureNamesCache, uncachedImageExists), checkTilesetSanity (TileSetConfig, TileSetCache, Gdx.files, isRunFromJar)
//   and the unit-set-art check in checkUnit. They only ever add OK/WarningOptionsOnly/Warning lines about images.
// - checkCivilopediaText asks the IFormattedLineImageCheck in ImageCheck whether an extraImage exists (Kotlin: textureNamesCache.imageExists via uncachedImageExists);
//   until a client supplies one, every image is assumed to exist so only the other FormattedLine problems are reported.
// Deviations:
// - FileHandle becomes IRulesetFolder; messages that print a FileHandle path print the folder name instead.
// - The map preselection check reads UncivGame.Current.files.getModFolder(name): the ruleset's own FolderLocation is that folder for a mod.
// - getErrorList's catch adds the exception text (Kotlin's stack trace string is JVM specific).

/// <summary>
///  Class managing ruleset validation.
///
///  - This class manages code flow and checks that do not require the ruleset to be complete (it works on extension mods).
///  - The <see cref="BaseRulesetValidator"/> subclass on the other hand does detailed checks that require the Ruleset to be complete,
///    that is, all references to other objects should resolve.
///  - Instantiation **must** occur using the <see cref="Create"/> factory, it decides which class to deliver.
///  - Individual methods per RulesetObject type are virtual, and <see cref="BaseRulesetValidator"/> adds checks by overriding them.
///
///  Repeat to be clear: **All** RulesetInvariant checks should happen here, and **only** those.
///  <see cref="BaseRulesetValidator"/> adds the RulesetSpecific checks and only those via overrides.
/// </summary>
public class RulesetValidator
{
    protected readonly Ruleset ruleset;
    private readonly bool tryFixUnknownUniques;

    /// <summary>true for a <see cref="BaseRulesetValidator"/> instance, false for a <see cref="RulesetValidator"/> instance.</summary>
    private readonly bool reportRulesetSpecificErrors;

    protected readonly UniqueValidator uniqueValidator;

    /// <param name="ruleset">The Ruleset to check - can be a base ruleset, an extension mod, or a complex Ruleset assembled from a base ruleset and one or more mods.
    /// A complex Ruleset should have modOptions.isBaseRuleset ON, so the detailed checks are applied.</param>
    /// <param name="tryFixUnknownUniques">If on, the result will include hints on possible misspellings</param>
    protected RulesetValidator(Ruleset ruleset, bool tryFixUnknownUniques)
    {
        this.ruleset = ruleset;
        this.tryFixUnknownUniques = tryFixUnknownUniques;
        reportRulesetSpecificErrors = ruleset.ModOptions.IsBaseRuleset;
        uniqueValidator = new UniqueValidator(ruleset, tryFixUnknownUniques);
    }

    public bool TryFixUnknownUniques => tryFixUnknownUniques;

    private sealed class AssumeAllImagesExist : IFormattedLineImageCheck
    {
        public bool ExtraImageExists(string extraImage) => true;
    }

    /// <summary>Stands in for <c>uncachedImageExists</c>, which <c>FormattedLine</c> uses for its own checking</summary>
    public IFormattedLineImageCheck ImageCheck { get; set; } = new AssumeAllImagesExist();

    public static RulesetValidator Create(Ruleset ruleset, bool tryFixUnknownUniques = false) =>
        ruleset.ModOptions.IsBaseRuleset
            ? new BaseRulesetValidator(ruleset, tryFixUnknownUniques)
            : new RulesetValidator(ruleset, tryFixUnknownUniques);

    /// <summary>This is the public API called from <see cref="RulesetValidatorExtensions.GetErrorList"/></summary>
    public RulesetErrorList GetErrorList()
    {
        try
        {
            return GetErrorListInternal();
        }
        catch (Exception e)
        {
            var result = new RulesetErrorList(ruleset);
            result.Add($"Error while validating ruleset {ruleset.Name}: {e.Message}");
            result.Add(e.ToString());
            return result;
        }
    }

    private RulesetErrorList GetErrorListInternal()
    {
        var lines = new RulesetErrorList(ruleset);

        AddModOptionsErrors(lines);
        AddGlobalUniqueErrors(lines);

        AddUnitErrors(lines);
        AddBuildingErrors(lines);
        AddSpecialistErrors(lines);
        AddResourceErrors(lines);
        AddImprovementErrors(lines);
        AddTerrainErrors(lines);
        AddTechErrors(lines);
        AddTechColumnErrors(lines);
        AddEraErrors(lines);
        AddSpeedErrors(lines);
        AddPersonalityErrors(lines);
        AddBeliefErrors(lines);
        AddNationErrors(lines);
        AddPolicyErrors(lines);
        AddRuinsErrors(lines);
        AddPromotionErrors(lines);
        AddUnitTypeErrors(lines);
        AddUnitNameGroupsErrors(lines);
        AddVictoryTypeErrors(lines);
        AddDifficultyErrors(lines);
        AddEventErrors(lines);
        AddCityStateTypeErrors(lines);

        CheckFreeBuildingPossibleRecursions(lines);
        AddTranslationNameCollisionWarnings(lines);
        AddEmptyNamesErrors(lines);

        // Not ported: initTextureNamesCache, checkTilesetSanity (see the file header)
        CheckCivilopediaText(lines);
        CheckFileNames(lines);

        return lines;
    }

    //region RulesetObject-specific handlers

    protected virtual void AddBeliefErrors(RulesetErrorList lines)
    {
        foreach (var belief in ruleset.Beliefs.Values)
        {
            if (belief.Type is BeliefType.Any or BeliefType.None)
                lines.Add($"{belief.Name} type is {belief.Type}, which is not allowed!", sourceObject: belief);
            uniqueValidator.CheckUniques(belief, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddBuildingErrors(RulesetErrorList lines)
    {
        foreach (var building in ruleset.Buildings.Values)
        {
            CheckBuilding(building, lines);
            uniqueValidator.CheckUniques(building, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void CheckBuilding(Building building, RulesetErrorList lines)
    {
        if (!((IHasUniques)building).RequiredTechs().Any() && building.Cost == -1 && !((IHasUniques)building).HasUnique(UniqueType.Unbuildable))
            lines.Add(
                $"{building.Name} is buildable and therefore should either have an explicit cost or reference an existing tech!",
                RulesetErrorSeverity.Warning, building);

        if (building.Replaces != null && building.UniqueTo == null)
            lines.Add($"{building.Name} should replace {building.Replaces} but does not have uniqueTo assigned!");
        if (building.Replaces == building.Name)
            lines.Add($"{building.Name} replaces itself!");
    }

    protected virtual void AddCityStateTypeErrors(RulesetErrorList lines)
    {
        foreach (var cityStateType in ruleset.CityStateTypes.Values)
        {
            foreach (var unique in cityStateType.AllyBonusUniqueMap.GetAllUniques().Concat(cityStateType.FriendBonusUniqueMap.GetAllUniques()).ToList())
            {
                var errors = uniqueValidator.CheckUnique(unique, null,
                    reportRulesetSpecificErrors ? UniqueValidator.AllParameterSeverities : UniqueValidator.ExtensionModParameterSeverities);
                lines.AddAll(errors);
            }
            uniqueValidator.CheckUniques(cityStateType, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddDifficultyErrors(RulesetErrorList lines)
    {
        foreach (var difficulty in ruleset.Difficulties.Values)
        {
            if (difficulty.AiBuildingCostModifier < 0 || difficulty.AiBuildingMaintenanceModifier < 0 || difficulty.AiCityGrowthModifier < 0 ||
                difficulty.AiUnhappinessModifier < 0 || difficulty.AiUnitCostModifier < 0 || difficulty.AiUnitMaintenanceModifier < 0 ||
                difficulty.AiUnitSupplyModifier < 0 || difficulty.AiWonderCostModifier < 0 ||
                difficulty.BuildingCostModifier < 0 || difficulty.PolicyCostModifier < 0 || difficulty.ResearchCostModifier < 0 ||
                difficulty.UnhappinessModifier < 0 || difficulty.UnitCostModifier < 0)
                lines.Add($"Difficulty {difficulty.Name} contains one or more negative modifier(s)!", sourceObject: null);
            if (difficulty.TurnBarbariansCanEnterPlayerTiles < 0)
                lines.Add($"Difficulty {difficulty.Name} has a negative turnBarbariansCanEnterPlayerTiles!",
                    RulesetErrorSeverity.Warning, sourceObject: null);
            uniqueValidator.CheckUniques(difficulty, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddEraErrors(RulesetErrorList lines)
    {
        foreach (var era in ruleset.Eras.Values)
        {
            if (era.ResearchAgreementCost < 0 || era.StartingSettlerCount < 0 || era.StartingWorkerCount < 0 ||
                era.StartingMilitaryUnitCount < 0 || era.StartingGold < 0 || era.StartingCulture < 0)
                lines.Add($"Unexpected negative number found while parsing era {era.Name}", sourceObject: era);
            if (era.SettlerPopulation <= 0)
                lines.Add($"Population in cities from settlers must be strictly positive! Found value {era.SettlerPopulation} for era {era.Name}", sourceObject: era);

            if (era.AllyBonus.Count != 0 || era.FriendBonus.Count != 0)
                lines.Add(
                    $"Era {era.Name} contains city-state bonuses. City-state bonuses are now defined in CityStateType.json",
                    RulesetErrorSeverity.WarningOptionsOnly, era);

            uniqueValidator.CheckUniques(era, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddEventErrors(RulesetErrorList lines)
    {
        // An Event is not a IHasUniques, so not suitable as sourceObject
        foreach (var @event in ruleset.Events.Values)
        {
            foreach (var choice in @event.Choices)
                uniqueValidator.CheckUniques(choice, lines, reportRulesetSpecificErrors);
            uniqueValidator.CheckUniques(@event, lines, reportRulesetSpecificErrors);
        }
    }

    private sealed class FakeUniqueContainer : IHasUniques
    {
        public List<string> Uniques { get; set; } = [];
        public IReadOnlyList<UniqueObject> UniqueObjects { get; } = [];
        public UniqueMap UniqueMap { get; } = UniqueMap.Empty;
        public UniqueTarget GetUniqueTarget() => UniqueTarget.Unit;
        public string Name { get; set; } = "Global unit uniques";
    }

    protected virtual void AddGlobalUniqueErrors(RulesetErrorList lines)
    {
        uniqueValidator.CheckUniques(ruleset.GlobalUniques, lines, reportRulesetSpecificErrors);

        var fakeUniqueContainer = new FakeUniqueContainer();

        foreach (string uniqueText in ruleset.GlobalUniques.UnitUniques)
        {
            var unique = new UniqueObject(uniqueText);
            var errors = uniqueValidator.CheckUnique(
                unique,
                fakeUniqueContainer,
                reportRulesetSpecificErrors ? UniqueValidator.AllParameterSeverities : UniqueValidator.ExtensionModParameterSeverities);
            lines.AddAll(errors);
        }
    }

    protected virtual void AddImprovementErrors(RulesetErrorList lines)
    {
        var removeActions = Enum.GetValues<RoadStatus>().Select(it => it.GetRemoveAction()).ToList();
        foreach (var improvement in ruleset.TileImprovements.Values)
        {
            var improvementUniques = (IHasUniques)improvement;
            if (improvement.Replaces != null && improvement.UniqueTo == null)
                lines.Add($"{improvement.Name} should replace {improvement.Replaces} but does not have uniqueTo assigned!");
            if (improvement.Replaces == improvement.Name)
                lines.Add($"{improvement.Name} replaces itself!");
            if (improvement.TerrainsCanBeBuiltOn.Count == 0
                && !improvementUniques.HasUnique(UniqueType.CanOnlyImproveResource)
                && !improvementUniques.HasUnique(UniqueType.Unbuildable)
                && !improvement.Name.StartsWith(Constants.Remove, StringComparison.Ordinal)
                && !removeActions.Contains(improvement.Name)
                && improvement.Name != Constants.CancelImprovementOrder)
            {
                lines.Add(
                    $"{improvement.Name} has an empty `terrainsCanBeBuiltOn`, isn't allowed to only improve resources. As such it isn't buildable! Either give this the unique \"Unbuildable\", \"Can only be built to improve a resource\", or add \"Land\", \"Water\" or any other value to `terrainsCanBeBuiltOn`.",
                    RulesetErrorSeverity.Warning, improvement);
            }

            foreach (var unique in improvementUniques.UniqueObjects
                         .Where(it => it.Type is UniqueType.PillageYieldRandom or UniqueType.PillageYieldFixed))
            {
                if (!Stats.IsStats(unique.Params[0])) continue;
                var parameters = Stats.Parse(unique.Params[0]);
                if (parameters.Min() < 0f) lines.Add(
                    $"{improvement.Name} cannot have a negative value for a pillage yield!",
                    RulesetErrorSeverity.Error, improvement);
            }

            bool hasPillageUnique = improvementUniques.HasUnique(UniqueType.PillageYieldRandom, GameContext.IgnoreConditionalsState)
                || improvementUniques.HasUnique(UniqueType.PillageYieldFixed, GameContext.IgnoreConditionalsState);
            if (hasPillageUnique && improvementUniques.HasUnique(UniqueType.Unpillagable, GameContext.IgnoreConditionalsState))
            {
                lines.Add(
                    $"{improvement.Name} has both an `Unpillagable` unique type and a `PillageYieldRandom` or `PillageYieldFixed` unique type!",
                    RulesetErrorSeverity.Warning, improvement);
            }

            uniqueValidator.CheckUniques(improvement, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddModOptionsErrors(RulesetErrorList lines)
    {
        // Basic Unique validation (type, target, parameters) should always run.
        // Using reportRulesetSpecificErrors=true as ModOptions never should use Uniques depending on objects from a base ruleset anyway.
        uniqueValidator.CheckUniques(ruleset.ModOptions, lines, reportRulesetSpecificErrors: true);

        // propertyName is the Kotlin property name, which is what the message shows
        void CheckConstant(int value, string propertyName, int first, int last)
        {
            if (value >= first && value <= last) return;
            string describe = first == int.MinValue ? $"Maximum {last}"
                : last == int.MaxValue ? $"Minimum {first}"
                : $"Minimum {first}, Maximum {last}";
            // The stray '}' after the name is in the Kotlin template string
            lines.Add($"ModConstant '{propertyName}}}' does not meet criteria '{describe}'.");
        }

        var constants = ruleset.ModOptions.Constants;
        CheckConstant(constants.CityExpandRange, "cityExpandRange", 1, 100);
        CheckConstant(constants.CityWorkRange, "cityWorkRange", 1, 100);
        // Crashed with 10 as of writing
        CheckConstant(constants.MinimalCityDistance, "minimalCityDistance", 0, 9);
        CheckConstant(constants.MinimalCityDistanceOnDifferentContinents, "minimalCityDistanceOnDifferentContinents", 0, 9);
        // Game hangs with very high values
        CheckConstant(constants.BaseCityBombardRange, "baseCityBombardRange", 0, 1000);

        if (string.IsNullOrWhiteSpace(ruleset.Name)) return; // The rest of these tests don't make sense for combined rulesets

        var audioVisualUniqueTypes = new HashSet<UniqueType>
        {
            UniqueType.ModIsAudioVisual,
            UniqueType.ModIsAudioVisualOnly,
            UniqueType.ModIsNotAudioVisual,
        };
        // modOptions is a valid sourceObject, but unnecessary
        if (ruleset.ModOptions.UniqueObjects.Count(it => it.Type != null && audioVisualUniqueTypes.Contains(it.Type.Value)) > 1)
            lines.Add("A mod should only specify one of the 'can/should/cannot be used as permanent audiovisual mod' options.", sourceObject: null);

        var mapSelectUniques = ((IHasUniques)ruleset.ModOptions).GetMatchingUniques(UniqueType.ModMapPreselection).ToList();
        if (mapSelectUniques.Count > 1)
            lines.Add("Specifying more than one map as preselection makes no sense", RulesetErrorSeverity.WarningOptionsOnly, sourceObject: null);
        if (mapSelectUniques.Count != 0)
        {
            var mapsFolder = ruleset.FolderLocation?.Child("maps");
            if (mapsFolder is { Exists: true })
            {
                var maps = mapsFolder.List().Select(it => it.Name.ToLowerInvariant()).ToList();
                foreach (var unique in mapSelectUniques)
                {
                    if (maps.Contains(unique.Params[0].ToLowerInvariant())) continue;
                    lines.Add($"Mod names map '{unique.Params[0]}' as preselection, which does not exist.", RulesetErrorSeverity.WarningOptionsOnly, sourceObject: null);
                }
            }
            else
            {
                lines.Add("Mod option for map preselection exists but Mod has no 'maps' folder.", RulesetErrorSeverity.WarningOptionsOnly, sourceObject: null);
            }
        }
    }

    protected virtual void AddNationErrors(RulesetErrorList lines)
    {
        foreach (var nation in ruleset.Nations.Values)
        {
            CheckNation(nation, lines);
            uniqueValidator.CheckUniques(nation, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void CheckNation(NationObject nation, RulesetErrorList lines)
    {
        if (nation.Cities.Count == 0 && !nation.IsSpectator && !nation.IsBarbarian)
            lines.Add($"{nation.Name} can settle cities, but has no city names!", sourceObject: nation);

        static bool IsColorFaulty(List<int>? rgb) => rgb == null ? false : rgb.Count != 3 || rgb.Any(it => it is < 0 or > 255);

        bool badInner = IsColorFaulty(nation.InnerColor);
        bool badOuter = IsColorFaulty(nation.OuterColor);
        if (badInner)
            lines.Add($"{nation.Name}'s innerColor is not an array of three integers in the 0..255 range", sourceObject: nation);
        if (badOuter)
            lines.Add($"{nation.Name}'s outerColor is not an array of three integers in the 0..255 range", sourceObject: nation);

        if (!badInner && !badOuter)
            CheckContrasts(nation.GetInnerColor(), nation.GetOuterColor(), nation, lines);
    }

    protected virtual void AddPersonalityErrors(RulesetErrorList lines)
    {
        foreach (var personality in ruleset.Personalities.Values)
            uniqueValidator.CheckUniques(personality, lines, reportRulesetSpecificErrors);
    }

    protected virtual void AddPolicyErrors(RulesetErrorList lines)
    {
        foreach (var policy in ruleset.Policies.Values)
            uniqueValidator.CheckUniques(policy, lines, reportRulesetSpecificErrors);
    }

    protected virtual void AddPromotionErrors(RulesetErrorList lines)
    {
        foreach (var promotion in ruleset.UnitPromotions.Values)
        {
            uniqueValidator.CheckUniques(promotion, lines, reportRulesetSpecificErrors);
            CheckContrasts(promotion.GetInnerColorObject() ?? PortraitPromotionDefaultInnerColor,
                promotion.GetOuterColorObject() ?? PortraitPromotionDefaultOuterColor, promotion, lines);
            CheckPromotion(promotion, lines);
        }
    }

    // PortraitPromotion.defaultInnerColor / defaultOuterColor (Portrait.kt; the UI class is not ported)
    private static readonly EngineColor PortraitPromotionDefaultInnerColor = ColorExtensions.ColorFromRGB(255, 226, 0);
    private static readonly EngineColor PortraitPromotionDefaultOuterColor = ColorExtensions.ColorFromRGB(0, 12, 49);

    protected virtual void CheckPromotion(Promotion promotion, RulesetErrorList lines)
    {
        if (promotion.Row < -1) lines.Add($"Promotion {promotion.Name} has invalid row value: {promotion.Row}", sourceObject: promotion);
        if (promotion.Column < 0) lines.Add($"Promotion {promotion.Name} has invalid column value: {promotion.Column}", sourceObject: promotion);
        if (promotion.Row == -1) return;
        foreach (var otherPromotion in ruleset.UnitPromotions.Values)
            if (!ReferenceEquals(promotion, otherPromotion) && promotion.Column == otherPromotion.Column && promotion.Row == otherPromotion.Row)
                lines.Add($"Promotions {promotion.Name} and {otherPromotion.Name} have the same position: {promotion.Row}/{promotion.Column}", sourceObject: promotion);
    }

    protected virtual void AddResourceErrors(RulesetErrorList lines)
    {
        foreach (var resource in ruleset.TileResources.Values)
            uniqueValidator.CheckUniques(resource, lines, reportRulesetSpecificErrors);
    }

    protected virtual void AddRuinsErrors(RulesetErrorList lines)
    {
        foreach (var reward in ruleset.RuinRewards.Values)
        {
            if (reward.Weight < 0) lines.Add($"{reward.Name} has a negative weight, which is not allowed!", sourceObject: reward);
            uniqueValidator.CheckUniques(reward, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddSpecialistErrors(RulesetErrorList lines)
    {
    }

    protected virtual void AddSpeedErrors(RulesetErrorList lines)
    {
        foreach (var speed in ruleset.Speeds.Values)
        {
            if (speed.Modifier < 0f || speed.BarbarianModifier < 0f || speed.CultureCostModifier < 0f || speed.FaithCostModifier < 0f ||
                speed.GoldCostModifier < 0f || speed.GoldGiftModifier < 0f || speed.GoldenAgeLengthModifier < 0f ||
                speed.ImprovementBuildLengthModifier < 0f || speed.ProductionCostModifier < 0f || speed.ScienceCostModifier < 0f)
                lines.Add($"One or more negative speed modifier(s) for game speed {speed.Name}", sourceObject: speed);
            if (speed.DealDuration < 1 || speed.PeaceDealDuration < 1)
                lines.Add("Deal durations must be positive", sourceObject: speed);
            if (speed.ReligiousPressureAdjacentCity < 0)
                lines.Add("'religiousPressureAdjacentCity' must not be negative", sourceObject: speed);
            if (speed.YearsPerTurn.Count == 0)
                lines.Add($"Empty turn increment list for game speed {speed.Name}", sourceObject: speed);
            int lastTurn = 0;
            foreach (var (yearInterval, untilTurn) in speed.YearsPerTurn)
            {
                if (yearInterval <= 0f)
                    lines.Add($"Negative year interval {KotlinFloatToString(yearInterval)} in turn increment list", sourceObject: speed);
                if (untilTurn <= lastTurn)
                    lines.Add($"The 'untilTurn' field in the turn increment list must be monotonously increasing, but {untilTurn} is <= {lastTurn}", sourceObject: speed);
                lastTurn = untilTurn;
            }
            uniqueValidator.CheckUniques(speed, lines, reportRulesetSpecificErrors);
        }
    }

    /// <summary>Kotlin <c>Float.toString()</c> for the values that can occur here: shortest round-trip digits with a ".0" for integral values.</summary>
    private static string KotlinFloatToString(float value)
    {
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('N') || text.Contains('I') ? text : text + ".0";
    }

    protected virtual void AddTechErrors(RulesetErrorList lines)
    {
        var duplicateColumnNumbers = ruleset.TechColumns.GroupBy(it => it.ColumnNumber).Where(it => it.Count() > 1).Select(it => it.Key).ToList();
        if (duplicateColumnNumbers.Count != 0)
            lines.Add($"Tech column numbers [{string.Join(", ", duplicateColumnNumbers)}] have multiple TechColumn entries", RulesetErrorSeverity.ErrorOptionsOnly);

        foreach (var tech in ruleset.Technologies.Values)
        {
            if (tech.Row < 1) lines.Add($"Tech {tech.Name} has a row value below 1: {tech.Row}", sourceObject: tech);
            uniqueValidator.CheckUniques(tech, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddTechColumnErrors(RulesetErrorList lines)
    {
        // TechColumn is not a IHasUniques and unsuitable as sourceObject
        foreach (var techColumn in ruleset.TechColumns)
        {
            if (techColumn.ColumnNumber < 0)
                lines.Add($"Tech Column number {techColumn.ColumnNumber} is negative", sourceObject: null);

            var buildingsWithoutAssignedCost = ruleset.Buildings.Values.Where(building =>
                building.Cost == -1 && techColumn.Techs.Select(it => it.Name).Contains(building.RequiredTech)).ToList();

            var nonWondersWithoutAssignedCost = buildingsWithoutAssignedCost.Where(it => !it.IsAnyWonder()).ToList();
            if (techColumn.BuildingCost == -1 && nonWondersWithoutAssignedCost.Count != 0)
                lines.Add(
                    $"Tech Column number {techColumn.ColumnNumber} has no explicit building cost leaving " + string.Join(", ", nonWondersWithoutAssignedCost) + " unassigned",
                    RulesetErrorSeverity.Warning, sourceObject: null);

            var wondersWithoutAssignedCost = buildingsWithoutAssignedCost.Where(it => it.IsAnyWonder()).ToList();
            if (techColumn.WonderCost == -1 && wondersWithoutAssignedCost.Count != 0)
                lines.Add(
                    $"Tech Column number {techColumn.ColumnNumber} has no explicit wonder cost leaving " + string.Join(", ", wondersWithoutAssignedCost) + " unassigned",
                    RulesetErrorSeverity.Warning, sourceObject: null);
        }
    }

    protected virtual void AddTerrainErrors(RulesetErrorList lines)
    {
        foreach (var terrain in ruleset.Terrains.Values)
            uniqueValidator.CheckUniques(terrain, lines, reportRulesetSpecificErrors);
    }

    protected virtual void AddUnitErrors(RulesetErrorList lines)
    {
        foreach (var unit in ruleset.Units.Values)
        {
            CheckUnit(unit, lines);
            uniqueValidator.CheckUniques(unit, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void CheckUnit(BaseUnit unit, RulesetErrorList lines)
    {
        foreach (string upgradesTo in unit.GetUpgradeUnits(GameContext.IgnoreConditionalsState))
        {
            if (upgradesTo == unit.Name || upgradesTo == unit.Replaces)
                lines.Add($"{unit.Name} upgrades to itself!", sourceObject: unit);
        }

        if (unit.Replaces != null && unit.UniqueTo == null)
            lines.Add($"{unit.Name} should replace {unit.Replaces} but does not have uniqueTo assigned!");
        if (unit.Replaces == unit.Name)
            lines.Add($"{unit.Name} replaces itself!");

        if (unit.IsMilitary && unit.Strength == 0) // Should only match ranged units with 0 strength
            lines.Add($"{unit.Name} is a military unit but has no assigned strength!", sourceObject: unit);

        // Kotlin Regex("TileSets/[^/]+/Units/${unit.name}") is not quoted, so the unit name is a pattern
        var pixelUnitTexturePattern = new Regex($"\\A(?:TileSets/[^/]+/Units/{unit.Name})\\z", RegexOptions.CultureInvariant);
        if (unit.CivilopediaText.Any(it => pixelUnitTexturePattern.IsMatch(it.ExtraImage)))
            lines.Add($"Unit {unit.Name} includes the unit's UnitSet art in civilopediaText, which is superseded by the \"Size of Unitset art in Civilopedia\" option", RulesetErrorSeverity.WarningOptionsOnly, unit);
    }

    private void CheckCivilopediaText(RulesetErrorList lines)
    {
        foreach (var sourceObject in ruleset.AllICivilopediaText())
        {
            var index = 0;
            foreach (var line in sourceObject.CivilopediaText)
            {
                index++;
                foreach (var error in line.UnsupportedReasons(ImageCheck))
                {
                    var nameText = sourceObject is INamed named ? named.Name + "'s " : "";
                    var text = $"({sourceObject.GetType().Name}) {nameText}civilopediaText line {index}: {error}";
                    lines.Add(text, RulesetErrorSeverity.WarningOptionsOnly, sourceObject as IRulesetObject, null);
                }
            }
        }
    }

    protected virtual void AddUnitTypeErrors(RulesetErrorList lines)
    {
        var unitMovementTypes = Enum.GetNames<UnitMovementType>().ToHashSet();
        foreach (var unitType in ruleset.UnitTypes.Values)
        {
            if (unitType.MovementType == null || !unitMovementTypes.Contains(unitType.MovementType))
                lines.Add($"Unit type {unitType.Name} has an invalid movement type {unitType.MovementType ?? "null"}", sourceObject: unitType);
            uniqueValidator.CheckUniques(unitType, lines, reportRulesetSpecificErrors);
        }
    }

    protected virtual void AddUnitNameGroupsErrors(RulesetErrorList lines)
    {
        foreach (var unitNameGroup in ruleset.UnitNameGroups.Values)
            uniqueValidator.CheckUniques(unitNameGroup, lines, reportRulesetSpecificErrors);
    }

    protected virtual void AddVictoryTypeErrors(RulesetErrorList lines)
    {
        foreach (var victoryType in ruleset.Victories.Values)
        {
            uniqueValidator.CheckUniques(victoryType, lines, reportRulesetSpecificErrors);

            foreach (var milestone in victoryType.MilestoneObjects)
            {
                if (milestone.Type == null)
                    lines.Add(
                        $"Victory type {victoryType.Name} has milestone \"{milestone.UniqueDescription}\" that is of an unknown type!",
                        RulesetErrorSeverity.Error, sourceObject: victoryType);
            }

            foreach (var otherVictory in ruleset.Victories.Values)
                if (string.CompareOrdinal(otherVictory.Name, victoryType.Name) > 0 && otherVictory.Milestones.SequenceEqual(victoryType.Milestones))
                    lines.Add(
                        $"Victory types {victoryType.Name} and {otherVictory.Name} have the same requirements!",
                        RulesetErrorSeverity.Warning, sourceObject: victoryType);
        }
    }

    private void AddTranslationNameCollisionWarnings(RulesetErrorList lines)
    {
        var translatableNames = ruleset.AllNames().ToList();

        var builtInRulesetNames = Enum.GetValues<BaseRuleset>().Select(it => it.FullName()).ToHashSet();
        // Base rulesets intentionally reuse a few display names in different source types:
        // "Scout" is both a BaseUnit and UnitType, "Settler" is a BaseUnit and Difficulty,
        // and some great person names appear in both UnitNameGroups and generated unit names.
        var knownBenignSourceCollisions = new List<HashSet<string>>
        {
            new() { "BaseUnit", "UnitType" },
            new() { "BaseUnit", "Difficulty" },
            new() { "BaseUnit", "Difficulty", "Tutorial" },
            new() { "BaseUnit", "UnitNameGroup" },
            new() { "Personality", "UnitNameGroup.unitNames" },
            new() { "Personality", "Nation.leaderName" },
            new() { "Specialist", "UnitNameGroup" },
        };

        var duplicateNames = translatableNames
            .GroupBy(it => it.Name)
            .Where(group => ShouldReportTranslationNameCollision(group.ToList(), builtInRulesetNames, knownBenignSourceCollisions))
            .Select(group => (Name: group.Key, Sources: group.Select(it => it.Source).Distinct().Order(StringComparer.Ordinal).ToList()))
            .OrderBy(it => it.Name, StringComparer.Ordinal) // toSortedMap
            .ToList();

        foreach (var (name, sources) in duplicateNames)
        {
            lines.Add(
                $"The name \"{name}\" is used by several ruleset entries ({string.Join(", ", sources)}) and may cause translation problems.",
                RulesetErrorSeverity.OK,
                sourceObject: null);
        }
    }

    private static bool ShouldReportTranslationNameCollision(
        List<RulesetName> names,
        IReadOnlySet<string> builtInRulesetNames,
        List<HashSet<string>> knownBenignSourceCollisions)
    {
        var sourceTypes = names.Select(it => it.Source).ToHashSet();
        if (sourceTypes.Count < 2) return false;

        var origins = names.Select(it => it.OriginRuleset).ToList();
        if (IsCollisionWithinSingleBuiltInRuleset(origins, builtInRulesetNames)) return false;
        if (IsKnownBenignBuiltInSourceCollision(sourceTypes, origins, builtInRulesetNames, knownBenignSourceCollisions)) return false;

        return true;
    }

    private static bool IsCollisionWithinSingleBuiltInRuleset(List<string> origins, IReadOnlySet<string> builtInRulesetNames)
    {
        if (origins.Any(it => it.Length == 0)) return false;
        var distinctOrigins = origins.ToHashSet();
        return distinctOrigins.Count == 1 && builtInRulesetNames.Contains(distinctOrigins.Single());
    }

    private static bool IsKnownBenignBuiltInSourceCollision(
        HashSet<string> sourceTypes,
        List<string> origins,
        IReadOnlySet<string> builtInRulesetNames,
        List<HashSet<string>> knownBenignSourceCollisions) =>
        knownBenignSourceCollisions.Any(it => it.SetEquals(sourceTypes))
        && origins.All(builtInRulesetNames.Contains);

    private void AddEmptyNamesErrors(RulesetErrorList lines)
    {
        var emptyNameObjects = ruleset.AllRulesetObjects()
            .Where(it => it.Name.Length == 0 && it is not EventChoice)
            .ToList();
        foreach (var obj in emptyNameObjects)
            lines.Add($"There's a {obj.GetType().Name} with an empty name in {obj.OriginRuleset}", RulesetErrorSeverity.Error, obj);
    }

    //endregion
    //region General helpers

    protected static List<string> GetPossibleMisspellings(string originalText, IEnumerable<string> possibleMisspellings) =>
        possibleMisspellings.Where(it =>
            TextSimilarity.GetRelativeTextDistance(it, originalText) <= RulesetCache.UniqueMisspellingThreshold).ToList();

    private static void CheckContrasts(EngineColor innerColor, EngineColor outerColor, RulesetObject nation, RulesetErrorList lines)
    {
        double contrastRatio = ColorContrast.GetContrastRatio(innerColor, outerColor);
        if (contrastRatio < 3) // https://www.w3.org/TR/WCAG20/#visual-audio-contrast-contrast
        {
            var (newInnerColor, newOuterColor) = GetSuggestedColors(innerColor, outerColor);

            string text = $"{nation.Name}'s colors do not contrast enough - it is unreadable!";
            text += "\nSuggested colors: ";
            text += $"\n\t\t\"outerColor\": [{(int)(newOuterColor.R * 255)}, {(int)(newOuterColor.G * 255)}, {(int)(newOuterColor.B * 255)}],";
            text += $"\n\t\t\"innerColor\": [{(int)(newInnerColor.R * 255)}, {(int)(newInnerColor.G * 255)}, {(int)(newInnerColor.B * 255)}],";

            lines.Add(text, RulesetErrorSeverity.WarningOptionsOnly, nation);
        }
    }

    // ImageGetter.CHARCOAL = Color(0x111111FF)
    private static readonly EngineColor Charcoal = ColorExtensions.ColorFromRGB(0x11, 0x11, 0x11);

    private static (EngineColor InnerColor, EngineColor OuterColor) GetSuggestedColors(EngineColor innerColor, EngineColor outerColor)
    {
        double innerColorLuminance = ColorContrast.GetRelativeLuminance(innerColor);
        double outerColorLuminance = ColorContrast.GetRelativeLuminance(outerColor);

        EngineColor innerLerpColor;
        EngineColor outerLerpColor;

        if (innerColorLuminance > outerColorLuminance) // inner is brighter
        {
            innerLerpColor = EngineColor.White;
            outerLerpColor = Charcoal;
        }
        else
        {
            innerLerpColor = Charcoal;
            outerLerpColor = EngineColor.White;
        }

        for (int i = 1; i <= 10; i++)
        {
            var newInnerColor = innerColor.Cpy().Lerp(innerLerpColor, 0.05f * i);
            var newOuterColor = outerColor.Cpy().Lerp(outerLerpColor, 0.05f * i);

            if (ColorContrast.GetContrastRatio(newInnerColor, newOuterColor) > 3) return (newInnerColor, newOuterColor);
        }
        throw new InvalidOperationException("Error getting suggested colors");
    }

    //endregion
    //region File related checks

    private void CheckFileNames(RulesetErrorList lines)
    {
        var folder = ruleset.FolderLocation;
        if (folder == null) return;

        CheckMisplacedJsonFiles(folder, lines);
        CheckMisspelledFolders(folder, lines);
        CheckImagesFolders(folder, lines);
        CheckUnknownJsonFilenames(folder, lines);
        WarnSplitAtlasesPerformanceDegradation(folder, lines);
    }

    private static void CheckMisspelledFolders(IRulesetFolder folder, RulesetErrorList lines)
    {
        string[] knownFolderNames = ["jsons", "maps", "sounds", "Images", "fonts"];
        foreach (var child in folder.List())
        {
            if (!child.IsDirectory || knownFolderNames.Contains(child.Name)) continue;

            var possibleMisspellings = GetPossibleMisspellings(child.Name, knownFolderNames);
            if (possibleMisspellings.Count != 0)
                lines.Add(
                    $"Folder \"{child.Name}\" is probably a misspelling of " + string.Join("/", possibleMisspellings),
                    RulesetErrorSeverity.OK);
        }
    }

    private static void CheckMisplacedJsonFiles(IRulesetFolder folder, RulesetErrorList lines)
    {
        foreach (var child in folder.List())
        {
            if (child.Name.EndsWith("json", StringComparison.Ordinal) && !child.Name.StartsWith("Atlas", StringComparison.Ordinal))
                lines.Add($"File {child.Name} is located in the root folder - it should be moved to a 'jsons' folder");
        }
    }

    // Portrait.Type.entries.map { it.directory } (Portrait.kt; the UI class is not ported)
    private static readonly string[] PortraitTypeDirectories =
        ["Unit", "Building", "Tech", "Resource", "Improvement", "UnitPromotion", "Unique", "Nation", "Religion", "UnitAction"];

    private static void CheckImagesFolders(IRulesetFolder folder, RulesetErrorList lines)
    {
        var knownImageFolders = PortraitTypeDirectories.SelectMany(it => new[] { it + "Icons", it + "Portraits" })
            .Concat(["CityStateIcons", "PolicyBranchIcons", "PolicyIcons", "OtherIcons", "EmojiIcons", "StatIcons", "TileIcons", "TileSets"]) // Not portrait-able (yet?)
            .ToList();

        var imageFolders = folder.List().Where(it => it.Name.StartsWith("Images", StringComparison.Ordinal)).ToList();
        foreach (var imageFolder in imageFolders)
        {
            foreach (var child in imageFolder.List())
            {
                if (!child.IsDirectory)
                {
                    lines.Add(
                        $"File \"{imageFolder.Name}/{child.Name}\" is misplaced - Images folders should not contain any files directly - only subfolders",
                        RulesetErrorSeverity.OK);
                }
                else if (!knownImageFolders.Contains(child.Name))
                {
                    var possibleMisspellings = GetPossibleMisspellings(child.Name, knownImageFolders);
                    if (possibleMisspellings.Count != 0)
                        lines.Add(
                            $"Folder \"{imageFolder.Name}/{child.Name}\" is probably a misspelling of " +
                            string.Join("/", possibleMisspellings),
                            RulesetErrorSeverity.OK);
                }
            }
        }
    }

    private static void CheckUnknownJsonFilenames(IRulesetFolder folder, RulesetErrorList lines)
    {
        var jsonFolder = folder.Child("jsons");
        if (!jsonFolder.Exists) return;

        var rulesetFileNames = RulesetFile.Values.Select(it => it.Filename).ToList();
        foreach (var file in jsonFolder.List().Where(it => it.Name.EndsWith(".json", StringComparison.Ordinal)))
        {
            if (rulesetFileNames.Contains(file.Name)) continue;

            string text = $"File {file.Name} is in the jsons folder but is not a recognized ruleset file";
            var possibleMisspellings = GetPossibleMisspellings(file.Name, rulesetFileNames);
            if (possibleMisspellings.Count != 0)
                text += "\nPossible misspelling of: " + string.Join("/", possibleMisspellings);
            lines.Add(text, RulesetErrorSeverity.OK);
        }
    }

    private static void WarnSplitAtlasesPerformanceDegradation(IRulesetFolder folder, RulesetErrorList lines)
    {
        if (folder.Child("game2.png").Exists)
        {
            lines.Add(
                "Your images are being generated into multiple atlas files - this can cause lag for players. " +
                "Please consult https://yairm210.github.io/Unciv/Modders/Images-and-Audio/#rendering-performance on how to improve rendering performance.",
                RulesetErrorSeverity.WarningOptionsOnly, sourceObject: null);
        }
    }

    //endregion

    private void CheckFreeBuildingPossibleRecursions(RulesetErrorList lines)
    {
        LinkedHashMap<string, List<UniqueObject>> GetBuildingIndex(UniqueType type) =>
            GroupByPair(ruleset.AllUniques().Where(it => it.Type == type).Select(it => (it.Params[0], it)));

        static string DisplayUniques(List<UniqueObject> uniques) =>
            string.Join(", ", uniques.Select(it =>
                $"\"{it.Text}\"" +
                (it.SourceObjectType == null ? "" : $" on {it.GetSourceNameForUser()} \"{it.SourceObjectName}\"")));

        const string suppressorKey = "is both granted free and could possibly be removed by triggers";
        static string GetSuppressor() => UniqueType.SuppressWarnings.PlaceholderText().FillPlaceholders(suppressorKey);

        // Map of building **names** that are granted free via triggerable anywhere to a List of source Uniques
        var allFreeBuildings = GetBuildingIndex(UniqueType.GainFreeBuildings);
        // Map of building **filters** that are removed via triggerable anywhere to a List of source Uniques
        var allRemovals = GetBuildingIndex(UniqueType.RemoveBuilding);
        // Possible sources of infinite recursion: Map of buildingName to a list of all possibly removing Uniques
        var possibleRecursions = GroupByPair(allFreeBuildings.Keys
            .Where(it => ruleset.Buildings.ContainsKey(it)) // The names can still be non-existing, which is checked elsewhere
            .Select(name => (Name: name, Building: ruleset.Buildings[name]))
            .SelectMany(entry => allRemovals.Keys.SelectMany(filter =>
                // Double flatMap since more than one filter can match the building, and we want all removal Uniques in a single List
                allRemovals[filter].Where(_ => entry.Building.MatchesFilter(filter)).Select(removal => (entry.Name, removal)))));

        // Output
        foreach (var (buildingName, removals) in possibleRecursions)
        {
            var grants = allFreeBuildings[buildingName];
            string text = $"Building \"{buildingName}\" {suppressorKey}.\n" +
                "This can lead to infinite recursion if care is not taken that conditionals are mutually exclusive.\n" +
                $"Grants: {DisplayUniques(grants)},\n" +
                $"Possible removals: {DisplayUniques(removals)}\n" +
                $"If you're *SURE* this is safe, add the unique \"{GetSuppressor()}\" to the building.";
            lines.Add(text, RulesetErrorSeverity.WarningOptionsOnly, ruleset.Buildings[buildingName], grants.FirstOrDefault());
        }
    }

    /// <summary>Kotlin <c>groupBy({ it.first }, { it.second })</c>: groups and values keep encounter order.</summary>
    private static LinkedHashMap<K, List<V>> GroupByPair<K, V>(IEnumerable<(K, V)> pairs) where K : notnull
    {
        var result = new LinkedHashMap<K, List<V>>();
        foreach (var (key, value) in pairs)
        {
            if (!result.TryGetValue(key, out var list))
            {
                list = [];
                result[key] = list;
            }
            list.Add(value);
        }
        return result;
    }
}

public static class RulesetValidatorExtensions
{
    /// <summary>Kotlin <c>Ruleset.getErrorList</c></summary>
    public static RulesetErrorList GetErrorList(this Ruleset ruleset, bool tryFixUnknownUniques = false) =>
        RulesetValidator.Create(ruleset, tryFixUnknownUniques).GetErrorList();
}
