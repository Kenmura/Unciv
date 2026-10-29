using Unciv.Models.Ruleset.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

public class UniqueParameterTypeTests
{
    private sealed record StubRulesetObject(string Name, IReadOnlyCollection<string> Uniques)
        : IRulesetObjectParameterView;

    private sealed record StubRulesetUnit(string Name, IReadOnlyCollection<string> Uniques, bool HasGreatPersonUnique = false)
        : IRulesetUnitParameterView;

    private sealed record StubRulesetTerrain(
        string Name,
        IReadOnlyCollection<string> Uniques,
        bool IsBaseTerrain = false,
        bool IsTerrainFeature = false)
        : IRulesetTerrainParameterView;

    private sealed record StubRulesetResource(
        string Name,
        IReadOnlyCollection<string> Uniques,
        bool IsStockpiled = false)
        : IRulesetResourceParameterView;

    private sealed record StubUniqueTargetView(
        bool IsTriggerable,
        UniqueTarget? SourceObjectType,
        IReadOnlyCollection<UniqueTarget>? TargetTypes)
        : IUniqueTargetView;

    private sealed class PopulatedRulesetView : IRulesetParameterView
    {
        public Dictionary<string, IRulesetUnitParameterView> UnitsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> UnitTypesDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> UnitPromotionsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> NationsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> BuildingsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetTerrainParameterView> TerrainsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetResourceParameterView> TileResourcesDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> TileImprovementsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> BeliefsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> TechnologiesDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> PoliciesDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IRulesetObjectParameterView> UnitNameGroupsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> SpecialistsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> GreatWorksDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> ErasDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> SpeedsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> DifficultiesDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> EventsDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> PolicyBranchesDict { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, object> VictoriesDict { get; } = new(StringComparer.Ordinal);
        public List<string> ReligionsList { get; } = [];

        public IRulesetMapView<IRulesetUnitParameterView> Units => new RulesetMapView<IRulesetUnitParameterView>(UnitsDict);
        public IRulesetMapView<IRulesetObjectParameterView> UnitTypes => new RulesetMapView<IRulesetObjectParameterView>(UnitTypesDict);
        public IRulesetMapView<IRulesetObjectParameterView> UnitPromotions => new RulesetMapView<IRulesetObjectParameterView>(UnitPromotionsDict);
        public IRulesetMapView<IRulesetObjectParameterView> Nations => new RulesetMapView<IRulesetObjectParameterView>(NationsDict);
        public IRulesetMapView<IRulesetObjectParameterView> Buildings => new RulesetMapView<IRulesetObjectParameterView>(BuildingsDict);
        public IRulesetMapView<IRulesetTerrainParameterView> Terrains => new RulesetMapView<IRulesetTerrainParameterView>(TerrainsDict);
        public IRulesetMapView<IRulesetResourceParameterView> TileResources => new RulesetMapView<IRulesetResourceParameterView>(TileResourcesDict);
        public IRulesetMapView<IRulesetObjectParameterView> TileImprovements => new RulesetMapView<IRulesetObjectParameterView>(TileImprovementsDict);
        public IRulesetMapView<IRulesetObjectParameterView> Beliefs => new RulesetMapView<IRulesetObjectParameterView>(BeliefsDict);
        public IRulesetMapView<IRulesetObjectParameterView> Technologies => new RulesetMapView<IRulesetObjectParameterView>(TechnologiesDict);
        public IRulesetMapView<IRulesetObjectParameterView> Policies => new RulesetMapView<IRulesetObjectParameterView>(PoliciesDict);
        public IRulesetMapView<IRulesetObjectParameterView> UnitNameGroups => new RulesetMapView<IRulesetObjectParameterView>(UnitNameGroupsDict);
        public IRulesetMapView<object> Specialists => new RulesetMapView<object>(SpecialistsDict);
        public IRulesetMapView<object> GreatWorks => new RulesetMapView<object>(GreatWorksDict);
        public IRulesetMapView<object> Eras => new RulesetMapView<object>(ErasDict);
        public IRulesetMapView<object> Speeds => new RulesetMapView<object>(SpeedsDict);
        public IRulesetMapView<object> Difficulties => new RulesetMapView<object>(DifficultiesDict);
        public IRulesetMapView<object> Events => new RulesetMapView<object>(EventsDict);
        public IRulesetMapView<object> PolicyBranches => new RulesetMapView<object>(PolicyBranchesDict);
        public IRulesetMapView<object> Victories => new RulesetMapView<object>(VictoriesDict);
        public IReadOnlyCollection<string> Religions => ReligionsList;
    }

    // Golden table generated from compiled JVM UniqueTarget.values()
    [Fact]
    public void UniqueTarget_OrdinalsNamesInheritsFromAndModifierType_MatchKotlin()
    {
        var expected = new (int Ordinal, string Name, UniqueTarget? InheritsFrom, UniqueTargetModifierType ModifierType)[]
        {
            (0, "Triggerable", null, UniqueTargetModifierType.None),
            (1, "UnitTriggerable", UniqueTarget.Triggerable, UniqueTargetModifierType.None),
            (2, "Global", UniqueTarget.Triggerable, UniqueTargetModifierType.None),
            (3, "Nation", UniqueTarget.Global, UniqueTargetModifierType.None),
            (4, "Personality", null, UniqueTargetModifierType.None),
            (5, "Era", UniqueTarget.Global, UniqueTargetModifierType.None),
            (6, "Tech", UniqueTarget.Global, UniqueTargetModifierType.None),
            (7, "Policy", UniqueTarget.Global, UniqueTargetModifierType.None),
            (8, "FounderBelief", UniqueTarget.Global, UniqueTargetModifierType.None),
            (9, "FollowerBelief", UniqueTarget.Triggerable, UniqueTargetModifierType.None),
            (10, "Building", UniqueTarget.Global, UniqueTargetModifierType.None),
            (11, "Wonder", UniqueTarget.Building, UniqueTargetModifierType.None),
            (12, "UnitAction", UniqueTarget.UnitTriggerable, UniqueTargetModifierType.None),
            (13, "Unit", UniqueTarget.UnitAction, UniqueTargetModifierType.None),
            (14, "UnitType", UniqueTarget.Unit, UniqueTargetModifierType.None),
            (15, "Promotion", UniqueTarget.Unit, UniqueTargetModifierType.None),
            (16, "Terrain", null, UniqueTargetModifierType.None),
            (17, "Improvement", UniqueTarget.Triggerable, UniqueTargetModifierType.None),
            (18, "Resource", UniqueTarget.Global, UniqueTargetModifierType.None),
            (19, "Ruins", UniqueTarget.UnitTriggerable, UniqueTargetModifierType.None),
            (20, "GlobalUniques", UniqueTarget.Global, UniqueTargetModifierType.None),
            (21, "Speed", UniqueTarget.GlobalUniques, UniqueTargetModifierType.None),
            (22, "Difficulty", UniqueTarget.GlobalUniques, UniqueTargetModifierType.None),
            (23, "Tutorial", null, UniqueTargetModifierType.None),
            (24, "CityState", UniqueTarget.Global, UniqueTargetModifierType.None),
            (25, "ModOptions", null, UniqueTargetModifierType.None),
            (26, "Event", null, UniqueTargetModifierType.None),
            (27, "EventChoice", UniqueTarget.UnitTriggerable, UniqueTargetModifierType.None),
            (28, "Victory", null, UniqueTargetModifierType.None),
            (29, "Conditional", null, UniqueTargetModifierType.Conditional),
            (30, "TriggerCondition", null, UniqueTargetModifierType.Other),
            (31, "UnitTriggerCondition", null, UniqueTargetModifierType.Other),
            (32, "UnitActionModifier", null, UniqueTargetModifierType.Other),
            (33, "MetaModifier", null, UniqueTargetModifierType.Other),
        };

        var actual = Enum.GetValues<UniqueTarget>();
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            var t = actual[i];
            Assert.Equal(expected[i].Ordinal, (int)t);
            Assert.Equal(expected[i].Name, t.ToString());
            Assert.Equal(expected[i].InheritsFrom, t.InheritsFrom());
            Assert.Equal(expected[i].ModifierType, t.ModifierType());
        }

        Assert.Equal(
            [
                UniqueTarget.Building, UniqueTarget.Unit, UniqueTarget.UnitType, UniqueTarget.Improvement,
                UniqueTarget.Tech, UniqueTarget.FollowerBelief, UniqueTarget.FounderBelief, UniqueTarget.Terrain,
                UniqueTarget.Resource, UniqueTarget.Policy, UniqueTarget.Promotion, UniqueTarget.Nation,
                UniqueTarget.Ruins, UniqueTarget.Speed, UniqueTarget.EventChoice, UniqueTarget.Difficulty,
            ],
            UniqueTargetExtensions.Displayable);

        Assert.Equal(
            [
                UniqueTarget.Triggerable, UniqueTarget.Terrain, UniqueTarget.Speed,
                UniqueTarget.ModOptions, UniqueTarget.MetaModifier,
            ],
            UniqueTargetExtensions.CanIncludeSuppression);
    }

    [Fact]
    public void UniqueTarget_CanAcceptUniqueTarget_And_IsAcceptableModifierFor()
    {
        Assert.True(UniqueTarget.Building.CanAcceptUniqueTarget(UniqueTarget.Global));
        Assert.True(UniqueTarget.Wonder.CanAcceptUniqueTarget(UniqueTarget.Building));
        Assert.True(UniqueTarget.Wonder.CanAcceptUniqueTarget(UniqueTarget.Global));
        Assert.True(UniqueTarget.Wonder.CanAcceptUniqueTarget(UniqueTarget.Triggerable));
        Assert.False(UniqueTarget.Global.CanAcceptUniqueTarget(UniqueTarget.Building));
        Assert.True(UniqueTarget.Promotion.CanAcceptUniqueTarget(UniqueTarget.UnitAction));
        Assert.True(UniqueTarget.Promotion.CanAcceptUniqueTarget(UniqueTarget.UnitTriggerable));
        Assert.True(UniqueTarget.Promotion.CanAcceptUniqueTarget(UniqueTarget.Triggerable));
        Assert.False(UniqueTarget.Promotion.CanAcceptUniqueTarget(UniqueTarget.Global));

        var triggerableOnUnit = new StubUniqueTargetView(
            IsTriggerable: true,
            SourceObjectType: UniqueTarget.Promotion,
            TargetTypes: [UniqueTarget.Global]);
        Assert.True(UniqueTarget.TriggerCondition.IsAcceptableModifierFor(triggerableOnUnit));
        Assert.True(UniqueTarget.UnitTriggerCondition.IsAcceptableModifierFor(triggerableOnUnit));
        Assert.False(UniqueTarget.UnitActionModifier.IsAcceptableModifierFor(triggerableOnUnit));

        var untypedUnique = new StubUniqueTargetView(
            IsTriggerable: true,
            SourceObjectType: UniqueTarget.Unit,
            TargetTypes: null);
        Assert.False(UniqueTarget.UnitTriggerCondition.IsAcceptableModifierFor(untypedUnique));
        Assert.False(UniqueTarget.UnitActionModifier.IsAcceptableModifierFor(untypedUnique));

        var unitActionUnique = new StubUniqueTargetView(
            IsTriggerable: false,
            SourceObjectType: UniqueTarget.Building,
            TargetTypes: [UniqueTarget.UnitAction]);
        Assert.False(UniqueTarget.TriggerCondition.IsAcceptableModifierFor(unitActionUnique));
        Assert.False(UniqueTarget.UnitTriggerCondition.IsAcceptableModifierFor(unitActionUnique));
        Assert.True(UniqueTarget.UnitActionModifier.IsAcceptableModifierFor(unitActionUnique));
    }

    [Fact]
    public void UniqueFlag_OrdinalsAndSets_MatchKotlin()
    {
        var flags = Enum.GetValues<UniqueFlag>();
        Assert.Equal(4, flags.Length);
        Assert.Equal((0, "HiddenToUsers"), ((int)flags[0], flags[0].ToString()));
        Assert.Equal((1, "NoConditionals"), ((int)flags[1], flags[1].ToString()));
        Assert.Equal((2, "AcceptsSpeedModifier"), ((int)flags[2], flags[2].ToString()));
        Assert.Equal((3, "AcceptsGameProgressModifier"), ((int)flags[3], flags[3].ToString()));

        Assert.Equal([UniqueFlag.HiddenToUsers], UniqueFlagSets.SetOfHiddenToUsers);
        Assert.Equal([UniqueFlag.NoConditionals], UniqueFlagSets.SetOfNoConditionals);
        Assert.Equal([UniqueFlag.HiddenToUsers, UniqueFlag.NoConditionals], UniqueFlagSets.SetOfHiddenNoConditionals);
    }

    // Golden table generated from compiled JVM UniqueParameterType.values()
    [Fact]
    public void UniqueParameterType_All62Entries_MetadataAndSeverityDefault_MatchKotlin()
    {
        var expected = new (int Ordinal, string Name, string ParamName, string DocExample, string DisplayName, UniqueParameterErrorSeverity SeverityDefault)[]
        {
            (0, "Number", "amount", "3", "amount", UniqueParameterErrorSeverity.RulesetSpecific),
            (1, "PositiveNumber", "positiveAmount", "3", "positiveAmount", UniqueParameterErrorSeverity.RulesetSpecific),
            (2, "NonNegativeNumber", "nonNegativeAmount", "3", "nonNegativeAmount", UniqueParameterErrorSeverity.RulesetSpecific),
            (3, "Fraction", "fraction", "0.5", "fraction", UniqueParameterErrorSeverity.RulesetSpecific),
            (4, "RelativeNumber", "relativeAmount", "+20", "relativeAmount", UniqueParameterErrorSeverity.RulesetSpecific),
            (5, "Countable", "countable", "1000", "countable", UniqueParameterErrorSeverity.RulesetSpecific),
            (6, "KeywordAll", "'all'", "All", "'all'", UniqueParameterErrorSeverity.RulesetInvariant),
            (7, "CombatantFilter", "combatantFilter", "City", "combatantFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (8, "MapUnitFilter", "mapUnitFilter", "Wounded", "Map Unit Filters", UniqueParameterErrorSeverity.RulesetSpecific),
            (9, "BaseUnitFilter", "baseUnitFilter", "Melee", "baseUnitFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (10, "UnitTypeFilter", "unitType", "Water", "Unit Type Filters", UniqueParameterErrorSeverity.RulesetSpecific),
            (11, "UnitName", "unit", "Musketman", "unit", UniqueParameterErrorSeverity.RulesetSpecific),
            (12, "GreatPerson", "greatPerson", "Great General", "greatPerson", UniqueParameterErrorSeverity.RulesetSpecific),
            (13, "Stats", "stats", "+1 Gold, +2 Production", "stats", UniqueParameterErrorSeverity.RulesetInvariant),
            (14, "StatName", "stat", "Culture", "stat", UniqueParameterErrorSeverity.RulesetInvariant),
            (15, "CivWideStatName", "civWideStat", "Gold", "civWideStat", UniqueParameterErrorSeverity.RulesetInvariant),
            (16, "CivFilter", "civFilter", "City-States", "civFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (17, "NationFilter", "nationFilter", "City-States", "nationFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (18, "CityFilter", "cityFilter", "in all cities", "City filters", UniqueParameterErrorSeverity.RulesetSpecific),
            (19, "BuildingName", "buildingName", "Library", "buildingName", UniqueParameterErrorSeverity.RulesetSpecific),
            (20, "BuildingFilter", "buildingFilter", "Culture", "buildingFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (21, "PopulationFilter", "populationFilter", "Followers of this Religion", "Population Filters", UniqueParameterErrorSeverity.RulesetInvariant),
            (22, "TerrainFilter", "terrainFilter", "Fresh Water", "Terrain Filters", UniqueParameterErrorSeverity.RulesetSpecific),
            (23, "TileFilter", "tileFilter", "Farm", "Tile Filters", UniqueParameterErrorSeverity.RulesetSpecific),
            (24, "SimpleTerrain", "simpleTerrain", "Elevated", "simpleTerrain", UniqueParameterErrorSeverity.RulesetSpecific),
            (25, "BaseTerrain", "baseTerrain", "Grassland", "baseTerrain", UniqueParameterErrorSeverity.RulesetSpecific),
            (26, "TerrainFeature", "terrainFeature", "Hill", "terrainFeature", UniqueParameterErrorSeverity.RulesetSpecific),
            (27, "TerrainName", "terrainName", "Forest", "terrainName", UniqueParameterErrorSeverity.RulesetSpecific),
            (28, "RegionType", "regionType", "Hybrid", "Region Types", UniqueParameterErrorSeverity.RulesetSpecific),
            (29, "TerrainQuality", "terrainQuality", "Undesirable", "Terrain Quality", UniqueParameterErrorSeverity.RulesetInvariant),
            (30, "Promotion", "promotion", "Shock I", "promotion", UniqueParameterErrorSeverity.RulesetSpecific),
            (31, "Era", "era", "Ancient era", "era", UniqueParameterErrorSeverity.RulesetSpecific),
            (32, "EraFilter", "eraFilter", "Ancient era", "eraFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (33, "Speed", "speed", "Quick", "speed", UniqueParameterErrorSeverity.RulesetSpecific),
            (34, "Difficulty", "difficulty", "Prince", "difficulty", UniqueParameterErrorSeverity.RulesetSpecific),
            (35, "ImprovementName", "improvementName", "Trading Post", "improvementName", UniqueParameterErrorSeverity.RulesetSpecific),
            (36, "ImprovementFilter", "improvementFilter", "All Road", "Improvement Filters", UniqueParameterErrorSeverity.RulesetSpecific),
            (37, "Resource", "resource", "Iron", "resource", UniqueParameterErrorSeverity.RulesetSpecific),
            (38, "StockpiledResource", "stockpiledResource", "Mana", "stockpiledResource", UniqueParameterErrorSeverity.RulesetSpecific),
            (39, "Stockpile", "stockpile", "Mana", "stockpile", UniqueParameterErrorSeverity.RulesetSpecific),
            (40, "ResourceFilter", "resourceFilter", "Strategic", "resourceFilter", UniqueParameterErrorSeverity.PossibleFilteringUnique),
            (41, "BeliefTypeName", "beliefType", "Follower", "beliefType", UniqueParameterErrorSeverity.RulesetInvariant),
            (42, "SpyAction", "spyAction", "Counter-intelligence", "spyAction", UniqueParameterErrorSeverity.RulesetInvariant),
            (43, "Belief", "belief", "God of War", "belief", UniqueParameterErrorSeverity.RulesetSpecific),
            (44, "ReligionFilter", "religionFilter", "major", "religionFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (45, "FoundingOrEnhancing", "foundingOrEnhancing", "founding", "Prophet Action Filters", UniqueParameterErrorSeverity.RulesetInvariant),
            (46, "Event", "event", "Inspiration", "event", UniqueParameterErrorSeverity.RulesetSpecific),
            (47, "Technology", "tech", "Agriculture", "tech", UniqueParameterErrorSeverity.RulesetSpecific),
            (48, "TechFilter", "techFilter", "Agriculture", "techFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (49, "Specialist", "specialist", "Merchant", "specialist", UniqueParameterErrorSeverity.RulesetSpecific),
            (50, "Policy", "policy", "Oligarchy", "policy", UniqueParameterErrorSeverity.RulesetSpecific),
            (51, "PolicyFilter", "policyFilter", "Oligarchy", "policyFilter", UniqueParameterErrorSeverity.RulesetSpecific),
            (52, "VictoryT", "victoryType", "Domination", "victoryType", UniqueParameterErrorSeverity.RulesetSpecific),
            (53, "UnitNameGroup", "unitNameGroup", "Scientist", "unitNameGroup", UniqueParameterErrorSeverity.RulesetSpecific),
            (54, "CostOrStrength", "costOrStrength", "Cost", "costOrStrength", UniqueParameterErrorSeverity.RulesetInvariant),
            (55, "UnitTriggerTarget", "unitTriggerTarget", "This Unit", "unitTriggerTarget", UniqueParameterErrorSeverity.RulesetSpecific),
            (56, "ModName", "modFilter", "DeCiv Redux", "Mod name filter", UniqueParameterErrorSeverity.RulesetInvariant),
            (57, "ValidationWarning", "validationWarning", UniqueParameterTypeExtensions.SuppressionParameterDocExample, "Mod-check warning", UniqueParameterErrorSeverity.RulesetInvariant),
            (58, "CivilopediaLink", "pediaLink", "Units/Settler", "Unique Specials", UniqueParameterErrorSeverity.RulesetInvariant),
            (59, "Comment", "comment", "comment", "Unique Specials", UniqueParameterErrorSeverity.RulesetSpecific),
            (60, "LeaderTitle", "leaderTitle", "Sovereign [leaderName] the Great", "Leader Title", UniqueParameterErrorSeverity.RulesetSpecific),
            (61, "Unknown", "param", "Unknown", "param", UniqueParameterErrorSeverity.RulesetSpecific),
            (62, "GreatWork", "greatWork", "Parthenon Frieze", "greatWork", UniqueParameterErrorSeverity.RulesetSpecific),
        };

        var actual = UniqueParameterTypeExtensions.Entries;
        Assert.Equal(expected.Length, actual.Count);

        for (int i = 0; i < expected.Length; i++)
        {
            var p = actual[i];
            Assert.Equal(expected[i].Ordinal, (int)p);
            Assert.Equal(expected[i].Name, p.ToString());
            Assert.Equal(expected[i].ParamName, p.ParameterName());
            Assert.Equal(expected[i].DocExample, p.DocExample());
            Assert.Equal(expected[i].DisplayName, p.DisplayName());
            Assert.Equal(expected[i].SeverityDefault, p.SeverityDefault());
            Assert.Equal(p, UniqueParameterTypeExtensions.SafeValueOf(expected[i].ParamName));
        }

        Assert.Null(UniqueParameterTypeExtensions.SafeValueOf("nonExistentParam"));
    }

    // Ported from tests/src/com/unciv/logic/MultiFilterTests.kt (deferred from T020)
    [Fact]
    public void TestParameterTypeSplits()
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        Assert.Null(UniqueParameterType.MapUnitFilter.GetErrorSeverity("{Wounded} {Barbarian}", ruleset));
        Assert.Equal(
            UniqueParameterErrorSeverity.PossibleFilteringUnique,
            UniqueParameterType.MapUnitFilter.GetErrorSeverity("{Wounded} {NONEXISTANTFILTER}", ruleset));
    }

    [Fact]
    public void TestParameterNonFilters()
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        Assert.Null(UniqueParameterType.MapUnitFilter.GetErrorSeverity("non-[Wounded]", ruleset));
        Assert.Null(UniqueParameterType.MapUnitFilter.GetErrorSeverity("{non-[Wounded]} {Barbarian}", ruleset));
    }

    [Fact]
    public void TestParameterTypeSplitsWorkWithMultipleLevels()
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        // Wounded is part of MapUnitFilter, Melee - BaseUnitFilter, and Land - UnitTypeFilter
        Assert.Null(UniqueParameterType.MapUnitFilter.GetErrorSeverity("{Wounded} {Melee} {Land}", ruleset));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("+0", true)]
    [InlineData("-0", true)]
    [InlineData("42", true)]
    [InlineData("+42", true)]
    [InlineData("-42", true)]
    [InlineData(" 42", false)]
    [InlineData("42 ", false)]
    [InlineData("2147483647", true)]
    [InlineData("-2147483648", true)]
    [InlineData("2147483648", false)]
    [InlineData("٠", true)]
    [InlineData("१", true)]
    [InlineData("+", false)]
    [InlineData("-", false)]
    [InlineData("", false)]
    [InlineData("3.0", false)]
    public void Number_MatchesJvmToIntOrNull(string input, bool expectedValid)
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        var expected = expectedValid ? (UniqueParameterErrorSeverity?)null : UniqueParameterErrorSeverity.RulesetInvariant;
        Assert.Equal(expected, UniqueParameterType.Number.GetErrorSeverity(input, ruleset));
        Assert.Equal(expected, UniqueParameterType.RelativeNumber.GetErrorSeverity(input, ruleset));
    }

    [Fact]
    public void PositiveAndNonNegativeNumbers_BoundsMatchKotlin()
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.PositiveNumber.GetErrorSeverity("0", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.PositiveNumber.GetErrorSeverity("-1", ruleset));
        Assert.Null(UniqueParameterType.PositiveNumber.GetErrorSeverity("1", ruleset));
        Assert.Null(UniqueParameterType.PositiveNumber.GetErrorSeverity("+1", ruleset));

        Assert.Null(UniqueParameterType.NonNegativeNumber.GetErrorSeverity("0", ruleset));
        Assert.Null(UniqueParameterType.NonNegativeNumber.GetErrorSeverity("-0", ruleset));
        Assert.Null(UniqueParameterType.NonNegativeNumber.GetErrorSeverity("1", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.NonNegativeNumber.GetErrorSeverity("-1", ruleset));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("0.5", true)]
    [InlineData("-0.25", true)]
    [InlineData("+0.5", true)]
    [InlineData(".5", true)]
    [InlineData("5.", true)]
    [InlineData(" 0.5", true)]
    [InlineData("0.5 ", true)]
    [InlineData("1e-3", true)]
    [InlineData("1E+3", true)]
    [InlineData("0.5f", true)]
    [InlineData("0.5d", true)]
    [InlineData("NaN", true)]
    [InlineData("Infinity", true)]
    [InlineData("-Infinity", true)]
    [InlineData("1,000.5", false)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("+.", false)]
    [InlineData("0x1p3", true)]
    [InlineData("-0x1.8p1", true)]
    [InlineData("0x.8p1", true)]
    [InlineData("0x1.p1", true)]
    [InlineData("0X1P-2", true)]
    [InlineData("0x1p3f", true)]
    [InlineData("0x1", false)]
    [InlineData("0x1p", false)]
    [InlineData("0xp1", false)]
    [InlineData("0x.p1", false)]
    [InlineData("0x1.2.3p1", false)]
    [InlineData("0xgp1", false)]
    public void Fraction_MatchesJvmToFloatOrNull(string input, bool expectedValid)
    {
        var ruleset = EmptyRulesetParameterView.Instance;
        var expected = expectedValid ? (UniqueParameterErrorSeverity?)null : UniqueParameterErrorSeverity.RulesetInvariant;
        Assert.Equal(expected, UniqueParameterType.Fraction.GetErrorSeverity(input, ruleset));
    }

    [Fact]
    public void ToFloatOrNull_HexFloatValues_MatchJava()
    {
        Assert.Equal(8f, UniqueParameterTypeExtensions.ToFloatOrNull("0x1p3"));
        Assert.Equal(3f, UniqueParameterTypeExtensions.ToFloatOrNull("0x1.8p1"));
        Assert.Equal(-0.25f, UniqueParameterTypeExtensions.ToFloatOrNull("-0X1P-2"));
        Assert.Equal(0.25f, UniqueParameterTypeExtensions.ToFloatOrNull("0x.8p-1f"));
    }

    [Fact]
    public void SpecialParameterTypes_MatchJvmBehavior()
    {
        var ruleset = EmptyRulesetParameterView.Instance;

        // Stats / StatName / CivWideStatName
        Assert.Null(UniqueParameterType.Stats.GetErrorSeverity("+1 Gold, +2 Production", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.Stats.GetErrorSeverity("+1 Foo", ruleset));
        Assert.Null(UniqueParameterType.StatName.GetErrorSeverity("Culture", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.StatName.GetErrorSeverity("culture", ruleset));
        Assert.Null(UniqueParameterType.CivWideStatName.GetErrorSeverity("Happiness", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.CivWideStatName.GetErrorSeverity("Food", ruleset));

        // ModName
        Assert.Null(UniqueParameterType.ModName.GetErrorSeverity("Civ V - Gods & Kings", ruleset));
        Assert.Null(UniqueParameterType.ModName.GetErrorSeverity("*Civ V -*", ruleset));
        Assert.Null(UniqueParameterType.ModName.GetErrorSeverity("DeCiv Redux", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.ModName.GetErrorSeverity("Some-Mod-With-Dash", ruleset));

        // ValidationWarning
        Assert.Null(UniqueParameterType.ValidationWarning.GetErrorSeverity(
            "Tinman is supposed to automatically upgrade at tech Clockwork, and therefore Servos for its upgrade Mecha may not yet be researched!",
            ruleset));
        Assert.Null(UniqueParameterType.ValidationWarning.GetErrorSeverity(
            "*is supposed to automatically upgrade*",
            ruleset));
        Assert.Equal(
            UniqueParameterErrorSeverity.RulesetInvariant,
            UniqueParameterType.ValidationWarning.GetErrorSeverity("*unbalanced", ruleset));
        Assert.Equal(
            UniqueParameterErrorSeverity.RulesetInvariant,
            UniqueParameterType.ValidationWarning.GetErrorSeverity("too short", ruleset));
        Assert.Equal(
            UniqueParameterErrorSeverity.RulesetInvariant,
            UniqueParameterType.ValidationWarning.GetErrorSeverity("is deprecated as of 4.0, replace with Something", ruleset));
        Assert.Equal(
            UniqueParameterErrorSeverity.RulesetInvariant,
            UniqueParameterType.ValidationWarning.GetErrorSeverity("not found in Unciv's unique types", ruleset));

        // CivilopediaLink
        Assert.Null(UniqueParameterType.CivilopediaLink.GetErrorSeverity("Units/Settler", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.CivilopediaLink.GetErrorSeverity("Units/", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.CivilopediaLink.GetErrorSeverity("/Settler", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetInvariant, UniqueParameterType.CivilopediaLink.GetErrorSeverity("Units/Settler/Extra", ruleset));

        // Comment & Unknown
        Assert.False(UniqueParameterType.Comment.IsKnownValue("anything", ruleset));
        Assert.Null(UniqueParameterType.Comment.GetErrorSeverity("anything", ruleset));
        Assert.False(UniqueParameterType.Unknown.IsKnownValue("anything", ruleset));
        Assert.Null(UniqueParameterType.Unknown.GetErrorSeverity("anything", ruleset));

        // LeaderTitle
        Assert.Null(UniqueParameterType.LeaderTitle.GetErrorSeverity("Sovereign [leaderName] the Great", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.LeaderTitle.GetErrorSeverity("Sovereign [other] the Great", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.LeaderTitle.GetErrorSeverity("Sovereign", ruleset));
    }

    [Fact]
    public void RulesetDependentParameterTypes_MatchPopulatedRuleset()
    {
        var ruleset = new PopulatedRulesetView();
        ruleset.UnitsDict["Musketman"] = new StubRulesetUnit("Musketman", ["GunpowderUnit"]);
        ruleset.UnitsDict["Great General"] = new StubRulesetUnit("Great General", ["Great Person - [War]"], HasGreatPersonUnique: true);
        ruleset.UnitTypesDict["Mounted"] = new StubRulesetObject("Mounted", ["CavalryTag"]);
        ruleset.UnitPromotionsDict["Shock I"] = new StubRulesetObject("Shock I", ["MeleePromo [amount]%"]);
        ruleset.NationsDict["Rome"] = new StubRulesetObject("Rome", ["ClassicalCiv"]);
        ruleset.BuildingsDict["Library"] = new StubRulesetObject("Library", ["ScienceBuilding"]);
        ruleset.TerrainsDict["Grassland"] = new StubRulesetTerrain("Grassland", [], IsBaseTerrain: true);
        ruleset.TerrainsDict["Hill"] = new StubRulesetTerrain(
            "Hill",
            ["A Region is formed with at least [30]% [Hill] tiles, with priority [1]"],
            IsTerrainFeature: true);
        ruleset.TileResourcesDict["Iron"] = new StubRulesetResource("Iron", ["MetalResource"], IsStockpiled: false);
        ruleset.TileResourcesDict["Mana"] = new StubRulesetResource("Mana", [], IsStockpiled: true);
        ruleset.TileImprovementsDict["Trading Post"] = new StubRulesetObject("Trading Post", ["CommerceImprovement"]);
        ruleset.TileImprovementsDict["Cancel improvement order"] = new StubRulesetObject("Cancel improvement order", []);
        ruleset.BeliefsDict["God of War"] = new StubRulesetObject("God of War", ["WarBelief"]);
        ruleset.TechnologiesDict["Agriculture"] = new StubRulesetObject("Agriculture", ["StarterTech"]);
        ruleset.PoliciesDict["Oligarchy"] = new StubRulesetObject("Oligarchy", ["TraditionPolicy"]);
        ruleset.PolicyBranchesDict["Tradition"] = new object();
        ruleset.ErasDict["Ancient era"] = new object();
        ruleset.SpeedsDict["Quick"] = new object();
        ruleset.DifficultiesDict["Prince"] = new object();
        ruleset.SpecialistsDict["Merchant"] = new object();
        ruleset.EventsDict["Inspiration"] = new object();
        ruleset.VictoriesDict["Domination"] = new object();
        ruleset.UnitNameGroupsDict["Scientist"] = new StubRulesetObject("Scientist", ["ScienceNames <when in war>"]);
        ruleset.ReligionsList.Add("Christianity");

        Assert.Null(UniqueParameterType.UnitName.GetErrorSeverity("Musketman", ruleset));
        Assert.Null(UniqueParameterType.BaseUnitFilter.GetErrorSeverity("GunpowderUnit", ruleset));
        Assert.Null(UniqueParameterType.GreatPerson.GetErrorSeverity("Great General", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.GreatPerson.GetErrorSeverity("Musketman", ruleset));

        Assert.Null(UniqueParameterType.UnitTypeFilter.GetErrorSeverity("Mounted", ruleset));
        Assert.Null(UniqueParameterType.UnitTypeFilter.GetErrorSeverity("CavalryTag", ruleset));

        Assert.Null(UniqueParameterType.MapUnitFilter.GetErrorSeverity("Shock I", ruleset));
        Assert.Null(UniqueParameterType.MapUnitFilter.GetErrorSeverity("MeleePromo []%", ruleset));

        Assert.Null(UniqueParameterType.NationFilter.GetErrorSeverity("Rome", ruleset));
        Assert.Null(UniqueParameterType.NationFilter.GetErrorSeverity("ClassicalCiv", ruleset));
        Assert.Null(UniqueParameterType.CivFilter.GetErrorSeverity("Rome", ruleset));

        Assert.Null(UniqueParameterType.BuildingName.GetErrorSeverity("Library", ruleset));
        Assert.Null(UniqueParameterType.BuildingFilter.GetErrorSeverity("ScienceBuilding", ruleset));
        Assert.Null(UniqueParameterType.BuildingFilter.GetErrorSeverity("Agriculture", ruleset));

        Assert.Null(UniqueParameterType.PopulationFilter.GetErrorSeverity("Merchant", ruleset));

        Assert.Null(UniqueParameterType.BaseTerrain.GetErrorSeverity("Grassland", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.BaseTerrain.GetErrorSeverity("Hill", ruleset));
        Assert.Null(UniqueParameterType.TerrainFeature.GetErrorSeverity("Hill", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.TerrainFeature.GetErrorSeverity("Grassland", ruleset));
        Assert.Null(UniqueParameterType.RegionType.GetErrorSeverity("Hill", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.RegionType.GetErrorSeverity("Grassland", ruleset));

        Assert.Null(UniqueParameterType.EraFilter.GetErrorSeverity("pre-[Ancient era]", ruleset));
        Assert.Null(UniqueParameterType.EraFilter.GetErrorSeverity("post-[Ancient era]", ruleset));
        Assert.Null(UniqueParameterType.TechFilter.GetErrorSeverity("pre-[Ancient era]", ruleset));
        Assert.Null(UniqueParameterType.TechFilter.GetErrorSeverity("StarterTech", ruleset));

        Assert.Null(UniqueParameterType.ImprovementName.GetErrorSeverity("Trading Post", ruleset));
        Assert.Equal(
            UniqueParameterErrorSeverity.RulesetInvariant,
            UniqueParameterType.ImprovementName.GetErrorSeverity("Cancel improvement order", ruleset));
        Assert.Null(UniqueParameterType.ImprovementFilter.GetErrorSeverity("CommerceImprovement", ruleset));

        Assert.Null(UniqueParameterType.Resource.GetErrorSeverity("Iron", ruleset));
        Assert.Equal(UniqueParameterErrorSeverity.RulesetSpecific, UniqueParameterType.StockpiledResource.GetErrorSeverity("Iron", ruleset));
        Assert.Null(UniqueParameterType.StockpiledResource.GetErrorSeverity("Mana", ruleset));
        Assert.Null(UniqueParameterType.Stockpile.GetErrorSeverity("Mana", ruleset));
        Assert.Null(UniqueParameterType.Stockpile.GetErrorSeverity("Stored Food", ruleset));
        Assert.Null(UniqueParameterType.Stockpile.GetErrorSeverity("Golden Age points", ruleset));

        Assert.Null(UniqueParameterType.ReligionFilter.GetErrorSeverity("Christianity", ruleset));
        Assert.Null(UniqueParameterType.ReligionFilter.GetErrorSeverity("God of War", ruleset));
        Assert.Null(UniqueParameterType.ReligionFilter.GetErrorSeverity("WarBelief", ruleset));

        Assert.Null(UniqueParameterType.PolicyFilter.GetErrorSeverity("[Tradition] branch", ruleset));
        Assert.Null(UniqueParameterType.PolicyFilter.GetErrorSeverity("TraditionPolicy", ruleset));

        Assert.Null(UniqueParameterType.UnitNameGroup.GetErrorSeverity("Scientist", ruleset));
        Assert.Null(UniqueParameterType.UnitNameGroup.GetErrorSeverity("ScienceNames", ruleset));
    }
}
