using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unciv.Logic;
using Unciv.Utils;

namespace Unciv.Models.Translations;

/// <summary>
/// Caches repeatedly used values that are only dependent on the current language - call <see cref="GetLangData"/> to get a
/// <see cref="LanguageCache"/> with <c>ConditionalsAfterUnique</c>, <c>ShouldCapitalize</c>, <c>SpaceEquivalent</c>, <c>SortPriorities</c>,
/// <c>NumberRegex</c>, <c>Format</c>, <c>Reformat</c>. Ported from <c>TranslationCache.kt</c>; the language setting comes from
/// <see cref="Translations.CurrentLanguage"/> instead of <c>UncivGame.Current.settings</c>.
/// </summary>
public static class TranslationCache
{
    private static LanguageCache? CachedLangData;

    /// <summary>
    /// Mods active in the current game, or set by MainMenuScreen via <see cref="Translations.TranslationActiveMods"/>.
    /// </summary>
    public static LinkedHashSet<string> ActiveMods
    {
        get
        {
            var cache = Translations.Current.ActiveModsCache;
            if (cache?.IsValid() == true)
            {
                return cache.ActiveMods;
            }

            var newCache = ActiveModsCache.Create();
            Translations.Current.ActiveModsCache = newCache;
            return newCache.ActiveMods;
        }
    }

    public static string Language => GetLangData().Language;

    public static LanguageCache GetLangData()
    {
        string setting = Translations.Current.CurrentLanguage;
        var data = CachedLangData;
        if (data != null && data.Language == setting)
        {
            return data;
        }

        var newData = LanguageCache.Create(setting);
        CachedLangData = newData;
        return newData;
    }

    /// <summary>Call this when updating translation tables.</summary>
    public static void InvalidateLanguage()
    {
        CachedLangData = null;
    }

    /// <summary>Uncached, bypassing the cached language, for unit tests.</summary>
    public static JavaDecimalFormat GetNumberFormatForLanguage(string language) =>
        new(SymbolsFor(language));

    /// <summary>
    /// <c>NumberFormat.getInstance(LocaleCode.getLocale(language))</c>. Upstream falls back to <c>Locale.getDefault()</c> for a language that is not
    /// a LocaleCode, which depends on the machine; the port uses the English data.
    /// </summary>
    internal static JavaNumberSymbols SymbolsFor(string language) =>
        JavaLocaleData.NumberSymbols.TryGetValue(language, out var symbols) ? symbols : JavaLocaleData.NumberSymbols["English"];

    public sealed class LanguageCache
    {
        public string Language { get; }

        /// <summary>The language subtag of <c>LocaleCode.getLocale(language)</c>, selects the case mapping.</summary>
        public string LocaleLanguage { get; }

        public bool ConditionalsAfterUnique { get; }

        public bool ShouldCapitalize { get; }

        public string SpaceEquivalent { get; }

        public IReadOnlyDictionary<string, int> SortPriorities { get; }

        /// <summary>A Regex able to find numbers that were formatted in the current language _or_ plain positive ASCII digit numbers.</summary>
        public Regex NumberRegex { get; }

        private readonly JavaDecimalFormat formatter;

        private LanguageCache(
            string language,
            JavaNumberSymbols symbols,
            bool conditionalsAfterUnique,
            bool shouldCapitalize,
            string spaceEquivalent,
            IReadOnlyDictionary<string, int> sortPriorities)
        {
            Language = language;
            LocaleLanguage = symbols.LocaleLanguage;
            ConditionalsAfterUnique = conditionalsAfterUnique;
            ShouldCapitalize = shouldCapitalize;
            SpaceEquivalent = spaceEquivalent;
            SortPriorities = sortPriorities;
            formatter = new JavaDecimalFormat(symbols);
            NumberRegex = BuildNumberRegex(formatter);
        }

        /// <summary>The (stateless) number formatter of the language, Kotlin's <c>getMutableFormatter</c>; configure per call via the <see cref="JavaDecimalFormat"/> overloads.</summary>
        public JavaDecimalFormat NumberFormatter => formatter;

        public string Format(int value) => formatter.Format((long)value);

        public string Format(long value) => formatter.Format(value);

        /// <summary>A Kotlin <c>Float</c> boxed as <c>Number</c> is formatted through its <c>double</c> value.</summary>
        public string Format(float value) => formatter.Format((double)value);

        public string Format(double value) => formatter.Format(value);

        /// <summary>Reformat a number given as plain ASCII digits or in this language's format; null if it doesn't parse completely.</summary>
        public string? Reformat(string numericString)
        {
            int index = 0;
            object? number = formatter.Parse(numericString, ref index);
            if (number == null)
            {
                return null;
            }

            return index == numericString.Length ? formatter.Format(number) : null;
        }

        public static LanguageCache Create(string language)
        {
            var symbols = SymbolsFor(language);

            var tr = Translations.Current;
            string conditionalOrdering = tr.GetConditionalOrder(language);
            bool conditionalsAfterUnique = tr.PlaceConditionalsAfterUnique(language);
            bool shouldCapitalize = tr.ShouldCapitalize(language);
            string spaceEquivalent = tr.GetSpaceEquivalent(language);

            // Somewhere, we asked the translators to reorder all possible conditionals in a way that
            // makes sense in their language. We get this ordering, and map their placeholderText to their ordinal.
            // A repeated placeholderText keeps the last position (buildMap { put } semantics).
            var sortPriorities = new Dictionary<string, int>(StringComparer.Ordinal);
            int index = 0;
            foreach (string text in conditionalOrdering.GetModifierTexts())
            {
                sortPriorities[text.GetPlaceholderText()] = index++;
            }

            return new LanguageCache(language, symbols, conditionalsAfterUnique, shouldCapitalize, spaceEquivalent, sortPriorities);
        }

        private static void Quoted(StringBuilder sb, string text) => sb.Append(Regex.Escape(text));

        private static void Optional(StringBuilder sb, string text)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (text.Length == 1)
            {
                Quoted(sb, text);
                sb.Append('?');
            }
            else
            {
                sb.Append("(?:");
                Quoted(sb, text);
                sb.Append(")?");
            }
        }

        private static Regex BuildNumberRegex(JavaDecimalFormat format)
        {
            char zero = format.ZeroDigit;
            // ASCII plus the locale's own digits, NOT \p{Nd}, because that would re-format e.g. "２"
            string digit = zero == '0' ? "[0-9]" : $"[0-9{zero}-{(char)(zero + 9)}]";
            var pattern = new StringBuilder();
            // Since we might include a "-", a "\b" won't do:
            // First, a look-ahead for a (unicode) digit or negativePrefix mitigates the extra cost of a look-behind.
            pattern.Append("(?=").Append(digit);
            if (format.NegativePrefix.Length > 0)
            {
                pattern.Append('|');
                Quoted(pattern, format.NegativePrefix);
            }

            pattern.Append(')');
            // Now, do a look-behind for "not a (ascii) letter or digit"
            pattern.Append("(?<![A-Za-z0-9_])");
            // Main number pattern
            Optional(pattern, format.NegativePrefix);
            pattern.Append(digit).Append('+');
            pattern.Append("(?:");
            Quoted(pattern, format.GroupingSeparator.ToString());
            pattern.Append(digit).Append('{').Append(format.GroupingSize.ToString(CultureInfo.InvariantCulture)).Append("})*");
            Optional(pattern, format.NegativeSuffix);
            // Ensure not followed by (ascii) letter or digit, more portable than "\b"
            pattern.Append("(?![A-Za-z0-9_])");
            return new Regex(pattern.ToString(), RegexOptions.CultureInvariant);
        }
    }

    internal sealed class ActiveModsCache
    {
        public LinkedHashSet<string> ActiveMods { get; }
        private readonly int cachedHash;

        public ActiveModsCache(LinkedHashSet<string> activeMods, int cachedHash)
        {
            ActiveMods = activeMods;
            this.cachedHash = cachedHash;
        }

        public bool IsValid() => cachedHash == GetCurrentHash();

        public static ActiveModsCache Create()
        {
            var gameInfo = Translations.Current.CurrentGame;
            var fallbackMods = Translations.Current.TranslationActiveMods;
            int hash = GetCurrentHash(gameInfo, fallbackMods);
            var mods = GetCurrentSet(gameInfo, fallbackMods);
            return new ActiveModsCache(mods, hash);
        }

        private static int GetCurrentHash() =>
            GetCurrentHash(Translations.Current.CurrentGame, Translations.Current.TranslationActiveMods);

        private static int GetCurrentHash(GameInfo? gameInfo, LinkedHashSet<string>? fallbackMods)
        {
            if (gameInfo?.GameParameters != null)
            {
                int modsHash = gameInfo.GameParameters.Mods?.GetHashCode() ?? 0;
                int baseRulesetHash = gameInfo.GameParameters.BaseRuleset == null ? 0 : JavaHash.HashCode(gameInfo.GameParameters.BaseRuleset);
                return Randomness.HashOf(modsHash, baseRulesetHash);
            }

            return fallbackMods?.GetHashCode() ?? 0;
        }

        private static LinkedHashSet<string> GetCurrentSet(GameInfo? gameInfo, LinkedHashSet<string>? fallbackMods)
        {
            if (gameInfo?.GameParameters == null)
            {
                return fallbackMods ?? [];
            }

            var par = gameInfo.GameParameters;
            var set = new LinkedHashSet<string>();
            if (par.Mods != null)
            {
                set.AddAll(par.Mods);
            }

            if (par.BaseRuleset != null)
            {
                set.Add(par.BaseRuleset);
            }

            return set;
        }
    }
}
