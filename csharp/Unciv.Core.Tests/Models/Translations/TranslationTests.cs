using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.Utils;
using StatsModel = Unciv.Models.StatTypes.Stats;
using Trans = Unciv.Models.Translations.Translations;

namespace Unciv.Core.Tests.Models.Translations;

[Collection("TranslationState")]
public class TranslationTests : IDisposable
{
    public TranslationTests()
    {
        Trans.Current = new Trans();
        Trans.Current.CurrentLanguage = "English";
        TranslationCache.InvalidateLanguage();
    }

    public void Dispose()
    {
        Trans.Current = new Trans();
        Trans.Current.CurrentLanguage = "English";
        TranslationCache.InvalidateLanguage();
        FontRulesetIcons.RulesetObjectNameToChar.Clear();
    }

    private static void AddTranslation(string original, string result) =>
        AddTranslation(original.GetPlaceholderText(), original, result);

    private static void AddTranslation(string key, string original, string result)
    {
        var entry = new TranslationEntry(original) { ["English"] = result };
        Trans.Current[key] = entry;
    }

    [Fact]
    public void TestNumberTr()
    {
        Assert.Equal("1", 1.Tr());
        Assert.Equal("-1", (-1).Tr());
        Assert.Equal("0.123", 0.123f.Tr());
        Assert.Equal("-0.123", (-0.123f).Tr());

        Assert.Equal("1", 1.Tr("English"));
        Assert.Equal("-1", (-1).Tr("English"));
        Assert.Equal("0.123", 0.123f.Tr("English"));
        Assert.Equal("-0.123", (-0.123f).Tr("English"));

        Assert.Equal("১", 1.Tr("Bangla"));
        Assert.Equal("-১", (-1).Tr("Bangla"));
        Assert.Equal("০.১২৩", 0.123f.Tr("Bangla"));
        Assert.Equal("-০.১২৩", (-0.123f).Tr("Bangla"));
    }

    [Fact]
    public void TestStringsWithNumbers()
    {
        string[] tests = ["1", "+1", "-1", "1.0", "+1.0", "-1.0", "0%", "1/2", "(3/4)", "Population 1234"];

        var entry = new TranslationEntry("Population 1234")
        {
            ["English"] = "Citizens 1234",
            ["Bangla"] = "নাগরিক 1234"
        };
        Trans.Current["Population 1234"] = entry;

        Trans.Current.CurrentLanguage = "English";
        TranslationCache.InvalidateLanguage();
        string[] englishExpected = ["1", "+1", "-1", "1.0", "+1.0", "-1.0", "0%", "1/2", "(3/4)", "Citizens 1,234"];
        Assert.Equal(englishExpected, tests.Select(t => t.Tr()).ToArray());

        Trans.Current.CurrentLanguage = "Bangla";
        TranslationCache.InvalidateLanguage();
        string[] banglaExpected = ["১", "+১", "-১", "১.০", "+১.০", "-১.০", "০%", "১/২", "(৩/৪)", "নাগরিক ১,২৩৪"];
        Assert.Equal(banglaExpected, tests.Select(t => t.Tr()).ToArray());
    }

    [Fact]
    public void TranslationParameterExtractionForNestedBracesWorks()
    {
        string superNestedString = "The brother of [[my [best friend]] and [[America]'s greatest [Dad]]]";

        Assert.Equal(["New [York]"], "The city of [New [York]]".GetPlaceholderParameters());
        Assert.Equal(["New [York]"], "The city of [New [York]]]".GetPlaceholderParameters());
        Assert.Empty("The city of [[New [York]".GetPlaceholderParameters());
        Assert.Equal(["[my [best friend]] and [[America]'s greatest [Dad]]"], superNestedString.GetPlaceholderParameters());
        Assert.Equal(["my [best friend]", "[America]'s greatest [Dad]"], superNestedString.GetPlaceholderParameters()[0].GetPlaceholderParameters());
        Assert.Equal(["a"], "Foo [a] <when [b]>".GetPlaceholderParameters());

        AddTranslation("The brother of [person]", "The sibling of [person]");
        Assert.Equal("The sibling of bob", "The brother of [bob]".Tr());

        AddTranslation("[a] and [b]", "[a] and indeed [b]");
        AddTranslation("my [whatever]", "mine own [whatever]");
        AddTranslation("[place]'s greatest [job]", "the greatest [job] in [place]");
        AddTranslation("Dad", "Father");
        AddTranslation("best friend", "closest ally");
        AddTranslation("America", "The old British colonies");

        Assert.Equal(["Dad", "my [best friend]"], "[Dad] and [my [best friend]]".GetPlaceholderParameters());
        Assert.Equal("Father and indeed mine own closest ally", "[Dad] and [my [best friend]]".Tr());

        Assert.Equal("The sibling of mine own closest ally and indeed the greatest Father in The old British colonies",
            superNestedString.Tr());
    }

    [Fact]
    public void FillPlaceholdersWorks()
    {
        Assert.Equal("Gain [x] and [y]", "Gain [a] and [b]".FillPlaceholders("x", "y"));
        Assert.Equal("[x]", "[a [b]]".FillPlaceholders("x"));
        Assert.Throws<ArgumentException>(() => "Gain [a] and [b] or [c]?".FillPlaceholders("x", "y"));
    }

    [Fact]
    public void IsStatsRecognizesStatsIncludingStatCharacter()
    {
        var stat1 = new StatsModel(production: 1f);
        var stats2 = new StatsModel(production: 1f, food: -2f);
        string serializedStat = stat1.ToStringForNotifications();
        string serializedStats = stats2.ToStringForNotifications();

        Assert.True(StatsModel.IsStats(serializedStat));
        Assert.True(StatsModel.IsStats(serializedStats));
        Assert.Equal($"+1 {Stat.Production.Character()}Production", serializedStat.Tr());
        Assert.Equal($"+1 {Stat.Production.Character()}Production, -2 {Stat.Food.Character()}Food", serializedStats.Tr());
        Assert.Equal("+1 Production", serializedStat.Tr(hideStats: true));
        Assert.Equal("+1 Production, -2 Food", serializedStats.Tr(hideStats: true));

        Trans.Current["Production"] = new TranslationEntry("Production") { ["Bangla"] = "উৎপাদন" };
        Trans.Current["Food"] = new TranslationEntry("Food") { ["Bangla"] = "খাদ্য" };

        Trans.Current.CurrentLanguage = "Bangla";
        TranslationCache.InvalidateLanguage();

        Assert.Equal($"+১ {Stat.Production.Character()}উৎপাদন", serializedStat.Tr());
        Assert.Equal($"+১ {Stat.Production.Character()}উৎপাদন, -২ {Stat.Food.Character()}খাদ্য", serializedStats.Tr());
        Assert.Equal("+১ উৎপাদন", serializedStat.Tr(hideStats: true));
        Assert.Equal("+১ উৎপাদন, -২ খাদ্য", serializedStats.Tr(hideStats: true));
    }

    [Fact]
    public void IconSuppressionFlagsAffectOnlyTheirIconTypes()
    {
        const string rulesetObjectName = "Test ruleset object";
        const char rulesetObjectIcon = '\uE000';
        FontRulesetIcons.RulesetObjectNameToChar[rulesetObjectName] = rulesetObjectIcon;

        Assert.Equal(Stat.Production.Character() + "Production", "Production".Tr());
        Assert.Equal(Stat.Production.Character() + "Production", "Production".Tr(hideIcons: true));
        Assert.Equal("Production", "Production".Tr(hideStats: true));

        Assert.Equal(rulesetObjectIcon + rulesetObjectName, rulesetObjectName.Tr());
        Assert.Equal(rulesetObjectName, rulesetObjectName.Tr(hideIcons: true));
        Assert.Equal(rulesetObjectIcon + rulesetObjectName, rulesetObjectName.Tr(hideStats: true));
    }

    [Fact]
    public void TestTranslateConditionals()
    {
        void SetReordered()
        {
            AddTranslation(Trans.ConditionalOrderingKey, Trans.ConditionalOrderingKey, "<when attacking> <for [mapUnitFilter] units> <with a garrison>");
            TranslationCache.InvalidateLanguage();
        }

        void SetBeforeAndCapitalized()
        {
            AddTranslation(Trans.ConditionalPlacementKey, Trans.ConditionalPlacementKey, "before");
            AddTranslation(Trans.ShouldCapitalizeKey, Trans.ShouldCapitalizeKey, "true");
            TranslationCache.InvalidateLanguage();
        }

        Assert.Equal(
            "Only available when number of Owned All Road Tiles is more than 6 when number of in all cities connected to capital Cities is more than 1",
            "Only available <when number of [Owned [All Road] Tiles] is more than [6]> <when number of [[in all cities connected to capital] Cities] is more than [1]>".Tr()
        );

        Assert.Equal(
            "+1 Strength with a garrison vs cities when attacking",
            "[+1] Strength <when attacking> <vs cities> <with a garrison>".Tr()
        );

        SetReordered();
        Assert.Equal(
            "+1 Strength when attacking with a garrison vs cities",
            "[+1] Strength <when attacking> <vs cities> <with a garrison>".Tr()
        );

        SetBeforeAndCapitalized();
        Assert.Equal(
            "When attacking with a garrison vs cities only available",
            "Only available <when attacking> <vs cities> <with a garrison>".Tr()
        );
    }

    [Theory]
    [InlineData("[a]", "A")]
    [InlineData("x [a] y [b] z", "x A y B z")]
    [InlineData("[a][b]", "AB")]
    [InlineData("[]", "")]
    [InlineData("[a [b]]", "á B")]
    [InlineData("[a [b [c]]]", "á µ C")]
    [InlineData("[a {b}]", "a B")]
    [InlineData("a [b", "a [b")]
    [InlineData("a b]", "a b]")]
    [InlineData("[a]]", "A]")]
    [InlineData("[[a]", "[[a]")]
    [InlineData("[a][b", "A[b")]
    [InlineData("{a}", "A")]
    [InlineData("x {a b} y", "x A+B y")]
    [InlineData("{a}{b}", "AB")]
    [InlineData("{a [b]}", "á B")]
    [InlineData("{a}}", "A}")]
    [InlineData("a {b", "a {b")]
    [InlineData("a b}", "a b}")]
    [InlineData("{a} [b]", "A [b]")]
    [InlineData("[a] {b}", "A {b}")]
    [InlineData("{a} and [b] {c}", "A and [b] C")]
    [InlineData("[a] and {b} [c]", "A and {b} C")]
    [InlineData("base <a>", "Base A")]
    [InlineData("base <a> <b>", "Base A B")]
    [InlineData("base <a><b>", "Base A B")]
    [InlineData("base  <a>  ", "Base A")]
    [InlineData("base [x] <when [y]>", "Phase X When Y")]
    [InlineData("base {a b} <when {c}>", "base A+B when C")]
    [InlineData("base\u00A0<a>\u00A0", "Base A")]
    [InlineData("a  b   <x>", "a  b X")]
    [InlineData("a   b   <x>", "a   b X")]
    [InlineData(" base <a>", " base A")]
    [InlineData("a < b", "a < b")]
    [InlineData("a > b", "a > b")]
    [InlineData("a > b <", "a > b <")]
    [InlineData("[a] < [b]", "A < B")]
    [InlineData("<[a b]", "<A+B")]
    [InlineData("<[a  b]", "<a  b")]
    [InlineData("x   <[a]", "x   <A")]
    [InlineData("base <a> <b> tail", "base tail A B")]
    [InlineData("Foo <a> bar", "Foo+Bar A")]
    [InlineData("Foo <a>bar", "Foo+Bar A")]
    [InlineData("Foo<a> bar", "FooBar A")]
    [InlineData("Foo<a>bar", "FooBar A")]
    [InlineData("{{a}", "{{a}")]
    [InlineData("{a {b} c}", "a B c")]
    [InlineData("[][[]", "[[]")]
    [InlineData("[]][[]", "][[]")]
    public void TestBracketNestingAndUnmatchedBrackets(string input, string expected)
    {
        AddTranslation("a", "A");
        AddTranslation("b", "B");
        AddTranslation("c", "C");
        AddTranslation("x", "X");
        AddTranslation("y", "Y");
        AddTranslation("z", "Z");
        AddTranslation("base", "Base");
        AddTranslation("base [arg]", "Phase [arg]");
        AddTranslation("when [arg]", "When [arg]");
        AddTranslation("a [arg]", "á [arg]");
        AddTranslation("b [arg]", "µ [arg]");
        AddTranslation("a b", "A+B");
        AddTranslation("{a", "{à");
        AddTranslation("a {b", "à {B");
        AddTranslation("a ", "A+1");
        AddTranslation("a  ", "A+2");
        AddTranslation("Foobar", "FooBar");
        AddTranslation("Foo bar", "Foo+Bar");
        AddTranslation(Trans.ShouldCapitalizeKey, Trans.ShouldCapitalizeKey, "false");
        TranslationCache.InvalidateLanguage();

        Assert.Equal(expected, input.Tr());
    }

    [Fact]
    public void ValidateConditionalsWorks()
    {
        Assert.Null("[+1] Sight <when at war>".ValidateConditionals());
        Assert.Equal("mismatched conditional braces", "[+1] Sight > 9 <when at war>".ValidateConditionals());
        Assert.Equal("mismatched conditional braces", "[+1] Sight <when at war>>".ValidateConditionals());
        Assert.Equal("mismatched conditional braces", "[+1] Sight <when at war><".ValidateConditionals());
        Assert.Equal("text between or after conditionals", "[+1] Sight <a> <b> <c>!".ValidateConditionals());
        Assert.Equal("text between or after conditionals", "[+1] Sight <a>, <b>, <c>".ValidateConditionals());

        string nestedWithinLimit = "[+1] Sight " + new string('<', TranslationExtensions.MaxConditionalNesting) + new string('>', TranslationExtensions.MaxConditionalNesting);
        Assert.Null(nestedWithinLimit.ValidateConditionals());

        string nestedExceedingLimit = "[+1] Sight " + new string('<', TranslationExtensions.MaxConditionalNesting + 1) + new string('>', TranslationExtensions.MaxConditionalNesting + 1);
        Assert.Equal("excessive nesting of conditionals", nestedExceedingLimit.ValidateConditionals());
    }

    [Fact]
    public void TranslationFileReader_ReadsProperties()
    {
        string content = """
            # A comment
            hello = world
            multi\nline = first\nsecond
            empty = 
            not_a_key_value_line
            """;

        var dict = TranslationFileReader.Read(content);
        Assert.Equal("world", dict["hello"]);
        Assert.Equal("first\nsecond", dict["multi\nline"]);
        Assert.False(dict.ContainsKey("empty"));
        Assert.Equal("[comment]", dict["Comment [comment]"]);
    }

    [Fact]
    public void TranslationFileReader_ReadsPercentages()
    {
        string content = """
            # Percentages
            English = 100
            German = 95
            Spanish = 80
            """;

        using var reader = new StringReader(content);
        var percentages = TranslationFileReader.ReadLanguagePercentages(reader);
        Assert.Equal(100, percentages["English"]);
        Assert.Equal(95, percentages["German"]);
        Assert.Equal(80, percentages["Spanish"]);
    }

    // Expected values: the real Kotlin getPlaceholderText / getPlaceholderParameters on the JVM
    [Theory]
    [InlineData("{a [b]} [c]", "{a [b]} []", "b|c")]
    [InlineData("<x [y]> [z]", "[]", "z")]
    [InlineData("{a [b]} c", "{a [b]} c", "b")]
    [InlineData("x {[y]} [z] {[w]}", "x {[y]} [] {[w]}", "y|z")]
    [InlineData("[a] {b [c]}", "[] {b [c]}", "a")]
    [InlineData("a <b> {c [d]} [e]", "a {c [d]} []", "d|e")]
    public void PlaceholderTextSkipsBracketsInsideGroups(string input, string text, string parameters)
    {
        Assert.Equal(text, input.GetPlaceholderText());
        Assert.Equal(parameters, string.Join("|", input.GetPlaceholderParameters()));
    }

    [Fact]
    public void RepeatedConditionalOrderEntryKeepsLastPosition()
    {
        // buildMap { put(placeholderText, index) }: a -> 2, b -> 1, so <b> sorts before <a>
        Trans.Current[Trans.ConditionalOrderingKey] = new TranslationEntry(Trans.ConditionalOrderingKey) { ["English"] = "<a> <b> <a>" };
        TranslationCache.InvalidateLanguage();
        Assert.Equal("X b a", "x <a> <b>".Tr());
    }

    [Fact]
    public void NumbersInTextAreReformattedWithGrouping()
    {
        AddTranslation("Population 1234", "Population 1234");
        Assert.Equal("Population 1,234", "Population 1234".Tr());
        Assert.Equal("1,234", 1234.Tr());
    }

    [Theory]
    [InlineData("English", "ice <vs cities>", "Ice vs cities")]
    [InlineData("Turkish", "ice <vs cities>", "\u0130ce vs cities")]
    [InlineData("English", "\u00DFa <vs cities>", "SSa vs cities")]
    [InlineData("German", "\u00DFa <vs cities>", "SSa vs cities")]
    public void FirstCharacterCasingFollowsTheLocale(string language, string input, string expected)
    {
        Trans.Current.CurrentLanguage = language;
        TranslationCache.InvalidateLanguage();
        Assert.Equal(expected, input.Tr());
    }

    [Fact]
    public void ConditionalsBeforeUniqueLowercasesTheBaseWithTheLocale()
    {
        Trans.Current[Trans.ConditionalPlacementKey] = new TranslationEntry(Trans.ConditionalPlacementKey) { ["Turkish"] = "before" };
        Trans.Current.CurrentLanguage = "Turkish";
        TranslationCache.InvalidateLanguage();
        Assert.Equal("Vs cities ice", "\u0130ce <vs cities>".Tr());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" true ", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    public void BooleanSettingsParseLikeKotlinToBoolean(string value, bool expected)
    {
        Trans.Current[Trans.ShouldCapitalizeKey] = new TranslationEntry(Trans.ShouldCapitalizeKey) { ["English"] = value };
        Trans.Current[Trans.EffectBeforeCauseKey] = new TranslationEntry(Trans.EffectBeforeCauseKey) { ["English"] = value };
        Assert.Equal(expected, Trans.Current.ShouldCapitalize("English"));
        Assert.Equal(expected, Trans.Current.TriggerNotificationEffectBeforeCause("English"));
    }

    [Fact]
    public void ActiveModsAreSearchedInOrder()
    {
        const string key = "Hello [name]";
        var translations = new Trans();
        translations.TryReadTranslationForLanguage("English", new StringTranslationFileHandle("English", $"{key} = Base [name]\n"), [
            ("ModA", new StringTranslationFileHandle("English", $"{key} = A [name]\n")),
            ("ModB", new StringTranslationFileHandle("English", $"{key} = B [name]\n")),
            ("ModC", new MissingTranslationFileHandle("English")),
        ]);

        Assert.Equal(["ModA", "ModB"], translations.ModsWithTranslations.Keys.ToArray());
        Assert.Equal("Base [name]", translations.GetText("Hello []", "English"));
        Assert.Equal("A [name]", translations.GetText("Hello []", "English", new LinkedHashSet<string>(["ModA", "ModB"])));
        Assert.Equal("B [name]", translations.GetText("Hello []", "English", new LinkedHashSet<string>(["ModB", "ModA"])));
        Assert.Equal("Base [name]", translations.GetText("Hello []", "English", new LinkedHashSet<string>(["ModC"])));
        Assert.Equal("Hello []", translations.GetText("Hello []", "French", new LinkedHashSet<string>(["ModA"])));
    }

    [Fact]
    public void ReadAllLanguagesLoadsOnlyExistingFiles()
    {
        var translations = new Trans();
        translations.ReadAllLanguagesTranslation(language => language switch
        {
            "English" => new StringTranslationFileHandle(language, "Hello = Hello\n"),
            "German" => new StringTranslationFileHandle(language, "Hello = Hallo\n"),
            _ => new MissingTranslationFileHandle(language),
        });
        Assert.Equal(["English", "German"], translations.GetLanguages().Order().ToArray());
        Assert.Equal("Hallo", translations.GetText("Hello", "German"));
        Assert.Equal("[comment]", translations.GetText("Comment []", "German"));
    }

    [Fact]
    public void LanguagePercentagesAreReadStrictly()
    {
        var percentages = TranslationFileReader.ReadLanguagePercentages(new StringReader("# comment\n\nEnglish = 100\nGerman = 97\n"));
        Assert.Equal(100, percentages["English"]);
        Assert.Equal(97, percentages["German"]);
        Assert.ThrowsAny<Exception>(() => TranslationFileReader.ReadLanguagePercentages(new StringReader("German = 9 7\n")));
        Assert.ThrowsAny<Exception>(() => TranslationFileReader.ReadLanguagePercentages(new StringReader("German\n")));
    }

    [Fact]
    public void StatsStringsUseTheTranslationLayer()
    {
        var stats = new StatsModel(production: 1234f);
        Assert.Equal("+1,234 \u2699Production", stats.ToString());
        Assert.Equal("+1,234 Production", stats.ToStringWithoutIcons());
        Assert.Equal("+1,234 \u2699Production", "+1234 Production".Tr());
        Assert.Equal("+1,234 Production", "+1234 Production".Tr(hideStats: true));
    }

    [Fact]
    public void LogicTextsAreTranslated()
    {
        AddTranslation("[text] (Build [number])", "[number] build of [text]");
        Assert.Equal("1,275 build of 4.22.13", new Unciv.Logic.Version("4.22.13", 1275).ToNiceString());
        AddTranslation("Missing [thing]", "Absent: [thing]");
        Assert.Equal("Absent: Foo", new Unciv.Logic.UncivShowableException("Missing [Foo]").GetLocalizedMessage());
    }

    [Fact]
    public void KotlinWhitespaceDiffersFromDotNetAtTheEdges()
    {
        Assert.True('\u00A0'.IsKotlinWhitespace());
        Assert.True('\u001F'.IsKotlinWhitespace());
        Assert.False('\u0085'.IsKotlinWhitespace());
        Assert.Equal("a\u0085 ", "\u001F \u00A0a\u0085 ".KotlinTrimStart());
        Assert.True(" \t\u2003".IsKotlinBlank());
    }
}
