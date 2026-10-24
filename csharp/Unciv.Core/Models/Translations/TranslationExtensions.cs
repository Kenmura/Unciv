using System.Text;
using Unciv.Models.StatTypes;
using Unciv.Utils;

namespace Unciv.Models.Translations;

public static class TranslationExtensions
{
    public const int MaxConditionalNesting = 666;

    public static string Tr(this string text, bool hideIcons = false, bool hideStats = false) =>
        text.Tr(TranslationCache.Language, hideIcons, hideStats);

    public static string Tr(this string text, string language, bool hideIcons = false, bool hideStats = false)
    {
        int ltIndex = FindNextTopLevel(text, 0, '<');
        if (ltIndex >= 0)
            return TranslateConditionals(text, language, hideIcons, ltIndex);

        return text.TrNoConditionals(language, hideIcons, hideStats);
    }

    public static string TrNoConditionals(this string text, string language, bool hideIcons = false, bool hideStats = false)
    {
        int ltIndex = FindNextTopLevel(text, 0, '<');
        if (ltIndex >= 0 && FindMatchingAnyKind(text, ltIndex) >= 0)
            return TranslateConditionals(text, language, hideIcons, ltIndex);

        int splitIndex = text.IndexOfAny(['[', '{']);
        if (splitIndex < 0)
            return TranslateIndividualWord(text, language, hideIcons, hideStats);

        if (text[splitIndex] == '[')
            return TranslatePlaceholders(text, language, hideIcons, splitIndex);

        return TranslatePartial(text, language, hideIcons, splitIndex);
    }

    private delegate void EmitPair(int start, int end);

    private static void ParsePairs(
        string text,
        char open,
        char close,
        int firstPos,
        bool supportNesting,
        EmitPair emitInside,
        EmitPair emitOutside)
    {
        int pos = 0;
        int openPos = firstPos != int.MinValue
            ? firstPos
            : (supportNesting ? FindNextTopLevel(text, 0, open) : text.IndexOf(open));

        while (openPos >= 0)
        {
            int closePos = supportNesting
                ? FindMatchingAnyKind(text, openPos)
                : text.IndexOf(close, openPos + 1);

            if (closePos < 0) break;

            emitOutside(pos, openPos);
            emitInside(openPos + 1, closePos);
            pos = closePos + 1;
            openPos = supportNesting ? FindNextTopLevel(text, pos, open) : text.IndexOf(open, pos);
        }

        emitOutside(pos, text.Length);
    }

    private static char CloserOf(char c) => c switch
    {
        '[' => ']',
        '<' => '>',
        '{' => '}',
        _ => '\0'
    };

    private static int FindMatchingAnyKind(string text, int openPos)
    {
        char[] stack = new char[8];
        int depth = 0;
        for (int pos = openPos; pos < text.Length; pos++)
        {
            char c = text[pos];
            char closer = CloserOf(c);
            if (closer != '\0')
            {
                if (depth == stack.Length) Array.Resize(ref stack, depth * 2);
                stack[depth++] = closer;
            }
            else if (c == ']' || c == '>' || c == '}')
            {
                int i = depth - 1;
                while (i >= 0 && stack[i] != c) i--;
                if (i >= 0)
                {
                    depth = i;
                    if (depth == 0) return pos;
                }
            }
        }
        return -1;
    }

    private static int FindMatchingSameKind(char open, char close, string text, int openPos)
    {
        int depth = 1;
        for (int pos = openPos + 1; pos < text.Length; pos++)
        {
            char c = text[pos];
            if (c == open) depth++;
            else if (c == close && --depth <= 0) return pos;
        }
        return -1;
    }

    private static int FindNextTopLevel(string text, int from, char open)
    {
        int pos = from;
        while (pos < text.Length)
        {
            char c = text[pos];
            if (c == open) return pos;
            if (CloserOf(c) != '\0')
            {
                int end = FindMatchingAnyKind(text, pos);
                if (end >= 0) pos = end;
            }
            pos++;
        }
        return -1;
    }

    private static void AppendTrimming(StringBuilder sb, string input, int start, int end)
    {
        if (start >= end || start >= input.Length) return;
        while (start < end && start > 0 && input[start] == ' ') start++;
        sb.Append(input, start, end - start);
    }

    private static void TrimEnd(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1].IsKotlinWhitespace())
        {
            sb.Length--;
        }
    }

    private static string TranslatePartial(string text, string language, bool hideIcons, int firstPos)
    {
        var sb = new StringBuilder(text.Length + 16);
        ParsePairs(text, '{', '}', firstPos, true,
            emitInside: (start, end) => sb.Append(text[start..end].Tr(language, hideIcons)),
            emitOutside: (start, end) => sb.Append(text, start, end - start)
        );
        return sb.ToString();
    }

    private static string TranslatePlaceholders(string text, string language, bool hideIcons, int firstPos)
    {
        var key = new StringBuilder(text.Length);
        var termsInMessage = new List<string>();

        ParsePairs(text, '[', ']', firstPos, true,
            emitInside: (start, end) =>
            {
                termsInMessage.Add(text[start..end]);
                key.Append("[]");
            },
            emitOutside: (start, end) => key.Append(text, start, end - start)
        );

        var translationEntry = Translations.Current.Get(key.ToString(), language, TranslationCache.ActiveMods);

        string translatedWithPlaceholders;
        string originalWithPlaceholders;
        if (translationEntry != null && translationEntry.TryGetValue(language, out var langEntry) && langEntry != null)
        {
            translatedWithPlaceholders = langEntry;
            originalWithPlaceholders = translationEntry.Entry;
        }
        else
        {
            translatedWithPlaceholders = text;
            originalWithPlaceholders = text;
        }

        var termsInTranslationPlaceholder = new List<string>();
        ParsePairs(originalWithPlaceholders, '[', ']', int.MinValue, true,
            emitInside: (start, end) => termsInTranslationPlaceholder.Add(originalWithPlaceholders[start..end]),
            emitOutside: (_, _) => { }
        );

        if (termsInMessage.Count != termsInTranslationPlaceholder.Count)
            throw new InvalidOperationException($"Message {text} has a different number of terms ({termsInMessage.Count}) than the placeholder {translationEntry?.Entry} ({termsInTranslationPlaceholder.Count})!");

        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < termsInMessage.Count; i++)
        {
            string translated = termsInMessage[i].Tr(language, hideIcons);
            substitutions.TryAdd(termsInTranslationPlaceholder[i], translated);
        }

        var sb = new StringBuilder(translatedWithPlaceholders.Length + 16);
        ParsePairs(translatedWithPlaceholders, '[', ']', int.MinValue, true,
            emitInside: (start, end) =>
            {
                string term = translatedWithPlaceholders[start..end];
                if (substitutions.TryGetValue(term, out var replacement))
                    sb.Append(replacement);
                else
                    sb.Append(translatedWithPlaceholders, start, end - start);
            },
            emitOutside: (start, end) => sb.Append(translatedWithPlaceholders, start, end - start)
        );
        return sb.ToString();
    }

    private static string TranslateConditionals(string text, string language, bool hideIcons, int firstPos)
    {
        var langData = TranslationCache.GetLangData();
        string localeLanguage = langData.LocaleLanguage;
        bool conditionalsAfterUnique = langData.ConditionalsAfterUnique;
        bool shouldCapitalize = langData.ShouldCapitalize;
        string spaceEquivalent = langData.SpaceEquivalent;
        var sortPriorities = langData.SortPriorities;

        var conditionalContents = new List<string>();
        var baseSb = new StringBuilder(text.Length);
        ParsePairs(text, '<', '>', firstPos, true,
            emitInside: (start, end) => conditionalContents.Add(text[start..end]),
            emitOutside: (start, end) => AppendTrimming(baseSb, text, start, end)
        );
        TrimEnd(baseSb);

        int GetSortPriority(string modifierText) =>
            sortPriorities.TryGetValue(modifierText.GetPlaceholderText(), out int priority) ? priority : int.MaxValue;

        var sortedConditionals = conditionalContents.OrderBy(GetSortPriority).ToList();
        var translatedConditionals = new List<string>(sortedConditionals.Count);
        foreach (var cond in sortedConditionals)
        {
            translatedConditionals.Add(cond.Tr(language, hideIcons));
        }

        string translatedBaseUnique = baseSb.ToString().TrNoConditionals(language, hideIcons);
        var sb = new StringBuilder(text.Length + 16);

        // Char.uppercase(locale) / lowercase(locale): locale dependent and possibly more than one character
        void AppendChangingFirstChar(string s, Func<char, string, string> transform)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (!shouldCapitalize)
            {
                sb.Append(s);
                return;
            }
            sb.Append(transform(s[0], localeLanguage));
            if (s.Length > 1)
                sb.Append(s, 1, s.Length - 1);
        }

        if (conditionalsAfterUnique)
            AppendChangingFirstChar(translatedBaseUnique, JavaCasing.ToUpper);

        bool capitalizeOnce = !conditionalsAfterUnique;
        foreach (var translatedConditional in translatedConditionals)
        {
            if (capitalizeOnce)
            {
                AppendChangingFirstChar(translatedConditional, JavaCasing.ToUpper);
                capitalizeOnce = false;
            }
            else
            {
                sb.Append(spaceEquivalent);
                sb.Append(translatedConditional);
            }
        }

        if (!conditionalsAfterUnique)
        {
            sb.Append(spaceEquivalent);
            AppendChangingFirstChar(translatedBaseUnique, JavaCasing.ToLower);
        }

        return sb.ToString();
    }

    private static string TranslateIndividualWord(string text, string language, bool hideIcons, bool hideStatIcons)
    {
        if (Stats.IsStats(text))
        {
            var stats = Stats.Parse(text);
            return hideStatIcons ? stats.ToStringWithoutIcons() : stats.ToString();
        }

        string translationBeforeNumberScan = Translations.Current.GetText(text, language, TranslationCache.ActiveMods);
        string translation;
        if (!ContainsAnyDigit(translationBeforeNumberScan))
        {
            translation = translationBeforeNumberScan;
        }
        else
        {
            var langData = TranslationCache.GetLangData();
            translation = langData.NumberRegex.Replace(translationBeforeNumberScan, m =>
                langData.Reformat(m.Value) ?? m.Value
            );
        }

        var stat = StatExtensions.SafeValueOf(text);
        if (!hideStatIcons && stat != null)
            return stat.Value.Character() + translation;

        if (hideIcons) return translation;

        if (FontRulesetIcons.RulesetObjectNameToChar.TryGetValue(text, out char iconChar))
            return iconChar + translation;

        return translation;
    }

    private static bool ContainsAnyDigit(string text)
    {
        foreach (char c in text)
        {
            if (char.IsDigit(c)) return true;
        }
        return false;
    }

    public static List<string> GetPlaceholderParameters(this string text)
    {
        string stringToParse = text.RemoveConditionals();
        int firstPos = stringToParse.IndexOf('[');
        if (firstPos < 0) return [];

        var parameters = new List<string>();
        ParsePairs(stringToParse, '[', ']', firstPos, true,
            emitInside: (start, end) => parameters.Add(stringToParse[start..end]),
            emitOutside: (_, _) => { }
        );
        return parameters;
    }

    public static string GetPlaceholderText(this string text)
    {
        string stringToParse = text.RemoveConditionals();
        int firstPos = stringToParse.IndexOf('[');
        if (firstPos < 0) return stringToParse;

        // Unlike getPlaceholderParameters, the scan starts at the first top-level '[', skipping any inside a complete {} or <> group
        var key = new StringBuilder(stringToParse.Length);
        ParsePairs(stringToParse, '[', ']', int.MinValue, true,
            emitInside: (_, _) => key.Append("[]"),
            emitOutside: (start, end) => key.Append(stringToParse, start, end - start)
        );
        return key.ToString();
    }

    public static bool EqualsPlaceholderText(this string text, string str)
    {
        if (text.Length == 0) return str.Length == 0;
        if (str.Length == 0) return false;
        if (text[0] != str[0]) return false;
        return text.GetPlaceholderText() == str;
    }

    public static bool HasPlaceholderParameters(this string text)
    {
        if (!text.Contains('[')) return false;
        string withoutConds = text.RemoveConditionals();
        int openPos = withoutConds.IndexOf('[');
        return openPos >= 0 && FindMatchingSameKind('[', ']', withoutConds, openPos) >= 0;
    }

    public static string FillPlaceholders(this string text, params string[] strings)
    {
        var sb = new StringBuilder(text.Length + 16);
        int index = 0;
        void SubstituteNext()
        {
            if (index < strings.Length)
            {
                sb.Append('[');
                sb.Append(strings[index]);
                sb.Append(']');
            }
            else
            {
                sb.Append("[]");
            }
            index++;
        }

        ParsePairs(text, '[', ']', int.MinValue, true,
            emitInside: (_, _) => SubstituteNext(),
            emitOutside: (start, end) => sb.Append(text, start, end - start)
        );

        if (index > strings.Length)
            throw new ArgumentException($"String {text} has a different number of placeholders ({index}) than the substitutive strings {string.Join(", ", strings)} ({strings.Length})!");

        return sb.ToString();
    }

    public static List<string> GetPartsInsideCurlies(this string text)
    {
        var result = new List<string>();
        ParsePairs(text, '{', '}', int.MinValue, true,
            emitInside: (start, end) => result.Add(text[start..end]),
            emitOutside: (_, _) => { }
        );
        return result;
    }

    // TODO(T027): String.getModifiers() (the same scan, producing Unique objects) once Unique exists
    public static IEnumerable<string> GetModifierTexts(this string text)
    {
        var result = new List<string>();
        ParsePairs(text, '<', '>', int.MinValue, true,
            emitInside: (start, end) => result.Add(text[start..end]),
            emitOutside: (_, _) => { }
        );
        return result;
    }

    public static string RemoveConditionals(this string text)
    {
        int firstPos = FindNextTopLevel(text, 0, '<');
        if (firstPos < 0) return text;
        var sb = new StringBuilder(text.Length);
        ParsePairs(text, '<', '>', firstPos, true,
            emitInside: (_, _) => { },
            emitOutside: (start, end) => AppendTrimming(sb, text, start, end)
        );
        TrimEnd(sb);
        return sb.ToString();
    }

    public static string? ValidateConditionals(this string text) => ValidateConditionals(text, 0);

    private static string? ValidateConditionals(string text, int recursionDepth)
    {
        if (!text.Contains('<')) return null;
        string baseText = text.RemoveConditionals();
        if (baseText.Contains('<') || baseText.Contains('>'))
            return "mismatched conditional braces";
        if (!text.KotlinTrimStart().StartsWith(baseText.KotlinTrimStart(), StringComparison.Ordinal))
            return "text between or after conditionals";
        if (recursionDepth >= MaxConditionalNesting)
            return "excessive nesting of conditionals";

        foreach (var modifier in text.GetModifierTexts())
        {
            string? error = ValidateConditionals(modifier, recursionDepth + 1);
            if (error != null) return error;
        }
        return null;
    }

    public static string Tr(this int number, string? language = null) =>
        language == null ? TranslationCache.GetLangData().Format(number) : TranslationCache.GetNumberFormatForLanguage(language).Format(number);

    public static string Tr(this long number, string? language = null) =>
        language == null ? TranslationCache.GetLangData().Format(number) : TranslationCache.GetNumberFormatForLanguage(language).Format(number);

    public static string Tr(this float number, string? language = null) =>
        language == null ? TranslationCache.GetLangData().Format(number) : TranslationCache.GetNumberFormatForLanguage(language).Format(number);

    public static string Tr(this double number, string? language = null) =>
        language == null ? TranslationCache.GetLangData().Format(number) : TranslationCache.GetNumberFormatForLanguage(language).Format(number);
}
