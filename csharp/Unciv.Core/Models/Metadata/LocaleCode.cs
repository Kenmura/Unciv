namespace Unciv.Models.Metadata;

// core/src/com/unciv/models/metadata/LocaleCode.kt
//
// Maps an Unciv language key to its IETF BCP 47 tag. Not ported (client side, depend on java.util.Locale and UncivGame):
// locale(), find(Locale), getSystemLanguage(), getLocale(language).

/// <summary>
/// Name <b>should</b> be the translation file name with ' ', '_', '-', '(', ')' removed.
/// </summary>
public enum LocaleCode
{
    Afrikaans,
    Arabic,
    Bangla,
    Belarusian,
    Bosnian,
    BrazilianPortuguese,
    Bulgarian,
    Catalan,
    Croatian,
    Czech,
    Danish,
    Dutch,
    English,
    Esperanto,
    Estonian,
    Filipino,
    Finnish,
    French,
    Galician,
    German,
    Greek,
    Hindi,
    Hungarian,
    Indonesian,
    Italian,
    Japanese,
    Korean,
    Latin,
    Latvian,
    Lithuanian,
    Malay,
    Maltese,
    Navajo,
    Norwegian,
    NorwegianNynorsk,
    PersianPinglishDIN,
    PersianPinglishUN,
    Polish,
    Portuguese,
    Romanian,
    Russian,
    Rusyn,
    Serbian,
    SimplifiedChinese,
    Slovak,
    Spanish,
    Swedish,
    Thai,
    TokiPona,
    TraditionalChinese,
    Turkish,
    Ukrainian,
    Vietnamese,
    Zulu
}

public static class LocaleCodeExtensions
{
    // languageTag, fastlaneFolder override, languageName override, unused; in declaration order of LocaleCode
    private static readonly (string LanguageTag, string? FastlaneFolder, string? LanguageName, bool Unused)[] Info =
    [
        ("af-ZA", null, null, false),  // Afrikaans
        ("ar-IQ", null, null, true),  // Arabic
        ("bn-BD", null, null, false),  // Bangla
        ("be-BY", null, null, false),  // Belarusian
        ("bs-BA", null, null, false),  // Bosnian
        ("pt-BR", null, "Brazilian_Portuguese", false),  // BrazilianPortuguese
        ("bg-BG", null, null, false),  // Bulgarian
        ("ca-ES", null, null, false),  // Catalan
        ("hr-HR", null, null, false),  // Croatian
        ("cs-CZ", null, null, false),  // Czech
        ("da-DK", null, null, true),  // Danish
        ("nl-NL", null, null, false),  // Dutch
        ("en-US", null, null, false),  // English
        ("eo", null, null, false),  // Esperanto
        ("et-EE", null, null, true),  // Estonian
        ("fil-PH-u-co-search", null, null, false),  // Filipino
        ("fi-FI", null, null, false),  // Finnish
        ("fr-FR", null, null, false),  // French
        ("gl-ES", null, null, false),  // Galician
        ("de-DE", null, null, false),  // German
        ("el-GR", null, null, false),  // Greek
        ("hi-IN", null, null, false),  // Hindi
        ("hu-HU", null, null, false),  // Hungarian
        ("in-ID", null, null, false),  // Indonesian
        ("it-IT", null, null, false),  // Italian
        ("ja-JP", null, null, false),  // Japanese
        ("ko-KR", null, null, false),  // Korean
        ("la-IT", null, null, false),  // Latin
        ("lv-LV", null, null, true),  // Latvian
        ("lt-LT", null, null, false),  // Lithuanian
        ("ms-MY", null, null, false),  // Malay
        ("mt-MT", null, null, false),  // Maltese
        ("nv-US", null, null, false),  // Navajo
        ("no-NO", null, null, false),  // Norwegian
        ("nn-NO", null, null, true),  // NorwegianNynorsk
        ("fa-IR", null, "Persian_(Pinglish-DIN)", false),  // PersianPinglishDIN
        ("fa-IR", null, "Persian_(Pinglish-UN)", false),  // PersianPinglishUN
        ("pl-PL", null, null, false),  // Polish
        ("pt-PT", null, null, false),  // Portuguese
        ("ro-RO", null, null, false),  // Romanian
        ("ru-RU", null, null, false),  // Russian
        ("rue-SK-u-kr-cyrl-latn-digit", "rue", null, false),  // Rusyn
        ("sr-RS", null, null, true),  // Serbian
        ("zh-CN", null, "Simplified_Chinese", false),  // SimplifiedChinese
        ("sk-SK", null, null, true),  // Slovak
        ("es-ES", null, null, false),  // Spanish
        ("sv-SE", null, null, false),  // Swedish
        ("th-TH", null, null, false),  // Thai
        ("tok", null, "Toki_Pona", false),  // TokiPona
        ("zh-TW", null, "Traditional_Chinese", false),  // TraditionalChinese
        ("tr-TR", null, null, false),  // Turkish
        ("uk-UA", null, null, false),  // Ukrainian
        ("vi-VN", null, null, false),  // Vietnamese
        ("zu-ZA", null, null, false),  // Zulu
    ];

    /// <summary>All entries in declaration order (Kotlin <c>LocaleCode.entries</c>).</summary>
    public static IReadOnlyList<LocaleCode> Entries { get; } = Enum.GetValues<LocaleCode>();

    /// <summary>IETF BCP 47 language tag; usually the ISO 639-1 code, a dash, and the ISO 3166 code of the main nation.</summary>
    public static string LanguageTag(this LocaleCode code) => Info[(int)code].LanguageTag;

    /// <summary>True for languages without a current translation file.</summary>
    public static bool Unused(this LocaleCode code) => Info[(int)code].Unused;

    /// <summary>The fastlane folder: the override if set, else the language part of the tag (legacy "in" reported as "id", as OpenJDK 17+ does).</summary>
    public static string FastlaneFolder(this LocaleCode code) => Info[(int)code].FastlaneFolder ?? LanguageOf(code.LanguageTag());

    /// <summary>The Unciv language setting and file name: the override if set, else the enum name.</summary>
    public static string LanguageName(this LocaleCode code) => Info[(int)code].LanguageName ?? code.ToString();

    /// <summary>Find a LocaleCode for a language as stored in GameSettings.</summary>
    public static LocaleCode? Find(string language)
    {
        foreach (var code in Entries)
        {
            if (code.LanguageName() == language) return code;
        }
        return null;
    }

    public static IEnumerable<string> GetSupportedLanguages() =>
        Entries.Where(code => !code.Unused()).Select(code => code.LanguageName());

    /// <summary>Get the fastlane folder name for a language as stored in GameSettings.</summary>
    public static string FastlaneFolder(string language) => Find(language)?.FastlaneFolder() ?? "en";

    // Locale.forLanguageTag(tag).language
    private static string LanguageOf(string languageTag)
    {
        string language = languageTag.Split('-')[0];
        return language switch
        {
            "in" => "id", // OpenJDK 17+; older JDKs and Android keep "in"
            "iw" => "he",
            "ji" => "yi",
            _ => language
        };
    }
}
