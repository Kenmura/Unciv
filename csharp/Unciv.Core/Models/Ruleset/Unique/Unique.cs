using Unciv.Models.StatTypes;
using Unciv.Models.Translations;

namespace Unciv.Models.Ruleset.Unique;

/// <summary>What <see cref="Unique.GetGameProgressModifier"/> reads from <c>Civilization</c>.</summary>
public interface IGameProgressCivilization
{
    /// <summary><c>civ.tech.researchedTechnologies.size</c></summary>
    int ResearchedTechnologiesCount { get; }
    /// <summary><c>civ.gameInfo.ruleset.technologies.size</c></summary>
    int RulesetTechnologiesCount { get; }
    /// <summary><c>civ.policies.adoptedPolicies.size</c></summary>
    int AdoptedPoliciesCount { get; }
    /// <summary><c>civ.gameInfo.ruleset.policies.size</c></summary>
    int RulesetPoliciesCount { get; }
}

/// <summary>
/// Ported from core/src/com/unciv/models/ruleset/unique/Unique.kt.
/// Deviations: <c>timeThis</c> profiling is dropped; <c>getReplacementText</c> takes the "does this unique validate without errors" check
/// as a delegate instead of a <c>Ruleset</c> (TODO(T036): <c>UniqueValidator(ruleset).checkUnique(unique, null).isEmpty()</c>);
/// <c>toFloat()</c> follows <c>java.lang.Float.parseFloat</c> (ASCII whitespace trimmed, an <c>f</c>/<c>F</c>/<c>d</c>/<c>D</c> suffix accepted),
/// <c>toInt()</c> follows <c>Integer.parseInt</c> (strict).
/// </summary>
public class Unique : IUniqueTargetView
{
    public Unique(string text, UniqueTarget? sourceObjectType = null, string? sourceObjectName = null)
    {
        Text = text;
        SourceObjectType = sourceObjectType;
        SourceObjectName = sourceObjectName;

        PlaceholderText = text.GetPlaceholderText();
        Params = text.GetPlaceholderParameters();
        Type = UniqueTypeExtensions.UniqueTypeMap.TryGetValue(PlaceholderText, out var type) ? type : null;
        DeprecatedType = Type == null ? DeprecatedUniqueTypes.Lookup(PlaceholderText) : null;

        Modifiers = text.GetModifiers();
        modifiersMap = new Dictionary<UniqueType, List<Unique>>();
        foreach (var modifier in Modifiers)
        {
            if (modifier.Type == null) continue;
            if (!modifiersMap.TryGetValue(modifier.Type.Value, out var group))
            {
                group = new List<Unique>();
                modifiersMap[modifier.Type.Value] = group;
            }
            group.Add(modifier);
        }

        IsTimedTriggerable = HasModifier(UniqueType.ConditionalTimedUnique);

        IsTriggerable = Type != null && (
            Type.Value.TargetTypes().Any(it => it.CanAcceptUniqueTarget(UniqueTarget.Triggerable))
            || Type.Value.TargetTypes().Any(it => it.CanAcceptUniqueTarget(UniqueTarget.UnitTriggerable))
            || IsTimedTriggerable);

        // Includes conditional params
        AllParams = Params.Concat(Modifiers.SelectMany(it => it.Params)).ToList();

        IsLocalEffect = Params.Contains("in this city") || HasModifier(UniqueType.ConditionalInThisCity);
        IsOtherModifierType = Type != null && Type.Value.TargetTypes().Any(it => it.ModifierType() == UniqueTargetModifierType.Other);
    }

    public string Text { get; }
    public UniqueTarget? SourceObjectType { get; }
    public string? SourceObjectName { get; }

    /// <summary>
    /// This is so the heavy regex-based parsing is only activated once per unique, instead of every time it's called
    /// - for instance, in the city screen, we call every tile unique for every tile, which can lead to ANRs
    /// </summary>
    public string PlaceholderText { get; }
    /// <summary>Does not include conditional params</summary>
    public List<string> Params { get; }
    public UniqueType? Type { get; }
    public DeprecatedUniqueTypeEntry? DeprecatedType { get; }

    private Stats? stats;
    public Stats Stats
    {
        get
        {
            if (stats != null) return stats;
            string? firstStatParam = Params.FirstOrDefault(Stats.IsStats);
            // So badly-defined stats don't crash the entire game
            stats = firstStatParam == null ? new Stats() : Stats.Parse(firstStatParam);
            return stats;
        }
    }

    public List<Unique> Modifiers { get; }
    private readonly Dictionary<UniqueType, List<Unique>> modifiersMap;

    public bool IsTimedTriggerable { get; }

    public bool IsTriggerable { get; }

    /// <summary>Includes conditional params</summary>
    public List<string> AllParams { get; }

    public bool IsLocalEffect { get; }
    public bool IsOtherModifierType { get; }

    // IUniqueTargetView, for UniqueTarget.IsAcceptableModifierFor
    IReadOnlyCollection<UniqueTarget>? IUniqueTargetView.TargetTypes => Type?.TargetTypes();

    public bool HasFlag(UniqueFlag flag) =>
        (Type != null && Type.Value.Flags().Contains(flag)) ||
        (DeprecatedType != null && DeprecatedType.Flags.Contains(flag));

    public bool IsHiddenToUsers() => HasFlag(UniqueFlag.HiddenToUsers) || HasModifier(UniqueType.ModifierHiddenFromUsers);

    public IReadOnlyList<Unique> GetModifiers(UniqueType type) =>
        modifiersMap.TryGetValue(type, out var modifiers) ? modifiers : Array.Empty<Unique>();

    public bool HasModifier(UniqueType type) => modifiersMap.ContainsKey(type);
    public bool IsModifiedByGameSpeed() => HasModifier(UniqueType.ModifiedByGameSpeed);
    public bool IsModifiedByGameProgress() => HasModifier(UniqueType.ModifiedByGameProgress);

    public float GetGameProgressModifier(IGameProgressCivilization civ)
    {
        // According to: https://www.reddit.com/r/civ/comments/gvx44v/comment/fsrifc2/
        float modifier = 1f;
        float techComplete = civ.RulesetTechnologiesCount != 0
            ? (float)civ.ResearchedTechnologiesCount / civ.RulesetTechnologiesCount : 0f;
        float policyComplete = civ.RulesetPoliciesCount != 0
            ? (float)civ.AdoptedPoliciesCount / civ.RulesetPoliciesCount : 0f;
        float gameProgess = Math.Max(techComplete, policyComplete);
        foreach (var unique in GetModifiers(UniqueType.ModifiedByGameProgress))
        {
            // Mod creators likely expect this to stack multiplicatively, otherwise they'd use a single modifier
            modifier *= 1 + (ParseFloat(unique.Params[0]) / 100 - 1) * gameProgess;
        }
        return modifier;
    }

    public bool HasTriggerConditional()
    {
        if (Modifiers.Count == 0) return false;
        return Modifiers.Any(conditional =>
            conditional.Type != null && conditional.Type.Value.TargetTypes().Any(it =>
                it.CanAcceptUniqueTarget(UniqueTarget.TriggerCondition)
                || it.CanAcceptUniqueTarget(UniqueTarget.UnitActionModifier)
                || it.CanAcceptUniqueTarget(UniqueTarget.UnitTriggerCondition)));
    }

    public bool ConditionalsApply(GameContext state)
    {
        if (state.IgnoreConditionals) return true;
        // Always allow Timed conditional uniques. They are managed elsewhere
        if (IsTimedTriggerable) return true;
        if (Modifiers.Count == 0) return true;
        foreach (var modifier in Modifiers)
        {
            if (!Conditionals.ConditionalApplies(this, modifier, state)) return false;
        }
        return true;
    }

    public int GetUniqueMultiplier(GameContext gameContext)
    {
        if (gameContext.Equals(GameContext.IgnoreMultiplicationForCaching))
            return 1;

        int amount = 1;

        var forEveryModifiers = GetModifiers(UniqueType.ForEveryCountable);
        foreach (var conditional in forEveryModifiers) // multiple multipliers DO multiply.
        {
            int multiplier = Countables.GetCountableAmount(conditional.Params[0], gameContext)
                ?? 0; // If the countable is invalid, ignore this unique entirely
            amount *= multiplier;
        }

        var forEveryAmountModifiers = GetModifiers(UniqueType.ForEveryAmountCountable);
        foreach (var conditional in forEveryAmountModifiers) // multiple multipliers DO multiply.
        {
            int multiplier = Countables.GetCountableAmount(conditional.Params[1], gameContext)
                ?? 0; // If the countable is invalid, ignore this unique entirely
            int perEvery = ParseInt(conditional.Params[0]);
            // JVM `Int.MIN_VALUE / -1` wraps to `Int.MIN_VALUE`, .NET throws OverflowException
            amount *= perEvery == -1 ? unchecked(-multiplier) : multiplier / perEvery;
        }

        if (gameContext.RelevantTile != null)
        {
            var forEveryAdjacentTileModifiers = GetModifiers(UniqueType.ForEveryAdjacentTile);
            foreach (var conditional in forEveryAdjacentTileModifiers)
            {
                int multiplier = gameContext.RelevantTile.Neighbors.Count(it => it.MatchesFilter(conditional.Params[0]));
                amount *= multiplier;
            }
        }

        return Math.Max(amount, 0);
    }

    /// <summary>Multiplies the unique according to the multiplication conditionals</summary>
    public IEnumerable<Unique> GetMultiplied(GameContext gameContext)
    {
        int multiplier = GetUniqueMultiplier(gameContext);
        return Enumerable.Repeat(this, multiplier);
    }

    public void ForEachMultiplied(GameContext gameContext, Action<Unique> op)
    {
        int multiplier = GetUniqueMultiplier(gameContext);
        for (int j = 0; j < multiplier; j++)
            op(this);
    }

    /// <summary><c>type?.getDeprecationAnnotation() ?: deprecatedType?.getDeprecationAnnotation()</c>; no <see cref="UniqueType"/> entry is deprecated.</summary>
    public DeprecatedUniqueTypeEntry? GetDeprecationAnnotation() => DeprecatedType;

    public string GetSourceNameForUser() => SourceObjectType switch
    {
        null => "",
        UniqueTarget.Global => GlobalUniques.GetUniqueSourceDescription(this),
        UniqueTarget.Wonder => "Wonders",
        UniqueTarget.Building => "Buildings",
        UniqueTarget.Policy => "Policies",
        UniqueTarget.CityState => Constants.CityStates,
        _ => SourceObjectType.Value.ToString(),
    };

    /// <summary>Zero-based, so n=0 returns the first</summary>
    private static int GetNthIndex(string str, IReadOnlyList<string> list, int n)
    {
        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == str)
            {
                if (count == n) return i;
                count += 1;
            }
        }
        return -1; // Not found
    }

    /// <param name="hasNoErrors">Whether a candidate replacement validates without errors (TODO(T036): UniqueValidator).</param>
    public string GetReplacementText(Func<Unique, bool> hasNoErrors)
    {
        var deprecationAnnotation = GetDeprecationAnnotation();
        if (deprecationAnnotation == null) return "";
        string replacementUniqueText = deprecationAnnotation.ReplaceWith;
        string deprecatedSourceText = Type != null ? Type.Value.Text() : DeprecatedType!.Text;
        var deprecatedUniquePlaceholders = deprecatedSourceText.GetPlaceholderParameters();
        var possibleUniques = replacementUniqueText.Split(Constants.UniqueOrDelimiter);

        // Here, for once, we DO want the conditional placeholder parameters together with the regular ones,
        //  so we cheat the conditional detector by removing the '<'
        //  note this is only done for the replacement, not the deprecated unique, thus parameters of
        //  conditionals on the deprecated unique are ignored

        var finalPossibleUniques = new List<string>();

        foreach (string possibleUnique in possibleUniques)
        {
            string resultingUnique = possibleUnique;

            var timesParameterWasSeen = new Counter<string>();
            foreach (string parameter in possibleUnique.Replace('<', ' ').GetPlaceholderParameters())
            {
                bool parameterHasSign = parameter.StartsWith('-') || parameter.StartsWith('+');
                string parameterUnsigned = parameterHasSign ? parameter[1..] : parameter;
                int timesSeen = timesParameterWasSeen[parameterUnsigned];

                // When deprecating a unique like "from [amount] to [amount]", we want to replace the first [amount]
                //  in the 'replaceWith' with the first [amount] in the unique, and the second with the second, etc.
                int parameterNumberInDeprecatedUnique = GetNthIndex(parameterUnsigned, deprecatedUniquePlaceholders, timesSeen);

                if (parameterNumberInDeprecatedUnique < 0 || parameterNumberInDeprecatedUnique >= Params.Count) continue;
                timesParameterWasSeen.Add(parameterUnsigned, 1);

                int positionInDeprecatedUnique = deprecatedSourceText.IndexOf($"[{parameterUnsigned}]", StringComparison.Ordinal);
                string replacementText = Params[parameterNumberInDeprecatedUnique];
                if (Type == null || Type.Value.ParameterTypeMap()[parameterNumberInDeprecatedUnique].Contains(UniqueParameterType.Number))
                {
                    // The following looks for a sign just before [amount] and detects replacing "-[-33]" with "[+33]" and similar situations
                    bool deprecatedHadPlusSign = positionInDeprecatedUnique > 0 && deprecatedSourceText[positionInDeprecatedUnique - 1] == '+';
                    bool deprecatedHadMinusSign = positionInDeprecatedUnique > 0 && deprecatedSourceText[positionInDeprecatedUnique - 1] == '-';
                    bool deprecatedHadSign = deprecatedHadPlusSign || deprecatedHadMinusSign;
                    int positionInNewUnique = possibleUnique.IndexOf($"[{parameter}]", StringComparison.Ordinal);
                    bool newHasMinusSign = positionInNewUnique > 0 && possibleUnique[positionInNewUnique - 1] == '-';
                    bool replacementHasMinusSign = replacementText.StartsWith('-');
                    bool replacementHasPlusSign = replacementText.StartsWith('+');
                    bool replacementIsSigned = replacementHasPlusSign || replacementHasMinusSign;
                    string replacementTextUnsigned = replacementIsSigned ? replacementText[1..] : replacementText;
                    bool replacementShouldBeNegative = deprecatedHadMinusSign == newHasMinusSign ? replacementHasMinusSign : !replacementHasMinusSign;
                    bool replacementShouldBeSigned = (deprecatedHadSign && !newHasMinusSign) || parameterHasSign;
                    if (deprecatedHadSign || newHasMinusSign || replacementIsSigned)
                    {
                        if (replacementShouldBeNegative) replacementText = "-" + replacementTextUnsigned;
                        else if (replacementShouldBeSigned) replacementText = "+" + replacementTextUnsigned;
                        else replacementText = replacementTextUnsigned;
                    }
                }
                resultingUnique = ReplaceFirst(resultingUnique, $"[{parameter}]", $"[{replacementText}]");
            }
            finalPossibleUniques.Add(resultingUnique);
        }
        if (finalPossibleUniques.Count == 1) return finalPossibleUniques[0];

        // filter out possible replacements that are obviously wrong
        var uniquesWithNoErrors = finalPossibleUniques.Where(it => hasNoErrors(new Unique(it))).ToList();
        if (uniquesWithNoErrors.Count == 1) return uniquesWithNoErrors[0];

        var uniquesToUnify = uniquesWithNoErrors.Count == 0 ? possibleUniques.ToList() : uniquesWithNoErrors;
        return string.Join("\", \"", uniquesToUnify);
    }

    /// <summary>Kotlin <c>String.replaceFirst(oldValue, newValue)</c>: literal, ordinal, first occurrence only.</summary>
    private static string ReplaceFirst(string str, string oldValue, string newValue)
    {
        int index = str.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? str : str[..index] + newValue + str[(index + oldValue.Length)..];
    }

    private static float ParseFloat(string str) =>
        UniqueParameterTypeExtensions.ToFloatOrNull(str) ?? throw new FormatException($"Invalid float: \"{str}\"");

    private static int ParseInt(string str) =>
        UniqueParameterTypeExtensions.ToIntOrNull(str) ?? throw new FormatException($"Invalid int: \"{str}\"");

    public override string ToString() => Type == null ? $"\"{Text}\"" : $"{Type.Value.Text()} (\"{Text}\")";

    public string GetDisplayText()
    {
        if (!Modifiers.Any(it => it.IsHiddenToUsers())) return Text;
        if (Modifiers.All(it => it.IsHiddenToUsers())) return Text.RemoveConditionals();
        return Text.RemoveConditionals() + " " + string.Join(" ", Modifiers.Where(it => !it.IsHiddenToUsers()).Select(it => $"<{it.Text}>"));
    }
}

public static class UniqueStringExtensions
{
    /// <summary>Kotlin <c>String.getModifiers()</c> (TranslationExtensions.kt): the parts between <c>&lt;</c> and <c>&gt;</c> as Uniques.</summary>
    public static List<Unique> GetModifiers(this string text) =>
        text.GetModifierTexts().Select(it => new Unique(it)).ToList();
}
