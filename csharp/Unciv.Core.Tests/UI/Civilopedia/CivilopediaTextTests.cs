using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using BuildingObject = Unciv.Models.Ruleset.Building;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.UI.Civilopedia;

/// <summary>
/// T095: FormattedLine, ICivilopediaText, CivilopediaCategories, FormattedLineListBuilder, DescriptionHelpers.
/// No Kotlin fixtures exist; the expected values are derived by hand from the Kotlin source.
/// </summary>
public class CivilopediaTextTests
{
    private sealed class ImageCheck(params string[] known) : IFormattedLineImageCheck
    {
        public bool ExtraImageExists(string extraImage) => known.Contains(extraImage);
    }

    private sealed class TestText(params FormattedLine[] lines) : ICivilopediaText
    {
        public List<FormattedLine> CivilopediaText { get; set; } = [.. lines];

        public string MakeLink() => "Test/Thing";
    }

    private sealed class NamedText(string name, params FormattedLine[] lines) : ICivilopediaText, Unciv.Models.StatTypes.INamed
    {
        public string Name { get; set; } = name;

        public List<FormattedLine> CivilopediaText { get; set; } = [.. lines];

        public string MakeLink() => $"Test/{Name}";
    }

    private static Ruleset MakeRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Buildings["Barracks"] = new BuildingObject { Name = "Barracks" };
        ruleset.Buildings["Pyramids"] = new BuildingObject { Name = "Pyramids", IsWonder = true };
        ruleset.TileImprovements["Railroad"] = new TileImprovement { Name = "Railroad" };
        ruleset.Terrains["Railroad"] = new Terrain { Name = "Railroad" };
        ruleset.Units["Warrior"] = new BaseUnit { Name = "Warrior" };
        ruleset.Terrains["Hidden Place"] = new Terrain { Name = "Hidden Place", Uniques = ["Will not be displayed in Civilopedia"] };
        return ruleset;
    }

    private static CivilopediaContext Ctx(Ruleset? ruleset = null) => new(ruleset ?? MakeRuleset());

    // region FormattedLine

    [Fact]
    public void LinkTypeAndDisplayText()
    {
        Assert.Equal(FormattedLine.LinkTypeEnum.None, new FormattedLine("a").LinkType);
        Assert.Equal(FormattedLine.LinkTypeEnum.Internal, new FormattedLine("a", "Unit/Warrior").LinkType);
        foreach (var url in new[] { "https://x.y", "http://x.y", "mailto:a@b.c" })
            Assert.Equal(FormattedLine.LinkTypeEnum.External, new FormattedLine(link: url).LinkType);

        var external = new FormattedLine(link: "https://x.y");
        Assert.Equal("https://x.y", external.TextToDisplay);
        Assert.Equal("", external.IconToDisplay);
        var internalLink = new FormattedLine("txt", "Unit/Warrior");
        Assert.Equal("txt", internalLink.TextToDisplay);
        Assert.Equal("Unit/Warrior", internalLink.IconToDisplay);
        Assert.Equal("Unit/Settler", new FormattedLine("txt", "Unit/Warrior", icon: "Unit/Settler").IconToDisplay);
    }

    [Fact]
    public void DefaultsAreKotlinDefaults()
    {
        var line = new FormattedLine();
        Assert.True(line.IsEmpty());
        Assert.Equal(int.MinValue, line.Size);
        Assert.True(float.IsNaN(line.ImageSize));
        Assert.True(float.IsNaN(line.Padding));
        Assert.Equal([18, 36, 32, 27, 24, 21, 15, 12, 9], FormattedLine.HeaderSizes);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("x", false)]
    public void IsEmptyText(string text, bool empty) => Assert.Equal(empty, new FormattedLine(text).IsEmpty());

    [Fact]
    public void IsEmptyOtherContent()
    {
        Assert.False(new FormattedLine(starred: true).IsEmpty());
        Assert.False(new FormattedLine(separator: true).IsEmpty());
        Assert.False(new FormattedLine(extraImage: "x").IsEmpty());
        Assert.False(new FormattedLine(icon: "a/b").IsEmpty());
        Assert.False(new FormattedLine(link: "a/b").IsEmpty());
        Assert.True(new FormattedLine(color: "RED", header: 1, indent: 2).IsEmpty());
    }

    [Theory]
    [InlineData("#FFA040", 1f, 0xA0 / 255f, 0x40 / 255f)]
    [InlineData("#fa4", 1f, 0xAA / 255f, 0x44 / 255f)]
    [InlineData("#F42", 1f, 0x44 / 255f, 0x22 / 255f)]
    [InlineData("goldenrod", 0xda / 255f, 0xa5 / 255f, 0x20 / 255f)]
    [InlineData("Red", 1f, 0f, 0f)]
    public void DisplayColorParses(string color, float r, float g, float b)
    {
        var parsed = new FormattedLine("t", color: color).DisplayColor;
        Assert.Equal(r, parsed.R, 4);
        Assert.Equal(g, parsed.G, 4);
        Assert.Equal(b, parsed.B, 4);
        Assert.Equal(1f, parsed.A);
    }

    [Theory]
    [InlineData("")]
    [InlineData("notacolor")]
    [InlineData("#12")]
    [InlineData("#ggg")]
    public void DisplayColorFallsBackToWhite(string color) =>
        Assert.Equal(EngineColor.White, new FormattedLine("t", color: color).DisplayColor);

    [Fact]
    public void FiveDigitHashColorIsNotHex6()
    {
        // "#12345": isHex(1,3) is true but isHex(1,6) false, so it reads as the 3-digit form "#112233"
        var parsed = new FormattedLine("t", color: "#12345").DisplayColor;
        Assert.Equal(0x11 / 255f, parsed.R, 4);
        Assert.Equal(0x22 / 255f, parsed.G, 4);
        Assert.Equal(0x33 / 255f, parsed.B, 4);
    }

    private static List<string> Reasons(FormattedLine line, params string[] knownImages) =>
        line.UnsupportedReasons(new ImageCheck(knownImages)).ToList();

    [Fact]
    public void UnsupportedReasonsValidLinesAreClean()
    {
        Assert.Empty(Reasons(new FormattedLine("t", "Unit/Warrior", header: 3, color: "#fff", indent: 1)));
        Assert.Empty(Reasons(new FormattedLine(separator: true)));
        Assert.Empty(Reasons(new FormattedLine(extraImage: "pic", imageSize: 50f), "pic"));
        Assert.Empty(Reasons(new FormattedLine()));
    }

    [Fact]
    public void UnsupportedReasonsEachProblem()
    {
        Assert.Equal(["separator and other options are incompatible"], Reasons(new FormattedLine("t", separator: true)));
        // The doc says a separator may carry size and color, but hasNormalContent() counts both: Kotlin reports them
        Assert.Equal(["separator and other options are incompatible"], Reasons(new FormattedLine(separator: true, size: 3)));
        Assert.Equal(["link is invalid - use internal category/name format or a https:// URL"], Reasons(new FormattedLine(link: "a/b/c")));
        Assert.Equal(["link is invalid - use internal category/name format or a https:// URL"], Reasons(new FormattedLine(link: "nolink")));
        Assert.Empty(Reasons(new FormattedLine(link: "https://a/b/c")));
        Assert.Equal(["use either size or header but not both"], Reasons(new FormattedLine("t", size: 20, header: 1)));
        Assert.Equal(["header should be in the range 1..8"], Reasons(new FormattedLine("t", header: 9)));
        Assert.Equal(["header should be in the range 1..8"], Reasons(new FormattedLine("t", header: -1)));
        Assert.Equal(["size is out of sensible range"], Reasons(new FormattedLine("t", size: 0)));
        Assert.Equal(["size is out of sensible range"], Reasons(new FormattedLine("t", size: 101)));
        Assert.Empty(Reasons(new FormattedLine("t", size: 100)));
        Assert.Equal(["indent is out of sensible range"], Reasons(new FormattedLine("t", indent: 101)));
        Assert.Equal(["indent is out of sensible range"], Reasons(new FormattedLine("t", indent: -1)));
        Assert.Equal(["unknown color \"bogus\""], Reasons(new FormattedLine("t", color: "bogus")));
        Assert.Equal(["color set but nothing to apply it to"], Reasons(new FormattedLine(color: "RED", header: 1)));
        Assert.Equal(["iconCrossed set without icon or link"], Reasons(new FormattedLine("t", iconCrossed: true)));
        Assert.Equal(["imageSize is only valid for an extraImage"], Reasons(new FormattedLine("t", imageSize: 5f)));
    }

    [Fact]
    public void UnsupportedReasonsIconCheckLooksAtLinkSicKotlin()
    {
        // Kotlin: `icon.isNotEmpty() && !isValidInternalLink(link)` tests link, so an icon with a valid link passes and an icon alone fails
        Assert.Empty(Reasons(new FormattedLine("t", "a/b", icon: "x")));
        Assert.Equal(["icon is invalid - use internal category/name format"], Reasons(new FormattedLine("t", icon: "a/b")));
    }

    [Fact]
    public void UnsupportedReasonsExtraImage()
    {
        Assert.Equal(["extraImage not found as either atlas texture or in ExtraImages folder"], Reasons(new FormattedLine(extraImage: "nope")));
        Assert.Equal(
            ["extraImage and other options except imageSize are incompatible"],
            Reasons(new FormattedLine("t", extraImage: "pic"), "pic"));
        Assert.Equal(
            ["separator and other options are incompatible", "extraImage and other options except imageSize are incompatible"],
            Reasons(new FormattedLine("t", extraImage: "pic", separator: true), "pic"));
        Assert.Equal(
            ["extraImage and other options except imageSize are incompatible"],
            Reasons(new FormattedLine(extraImage: "pic", separator: true), "pic"));
    }

    [Fact]
    public void EqualityThrows()
    {
        var line = new FormattedLine("a");
        Assert.Throws<InvalidOperationException>(() => line.Equals(line));
        Assert.Throws<InvalidOperationException>(() => line.GetHashCode());
    }

    [Fact]
    public void ToStringForms()
    {
        Assert.Equal("(empty)", new FormattedLine().ToString());
        Assert.Equal("(separator)", new FormattedLine(separator: true).ToString());
        Assert.Equal("(extraImage='p')", new FormattedLine(extraImage: "p").ToString());
        Assert.Equal("(header=2)'H'", new FormattedLine("H", header: 2).ToString());
        Assert.Equal("'plain'", new FormattedLine("plain").ToString());
        Assert.Equal("'t'->Unit/Warrior", new FormattedLine("t", "Unit/Warrior").ToString());
    }

    [Fact]
    public void JsonDeserializationUsesDefaults()
    {
        var line = Unciv.Json.UncivJson.Deserialize<FormattedLine>("""{"text":"Hi","link":"Unit/Warrior","header":2,"centered":true}""")!;
        Assert.Equal("Hi", line.Text);
        Assert.Equal("Unit/Warrior", line.Link);
        Assert.Equal(2, line.Header);
        Assert.True(line.Centered);
        Assert.Equal(int.MinValue, line.Size);
        Assert.True(float.IsNaN(line.Padding));
    }

    // endregion
    // region FormattedLine(Unique)

    [Fact]
    public void UniqueLineExplicitCivilopediaLinkWins()
    {
        var unique = new UniqueObject("Gives [Barracks] <Civilopedia link [Unit/Warrior]>");
        var line = new FormattedLine(unique, Ctx(), indent: 2);
        Assert.Equal("Unit/Warrior", line.Link);
        Assert.Equal(2, line.Indent);
    }

    [Fact]
    public void UniqueLineGuessesLinkFromParameters()
    {
        var ctx = Ctx();
        Assert.Equal("Building/Barracks", new FormattedLine(new UniqueObject("Gives [Barracks] to [all]"), ctx).Link);
        Assert.Equal("Wonder/Pyramids", new FormattedLine(new UniqueObject("Gives [Pyramids]"), ctx).Link);
        Assert.Equal("Unit/Warrior", new FormattedLine(new UniqueObject("Gives [Nothing] [Warrior]"), ctx).Link);
        Assert.Equal("", new FormattedLine(new UniqueObject("Gives [Nothing]"), ctx).Link);
        Assert.Equal("", new FormattedLine(new UniqueObject("Gives 5 stuff"), ctx).Link);
    }

    [Fact]
    public void UniqueLineSearchesModifierParametersAfterOwnParameters()
    {
        var ctx = Ctx();
        Assert.Equal("Unit/Warrior", new FormattedLine(new UniqueObject("Gives [Nothing] <for [Warrior] units>"), ctx).Link);
        Assert.Equal("Building/Barracks", new FormattedLine(new UniqueObject("Gives [Barracks] <for [Warrior] units>"), ctx).Link);
    }

    [Fact]
    public void UniqueLineNameConflictLaterCategoryWins()
    {
        // "Railroad" is both a Terrain and an Improvement: Improvement is listed after Terrain
        Assert.Equal("Improvement/Railroad", new FormattedLine(new UniqueObject("Gives [Railroad]"), Ctx()).Link);
    }

    [Fact]
    public void UniqueLineSkipsObjectsHiddenFromCivilopedia()
    {
        Assert.Equal("", new FormattedLine(new UniqueObject("Gives [Hidden Place]"), Ctx()).Link);
    }

    [Fact]
    public void UniqueLineCacheFollowsRulesetIdentity()
    {
        var first = MakeRuleset();
        Assert.Equal("Unit/Warrior", new FormattedLine(new UniqueObject("Gives [Warrior]"), Ctx(first)).Link);
        var second = new Ruleset();
        Assert.Equal("", new FormattedLine(new UniqueObject("Gives [Warrior]"), Ctx(second)).Link);
        Assert.Equal("Unit/Warrior", new FormattedLine(new UniqueObject("Gives [Warrior]"), Ctx(first)).Link);
    }

    // endregion
    // region ICivilopediaText

    [Fact]
    public void HeaderDefaultsToNamedOnly()
    {
        var named = (ICivilopediaText)new NamedText("Thing");
        var header = named.GetCivilopediaTextHeader()!;
        Assert.Equal("Thing", header.Text);
        Assert.Equal("Test/Thing", header.Icon);
        Assert.Equal(2, header.Header);
        Assert.Null(((ICivilopediaText)new TestText()).GetCivilopediaTextHeader());
        Assert.Equal("Thing", named.GetIconName());
        Assert.Equal("", ((ICivilopediaText)new TestText()).GetIconName());
        Assert.Equal(0, named.GetSortGroup(new Ruleset()));
        Assert.Null(named.GetSubCategory(new Ruleset()));
        Assert.Empty(named.GetCivilopediaTextLines(Ctx()));
    }

    private sealed class WithLines(params FormattedLine[] lines) : ICivilopediaText
    {
        public List<FormattedLine> CivilopediaText { get; set; } = [.. lines];

        public string MakeLink() => "";

        public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) => [new FormattedLine("auto1"), new FormattedLine("auto2")];
    }

    private static string Dump(ICivilopediaText text) => string.Join("|", text.CivilopediaText.Select(it => it.ToString()));

    [Fact]
    public void AssembleWithoutLinkedLinesAppendsAutomaticLinesAtEnd()
    {
        var result = ((ICivilopediaText)new WithLines(new FormattedLine("a"), new FormattedLine("b"))).AssembleCivilopediaText(Ctx());
        Assert.Equal("'a'|'b'|(empty)|'auto1'|'auto2'", Dump(result));
        Assert.IsType<SimpleCivilopediaText>(result);
    }

    [Fact]
    public void AssembleWithoutAnyLinesHasNoLeadingBlank()
    {
        Assert.Equal("'auto1'|'auto2'", Dump(((ICivilopediaText)new WithLines()).AssembleCivilopediaText(Ctx())));
    }

    [Fact]
    public void AssembleInsertsBeforeFirstLinkedLine()
    {
        var text = (ICivilopediaText)new WithLines(new FormattedLine("a"), new FormattedLine("see", "Unit/Warrior"), new FormattedLine("see2", "Unit/Warrior"));
        Assert.Equal("'a'|(empty)|'auto1'|'auto2'|(empty)|'see'->Unit/Warrior|'see2'->Unit/Warrior", Dump(text.AssembleCivilopediaText(Ctx())));
    }

    [Fact]
    public void AssembleLinkedLineFirstHasNoLeadingBlank()
    {
        var text = (ICivilopediaText)new WithLines(new FormattedLine("see", "Unit/Warrior"));
        Assert.Equal("'auto1'|'auto2'|(empty)|'see'->Unit/Warrior", Dump(text.AssembleCivilopediaText(Ctx())));
    }

    [Fact]
    public void AssembleEmptyLinkedLineDoesNotCountAsMiddle()
    {
        // isEmpty() is true for a line with only a colour; with no link it never triggers anyway - an empty linked line cannot exist, so use a plain empty one
        var text = (ICivilopediaText)new WithLines(new FormattedLine(color: "RED"), new FormattedLine("see", "Unit/Warrior"));
        Assert.Equal("(empty)|(empty)|'auto1'|'auto2'|(empty)|'see'->Unit/Warrior", Dump(text.AssembleCivilopediaText(Ctx())));
    }

    [Fact]
    public void AssembleAddsHeaderAndSeparatorForNamedObjects()
    {
        var text = (ICivilopediaText)new NamedText("Thing", new FormattedLine("a"));
        Assert.Equal("(header=2)'Thing'|(separator)|'a'|(empty)", Dump(text.AssembleCivilopediaText(Ctx())));
    }

    [Fact]
    public void AssembleAddsModLineOnlyWithSeveralMods()
    {
        var building = new BuildingObject { Name = "Barracks", OriginRuleset = "Cool Mod" };
        var ruleset = new Ruleset();
        var single = ((ICivilopediaText)building).AssembleCivilopediaText(Ctx(ruleset));
        Assert.DoesNotContain(single.CivilopediaText, it => it.Text.StartsWith("Mod:", StringComparison.Ordinal));

        ruleset.Mods.Add("Base");
        ruleset.Mods.Add("Cool Mod");
        var multi = ((ICivilopediaText)building).AssembleCivilopediaText(Ctx(ruleset)).CivilopediaText;
        var mod = multi[^1];
        Assert.Equal("Mod: [Cool Mod]", mod.Text);
        Assert.True(mod.Starred);
        Assert.Equal("#daa520", mod.Color);
        Assert.True(multi[^2].IsEmpty());

        building.OriginRuleset = "";
        Assert.DoesNotContain(((ICivilopediaText)building).AssembleCivilopediaText(Ctx(ruleset)).CivilopediaText, it => it.Starred);
    }

    [Fact]
    public void SimpleCivilopediaTextConstructors()
    {
        Assert.Equal("'a'|'b'", Dump(new SimpleCivilopediaText(["a", "b"])));
        Assert.Equal("'x'|'a'", Dump(new SimpleCivilopediaText([new FormattedLine("x")], ["a"])));
        Assert.Equal("", new SimpleCivilopediaText(new List<FormattedLine>()).MakeLink());
    }

    // endregion
    // region CivilopediaCategories

    [Fact]
    public void FromLinkByNameThenLabel()
    {
        Assert.Equal(CivilopediaCategories.Unit, CivilopediaCategoriesExtensions.FromLink("Unit"));
        Assert.Equal(CivilopediaCategories.Unit, CivilopediaCategoriesExtensions.FromLink("Units"));
        Assert.Equal(CivilopediaCategories.UnitType, CivilopediaCategoriesExtensions.FromLink("Unit types"));
        Assert.Equal(CivilopediaCategories.UnitNameGroup, CivilopediaCategoriesExtensions.FromLink("Unit Names"));
        Assert.Equal(CivilopediaCategories.Belief, CivilopediaCategoriesExtensions.FromLink("Religions and Beliefs"));
        Assert.Null(CivilopediaCategoriesExtensions.FromLink("unit"));
        Assert.Null(CivilopediaCategoriesExtensions.FromLink(""));
    }

    [Fact]
    public void EveryCategoryHasLabelAndHeaderIconInDeclaredOrder()
    {
        var all = Enum.GetValues<CivilopediaCategories>();
        Assert.Equal(19, all.Length);
        Assert.Equal(CivilopediaCategories.Building, all[0]);
        Assert.Equal(CivilopediaCategories.Speed, all[^1]);
        foreach (var category in all)
        {
            Assert.NotEmpty(category.Label());
            Assert.Contains('/', category.HeaderIcon());
            Assert.Equal(category, CivilopediaCategoriesExtensions.FromLink(category.ToString()));
            Assert.Equal(category, CivilopediaCategoriesExtensions.FromLink(category.Label()));
        }
    }

    [Fact]
    public void CategoryIteratorSplitsBuildingsAndWonders()
    {
        var ruleset = MakeRuleset();
        Assert.Equal(["Barracks"], CivilopediaCategories.Building.GetCategoryIterator(ruleset, null).Select(it => ((BuildingObject)it).Name));
        Assert.Equal(["Pyramids"], CivilopediaCategories.Wonder.GetCategoryIterator(ruleset, null).Select(it => ((BuildingObject)it).Name));
        Assert.Equal(["Warrior"], CivilopediaCategories.Unit.GetCategoryIterator(ruleset, null).Select(it => ((BaseUnit)it).Name));
    }

    [Fact]
    public void CategoryIteratorTutorialsAppendGlobalUniquesOnlyWithContent()
    {
        var ruleset = new Ruleset();
        Assert.Empty(CivilopediaCategories.Tutorial.GetCategoryIterator(ruleset, null));
        ruleset = new Ruleset(); // UniqueObjects are cached on first use
        ruleset.GlobalUniques.Uniques.Add("Consumes [1] [Iron]");
        var entries = CivilopediaCategories.Tutorial.GetCategoryIterator(ruleset, null);
        Assert.Single(entries);
        Assert.Equal("Tutorial/Global Uniques", entries.Single().MakeLink());
    }

    [Fact]
    public void CategoryIteratorVictoryFiltersByGameParameters()
    {
        var ruleset = new Ruleset();
        ruleset.Victories["Domination"] = new Victory { Name = "Domination" };
        ruleset.Victories["Science"] = new Victory { Name = "Science" };
        Assert.Equal(2, CivilopediaCategories.Victory.GetCategoryIterator(ruleset, null).Count);
        var game = new FakeGameInfo();
        game.GameParameters.VictoryTypes = ["Science"];
        Assert.Equal(["Science"], CivilopediaCategories.Victory.GetCategoryIterator(ruleset, game).Select(it => ((Victory)it).Name));
    }

    // endregion
    // region FormattedLineListBuilder

    [Fact]
    public void BuilderAddUsesDefaultsTemplate()
    {
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b =>
        {
            b.Add("one");
            b.Add("two", color: "RED", indent: 3);
            b.Defaults(new FormattedLine(indent: 1, header: 4));
            b.Add("three");
            b.Add("four", header: 0);
        }, defaults: new FormattedLine(color: "BLUE", size: 12));
        Assert.Equal(4, lines.Count);
        Assert.Equal(("one", "BLUE", 12, 0), (lines[0].Text, lines[0].Color, lines[0].Size, lines[0].Indent));
        Assert.Equal(("two", "RED", 12, 3), (lines[1].Text, lines[1].Color, lines[1].Size, lines[1].Indent));
        Assert.Equal(("three", "", int.MinValue, 1, 4), (lines[2].Text, lines[2].Color, lines[2].Size, lines[2].Indent, lines[2].Header));
        Assert.Equal(0, lines[3].Header);
    }

    [Fact]
    public void BuilderSeparatorsAndSpace()
    {
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b =>
        {
            b.Space();
            b.Separator();
            b.Separator(5, "RED");
            b.Add(SeparatorType.None);
            b.Add(SeparatorType.Space);
            b.Add(SeparatorType.Line, 7, "#fff");
            b.Add(SeparatorType.Line);
        }, defaults: new FormattedLine(size: 3, color: "GRAY"));
        Assert.Equal("(empty)|(separator)|(separator)|(empty)|(separator)|(separator)", string.Join("|", lines.Select(it => it.ToString())));
        Assert.Equal((3, "GRAY"), (lines[1].Size, lines[1].Color));
        Assert.Equal((5, "RED"), (lines[2].Size, lines[2].Color));
        Assert.Equal((7, "#fff"), (lines[4].Size, lines[4].Color));
        Assert.Equal((3, "GRAY"), (lines[5].Size, lines[5].Color));
    }

    [Fact]
    public void BuilderAddOverloads()
    {
        var unique = new UniqueObject("Gives [Barracks]");
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b =>
        {
            b.Add(new FormattedLine("whole"));
            b.Add(unique, 4);
            b.Add(unique);
            b.Add("picture", 64f);
            b.Add([new FormattedLine("x"), new FormattedLine("y")]);
            b.Add(new[] { 1, 2 }, n => new FormattedLine($"n{n}"));
        }, defaults: new FormattedLine(indent: 9));
        Assert.Equal(
            "'whole'|'Gives [Barracks]'->Building/Barracks|'Gives [Barracks]'->Building/Barracks|(extraImage='picture')|'x'|'y'|'n1'|'n2'",
            string.Join("|", lines.Select(it => it.ToString())));
        Assert.Equal(4, lines[1].Indent);
        Assert.Equal(9, lines[2].Indent); // indent comes from defaults
        Assert.Equal(64f, lines[3].ImageSize);
        Assert.Equal(0, lines[0].Indent); // a complete line ignores defaults
    }

    [Fact]
    public void BuilderAddObjectsAndSeeAlso()
    {
        var ruleset = MakeRuleset();
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(ruleset), b =>
        {
            b.AddObjects(ruleset.Buildings.Values);
            b.AddSeeAlso([]);
            b.AddSeeAlso(ruleset.Units.Values);
        });
        Assert.Equal(
            "'Barracks'->Building/Barracks|'Pyramids'->Wonder/Pyramids|(empty)|'{See also}:'|'Warrior'->Unit/Warrior",
            string.Join("|", lines.Select(it => it.ToString())));
        Assert.Equal(1, lines[^1].Indent);
    }

    private sealed class UniqueHolder : RulesetObject
    {
        public override string Name { get; set; } = "Holder";

        public override Unciv.Models.Ruleset.Unique.UniqueTarget GetUniqueTarget() => Unciv.Models.Ruleset.Unique.UniqueTarget.Global;

        public override string MakeLink() => "";

        public static UniqueHolder With(params string[] uniques)
        {
            var holder = new UniqueHolder();
            holder.Uniques.AddRange(uniques);
            return holder;
        }
    }

    [Fact]
    public void BuilderAddUniquesFiltersHiddenAndExcluded()
    {
        var holder = UniqueHolder.With("Gives [Barracks]", "Gives [Pyramids] <hidden from users>", "Gives [Warrior]", "Skip [Warrior]");
        var separators = 0;
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b =>
            b.AddUniques(holder, SeparatorType.Line, extraSeparator: () => separators++, exclude: it => it.Text.StartsWith("Skip", StringComparison.Ordinal)));
        Assert.Equal("(separator)|'Gives [Barracks]'->Building/Barracks|'Gives [Warrior]'->Unit/Warrior", string.Join("|", lines.Select(it => it.ToString())));
        Assert.Equal(1, separators);
    }

    [Fact]
    public void BuilderAddUniquesWithoutContentAddsNoSeparator()
    {
        var holder = UniqueHolder.With("Gives [Pyramids] <hidden from users>");
        var called = false;
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b => b.AddUniques(holder, extraSeparator: () => called = true));
        Assert.Empty(lines);
        Assert.False(called);
    }

    [Fact]
    public void BuilderAddUniquesColorsConsumesResources()
    {
        var holder = UniqueHolder.With("Consumes [1] [Iron]", "Consumes [1] [Iron]");
        var lines = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b => b.AddUniques(holder, SeparatorType.None, colorConsumesResources: true));
        Assert.Equal(2, lines.Count);
        Assert.Equal(("Resources/Iron", "#F42"), (lines[0].Link, lines[0].Color));

        var plain = FormattedLineListBuilder.BuildCivilopediaText(Ctx(), b => b.AddUniques(holder, SeparatorType.None));
        Assert.Equal(("", ""), (plain[0].Link, plain[0].Color));
    }

    [Fact]
    public void GlobalUniquesCivilopediaLinesAndCombine()
    {
        var a = new GlobalUniques { CivilopediaText = [new FormattedLine("from a")], UnitUniques = ["Consumes [1] [Iron]"] };
        a.Uniques.Add("Consumes [2] [Coal]");
        var combined = GlobalUniques.Combine(new GlobalUniques(), a, new GlobalUniques { CivilopediaText = [new FormattedLine("from b")] });
        Assert.Equal(["from a", "from b"], combined.CivilopediaText.Select(it => it.Text));
        Assert.Equal(["Consumes [1] [Iron]"], combined.UnitUniques);

        var lines = ((ICivilopediaText)a).GetCivilopediaTextLines(Ctx()).Select(it => it.ToString());
        Assert.Equal(
            "(empty)|(header=4)'Global Effect'|'Consumes [2] [Coal]'|(empty)|(header=4)'Units'|'Consumes [1] [Iron]'",
            string.Join("|", lines));
    }

    // endregion
    // region DescriptionHelpers

    [Fact]
    public void UniquesToDescriptionTranslatesVisibleUniques()
    {
        var holder = UniqueHolder.With("Gives [Barracks]", "Gives [Pyramids] <hidden from users>", "Gives [Warrior]");
        Assert.Equal(["Gives Barracks", "Gives Warrior"], holder.UniquesToDescription());
        var target = new List<string> { "first" };
        var returned = holder.UniquesToDescription(target, it => it.Text.Contains("Warrior", StringComparison.Ordinal));
        Assert.Same(target, returned);
        Assert.Equal(["first", "Gives Barracks"], target);
    }

    [Fact]
    public void UniquesToCivilopediaTextLinesLeadingSeparatorOptions()
    {
        var holder = UniqueHolder.With("Gives [Barracks]", "Gives [Warrior]");
        string Run(Func<IEnumerable<FormattedLine>>? sep = null, bool none = false) =>
            string.Join("|", holder.UniquesToCivilopediaTextLines(Ctx(), sep, none).Select(it => it.ToString()));

        Assert.Equal("(empty)|'Gives [Barracks]'->Building/Barracks|'Gives [Warrior]'->Unit/Warrior", Run());
        Assert.Equal("'Gives [Barracks]'->Building/Barracks|'Gives [Warrior]'->Unit/Warrior", Run(none: true));
        Assert.Equal(
            "(separator)|(empty)|'Gives [Barracks]'->Building/Barracks|'Gives [Warrior]'->Unit/Warrior",
            Run(() => [new FormattedLine(separator: true), new FormattedLine()]));
    }

    [Fact]
    public void UniquesToCivilopediaTextLinesNoSeparatorWhenNothingVisible()
    {
        var holder = UniqueHolder.With("Gives [Pyramids] <hidden from users>");
        Assert.Empty(holder.UniquesToCivilopediaTextLines(Ctx()));
    }

    [Fact]
    public void UniquesToCivilopediaTextLinesIsLazyAndAppendsToCollection()
    {
        var holder = UniqueHolder.With("Consumes [1] [Iron]");
        var list = new List<FormattedLine> { new("existing") };
        holder.UniquesToCivilopediaTextLines(list, Ctx(), colorConsumesResources: true);
        Assert.Equal(3, list.Count);
        Assert.Equal("existing", list[0].Text);
        Assert.True(list[1].IsEmpty());
        Assert.Equal("Resources/Iron", list[2].Link);
    }

    // endregion
}
