using System.Collections.Immutable;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/BaseRulesetValidator.kt

/// <summary>This implementation of <see cref="RulesetValidator"/> can rely on the ruleset being complete.</summary>
internal sealed class BaseRulesetValidator : RulesetValidator
{
    // for UnitTypes fallback, used only if a mod fails to provide
    // the UnitTypes entirely (ruleset.unitTypes.isEmpty() is true).
    private readonly KotlinLazy<Ruleset> vanillaRuleset = new(RulesetCache.GetVanillaRuleset);

    /// <summary>Collects known technology prerequisite paths: key is the technology name, value a Set of its prerequisites including indirect ones</summary>
    private readonly Dictionary<string, HashSet<string>> prereqsHashMap = [];

    private readonly KotlinLazy<bool> anyAncientRuins;

    // CityStatePersonality.entries names
    private static readonly ImmutableArray<string> CityStatePersonalities = [.. Enum.GetNames<CityStatePersonality>()];

    public BaseRulesetValidator(Ruleset ruleset, bool tryFixUnknownUniques) : base(ruleset, tryFixUnknownUniques)
    {
        anyAncientRuins = new KotlinLazy<bool>(() =>
            ruleset.TileImprovements.Values
                .SelectMany(it => ((IHasUniques)it).UniqueObjects)
                .Any(it => it.Type == UniqueType.IsAncientRuinsEquivalent));

        // The `UniqueValidator.checkUntypedUnique` filtering Unique test ("X not found in Unciv's unique types, and is not used as a filtering unique")
        // should not complain when running the RulesetInvariant version, because an Extension Mod may e.g. define additional "Aircraft" and the _use_ of the
        // filtering Unique only exists in the Base Ruleset. But here we *do* want the test, and it needs its cache filled, and that is not done automatically.
        uniqueValidator.PopulateFilteringUniqueHashsets();
    }

    protected override void CheckBuilding(Building building, RulesetErrorList lines)
    {
        base.CheckBuilding(building, lines);

        foreach (var (gppName, _) in building.GreatPersonPoints)
            if (!ruleset.Units.ContainsKey(gppName))
                lines.Add(
                    $"Building {building.Name} has greatPersonPoints for {gppName}, which is not a unit in the ruleset!",
                    RulesetErrorSeverity.Warning, building);
        foreach (string requiredTech in ((IHasUniques)building).RequiredTechs())
            if (!ruleset.Technologies.ContainsKey(requiredTech))
                lines.Add($"{building.Name} requires tech {requiredTech} which does not exist!", sourceObject: building);
        foreach (string specialistName in building.SpecialistSlots.Keys)
            if (!ruleset.Specialists.ContainsKey(specialistName))
                lines.Add($"{building.Name} provides specialist {specialistName} which does not exist!", sourceObject: building);
        foreach (string resource in building.GetResourceRequirementsPerTurn(GameContext.IgnoreConditionalsState).Keys)
            if (!ruleset.TileResources.ContainsKey(resource))
                lines.Add($"{building.Name} requires resource {resource} which does not exist!", sourceObject: building);
        if (building.Replaces != null && !ruleset.Buildings.ContainsKey(building.Replaces))
            lines.Add($"{building.Name} replaces {building.Replaces} which does not exist!", sourceObject: building);
        if (building.RequiredBuilding != null && !ruleset.Buildings.ContainsKey(building.RequiredBuilding))
            lines.Add($"{building.Name} requires {building.RequiredBuilding} which does not exist!", sourceObject: building);

        CheckUniqueToMisspelling(building, building.UniqueTo, lines);
        CheckGreatWorkSlots(building, lines);
    }

    private static void CheckGreatWorkSlots(Building building, RulesetErrorList lines)
    {
        if (building.GreatWorkSlots < 0)
            lines.Add($"{building.Name} has a negative number of greatWorkSlots!", sourceObject: building);
        else if (building.GreatWorkSlots > 0 && building.GreatWorkSlotType == null)
            lines.Add($"{building.Name} has greatWorkSlots but no greatWorkSlotType!", sourceObject: building);
        else if (building.GreatWorkSlots == 0 && building.GreatWorkSlotType != null)
            lines.Add($"{building.Name} has a greatWorkSlotType but no greatWorkSlots!", RulesetErrorSeverity.Warning, building);
        if (building.ThemingBonuses.Count != 0 && building.GreatWorkSlots == 0)
            lines.Add($"{building.Name} has themingBonuses but no greatWorkSlots, so they can never apply!", RulesetErrorSeverity.Warning, building);
    }

    protected override void AddDifficultyErrors(RulesetErrorList lines)
    {
        base.AddDifficultyErrors(lines);

        // A Difficulty is not a IHasUniques, so not suitable as sourceObject
        foreach (var difficulty in ruleset.Difficulties.Values)
        {
            foreach (string unitName in difficulty.AiCityStateBonusStartingUnits.Concat(difficulty.AiMajorCivBonusStartingUnits).Concat(difficulty.PlayerBonusStartingUnits))
                if (unitName != Constants.EraSpecificUnit && !ruleset.Units.ContainsKey(unitName))
                    lines.Add($"Difficulty {difficulty.Name} contains starting unit {unitName} which does not exist!", sourceObject: null);
            if (difficulty.AiDifficultyLevel != null && !ruleset.Difficulties.ContainsKey(difficulty.AiDifficultyLevel))
                lines.Add($"Difficulty {difficulty.Name} contains aiDifficultyLevel {difficulty.AiDifficultyLevel} which does not exist!",
                    RulesetErrorSeverity.Warning, sourceObject: null);
            foreach (string tech in difficulty.AiFreeTechs)
                if (!ruleset.Technologies.ContainsKey(tech))
                    lines.Add($"Difficulty {difficulty.Name} contains AI free tech {tech} which does not exist!", sourceObject: null);
        }
    }

    protected override void AddEraErrors(RulesetErrorList lines)
    {
        base.AddEraErrors(lines);

        if (ruleset.Eras.Count == 0)
            lines.Add("Eras file is empty! This will likely lead to crashes. Ask the mod maker to update this mod!", sourceObject: null);

        var allDifficultiesStartingUnits = new HashSet<string>();
        foreach (var difficulty in ruleset.Difficulties.Values)
        {
            allDifficultiesStartingUnits.UnionWith(difficulty.AiCityStateBonusStartingUnits);
            allDifficultiesStartingUnits.UnionWith(difficulty.AiMajorCivBonusStartingUnits);
            allDifficultiesStartingUnits.UnionWith(difficulty.PlayerBonusStartingUnits);
        }

        foreach (var era in ruleset.Eras.Values)
        {
            foreach (string wonder in era.StartingObsoleteWonders)
                if (!ruleset.Buildings.ContainsKey(wonder))
                    lines.Add($"Nonexistent wonder {wonder} obsoleted when starting in {era.Name}!", sourceObject: era);
            foreach (string building in era.SettlerBuildings)
                if (!ruleset.Buildings.ContainsKey(building))
                    lines.Add($"Nonexistent building {building} built by settlers when starting in {era.Name}", sourceObject: era);
            // todo the whole 'starting unit' thing needs to be redone, there's no reason we can't have a single list containing all the starting units.
            if (!ruleset.Units.ContainsKey(era.StartingSettlerUnit) && !ruleset.Units.Values.Any(it => it.IsCityFounder()))
                lines.Add($"Nonexistent unit {era.StartingSettlerUnit} marked as starting unit when starting in {era.Name}", sourceObject: era);
            if (era.StartingWorkerCount != 0 && !ruleset.Units.ContainsKey(era.StartingWorkerUnit)
                && !ruleset.Units.Values.Any(it => ((IHasUniques)it).HasUnique(UniqueType.BuildImprovements)))
                lines.Add($"Nonexistent unit {era.StartingWorkerUnit} marked as starting unit when starting in {era.Name}", sourceObject: era);

            bool grantsStartingMilitaryUnit = era.StartingMilitaryUnitCount != 0
                || allDifficultiesStartingUnits.Contains(Constants.EraSpecificUnit);
            if (grantsStartingMilitaryUnit && !ruleset.Units.ContainsKey(era.StartingMilitaryUnit))
                lines.Add($"Nonexistent unit {era.StartingMilitaryUnit} marked as starting unit when starting in {era.Name}", sourceObject: era);
        }
    }

    protected override void AddImprovementErrors(RulesetErrorList lines)
    {
        base.AddImprovementErrors(lines);

        foreach (var improvement in ruleset.TileImprovements.Values)
        {
            if (improvement.TechRequired != null && !ruleset.Technologies.ContainsKey(improvement.TechRequired))
                lines.Add($"{improvement.Name} requires tech {improvement.TechRequired} which does not exist!", sourceObject: improvement);
            if (improvement.Replaces != null && !ruleset.TileImprovements.ContainsKey(improvement.Replaces))
                lines.Add($"{improvement.Name} replaces {improvement.Replaces} which does not exist!", sourceObject: improvement);

            CheckUniqueToMisspelling(improvement, improvement.UniqueTo, lines);

            foreach (string terrain in improvement.TerrainsCanBeBuiltOn)
                if (!ruleset.Terrains.ContainsKey(terrain) && terrain != "Land" && terrain != "Water")
                    lines.Add($"{improvement.Name} can be built on terrain {terrain} which does not exist!", sourceObject: improvement);
        }
    }

    protected override void AddModOptionsErrors(RulesetErrorList lines)
    {
        base.AddModOptionsErrors(lines);

        // `ruleset` can be a true base ruleset or a combined one when we're checking an extension mod together with a base.
        // In the combined case, don't complain about ModRequires!
        if (ruleset.Name.Length == 0 && ruleset.Mods.Count > 1) return;

        foreach (var unique in ((IHasUniques)ruleset.ModOptions).GetMatchingUniques(UniqueType.ModRequires))
            lines.Add($"Mod option '{unique.Text}' is invalid for a base ruleset.", sourceObject: null);
    }

    protected override void CheckNation(NationObject nation, RulesetErrorList lines)
    {
        base.CheckNation(nation, lines);
        if (nation.PreferredVictoryType != Constants.NeutralVictoryType && !ruleset.Victories.ContainsKey(nation.PreferredVictoryType))
            lines.Add($"{nation.Name}'s preferredVictoryType is {nation.PreferredVictoryType} which does not exist!", sourceObject: nation);
        if (nation.CityStateType != null && !ruleset.CityStateTypes.ContainsKey(nation.CityStateType))
            lines.Add($"{nation.Name} is of city-state type {nation.CityStateType} which does not exist!", sourceObject: nation);
        if (nation.FavoredReligion != null && !ruleset.Religions.Contains(nation.FavoredReligion))
            lines.Add($"{nation.Name} has {nation.FavoredReligion} as their favored religion, which does not exist!", sourceObject: nation);
        if (nation.Personality != null)
        {
            bool unknownPersonality = nation.IsCityState
                ? !CityStatePersonalities.Contains(nation.Personality)
                : !ruleset.Personalities.ContainsKey(nation.Personality);
            if (unknownPersonality)
                lines.Add($"{nation.Name} has personality {nation.Personality}, which does not exist!", sourceObject: nation);
        }
    }

    protected override void AddPersonalityErrors(RulesetErrorList lines)
    {
        base.AddPersonalityErrors(lines);

        foreach (var personality in ruleset.Personalities.Values)
        {
            if (personality.PreferredVictoryType != Constants.NeutralVictoryType
                && !ruleset.Victories.ContainsKey(personality.PreferredVictoryType))
                lines.Add($"Preferred victory type {personality.PreferredVictoryType} does not exist in ruleset",
                    RulesetErrorSeverity.Warning, sourceObject: personality);
        }
    }

    protected override void AddPolicyErrors(RulesetErrorList lines)
    {
        base.AddPolicyErrors(lines);

        foreach (var policy in ruleset.Policies.Values)
        {
            foreach (string prereq in policy.Requires ?? [])
                if (!ruleset.Policies.ContainsKey(prereq))
                    lines.Add($"{policy.Name} requires policy {prereq} which does not exist!", sourceObject: policy);
        }

        foreach (var branch in ruleset.PolicyBranches.Values)
        {
            if (!ruleset.Eras.ContainsKey(branch.Era))
                lines.Add($"{branch.Name} requires era {branch.Era} which does not exist!", sourceObject: branch);

            var policyLocations = new Dictionary<string, Policy>();
            foreach (var policy in branch.Policies)
            {
                string policyLocation = $"{policy.Row}/{policy.Column}";

                if (!policyLocations.TryGetValue(policyLocation, out var existingPolicyInLocation))
                    policyLocations[policyLocation] = policy;
                else
                    lines.Add($"Policies {policy.Name} and {existingPolicyInLocation.Name} in branch {branch.Name}" +
                        $" are both located at column {policy.Column} row {policy.Row}!", sourceObject: policy);
            }
        }

        foreach (var policy in ruleset.PolicyBranches.Values.SelectMany(it => it.Policies.Concat([(Policy)it])))
            if (!ReferenceEquals(policy, ruleset.Policies.GetValueOrDefault(policy.Name)))
                lines.Add($"More than one policy with the name {policy.Name} exists!", sourceObject: policy);
    }

    protected override void AddEventErrors(RulesetErrorList lines)
    {
        base.AddEventErrors(lines);
        CheckEventCircularTriggers(lines);
    }

    private void CheckEventCircularTriggers(RulesetErrorList lines)
    {
        static bool IsHumanOnlyModifier(UniqueObject unique) =>
            unique.Type == UniqueType.ConditionalCivFilter && unique.Params[0] == Constants.HumanPlayer;
        static bool IsAiOnlyModifier(UniqueObject unique) =>
            unique.Type == UniqueType.ConditionalCivFilter && unique.Params[0] == Constants.AiPlayer;

        // A choice is unreachable for AI if:
        // - It has an unconditional AiChoiceWeight of -100% (or worse) -> effective weight <= 0
        // - It has OnlyAvailable with <for [Human player] Civilizations> -> AI never satisfies this
        // - It has Unavailable with only <for [AI player] Civilizations> -> always blocks AI
        static bool IsChoiceUnreachableForAI(EventChoice choice)
        {
            float weight = 1f;
            foreach (var unique in choice.UniqueObjects)
            {
                if (unique.Type != UniqueType.AiChoiceWeight) continue;
                if (unique.Modifiers.Count != 0) continue; // skip conditional weights
                weight *= 1 + UniqueParameterTypeExtensions.ToFloatOrNull(unique.Params[0]).NotNull() / 100;
            }
            if (weight <= 0f) return true;

            if (choice.UniqueObjects.Any(unique =>
                    unique.Type == UniqueType.OnlyAvailable &&
                    unique.Modifiers.Any(IsHumanOnlyModifier))) return true;

            if (choice.UniqueObjects.Any(unique =>
                    unique.Type == UniqueType.Unavailable &&
                    unique.Modifiers.Count == 1 && IsAiOnlyModifier(unique.Modifiers[0]))) return true;

            return false;
        }

        // A TriggerEvent unique is unreachable for AI if it has <for [Human player] Civilizations>,
        // since conditionalsApply will always be false for an AI civ.
        static bool IsTriggerUnreachableForAI(UniqueObject unique) =>
            unique.Modifiers.Any(IsHumanOnlyModifier);

        // history is a java.util.HashSet in Kotlin and its iteration order feeds the message, hence JavaHashSet
        void RecursiveCheck(JavaHashSet<string> history, string eventName, int level)
        {
            if (history.Contains(eventName))
            {
                lines.Add(
                    $"Circular trigger in Events: {string.Join("→", history)}→{eventName} - will cause an infinite loop for AI!",
                    RulesetErrorSeverity.Warning,
                    sourceObject: ruleset.Events.GetValueOrDefault(eventName));
                return;
            }
            if (level > 99) return;
            var @event = ruleset.Events.GetValueOrDefault(eventName);
            if (@event == null) return;
            var triggerEventUniques = @event.Choices
                .Where(it => !IsChoiceUnreachableForAI(it))
                .SelectMany(choice => choice.UniqueObjects.Where(it =>
                    it.Type == UniqueType.TriggerEvent && !IsTriggerUnreachableForAI(it)))
                .ToList();
            if (triggerEventUniques.Count == 0) return;
            history.Add(eventName);
            foreach (var unique in triggerEventUniques)
            {
                var linkedSetToPass = triggerEventUniques.Count == 1 ? history : JavaHashSet<string>.ToHashSet(history);
                RecursiveCheck(linkedSetToPass, unique.Params[0], level + 1);
            }
        }

        foreach (string eventName in ruleset.Events.Keys)
            RecursiveCheck(new JavaHashSet<string>(), eventName, 0);
    }

    protected override void AddPromotionErrors(RulesetErrorList lines)
    {
        base.AddPromotionErrors(lines);
        CheckPromotionCircularReferences(lines);
    }

    protected override void CheckPromotion(Promotion promotion, RulesetErrorList lines)
    {
        base.CheckPromotion(promotion, lines);

        foreach (string prereq in promotion.Prerequisites)
            if (!ruleset.UnitPromotions.ContainsKey(prereq))
                lines.Add(
                    $"{promotion.Name} requires promotion {prereq} which does not exist!",
                    RulesetErrorSeverity.ErrorOptionsOnly, promotion);
        foreach (string unitType in promotion.UnitTypes)
            CheckUnitType(unitType, () => lines.Add(
                $"{promotion.Name} references unit type {unitType}, which does not exist!",
                RulesetErrorSeverity.ErrorOptionsOnly, promotion));
    }

    private void CheckPromotionCircularReferences(RulesetErrorList lines)
    {
        var promotionsUnlockedByPrerequisite = new Dictionary<string, List<Promotion>>();
        foreach (var promotion in ruleset.UnitPromotions.Values)
        {
            foreach (string prerequisiteName in promotion.Prerequisites)
            {
                if (!promotionsUnlockedByPrerequisite.TryGetValue(prerequisiteName, out var unlocked))
                {
                    unlocked = [];
                    promotionsUnlockedByPrerequisite[prerequisiteName] = unlocked;
                }
                unlocked.Add(promotion);
            }
        }

        // Promotion has reference identity in Kotlin (no equals override)
        var promotionsWithAcyclicPathToRoot = new HashSet<Promotion>(ReferenceEqualityComparer.Instance);
        var promotionsToCheck = new Queue<Promotion>();
        foreach (var promotion in ruleset.UnitPromotions.Values)
        {
            if (promotion.Prerequisites.Count != 0) continue;
            promotionsWithAcyclicPathToRoot.Add(promotion);
            promotionsToCheck.Enqueue(promotion);
        }

        while (promotionsToCheck.Count != 0)
        {
            var promotion = promotionsToCheck.Dequeue();
            foreach (var unlockedPromotion in promotionsUnlockedByPrerequisite.GetValueOrDefault(promotion.Name) ?? [])
            {
                if (!promotionsWithAcyclicPathToRoot.Add(unlockedPromotion)) continue;
                promotionsToCheck.Enqueue(unlockedPromotion);
            }
        }

        var promotionsInAlreadyReportedCycles = new HashSet<Promotion>(ReferenceEqualityComparer.Instance);
        var promotionsAlreadyChecked = new HashSet<Promotion>(ReferenceEqualityComparer.Instance);

        List<Promotion>? FindCircularReference(List<Promotion> history, Promotion promotion)
        {
            int existingIndex = history.FindIndex(it => ReferenceEquals(it, promotion));
            if (existingIndex >= 0) return history.Skip(existingIndex).Append(promotion).ToList();
            if (promotionsAlreadyChecked.Contains(promotion)) return null;
            if (promotionsWithAcyclicPathToRoot.Contains(promotion)) return null;

            var newHistory = history.Append(promotion).ToList();
            foreach (string prerequisiteName in promotion.Prerequisites)
            {
                var prerequisite = ruleset.UnitPromotions.GetValueOrDefault(prerequisiteName);
                if (prerequisite == null) continue;
                var circularReference = FindCircularReference(newHistory, prerequisite);
                if (circularReference != null) return circularReference;
            }

            promotionsAlreadyChecked.Add(promotion);
            return null;
        }

        foreach (var promotion in ruleset.UnitPromotions.Values)
        {
            if (promotionsWithAcyclicPathToRoot.Contains(promotion)) continue;
            if (promotionsInAlreadyReportedCycles.Contains(promotion)) continue;

            var circularReference = FindCircularReference([], promotion);
            if (circularReference == null) continue;
            promotionsInAlreadyReportedCycles.UnionWith(circularReference);
            lines.Add($"Circular Reference in Promotions: {string.Join("→", circularReference.Select(it => it.Name))}",
                RulesetErrorSeverity.Warning, promotion);
        }
    }

    protected override void AddResourceErrors(RulesetErrorList lines)
    {
        foreach (var resource in ruleset.TileResources.Values)
        {
            if (resource.RevealedBy != null && !ruleset.Technologies.ContainsKey(resource.RevealedBy))
                lines.Add($"{resource.Name} revealed by tech {resource.RevealedBy} which does not exist!", sourceObject: resource);
            if (resource.Improvement != null && !ruleset.TileImprovements.ContainsKey(resource.Improvement))
                lines.Add($"{resource.Name} improved by improvement {resource.Improvement} which does not exist!", sourceObject: resource);
            foreach (string improvement in resource.ImprovedBy)
                if (!ruleset.TileImprovements.ContainsKey(improvement))
                    lines.Add($"{resource.Name} improved by improvement {improvement} which does not exist!", sourceObject: resource);
            foreach (string terrain in resource.TerrainsCanBeFoundOn)
                if (!ruleset.Terrains.ContainsKey(terrain))
                    lines.Add($"{resource.Name} can be found on terrain {terrain} which does not exist!", sourceObject: resource);
        }
        base.AddResourceErrors(lines);
    }

    protected override void AddRuinsErrors(RulesetErrorList lines)
    {
        base.AddRuinsErrors(lines);

        foreach (var reward in ruleset.RuinRewards.Values)
        {
            foreach (string difficulty in reward.ExcludedDifficulties)
                if (!ruleset.Difficulties.ContainsKey(difficulty))
                    lines.Add($"{reward.Name} references difficulty {difficulty}, which does not exist!", sourceObject: reward);
        }

        // brainstorm: What additional constraints apply for rulesets without any ruins?
        // * Any UniqueType.RuinsUpgrade is not OK -> See UniqueValidator.addUniqueTypeSpecificErrors
        // * Any RuinRewards is not OK
        // * Conversely, No ruinRewards is only OK for such rulesets
        if (anyAncientRuins.Value && ruleset.RuinRewards.Count == 0)
            lines.Add("There are Ancient Ruins or equivalents, but not RuinRewards", sourceObject: null);
        if (!anyAncientRuins.Value && ruleset.RuinRewards.Count != 0)
            lines.Add("There are RuinRewards, but no Ancient Ruins or equivalents", RulesetErrorSeverity.Warning, sourceObject: null);
    }

    protected override void AddSpecialistErrors(RulesetErrorList lines)
    {
        base.AddSpecialistErrors(lines);

        // Specialist is not a IHasUniques and unsuitable as sourceObject
        foreach (var specialist in ruleset.Specialists.Values)
        {
            foreach (var (gppName, _) in specialist.GreatPersonPoints)
                if (!ruleset.Units.ContainsKey(gppName))
                    lines.Add(
                        $"Specialist {specialist.Name} has greatPersonPoints for {gppName}, which is not a unit in the ruleset!",
                        RulesetErrorSeverity.Warning, sourceObject: null);
        }
        CheckGreatWorks(lines);
    }

    private void CheckGreatWorks(RulesetErrorList lines)
    {
        // GreatWork is not a IHasUniques and unsuitable as sourceObject
        foreach (var work in ruleset.GreatWorks.Values)
            if (work.Era.Length != 0 && !ruleset.Eras.ContainsKey(work.Era))
                lines.Add($"Great Work {work.Name} is from era {work.Era}, which is not an era in the ruleset!", sourceObject: null);
    }

    protected override void AddTechErrors(RulesetErrorList lines)
    {
        base.AddTechErrors(lines);

        var numbers = ruleset.TechColumns.Select(it => it.ColumnNumber).ToList();
        if (!numbers.OrderBy(it => it).SequenceEqual(numbers))
            lines.Add($"Tech columns should be ordered. Actual: [{string.Join(", ", numbers)}]", RulesetErrorSeverity.ErrorOptionsOnly);
        foreach (var column in ruleset.TechColumns)
        {
            if (column.Techs.Count == 0)
                lines.Add($"Tech column {column.ColumnNumber} is empty");
            if (!ruleset.Eras.ContainsKey(column.Era))
            {
                var tech = column.Techs.FirstOrDefault();
                if (tech == null)
                    lines.Add($"Unknown era {column.Era} referenced in tech column {column.ColumnNumber}");
                else
                    lines.Add($"Unknown era {column.Era} referenced in tech column {column.ColumnNumber} of tech {tech.Name}", sourceObject: tech);
            }
        }

        foreach (var tech in ruleset.Technologies.Values)
        {
            foreach (string prereq in tech.Prerequisites)
            {
                if (!ruleset.Technologies.ContainsKey(prereq))
                    lines.Add($"{tech.Name} requires tech {prereq} which does not exist!", sourceObject: tech);

                if (tech.Prerequisites.Any(it => it != prereq && GetPrereqTree(it).Contains(prereq)))
                {
                    lines.Add(
                        $"No need to add {prereq} as a prerequisite of {tech.Name} - it is already implicit from the other prerequisites!",
                        RulesetErrorSeverity.WarningOptionsOnly, tech);
                }

                if (GetPrereqTree(prereq).Contains(tech.Name))
                    lines.Add($"Techs {tech.Name} and {prereq} require each other!", sourceObject: tech);
            }

            foreach (var otherTech in ruleset.Technologies.Values)
            {
                if (string.CompareOrdinal(tech.Name, otherTech.Name) > 0 && otherTech.Column?.ColumnNumber == tech.Column?.ColumnNumber && otherTech.Row == tech.Row)
                    lines.Add($"{tech.Name} is in the same row and column as {otherTech.Name}!", sourceObject: tech);
            }
        }
    }

    protected override void AddTerrainErrors(RulesetErrorList lines)
    {
        if (!ruleset.Terrains.Values.Any(it => it.Type == TerrainType.Land && !it.Impassable && !((IHasUniques)it).HasUnique(UniqueType.NoNaturalGeneration)))
            lines.Add("No passable land terrains exist!", sourceObject: null);

        foreach (var terrain in ruleset.Terrains.Values)
        {
            if (terrain.Type is TerrainType.Land or TerrainType.Water && terrain.OccursOn.Count != 0)
                lines.Add($"{terrain.Name} is a base terrain but has occursOn set - this is unsupported.", RulesetErrorSeverity.WarningOptionsOnly, terrain);
            foreach (string baseTerrainName in terrain.OccursOn)
            {
                var baseTerrain = ruleset.Terrains.GetValueOrDefault(baseTerrainName);
                if (baseTerrain == null)
                    lines.Add($"{terrain.Name} occurs on terrain {baseTerrainName} which does not exist!", sourceObject: terrain);
                else if (baseTerrain.Type == TerrainType.NaturalWonder)
                    lines.Add($"{terrain.Name} occurs on natural wonder {baseTerrainName}: Unsupported.", RulesetErrorSeverity.WarningOptionsOnly, terrain);
            }
            if (terrain.Type == TerrainType.NaturalWonder && terrain.TurnsInto != null)
            {
                var baseTerrain = ruleset.Terrains.GetValueOrDefault(terrain.TurnsInto);
                if (baseTerrain == null)
                    lines.Add($"{terrain.Name} turns into terrain {terrain.TurnsInto} which does not exist!", sourceObject: terrain);
                else if (!baseTerrain.Type.IsBaseTerrain())
                    // See https://github.com/hackedpassword/Z2/blob/main/HybridTileTech.md for a clever exploit
                    lines.Add($"{terrain.Name} turns into terrain {terrain.TurnsInto} which is not a base terrain!", RulesetErrorSeverity.Warning, terrain);
            }
        }

        base.AddTerrainErrors(lines);
    }

    protected override void AddUnitErrors(RulesetErrorList lines)
    {
        if (!ruleset.Units.Values.Any(it => it.IsCityFounder()))
            lines.Add("No city-founding units in ruleset!", sourceObject: null);
        base.AddUnitErrors(lines);
    }

    /// <summary>Collects all RulesetSpecific checks for a BaseUnit</summary>
    protected override void CheckUnit(BaseUnit unit, RulesetErrorList lines)
    {
        base.CheckUnit(unit, lines);

        foreach (string requiredTech in ((IHasUniques)unit).RequiredTechs())
            if (!ruleset.Technologies.ContainsKey(requiredTech))
                lines.Add($"{unit.Name} requires tech {requiredTech} which does not exist!", sourceObject: unit);
        foreach (string obsoleteTech in unit.TechsAtWhichNoLongerAvailable())
            if (!ruleset.Technologies.ContainsKey(obsoleteTech))
                lines.Add($"{unit.Name} obsoletes at tech {obsoleteTech} which does not exist!", sourceObject: unit);
        foreach (string upgradesTo in unit.GetUpgradeUnits(GameContext.IgnoreConditionalsState))
            if (!ruleset.Units.ContainsKey(upgradesTo))
                lines.Add($"{unit.Name} upgrades to unit {upgradesTo} which does not exist!", sourceObject: unit);

        // Check that we don't obsolete ourselves before we can upgrade
        foreach (string obsoleteTech in unit.TechsAtWhichAutoUpgradeInProduction())
        {
            foreach (string upgradesTo in unit.GetUpgradeUnits(GameContext.IgnoreConditionalsState))
            {
                if (!ruleset.Units.ContainsKey(upgradesTo)) continue;
                if (!ruleset.Technologies.ContainsKey(obsoleteTech)) continue;
                var upgradedUnit = ruleset.Units[upgradesTo];
                foreach (string requiredTech in ((IHasUniques)upgradedUnit).RequiredTechs())
                    if (requiredTech != obsoleteTech && !GetPrereqTree(obsoleteTech).Contains(requiredTech))
                        lines.Add(
                            $"{unit.Name} is supposed to automatically upgrade at tech {obsoleteTech}," +
                            $" and therefore {requiredTech} for its upgrade {upgradedUnit.Name} may not yet be researched!",
                            RulesetErrorSeverity.Warning, unit);
            }
        }

        foreach (string resource in unit.GetResourceRequirementsPerTurn(GameContext.IgnoreConditionalsState).Keys)
            if (!ruleset.TileResources.ContainsKey(resource))
                lines.Add($"{unit.Name} requires resource {resource} which does not exist!", sourceObject: unit);
        if (unit.Replaces != null && !ruleset.Units.ContainsKey(unit.Replaces))
            lines.Add($"{unit.Name} replaces {unit.Replaces} which does not exist!", sourceObject: unit);
        foreach (string promotion in unit.Promotions)
            if (!ruleset.UnitPromotions.ContainsKey(promotion))
                lines.Add($"{unit.Name} contains promotion {promotion} which does not exist!", sourceObject: unit);
        CheckUnitType(unit.UnitType, () =>
            lines.Add($"{unit.Name} is of type {unit.UnitType}, which does not exist!", sourceObject: unit));

        // We should ignore conditionals here - there are condition implementations on this out there that require a game state (and will test false without)
        foreach (var unique in ((IHasUniques)unit).GetMatchingUniques(UniqueType.ConstructImprovementInstantly, GameContext.IgnoreConditionalsState))
        {
            string improvementName = unique.Params[0];
            var improvement = ruleset.TileImprovements.GetValueOrDefault(improvementName);
            if (improvement == null) continue; // this will be caught in the uniqueValidator.checkUniques
            if (improvement.IsEmpty() &&
                unit.IsCivilian() &&
                !unit.IsGreatPersonOfType("War"))
            {
                lines.Add($"{unit.Name} can place improvement {improvementName} which has no stats, preventing unit automation!",
                    RulesetErrorSeverity.WarningOptionsOnly, unit);
            }
        }

        CheckUniqueToMisspelling(unit, unit.UniqueTo, lines);
    }

    protected override void AddVictoryTypeErrors(RulesetErrorList lines)
    {
        base.AddVictoryTypeErrors(lines);

        foreach (var victoryType in ruleset.Victories.Values)
        {
            foreach (string requiredUnit in victoryType.RequiredSpaceshipParts)
                if (!ruleset.Units.ContainsKey(requiredUnit))
                    lines.Add(
                        $"Victory type {victoryType.Name} requires adding the non-existant unit {requiredUnit} to the capital to win!",
                        RulesetErrorSeverity.Warning, sourceObject: victoryType);

            foreach (var milestone in victoryType.MilestoneObjects)
            {
                if (milestone.Type is MilestoneType.BuiltBuilding or MilestoneType.BuildingBuiltGlobally
                    && !ruleset.Buildings.ContainsKey(milestone.Params[0]))
                    lines.Add(
                        $"Victory type {victoryType.Name} has milestone \"{milestone.UniqueDescription}\" that references an unknown building {milestone.Params[0]}!",
                        RulesetErrorSeverity.Error, sourceObject: victoryType);
                if (milestone.Type == MilestoneType.HaveCountable
                    && (UniqueParameterTypeExtensions.ToIntOrNull(milestone.Params[0]) == null || Countables.GetMatching(milestone.Params[1], ruleset) == null))
                    lines.Add(
                        $"Victory type {victoryType.Name} has milestone \"{milestone.UniqueDescription}\" with an invalid amount or an unknown countable!",
                        RulesetErrorSeverity.Error);
            }
        }
    }

    private void CheckUniqueToMisspelling(IHasUniques obj, string? uniqueTo, RulesetErrorList lines)
    {
        if (uniqueTo == null || ruleset.Nations.ContainsKey(uniqueTo)) return;

        // toSet() keeps first-encounter order
        var civUniques = ruleset.Nations.Values.SelectMany(it => it.Uniques).Distinct().ToList();
        if (civUniques.Contains(uniqueTo)) return;

        var possibleMisspellings = GetPossibleMisspellings(uniqueTo, civUniques.Concat(ruleset.Nations.Keys));
        if (possibleMisspellings.Count == 0) return;
        lines.Add(
            $"{obj.Name} has uniqueTo \"{uniqueTo}\" does not match any nation/unique - may be a misspelling of " +
            string.Join(" OR ", possibleMisspellings.Select(it => $"\"{it}\"")), sourceObject: obj,
            errorSeverityToReport: RulesetErrorSeverity.OK);
    }

    /// <summary>Checks validity of one UnitType by name supporting fallback if a mod has no UnitTypes at all, and calls <paramref name="reportError"/> if it is bad.</summary>
    private void CheckUnitType(string type, Action reportError)
    {
        if (ruleset.UnitTypes.ContainsKey(type)) return;
        if (ruleset.UnitTypes.Count == 0 && vanillaRuleset.Value.UnitTypes.ContainsKey(type)) return;
        reportError();
    }

    /// <returns>The Set of direct and indirect prerequisites of a Technology by names</returns>
    private HashSet<string> GetPrereqTree(string technologyName)
    {
        if (prereqsHashMap.TryGetValue(technologyName, out var cached)) return cached;
        var technology = ruleset.Technologies.GetValueOrDefault(technologyName);
        if (technology == null) return [];
        var techHashSet = new HashSet<string>(technology.Prerequisites);
        prereqsHashMap[technologyName] = techHashSet;
        foreach (string prerequisite in technology.Prerequisites)
            techHashSet.UnionWith(GetPrereqTree(prerequisite));
        return techHashSet;
    }
}
