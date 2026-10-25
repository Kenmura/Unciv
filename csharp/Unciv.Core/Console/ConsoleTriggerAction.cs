using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Validation;
using Unciv.Models.Translations;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.DevConsole;

/// <summary>
/// Kotlin: <c>ConsoleTriggerAction</c>. Console access to <see cref="UniqueTriggerActivation"/>: resolves the civ, city, unit and tile from the
/// selection, builds and validates the Unique, and asks <see cref="UniqueTriggerActivation.GetTriggerFunction"/> whether it can run. Running it
/// is the plan's effect.
/// Deviations: the Kotlin command rewrites the console text field when a deprecated unique has a replacement (a client concern, and no
/// <c>UniqueType</c> carries a deprecation annotation here); whether the trigger applies is decided while planning, so a refused trigger is an
/// error and never logged. A trigger whose function reports failure when it runs still stays in the log, as it is deterministic on replay.
/// </summary>
internal sealed class ConsoleTriggerAction(string topLevelCommand)
    : ConsoleAction(GetFormat(topLevelCommand), (console, parameters) => Plan(console, topLevelCommand, parameters))
{
    private static string GetFormat(string topLevelCommand)
    {
        string capitalized = char.ToUpperInvariant(topLevelCommand[0]) + topLevelCommand[1..];
        return $"{topLevelCommand} activatetrigger <triggeredUnique|triggered{capitalized}UniqueTemplate> [uniqueParam]...";
    }

    private static ConsolePlan Plan(ConsoleContext console, string topLevelCommand, IReadOnlyList<CliInput> parameters)
    {
        var targetType = topLevelCommand == "unit" ? UniqueTarget.UnitTriggerable : UniqueTarget.Triggerable;
        var paramStack = new LinkedList<CliInput>(parameters);
        // Each of these may be absent unless the command is named after it; the error message stays in one place
        var city = TryGet(topLevelCommand == "city", console.GetSelectedCity);
        var unit = TryGet(topLevelCommand == "unit", console.GetSelectedUnit);
        var tile = TryGet(topLevelCommand == "tile", console.GetSelectedTile);
        var civ = GetCiv(console, topLevelCommand, paramStack) ?? city?.Civ ?? unit?.Civ ?? tile?.GetOwner()
            ?? throw new ConsoleErrorException("A trigger command needs a Civilization from some source");
        var (unique, hint) = GetUnique(console, paramStack, targetType);
        var function = UniqueTriggerActivation.GetTriggerFunction(unique, (CivilizationObject)civ, city, unit, tile, null, "due to cheating");
        if (function == null) return ConsolePlan.Error($"The `triggerUnique` call failed\n{hint}");
        var response = hint.Length == 0 ? ConsoleResponse.OK : ConsoleResponse.Ok(hint);
        return new ConsolePlan(response, () => function());
    }

    private static T? TryGet<T>(bool required, Func<T> getter) where T : class
    {
        try
        {
            return getter();
        }
        catch (ConsoleErrorException) when (!required)
        {
            return null;
        }
    }

    private static CivilizationObject? GetCiv(ConsoleContext console, string topLevelCommand, LinkedList<CliInput> paramStack)
    {
        if (topLevelCommand != "civ") return null;
        // Came from `civ activatetrigger`: We want a civ, but on the command line it should be an optional parameter, defaulting to the selected one
        if (paramStack.First == null) return null; // not having any param needs a throw, but let the unique getter handle that
        var civ = console.GetCivByNameOrNull(paramStack.First.Value);
        if (civ == null) return console.GetCivByNameOrSelected(null);
        paramStack.RemoveFirst(); // name was good
        return civ;
    }

    private static (UniqueObject Unique, string Hint) GetUnique(ConsoleContext console, LinkedList<CliInput> paramStack, UniqueTarget targetType)
    {
        if (paramStack.First == null) throw new ConsoleErrorException("Parameter triggeredUnique missing");
        var uniqueText = paramStack.First.Value.ToMethod(CliInput.Method.Quoted);
        paramStack.RemoveFirst();
        var uniqueType = GetUniqueType(uniqueText, targetType);
        if (paramStack.Count > 0 && !uniqueText.Equals(uniqueType.Text()))
        {
            // Simplification: You either specify a fully formatted Unique as one parameter or the default text and a full set of replacements
            string[] values = [.. paramStack.Select(it => it.OriginalUnquoted())];
            uniqueText = new CliInput(uniqueType.PlaceholderText().FillPlaceholders(values), CliInput.Method.Quoted);
        }
        var unique = new UniqueObject(uniqueText.Content, targetType, "DevConsole");

        // Validate the found Unique
        var errors = new UniqueValidator(console.Game.Ruleset).CheckUnique(unique, new ConsoleRulesetObject(targetType));
        if (!errors.IsNotOK()) return (unique, errors.GetErrorText(true));

        // Errors - is deprecation the only problem?
        bool onlyDeprecated = errors.All(it =>
            it.ErrorSeverityToReport == RulesetErrorSeverity.ErrorOptionsOnly && it.Text.Contains("deprecated", StringComparison.Ordinal));
        if (!onlyDeprecated) throw new ConsoleUniqueErrorException(errors.GetErrorText(true));
        return (unique, errors.GetErrorText(true));
    }

    private static UniqueType GetUniqueType(CliInput param, UniqueTarget targetType)
    {
        var filterText = new CliInput(param.Content.GetPlaceholderText(), param.MethodUsed);
        var uniqueTypes = UniqueTypeExtensions.Entries
            .Where(it => new CliInput(it.PlaceholderText(), param.MethodUsed).Equals(filterText))
            .Take(4).ToList();
        if (uniqueTypes.Count == 0) throw new ConsoleUniqueErrorException($"`{param}` not found in UniqueTypes");
        if (uniqueTypes.Count > 1)
            throw new ConsoleUniqueErrorException($"`{param}` has ambiguous UniqueType: {string.Join(", ", uniqueTypes.Take(3).Select(it => it.Text()))}{(uniqueTypes.Count > 3 ? ", ..." : "")}?");
        var uniqueType = uniqueTypes[0];
        if (uniqueType.CanAcceptUniqueTarget(targetType)) return uniqueType;
        throw new ConsoleUniqueErrorException($"`{param}` is not a Triggerable");
    }

    private sealed class ConsoleRulesetObject(UniqueTarget targetType) : RulesetObject
    {
        public override string Name { get; set; } = "DevConsole";

        public override UniqueTarget GetUniqueTarget() => targetType;

        public override string MakeLink() => "";
    }
}
