using Unciv.Json;
using Unciv.Models;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using BuildingObject = Unciv.Models.Ruleset.Building;
using StatsObject = Unciv.Models.StatTypes.Stats;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>
/// The nation and tech objects of T032. The Kotlin fixture CityStateTypeStartBiasTests loads whole rulesets (RulesetCache, T034/T035) and is
/// ported with them; its Nation.getStartBias is not ported yet. These cover deserialization defaults and the logic that does not need a Ruleset.
/// </summary>
public class RulesetNationTechTests
{
    private static T Read<T>(string json) => UncivJson.Deserialize<T>(json)!;

    private sealed class FakeTechView : IRulesetTechView
    {
        public Dictionary<string, Technology> Techs { get; } = [];
        public Dictionary<string, Era> Eras { get; } = [];
        public Technology? GetTechnology(string name) => Techs.GetValueOrDefault(name);
        public Era? GetEra(string? name) => name == null ? null : Eras.GetValueOrDefault(name);
    }


    // region CityStateType

    [Fact]
    public void CityStateTypeReadsBonusUniqueMapsAndColor()
    {
        var type = Read<CityStateType>("""{ "name": "Maritime", "friendBonusUniques": ["Start bias [Coast]"], "allyBonusUniques": [], "color": [10, 20, 30] }""");
        Assert.Equal(UniqueTarget.CityState, type.GetUniqueTarget());
        Assert.True(type.FriendBonusUniqueMap.HasUnique(UniqueType.StartBias));
        Assert.False(type.AllyBonusUniqueMap.HasUnique(UniqueType.StartBias));
        Assert.Equal(10 / 255f, type.GetColor().R);
        Assert.Equal(30 / 255f, type.GetColor().B);
        Assert.Equal([255, 255, 255], new CityStateType().Color);
        Assert.Equal(1f, new CityStateType().GetColor().G);
    }

    [Fact]
    public void CityStateTypeUniquesCanCarryStartBias()
    {
        // Ported subset of CityStateTypeStartBiasTests: the type's uniqueMap has the StartBias unique
        var type = new CityStateType { Name = "TestType", Uniques = ["Start bias [Coast]"] };
        Assert.Contains(type.UniqueMap.GetMatchingUniques(UniqueType.StartBias, GameContext.IgnoreConditionalsState),
            it => it.Params[0] == "Coast");
    }

    // endregion

    // region Personality

    [Fact]
    public void PersonalityDefaultsAndIndexer()
    {
        var personality = new Personality();
        Assert.All(Enum.GetValues<PersonalityValue>(), it => Assert.Equal(5f, personality[it]));
        Assert.Equal(Constants.NeutralVictoryType, personality.PreferredVictoryType);
        Assert.Equal(UniqueTarget.Personality, personality.GetUniqueTarget());

        foreach (var value in Enum.GetValues<PersonalityValue>())
        {
            personality[value] = 3f + (int)value;
            Assert.Equal(3f + (int)value, personality[value]);
        }
        Assert.Equal(3f, personality.Production);
        Assert.Equal(17f, personality.DenounceWillingness);
        Assert.True(Personality.NeutralPersonality.IsNeutralPersonality);
        Assert.Same(Personality.NeutralPersonality, Personality.NeutralPersonality);
    }

    [Fact]
    public void PersonalityFocusScaling()
    {
        var personality = new Personality { Gold = 10f, Science = 0f, Faith = 8f };
        Assert.Equal(2f, personality.ScaledFocus(PersonalityValue.Gold));
        Assert.Equal(0f, personality.ScaledFocus(PersonalityValue.Science));
        Assert.Equal(1f, personality.ScaledFocus(PersonalityValue.Food));
        Assert.Equal(0f, personality.InverseScaledFocus(PersonalityValue.Gold));
        Assert.Equal(2f, personality.InverseScaledFocus(PersonalityValue.Science));
        Assert.Equal(1f + (2f - 1) * 0.5f, personality.ModifierFocus(PersonalityValue.Gold, 0.5f));
        Assert.Equal(1f + (0f - 1) * 0.5f, personality.InverseModifierFocus(PersonalityValue.Gold, 0.5f));
        Assert.Equal(1f + (8f / 5 - 1) * 1f, personality.ModifierFocus(PersonalityValue.Faith, 1f));
    }

    [Fact]
    public void PersonalityScaleStatsMultipliesEachStatByItsValue()
    {
        var personality = new Personality { Gold = 10f, Science = 0f };
        var stats = new StatsObject(production: 10f, food: 10f, gold: 10f, science: 10f, culture: 10f, happiness: 10f, faith: 10f);
        var result = personality.ScaleStats(stats, 1f);
        Assert.Same(stats, result);
        Assert.Equal(10f, result.Production);
        Assert.Equal(20f, result.Gold);
        Assert.Equal(0f, result.Science);
        Assert.Equal(10f, result.Faith);
    }

    [Fact]
    public void PersonalityValueFromStatMapsEveryStatByName()
    {
        foreach (var stat in Enum.GetValues<Stat>())
            Assert.Equal(stat.ToString(), PersonalityValueExtensions.FromStat(stat).ToString());
        Assert.Equal("Greedy", PersonalityValue.Gold.GetDescription());
        Assert.Equal("Denouncer", PersonalityValue.DenounceWillingness.GetDescription());
    }

    [Fact]
    public void PersonalityToStringIsTheShortDescription()
    {
        Assert.Equal(
            "[Productive], [Prolific], [Greedy], [Rational], [Cultured], [Hedonistic], [Religious], [Militaristic], [Pushy], [Warmonger], [Trader], [Negotiator], [Stalwart], [Expansionistic], [Denouncer]",
            new Personality().ToString());
        var p = new Personality { PreferredVictoryType = "Cultural", Culture = 9f, Loyal = 9f };
        Assert.Equal("[Cultural], [Cultured], [Stalwart]", p.ToString());
        Assert.Equal("[Rational]", new Personality { PreferredVictoryType = "", Science = 6f }.ToString());
    }

    [Fact]
    public void PersonalityReadsFromJsonIncludingPrioritiesOrder()
    {
        var p = Read<Personality>("""{ "name": "Gandhi", "gold": 2, "priorities": { "b": 2, "a": 1 }, "preferredVictoryType": "Cultural" }""");
        Assert.Equal(2f, p[PersonalityValue.Gold]);
        Assert.Equal(["b", "a"], p.Priorities.Keys);
        Assert.Equal("Cultural", p.PreferredVictoryType);
    }

    // endregion

    // region Difficulty

    [Fact]
    public void DifficultyDefaultsMatchKotlin()
    {
        var d = new Difficulty();
        Assert.Equal(1f, d.ResearchCostModifier);
        Assert.Equal(5, d.UnitSupplyBase);
        Assert.Equal(2, d.UnitSupplyPerCity);
        Assert.Equal(0f, d.AiUnitSupplyModifier);
        Assert.Equal(25, d.ClearBarbarianCampReward);
        Assert.Equal(1f, d.AiUnhappinessModifier);
        Assert.Null(d.AiDifficultyLevel);
        Assert.Equal(UniqueTarget.Difficulty, d.GetUniqueTarget());
    }

    [Fact]
    public void DifficultyReadsFields()
    {
        var d = Read<Difficulty>("""{ "name": "Prince", "baseHappiness": 9, "extraHappinessPerLuxury": 0.5, "aiFreeTechs": ["Agriculture"], "aiDifficultyLevel": "Chieftain" }""");
        Assert.Equal("Prince", d.Name);
        Assert.Equal(9, d.BaseHappiness);
        Assert.Equal(0.5f, d.ExtraHappinessPerLuxury);
        Assert.Equal(["Agriculture"], d.AiFreeTechs);
        Assert.Equal("Chieftain", d.AiDifficultyLevel);
    }

    // endregion

    // region Nation

    [Fact]
    public void NationKindFlags()
    {
        Assert.True(new Nation { Name = Constants.Barbarians }.IsBarbarian);
        Assert.False(new Nation { Name = Constants.Barbarians }.IsMajorCiv);
        Assert.True(new Nation { Name = Constants.Spectator }.IsSpectator);
        Assert.True(new Nation { Name = "X", CityStateType = "Maritime" }.IsCityState);
        Assert.False(new Nation { Name = "X", CityStateType = "Maritime" }.IsMajorCiv);
        Assert.True(new Nation { Name = "Rome" }.IsMajorCiv);
    }

    [Fact]
    public void NationLeaderDisplayName()
    {
        var rome = new Nation { Name = "Rome", LeaderName = "Caesar" };
        Assert.Equal("[Caesar] of [Rome]", rome.GetLeaderDisplayName());
        Assert.Equal("[[Caesar] the Great] of [Rome]", rome.GetLeaderDisplayName("[] the Great"));
        Assert.Equal("Rome", new Nation { Name = "Rome", CityStateType = "x" }.GetLeaderDisplayName("t"));
        Assert.Equal("Rome", new Nation { Name = "Rome", CityStateType = "x" }.GetLeaderDisplayName());
        Assert.Equal("Spectator", new Nation { Name = Constants.Spectator }.GetLeaderDisplayName());
    }

    [Fact]
    public void NationStyleOrCivName()
    {
        Assert.Equal("Rome", new Nation { Name = "Rome" }.GetStyleOrCivName());
        Assert.Equal("Latin", new Nation { Name = "Rome", Style = "Latin" }.GetStyleOrCivName());
    }

    [Fact]
    public void NationColorsAndTransients()
    {
        var nation = Read<Nation>("""{ "name": "Rome", "outerColor": [255, 0, 0], "uniques": ["Forests and Jungles are Roads", "Units ignore hill movement cost"] }""");
        Assert.Equal(EngineColor.White, nation.GetOuterColor());
        Assert.Equal(EngineColor.Black, nation.GetInnerColor());
        nation.SetTransients();
        Assert.Equal(new EngineColor(1f, 0f, 0f, 1f), nation.GetOuterColor());
        Assert.Equal(0x11 / 255f, nation.GetInnerColor().R);
        Assert.IsType<ImmutableColor>(nation.GetOuterColor());
        Assert.Throws<NotSupportedException>(() => nation.GetOuterColor().Mul(2f));

        var bad = new Nation { Name = "B", OuterColor = [1, 2], InnerColor = [3] };
        bad.SetTransients();
        var purple = new EngineColor(0xa0 / 255f, 0x20 / 255f, 0xf0 / 255f, 1f);
        Assert.Equal(purple, bad.GetOuterColor());
        Assert.Equal(purple, bad.GetInnerColor());
        Assert.False(bad.ForestsAndJunglesAreRoads);
        Assert.False(bad.IgnoreHillMovementCost);
    }

    [Fact]
    public void NationOuterColorIsLateinit()
    {
        Assert.Throws<InvalidOperationException>(() => new Nation().SetTransients());
    }

    [Fact]
    public void NationMatchesFilter()
    {
        var rome = new Nation { Name = "Rome" };
        var cs = new Nation { Name = "Geneva", CityStateType = "Cultured" };
        Assert.True(rome.MatchesFilter("All"));
        Assert.True(rome.MatchesFilter("all"));
        Assert.True(rome.MatchesFilter("Major"));
        Assert.False(cs.MatchesFilter("Major"));
        Assert.True(cs.MatchesFilter("City-States"));
        Assert.True(cs.MatchesFilter("City-State"));
        Assert.True(rome.MatchesFilter("Rome"));
        Assert.False(rome.MatchesFilter("Greece"));
        Assert.True(rome.MatchesFilter("non-[City-State]"));
        Assert.False(rome.MatchesFilter("non-[City-State]", multiFilter: false));
        Assert.True(rome.MatchesFilter("{Major} {Rome}"));
        Assert.False(rome.MatchesFilter("{Major} {Greece}"));
    }

    [Fact]
    public void NationMatchesFilterConsultsTagUniques()
    {
        var rome = new Nation { Name = "Rome", Uniques = ["Hello [Tag]"] };
        // A tag unique named like the filter matches; "Hello [Tag]" is the unique's placeholder text
        Assert.False(rome.MatchesFilter("Tag"));
        Assert.False(rome.MatchesFilter("Tag", new GameContext()));
    }

    [Fact]
    public void NationReadsFields()
    {
        var n = Read<Nation>("""{ "name": "Rome", "leaderName": "Caesar", "cityStateType": null, "startBias": ["Coast"], "innerColor": [1,2,3], "outerColor": [4,5,6], "cities": ["Roma"], "personality": "Aggressive" }""");
        Assert.Equal(Constants.NeutralVictoryType, n.PreferredVictoryType);
        Assert.Equal(["Coast"], n.StartBias);
        Assert.Equal([4, 5, 6], n.OuterColor);
        Assert.Equal([1, 2, 3], n.InnerColor);
        Assert.Equal("Aggressive", n.Personality);
        Assert.Equal(UniqueTarget.Nation, n.GetUniqueTarget());
        Assert.Null(n.FavoredReligion);
    }

    // endregion

    // region Era, TechColumn, Technology

    [Fact]
    public void EraDefaultsAndColor()
    {
        var era = new Era();
        Assert.Equal(-1, era.EraNumber);
        Assert.Equal(300, era.ResearchAgreementCost);
        Assert.Equal("Settler", era.StartingSettlerUnit);
        Assert.Equal("Warrior", era.StartingMilitaryUnit);
        Assert.Equal(200, era.BaseUnitBuyCost);
        Assert.Equal(3, era.EmbarkDefense);
        Assert.Equal("cityClassical", era.CitySound);
        Assert.Equal(UniqueTarget.Era, era.GetUniqueTarget());
        Assert.Equal("#ffffff", era.GetHexColor());
        Assert.NotSame(EngineColor.White, era.GetColor());
        era.GetColor().Mul(0.5f); // a copy: does not throw, does not change White
        Assert.Equal(1f, EngineColor.White.R);

        var red = Read<Era>("""{ "name": "Ancient era", "eraNumber": 0, "iconRGB": [255, 0, 128], "friendBonus": { "Maritime": ["a"] } }""");
        Assert.Equal("#ff0080", red.GetHexColor());
        Assert.Equal(["a"], red.FriendBonus["Maritime"]);
        Assert.Empty(red.AllyBonus);
    }

    [Fact]
    public void TechColumnDefaults()
    {
        var column = new TechColumn();
        Assert.Equal(0, column.ColumnNumber);
        Assert.Equal(-1, column.BuildingCost);
        Assert.Equal(-1, column.WonderCost);
        Assert.Empty(column.Techs);
        Assert.Throws<InvalidOperationException>(() => column.Era);
    }

    [Fact]
    public void TechnologyReadsFieldsAndEraComesFromColumn()
    {
        var tech = Read<Technology>("""{ "name": "Pottery", "cost": 35, "prerequisites": ["Agriculture", "Agriculture", "Fire"], "row": 2, "quote": "q" }""");
        Assert.Equal(35, tech.Cost);
        Assert.Equal(["Agriculture", "Fire"], tech.Prerequisites);
        Assert.Equal(2, tech.Row);
        Assert.Null(tech.Column);
        Assert.Equal(UniqueTarget.Tech, tech.GetUniqueTarget());
        tech.Column = new TechColumn { Era = "Ancient era" };
        Assert.Equal("Ancient era", tech.Era());
    }

    [Fact]
    public void TechnologyEraWithoutColumnThrows()
    {
        Assert.Throws<NullReferenceException>(() => new Technology().Era());
    }

    [Fact]
    public void TechnologyContinuallyResearchable()
    {
        Assert.False(new Technology().IsContinuallyResearchable());
        Assert.True(new Technology { Uniques = ["Can be continually researched"] }.IsContinuallyResearchable());
    }

    [Fact]
    public void TechnologyUniqueIsRequirementForThisTech()
    {
        var tech = new Technology { Name = "Pottery" };
        UniqueObject U(string text) => new(text, UniqueTarget.Building, "x");
        var requirement = U("Only available <after discovering [Pottery]>");
        Assert.True(tech.UniqueIsRequirementForThisTech(requirement));
        Assert.False(tech.UniqueIsNotRequirementForThisTech(requirement));
        Assert.False(tech.UniqueIsRequirementForThisTech(U("Only available <after discovering [Writing]>")));
        Assert.False(tech.UniqueIsRequirementForThisTech(U("Only available <after discovering [Pottery]> <in cities with [x]>")));
        Assert.False(tech.UniqueIsRequirementForThisTech(U("Only available")));
        Assert.False(tech.UniqueIsRequirementForThisTech(U("Unavailable <after discovering [Pottery]>")));
    }

    // endregion

    // region IHasUniques tech helpers

    private static (FakeTechView View, Technology Early, Technology Late) World()
    {
        var view = new FakeTechView();
        var ancient = new Era { Name = "Ancient era", EraNumber = 0 };
        var classical = new Era { Name = "Classical era", EraNumber = 1 };
        view.Eras[ancient.Name] = ancient;
        view.Eras[classical.Name] = classical;
        var early = new Technology { Name = "Pottery", Column = new TechColumn { Era = "Ancient era", ColumnNumber = 1 } };
        var late = new Technology { Name = "Philosophy", Column = new TechColumn { Era = "Classical era", ColumnNumber = 4 } };
        view.Techs[early.Name] = early;
        view.Techs[late.Name] = late;
        return (view, early, late);
    }

    [Fact]
    public void RequiredTechnologiesEraAndColumnTakeTheMaximum()
    {
        var (view, early, late) = World();
        var obj = new BuildingObject { Name = "Obj", Uniques = ["Only available <after discovering [Pottery]>", "Only available <after discovering [Philosophy]>"] };
        IHasUniques self = obj;
        Assert.Equal([early, late], self.RequiredTechnologies(view));
        Assert.Equal("Classical era", self.Era(view)!.Name);
        Assert.Equal(4, self.TechColumn(view)!.ColumnNumber);
        Assert.True(self.AvailableInEra(view, "Classical era"));
        Assert.False(self.AvailableInEra(view, "Ancient era"));
    }

    [Fact]
    public void NoRequiredTechsMeansNoEraNoColumnAndAlwaysAvailable()
    {
        var (view, _, _) = World();
        IHasUniques self = new BuildingObject { Name = "Obj" };
        Assert.Empty(self.RequiredTechnologies(view));
        Assert.Null(self.Era(view));
        Assert.Null(self.TechColumn(view));
        Assert.True(self.AvailableInEra(view, "Ancient era"));
    }

    [Fact]
    public void UnknownRequiredTechIsNullAndIgnored()
    {
        var (view, _, _) = World();
        IHasUniques self = new BuildingObject { Name = "Obj", Uniques = ["Only available <after discovering [Nope]>"] };
        Assert.Single(self.RequiredTechnologies(view));
        Assert.Null(self.Era(view));
        Assert.Null(self.TechColumn(view));
        Assert.True(self.AvailableInEra(view, "Ancient era"));
    }

    [Fact]
    public void EraTieKeepsTheFirstMaximum()
    {
        var view = new FakeTechView();
        view.Eras["A"] = new Era { Name = "A", EraNumber = 2 };
        view.Eras["B"] = new Era { Name = "B", EraNumber = 2 };
        view.Techs["t1"] = new Technology { Name = "t1", Column = new TechColumn { Era = "A", ColumnNumber = 3 } };
        view.Techs["t2"] = new Technology { Name = "t2", Column = new TechColumn { Era = "B", ColumnNumber = 3 } };
        IHasUniques self = new BuildingObject { Name = "Obj", Uniques = ["Only available <after discovering [t1]>", "Only available <after discovering [t2]>"] };
        Assert.Equal("A", self.Era(view)!.Name);
        Assert.Equal("t1", self.TechColumn(view) == view.Techs["t1"].Column ? "t1" : "t2");
    }

    // endregion
}
