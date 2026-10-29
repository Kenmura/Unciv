using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Settings;

namespace Unciv.GodotHarness;

/// <summary>
/// C079: switches the client to a long-string language (German, Russian) in the middle of a scenario. Localization is started on first use with a settings file in the
/// harness temp directory; screens built afterwards show the language (text already built is not rewritten), so a scenario rebuilds the screen it checks after this step.
/// </summary>
internal static class LocaleSteps
{
    public static ScenarioStep Use(string language) => new Act("switch the language to " + language, () => Apply(language));

    public static void Apply(string language)
    {
        if (Loc.Current == null)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-locale-steps-" + System.Environment.ProcessId, "settings.json");
            Loc.Init(new ClientSettingsService(new ClientSettingsStore(path)), null, TranslationLocator.Directory());
        }
        Loc.Current!.SetLanguage(language);
    }
}
