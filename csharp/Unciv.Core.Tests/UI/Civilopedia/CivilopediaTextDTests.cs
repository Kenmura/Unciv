using Unciv.Core.Tests.Models.Ruleset.Validation;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unit;
using Unciv.Models.Ruleset.Validation;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using BuildingObject = Unciv.Models.Ruleset.Building;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using PolicyObject = Unciv.Models.Ruleset.Policy;

namespace Unciv.Core.Tests.UI.Civilopedia;

/// <summary>
/// T108: the Civilopedia members of Policy, Speed, Victory, Belief, Tutorial, UnitType, UnitNameGroup, Promotion, Personality and Difficulty,
/// Ruleset.AllICivilopediaText and the validator's checkCivilopediaText.
/// No Kotlin fixtures exist; the expected values are derived by hand from the Kotlin source.
/// </summary>
[Collection("RulesetCache")]
public class CivilopediaTextDTests
{
    private static CivilopediaContext Ctx(Ruleset ruleset) => new(ruleset);

    private static string Dump(IEnumerable<FormattedLine> lines) =>
        string.Join("\n", lines.Select(it =>
            it.Separator ? "---" :
            $"{new string(' ', it.Indent * 2)}{it.Text}"
            + (it.Link.Length != 0 ? $" ->{it.Link}" : "")
            + (it.Header != 0 ? $" h{it.Header}" : "")
            + (it.Color.Length != 0 ? $" color={it.Color}" : "")
            + (it.Starred ? " *" : "")
            + (it.Centered ? " centered" : "")
            + (it.ExtraImage.Length != 0 ? $" img:{it.ExtraImage}" : "")));

    // region KotlinNumberText

    [Theory]
    [InlineData(100f, "100.0")]
    [InlineData(0f, "0.0")]
    [InlineData(-0f, "-0.0")]
    [InlineData(6.5f, "6.5")]
    [InlineData(0.5f, "0.5")]
    [InlineData(0.001f, "0.001")]
    [InlineData(0.0001f, "1.0E-4")]
    [InlineData(1234567f, "1234567.0")]
    [InlineData(1e7f, "1.0E7")]
    [InlineData(12345678f, "1.2345678E7")]
    [InlineData(-150f, "-150.0")]
    [InlineData(66.700005f, "66.700005")]
    public void KotlinFloatToString(float value, string expected) => Assert.Equal(expected, KotlinNumberText.FloatToString(value));

    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(1e20f, int.MaxValue)]
    [InlineData(-1e20f, int.MinValue)]
    [InlineData(-2.9f, -2)]
    [InlineData(2.9f, 2)]
    public void KotlinFloatToInt(float value, int expected) => Assert.Equal(expected, KotlinNumberText.FloatToInt(value));

    // endregion

    // region Speed

    [Fact]
    public void SpeedLines()
    {
        var speed = new Speed { Name = "Quick", Modifier = 0.67f, StartYear = -4000f, CityStateTributeScalingInterval = 6.5f };
        var lines = ((ICivilopediaText)speed).GetCivilopediaTextLines(Ctx(new Ruleset()));
        var text = lines.Select(it => it.Text).ToList();
        Assert.Equal("General speed modifier: [67.0]%⏳", text[0]);
        Assert.Equal("Gold cost modifier: [100.0]%¤", text[2]); // Kotlin defaults refer to `modifier` at construction time, so they stay at the constructor-time 1.0
        Assert.Equal(15, lines.Count);
        Assert.Equal("City-state tribute scaling interval: [6.5] turns⏳", text[9]);
        Assert.Equal("Diplomatic deal duration: [30] turns⏳", text[7]);
        Assert.Equal("Adjacent city religious pressure: [6]☮", text[12]);
        Assert.Equal("Peace deal duration: [10] turns⏳", text[13]);
        Assert.Equal("Start year: [4,000 BC]", text[14]); // "{[4000] BC}]".Tr() formats the number parameter
    }

    [Fact]
    public void SpeedHeaderAndSortGroup()
    {
        var speed = new Speed { Name = "Epic", Modifier = 1.5f };
        var header = ((ICivilopediaText)speed).GetCivilopediaTextHeader()!;
        Assert.Equal("Epic", header.Text);
        Assert.Equal(2, header.Header);
        Assert.Equal("", header.Icon);
        Assert.Equal(1500, ((ICivilopediaText)speed).GetSortGroup(new Ruleset()));
        Assert.Equal("Speed/Epic", speed.MakeLink());
    }

    [Fact]
    public void SpeedUniquesAreListed()
    {
        var speed = new Speed { Name = "Quick", Uniques = ["[+10]% Strength"] };
        var lines = ((ICivilopediaText)speed).GetCivilopediaTextLines(Ctx(new Ruleset()));
        Assert.Equal(17, lines.Count); // 15 + separator + unique
        Assert.True(lines[15].IsEmpty());
    }

    // endregion

    // region Tutorial

    [Fact]
    public void TutorialLinesSubCategoryAndSortGroup()
    {
        var ruleset = new Ruleset();
        var tutorial = new Tutorial { Name = "Test", Steps = ["one", "two"] };
        var lines = ((ICivilopediaText)tutorial).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.Equal(["one", "two"], lines.Select(it => it.Text));
        Assert.Empty(((ICivilopediaText)new Tutorial { Name = "x" }).GetCivilopediaTextLines(Ctx(ruleset)));

        Assert.Equal("Tutorials", ((ICivilopediaText)tutorial).GetSubCategory(ruleset));
        tutorial.Category = "";
        Assert.Equal("Tutorials", ((ICivilopediaText)tutorial).GetSubCategory(ruleset));
        Assert.Equal(-1, ((ICivilopediaText)new Tutorial()).GetSortGroup(ruleset));

        tutorial.Category = "Combat";
        Assert.Equal("Combat", ((ICivilopediaText)tutorial).GetSubCategory(ruleset));
        // Translation returns the text unchanged without loaded translations: 'c' = 99, 'o' = 111
        Assert.Equal((99 << 16) + 111, ((ICivilopediaText)tutorial).GetSortGroup(ruleset));
        tutorial.Category = "A";
        Assert.Equal('a', ((ICivilopediaText)tutorial).GetSortGroup(ruleset));
    }

    // endregion

    // region UnitType

    private static Ruleset UnitRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.UnitTypes["Melee"] = new UnitType("Melee", "Land");
        ruleset.UnitTypes["Fighter"] = new UnitType("Fighter", "Air");
        ruleset.UnitTypes["Unused"] = new UnitType("Unused", "Land");
        ruleset.Units["Warrior"] = new BaseUnit { Name = "Warrior", UnitType = "Melee" };
        ruleset.Units["Swordsman"] = new BaseUnit { Name = "Swordsman", UnitType = "Melee", Promotions = new Unciv.Utils.JavaHashSet<string>() };
        ruleset.Units["Fighter Jet"] = new BaseUnit { Name = "Fighter Jet", UnitType = "Fighter" };
        ruleset.UnitPromotions["Shock"] = new Promotion { Name = "Shock", UnitTypes = ["Melee"] };
        return ruleset;
    }

    [Fact]
    public void UnitTypeLines()
    {
        var ruleset = UnitRuleset();
        var lines = ((ICivilopediaText)ruleset.UnitTypes["Melee"]).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.Equal(
            "Domain: [Land] ->UnitType/Domain: [Land] color=#ffc080\n---\nUnits: h4\nWarrior ->Unit/Warrior\nSwordsman ->Unit/Swordsman\nPromotions h4\nShock ->Promotion/Shock",
            Dump(lines));
    }

    [Fact]
    public void UnitTypeDomainLines()
    {
        var ruleset = UnitRuleset();
        var domain = new UnitType("Domain: [Land]", "Land");
        var lines = ((ICivilopediaText)domain).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.Equal("{Unit types}: h4\nMelee ->UnitType/Melee", Dump(lines));
    }

    [Fact]
    public void UnitTypeSortGroupAndSubCategory()
    {
        var ruleset = UnitRuleset();
        Assert.Equal(1, ((ICivilopediaText)ruleset.UnitTypes["Melee"]).GetSortGroup(ruleset));
        Assert.Equal(0, ((ICivilopediaText)new UnitType("Domain: [Land]", "Land")).GetSortGroup(ruleset));
        Assert.Equal(5, ((ICivilopediaText)ruleset.UnitTypes["Fighter"]).GetSortGroup(ruleset));
        Assert.Equal(201, ((ICivilopediaText)new UnitType("Plain")).GetSortGroup(ruleset));
        Assert.Equal("Air", ((ICivilopediaText)ruleset.UnitTypes["Fighter"]).GetSubCategory(ruleset));
        Assert.Equal("Other", ((ICivilopediaText)new UnitType("Plain")).GetSubCategory(ruleset));
    }

    // endregion

    // region Promotion

    [Fact]
    public void PromotionLines()
    {
        var ruleset = UnitRuleset();
        ruleset.UnitPromotions["Shock II"] = new Promotion { Name = "Shock II", Prerequisites = ["Shock"], UnitTypes = ["Warrior", "Melee"] };
        ruleset.UnitPromotions["Drill"] = new Promotion { Name = "Drill", Prerequisites = ["Shock", "Shock II", "Missing"], UnitTypes = ["Fighter"] };
        ruleset.Units["Warrior"].Promotions.Add("Shock");

        Assert.Equal(
            "\nAvailable for [Melee] ->UnitType/Melee\n\nFree for [Warrior] ->Unit/Warrior",
            Dump(((ICivilopediaText)ruleset.UnitPromotions["Shock"]).GetCivilopediaTextLines(Ctx(ruleset))));
        Assert.Equal(
            "\nRequires [Shock] ->Promotion/Shock\n\nAvailable for:\n  Warrior ->Unit/Warrior\n  Melee ->UnitType/Melee",
            Dump(((ICivilopediaText)ruleset.UnitPromotions["Shock II"]).GetCivilopediaTextLines(Ctx(ruleset))));
        Assert.Equal(
            "\nRequires at least one of the following:\nShock ->Promotion/Shock\nShock II ->Promotion/Shock II\n\nAvailable for [Fighter] ->UnitType/Fighter",
            Dump(((ICivilopediaText)ruleset.UnitPromotions["Drill"]).GetCivilopediaTextLines(Ctx(ruleset))));
    }

    [Fact]
    public void PromotionSingleBaseUnitLinksUnit()
    {
        var ruleset = UnitRuleset();
        var promotion = new Promotion { Name = "Solo", UnitTypes = ["Warrior"] };
        Assert.Equal("\nAvailable for [Warrior] ->Unit/Warrior", Dump(((ICivilopediaText)promotion).GetCivilopediaTextLines(Ctx(ruleset))));
    }

    [Fact]
    public void PromotionGrantors()
    {
        var ruleset = UnitRuleset();
        var promotion = new Promotion { Name = "Medic" };
        ruleset.Buildings["Barracks"] = new BuildingObject { Name = "Barracks", Uniques = ["All newly-trained [Melee] units [in this city] receive the [Medic] promotion"] };
        ruleset.Terrains["Hills"] = new Terrain { Name = "Hills", Uniques = ["Grants [Medic] ([heals]) to adjacent [Melee] units for the rest of the game"] };
        var text = Dump(((ICivilopediaText)promotion).GetCivilopediaTextLines(Ctx(ruleset)));
        Assert.Equal("\nGranted by:\n  Barracks ->Building/Barracks\n  Hills ->Terrain/Hills", text);
    }

    [Fact]
    public void PromotionSortGroupAndSubCategory()
    {
        var ruleset = UnitRuleset();
        Assert.Equal(0, ((ICivilopediaText)ruleset.UnitPromotions["Shock"]).GetSortGroup(ruleset));
        Assert.Equal(1000, ((ICivilopediaText)new Promotion()).GetSortGroup(ruleset));
        Assert.Equal(1000, ((ICivilopediaText)new Promotion { UnitTypes = ["Nope"] }).GetSortGroup(ruleset));
        Assert.Equal("Melee", ((ICivilopediaText)ruleset.UnitPromotions["Shock"]).GetSubCategory(ruleset));
        Assert.Equal("Other", ((ICivilopediaText)new Promotion()).GetSubCategory(ruleset));
    }

    // endregion

    // region UnitNameGroup

    [Fact]
    public void UnitNameGroupLinesAndSorting()
    {
        var ruleset = UnitRuleset();
        ruleset.Units["Warrior"].Uniques = ["Can be named [Hero]"];
        var group = new UnitNameGroup { Name = "Heroes", UnitNames = ["b c", "Zeta", "alpha"] };
        ruleset.Units["Warrior"] = new BaseUnit { Name = "Warrior", UnitType = "Melee", Uniques = ["Gets a name from [Heroes]"] };
        // OneTimeUnitGetsName text: check the type exists
        var lines = ((ICivilopediaText)group).GetCivilopediaTextLines(new CivilopediaContext(ruleset, Language: "de", Collator: StringComparer.Ordinal));
        var dump = Dump(lines);
        Assert.Contains("Unit Names h4", dump);
        // Ordinal: uppercase before lowercase
        Assert.Contains("Zeta ->https://de.wikipedia.org/w/index.php?search=Zeta\nalpha ->https://de.wikipedia.org/w/index.php?search=alpha\nb c ->https://de.wikipedia.org/w/index.php?search=b+c", dump);
        Assert.Equal("Unit Names/Heroes", group.MakeLink());
    }

    [Fact]
    public void UnitNameGroupUrlEncodingMatchesJava()
    {
        var ruleset = new Ruleset();
        var group = new UnitNameGroup { Name = "G", UnitNames = ["José *-._~!()'&/ 中"] };
        var line = ((ICivilopediaText)group).GetCivilopediaTextLines(Ctx(ruleset)).Last();
        Assert.Equal("https://en.wikipedia.org/w/index.php?search=Jos%C3%A9+*-._%7E%21%28%29%27%26%2F+%E4%B8%AD", line.Link);
    }

    [Fact]
    public void UnitNameGroupSortGroupAndSubCategoryWithoutUnits()
    {
        var ruleset = new Ruleset();
        var group = new UnitNameGroup { Name = "Heroes", UnitNames = ["a"] };
        Assert.Equal(100, ((ICivilopediaText)group).GetSortGroup(ruleset));
        Assert.Equal("Other", ((ICivilopediaText)group).GetSubCategory(ruleset));
    }

    // endregion

    // region Policy

    private static Ruleset PolicyRuleset()
    {
        var ruleset = new Ruleset();
        ruleset.Eras["Ancient era"] = new Era { Name = "Ancient era", EraNumber = 0 };
        ruleset.Eras["Medieval era"] = new Era { Name = "Medieval era", EraNumber = 2 };
        var tradition = new PolicyBranch { Name = "Tradition", Era = "Medieval era" };
        tradition.Branch = tradition;
        var oligarchy = new PolicyObject { Name = "Oligarchy", Requires = ["Tradition"], Branch = tradition };
        var legalism = new PolicyObject { Name = "Legalism", Requires = ["Tradition"], Branch = tradition };
        var aristocracy = new PolicyObject { Name = "Aristocracy", Requires = ["Oligarchy", "Legalism"], Branch = tradition };
        var complete = new PolicyObject { Name = "Tradition Complete", Branch = tradition, Requires = ["Aristocracy"] };
        var piety = new PolicyBranch { Name = "Piety", Era = "Ancient era" };
        piety.Branch = piety;
        foreach (var policy in new PolicyObject[] { tradition, oligarchy, legalism, aristocracy, complete, piety })
            ruleset.Policies[policy.Name] = policy;
        ruleset.PolicyBranches["Tradition"] = tradition;
        ruleset.PolicyBranches["Piety"] = piety;
        return ruleset;
    }

    [Fact]
    public void PolicyBranchLines()
    {
        var ruleset = PolicyRuleset();
        var lines = ((ICivilopediaText)ruleset.Policies["Tradition"]).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.Equal("{Unlocked at} {Medieval era} ->Era/Medieval era h4 color=" + ruleset.Eras["Medieval era"].GetHexColor() + "\n\nLeads to:\n  Oligarchy ->Policy/Oligarchy\n  Legalism ->Policy/Legalism", Dump(lines));
    }

    [Fact]
    public void PolicyMemberLines()
    {
        var ruleset = PolicyRuleset();
        Assert.Equal(
            "Policy branch: [Tradition] ->Policy/Tradition\n\nRequires [Tradition] ->Policy/Tradition\n\nLeads to [Aristocracy] ->Policy/Aristocracy",
            Dump(((ICivilopediaText)ruleset.Policies["Oligarchy"]).GetCivilopediaTextLines(Ctx(ruleset))));
        Assert.Equal(
            "Policy branch: [Tradition] ->Policy/Tradition\n\nRequires all of the following:\nOligarchy ->Policy/Oligarchy\nLegalism ->Policy/Legalism",
            Dump(((ICivilopediaText)ruleset.Policies["Aristocracy"]).GetCivilopediaTextLines(Ctx(ruleset))));
        // BranchComplete: no requires block, and a 'requires' pointing at it is not listed as leading to it
        Assert.Equal(
            "Policy branch: [Tradition] ->Policy/Tradition",
            Dump(((ICivilopediaText)ruleset.Policies["Tradition Complete"]).GetCivilopediaTextLines(Ctx(ruleset))));
    }

    [Fact]
    public void PolicySortGroupAndSubCategory()
    {
        var ruleset = PolicyRuleset();
        // era number * 10000 + branch index * 100 + PolicyBranchTypes ordinal (BranchStart 0, Member 1, BranchComplete 2)
        Assert.Equal(20000, ((ICivilopediaText)ruleset.Policies["Tradition"]).GetSortGroup(ruleset));
        Assert.Equal(20001, ((ICivilopediaText)ruleset.Policies["Oligarchy"]).GetSortGroup(ruleset));
        Assert.Equal(20002, ((ICivilopediaText)ruleset.Policies["Tradition Complete"]).GetSortGroup(ruleset));
        Assert.Equal(100, ((ICivilopediaText)ruleset.Policies["Piety"]).GetSortGroup(ruleset));
        Assert.Equal("Tradition", ((ICivilopediaText)ruleset.Policies["Oligarchy"]).GetSubCategory(ruleset));
    }

    [Fact]
    public void PolicyEnablesAndDisables()
    {
        var ruleset = PolicyRuleset();
        ruleset.Buildings["Temple"] = new BuildingObject { Name = "Temple", Uniques = ["Only available <after adopting [Oligarchy]>"] };
        ruleset.Buildings["Court"] = new BuildingObject { Name = "Court", Uniques = ["Only available <before adopting [Oligarchy]>"] };
        // Kotlin quirk kept: the Unavailable half of isEnabledByPolicy does not ignore conditionals, so "Unavailable <before adopting>" never matches without a civ
        ruleset.Units["Spear"] = new BaseUnit { Name = "Spear", Uniques = ["Unavailable <before adopting [Oligarchy]>"] };
        ruleset.Units["Lance"] = new BaseUnit { Name = "Lance", Uniques = ["Only available <after adopting [Oligarchy]>"] };
        ruleset.Units["Pike"] = new BaseUnit { Name = "Pike", Uniques = ["Unavailable <after adopting [Oligarchy]>"] };
        var text = Dump(((ICivilopediaText)ruleset.Policies["Oligarchy"]).GetCivilopediaTextLines(Ctx(ruleset)));
        Assert.Contains("Enables:\n  Temple ->Building/Temple\n  Lance ->Unit/Lance\nDisables:", text);
        Assert.Contains("Disables:\n  Court ->Building/Court\n  Pike ->Unit/Pike", text);
        Assert.DoesNotContain("Spear", text);
    }

    // endregion

    // region Belief

    [Fact]
    public void BeliefLines()
    {
        var ruleset = new Ruleset();
        var belief = new Belief(BeliefType.Founder) { Name = "Tithe", Uniques = ["[+1 Gold] from every [Temple]"] };
        var plain = ((ICivilopediaText)belief).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.Equal("{Type}: {Founder} color=#c00000\n[+1 Gold] from every [Temple]", Dump(plain));
        var withHeader = belief.GetCivilopediaTextLines(Ctx(ruleset), true);
        Assert.Equal(24, withHeader[0].Size);
        Assert.True(withHeader[0].Centered);
        Assert.True(withHeader[2].Centered);
        Assert.True(withHeader[1].IsEmpty());
        // BeliefType.None prints no type line
        Assert.Empty(((ICivilopediaText)new Belief { Name = "x" }).GetCivilopediaTextLines(Ctx(ruleset)));
    }

    [Fact]
    public void BeliefHeaderSortGroupSubCategory()
    {
        var ruleset = new Ruleset();
        var belief = new Belief(BeliefType.Follower) { Name = "Feast" };
        var header = ((ICivilopediaText)belief).GetCivilopediaTextHeader()!;
        Assert.Equal(("Feast", "Belief/Feast", 2, ""), (header.Text, header.Icon, header.Header, header.Color));
        Assert.Equal("#e34a2b", ((ICivilopediaText)new Belief { Name = "n" }).GetCivilopediaTextHeader()!.Color);
        Assert.Equal(3, ((ICivilopediaText)belief).GetSortGroup(ruleset));
        Assert.Equal("Follower", ((ICivilopediaText)belief).GetSubCategory(ruleset));
        Assert.Null(((ICivilopediaText)new Belief()).GetSubCategory(ruleset));
    }

    [Fact]
    public void BeliefsMatchingAndSeeAlso()
    {
        var ruleset = new Ruleset();
        ruleset.Beliefs["Tithe"] = new Belief(BeliefType.Founder) { Name = "Tithe", Uniques = ["[+1 Gold] from every [Temple]"] };
        ruleset.Beliefs["Hidden"] = new Belief(BeliefType.Founder) { Name = "Hidden", Uniques = ["[+1 Gold] from every [Temple]", "Will not be displayed in Civilopedia"] };
        ruleset.Beliefs["Other"] = new Belief(BeliefType.Founder) { Name = "Other", Uniques = ["[+1 Gold] from every [Shrine]"] };
        Assert.Equal(["Tithe"], Belief.GetBeliefsMatching("Temple", ruleset).Select(it => it.Name));
        Assert.Equal("\n{See also}:\n  Tithe ->Belief/Tithe", Dump(Belief.GetCivilopediaTextMatching("Temple", ruleset)));
        Assert.Equal(["Tithe"], Belief.GetCivilopediaTextMatching("Temple", ruleset, withSeeAlso: false).Select(it => it.Text));
        Assert.Empty(Belief.GetCivilopediaTextMatching("Nothing", ruleset));
    }

    [Fact]
    public void BeliefsMatchingHonoursTheGivenGameInfo()
    {
        var ruleset = new Ruleset();
        ruleset.Beliefs["Tithe"] = new Belief(BeliefType.Founder) { Name = "Tithe", Uniques = ["[+1 Gold] from every [Temple]", "Only available <when religion is enabled>"] };
        var game = new Unciv.Core.Tests.Models.Ruleset.Unique.FakeGameInfo { ReligionEnabled = false };
        Assert.Equal(["Tithe"], Belief.GetBeliefsMatching("Temple", ruleset).Select(it => it.Name));
        Assert.Empty(Belief.GetBeliefsMatching("Temple", ruleset, game));
        Assert.Empty(Belief.GetCivilopediaTextMatching("Temple", ruleset, gameInfo: game));
        game.ReligionEnabled = true;
        Assert.Equal(["Tithe"], Belief.GetBeliefsMatching("Temple", ruleset, game).Select(it => it.Name));
    }

    [Theory]
    [InlineData(1.4E-45f, "1.4E-45")]
    [InlineData(2.8E-45f, "2.8E-45")]
    [InlineData(1e-10f, "1.0E-10")]
    public void KotlinFloatToStringSubnormalsUseTwoDigits(float value, string expected) => Assert.Equal(expected, KotlinNumberText.FloatToString(value));

    [Fact]
    public void CategoryIteratorAppendsVirtualBeliefEntries()
    {
        var ruleset = new Ruleset();
        ruleset.Beliefs["Tithe"] = new Belief(BeliefType.Founder) { Name = "Tithe" };
        ruleset.Religions.AddRange(["Zoroastrianism", "Buddhism", "Islam"]);
        var entries = CivilopediaCategories.Belief.GetCategoryIterator(ruleset, null, new CivilopediaContext(ruleset, Collator: StringComparer.Ordinal));
        Assert.Equal(3, entries.Count);
        var beliefs = entries.Cast<Belief>().ToList();
        Assert.Equal(["Tithe", "Beliefs", "Religions"], beliefs.Select(it => it.Name));
        Assert.Equal(2, beliefs[1].CivilopediaText.Count);
        Assert.Equal("There are four types of beliefs: Pantheon, Founder, Follower and Enhancer beliefs.", beliefs[1].CivilopediaText[0].Text);
        Assert.Equal(["Buddhism", "Islam", "Zoroastrianism"], beliefs[2].CivilopediaText.Select(it => it.Text));
        Assert.Equal("Belief/Islam", beliefs[2].CivilopediaText[1].Icon);
    }

    // endregion

    // region Personality

    [Fact]
    public void PersonalityNeutral()
    {
        var ruleset = new Ruleset();
        var neutral = Personality.NeutralPersonality;
        var personality = (ICivilopediaText)neutral;
        Assert.Equal("Neutral personality", Dump(personality.GetCivilopediaTextLines(Ctx(ruleset))));
        Assert.Equal("", personality.GetIconName());
    }

    [Fact]
    public void PersonalityLinesBiasesAndPriorities()
    {
        var ruleset = new Ruleset();
        ruleset.Nations["Rome"] = new NationObject { Name = "Rome", Personality = "Builder" };
        ruleset.Nations["Greece"] = new NationObject { Name = "Greece", Personality = "Other" };
        var personality = new Personality
        {
            Name = "Builder",
            PreferredVictoryType = "Cultural",
            Production = 10f,
            Gold = 2.5f,
            Priorities = new Dictionary<string, int> { ["Tradition"] = 3, ["Piety"] = 0 },
        };
        var text = Dump(((ICivilopediaText)personality).GetCivilopediaTextLines(Ctx(ruleset)));
        Assert.Equal(
            "See also: [Rome] ->Nation/Rome\n\nPreferred victory type: [Cultural]\n\nBiases:\n  {Productive}: 100%\n  {Greedy}: -50%\n\nPolicy priorities:\n  [Tradition]: [3]\n  [Piety]: [0]",
            text);
        var header = ((ICivilopediaText)personality).GetCivilopediaTextHeader()!;
        Assert.Equal(("[Builder]'s personality", "Personality/Builder", 2), (header.Text, header.Icon, header.Header));
    }

    [Fact]
    public void PersonalityWithNothingToSayIsEmpty()
    {
        var personality = new Personality { Name = "Plain" };
        Assert.Empty(((ICivilopediaText)personality).GetCivilopediaTextLines(Ctx(new Ruleset())));
    }

    // endregion

    // region Difficulty

    [Fact]
    public void DifficultyLines()
    {
        var ruleset = new Ruleset();
        ruleset.Difficulties["Settler"] = new Difficulty { Name = "Settler" };
        var difficulty = new Difficulty
        {
            Name = "Prince",
            BaseHappiness = 9,
            ExtraHappinessPerLuxury = 1.9f,
            ResearchCostModifier = 0.75f,
            BarbarianBonus = 0.2f,
            PlayerBonusStartingUnits = ["Settler", "Settler", "Warrior"],
            AiDifficultyLevel = "Chieftain",
            AiFreeTechs = ["Agriculture"],
            AiMajorCivBonusStartingUnits = ["Worker"],
            AiCityStateBonusStartingUnits = ["Worker", "Worker", "Worker"],
            TurnBarbariansCanEnterPlayerTiles = 20,
        };
        ruleset.Difficulties["Prince"] = difficulty;
        var lines = ((ICivilopediaText)difficulty).GetCivilopediaTextLines(Ctx(ruleset));
        var text = lines.Select(it => (it.Indent, it.Text)).ToList();
        Assert.Equal((0, "Player settings"), text[0]);
        Assert.Equal(3, lines[0].Header);
        Assert.Equal((1, "{Base happiness}: 9 ⌣"), text[1]);
        Assert.Equal((1, "{Extra happiness per luxury}: 1 ⌣"), text[2]);
        Assert.Equal((1, "{Research cost modifier}: 75% ⍾"), text[3]);
        Assert.Equal((1, "{Unhappiness modifier}: 100%"), text[7]);
        Assert.Equal((1, "{Bonus vs. Barbarians}: 20% †"), text[8]);
        Assert.Equal((1, "{Barbarian spawning delay}: 0"), text[9]);
        Assert.True(lines[10].IsEmpty());
        Assert.Equal((1, "{Bonus starting units}:"), text[11]);
        // grouped by name in first-occurrence order: "2 [Settler]" then "[Warrior]", through virtual Uniques
        Assert.Equal(2, lines[12].Indent);
        Assert.Equal(2, lines[13].Indent);
        Assert.Contains("Settler", lines[12].Text);
        Assert.Contains("Warrior", lines[13].Text);
        Assert.Contains(lines, it => it.Indent == 2 && it.Link == "Technology/Agriculture");
        Assert.Contains((1, "{AI difficulty level}: {Chieftain}"), text);
        Assert.Contains((1, "{City state bonus starting units}:"), text);
        // after defaults(FormattedLine()) the indent is back to 0
        Assert.Contains((0, "{Turns until barbarians enter player tiles}: 20 ⏳"), text);
        Assert.Contains((0, "{Gold reward for clearing barbarian camps}: 25 ¤"), text);
        Assert.Equal(0, ((ICivilopediaText)ruleset.Difficulties["Settler"]).GetSortGroup(ruleset));
        Assert.Equal(1, ((ICivilopediaText)difficulty).GetSortGroup(ruleset));
        Assert.Equal(-1, ((ICivilopediaText)new Difficulty { Name = "Nope" }).GetSortGroup(ruleset));
    }

    // endregion

    // region Victory

    [Fact]
    public void VictoryLines()
    {
        var ruleset = new Ruleset();
        var victory = new Victory
        {
            Name = "Scientific",
            VictoryScreenHeader = "Explore the stars",
            Milestones = ["Build [Apollo Program]", "Complete [5] Policy branches", "Destroy all players", "Become the world religion"],
            Uniques = ["[+1 Gold] from every [Temple]"],
        };
        var text = Dump(((ICivilopediaText)victory).GetCivilopediaTextLines(Ctx(ruleset)));
        Assert.StartsWith(
            "Explore the stars\n centered img:VictoryIllustrations/Scientific/Won\n\n"
            + "Build [Apollo Program] ->Wonder/Apollo Program\n"
            + "Complete [5] Policy branches ->Policies\n"
            + "Destroy all players *\n"
            + "Become the world religion ->Tutorials/Religion\n",
            text, StringComparison.Ordinal);
        Assert.EndsWith("[+1 Gold] from every [Temple]", text, StringComparison.Ordinal);
    }

    // endregion

    // region Ruleset.AllICivilopediaText and the validator

    [Fact]
    public void AllICivilopediaTextIsAllRulesetObjects()
    {
        var ruleset = PolicyRuleset();
        Assert.Equal(ruleset.AllRulesetObjects().Cast<object>(), ruleset.AllICivilopediaText().Cast<object>());
    }

    [Fact]
    public void AllICivilopediaTextRealRulesetsHaveNoDuplicateReferencesAndRender()
    {
        RulesetCache.Clear();
        string jsons = ValidatorTestRuleset.JsonsPath();
        Assert.Empty(RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true));
        try
        {
            foreach (var baseRuleset in Enum.GetValues<BaseRuleset>().Where(it => it != BaseRuleset.Civ_V_BNW))
            {
                var ruleset = RulesetCache.Get(baseRuleset.FullName())!;
                var context = Ctx(ruleset);
                var all = ruleset.AllICivilopediaText().ToList();
                Assert.Equal(all.Count, all.Distinct(ReferenceEqualityComparer.Instance).Count());
                foreach (var category in Enum.GetValues<CivilopediaCategories>())
                    foreach (var entry in category.GetCategoryIterator(ruleset, null, context))
                    {
                        var assembled = entry.AssembleCivilopediaText(context);
                        Assert.NotNull(assembled.CivilopediaText);
                        _ = entry.GetSortGroup(ruleset);
                        _ = entry.GetSubCategory(ruleset);
                    }
            }
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    private sealed class ImageCheck(params string[] known) : IFormattedLineImageCheck
    {
        public bool ExtraImageExists(string extraImage) => known.Contains(extraImage);
    }

    [Fact]
    public void ValidatorReportsCivilopediaTextProblems()
    {
        var game = new ValidatorTestRuleset();
        try
        {
            var unit = game.CreateBaseUnit("Melee");
            unit.CivilopediaText = [new FormattedLine("ok"), new FormattedLine("t", header: 99), new FormattedLine(extraImage: "Missing/Image")];
            var validator = RulesetValidator.Create(game.Ruleset);
            validator.ImageCheck = new ImageCheck();
            var errors = validator.GetErrorList();
            var text = $"(BaseUnit) {unit.Name}'s civilopediaText line ";
            var mine = errors.Where(it => it.Text.StartsWith(text, StringComparison.Ordinal)).ToList();
            Assert.Equal(
                [text + "2: header should be in the range 1..8", text + "3: extraImage not found as either atlas texture or in ExtraImages folder"],
                mine.Select(it => it.Text));
            Assert.All(mine, it => Assert.Equal(RulesetErrorSeverity.WarningOptionsOnly, it.ErrorSeverityToReport));

            // The default image check accepts every image, so only the header problem remains
            var defaults = RulesetValidator.Create(game.Ruleset).GetErrorList().Where(it => it.Text.StartsWith(text, StringComparison.Ordinal)).ToList();
            Assert.Equal([text + "2: header should be in the range 1..8"], defaults.Select(it => it.Text));
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    [Fact]
    public void ValidatorWarnsAboutUnitSetArtInCivilopediaText()
    {
        var game = new ValidatorTestRuleset();
        try
        {
            var unit = game.CreateBaseUnit("Melee");
            unit.CivilopediaText = [new FormattedLine(extraImage: $"TileSets/FantasyHex/Units/{unit.Name}"), new FormattedLine(extraImage: $"TileSets/FantasyHex/Units/{unit.Name}/x")];
            var errors = RulesetValidator.Create(game.Ruleset).GetErrorList();
            var message = $"Unit {unit.Name} includes the unit's UnitSet art in civilopediaText, which is superseded by the \"Size of Unitset art in Civilopedia\" option";
            var found = errors.Where(it => it.Text == message).ToList();
            Assert.Single(found);
            Assert.Equal(RulesetErrorSeverity.WarningOptionsOnly, found[0].ErrorSeverityToReport);
        }
        finally
        {
            RulesetCache.Clear();
        }
    }

    // endregion
}
