using Unciv.Core.Tests.Logic;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;

namespace Unciv.Core.Tests.UI.Civilopedia;

/// <summary>
/// T097: BaseUnit, Nation, Era and Technology descriptions (BaseUnitDescriptions.kt, NationDescriptions.kt, TechnologyDescriptions.kt).
/// No Kotlin fixtures exist; the expected values are derived by hand from the Kotlin source. The Building/Improvement sections are T129.
/// </summary>
public class ObjectDescriptionsCTests
{
    private static string Dump(IEnumerable<FormattedLine> lines) =>
        string.Join("\n", lines.Select(it =>
            it.Separator ? "---" :
            $"{new string(' ', it.Indent * 2)}{it.Text}"
            + (it.Link.Length != 0 ? $" ->{it.Link}" : "")
            + (it.Icon.Length != 0 ? $" icon:{it.Icon}" : "")
            + (it.Header != 0 ? $" h{it.Header}" : "")
            + (it.Color.Length != 0 ? $" color={it.Color}" : "")
            + (it.Centered ? " centered" : "")
            + (it.IconCrossed ? " crossed" : "")
            + (it.ExtraImage.Length != 0 ? $" img:{it.ExtraImage}" : "")));

    private static BaseUnit Unit(string name, string type = "Melee", int strength = 0, int movement = 2, int cost = 0, params string[] uniques)
    {
        var unit = new BaseUnit { Name = name, UnitType = type, Strength = strength, Movement = movement, Cost = cost };
        unit.Uniques.AddRange(uniques);
        return unit;
    }

    private static Ruleset NewRuleset(params BaseUnit[] units)
    {
        var ruleset = new Ruleset();
        ruleset.UnitTypes.Put("Melee", new UnitType { Name = "Melee" });
        foreach (var unit in units) ruleset.Units.Put(unit.Name, unit);
        return ruleset;
    }

    // region BaseUnit

    [Fact]
    public void BaseUnitShortDescription()
    {
        var unit = Unit("Spearman", strength: 10, movement: 3, uniques: "Cannot attack");
        unit.RangedStrength = 5;
        unit.Promotions.Add("Shock I");
        Assert.Equal("10†, 5‡, 3➡, Shock I, Cannot attack", unit.GetShortDescription());
        Assert.Equal("10†, 5‡, 3➡, Shock I", unit.GetShortDescription(it => it.Text == "Cannot attack"));
        unit.ReplacementTextForUniques = "Custom";
        Assert.Equal("10†, 5‡, 3➡, Shock I, Custom", unit.GetShortDescription());
    }

    [Fact]
    public void BaseUnitShortDescriptionOmitsDefaultMovement()
    {
        Assert.Equal("6†", Unit("Warrior", strength: 6).GetShortDescription());
        Assert.Equal("1➡", Unit("Slow", movement: 1).GetShortDescription());
        Assert.Equal("", Unit("Plain").GetShortDescription());
    }

    [Fact]
    public void BaseUnitCivilopediaLines()
    {
        var spearman = Unit("Spearman", strength: 11, cost: 56, uniques: "Cannot attack");
        spearman.RequiredTech = "Bronze Working";
        spearman.UpgradesTo = "Pikeman";
        spearman.ObsoleteTech = "Chivalry";
        spearman.Promotions.Add("Shock I");
        var pikeman = Unit("Pikeman", strength: 16, cost: 90);
        var hoplite = Unit("Hoplite", strength: 11, cost: 56);
        hoplite.UniqueTo = "Greece";
        hoplite.Replaces = "Spearman";
        var ruleset = NewRuleset(spearman, pikeman, hoplite);

        var lines = ((ICivilopediaText)spearman).GetCivilopediaTextLines(new CivilopediaContext(ruleset));
        Assert.Equal(
            "{Unit type}: {Melee} ->UnitType/Melee\n"
            + "11†, 2➡\n"
            + "{Cost}: 56⚙/260¤\n"
            + "\n"
            + "Cannot attack\n"
            + "\n"
            + "Required tech: [Bronze Working] ->Technology/Bronze Working\n"
            + "Upgrades to [Pikeman] ->Unit/Pikeman\n"
            + "Obsolete with [Chivalry] ->Technology/Chivalry\n"
            + "\n"
            + "{Free promotion:} {Shock I} ->Promotions/Shock I\n"
            + "\n"
            + "{See also}:\n"
            + "  Hoplite ->Unit/Hoplite",
            Dump(lines));
    }

    [Fact]
    public void BaseUnitCivilopediaLines_UniqueUnitUpgradesAndAirMovement()
    {
        var warrior = Unit("Warrior", strength: 6);
        warrior.UpgradesTo = "Swordsman";
        var archer = Unit("Archer", strength: 4);
        archer.UpgradesTo = "Swordsman";
        var swordsman = Unit("Swordsman", strength: 11);
        swordsman.UniqueTo = "Rome";
        swordsman.Replaces = "Legion";
        swordsman.RequiredResource = "Iron";
        swordsman.ReplacementTextForUniques = "Replaced text";
        var ruleset = NewRuleset(warrior, archer, swordsman);
        ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron" });
        // Warrior and Archer upgrade to the Swordsman and are not unique to another nation
        var lines = ((ICivilopediaText)swordsman).GetCivilopediaTextLines(new CivilopediaContext(ruleset));
        Assert.Equal(
            "{Unit type}: {Melee} ->UnitType/Melee\n"
            + "11†, 2➡\n"
            + "\n"
            + "Replaced text\n"
            + "\n"
            + "{Consumes [1] [Iron]} ->Resources/Iron color=#F42\n"
            + "\n"
            + "Unique to [Rome] ->Nation/Rome\n"
            + "  Replaces [Legion] ->Unit/Legion\n"
            + "\n"
            + "Can upgrade from:\n"
            + "    Archer ->Unit/Archer\n"
            + "    Warrior ->Unit/Warrior\n",
            Dump(lines));

        warrior.UniqueTo = "Greece"; // a unit unique to another nation is not an upgrade source, so a single one remains
        var single = Dump(((ICivilopediaText)swordsman).GetCivilopediaTextLines(new CivilopediaContext(ruleset)));
        Assert.EndsWith("  Replaces [Legion] ->Unit/Legion\nCan upgrade from [Archer] ->Unit/Archer", single);
    }

    [Fact]
    public void BaseUnitCivilopediaLines_RangedAirAndIntercept()
    {
        var ruleset = new Ruleset();
        ruleset.UnitTypes.Put("Fighter", new UnitType { Name = "Fighter", MovementType = "Air" });
        var plane = Unit("Fighter", "Fighter", strength: 0, movement: 8, cost: 100);
        plane.RangedStrength = 40;
        plane.Range = 8;
        plane.InterceptRange = 4;
        plane.HurryCostModifier = 50;
        ruleset.Units.Put(plane.Name, plane);
        var lines = Dump(((ICivilopediaText)plane).GetCivilopediaTextLines(new CivilopediaContext(ruleset)));
        Assert.Equal(
            "{Unit type}: {Fighter} ->UnitType/Fighter\n"
            + "40‡, 8…\n"
            + "{Cost}: 100⚙/" + (((INonPerpetualConstruction)plane).GetCivilopediaGoldCost() + "¤") + "\n"
            + "Air Intercept Range: [4]",
            lines);
    }

    [Fact]
    public void BaseUnitSortGroupAndSubCategory()
    {
        var ruleset = new Ruleset();
        ruleset.Eras.Put("Classical era", new Era { Name = "Classical era", EraNumber = 2 });
        ruleset.Technologies.Put("Iron Working", new Technology { Name = "Iron Working", Column = new TechColumn { Era = "Classical era" } });
        var unit = Unit("Legion");
        unit.RequiredTech = "Iron Working";
        Assert.Equal(2, unit.GetSortGroup(ruleset));
        Assert.Equal("Classical era", unit.GetSubCategory(ruleset));
        var none = Unit("Scout");
        Assert.Equal(100, none.GetSortGroup(ruleset));
        Assert.Equal("Other", none.GetSubCategory(ruleset));
    }

    [Fact]
    public void AddUnitDifferences()
    {
        var original = Unit("Warrior", strength: 6, uniques: "Lost thing");
        var better = Unit("Swordsman", strength: 11, uniques: "Cannot attack");
        better.Promotions.Add("Shock I");
        var ruleset = NewRuleset(original, better);
        var promotion = new Promotion { Name = "Shock I" };
        promotion.Uniques.Add("Bonus vs [Foo]");
        ruleset.UnitPromotions.Put(promotion.Name, promotion);

        var plain = FormattedLineListBuilder.BuildCivilopediaText(new CivilopediaContext(ruleset), b => b.AddUnitDifferences(ruleset, original, better));
        Assert.Equal(
            "† {[11] vs [6]}\n"
            + "Cannot attack\n"
            + "Lost ability (vs [Warrior]): [Lost thing]\n"
            + "{Shock I} (Bonus vs Foo) icon:Promotion/Shock I",
            Dump(plain));

        var linked = FormattedLineListBuilder.BuildCivilopediaText(new CivilopediaContext(ruleset), b => b.AddUnitDifferences(ruleset, original, better, true, 1));
        Assert.EndsWith("  {Shock I} (Bonus vs Foo) ->Promotion/Shock I", Dump(linked));
    }

    [Fact]
    public void AddUnitDifferences_RangedGainedLostAndReplacementText()
    {
        var melee = Unit("Melee", strength: 5);
        var ranged = Unit("Ranged", strength: 5);
        ranged.RangedStrength = 7;
        ranged.Range = 3;
        ranged.ReplacementTextForUniques = "Replacement";
        var ruleset = NewRuleset(melee, ranged);
        Assert.Equal(
            "[Gained] ‡ [7] … [3]\nReplacement",
            Dump(FormattedLineListBuilder.BuildCivilopediaText(new CivilopediaContext(ruleset), b => b.AddUnitDifferences(ruleset, melee, ranged))));
        Assert.Equal(
            "[Lost] ‡ [7] … [3]",
            Dump(FormattedLineListBuilder.BuildCivilopediaText(new CivilopediaContext(ruleset), b => b.AddUnitDifferences(ruleset, ranged, melee))));
        var farther = Unit("Farther", strength: 5, movement: 4);
        farther.RangedStrength = 9;
        farther.Range = 4;
        Assert.Equal(
            "‡ {[9] vs [7]}\n… {[4] vs [3]}\n➡ {[4] vs [2]}",
            Dump(FormattedLineListBuilder.BuildCivilopediaText(new CivilopediaContext(ruleset), b => b.AddUnitDifferences(ruleset, ranged, farther))));
    }

    // endregion
    // region Era

    [Fact]
    public void EraLinesHeaderAndSortGroup()
    {
        var ruleset = new Ruleset();
        var era = new Era { Name = "Ancient era", EraNumber = 3, IconRGB = [255, 128, 0] };
        ruleset.Eras.Put(era.Name, era);
        ruleset.Technologies.Put("Pottery", new Technology { Name = "Pottery", Column = new TechColumn { Era = "Ancient era" } });
        ruleset.Technologies.Put("Iron Working", new Technology { Name = "Iron Working", Column = new TechColumn { Era = "Classical era" } });
        ruleset.PolicyBranches.Put("Tradition", new PolicyBranch { Name = "Tradition", Era = "Ancient era" });
        ruleset.PolicyBranches.Put("Honor", new PolicyBranch { Name = "Honor", Era = "Classical era" });

        var pedia = (ICivilopediaText)era;
        var header = pedia.GetCivilopediaTextHeader()!;
        Assert.Equal("Ancient era", header.Text);
        Assert.Equal(2, header.Header);
        Assert.Equal("#ff8000", header.Color);
        Assert.Equal(3, pedia.GetSortGroup(ruleset));
        Assert.Equal(
            "Embarked strength: [3]†\n"
            + "Base unit buy cost: [200]¤\n"
            + "Research agreement cost: [300]¤\n"
            + "\n"
            + "Pottery ->Technology/Pottery\n"
            + "\n"
            + "{See also}:\n"
            + "  Tradition ->Policy/Tradition",
            Dump(pedia.GetCivilopediaTextLines(new CivilopediaContext(ruleset))));
    }

    [Fact]
    public void EraGetStartingUnits()
    {
        var ruleset = NewRuleset(Unit("Settler", uniques: "Founds a new city"), Unit("Worker"));
        var era = new Era { Name = "Ancient era", StartingSettlerCount = 2, StartingWorkerCount = 1, StartingMilitaryUnitCount = 2 };
        Assert.Equal(["Settler", "Settler", "Worker", "Warrior", "Warrior"], era.GetStartingUnits(ruleset));

        var modded = NewRuleset(Unit("Colonist", uniques: "Founds a new city"), Unit("Builder", uniques: "Can build [Land] improvements on tiles"));
        Assert.Equal(["Colonist", "Builder", "Warrior"], new Era { Name = "E", StartingWorkerCount = 1 }.GetStartingUnits(modded));
        Assert.Equal(["Colonist", "Warrior"], new Era { Name = "E", StartingWorkerCount = 0 }.GetStartingUnits(modded)); // no worker wanted, none is looked up
        Assert.Equal(["Colonist"], new Era { Name = "E", StartingMilitaryUnitCount = 0 }.GetStartingUnits(modded));

        var empty = NewRuleset(Unit("Worker"));
        var error = Assert.Throws<UncivShowableException>(() => era.GetStartingUnits(empty));
        Assert.Equal("No Settler unit found for era Ancient era", error.Message);
        var noWorker = NewRuleset(Unit("Settler", uniques: "Founds a new city"));
        var workerError = Assert.Throws<UncivShowableException>(() => new Era { Name = "E", StartingWorkerCount = 1 }.GetStartingUnits(noWorker));
        Assert.Equal("No Worker unit found for era E", workerError.Message);
    }

    // endregion
    // region Nation

    [Fact]
    public void NationSortGroupAndSubCategory()
    {
        var ruleset = new Ruleset();
        var major = new NationObject { Name = "Rome" };
        var cityState = new NationObject { Name = "Geneva", CityStateType = "Cultured" };
        var barbarians = new NationObject { Name = Constants.Barbarians };
        Assert.Equal((0, 1, 9), (major.GetSortGroup(ruleset), cityState.GetSortGroup(ruleset), barbarians.GetSortGroup(ruleset)));
        Assert.Equal(("Civilizations", "City-States", "Other"), (major.GetSubCategory(ruleset), cityState.GetSubCategory(ruleset), barbarians.GetSubCategory(ruleset)));
    }

    [Fact]
    public void NationLines_LeaderUniqueStartBiasAndUniqueUnits()
    {
        var archer = Unit("Archer", strength: 4, cost: 40);
        var bowman = Unit("Bowman", strength: 6, cost: 50);
        bowman.UniqueTo = "Babylon";
        bowman.Replaces = "Archer";
        var wonder = Unit("Ghost", strength: 1);
        wonder.UniqueTo = "Babylon";
        wonder.Replaces = "Missing";
        var hidden = Unit("Hidden", strength: 1, uniques: "Will not be displayed in Civilopedia");
        hidden.UniqueTo = "Babylon";
        var other = Unit("Other", strength: 1);
        other.UniqueTo = "Egypt";
        var ruleset = NewRuleset(archer, bowman, wonder, hidden, other);
        var babylon = new NationObject
        {
            Name = "Babylon",
            LeaderName = "Nebuchadnezzar II",
            UniqueName = "Ancient Glory",
            UniqueText = "Free tech",
            StartBias = ["Coast", "Avoid [Forest]"],
        };
        ruleset.Nations.Put(babylon.Name, babylon);

        var lines = ((ICivilopediaText)babylon).GetCivilopediaTextLines(new CivilopediaContext(ruleset));
        Assert.Equal(
            " img:LeaderIcons/Nebuchadnezzar II\n"
            + "[Nebuchadnezzar II] of [Babylon] h3 centered\n"
            + "\n"
            + "{Ancient Glory}: h4\n"
            + "  Free tech\n"
            + "\n"
            + "[Start bias:] Coast ->Terrain/Coast\n"
            + "  Avoid Forest ->Terrain/Forest crossed\n"
            + "\n"
            + "---\n"
            + "{Bowman} - ->Unit/Bowman\n"
            + "  Replaces [Archer] ->Unit/Archer\n"
            + "  Cost 50 vs 40\n"
            + "  † {[6] vs [4]}\n"
            + "\n"
            + "---\n"
            + "{Ghost} - ->Unit/Ghost\n"
            + "  Replaces [Missing], which is not found in the ruleset!\n"
            + "\n",
            Dump(lines) + "\n");
    }

    [Fact]
    public void NationLines_UniqueUnitWithoutReplacementShowsItsOwnPedia()
    {
        var ruleset = NewRuleset(Unit("Jaguar", strength: 8, cost: 60));
        ruleset.Units["Jaguar"].UniqueTo = "Aztecs";
        var aztecs = new NationObject { Name = "Aztecs" };
        var dump = Dump(((ICivilopediaText)aztecs).GetCivilopediaTextLines(new CivilopediaContext(ruleset)));
        Assert.Contains("---\n{Jaguar} - ->Unit/Jaguar\n  {Unit type}: {Melee} ->UnitType/Melee\n  8†, 2➡\n  {Cost}: 60⚙/", dump);
    }

    [Fact]
    public void NationLines_CityStateBonusesAndLuxuries()
    {
        var ruleset = new Ruleset();
        var type = new CityStateType { Name = "Mercantile", Color = [255, 0, 16], FriendBonusUniques = ["Provides a unique luxury"], AllyBonusUniques = ["Cannot attack"] };
        ruleset.CityStateTypes.Put(type.Name, type);
        var luxury = new TileResource { Name = "Spices" };
        luxury.Uniques.Add("Can only be created by Mercantile City-States");
        ruleset.TileResources.Put(luxury.Name, luxury);
        ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron" });
        var geneva = new NationObject { Name = "Geneva", CityStateType = "Mercantile" };

        Assert.Equal(
            "{Type}: {Mercantile} h4 color=#ff0010ff\n"
            + "\n"
            + "{When Friends:} \n"
            + "  Provides a unique luxury\n"
            + "\n"
            + "{When Allies:} \n"
            + "  Cannot attack\n"
            + "\n"
            + "The unique luxury is one of:\n"
            + "  Spices ->Resource/Spices\n"
            + "---\n"
            + "\n",
            Dump(((ICivilopediaText)geneva).GetCivilopediaTextLines(new CivilopediaContext(ruleset))) + "\n");
    }

    // endregion
    // region Technology

    private static (Ruleset Ruleset, Technology Agriculture, Technology Pottery) TechRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Eras.Put("Ancient era", new Era { Name = "Ancient era", EraNumber = 0, IconRGB = [255, 128, 0] });
        var column = new TechColumn { Era = "Ancient era" };
        var agriculture = new Technology { Name = "Agriculture", Cost = 20, Column = column };
        var pottery = new Technology { Name = "Pottery", Cost = 35, Column = column };
        pottery.Prerequisites.Add("Agriculture");
        var husbandry = new Technology { Name = "Animal Husbandry", Cost = 35, Column = column };
        husbandry.Prerequisites.Add("Agriculture");
        foreach (var tech in new[] { agriculture, pottery, husbandry }) ruleset.Technologies.Put(tech.Name, tech);
        var scout = Unit("Scout", strength: 5, movement: 3);
        scout.RequiredTech = "Pottery";
        ruleset.UnitTypes.Put("Melee", new UnitType { Name = "Melee" });
        ruleset.Units.Put(scout.Name, scout);
        var iron = new TileResource { Name = "Iron", RevealedBy = "Pottery" };
        ruleset.TileResources.Put(iron.Name, iron);
        return (ruleset, agriculture, pottery);
    }

    [Fact]
    public void TechnologySortGroupSubCategoryAndEra()
    {
        var (ruleset, agriculture, _) = TechRuleset();
        Assert.Equal(0, agriculture.GetSortGroup(ruleset));
        Assert.Equal("Ancient era", agriculture.GetSubCategory(ruleset));
        var orphan = new Technology { Name = "Orphan" };
        Assert.Equal(-1, orphan.GetSortGroup(ruleset));
        Assert.Null(orphan.GetSubCategory(ruleset));
        Assert.Same(ruleset.Eras["Ancient era"], ((Unciv.Models.Ruleset.Unique.IHasUniques)agriculture).Era(ruleset));
    }

    [Fact]
    public void TechnologyCivilopediaLines()
    {
        var (ruleset, agriculture, pottery) = TechRuleset();
        var context = new CivilopediaContext(ruleset);
        Assert.Equal(
            "Ancient era h3 color=#ff8000\n"
            + "\n"
            + "{Cost}: 20⍾\n"
            + "\n"
            + "Leads to:\n"
            + "Pottery ->Technology/Pottery\n"
            + "Animal Husbandry ->Technology/Animal Husbandry",
            Dump(((ICivilopediaText)agriculture).GetCivilopediaTextLines(context)));
        Assert.Equal(
            "Ancient era h3 color=#ff8000\n"
            + "\n"
            + "{Cost}: 35⍾\n"
            + "\n"
            + "Required tech: [Agriculture] ->Technology/Agriculture\n"
            + "\n"
            + "{Units enabled}:\n"
            + "Scout (5†, 3➡) ->Unit/Scout\n"
            + "\n"
            + "Reveals [Iron] on the map ->Resource/Iron",
            Dump(((ICivilopediaText)pottery).GetCivilopediaTextLines(context)));
    }

    [Fact]
    public void TechnologyCivilopediaLines_MultiplePrerequisitesObsoleteAndSeeAlso()
    {
        var (ruleset, _, _) = TechRuleset();
        var masonry = new Technology { Name = "Masonry", Cost = 50, Column = new TechColumn { Era = "Ancient era" } };
        masonry.Prerequisites.Add("Pottery");
        masonry.Prerequisites.Add("Agriculture");
        ruleset.Technologies.Put(masonry.Name, masonry);
        ruleset.TileResources["Iron"].Uniques.Add("Obsolete with [Masonry]");
        var improvement = new TileImprovement { Name = "Farm", TechRequired = "Masonry" };
        improvement.Uniques.Add("[+1 Food] <with [Pottery]>");
        ruleset.TileImprovements.Put(improvement.Name, improvement);
        var branch = new PolicyBranch { Name = "Tradition", Era = "Ancient era" };
        branch.Uniques.Add("Only available <after discovering [Masonry]>");
        ruleset.PolicyBranches.Put(branch.Name, branch);

        var lines = Dump(((ICivilopediaText)masonry).GetCivilopediaTextLines(new CivilopediaContext(ruleset)));
        Assert.Contains("\nRequires all of the following:\nPottery ->Technology/Pottery\nAgriculture ->Technology/Agriculture\n", lines);
        Assert.Contains("\n[Iron] obsoleted ->Resource/Iron", lines);
        Assert.Contains("\n{Tile improvements enabled}:\nFarm ->Improvement/Farm", lines);
    }

    [Fact]
    public void TechnologyDescription()
    {
        var (ruleset, _, pottery) = TechRuleset();
        pottery.CivilopediaText = [new FormattedLine("Who knows"), new FormattedLine("Header", header: 2)];
        pottery.Uniques.Add("Cannot attack");
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var civ = new Civilization(new NationObject { Name = "Rome" }) { CivId = "Rome", GameInfo = info };
        info.Civs.Add(civ);

        Assert.Equal(
            "Who knows\nCannot attack\nUnits enabled: \n • Scout (5†, 3➡)\n\nReveals Iron on the map", // Tr() drops the braces and brackets
            pottery.GetDescription(civ));
    }

    // endregion
}
