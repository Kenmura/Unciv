using Unciv.Assets;
using Unciv.Harness;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.Assets;

/// <summary>C027: the Civilopedia model over the Vanilla ruleset and a few hand-made rulesets.</summary>
[Collection("RulesetCache")]
public class PediaModelTests
{
    private static PediaLine Line(string text = "", string link = "", bool external = false, int header = 0, int size = 0, int indent = 0, string color = "#ffffff", bool separator = false, bool starred = false, bool centered = false) =>
        new(text, link, external, "", header, size, indent, color, separator, starred, centered, "");

    private static string Bb(PediaLine line) => PediaModel.ToBbcode(line, it => it.ToUpperInvariant());

    [Fact]
    public void Categories_AreTheEnginesInItsOrderAndHideEmptyOnes()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var categories = PediaModel.Categories(ruleset);
        Assert.Equal(categories.Select(it => it.Category).OrderBy(it => (int)it), categories.Select(it => it.Category));
        Assert.Contains(categories, it => it.Category == CivilopediaCategories.Unit && it.Label == "Units" && it.Count == PediaModel.Entries(ruleset, CivilopediaCategories.Unit).Count);
        Assert.Contains(categories, it => it.Category == CivilopediaCategories.Building);
        Assert.Contains(categories, it => it.Category == CivilopediaCategories.Wonder);

        var empty = new RulesetClass();
        Assert.DoesNotContain(PediaModel.Categories(empty), it => it.Category == CivilopediaCategories.Building);
    }

    [Fact]
    public void Entries_AreSortedByEngineGroupThenTranslatedName()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var units = PediaModel.Entries(ruleset, CivilopediaCategories.Unit);
        Assert.Equal(ruleset.Units.Count(it => !((Unciv.Models.Ruleset.Unique.IHasUniques)it.Value).IsHiddenFromCivilopedia(null, ruleset)), units.Count);
        var expected = units.OrderBy(it => ruleset.Units[it.Name].GetSortGroup(ruleset)).ThenBy(it => it.Name, StringComparer.Ordinal).Select(it => it.Name);
        Assert.Equal(expected, units.Select(it => it.Name));
        var warrior = units.Single(it => it.Name == "Warrior");
        Assert.Equal("Unit/Warrior", warrior.Link);
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.UnitIcon, "Warrior"), warrior.Icon);
        Assert.Equal(CivilopediaCategories.Unit, warrior.Category);
    }

    [Fact]
    public void Entries_HideWhatTheRulesetHides_AndNamelessOnes()
    {
        var ruleset = new RulesetClass();
        ruleset.Terrains["Visible"] = new Unciv.Models.Ruleset.Tile.Terrain { Name = "Visible", Type = Unciv.Models.Ruleset.Tile.TerrainType.Land };
        ruleset.Terrains["Hidden"] = new Unciv.Models.Ruleset.Tile.Terrain { Name = "Hidden", Type = Unciv.Models.Ruleset.Tile.TerrainType.Land, Uniques = ["Will not be displayed in Civilopedia"] };
        Assert.Equal(["Visible"], PediaModel.Entries(ruleset, CivilopediaCategories.Terrain).Select(it => it.Name));
        Assert.Null(PediaModel.Page(ruleset, CivilopediaCategories.Terrain, "Hidden"));
        Assert.Null(PediaModel.Page(ruleset, CivilopediaCategories.Terrain, "Nothing"));
    }

    [Fact]
    public void Page_IsTheEnginesAssembledText()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var page = PediaModel.Page(ruleset, CivilopediaCategories.Unit, "Warrior").NotNull();
        var engine = ((ICivilopediaText)ruleset.Units["Warrior"]).AssembleCivilopediaText(new CivilopediaContext(ruleset, null, StringComparer.Ordinal)).CivilopediaText;
        Assert.Equal(engine.Select(it => it.Text), page.Lines.Select(it => it.Text));
        Assert.Equal(engine.Select(it => it.Link), page.Lines.Select(it => it.Link));
        Assert.Equal("Warrior", page.Lines[0].Text);
        Assert.True(page.Lines[0].Header > 0);
        Assert.True(page.Lines[1].Separator);
        Assert.Contains(page.Lines, it => it.Link.StartsWith("Technology/", StringComparison.Ordinal) && !it.IsExternal);
        Assert.Equal("Warrior", page.Entry.Name);
    }

    [Fact]
    public void Page_ColorsAreHexAndSizesDefaultToZero()
    {
        var ruleset = new RulesetClass();
        ruleset.Terrains["T"] = new Unciv.Models.Ruleset.Tile.Terrain
        {
            Name = "T",
            Type = Unciv.Models.Ruleset.Tile.TerrainType.Land,
            CivilopediaText = [new FormattedLine("coloured", color: "#ff8000"), new FormattedLine("sized", size: 20), new FormattedLine("web", link: "https://example.org/x")],
        };
        var lines = PediaModel.Page(ruleset, CivilopediaCategories.Terrain, "T").NotNull().Lines;
        var coloured = lines.Single(it => it.Text == "coloured");
        Assert.Equal("#ff8000", coloured.Color);
        Assert.Equal(0, coloured.Size);
        Assert.Equal("#ffffff", lines[0].Color);
        Assert.Equal(20, lines.Single(it => it.Text == "sized").Size);
        var web = lines.Single(it => it.Link.StartsWith("https", StringComparison.Ordinal));
        Assert.True(web.IsExternal);
        Assert.Equal("[url=https://example.org/x]WEB[/url]", Bb(web)); // text: the text is shown, not the URL
        Assert.Equal("[url=https://example.org/x]HTTPS://EXAMPLE.ORG/X[/url]", Bb(web with { Text = "" })); // no text: the URL is shown
    }

    [Fact]
    public void Resolve_FollowsLinksByKeyOrLabel_AndRefusesBadOnes()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        Assert.Equal("Warrior", PediaModel.Resolve(ruleset, "Unit/Warrior").NotNull().Entry.Name);
        Assert.Equal("Warrior", PediaModel.Resolve(ruleset, "Units/Warrior").NotNull().Entry.Name);
        Assert.Null(PediaModel.Resolve(ruleset, "Unit/Nonexistent"));
        Assert.Null(PediaModel.Resolve(ruleset, "Nonsense/Warrior"));
        Assert.Null(PediaModel.Resolve(ruleset, "Warrior"));
        Assert.Null(PediaModel.Resolve(ruleset, "/Warrior"));
        Assert.Null(PediaModel.Resolve(ruleset, "Unit/"));
        Assert.Null(PediaModel.Resolve(ruleset, "https://example.org"));
    }

    [Fact]
    public void EveryVanillaEntryOfEveryCategoryHasAPage()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        foreach (var category in PediaModel.Categories(ruleset))
            foreach (var entry in PediaModel.Entries(ruleset, category.Category))
            {
                var page = PediaModel.Page(ruleset, category.Category, entry.Name);
                Assert.True(page != null && page.Lines.Count > 0, entry.Link);
            }
    }

    [Fact]
    public void IconFor_CoversTheCategoriesWithIcons()
    {
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.BuildingIcon, "Monument"), PediaModel.IconFor(CivilopediaCategories.Building, "Monument"));
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.BuildingIcon, "Stonehenge"), PediaModel.IconFor(CivilopediaCategories.Wonder, "Stonehenge"));
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.ResourceIcon, "Iron"), PediaModel.IconFor(CivilopediaCategories.Resource, "Iron"));
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.ImprovementIcon, "Farm"), PediaModel.IconFor(CivilopediaCategories.Improvement, "Farm"));
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.TechIcon, "Writing"), PediaModel.IconFor(CivilopediaCategories.Technology, "Writing"));
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.PolicyIcon, "Liberty"), PediaModel.IconFor(CivilopediaCategories.Policy, "Liberty"));
        Assert.Equal(AssetKey.Of(RulesetAssetKeys.PromotionIcon, "Shock I"), PediaModel.IconFor(CivilopediaCategories.Promotion, "Shock I"));
        Assert.Null(PediaModel.IconFor(CivilopediaCategories.Terrain, "Grassland"));
    }

    [Fact]
    public void Bbcode_WordsEachKindOfLine()
    {
        Assert.Equal("PLAIN", Bb(Line("plain")));
        Assert.Equal("[url=Unit/Warrior]WARRIOR[/url]", Bb(Line("Warrior", "Unit/Warrior")));
        Assert.Equal("[url=https://x.org]HTTPS://X.ORG[/url]", Bb(Line("", "https://x.org", external: true)));
        Assert.Equal("[color=#ff0000]RED[/color]", Bb(Line("red", color: "#ff0000")));
        Assert.Equal("[color=#ffd700]* STAR[/color]", Bb(Line("star", color: "#ffd700", starred: true)));
        Assert.Equal("[color=#ffffff]* STAR[/color]", Bb(Line("star", starred: true)));
        Assert.Equal("[font_size=20]BIG[/font_size]", Bb(Line("big", size: 20)));
        Assert.Equal("BIG", Bb(Line("big", size: Constants.DefaultFontSize)));
        Assert.Equal($"[b][font_size={FormattedLine.HeaderSizes[2]}]HEAD[/font_size][/b]", Bb(Line("head", header: 2)));
        Assert.Equal("[b]HEAD[/b]", Bb(Line("head", header: 99)));
        Assert.Equal("        INDENTED", Bb(Line("indented", indent: 2)));
        Assert.Equal("[center]MID[/center]", Bb(Line("mid", centered: true)));
        Assert.Equal("[color=#808080]------------------------[/color]", Bb(Line(color: "#808080", separator: true)));
        Assert.Equal("", Bb(Line()));
        Assert.Equal("[lb]X]", Bb(Line("[x]")));
    }

    [Fact]
    public void Search_FindsNamesCaseInsensitivelyAcrossCategoriesPrefixFirst()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var found = PediaModel.Search(ruleset, "  aRcH ");
        Assert.Contains(found, it => it.Link == "Unit/Archer");
        Assert.All(found, it => Assert.Contains("arch", it.Name, StringComparison.OrdinalIgnoreCase));
        var prefixCount = found.TakeWhile(it => it.Name.StartsWith("arch", StringComparison.OrdinalIgnoreCase)).Count();
        Assert.True(prefixCount > 0);
        Assert.All(found.Skip(prefixCount), it => Assert.False(it.Name.StartsWith("arch", StringComparison.OrdinalIgnoreCase)));
        Assert.True(found.Skip(prefixCount).Count() > 0, "Vanilla has names that only contain the text (Longbowman has no 'arch', Archery... )");
        Assert.Equal(found.Count, found.Select(it => it.Link).Distinct().Count());
    }

    [Fact]
    public void Search_OrdersByCategoryThenNameWithinAGroup()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var found = PediaModel.Search(ruleset, "an");
        var prefix = found.Where(it => it.Name.StartsWith("an", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(prefix.OrderBy(it => it.Category).ThenBy(it => it.Name, StringComparer.Ordinal).Select(it => it.Link), prefix.Select(it => it.Link));
        var rest = found.Except(prefix).ToList();
        Assert.Equal(rest.OrderBy(it => it.Category).ThenBy(it => it.Name, StringComparer.Ordinal).Select(it => it.Link), rest.Select(it => it.Link));
    }

    [Fact]
    public void Search_EmptyBlankNullOrUnknownFindsNothing()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        Assert.Empty(PediaModel.Search(ruleset, null));
        Assert.Empty(PediaModel.Search(ruleset, ""));
        Assert.Empty(PediaModel.Search(ruleset, "   "));
        Assert.Empty(PediaModel.Search(ruleset, "zzzqqq-nothing"));
    }

    [Fact]
    public void Search_IsCappedAtMaxResults()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var found = PediaModel.Search(ruleset, "e");
        Assert.Equal(PediaModel.MaxSearchResults, found.Count);
        Assert.Equal(50, PediaModel.MaxSearchResults);
    }

    [Fact]
    public void Search_DoesNotListHiddenEntries()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var all = Enum.GetValues<CivilopediaCategories>().SelectMany(it => PediaModel.Entries(ruleset, it)).Select(it => it.Link).ToHashSet();
        Assert.All(PediaModel.Search(ruleset, "a"), it => Assert.Contains(it.Link, all));
    }

    [Fact]
    public void Search_HugeAndHostileInputStaysBoundedAndFindsNothing()
    {
        var ruleset = ScenarioGame.LoadVanilla();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Empty(PediaModel.Search(ruleset, new string('a', 1_000_000)));
        Assert.Empty(PediaModel.Search(ruleset, "[lb]\u0000\uD800{}%$" + new string('\u202E', 1000)));
        Assert.Empty(PediaModel.Search(ruleset, string.Concat(Enumerable.Repeat("\uFE0F", 100_000))));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "hostile search input must not blow up");
    }
}
