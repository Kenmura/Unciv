using Unciv.Models.Translations;

namespace Unciv.Assets.Localization;

/// <summary>A language the player can pick: its name (as in the settings file and <c>Translations</c>), and how complete its translation is.</summary>
public sealed record LanguageInfo(string Name, int PercentComplete)
{
    /// <summary>The name for a picker row: underscores read as spaces ("Simplified_Chinese" shows as "Simplified Chinese").</summary>
    public string Display => Name.Replace('_', ' ');
}

/// <summary>
/// C025: the client's translations. Reads the game's <c>jsons/translations/*.properties</c> files from one directory into the engine's
/// <see cref="Translations"/> (the one <c>Tr()</c> uses), tracks the selected language, and answers what languages exist and which scripts they need.
/// Languages are read on first use (the whole set is about 5 MB of text; the player only ever needs one). English is the key itself, as upstream:
/// <c>English.properties</c> has no values, so <see cref="Tr"/> returns an untranslated key unchanged, which is also what any missing entry does.
/// </summary>
public sealed class ClientLocalization
{
    public const string English = "English";
    private const string Extension = ".properties";
    private readonly string directory;
    private readonly HashSet<string> loaded = new(StringComparer.Ordinal);

    public ClientLocalization(string translationsDirectory, Translations? target = null)
    {
        directory = translationsDirectory;
        Translations = target ?? Translations.Current;
        Translations.Current = Translations;
        Reload();
    }

    public Translations Translations { get; }

    public string Language => Translations.CurrentLanguage;

    /// <summary>Raised after <see cref="SetLanguage"/> changed the language, so screens that already built their text can rebuild it.</summary>
    public event Action<string>? LanguageChanged;

    /// <summary>The languages that have a translation file, English first and then by display name; each with the completion percentage from <c>completionPercentages.properties</c> (100 for English, 0 when unlisted).</summary>
    public IReadOnlyList<LanguageInfo> Languages { get; private set; } = [];

    private void Reload()
    {
        loaded.Clear();
        Translations.Clear();
        string percentFile = Path.Combine(directory, "completionPercentages" + Extension);
        if (File.Exists(percentFile)) Translations.LoadPercentageCompleteOfLanguages(FileHandle(percentFile));
        var names = new List<string>();
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*" + Extension))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name is "completionPercentages" or "template") continue;
                names.Add(name);
            }
        }
        if (!names.Contains(English, StringComparer.Ordinal)) names.Add(English);
        Languages = names
            .OrderBy(n => n == English ? 0 : 1)
            .ThenBy(n => n, StringComparer.Ordinal)
            .Select(n => new LanguageInfo(n, n == English ? 100 : Translations.PercentCompleteOfLanguages.GetValueOrDefault(n)))
            .ToList();
    }

    public bool IsAvailable(string language) => Languages.Any(l => l.Name == language);

    /// <summary>
    /// Selects <paramref name="language"/> (loading its file if needed). An unknown language selects English, so a settings file naming a language that
    /// was removed still works. Returns the language now in effect.
    /// </summary>
    public string SetLanguage(string language)
    {
        if (!IsAvailable(language)) language = English;
        Load(language);
        if (Translations.CurrentLanguage == language) return language;
        Translations.CurrentLanguage = language;
        TranslationCache.InvalidateLanguage();
        LanguageChanged?.Invoke(language);
        return language;
    }

    /// <summary>Translates <paramref name="key"/> into the current language, exactly as the engine's <c>Tr()</c> does.</summary>
    public string Tr(string key) => key.Tr();

    /// <summary>Loads a language's file into <see cref="Translations"/> if it is not there yet. Returns false when it has no file (English, or a name that does not exist).</summary>
    public bool Load(string language)
    {
        if (loaded.Contains(language)) return true;
        if (!IsAvailable(language)) return false;
        string path = FilePath(language);
        if (!File.Exists(path)) return false;
        Translations.TryReadTranslationForLanguage(language, FileHandle(path), []);
        loaded.Add(language);
        return true;
    }

    /// <summary>Every translated value of <paramref name="language"/> (not the keys), for checks that need all the text a language can show. Empty for a language without a file.</summary>
    public IReadOnlyList<string> Values(string language)
    {
        string path = FilePath(language);
        return IsAvailable(language) && File.Exists(path) ? TranslationFileReader.Read(File.ReadAllText(path)).Where(it => it.Key != TranslationFileReader.CommentUniqueText).Select(it => it.Value).ToList() : [];
    }

    /// <summary>The distinct code points a font must cover to show every value of <paramref name="language"/> (see <see cref="ScriptClassifier.RequiredCodePoints"/>), ascending.</summary>
    public IReadOnlyList<int> RequiredCodePoints(string language) =>
        Values(language).SelectMany(ScriptClassifier.RequiredCodePoints).Distinct().Order().ToList();

    /// <summary>The scripts, other than Common, that <paramref name="language"/>'s text needs, in enum order.</summary>
    public IReadOnlyList<TextScript> ScriptsOf(string language) =>
        RequiredCodePoints(language).Select(ScriptClassifier.Of).Where(s => s != TextScript.Common).Distinct().Order().ToList();

    /// <summary>True when the language's text is written right to left (its values use Arabic or Hebrew letters); the client then mirrors its layout.</summary>
    public bool IsRightToLeft(string language) => ScriptsOf(language).Any(s => s is TextScript.Arabic or TextScript.Hebrew);

    private string FilePath(string language) => Path.Combine(directory, language + Extension);

    private static StringTranslationFileHandle FileHandle(string path) => new(Path.GetFileName(path), File.ReadAllText(path));
}
