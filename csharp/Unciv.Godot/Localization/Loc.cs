using Godot;
using Unciv.Assets.Localization;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Settings;

namespace Unciv.GodotClient.Localization;

/// <summary>
/// C025: the client's localization, started once. Reads the translation files, selects the language stored in the settings (and follows later changes of
/// it, so the picker persists through the settings file), installs the font fallback chain as the default font of every control, and mirrors the layout
/// for right-to-left languages. Screens translate with the engine's <c>"key".Tr()</c>; text already on screen is rebuilt by listening to
/// <see cref="ClientLocalization.LanguageChanged"/> (screens opened afterwards pick the new language up by themselves).
/// </summary>
public static class Loc
{
    public static ClientLocalization? Current { get; private set; }

    /// <summary>Starts localization with the language of <paramref name="settings"/>. <paramref name="window"/> is mirrored for right-to-left languages.</summary>
    public static ClientLocalization Init(ClientSettingsService settings, Window? window = null, string? translationsDirectory = null)
    {
        ThemeDB.GetDefaultTheme().DefaultFont = FontChain.Default;
        FontChain.Configure([]);
        var localization = new ClientLocalization(translationsDirectory ?? TranslationLocator.Directory());
        Current = localization;
        localization.LanguageChanged += language => Apply(localization, language, window);
        localization.SetLanguage(settings.Current.Language);
        Apply(localization, localization.Language, window);
        settings.Changed += s => localization.SetLanguage(s.Language);
        return localization;
    }

    private static void Apply(ClientLocalization localization, string language, Window? window)
    {
        FontChain.Configure(localization.RequiredCodePoints(language));
        if (window != null) window.SetLayoutDirection(localization.IsRightToLeft(language) ? Window.LayoutDirection.Rtl : Window.LayoutDirection.Ltr);
    }
}
