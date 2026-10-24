using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Unciv.Models.Translations;

public static class TranslationCache
{
    private static LanguageCache? CachedLangData;

    public static IReadOnlySet<string> ActiveMods => Translations.Current.TranslationActiveMods;

    public static string Language => GetLangData().Language;

    public static LanguageCache GetLangData()
    {
        string currentLang = Translations.Current.CurrentLanguage;
        var data = CachedLangData;
        if (data != null && data.Language == currentLang) return data;
        var newData = LanguageCache.Create(currentLang);
        CachedLangData = newData;
        return newData;
    }

    public static void InvalidateLanguage()
    {
        CachedLangData = null;
    }

    public static LanguageCache GetNumberFormatForLanguage(string language) =>
        LanguageCache.Create(language);

    public sealed class LanguageCache
    {
        public string Language { get; }
        public CultureInfo Culture { get; }
        public char ZeroDigit { get; }
        public bool ConditionalsAfterUnique { get; }
        public bool ShouldCapitalize { get; }
        public string SpaceEquivalent { get; }
        public IReadOnlyDictionary<string, int> SortPriorities { get; }
        public Regex NumberRegex { get; }

        public LanguageCache(
            string language,
            CultureInfo culture,
            char zeroDigit,
            bool conditionalsAfterUnique,
            bool shouldCapitalize,
            string spaceEquivalent,
            IReadOnlyDictionary<string, int> sortPriorities,
            Regex numberRegex)
        {
            Language = language;
            Culture = culture;
            ZeroDigit = zeroDigit;
            ConditionalsAfterUnique = conditionalsAfterUnique;
            ShouldCapitalize = shouldCapitalize;
            SpaceEquivalent = spaceEquivalent;
            SortPriorities = sortPriorities;
            NumberRegex = numberRegex;
        }

        public string Format(int value) => FormatInteger(value);
        public string Format(long value) => FormatInteger(value);
        public string Format(float value) => FormatFloat(value);
        public string Format(double value) => FormatFloat(value);

        private string FormatInteger(long value)
        {
            string raw = value.ToString(CultureInfo.InvariantCulture);
            if (ZeroDigit == '0') return raw;
            return ShiftDigits(raw, ZeroDigit);
        }

        private string FormatFloat(double value)
        {
            string raw = value.ToString("0.###", CultureInfo.InvariantCulture);
            if (ZeroDigit == '0') return raw;
            return ShiftDigits(raw, ZeroDigit);
        }

        public string? Reformat(string numericString)
        {
            var sb = new StringBuilder(numericString.Length);
            bool negative = false;
            int i = 0;
            if (i < numericString.Length && numericString[i] == '-')
            {
                negative = true;
                i++;
            }
            for (; i < numericString.Length; i++)
            {
                char c = numericString[i];
                if (c == ',') continue;
                if (c >= '0' && c <= '9')
                    sb.Append(c);
                else if (ZeroDigit != '0' && c >= ZeroDigit && c <= ZeroDigit + 9)
                    sb.Append((char)('0' + (c - ZeroDigit)));
                else
                    return null;
            }
            if (sb.Length == 0) return null;
            if (!long.TryParse(sb.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long val))
                return null;
            if (negative) val = -val;

            string formatted = val.ToString("#,##0", CultureInfo.InvariantCulture);
            if (ZeroDigit == '0') return formatted;
            return ShiftDigits(formatted, ZeroDigit);
        }

        private static string ShiftDigits(string text, char zeroDigit)
        {
            var res = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9')
                    res.Append((char)(zeroDigit + (c - '0')));
                else
                    res.Append(c);
            }
            return res.ToString();
        }

        public static LanguageCache Create(string language)
        {
            char zeroDigit = GetZeroDigitForLanguage(language);

            var tr = Translations.Current;
            string conditionalOrdering = tr.GetConditionalOrder(language);
            bool conditionalsAfterUnique = tr.PlaceConditionalsAfterUnique(language);
            bool shouldCapitalize = tr.ShouldCapitalize(language);
            string spaceEquivalent = tr.GetSpaceEquivalent(language);

            var sortPriorities = new Dictionary<string, int>(StringComparer.Ordinal);
            int idx = 0;
            foreach (var modifier in conditionalOrdering.GetModifierTexts())
            {
                string key = modifier.GetPlaceholderText();
                if (!sortPriorities.ContainsKey(key))
                {
                    sortPriorities[key] = idx++;
                }
            }

            string digit = zeroDigit == '0' ? "[0-9]" : $"[0-9{zeroDigit}-{(char)(zeroDigit + 9)}]";
            string pattern = $"(?={digit}|-)(?<![A-Za-z0-9_])-?{digit}+(?:,{digit}{{3}})*(?![A-Za-z0-9_])";
            var numberRegex = new Regex(pattern, RegexOptions.CultureInvariant);

            return new LanguageCache(
                language,
                CultureInfo.InvariantCulture,
                zeroDigit,
                conditionalsAfterUnique,
                shouldCapitalize,
                spaceEquivalent,
                sortPriorities,
                numberRegex);
        }

        private static char GetZeroDigitForLanguage(string language) => language switch
        {
            "Bangla" => '০',
            "Arabic" => '٠',
            "Persian_(Pinglish-DIN)" or "Persian_(Pinglish-UN)" => '۰',
            _ => '0'
        };
    }
}
