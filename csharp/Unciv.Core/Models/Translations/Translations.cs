using System.Collections;
using Unciv.Models.Metadata;
using Unciv.Utils;

namespace Unciv.Models.Translations;

/// <summary>
/// This collection holds all translations for the game. Ported from <c>Translations.kt</c> (a <c>LinkedHashMap</c>, hence ordered).
/// </summary>
/// <remarks>
/// Translatables have two forms: normal or containing placeholders (delimited by square brackets).
/// Translation _requests_ can have a third form: containing {subsentences} - these are split into their
/// components and passed individually back into the translator, therefore they need no entry in this collection.
/// The key is a copy of the translatable string in the normal case. For the placeholder case, the key is modified: placeholders reduced to empty [] pairs.
/// <para>
/// Not ported, by decision (T023): <c>DiacriticSupport</c> (it remaps characters to fake glyphs for libGDX bitmap fonts, a client rendering
/// concern; <c>createTranslations</c> therefore has no <c>noDiacritics</c> parameter), and the libGDX file lookups
/// (<c>Gdx.files.internal</c>), which are replaced by <see cref="ITranslationFileHandle"/>.
/// </para>
/// </remarks>
public sealed class Translations : IEnumerable<KeyValuePair<string, TranslationEntry>>
{
    private readonly OrderedDictionary<string, TranslationEntry> entries = new(StringComparer.Ordinal);

    /// <summary>Stands in for <c>UncivGame.Current.translations</c>.</summary>
    public static Translations Current { get; set; } = new();

    /// <summary>Stands in for <c>UncivGame.Current.settings.language</c>.</summary>
    public string CurrentLanguage { get; set; } = Constants.English;

    /// <summary>The completion percentage for each language, read from <c>completionPercentages.properties</c>.</summary>
    public Dictionary<string, int> PercentCompleteOfLanguages { get; set; } =
        new(StringComparer.Ordinal) { [Constants.English] = 100 }; // So even if we don't manage to load the percentages, we can still pass the language screen

    internal Dictionary<string, Translations> ModsWithTranslations { get; } = new(StringComparer.Ordinal); // key == mod name

    /// <summary>Used by <c>tr()</c> whenever GameInfo is not initialized (allowing new game screen to use mod translations).</summary>
    public LinkedHashSet<string> TranslationActiveMods { get; set; } = [];

    /// <summary>
    /// Searches for the translation entry of a given <paramref name="text"/> for a given <paramref name="language"/>.
    /// This includes translations provided by mods from <paramref name="activeMods"/>, in their iteration order (the first mod that has one wins).
    /// </summary>
    /// <returns>the translation entry or null when not available</returns>
    public TranslationEntry? Get(string text, string language, IEnumerable<string>? activeMods = null)
    {
        if (activeMods != null)
        {
            foreach (string activeMod in activeMods)
            {
                if (!ModsWithTranslations.TryGetValue(activeMod, out var modTranslations))
                {
                    continue;
                }

                var translationEntry = modTranslations[text];
                if (translationEntry != null && translationEntry.ContainsKey(language))
                {
                    return translationEntry;
                }
            }
        }

        return this[text];
    }

    /// <summary>Returns a specific translation, or the original <paramref name="text"/> if no translation for <paramref name="language"/> exists.</summary>
    public string GetText(string text, string language, IEnumerable<string>? activeMods = null) =>
        Get(text, language, activeMods)?.GetValueOrDefault(language) ?? text;

    /// <summary>Returns a specific translation, or <paramref name="default"/> if no translation for <paramref name="language"/> exists.</summary>
    public string GetText(string text, string language, IEnumerable<string>? activeMods, string @default) =>
        Get(text, language, activeMods)?.GetValueOrDefault(language) ?? @default;

    /// <summary>All languages present in this; no deterministic order (a Kotlin <c>hashSetOf</c>).</summary>
    public HashSet<string> GetLanguages()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Values)
        {
            foreach (string language in entry.Keys)
            {
                result.Add(language);
            }
        }

        return result;
    }

    /// <summary>
    /// This reads all translations for a specific language, including the given mods' translation files.
    /// Vanilla translations go into <c>this</c> instance, mod translations into <see cref="ModsWithTranslations"/>.
    /// </summary>
    /// <param name="language">The language name as used in <c>LocaleCode</c>.</param>
    /// <param name="languageFile">The <c>jsons/translations/$language.properties</c> of the game; nothing happens if it does not exist.</param>
    /// <param name="modFiles">Per mod folder name, its <c>jsons/translations/$language.properties</c> (TODO(T034): the caller will be RulesetCache, which knows the mod folders).</param>
    public void TryReadTranslationForLanguage(
        string language,
        ITranslationFileHandle languageFile,
        IEnumerable<(string ModName, ITranslationFileHandle File)> modFiles)
    {
        TranslationCache.InvalidateLanguage();

        if (!languageFile.Exists)
        {
            return;
        }

        // On some devices we get a weird UnsupportedEncodingException upstream, which is logged and the read abandoned
        OrderedDictionary<string, string> languageTranslations;
        try
        {
            languageTranslations = TranslationFileReader.Read(languageFile);
        }
        catch (Exception ex)
        {
            Log.Error($"Exception reading translations for {language}", ex);
            return;
        }

        foreach (var (modName, modFile) in modFiles)
        {
            if (!modFile.Exists)
            {
                continue;
            }

            if (!ModsWithTranslations.TryGetValue(modName, out var translationsForMod))
            {
                translationsForMod = new Translations();
                ModsWithTranslations[modName] = translationsForMod;
            }

            try
            {
                translationsForMod.CreateTranslations(language, TranslationFileReader.Read(modFile));
            }
            catch (Exception ex)
            {
                Log.Error($"Exception reading translations for {modName} {language}", ex);
            }
        }

        CreateTranslations(language, languageTranslations);
    }

    public void CreateTranslations(string language, IEnumerable<KeyValuePair<string, string>> languageTranslations)
    {
        foreach (var (key, value) in languageTranslations)
        {
            string hashKey = key.Contains('[') && !key.Contains('<')
                ? key.GetPlaceholderText()
                : key;

            if (!entries.TryGetValue(hashKey, out var entry))
            {
                entry = new TranslationEntry(key);
                entries[hashKey] = entry;
            }

            entry[language] = value;
        }
    }

    /// <summary>
    /// Ensure _all_ languages are loaded, used by the translation file writer and the translation tests only. Expects to run on a newly created instance.
    /// </summary>
    /// <param name="languageFileProvider">Gives the <c>jsons/translations/$language.properties</c> of a language.</param>
    public void ReadAllLanguagesTranslation(Func<string, ITranslationFileHandle> languageFileProvider)
    {
        foreach (string language in LocaleCodeExtensions.GetSupportedLanguages().ToList())
        {
            var file = languageFileProvider(language);
            if (file.Exists)
            {
                TryReadTranslationForLanguage(language, file, []);
            }
        }
    }

    public void LoadPercentageCompleteOfLanguages(ITranslationFileHandle? percentagesFile)
    {
        PercentCompleteOfLanguages = TranslationFileReader.ReadLanguagePercentages(percentagesFile);
    }

    public const string ConditionalOrderingKey = "ConditionalsOrder";

    public const string DefaultConditionalOrderingString =
        "<with a garrison> <for [mapUnitFilter] units> <when above [amount] HP> <when below [amount] HP> <vs cities> <vs [mapUnitFilter] units> <when fighting in [tileFilter] tiles> <when attacking> <when defending> <when at war> <when not at war> <while the empire is happy> <during a Golden Age> <during the [era]> <starting from the [era]> <before the [era]> <with [techOrPolicy]> <without [techOrPolicy]>";

    public const string ConditionalPlacementKey = "ConditionalsPlacement";

    public const string ShouldCapitalizeKey = "StartWithCapitalLetter";

    public const string EffectBeforeCauseKey = "EffectBeforeCause";

    public string GetConditionalOrder(string language) =>
        GetText(ConditionalOrderingKey, language, null, DefaultConditionalOrderingString);

    public bool PlaceConditionalsAfterUnique(string language) =>
        GetText(ConditionalPlacementKey, language, null, "") != "before";

    /// <summary>Returns the equivalent of a space in the given language. Defaults to a space if no translation is provided.</summary>
    public string GetSpaceEquivalent(string language)
    {
        string raw = GetText("\" \"", language, null);
        // Kotlin removeSurrounding("\""): both quotes or nothing
        return raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw;
    }

    public bool ShouldCapitalize(string language) =>
        ToBoolean(GetText(ShouldCapitalizeKey, language, null, "true"));

    public bool TriggerNotificationEffectBeforeCause(string language) =>
        ToBoolean(GetText(EffectBeforeCauseKey, language, null, "true"));

    // Kotlin String.toBoolean(): equals("true", ignoreCase = true), nothing else counts (no trimming)
    private static bool ToBoolean(string text) => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

    public TranslationEntry? this[string key]
    {
        get => entries.GetValueOrDefault(key);
        set
        {
            if (value == null)
            {
                entries.Remove(key);
            }
            else
            {
                entries[key] = value;
            }
        }
    }

    public bool ContainsKey(string key) => entries.ContainsKey(key);

    public bool TryGetValue(string key, out TranslationEntry? value) => entries.TryGetValue(key, out value);

    public int Count => entries.Count;

    public IEnumerable<string> Keys => entries.Keys;

    public IEnumerable<TranslationEntry> Values => entries.Values;

    public void Clear() => entries.Clear();

    public IEnumerator<KeyValuePair<string, TranslationEntry>> GetEnumerator() => entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
