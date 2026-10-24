using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using StatsModel = Unciv.Models.StatTypes.Stats;
using Trans = Unciv.Models.Translations.Translations;

namespace Unciv.Core.Tests.Models.Translations;

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
}
