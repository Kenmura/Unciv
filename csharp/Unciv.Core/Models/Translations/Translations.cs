using System.Collections;

namespace Unciv.Models.Translations;

public sealed class Translations : IEnumerable<KeyValuePair<string, TranslationEntry>>
{
    private readonly Dictionary<string, TranslationEntry> entries = new(StringComparer.Ordinal);

    public static Translations Current { get; set; } = new();

    public string CurrentLanguage { get; set; } = "English";

    public Dictionary<string, int> PercentCompleteOfLanguages { get; set; } =
        new(StringComparer.Ordinal) { ["English"] = 100 };

    internal Dictionary<string, Translations> ModsWithTranslations { get; } = new(StringComparer.Ordinal);

    public HashSet<string> TranslationActiveMods { get; } = new(StringComparer.Ordinal);

    public TranslationEntry? Get(string text, string language, IReadOnlySet<string>? activeMods = null)
    {
        if (activeMods != null)
        {
            foreach (var activeMod in activeMods)
            {
                if (!ModsWithTranslations.TryGetValue(activeMod, out var modTranslations)) continue;
                var modEntry = modTranslations[text];
                if (modEntry != null && modEntry.ContainsKey(language))
                    return modEntry;
            }
        }
        return this[text];
    }

    public string GetText(string text, string language, IReadOnlySet<string>? activeMods = null) =>
        Get(text, language, activeMods)?.GetValueOrDefault(language) ?? text;

    public string GetText(string text, string language, IReadOnlySet<string>? activeMods, string @default) =>
        Get(text, language, activeMods)?.GetValueOrDefault(language) ?? @default;

    public HashSet<string> GetLanguages()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Values)
        {
            foreach (var lang in entry.Keys)
            {
                result.Add(lang);
            }
        }
        return result;
    }

    public void CreateTranslations(string language, IDictionary<string, string> languageTranslations)
    {
        foreach (var (key, value) in languageTranslations)
        {
            string hashKey = (key.Contains('[') && !key.Contains('<'))
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

    public string GetSpaceEquivalent(string language)
    {
        string raw = GetText("\" \"", language, null);
        if (raw.StartsWith('\"') && raw.EndsWith('\"') && raw.Length >= 2)
            return raw[1..^1];
        return raw;
    }

    public bool ShouldCapitalize(string language) =>
        bool.TryParse(GetText(ShouldCapitalizeKey, language, null, "true"), out bool b) && b;

    public bool TriggerNotificationEffectBeforeCause(string language) =>
        bool.TryParse(GetText(EffectBeforeCauseKey, language, null, "true"), out bool b) && b;

    public TranslationEntry? this[string key]
    {
        get => entries.GetValueOrDefault(key);
        set
        {
            if (value == null) entries.Remove(key);
            else entries[key] = value;
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
