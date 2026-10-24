using Unciv.Core.Tests.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.UI.Screens.Civilopedia;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;

namespace Unciv.Core.Tests.UI.Civilopedia;

/// <summary>
/// T129: Building and Improvement sections of Nation and Technology descriptions.
/// No Kotlin fixtures exist; the expected values are derived by hand from the Kotlin source.
/// </summary>
public class ObjectDescriptionsC2Tests
{
    private static string Dump(IEnumerable<FormattedLine> lines) =>
        string.Join("\n", lines.Select(it =>
            it.Separator ? "---" :
            $"{new string(' ', it.Indent * 2)}{it.Text}"
            + (it.Link.Length != 0 ? $" ->{it.Link}" : "")
            + (float.IsNaN(it.Padding) ? "" : $" pad={it.Padding}")));

    private static Building Build(string name, int cost = 100, string? uniqueTo = null, string? replaces = null, bool wonder = false)
    {
        var building = new Building { Name = name, Cost = cost, UniqueTo = uniqueTo, Replaces = replaces };
        building.IsWonder = wonder;
        return building;
    }

    private static List<FormattedLine> NationLines(Ruleset ruleset, NationObject nation) =>
        ((ICivilopediaText)nation).GetCivilopediaTextLines(new CivilopediaContext(ruleset));

    [Fact]
    public void NationUniqueBuildings_ReplacementDifferencesMissingAndPlain()
    {
        var ruleset = new Ruleset();
        var library = Build("Library", 90);
        library.Culture = 1;
        var paperMaker = Build("Paper Maker", 70, "Persia", "Library");
        paperMaker.Culture = 3;
        paperMaker.Uniques.Add("Cannot attack");
        var ghost = Build("Ghost", 10, "Persia", "Missing");
        var plain = Build("Satrap's Court", 120, "Persia");
        plain.Gold = 2;
        var hidden = Build("Hidden", 1, "Persia");
        hidden.Uniques.Add("Will not be displayed in Civilopedia");
        var foreign = Build("Foreign", 1, "Egypt");
        var common = Build("Common", 1);
        foreach (var it in new[] { library, paperMaker, ghost, plain, hidden, foreign, common }) ruleset.Buildings.Put(it.Name, it);
        var persia = new NationObject { Name = "Persia" };
        ruleset.Nations.Put(persia.Name, persia);
        Assert.Equal(
            "\n---\n"
            + "{Paper Maker} - ->Building/Paper Maker\n"
            + "  Replaces [Library] ->Building/Library\n"
            + "  \u266aCulture 3 vs 1\n"
            + "  Cost 70 vs 90\n"
            + "  Cannot attack\n"
            + "\n"
            + "---\n"
            + "{Ghost} - ->Building/Ghost\n"
            + "  Replaces [Missing], which is not found in the ruleset!\n"
            + "---\n"
            + "{Satrap's Court} - ->Building/Satrap's Court\n"
            + "  +2 \u00a4Gold",
            Dump(NationLines(ruleset, persia)));
    }

    [Fact]
    public void NationUniqueImprovements_ReplacementMissingAndPlain()
    {
        var ruleset = new Ruleset();
        var fort = new TileImprovement { Name = "Fort", TurnsToBuild = 5 };
        fort.TerrainsCanBeBuiltOn.Add("Hill");
        var kasbah = new TileImprovement { Name = "Kasbah", UniqueTo = "Morocco", Replaces = "Fort", TurnsToBuild = 7, Gold = 2 };
        kasbah.TerrainsCanBeBuiltOn.Add("Desert");
        var ghost = new TileImprovement { Name = "Ghost", UniqueTo = "Morocco", Replaces = "Missing" };
        var chateau = new TileImprovement { Name = "Chateau", UniqueTo = "Morocco", Food = 1 };
        chateau.TerrainsCanBeBuiltOn.Add("Plains");
        chateau.Uniques.Add("Cannot attack");
        var hidden = new TileImprovement { Name = "Hidden", UniqueTo = "Morocco" };
        hidden.Uniques.Add("Will not be displayed in Civilopedia");
        var foreign = new TileImprovement { Name = "Foreign", UniqueTo = "Egypt" };
        foreach (var it in new[] { fort, kasbah, ghost, chateau, hidden, foreign }) ruleset.TileImprovements.Put(it.Name, it);
        var morocco = new NationObject { Name = "Morocco" };

        var dump = Dump(NationLines(ruleset, morocco));
        Assert.Equal(
            "\n---\n"
            + "Kasbah ->Improvement/Kasbah\n"
            + "  +2 \u00a4Gold\n"
            + "  Replaces [Fort] ->Improvement/Fort\n"
            + "  \u00a4Gold 2 vs 0\n"
            + "  Can be built on [Desert]\n"
            + "  Cannot be built on [Hill]\n"
            + "  Turns to build 7 vs 5\n"
            + "\n"
            + "---\n"
            + "Ghost ->Improvement/Ghost\n"
            + "  \n"
            + "  Replaces [Missing], which is not found in the ruleset!\n"
            + "---\n"
            + "Chateau ->Improvement/Chateau\n"
            + "  +1 \u2042Food\n"
            + "  {Can be built on} {Plains} ->Terrain/Plains\n"
            + "  Cannot attack",
            dump);
    }

    private static (Ruleset, Technology) TechRuleset()
    {
        var ruleset = new Ruleset();
        var tech = new Technology { Name = "Construction", Cost = 50, Column = new TechColumn { Era = "Ancient era" } };
        tech.Uniques.Add("Cannot attack");
        ruleset.Technologies.Put(tech.Name, tech);

        var colosseum = Build("Colosseum", 100);
        colosseum.RequiredTech = "Construction";
        colosseum.Culture = 1;
        var greatWall = Build("Great Wall", 200, wonder: true);
        greatWall.RequiredTech = "Construction";
        greatWall.Uniques.Add("Cannot attack");
        var national = Build("Heroic Epic", 150);
        national.RequiredTech = "Construction";
        national.IsNationalWonder = true;
        var foreign = Build("Hidden", 1);
        foreign.RequiredTech = "Construction";
        foreign.Uniques.Add("Will not be displayed in Civilopedia");
        foreach (var it in new[] { colosseum, greatWall, national, foreign }) ruleset.Buildings.Put(it.Name, it);
        return (ruleset, tech);
    }

    [Fact]
    public void TechnologyCivilopediaLines_WondersThenBuildings()
    {
        var (ruleset, tech) = TechRuleset();
        var dump = Dump(((ICivilopediaText)tech).GetCivilopediaTextLines(new CivilopediaContext(ruleset)));
        Assert.EndsWith(
            "\n{Wonders enabled}:\n"
            + "Great Wall (Cannot attack) ->Wonder/Great Wall\n"
            + "Heroic Epic () ->Wonder/Heroic Epic\n"
            + "\n"
            + "{Buildings enabled}:\n"
            + "Colosseum (+1 \u266aCulture) ->Building/Colosseum pad=15",
            dump);
        Assert.DoesNotContain("Hidden", dump);
    }

    [Fact]
    public void TechnologyDescription_BuildingsThenWonders()
    {
        var (ruleset, tech) = TechRuleset();
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var civ = new Civilization(new NationObject { Name = "Rome" }) { CivId = "Rome", GameInfo = info };
        info.Civs.Add(civ);

        var text = tech.GetDescription(civ);
        Assert.EndsWith(
            "Buildings enabled: \n \u2022 Colosseum (+1 \u266aCulture)\n\n"
            + "Wonders enabled: \n \u2022 Great Wall (Cannot attack)\n\n \u2022 Heroic Epic ()\n",
            text);
        Assert.DoesNotContain("Hidden", text);
    }
}
