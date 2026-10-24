using Unciv.Logic;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unique.Expressions;
using Unciv.Models.Translations;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Validation;

// Ported from core/src/com/unciv/models/ruleset/validation/UniqueValidator.kt
// Deviations:
// - UniqueType.getDeprecationAnnotation is not needed: no UniqueType entry is deprecated (T029), only DeprecatedUniqueType entries are, and all of those are
//   DeprecationLevel.ERROR, so they report ErrorOptionsOnly.
// - MapUnitCache.UnitMovementUniques is copied here (MapUnitCache needs MapUnit and Civilization, which are not ported yet); keep in sync.
// - Kotlin's `UniqueValidator.getUniqueContainerPrefix` is internal; so is this one.

public class UniqueValidator(Ruleset ruleset, bool tryFixUnknownUniques = false)
{
    public Ruleset Ruleset { get; } = ruleset;

    /// <summary>Used to determine if certain uniques are used for filtering</summary>
    private readonly HashSet<string> allNonTypedUniques = [];
    /// <summary>Used to determine if certain uniques are used for filtering</summary>
    private readonly HashSet<string> allUniqueParameters = [];

    private readonly Lazy<bool> anyAncientRuins = new(() =>
        ruleset.TileImprovements.Values
            .SelectMany(it => ((IHasUniques)it).UniqueObjects)
            .Any(it => it.Type == UniqueType.IsAncientRuinsEquivalent));

    private void AddToHashsets(IHasUniques uniqueHolder)
    {
        foreach (var unique in uniqueHolder.UniqueObjects)
        {
            if (unique.Type == null) allNonTypedUniques.Add(unique.PlaceholderText); // not unique.text, since there are conditional tags as well
            else
                foreach (string parameter in unique.AllParams.SelectMany(MultiFilter.GetAllSingleFilters))
                    allUniqueParameters.Add(parameter);
        }
    }

    public void PopulateFilteringUniqueHashsets()
    {
        foreach (var rulesetObject in Ruleset.AllRulesetObjects())
            AddToHashsets(rulesetObject);
    }

    public void CheckUniques(IHasUniques uniqueContainer, RulesetErrorList lines, bool reportRulesetSpecificErrors)
    {
        var baseSeverities = reportRulesetSpecificErrors ? AllParameterSeverities : ExtensionModParameterSeverities;
        foreach (var unique in uniqueContainer.UniqueObjects)
        {
            var severities = IsDisabledByModConditionals(unique) ? DisabledUniqueParameterSeverities : baseSeverities;
            var errors = CheckUnique(unique, uniqueContainer, severities);
            lines.AddAll(errors);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="unique"/> has a <see cref="UniqueType.ConditionalModEnabled"/> or <see cref="UniqueType.ConditionalModNotEnabled"/>
    /// modifier that is not satisfied by the mods present in the ruleset, meaning the unique will
    /// never be active in this ruleset and should be excluded from ruleset-specific validation.
    /// </summary>
    private bool IsDisabledByModConditionals(UniqueObject unique)
    {
        foreach (var modifier in unique.Modifiers)
        {
            switch (modifier.Type)
            {
                case UniqueType.ConditionalModEnabled:
                    {
                        string filter = modifier.Params[0];
                        if (!Ruleset.Mods.Any(it => ModCompatibility.ModNameFilter(it, filter)))
                            return true;
                        break;
                    }
                case UniqueType.ConditionalModNotEnabled:
                    {
                        string filter = modifier.Params[0];
                        if (Ruleset.Mods.Any(it => ModCompatibility.ModNameFilter(it, filter)))
                            return true;
                        break;
                    }
            }
        }
        return false;
    }

    private static readonly IReadOnlySet<UniqueType> PerformanceHeavyConditionals = new HashSet<UniqueType>
    {
        UniqueType.ConditionalNeighborTiles, UniqueType.ConditionalAdjacentTo, UniqueType.ConditionalNotAdjacentTo,
    };

    public RulesetErrorList CheckUnique(
        UniqueObject unique,
        IHasUniques? uniqueContainer,
        IReadOnlySet<UniqueParameterErrorSeverity>? severityToReport = null)
    {
        severityToReport ??= AllParameterSeverities;
        bool reportRulesetSpecificErrors = severityToReport.Contains(UniqueParameterErrorSeverity.RulesetSpecific);
        var prefix = new Lazy<string>(() => GetUniqueContainerPrefix(uniqueContainer) + $"\"{unique.Text}\"");
        if (unique.Type == null)
        {
            if (unique.DeprecatedType != null && reportRulesetSpecificErrors)
                return GetDeprecationAnnotationErrors(unique, prefix.Value, uniqueContainer);
            return CheckUntypedUnique(unique, uniqueContainer, prefix.Value, reportRulesetSpecificErrors);
        }
        var uniqueType = unique.Type.Value;

        var rulesetErrors = new RulesetErrorList(Ruleset);

        if (uniqueContainer != null &&
            !(uniqueType.CanAcceptUniqueTarget(uniqueContainer.GetUniqueTarget()) ||
              // "for X turns" effectively turns a global unique into a trigger
              (unique.HasModifier(UniqueType.ConditionalTimedUnique)
               && uniqueContainer.GetUniqueTarget().CanAcceptUniqueTarget(UniqueTarget.Triggerable))))
            rulesetErrors.Add($"{prefix.Value} is not allowed on its target type", RulesetErrorSeverity.Warning, uniqueContainer, unique);

        var typeComplianceErrors = GetComplianceErrors(unique);
        foreach (var complianceError in typeComplianceErrors)
        {
            if (!severityToReport.Contains(complianceError.ErrorSeverity))
                continue;

            string text = $"{prefix.Value} contains parameter \"{complianceError.ParameterName}\", {WhichDoesNotFitParameterType}" +
                $" {string.Join(" or ", complianceError.AcceptableParameterTypes.Select(it => it.ParameterName()))} !";

            if (tryFixUnknownUniques) text = AddPossibleMisspellings(complianceError, text);

            rulesetErrors.Add(
                text,
                complianceError.ErrorSeverity.GetRulesetErrorSeverity(), uniqueContainer, unique);

            rulesetErrors.AddAll(GetExpressionParseErrors(complianceError, uniqueContainer, unique));
        }

        foreach (var modifier in unique.Modifiers)
            rulesetErrors.AddAll(GetModifierErrors(modifier, prefix.Value, unique, uniqueContainer, severityToReport));

        rulesetErrors.AddAll(GetUniqueTypeSpecificErrors(prefix.Value, unique, uniqueContainer, reportRulesetSpecificErrors));

        string? conditionalSyntaxProblem = unique.Text.ValidateConditionals();
        if (conditionalSyntaxProblem != null)
            rulesetErrors.Add($"{prefix.Value} contains {conditionalSyntaxProblem} and may break translations.", RulesetErrorSeverity.WarningOptionsOnly, uniqueContainer, unique);

        var conditionals = unique.Modifiers.Where(it => it.Type?.CanAcceptUniqueTarget(UniqueTarget.Conditional) == true).ToList();
        if (conditionals.Count > 1)
        {
            var lastCheapConditional = conditionals.LastOrDefault(it => !PerformanceHeavyConditionals.Contains(it.Type!.Value));
            var firstExpensiveConditional = conditionals.FirstOrDefault(it => PerformanceHeavyConditionals.Contains(it.Type!.Value));
            if (lastCheapConditional != null && firstExpensiveConditional != null)
            {
                if (conditionals.IndexOf(lastCheapConditional) > conditionals.IndexOf(firstExpensiveConditional))
                    rulesetErrors.Add($"{prefix.Value} contains multiple conditionals," +
                        $" of which \"{firstExpensiveConditional.Text}\" is more expensive to calculate than \"{lastCheapConditional.Text}\". " +
                        "For performance, consider switching their locations.", RulesetErrorSeverity.WarningOptionsOnly, uniqueContainer, unique);
            }
        }

        if (UnitMovementUniques.Contains(uniqueType)
            && unique.Modifiers.Any(it => it.Type != UniqueType.ConditionalOurUnit || !Constants.All.Contains(it.Params[0])))
            // (Stay silent if the only conditional is `<for [All] units>` - as in G&K Denmark)
            // Not necessarily even a problem, but yes something mod maker should be aware of
            rulesetErrors.Add(
                $"{prefix.Value} contains a conditional on a unit movement unique. " +
                "Due to performance considerations, this unique is cached on the unit," +
                " and the conditional may not always limit the unique correctly.",
                RulesetErrorSeverity.OK, uniqueContainer, unique);

        if (reportRulesetSpecificErrors)
            // If we don't filter these messages will be listed twice as this function is called twice on most objects
            // The tests are RulesetInvariant in nature, but RulesetSpecific is called for _all_ objects, invariant is not.
            rulesetErrors.AddAll(GetDeprecationAnnotationErrors(unique, prefix.Value, uniqueContainer));

        return rulesetErrors;
    }

    private string AddPossibleMisspellings(UniqueComplianceError complianceError, string text)
    {
        string toReturn = text;
        var similarParameters = complianceError.AcceptableParameterTypes
            .SelectMany(it => it.GetKnownValuesForAutocomplete(Ruleset))
            .Where(it => TextSimilarity.GetRelativeTextDistance(it, complianceError.ParameterName) <= RulesetCache.UniqueMisspellingThreshold)
            .ToList();
        if (similarParameters.Count != 0)
            toReturn += " May be a misspelling of: " + string.Join(", ", similarParameters.Select(it => $"\"{it}\""));
        return toReturn;
    }

    private RulesetErrorList GetExpressionParseErrors(UniqueComplianceError complianceError, IHasUniques? uniqueContainer, UniqueObject unique)
    {
        var rulesetErrors = new RulesetErrorList();
        if (!complianceError.AcceptableParameterTypes.Contains(UniqueParameterType.Countable)) return rulesetErrors;

        var parseError = Expressions.GetParsingError(complianceError.ParameterName);
        if (parseError != null)
        {
            const string marker = "HERE➡";
            int errorLocation = parseError.Position;
            string parameterWithErrorLocationMarked =
                complianceError.ParameterName[..errorLocation] + marker + complianceError.ParameterName[errorLocation..];
            string text = $"\"{complianceError.ParameterName}\" could not be parsed as an expression due to:" +
                $" {parseError.Message}. \n{parameterWithErrorLocationMarked}";
            rulesetErrors.Add(text, RulesetErrorSeverity.WarningOptionsOnly, uniqueContainer, unique);
            return rulesetErrors;
        }

        var countableErrors = Expressions.GetCountableErrors(complianceError.ParameterName, Ruleset);
        if (countableErrors.Count != 0)
        {
            string text = $"\"{complianceError.ParameterName}\" was parsed as an expression, but has the following errors with this ruleset:" +
                $" {string.Join(", ", countableErrors)}";
            rulesetErrors.Add(text, RulesetErrorSeverity.WarningOptionsOnly, uniqueContainer, unique);
        }
        return rulesetErrors;
    }

    private static readonly IReadOnlySet<UniqueType> ResourceUniques = new HashSet<UniqueType>
    {
        UniqueType.ProvidesResources, UniqueType.ConsumesResources,
        UniqueType.PercentResourceProduction, UniqueType.StatPercentFromObjectToResource,
    };

    private static readonly IReadOnlySet<UniqueType> ResourceConditionals = new HashSet<UniqueType>
    {
        UniqueType.ConditionalWithResource,
        UniqueType.ConditionalWithoutResource,
        UniqueType.ConditionalWhenBetweenStatResource,
        UniqueType.ConditionalWhenAboveAmountStatResource,
        UniqueType.ConditionalWhenBelowAmountStatResource,
    };

    private RulesetErrorList GetModifierErrors(
        UniqueObject modifier,
        string prefix,
        UniqueObject unique,
        IHasUniques? uniqueContainer,
        IReadOnlySet<UniqueParameterErrorSeverity> severityToReport)
    {
        var rulesetErrors = new RulesetErrorList();
        if (unique.HasFlag(UniqueFlag.NoConditionals) && modifier.Type?.CanAcceptUniqueTarget(UniqueTarget.MetaModifier) != true)
        {
            rulesetErrors.Add(
                $"{prefix} contains the conditional \"{modifier.Text}\"," +
                " but the unique does not accept conditionals!",
                RulesetErrorSeverity.Error, uniqueContainer, unique);
            return rulesetErrors;
        }

        if (modifier.Type == null)
        {
            string text = $"{prefix} contains the conditional \"{modifier.Text}\"," +
                " which is of an unknown type!";

            var similarConditionals = UniqueTypeExtensions.Entries.Where(it =>
                TextSimilarity.GetRelativeTextDistance(it.PlaceholderText(), modifier.PlaceholderText) <= RulesetCache.UniqueMisspellingThreshold).ToList();
            if (similarConditionals.Count != 0)
                text += " May be a misspelling of \"" + string.Join("\", or \"", similarConditionals.Select(it => it.Text())) + "\"";
            rulesetErrors.Add(
                text,
                RulesetErrorSeverity.Warning, uniqueContainer, unique);
            return rulesetErrors;
        }
        var modifierType = modifier.Type.Value;

        if (modifierType.TargetTypes().All(it => it.ModifierType() == UniqueTargetModifierType.None))
            rulesetErrors.Add(
                $"{prefix} contains the modifier \"{modifier.Text}\"," +
                " which is a Unique that is not a modifier.",
                RulesetErrorSeverity.Warning, uniqueContainer, unique);

        if (modifierType.TargetTypes().All(it => !it.IsAcceptableModifierFor(unique))) // No target is a modifier for whatever
        {
            rulesetErrors.Add(
                $"{prefix} contains the modifier \"{modifier.Text}\"," +
                " which is not an acceptable modifier for this unique.",
                RulesetErrorSeverity.Warning, uniqueContainer, unique);
        }

        if (unique.Type != null && ResourceUniques.Contains(unique.Type.Value) && ResourceConditionals.Contains(modifierType)
            && Ruleset.TileResources.GetValueOrDefault(modifier.Params[^1])?.IsCityWide == true)
            rulesetErrors.Add(
                $"{prefix} contains the conditional \"{modifier.Text}\"," +
                " which references a citywide resource. This is not a valid conditional for a resource uniques, " +
                "as it causes a recursive evaluation loop.",
                RulesetErrorSeverity.Error, uniqueContainer, unique);

        // Find resource uniques with countable parameters in conditionals, that depend on citywide resources
        // This too leads to an endless loop
        if (unique.Type != null && ResourceUniques.Contains(unique.Type.Value))
        {
            var parameterTypeMap = modifierType.ParameterTypeMap();
            for (int index = 0; index < modifier.Params.Count; index++)
            {
                string param = modifier.Params[index];
                if (Ruleset.TileResources.GetValueOrDefault(param)?.IsCityWide != true) continue;
                if (index >= parameterTypeMap.Count || !parameterTypeMap[index].Contains(UniqueParameterType.Countable)) continue;

                rulesetErrors.Add(
                    $"{prefix} contains the modifier \"{modifier.Text}\"," +
                    " which references a citywide resource as a countable." +
                    " This is not a valid conditional for a resource uniques, as it causes a recursive evaluation loop.",
                    RulesetErrorSeverity.Error, uniqueContainer, unique);
            }
        }

        var conditionalComplianceErrors = GetComplianceErrors(modifier);

        foreach (var complianceError in conditionalComplianceErrors)
        {
            if (!severityToReport.Contains(complianceError.ErrorSeverity))
                continue;

            string text = $"{prefix} contains modifier \"{modifier.Text}\"." +
                $" This contains the parameter \"{complianceError.ParameterName}\" {WhichDoesNotFitParameterType}" +
                $" {string.Join(" or ", complianceError.AcceptableParameterTypes.Select(it => it.ParameterName()))} !";

            if (tryFixUnknownUniques) text = AddPossibleMisspellings(complianceError, text);

            rulesetErrors.Add(text,
                complianceError.ErrorSeverity.GetRulesetErrorSeverity(), uniqueContainer, unique);

            rulesetErrors.AddAll(GetExpressionParseErrors(complianceError, uniqueContainer, unique));
        }

        rulesetErrors.AddAll(GetDeprecationAnnotationErrors(modifier, $"{prefix} contains modifier \"{modifier.Text}\" which", uniqueContainer));
        return rulesetErrors;
    }

    private RulesetErrorList GetUniqueTypeSpecificErrors(string prefix, UniqueObject unique, IHasUniques? uniqueContainer, bool reportRulesetSpecificErrors)
    {
        var rulesetErrors = new RulesetErrorList();
        if (unique.Type == UniqueType.RuinsUpgrade && reportRulesetSpecificErrors && !anyAncientRuins.Value)
            rulesetErrors.Add($"{prefix} is pointless - there are no ancient ruins", RulesetErrorSeverity.Warning, uniqueContainer, unique);
        return rulesetErrors;
    }

    private RulesetErrorList GetDeprecationAnnotationErrors(UniqueObject unique, string prefix, IHasUniques? uniqueContainer)
    {
        var rulesetErrors = new RulesetErrorList();
        var deprecationAnnotation = unique.GetDeprecationAnnotation();
        if (deprecationAnnotation != null)
        {
            string replacementUniqueText = unique.GetReplacementText(Ruleset);
            string deprecationText =
                $"{prefix} is deprecated {deprecationAnnotation.Message}" +
                (deprecationAnnotation.ReplaceWith != "" ? $", replace with \"{replacementUniqueText}\"" : "");
            // Every DeprecatedUniqueType entry is DeprecationLevel.ERROR (the WARNING level was for half-deprecated UniqueType entries, none exist)
            rulesetErrors.Add(deprecationText, RulesetErrorSeverity.ErrorOptionsOnly, uniqueContainer, unique); // User visible
        }

        // Check for deprecated Countables
        if (unique.Type == null) return rulesetErrors;
        var parameterTypeMap = unique.Type.Value.ParameterTypeMap();
        var countables = new List<Countables>();
        for (int index = 0; index < parameterTypeMap.Count; index++)
        {
            if (!parameterTypeMap[index].Contains(UniqueParameterType.Countable)) continue;
            var countable = Countables.GetMatching(unique.Params[index], Ruleset);
            if (countable != null) countables.Add(countable);
        }
        foreach (var countable in countables)
        {
            var deprecation = countable.GetDeprecationAnnotation();
            if (deprecation == null) continue;
            // This is less flexible than unique.getReplacementText(ruleset)
            string replaceExpression = deprecation.ReplaceWith;
            string text = $"Countable `{countable.Name}` is deprecated {deprecation.Message}" +
                (replaceExpression.Length == 0 ? "" : $", replace with \"{replaceExpression}\"");
            // DeprecationLevel.ERROR: user visible in new game and red in options
            rulesetErrors.Add(text, RulesetErrorSeverity.ErrorOptionsOnly, uniqueContainer, unique);
        }
        return rulesetErrors;
    }

    /// <summary>Maps uncompliant parameters to their required types</summary>
    private List<UniqueComplianceError> GetComplianceErrors(UniqueObject unique)
    {
        var errorList = new List<UniqueComplianceError>();
        if (unique.Type == null) return errorList;
        var parameterTypeMap = unique.Type.Value.ParameterTypeMap();
        for (int index = 0; index < unique.Params.Count; index++)
        {
            string param = unique.Params[index];
            // Trying to catch the error at #11404
            if (parameterTypeMap.Count != unique.Params.Count)
                throw new InvalidOperationException($"Unique {unique.Text} has {unique.Params.Count} parameters, " +
                    $"but its type {unique.Type} only {parameterTypeMap.Count} parameters?!");
            var acceptableParamTypes = parameterTypeMap[index];
            if (acceptableParamTypes.Count == 0) continue; // This is a deprecated parameter type, don't bother checking it

            var errorTypesForAcceptableParameters = acceptableParamTypes.Select(it => GetParamTypeErrorSeverityCached(it, param)).ToList();
            if (errorTypesForAcceptableParameters.Any(it => it == null)) continue; // This matches one of the types!
            if (errorTypesForAcceptableParameters.Contains(UniqueParameterErrorSeverity.PossibleFilteringUnique)
                && allNonTypedUniques.Contains(param))
                continue; // This is a filtering param, and the unique it's filtering for actually exists, no problem here!
            var leastSevereWarning = errorTypesForAcceptableParameters[0]!.Value;
            foreach (var severity in errorTypesForAcceptableParameters)
                if ((int)severity!.Value < (int)leastSevereWarning) leastSevereWarning = severity.Value; // minByOrNull: first of the minima
            errorList.Add(new UniqueComplianceError(param, acceptableParamTypes, leastSevereWarning));
        }
        return errorList;
    }

    private readonly Dictionary<UniqueParameterType, Dictionary<string, UniqueParameterErrorSeverity?>> paramTypeErrorSeverityCache = [];

    private UniqueParameterErrorSeverity? GetParamTypeErrorSeverityCached(UniqueParameterType uniqueParameterType, string param)
    {
        if (!paramTypeErrorSeverityCache.TryGetValue(uniqueParameterType, out var uniqueParamCache))
        {
            uniqueParamCache = [];
            paramTypeErrorSeverityCache[uniqueParameterType] = uniqueParamCache;
        }

        if (uniqueParamCache.TryGetValue(param, out var cached)) return cached;

        var severity = uniqueParameterType.GetErrorSeverity(param, Ruleset);
        uniqueParamCache[param] = severity;
        return severity;
    }

    private RulesetErrorList CheckUntypedUnique(UniqueObject unique, IHasUniques? uniqueContainer, string prefix, bool reportRulesetSpecificErrors)
    {
        // Malformed conditional is always bad
        string? conditionalSyntaxProblem = unique.Text.ValidateConditionals();
        if (conditionalSyntaxProblem != null)
            return RulesetErrorList.Of(
                $"{prefix} contains {conditionalSyntaxProblem}!",
                RulesetErrorSeverity.Warning, Ruleset, uniqueContainer, unique);

        // Support purely filtering Uniques without actual implementation
        if (IsFilteringUniqueAllowed(unique, reportRulesetSpecificErrors)) return new RulesetErrorList();

        if (tryFixUnknownUniques)
        {
            var fixes = TryFixUnknownUnique(unique, uniqueContainer, prefix);
            if (fixes.Count != 0) return fixes;
        }

        return RulesetErrorList.Of(
            $"{prefix} not found in Unciv's unique types, and is not used as a filtering unique.",
            unique.Params.Count == 0 ? RulesetErrorSeverity.OK : RulesetErrorSeverity.Warning,
            Ruleset, uniqueContainer, unique);
    }

    private bool IsFilteringUniqueAllowed(UniqueObject unique, bool reportRulesetSpecificErrors)
    {
        // Isolate this decision, to allow easy change of approach
        // This says: Must have no conditionals or parameters, and is used in any "filtering" parameter of another Unique
        if (unique.Modifiers.Count != 0 || unique.Params.Count != 0) return false;
        if (!reportRulesetSpecificErrors) return true; // Don't report unless checking a complete Ruleset
        return allUniqueParameters.Contains(unique.Text); // referenced at least once from elsewhere
    }

    private RulesetErrorList TryFixUnknownUnique(UniqueObject unique, IHasUniques? uniqueContainer, string prefix)
    {
        var similarUniques = UniqueTypeExtensions.Entries.Where(it =>
            TextSimilarity.GetRelativeTextDistance(it.PlaceholderText(), unique.PlaceholderText) <= RulesetCache.UniqueMisspellingThreshold).ToList();
        var equalUniques = similarUniques.Where(it => it.PlaceholderText() == unique.PlaceholderText).ToList();
        if (equalUniques.Count != 0)
            // This should only ever happen if a bug is or has been introduced that prevents Unique.type from being set for a valid UniqueType, I think.
            return RulesetErrorList.Of(
                $"{prefix} looks like it should be fine, but for some reason isn't recognized.",
                RulesetErrorSeverity.OK,
                Ruleset, uniqueContainer, unique);

        if (similarUniques.Count != 0)
        {
            string suggestions = string.Join("\n", similarUniques.Select(uniqueType =>
            {
                string line = $"\"{uniqueType.Text()}";
                if (unique.Modifiers.Count != 0)
                    line += " " + string.Join(" ", unique.Modifiers.Select(it => $"<{it.Text}>"));
                line += "\"";
                return line; // no UniqueType carries a deprecation annotation, so no " (Deprecated)" suffix
            }));
            // Kotlin prependIndent("\t")
            string text = $"{prefix} looks like it may be a misspelling of:\n" + string.Join("\n", suggestions.Split('\n').Select(it => "\t" + it));
            return RulesetErrorList.Of(text, RulesetErrorSeverity.OK, Ruleset, uniqueContainer, unique);
        }
        return new RulesetErrorList();
    }

    public const string WhichDoesNotFitParameterType = "which does not fit parameter type";

    /// <summary>All parameter error severities - used when validating a complete base ruleset.</summary>
    public static readonly IReadOnlySet<UniqueParameterErrorSeverity> AllParameterSeverities = new HashSet<UniqueParameterErrorSeverity>(Enum.GetValues<UniqueParameterErrorSeverity>());

    /// <summary>Skips RulesetSpecific - used for extension mods that are validated standalone, without their base ruleset mixed in.</summary>
    public static readonly IReadOnlySet<UniqueParameterErrorSeverity> ExtensionModParameterSeverities =
        new HashSet<UniqueParameterErrorSeverity>(Enum.GetValues<UniqueParameterErrorSeverity>().Where(it => it != UniqueParameterErrorSeverity.RulesetSpecific));

    /// <summary>Only RulesetInvariant - used when a unique is disabled in this ruleset via a mod-enabled conditional.</summary>
    public static readonly IReadOnlySet<UniqueParameterErrorSeverity> DisabledUniqueParameterSeverities =
        new HashSet<UniqueParameterErrorSeverity> { UniqueParameterErrorSeverity.RulesetInvariant };

    // Copy of MapUnitCache.UnitMovementUniques, see the deviations above.
    private static readonly IReadOnlySet<UniqueType> UnitMovementUniques = new HashSet<UniqueType>
    {
        UniqueType.AllTilesCost1Move,
        UniqueType.CanPassImpassable,
        UniqueType.IgnoresTerrainCost,
        UniqueType.IgnoresZOC,
        UniqueType.RoughTerrainPenalty,
        UniqueType.CannotMove,
        UniqueType.CanMoveOnWater,
        UniqueType.DoubleMovementOnTerrain,
        UniqueType.ReducedDisembarkCost,
        UniqueType.ReducedEmbarkCost,
        UniqueType.CanEnterIceTiles,
        UniqueType.CanEnterForeignTiles,
        UniqueType.CanEnterForeignTilesButLosesReligiousStrength,
        UniqueType.ForestsAndJunglesAreRoads,
        UniqueType.IgnoreHillMovementCost,
        UniqueType.DamagesContainingUnits,
        UniqueType.LandUnitEmbarkation,
        UniqueType.LandUnitsCrossTerrainAfterUnitGained,
        UniqueType.EnemyUnitsSpendExtraMovement,
    };

    internal static string GetUniqueContainerPrefix(IHasUniques? uniqueContainer) =>
        (uniqueContainer is IRulesetObject rulesetObject ? $"{rulesetObject.OriginRuleset}: " : "") +
        (uniqueContainer == null ? "The" : $"({uniqueContainer.GetUniqueTarget()}) {uniqueContainer.Name}'s") +
        " unique ";
}

public static class UniqueValidatorExtensions
{
    /// <summary>Kotlin <c>Unique.getReplacementText(ruleset)</c>: candidates that fail <see cref="UniqueValidator.CheckUnique"/> are filtered out.</summary>
    public static string GetReplacementText(this UniqueObject unique, Ruleset ruleset) =>
        unique.GetReplacementText(it => new UniqueValidator(ruleset).CheckUnique(it, null).Count == 0);
}
