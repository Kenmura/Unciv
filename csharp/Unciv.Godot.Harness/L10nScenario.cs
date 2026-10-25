using Godot;
using Unciv.Assets;
using Unciv.Assets.Localization;
using Unciv.Commands;
using Unciv.GodotClient.Assets;
using Unciv.GodotClient.Localization;
using Unciv.GodotClient.Settings;
using Unciv.GodotClient.Ui;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.GodotHarness;

/// <summary>
/// C025: the same strings in one language of each script the client ships a font for. Asserted first (no pixels): every letter, mark and digit of every translation value
/// of every shipped language is in the font chain (no missing-glyph boxes), the chain loaded all its files, no label shows a raw key where a translation exists, and
/// the choice made in the picker is in the settings file and survives a restart. Then one capture per script (Latin with accents, Cyrillic, Greek, Japanese, Korean,
/// Chinese, Devanagari, Bengali, Thai) and one of right-to-left sample text (Arabic and Hebrew have no shipped translation yet; the sample is written for this scenario).
/// </summary>
public sealed class L10nScenario : Scenario
{
    private static readonly string[] SampleKeys = ["Language", "Options", "Load game", "Diplomacy", "Production", "Units", "Cities", "Cancel"];
    private static readonly (string Language, string Step, string Caption)[] Captures =
    [
        ("German", "latin", "German: Latin with umlauts and sharp s"),
        ("Russian", "cyrillic", "Russian: Cyrillic"),
        ("Greek", "greek", "Greek"),
        ("Japanese", "japanese", "Japanese: kanji, hiragana and katakana from the CJK fallback font"),
        ("Korean", "korean", "Korean: Hangul"),
        ("Simplified_Chinese", "chinese", "Simplified Chinese: Han characters"),
        ("Hindi", "devanagari", "Hindi: Devanagari with conjuncts (partly translated, the rest stays English)"),
        ("Bangla", "bengali", "Bangla: Bengali script"),
        ("Thai", "thai", "Thai: marks above and below the line"),
    ];

    public override string Name => "l10n-sheet";

    public override IReadOnlyList<string> Checklist =>
    [
        "Is every letter of each script drawn (no empty boxes), and do marks, conjuncts and ligatures sit where a reader of the script expects (Devanagari, Bengali, Thai)?",
        "Do the Arabic and Hebrew lines read right to left, with letters joined in Arabic and the digits and Latin word kept in their natural order?",
        "Does the language picker show the language that is in effect, and is the text clipped or overlapping anywhere?",
    ];

    public override IReadOnlyList<ScenarioStep> Build(Control host, GameSession session)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unciv-l10n-scenario", "settings.json");
        if (File.Exists(path)) File.Delete(path);
        var service = new ClientSettingsService(new ClientSettingsStore(path));
        var localization = Loc.Init(service, null, TranslationLocator.Directory());
        var uiTheme = new UiTheme(AssetRegistry.ForRuleset(session.Game.Ruleset));
        var font = uiTheme.Registry.Get<Font>(RulesetAssetKeys.DefaultFont);

        var root = new HBoxContainer { Theme = uiTheme.Theme, Position = new Vector2(16, 16), Size = new Vector2(608, 448) };
        host.AddChild(root);
        var settingsPanel = new SettingsPanel(service, localization);
        root.AddChild(settingsPanel);
        var samples = new VBoxContainer { Name = "Samples" };
        root.AddChild(samples);
        var picker = settingsPanel.GetChildren().OfType<HBoxContainer>().SelectMany(row => row.GetChildren().OfType<LanguagePicker>()).Single();

        void Rebuild(string language)
        {
            foreach (var child in samples.GetChildren()) child.QueueFree();
            samples.AddChild(new Label { Text = language.Replace('_', ' ') });
            foreach (string key in SampleKeys) samples.AddChild(new Label { Text = key.Tr() });
            samples.AddChild(new Label { Text = "Abc 123 - Ünïcödé" });
        }
        localization.LanguageChanged += Rebuild;
        Rebuild(localization.Language);
        ClientSettingsService? restarted = null;
        string missing = "unchecked";
        const string rtlSample = "\u0645\u0631\u062d\u0628\u0627\u05e9\u05dc\u05d5\u05dd";

        var steps = new List<ScenarioStep>
        {
            new WaitFrames(2),
            new AssertQuery("the text server can shape complex scripts", () => TextServerManager.GetPrimaryInterface().HasFeature(TextServer.Feature.Shaping) && TextServerManager.GetPrimaryInterface().HasFeature(TextServer.Feature.BidiLayout) && TextServerManager.GetPrimaryInterface().HasFeature(TextServer.Feature.FontVariable)),
            new AssertQuery("every shipped language is on offer in the picker", () => picker.ItemCount == localization.Languages.Count && localization.Languages.Count >= 40),
            new AssertQuery("no language needs a script the chain has no font for", () => localization.Languages.All(l => localization.ScriptsOf(l.Name).All(s => s == TextScript.Other ? false : FontChainSpec.CoveredScripts.Contains(s)))),
            new Act("look for missing glyphs in every translation", () => { missing = MissingGlyphs(localization, font); if (missing.Length > 0) GD.PushError("missing glyphs: " + missing); }),
            new AssertQuery("every character of every translation has a glyph", () => missing.Length == 0),
            new AssertQuery("every font file asked for loaded", () => FontChain.Missing.Count == 0),
        };
        foreach (var (language, step, caption) in Captures)
        {
            steps.Add(new Act("choose " + language, () => picker.Choose(language)));
            steps.Add(new WaitFrames(3));
            steps.Add(new AssertQuery($"{language} is in effect and no known key is shown untranslated", () => localization.Language == language && RawKeys(root, localization, language).Count == 0));
            steps.Add(new Capture(step, caption));
        }
        steps.Add(new AssertQuery("the picker's choice reached the settings file", () => File.ReadAllText(path).Contains("\"language\": \"Thai\"", StringComparison.Ordinal)));
        steps.Add(new Act("restart: read the settings again", () => restarted = new ClientSettingsService(new ClientSettingsStore(path))));
        steps.Add(new AssertQuery("the language persisted", () => restarted.NotNull().Current.Language == "Thai"));
        steps.Add(new Act("show right-to-left sample text", () =>
        {
            foreach (var child in samples.GetChildren()) child.QueueFree();
            FontChain.Configure(rtlSample.EnumerateRunes().Select(r => r.Value).Concat(localization.RequiredCodePoints(localization.Language)));
            samples.AddChild(new Label { Text = "Arabic / Hebrew (sample)" });
            samples.AddChild(new Label { Text = "مرحبا بالعالم 123 Unciv", TextDirection = Control.TextDirection.Auto });
            samples.AddChild(new Label { Text = "שלום עולם 123 Unciv", TextDirection = Control.TextDirection.Auto });
        }));
        steps.Add(new WaitFrames(3));
        steps.Add(new AssertQuery("the sample letters have glyphs", () => "مرحباשלום".EnumerateRunes().All(r => font.HasChar(r.Value))));
        steps.Add(new Capture("rtl", "Arabic and Hebrew sample lines, right to left, with digits and a Latin word inside"));
        return steps;
    }

    /// <summary>Per language, the code points of its translations that the font chain cannot draw ("" when none), as "Language: U+XXXX ..." lines.</summary>
    private static string MissingGlyphs(ClientLocalization localization, Font font)
    {
        var lines = new List<string>();
        foreach (var language in localization.Languages)
        {
            localization.SetLanguage(language.Name);
            var missing = localization.RequiredCodePoints(language.Name).Where(cp => !font.HasChar(cp)).Take(8).Select(cp => $"U+{cp:X4}").ToList();
            if (missing.Count > 0) lines.Add($"{language.Name}: {string.Join(' ', missing)}");
        }
        return string.Join("; ", lines);
    }

    /// <summary>The texts on screen that equal a sample key although the language has a different translation for it.</summary>
    private static List<string> RawKeys(Node root, ClientLocalization localization, string language)
    {
        var shown = new List<string>();
        void Walk(Node node)
        {
            switch (node)
            {
                case Label label: shown.Add(label.Text); break;
                case Button button: shown.Add(button.Text); break;
            }
            foreach (var child in node.GetChildren()) Walk(child);
        }
        Walk(root);
        var translated = SampleKeys.Where(k => localization.Translations.GetText(k, language) != k).ToHashSet();
        return shown.Where(translated.Contains).ToList();
    }
}
