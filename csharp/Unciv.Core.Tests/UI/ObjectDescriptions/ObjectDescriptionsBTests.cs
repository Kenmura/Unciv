using Unciv.Core.Tests.Logic;
using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using BuildingObject = Unciv.Models.Ruleset.Building;
using CityObject = Unciv.Logic.City.City;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.UI.ObjectDescriptions;

/// <summary>
/// T096: Unit tests for BuildingDescriptions, ImprovementDescriptions, ResourceDescriptions,
/// TileDescription, and deferred ICivilopediaText implementations on Building, TileImprovement, TileResource, Terrain.
/// </summary>
public class ObjectDescriptionsBTests
{
    private static CivilopediaContext Ctx(Ruleset ruleset) => new(ruleset);

    private static Ruleset MakeRuleset()
    {
        var ruleset = new Ruleset();

        // Eras
        ruleset.Eras["Classical era"] = new Era { Name = "Classical era", EraNumber = 2 };

        // Techs
        ruleset.Technologies["Mining"] = new Technology { Name = "Mining", Cost = 50, Column = new TechColumn { Era = "Classical era" } };
        ruleset.Technologies["Masonry"] = new Technology { Name = "Masonry", Cost = 80, Prerequisites = ["Mining"], Column = new TechColumn { Era = "Classical era" } };
        ruleset.Technologies["Engineering"] = new Technology { Name = "Engineering", Cost = 150, Column = new TechColumn { Era = "Classical era" } };

        // Terrains
        ruleset.Terrains["Grassland"] = new Terrain { Name = "Grassland", Type = TerrainType.Land, Food = 2f };
        ruleset.Terrains["Plains"] = new Terrain { Name = "Plains", Type = TerrainType.Land, Food = 1f, Production = 1f };
        ruleset.Terrains["Hills"] = new Terrain { Name = "Hills", Type = TerrainType.Land, Production = 2f, DefenceBonus = 0.25f };
        ruleset.Terrains["Forest"] = new Terrain { Name = "Forest", Type = TerrainType.TerrainFeature, Food = 1f, Production = 1f, OccursOn = ["Grassland", "Plains"] };
        ruleset.Terrains["Old Faithful"] = new Terrain { Name = "Old Faithful", Type = TerrainType.NaturalWonder, Science = 3f, Happiness = 2f };

        // Resources
        ruleset.TileResources["Iron"] = new TileResource
        {
            Name = "Iron",
            ResourceType = ResourceType.Strategic,
            RevealedBy = "Mining",
            ImprovedBy = ["Mine"],
            TerrainsCanBeFoundOn = ["Hills", "Grassland"],
            Production = 1f,
        };
        ruleset.TileResources["Gold Ore"] = new TileResource
        {
            Name = "Gold Ore",
            ResourceType = ResourceType.Luxury,
            RevealedBy = "Mining",
            ImprovedBy = ["Mine"],
            TerrainsCanBeFoundOn = ["Hills"],
            Gold = 2f,
        };
        ruleset.TileResources["Wheat"] = new TileResource
        {
            Name = "Wheat",
            ResourceType = ResourceType.Bonus,
            ImprovedBy = ["Farm"],
            TerrainsCanBeFoundOn = ["Plains"],
            Food = 1f,
        };

        // Improvements
        ruleset.TileImprovements["Mine"] = new TileImprovement
        {
            Name = "Mine",
            Production = 1f,
            TechRequired = "Mining",
            TerrainsCanBeBuiltOn = ["Hills"],
            TurnsToBuild = 6,
            Uniques = ["[+1 Production] with [Engineering]"],
        };
        ruleset.TileImprovements["Farm"] = new TileImprovement
        {
            Name = "Farm",
            Food = 1f,
            TerrainsCanBeBuiltOn = ["Grassland", "Plains"],
            TurnsToBuild = 5,
        };
        ruleset.TileImprovements["Ancient Ruins"] = new TileImprovement
        {
            Name = "Ancient Ruins",
            Uniques = ["Provides a random bonus when entered"],
        };

        // Units
        ruleset.Units["Worker"] = new BaseUnit
        {
            Name = "Worker",
            UnitType = "Civilian",
            Cost = 60,
            Uniques = ["Can build [Land] improvements on tiles"],
        };
        ruleset.Units["Swordsman"] = new BaseUnit
        {
            Name = "Swordsman",
            Cost = 75,
            RequiredResource = "Iron",
        };

        // Buildings
        ruleset.Buildings["Monument"] = new BuildingObject
        {
            Name = "Monument",
            Cost = 40,
            Culture = 2f,
            Maintenance = 1,
        };
        ruleset.Buildings["Amphitheater"] = new BuildingObject
        {
            Name = "Amphitheater",
            Cost = 100,
            Culture = 3f,
            Maintenance = 2,
            RequiredBuilding = "Monument",
            RequiredTech = "Masonry",
        };
        ruleset.Buildings["Pyramids"] = new BuildingObject
        {
            Name = "Pyramids",
            Cost = 185,
            IsWonder = true,
            RequiredTech = "Masonry",
            Uniques = ["Can build [all] improvements at a [+25]% rate"],
        };
        ruleset.Buildings["National Monument"] = new BuildingObject
        {
            Name = "National Monument",
            Cost = 125,
            IsNationalWonder = true,
            RequiredBuilding = "Monument",
            Culture = 5f,
        };
        ruleset.Buildings["Forge"] = new BuildingObject
        {
            Name = "Forge",
            Cost = 120,
            Maintenance = 1,
            RequiredNearbyImprovedResources = ["Iron"],
            Uniques = ["[+1 Production] from [Iron] tiles"],
        };

        // Ruin rewards
        ruleset.RuinRewards["Free Tech"] = new RuinReward
        {
            Name = "Free Tech",
            Weight = 10,
            Uniques = ["Free technology"],
        };

        foreach (var resource in ruleset.TileResources.Values) resource.SetTransients(ruleset);
        foreach (var terrain in ruleset.Terrains.Values) terrain.SetTransients();

        return ruleset;
    }

    private static (CityObject City, Civilization Civ, Ruleset Ruleset) CreateTestCity()
    {
        var ruleset = MakeRuleset();
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(1) { GameInfo = info };
        map.TileList.Add(new TileClass { Position = HexCoord.Of(0, 0), BaseTerrain = "Grassland" });
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = new Civilization(new NationObject { Name = "Rome" })
        {
            CivId = "Rome",
            GameInfo = info,
            CityStateFunctions = new FakeCityStateFunctions(),
        };
        civ.State = new GameContext(civ);
        civ.Tech.CivInfo = civ;
        civ.CivConstructions.SetTransients(civ);
        civ.Policies.CivInfo = civ;
        info.Civs.Add(civ);
        var city = new FakeCityState("c1", new FakeCiv("Rome"))
        {
            CivData = civ,
            LocationData = HexCoord.Of(0, 0),
        }.City;
        city.Name = "Rome";
        city.SetTransients(civ);
        return (city, civ, ruleset);
    }

    private sealed class FakeTileViewDouble : ITileView
    {
        public ICivView? CivView { get; set; }
        public bool CityCenter { get; set; }
        public ICityView? City { get; set; }
        public string BaseTerrain { get; set; } = "Plains";
        public IEnumerable<string> TerrainFeatures { get; set; } = [];
        public TileResource? ViewableResource { get; set; }
        public string? Resource { get; set; }
        public int ResourceAmount { get; set; }
        public string? NaturalWonder { get; set; }
        public RoadStatus RoadStatus { get; set; } = RoadStatus.None;
        public bool RoadIsPillaged { get; set; }
        public string? ShownImprovement { get; set; }
        public bool ImprovementIsPillaged { get; set; }
        public string? ImprovementInProgress { get; set; }
        public int TurnsToImprovement { get; set; }
        public IMapUnitView? CivilianUnit { get; set; }
        public IMapUnitView? MilitaryUnit { get; set; }
        public float DefensiveBonus { get; set; }
        public bool Impassible { get; set; }
        public bool Land { get; set; } = true;
        public HashSet<string> AdjacentFilters { get; set; } = [];
        public Ruleset Ruleset { get; set; } = new();
        public bool IsVisibleMap { get; set; } = true;

        public ICivView? GetCivView() => CivView;
        public bool IsCityCenter() => CityCenter;
        public ICityView? OwningCity() => City;
        public TileResource? GetViewableResource(ICivView? viewingCiv) => ViewableResource;
        public string? GetShownImprovement() => ShownImprovement;
        public float GetDefensiveBonus() => DefensiveBonus;
        public bool IsImpassible() => Impassible;
        public bool IsLand => Land;
        public bool IsAdjacentTo(string terrainFilter) => AdjacentFilters.Contains(terrainFilter);
        public Ruleset GetRuleset() => Ruleset;
    }

    private sealed class FakeCivViewDouble : ICivView
    {
        public HashSet<string> ResearchedTechs { get; } = [];
        public Ruleset Ruleset { get; set; } = new();

        public bool CanSeeTile(ITileView tileView) => true;
        public bool CanBuildImprovementOn(TileImprovement improvement, ITileView tileView) =>
            improvement.TechRequired == null || ResearchedTechs.Contains(improvement.TechRequired);
        public IEnumerable<ImprovementBuildingProblem> GetImprovementBuildingProblems(TileImprovement improvement, ITileView tileView) =>
            improvement.TechRequired != null && !ResearchedTechs.Contains(improvement.TechRequired)
                ? [ImprovementBuildingProblem.MissingTech]
                : [];
        public Technology? TechnologyByName(string? name) => name != null && Ruleset.Technologies.TryGetValue(name, out var tech) ? tech : null;
        public bool IsResearched(string techName) => ResearchedTechs.Contains(techName);
    }

    private sealed class FakeCityViewDouble : ICityView
    {
        public string Name { get; set; } = "Rome";
        public int Health { get; set; } = 200;
        public bool OwnedByViewer { get; set; } = true;
        public FormattedLine ProductionMarkup { get; set; } = new("Producing [Warrior]");

        public int GetHealth() => Health;
        public bool IsOwnedByViewer() => OwnedByViewer;
        public FormattedLine GetProductionMarkup() => ProductionMarkup;
    }

    private sealed class FakeMapUnitViewDouble : IMapUnitView
    {
        public string Name { get; set; } = "Warrior";
        public string CivName { get; set; } = "Rome";
        public int UnitHealth { get; set; } = 100;
    }

    // region BuildingDescriptions Tests

    [Fact]
    public void BuildingDescriptions_GetShortDescription_SingleLineAndMultiline()
    {
        var building = new BuildingObject
        {
            Name = "Castle",
            CityStrength = 5.0,
            CityHealth = 50,
            Uniques = ["[+10]% [Production]"],
        };

        var shortSingle = BuildingDescriptions.GetShortDescription(building, multiline: false);
        Assert.Contains("+10% ⚙Production", shortSingle);
        Assert.Contains("City strength +5", shortSingle);
        Assert.Contains("City health +50", shortSingle);
        Assert.Contains("; ", shortSingle);

        var shortMulti = BuildingDescriptions.GetShortDescription(building, multiline: true);
        Assert.Contains("\n", shortMulti);
    }

    [Fact]
    public void BuildingDescriptions_GetShortDescription_WithRequiredResourcesAndReplacementText()
    {
        var building = new BuildingObject
        {
            Name = "Forge",
            RequiredNearbyImprovedResources = ["Iron"],
            ReplacementTextForUniques = "Custom ability text",
            Uniques = ["[+10]% Strength"],
        };

        var desc = BuildingDescriptions.GetShortDescription(building);
        Assert.Contains("Requires improved Iron near city", desc);
        Assert.Contains("Custom ability text", desc);
        Assert.DoesNotContain("[+10]% Strength", desc);
    }

    [Fact]
    public void BuildingDescriptions_GetShortDescription_WithUniqueInclusionFilter()
    {
        var building = new BuildingObject
        {
            Name = "Temple",
            Uniques = ["Provides [1] [Happiness]", "Hidden ability"],
        };

        var desc = BuildingDescriptions.GetShortDescription(building, uniqueInclusionFilter: u => !u.Text.Contains("Hidden", StringComparison.Ordinal));
        Assert.Contains("Provides 1", desc);
        Assert.DoesNotContain("Hidden ability", desc);
    }

    [Fact]
    public void BuildingDescriptions_GetDescription_WithCity()
    {
        var (city, _, _) = CreateTestCity();
        var building = new BuildingObject
        {
            Name = "Colosseum",
            Happiness = 2f,
            Maintenance = 1,
            Uniques = ["Unique Colosseum effect"],
        };

        var desc = BuildingDescriptions.GetDescription(building, city, showAdditionalInfo: true);
        Assert.Contains("+2 ⌣Happiness", desc);
        Assert.Contains("Maintenance cost: 1", desc);
        Assert.Contains("Unique Colosseum effect", desc);
    }

    [Fact]
    public void BuildingDescriptions_AddBuildingDifferences_ComparesStatsAndUniques()
    {
        var b1 = new BuildingObject
        {
            Name = "Small Barracks",
            Cost = 50,
            Culture = 1f,
            Uniques = ["Unique ability A"],
        };
        var b2 = new BuildingObject
        {
            Name = "Big Barracks",
            Cost = 100,
            Culture = 3f,
            Production = 2f,
            Uniques = ["Unique ability B"],
        };

        var context = Ctx(new Ruleset());
        var lines = FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            b.AddBuildingDifferences(b1, b2);
        });

        // Culture increased (3 vs 1), Production increased (2 vs 0), ability A was lost
        var text = string.Join("\n", lines.Select(l => l.Text));
        Assert.Contains("3 vs 1", text);
        Assert.Contains("2 vs 0", text);
        Assert.Contains("Lost ability (vs [Small Barracks]): [Unique ability A]", text);
    }

    [Fact]
    public void BuildingDescriptions_GetBuildingCivilopediaTextLines_BasicBuilding()
    {
        var ruleset = MakeRuleset();
        var building = ruleset.Buildings["Amphitheater"];
        var context = Ctx(ruleset);

        var lines = BuildingDescriptions.GetBuildingCivilopediaTextLines(building, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("{Cost}: 100", text);
        Assert.Contains("Required tech: [Masonry]", text);
        Assert.Contains("Requires [Monument] to be built in the city", text);
        Assert.Contains("+3 ♪Culture", text);
    }

    [Fact]
    public void BuildingDescriptions_GetBuildingCivilopediaTextLines_Wonder()
    {
        var ruleset = MakeRuleset();
        var pyramids = ruleset.Buildings["Pyramids"];
        var context = Ctx(ruleset);

        var lines = BuildingDescriptions.GetBuildingCivilopediaTextLines(pyramids, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Wonder", text);
        Assert.Contains("{Cost}: 185", text);
        Assert.Contains("Required tech: [Masonry]", text);
        Assert.Contains("Can build [all] improvements at a [+25]% rate", text);
    }

    [Fact]
    public void BuildingDescriptions_GetBuildingCivilopediaTextLines_NationalWonder()
    {
        var ruleset = MakeRuleset();
        var nationalMonument = ruleset.Buildings["National Monument"];
        var context = Ctx(ruleset);

        var lines = BuildingDescriptions.GetBuildingCivilopediaTextLines(nationalMonument, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("National Wonder", text);
        Assert.Contains("{Cost}: 125", text);
        Assert.Contains("+5 ♪Culture", text);
    }

    [Fact]
    public void Building_ICivilopediaText_Implementation()
    {
        var ruleset = MakeRuleset();
        var regular = ruleset.Buildings["Monument"];
        var amphitheater = ruleset.Buildings["Amphitheater"];
        var wonder = ruleset.Buildings["Pyramids"];

        Assert.Equal("Other", ((ICivilopediaText)regular).GetSubCategory(ruleset));
        Assert.Equal("Classical era", ((ICivilopediaText)amphitheater).GetSubCategory(ruleset));

        Assert.Equal(100, ((ICivilopediaText)regular).GetSortGroup(ruleset));
        Assert.Equal(2, ((ICivilopediaText)amphitheater).GetSortGroup(ruleset));
        Assert.Equal("Building/Monument", regular.MakeLink());
        Assert.Equal("Wonder/Pyramids", wonder.MakeLink());

        var lines = ((ICivilopediaText)regular).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.NotEmpty(lines);
        Assert.Equal("Monument", ((ICivilopediaText)regular).GetCivilopediaTextHeader()?.Text);
    }

    // endregion

    // region ImprovementDescriptions Tests

    [Fact]
    public void ImprovementDescriptions_GetDifferences_ComparesStatsAndUniques()
    {
        var ruleset = MakeRuleset();
        var imp1 = new TileImprovement
        {
            Name = "Basic Mine",
            Production = 1f,
            Uniques = ["Old ability"],
        };
        var imp2 = new TileImprovement
        {
            Name = "Advanced Mine",
            Production = 3f,
            Gold = 1f,
            Uniques = ["New ability"],
        };

        var diffs = ImprovementDescriptions.GetDifferences(ruleset, imp1, imp2).Select(l => l.Text).ToList();
        Assert.Contains("⚙Production 3 vs 1", diffs);
        Assert.Contains("¤Gold 1 vs 0", diffs);
        Assert.Contains("New ability", diffs);
        Assert.Contains("Lost ability (vs [Basic Mine]): [Old ability]", diffs);
    }

    [Fact]
    public void ImprovementDescriptions_GetCivilopediaTextLines_StandardImprovement()
    {
        var ruleset = MakeRuleset();
        var mine = ruleset.TileImprovements["Mine"];
        var context = Ctx(ruleset);

        var lines = ImprovementDescriptions.GetCivilopediaTextLines(mine, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("{Can be built on} {Hills}", text);
        Assert.Contains("Required tech: [Mining]", text);
        Assert.Contains("{Can be constructed by} {Worker}", text);
        Assert.Contains("+1 ⚙Production", text);
        Assert.Contains("Engineering", text);
    }

    [Fact]
    public void ImprovementDescriptions_GetCivilopediaTextLines_AncientRuins()
    {
        var ruleset = MakeRuleset();
        var ruins = ruleset.TileImprovements["Ancient Ruins"];
        var context = Ctx(ruleset);

        var lines = ImprovementDescriptions.GetCivilopediaTextLines(ruins, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("The possible rewards are:", text);
        Assert.Contains("Free Tech", text);
    }

    [Fact]
    public void ImprovementDescriptions_GetDescription_And_GetShortDescription()
    {
        var ruleset = MakeRuleset();
        var mine = ruleset.TileImprovements["Mine"];

        var desc = ImprovementDescriptions.GetDescription(mine, ruleset);
        Assert.Contains("+1 ⚙Production", desc);
        Assert.Contains("Can be built on Hills", desc);
        Assert.Contains("Required tech: Mining", desc);

        var shortDesc = ImprovementDescriptions.GetShortDescription(mine).Select(l => l.Text).ToList();
        Assert.Contains("{Can be built on} {Hills}", shortDesc);
    }

    [Fact]
    public void TileImprovement_ICivilopediaText_Implementation()
    {
        var ruleset = MakeRuleset();
        var mine = ruleset.TileImprovements["Mine"];

        Assert.Equal("Tile Improvements", ((ICivilopediaText)mine).GetSubCategory(ruleset));
        Assert.Equal("Improvement/Mine", mine.MakeLink());
        Assert.Equal(0, ((ICivilopediaText)mine).GetSortGroup(ruleset));

        var lines = ((ICivilopediaText)mine).GetCivilopediaTextLines(Ctx(ruleset));
        Assert.NotEmpty(lines);
        Assert.Equal("Mine", ((ICivilopediaText)mine).GetCivilopediaTextHeader()?.Text);

        var constructors = mine.GetConstructorUnits(ruleset).Select(u => u.Name).ToList();
        Assert.Contains("Worker", constructors);
    }

    // endregion

    // region ResourceDescriptions Tests

    [Fact]
    public void ResourceDescriptions_GetCivilopediaTextLines_StrategicResource()
    {
        var ruleset = MakeRuleset();
        var iron = ruleset.TileResources["Iron"];
        var context = Ctx(ruleset);

        var lines = ResourceDescriptions.GetCivilopediaTextLines(iron, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Strategic resource", text);
        Assert.Contains("Revealed by:", text);
        Assert.Contains("Mining", text);
        Assert.Contains("{Can be found on}:", text);
        Assert.Contains("Hills", text);
        Assert.Contains("Grassland", text);
        Assert.Contains("Improved by [Mine]", text);
        Assert.Contains("{Units that consume this resource}:", text);
        Assert.Contains("Swordsman", text);
    }

    [Fact]
    public void ResourceDescriptions_GetCivilopediaTextLines_LuxuryResource()
    {
        var ruleset = MakeRuleset();
        var goldOre = ruleset.TileResources["Gold Ore"];
        var context = Ctx(ruleset);

        var lines = ResourceDescriptions.GetCivilopediaTextLines(goldOre, context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Luxury resource", text);
        Assert.Contains("Revealed by:", text);
        Assert.Contains("Mining", text);
        Assert.Contains("{Can be found on} {Hills}", text);
        Assert.Contains("Improved by [Mine]", text);
    }

    [Fact]
    public void TileResource_ICivilopediaText_Implementation()
    {
        var ruleset = MakeRuleset();
        var iron = ruleset.TileResources["Iron"];
        var goldOre = ruleset.TileResources["Gold Ore"];
        var wheat = ruleset.TileResources["Wheat"];

        Assert.Equal("Strategic resource", ((ICivilopediaText)iron).GetSubCategory(ruleset));
        Assert.Equal("Luxury resource", ((ICivilopediaText)goldOre).GetSubCategory(ruleset));
        Assert.Equal("Bonus resource", ((ICivilopediaText)wheat).GetSubCategory(ruleset));

        Assert.Equal(1, ((ICivilopediaText)iron).GetSortGroup(ruleset));
        Assert.Equal(0, ((ICivilopediaText)goldOre).GetSortGroup(ruleset));
        Assert.Equal(2, ((ICivilopediaText)wheat).GetSortGroup(ruleset));

        Assert.Equal("Resource/Iron", iron.MakeLink());
        Assert.Equal("Iron", ((ICivilopediaText)iron).GetCivilopediaTextHeader()?.Text);
    }

    // endregion

    // region Terrain Tests

    [Fact]
    public void Terrain_DisplayAs_ReturnsTrueWhenMatching()
    {
        var ruleset = MakeRuleset();
        var plains = ruleset.Terrains["Plains"];
        var forest = ruleset.Terrains["Forest"];

        Assert.True(plains.DisplayAs(TerrainType.Land, ruleset));
        Assert.False(plains.DisplayAs(TerrainType.Water, ruleset));
        Assert.True(forest.DisplayAs(TerrainType.TerrainFeature, ruleset));
        Assert.True(forest.DisplayAs(TerrainType.Land, ruleset)); // Forest occurs on Grassland/Plains which are Land
    }

    [Fact]
    public void Terrain_GetCivilopediaTextLines_BaseTerrain()
    {
        var ruleset = MakeRuleset();
        var hills = ruleset.Terrains["Hills"];
        var context = Ctx(ruleset);

        var lines = hills.GetCivilopediaTextLines(context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("+2 ⚙Production", text);
        Assert.Contains("{Defence bonus}: 25%", text);
        Assert.Contains("May contain:", text);
        Assert.Contains("Iron", text);
        Assert.Contains("Gold Ore", text);
        Assert.Contains("{Tile Improvements}:", text);
        Assert.Contains("Mine", text);
    }

    [Fact]
    public void Terrain_GetCivilopediaTextLines_NaturalWonder()
    {
        var ruleset = MakeRuleset();
        var nw = ruleset.Terrains["Old Faithful"];
        var context = Ctx(ruleset);

        var lines = nw.GetCivilopediaTextLines(context);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Natural Wonder", text);
        Assert.Contains("+3 ⍾Science, +2 ⌣Happiness", text);
    }

    [Fact]
    public void Terrain_ICivilopediaText_Implementation()
    {
        var ruleset = MakeRuleset();
        var land = ruleset.Terrains["Plains"];
        var feature = ruleset.Terrains["Forest"];
        var nw = ruleset.Terrains["Old Faithful"];

        Assert.Equal("Land", ((ICivilopediaText)land).GetSubCategory(ruleset));
        Assert.Equal("Features", ((ICivilopediaText)feature).GetSubCategory(ruleset));
        Assert.Equal("Natural Wonders", ((ICivilopediaText)nw).GetSubCategory(ruleset));

        Assert.Equal(0, ((ICivilopediaText)land).GetSortGroup(ruleset));
        Assert.Equal(2, ((ICivilopediaText)feature).GetSortGroup(ruleset));
        Assert.Equal(3, ((ICivilopediaText)nw).GetSortGroup(ruleset));

        Assert.Equal("Terrain/Plains", land.MakeLink());
        Assert.Equal("Plains", ((ICivilopediaText)land).GetCivilopediaTextHeader()?.Text);
    }

    // endregion

    // region TileDescription Tests

    [Fact]
    public void TileDescription_ToMarkup_CityCenterTile()
    {
        var ruleset = MakeRuleset();
        var city = new FakeCityViewDouble { Name = "Rome", Health = 180, OwnedByViewer = true };
        var tile = new FakeTileViewDouble
        {
            CityCenter = true,
            City = city,
            BaseTerrain = "Grassland",
            Ruleset = ruleset,
        };

        var lines = TileDescription.ToMarkup(tile);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Rome (180)", text);
        Assert.Contains("Producing [Warrior]", text);
        Assert.Contains("Grassland", text);
    }

    [Fact]
    public void TileDescription_ToMarkup_WithUnitsResourceAndImprovement()
    {
        var ruleset = MakeRuleset();
        var civ = new FakeCivViewDouble { Ruleset = ruleset };
        civ.ResearchedTechs.Add("Mining");

        var tile = new FakeTileViewDouble
        {
            CivView = civ,
            BaseTerrain = "Hills",
            TerrainFeatures = ["Forest"],
            ShownImprovement = "Mine",
            ImprovementIsPillaged = true,
            Resource = "Iron",
            ResourceAmount = 2,
            ViewableResource = ruleset.TileResources["Iron"],
            MilitaryUnit = new FakeMapUnitViewDouble { Name = "Swordsman", UnitHealth = 90 },
            CivilianUnit = new FakeMapUnitViewDouble { Name = "Worker", UnitHealth = 100 },
            DefensiveBonus = 0.25f,
            Ruleset = ruleset,
        };

        var lines = TileDescription.ToMarkup(tile);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Hills", text);
        Assert.Contains("Forest", text);
        Assert.Contains("[Mine] (Pillaged!)", text);
        Assert.Contains("{Iron} (2)", text);
        Assert.Contains("Swordsman(90) - Rome", text);
        Assert.Contains("Worker - Rome", text);
        Assert.Contains("[+25%] to unit defence", text);
    }

    [Fact]
    public void TileDescription_ToMarkup_NeedsResearchLine()
    {
        var ruleset = MakeRuleset();
        // Civ has not researched Mining
        var civ = new FakeCivViewDouble { Ruleset = ruleset };

        var tile = new FakeTileViewDouble
        {
            CivView = civ,
            BaseTerrain = "Hills",
            Resource = "Iron",
            ResourceAmount = 2,
            ViewableResource = ruleset.TileResources["Iron"],
            Ruleset = ruleset,
        };

        var lines = TileDescription.ToMarkup(tile);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Requires [Mining]", text);
    }

    [Fact]
    public void TileDescription_ToMarkup_FreshWaterAndImpassible()
    {
        var ruleset = MakeRuleset();
        var tile = new FakeTileViewDouble
        {
            BaseTerrain = "Grassland",
            AdjacentFilters = ["Fresh water"],
            Impassible = true,
            Ruleset = ruleset,
        };

        var lines = TileDescription.ToMarkup(tile);
        var text = string.Join("\n", lines.Select(l => l.Text));

        Assert.Contains("Fresh water", text);
        Assert.Contains("Impassable", text);
    }

    // endregion
}
