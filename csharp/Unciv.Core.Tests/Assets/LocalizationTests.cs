using Unciv.Assets.Localization;
using Unciv.Models.Translations;

namespace Unciv.Core.Tests.Assets;

public class LocalizationTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("l10n-").FullName;
    private readonly Translations previous = Translations.Current;

    public void Dispose()
    {
        Translations.Current = previous;
        Directory.Delete(dir, recursive: true);
    }

    private ClientLocalization Make()
    {
        File.WriteAllText(Path.Combine(dir, "English.properties"), "Cancel = \n");
        File.WriteAllText(Path.Combine(dir, "German.properties"), "Cancel = Abbrechen\nClose = Schließen\n");
        File.WriteAllText(Path.Combine(dir, "Japanese.properties"), "Cancel = キャンセル\n");
        File.WriteAllText(Path.Combine(dir, "Hebrew.properties"), "Cancel = ביטול\n");
        File.WriteAllText(Path.Combine(dir, "completionPercentages.properties"), "German = 99\nJapanese = 50\n");
        File.WriteAllText(Path.Combine(dir, "template.properties"), "Cancel = \n");
        return new ClientLocalization(dir, new Translations());
    }

    [Fact]
    public void ListsLanguagesEnglishFirstWithPercentages()
    {
        var l = Make();
        Assert.Equal(["English", "German", "Hebrew", "Japanese"], l.Languages.Select(x => x.Name));
        Assert.Equal([100, 99, 0, 50], l.Languages.Select(x => x.PercentComplete));
    }

    [Fact]
    public void EnglishIsListedEvenWithoutAFile() => Assert.Equal("English", new ClientLocalization(Path.Combine(dir, "none"), new Translations()).Languages.Single().Name);

    [Fact]
    public void TranslatesAndFallsBackToTheKey()
    {
        var l = Make();
        Assert.Equal("Cancel", l.Tr("Cancel"));
        Assert.Equal("German", l.SetLanguage("German"));
        Assert.Equal("Abbrechen", l.Tr("Cancel"));
        Assert.Equal("Unknown key", l.Tr("Unknown key"));
        l.SetLanguage("Japanese");
        Assert.Equal("キャンセル", "Cancel".Tr());
        Assert.Equal("Close", l.Tr("Close"));
    }

    [Fact]
    public void UnknownOrHostileLanguageSelectsEnglishAndReadsNoFile()
    {
        var l = Make();
        l.SetLanguage("German");
        Assert.Equal("English", l.SetLanguage("../German"));
        Assert.False(l.Load("../German"));
        Assert.Empty(l.Values("Klingon"));
        Assert.Equal("Cancel", l.Tr("Cancel"));
    }

    [Fact]
    public void RaisesLanguageChangedOnlyOnChange()
    {
        var l = Make();
        var seen = new List<string>();
        l.LanguageChanged += seen.Add;
        l.SetLanguage("German");
        l.SetLanguage("German");
        l.SetLanguage("nope");
        Assert.Equal(["German", "English"], seen);
    }

    [Fact]
    public void ScriptsAndDirection()
    {
        var l = Make();
        Assert.Equal([TextScript.Latin], l.ScriptsOf("German"));
        Assert.Equal([TextScript.Kana], l.ScriptsOf("Japanese"));
        Assert.True(l.IsRightToLeft("Hebrew"));
        Assert.False(l.IsRightToLeft("German"));
        Assert.Contains(0xDF, l.RequiredCodePoints("German"));
    }

    [Fact]
    public void ArabicIsRightToLeftToo()
    {
        File.WriteAllText(Path.Combine(dir, "Arabic.properties"), "Cancel = إلغاء\n");
        Assert.True(new ClientLocalization(dir, new Translations()).IsRightToLeft("Arabic"));
    }

    [Fact]
    public void SwitchingLanguageDropsTheTranslationsCachedForTheOldOne()
    {
        var l = Make();
        l.SetLanguage("German");
        Assert.Equal("Abbrechen", "Cancel".Tr());
        l.SetLanguage("Japanese");
        Assert.Equal("キャンセル", "Cancel".Tr());
        l.SetLanguage("English");
        Assert.Equal("Cancel", "Cancel".Tr());
    }

    [Fact]
    public void PolicyScreenMessagesAreTranslatedExceptComposedOrRawText()
    {
        File.WriteAllText(Path.Combine(dir, "German.properties"),
            "Refused = Abgelehnt\nConnection problem = Verbindungsproblem\nThe engine would not show the policies. = Keine Politiken\nNot enough culture = Zu wenig Kultur\n");
        File.WriteAllText(Path.Combine(dir, "English.properties"), "Refused = \n");
        var l = new ClientLocalization(dir, new Translations());
        Assert.Equal("Refused: Not enough culture", Unciv.Assets.PolicyScreenModel.RefusedLine("Not enough culture"));
        Assert.Equal("Connection problem: Not enough culture", Unciv.Assets.PolicyScreenModel.ConnectionProblemLine("Not enough culture"));
        Assert.Equal("The engine would not show the policies.", Unciv.Assets.PolicyScreenModel.UnavailableLine());
        l.SetLanguage("German");
        Assert.Equal("Abgelehnt: Zu wenig Kultur", Unciv.Assets.PolicyScreenModel.RefusedLine("Not enough culture"));
        Assert.Equal("Abgelehnt: Needs 25 culture", Unciv.Assets.PolicyScreenModel.RefusedLine("Needs 25 culture")); // composed: left alone
        Assert.Equal("Verbindungsproblem: Not enough culture", Unciv.Assets.PolicyScreenModel.ConnectionProblemLine("Not enough culture")); // raw error text
        Assert.Equal("Keine Politiken", Unciv.Assets.PolicyScreenModel.UnavailableLine());
        l.SetLanguage("English");
    }

    [Fact]
    public void RequiredCodePointsKeepSpacingVowelSigns() => Assert.Contains(0x093E, ScriptClassifier.RequiredCodePoints("\u0915\u093e").ToArray());

    [Theory]
    [InlineData('A', TextScript.Latin)]
    [InlineData('ß', TextScript.Latin)]
    [InlineData('ʼ', TextScript.Latin)]
    [InlineData('Ж', TextScript.Cyrillic)]
    [InlineData('Ω', TextScript.Greek)]
    [InlineData('中', TextScript.Han)]
    [InlineData('あ', TextScript.Kana)]
    [InlineData('한', TextScript.Hangul)]
    [InlineData('م', TextScript.Arabic)]
    [InlineData('א', TextScript.Hebrew)]
    [InlineData('ก', TextScript.Thai)]
    [InlineData('अ', TextScript.Devanagari)]
    [InlineData('অ', TextScript.Bengali)]
    [InlineData('1', TextScript.Common)]
    [InlineData(' ', TextScript.Common)]
    [InlineData('́', TextScript.Common)]
    [InlineData('ම', TextScript.Common)] // Maya numeral icon
    [InlineData('අ', TextScript.Other)] // a Sinhala letter that is text
    public void ClassifiesScripts(char c, TextScript expected) => Assert.Equal(expected, ScriptClassifier.Of(c));

    [Fact]
    public void RequiredCodePointsKeepCombiningMarks() => Assert.Equal([(int)'e', 0x0301], ScriptClassifier.RequiredCodePoints("e\u0301").ToArray());

    [Fact]
    public void RequiredCodePointsSkipSymbolsPunctuationAndIcons() =>
        Assert.Equal([(int)'a', 'B', '5', 0x4e2d, 0x20000], ScriptClassifier.RequiredCodePoints("a, B! 5 \u2605 \u0db8 \u4e2d \U00020000").ToArray());

    [Theory]
    [InlineData("Next turn", false)]
    [InlineData("", false)]
    [InlineData("Turn 5 - 4000 BC", true)]
    [InlineData("0", true)]
    [InlineData("Sell for [x] gold", true)]
    [InlineData("{a} and {b}", true)]
    [InlineData("<in cities>", true)]
    public void ComposedLinesAreNotKeys(string text, bool composed) => Assert.Equal(composed, ComposedText.IsComposed(text));

    [Fact]
    public void FontChainHasOneFilePerScriptAndNoDuplicates()
    {
        Assert.Equal(FontChainSpec.Entries.Count, FontChainSpec.Entries.Select(e => e.File).Distinct().Count());
        foreach (var script in new[] { TextScript.Latin, TextScript.Cyrillic, TextScript.Greek, TextScript.Han, TextScript.Kana, TextScript.Hangul, TextScript.Arabic, TextScript.Hebrew, TextScript.Thai, TextScript.Devanagari, TextScript.Bengali })
            Assert.NotNull(FontChainSpec.For(script));
        Assert.Null(FontChainSpec.For(TextScript.Common));
    }

    [Fact]
    public void EveryFontFileOfTheChainIsShippedAndEverySourceIsRecorded()
    {
        string root = RepoFile("Unciv.Godot/Assets/fonts");
        string assets = File.ReadAllText(RepoFile("migration/ASSETS.md"));
        Assert.True(File.Exists(Path.Combine(root, "OFL.txt")));
        foreach (var e in FontChainSpec.Entries)
        {
            Assert.True(File.Exists(Path.Combine(root, e.File)), e.File);
            Assert.Contains(e.File, assets, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryShippedTranslationNeedsOnlyScriptsTheChainCovers()
    {
        string translations = Path.Combine(FindUp("android"), "assets", "jsons", "translations");
        var l = new ClientLocalization(translations, new Translations());
        Assert.True(l.Languages.Count >= 40);
        foreach (var language in l.Languages)
            Assert.All(l.ScriptsOf(language.Name), s => Assert.Contains(s, FontChainSpec.CoveredScripts));
        Assert.Contains(TextScript.Han, l.ScriptsOf("Simplified_Chinese"));
        Assert.Contains(TextScript.Devanagari, l.ScriptsOf("Hindi"));
    }

    private static string FindUp(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, name))) return Path.Combine(d.FullName, name);
        }
        throw new DirectoryNotFoundException(name);
    }

    private static string RepoFile(string relative)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "Unciv.sln"))) return Path.Combine(d.FullName, relative);
        }
        throw new DirectoryNotFoundException("Unciv.sln");
    }
}
