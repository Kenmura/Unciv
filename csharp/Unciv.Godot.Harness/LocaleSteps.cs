using Unciv.GodotClient.Localization;
using Godot;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Play;
using Unciv.GodotClient.Settings;
using Unciv.Utils;

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
        Loc.Current.NotNull().SetLanguage(language);
    }

    /// <summary>
    /// The steps that show a game screen in <paramref name="language"/>: switch, replace the host's game screen by a fresh one over <paramref name="session"/> (text is built with the screen),
    /// run <paramref name="open"/> (open the screen under test), assert the rectangles of <c>root(screen)</c> (and <paramref name="extra"/>) and capture <paramref name="step"/>.
    /// </summary>
    public static IEnumerable<ScenarioStep> GameScreenIn(Control host, string language, string step, string caption, IGameSession session, Unciv.Models.Ruleset.Ruleset ruleset,
        Func<GameScreen, Node> root, Action<GameScreen>? open = null, Func<GameScreen, bool>? extra = null, Func<GameScreen, bool>? opened = null)
    {
        GameScreen? built = null;
        return
        [
            Use(language),
            new Act("rebuild the game screen in " + language, () =>
            {
                foreach (var old in host.GetChildren().OfType<GameScreen>().ToList())
                {
                    host.RemoveChild(old);
                    old.QueueFree();
                }
                built = new GameScreen();
                host.AddChild(built);
                built.Start(session, AssetRegistry.ForRuleset(ruleset));
                built.Hud.HideBanner();
                open?.Invoke(built);
            }),
            new WaitFrames(6),
            new AssertQuery($"{language}: the screen under test is open", () => opened?.Invoke(built.NotNull()) ?? true),
            new AssertQuery($"{language}: the screen is intact", () => extra?.Invoke(built.NotNull()) ?? true),
            LayoutChecks.Assert($"{language}: no text is cut off, leaves the window or overlaps another", host, () => root(built.NotNull())),
            new Capture(step, caption),
        ];
    }
}
