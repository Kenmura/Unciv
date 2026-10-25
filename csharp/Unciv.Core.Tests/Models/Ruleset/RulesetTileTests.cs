using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Json;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Core.Tests.Models.Ruleset;

/// <summary>
/// The tile ruleset objects of T031. Kotlin has no tests for them (TileImprovementConstructionTests needs a game, T038);
/// the expectations are derived by hand from the Kotlin source.
/// </summary>
public class RulesetTileTests
{
    private static T Read<T>(string json) => UncivJson.Deserialize<T>(json)!;

    private static TileResource Resource(string name, ResourceType type = ResourceType.Bonus) => new() { Name = name, ResourceType = type };

    [Fact]
    public void HashCodesAreProcessIndependentJvmStyleValues()
    {
        // 31-based fold with Java's String hash ("Iron".hashCode() = 2287848): fixed numbers, not HashCode.Combine
        var supply = new ResourceSupplyList.ResourceSupply(Resource("Iron"), "Tiles", 3);
        Assert.Equal(unchecked((2287848 * 31 + Unciv.Utils.JavaHash.HashCode("Tiles")) * 31 + 3), supply.GetHashCode());
        Assert.Equal(supply.GetHashCode(), new ResourceSupplyList.ResourceSupply(Resource("Iron"), "Tiles", 3).GetHashCode());
        var promotions = new Unciv.Logic.Map.MapUnits.UnitPromotions { XP = 7, NumberOfPromotions = 2 };
        Assert.Equal((7 * 31 + 2) * 31, promotions.GetHashCode());
    }

    // region enums

    [Fact]
    public void EnumsKeepKotlinOrdinalsAndProperties()
    {
        Assert.Equal(["Land", "Water", "TerrainFeature", "NaturalWonder"], Enum.GetNames<TerrainType>());
        Assert.True(TerrainType.Land.IsBaseTerrain());
        Assert.True(TerrainType.Water.IsBaseTerrain());
        Assert.False(TerrainType.TerrainFeature.IsBaseTerrain());
        Assert.False(TerrainType.NaturalWonder.IsBaseTerrain());
        Assert.Equal(["Luxury", "Strategic", "Bonus"], Enum.GetNames<ResourceType>());
        Assert.Equal("#ffd800", ResourceType.Luxury.GetColorString());
        Assert.Equal("#c14d00", ResourceType.Strategic.GetColorString());
        Assert.Equal("#a8c3c9", ResourceType.Bonus.GetColorString());
    }

    // region Terrain

    [Fact]
    public void TerrainReadsJsonAndHasKotlinDefaults()
    {
        var terrain = Read<Terrain>("""{ "name": "Hill", "type": "TerrainFeature", "food": 0, "production": 2, "RGB": [10, 20, 30], "occursOn": ["Plains"], "defenceBonus": 0.25, "movementCost": 2, "overrideStats": true }""");
        Assert.Equal("Hill", terrain.Name);
        Assert.Equal(TerrainType.TerrainFeature, terrain.Type);
        Assert.Equal(2f, terrain.Production);
        Assert.Equal([10, 20, 30], terrain.RGB);
        Assert.Equal(["Plains"], terrain.OccursOn);
        Assert.Equal(0.25f, terrain.DefenceBonus);
        Assert.Equal(2, terrain.MovementCost);
        Assert.True(terrain.OverrideStats);

        var defaults = Read<Terrain>("""{ "name": "X", "type": "Land" }""");
        Assert.Equal(10, defaults.Weight);
        Assert.Equal(1, defaults.MovementCost);
        Assert.Equal(0f, defaults.DefenceBonus);
        Assert.False(defaults.Impassable);
        Assert.False(defaults.Unbuildable);
        Assert.Null(defaults.RGB);
        Assert.Null(defaults.TurnsInto);
        Assert.Empty(defaults.OccursOn);
        Assert.Equal(0, defaults.DamagePerTurn);
        Assert.Equal(UniqueTarget.Terrain, defaults.GetUniqueTarget());
    }

    [Fact]
    public void TerrainTypeIsLateinit()
    {
        Assert.Throws<InvalidOperationException>(() => new Terrain { Name = "X" }.Type);
    }

    [Fact]
    public void TerrainFlagsFollowUniquesAndType()
    {
        var rough = Read<Terrain>("""{ "name": "Hill", "type": "TerrainFeature", "uniques": ["Rough terrain", "Occurs in groups around high elevations"] }""");
        Assert.True(rough.IsRough);
        Assert.True(rough.IsHill);
        Assert.False(rough.IsMountain);
        Assert.False(rough.IsVegetation);

        var ice = Read<Terrain>("""{ "name": "Ice", "type": "TerrainFeature", "impassable": true, "occursOn": ["Ocean"] }""");
        Assert.True(ice.IsIce);
        var notIce = Read<Terrain>("""{ "name": "Mountain", "type": "TerrainFeature", "impassable": true, "occursOn": ["Plains"] }""");
        Assert.False(notIce.IsIce);
    }

    [Fact]
    public void CoastAndOceanDistinguishWaterTerrains()
    {
        var coast = Read<Terrain>("""{ "name": "Coast", "type": "Water" }""");
        Assert.True(coast.IsCoast);
        Assert.False(coast.IsOcean);

        var ocean = Read<Terrain>("""{ "name": "Ocean", "type": "Water" }""");
        Assert.False(ocean.IsCoast);
        Assert.True(ocean.IsOcean);

        var modCoast = Read<Terrain>("""{ "name": "Shallows", "type": "Water", "uniques": ["Coastal Water"] }""");
        Assert.True(modCoast.IsCoast);
        Assert.False(modCoast.IsOcean);

        var lake = Read<Terrain>("""{ "name": "Lake", "type": "Water", "uniques": ["Fresh water"] }""");
        Assert.True(lake.IsFreshwater);
        Assert.False(lake.IsCoast);
        Assert.False(lake.IsOcean);

        var land = Read<Terrain>("""{ "name": "Coast", "type": "Land" }""");
        Assert.False(land.IsCoast);
        Assert.False(land.IsOcean);
    }

    [Fact]
    public void TerrainMatchesSingleFilter()
    {
        var wonder = Read<Terrain>("""{ "name": "Krakatoa", "type": "NaturalWonder", "impassable": true, "uniques": ["Rough terrain"] }""");
        Assert.True(wonder.MatchesSingleFilter("all"));
        Assert.True(wonder.MatchesSingleFilter("All"));
        Assert.True(wonder.MatchesSingleFilter("Terrain"));
        Assert.True(wonder.MatchesSingleFilter("Impassable"));
        Assert.False(wonder.MatchesSingleFilter("Open terrain"));
        Assert.True(wonder.MatchesSingleFilter("Rough terrain"));
        Assert.True(wonder.MatchesSingleFilter("Natural Wonder"));
        Assert.False(wonder.MatchesSingleFilter("Terrain Feature"));
        Assert.True(wonder.MatchesSingleFilter("Krakatoa"));
        Assert.True(wonder.MatchesSingleFilter("NaturalWonder"));
        Assert.False(wonder.MatchesSingleFilter("Land"));
    }

    [Fact]
    public void TerrainMatchesFilterHandlesMultiFilterAndTagUniques()
    {
        var plains = Read<Terrain>("""{ "name": "Plains", "type": "Land", "uniques": ["Tagged"] }""");
        Assert.True(plains.MatchesFilter("Plains"));
        Assert.True(plains.MatchesFilter("{Land} {Plains}"));
        Assert.False(plains.MatchesFilter("{Land} {Water}"));
        Assert.True(plains.MatchesFilter("non-[Water]"));
        Assert.False(plains.MatchesFilter("non-[Land]"));
        Assert.True(plains.MatchesFilter("Tagged"));
        Assert.True(plains.MatchesFilter("Tagged", new GameContext()));
        Assert.False(plains.MatchesFilter("Untagged"));
        // multiFilter = false takes the whole string as one term
        Assert.False(plains.MatchesFilter("{Land} {Plains}", multiFilter: false));
        Assert.True(plains.MatchesFilter("Plains", multiFilter: false));
        // cached result stays stable
        Assert.True(plains.MatchesFilter("Plains"));
    }

    [Fact]
    public void SetTransientsSumsDamage()
    {
        var terrain = Read<Terrain>("""{ "name": "Lava", "type": "TerrainFeature", "uniques": ["Units ending their turn on this terrain take [30] damage", "Units ending their turn on this terrain take [5] damage"] }""");
        Assert.Equal(0, terrain.DamagePerTurn);
        terrain.SetTransients();
        Assert.Equal(35, terrain.DamagePerTurn);
    }

    // region TileResource

    [Fact]
    public void TileResourceReadsJsonAndHasKotlinDefaults()
    {
        var resource = Read<TileResource>("""
            { "name": "Iron", "resourceType": "Strategic", "terrainsCanBeFoundOn": ["Hill", "Plains"], "improvement": "Mine", "improvedBy": ["Quarry"],
              "improvementStats": { "production": 1 }, "revealedBy": "Iron Working", "major": 1,
              "majorDepositAmount": { "sparse": 5, "default": 6, "abundant": 7 } }
            """);
        Assert.Equal(ResourceType.Strategic, resource.ResourceType);
        Assert.Equal(["Hill", "Plains"], resource.TerrainsCanBeFoundOn);
        Assert.Equal("Mine", resource.Improvement);
        Assert.Equal(["Quarry"], resource.ImprovedBy);
        Assert.Equal(1f, resource.ImprovementStats!.Production);
        Assert.Equal("Iron Working", resource.RevealedBy);
        Assert.Equal((5, 6, 7), (resource.MajorDepositAmount.Sparse, resource.MajorDepositAmount.Default, resource.MajorDepositAmount.Abundant));
        Assert.Equal((1, 2, 3), (resource.MinorDepositAmount.Sparse, resource.MinorDepositAmount.Default, resource.MinorDepositAmount.Abundant));
        Assert.Equal(UniqueTarget.Resource, resource.GetUniqueTarget());
        Assert.Equal("Iron", ((IGameResource)resource).Name);

        var defaults = Read<TileResource>("""{ "name": "Wheat" }""");
        Assert.Equal(ResourceType.Bonus, defaults.ResourceType);
        Assert.Empty(defaults.TerrainsCanBeFoundOn);
        Assert.Empty(defaults.ImprovedBy);
        Assert.Null(defaults.ImprovementStats);
        Assert.Null(defaults.Improvement);
        Assert.Null(defaults.RevealedBy);
    }

    [Fact]
    public void TileResourceCityWideAndStockpiledFollowUniques()
    {
        var plain = Resource("Wheat");
        Assert.False(plain.IsCityWide);
        Assert.False(plain.IsStockpiled);
        var special = Read<TileResource>("""{ "name": "Oil", "uniques": ["Stockpiled", "City-level resource"] }""");
        Assert.True(special.IsStockpiled);
        Assert.True(special.IsCityWide);
    }

    [Fact]
    public void TileResourceMatchesFilter()
    {
        var resource = Read<TileResource>("""{ "name": "Iron", "resourceType": "Strategic", "improvementStats": { "production": 1 } }""");
        Assert.True(resource.MatchesSingleFilter("any"));
        Assert.True(resource.MatchesSingleFilter("all"));
        Assert.True(resource.MatchesSingleFilter("All"));
        Assert.True(resource.MatchesSingleFilter("Iron"));
        Assert.True(resource.MatchesSingleFilter("Strategic"));
        Assert.True(resource.MatchesSingleFilter("Production"));
        Assert.False(resource.MatchesSingleFilter("Food"));
        Assert.False(resource.MatchesSingleFilter("Luxury"));
        Assert.True(resource.MatchesFilter("{Strategic} {Iron}"));
        Assert.True(resource.MatchesFilter("non-[Luxury]"));
        Assert.False(resource.MatchesFilter("non-[Strategic]"));
        Assert.False(Resource("Wheat").MatchesSingleFilter("Production"));
    }

    // region TileImprovement

    [Fact]
    public void TileImprovementReadsJsonAndHasKotlinDefaults()
    {
        var improvement = Read<TileImprovement>("""{ "name": "Mine", "terrainsCanBeBuiltOn": ["Hill"], "techRequired": "Mining", "turnsToBuild": 4, "replaces": "Old", "uniqueTo": "Rome", "shortcutKey": "m" }""");
        Assert.Equal(["Hill"], improvement.TerrainsCanBeBuiltOn);
        Assert.Equal("Mining", improvement.TechRequired);
        Assert.Equal(4, improvement.TurnsToBuild);
        Assert.Equal("Old", improvement.Replaces);
        Assert.Equal("Rome", improvement.UniqueTo);
        Assert.Equal('m', improvement.ShortcutKey);
        Assert.Equal(["Mining"], improvement.LegacyRequiredTechs());
        Assert.Equal(UniqueTarget.Improvement, improvement.GetUniqueTarget());

        var defaults = Read<TileImprovement>("""{ "name": "Fort" }""");
        Assert.Equal(-1, defaults.TurnsToBuild);
        Assert.Empty(defaults.TerrainsCanBeBuiltOn);
        Assert.Null(defaults.ShortcutKey);
        Assert.Empty(defaults.LegacyRequiredTechs());
    }

    [Fact]
    public void ImprovementKindPredicates()
    {
        Assert.True(Read<TileImprovement>("""{ "name": "Road" }""").IsRoad());
        Assert.True(Read<TileImprovement>("""{ "name": "Railroad" }""").IsRoad());
        Assert.False(Read<TileImprovement>("""{ "name": "None" }""").IsRoad());
        Assert.False(Read<TileImprovement>("""{ "name": "Mine" }""").IsRoad());

        var great = Read<TileImprovement>("""{ "name": "Academy", "uniques": ["Great Improvement"] }""");
        Assert.True(great.IsGreatImprovement());
        Assert.False(Read<TileImprovement>("""{ "name": "Mine" }""").IsGreatImprovement());

        Assert.True(Read<TileImprovement>("""{ "name": "Barbarian encampment" }""").IsBarbarianCampEquivalent());
        Assert.False(Read<TileImprovement>("""{ "name": "Camp" }""").IsBarbarianCampEquivalent());
        Assert.False(Read<TileImprovement>("""{ "name": "Camp" }""").IsAncientRuinsEquivalent());
    }

    [Fact]
    public void ImprovementMatchesFilter()
    {
        var road = Read<TileImprovement>("""{ "name": "Railroad", "replaces": "Road", "uniques": ["Tagged"] }""");
        Assert.True(road.MatchesFilter("all"));
        Assert.True(road.MatchesFilter("Improvement"));
        Assert.True(road.MatchesFilter("All Road"));
        Assert.True(road.MatchesFilter("Railroad"));
        Assert.True(road.MatchesFilter("Road"));
        Assert.False(road.MatchesFilter("Great"));
        Assert.True(road.MatchesFilter("{Railroad} {Improvement}"));
        Assert.True(road.MatchesFilter("non-[Great]"));
        Assert.True(road.MatchesFilter("Tagged"));
        Assert.False(road.MatchesFilter("{Railroad} {Improvement}", multiFilter: false));
        var great = Read<TileImprovement>("""{ "name": "Academy", "uniques": ["Great Improvement"] }""");
        Assert.True(great.MatchesFilter("Great Improvement"));
        Assert.True(great.MatchesFilter("Great"));
    }

    [Fact]
    public void CanBeBuiltOnUsesNamesAndTerrainFilters()
    {
        var mine = Read<TileImprovement>("""{ "name": "Mine", "terrainsCanBeBuiltOn": ["Hill", "Rough terrain"] }""");
        Assert.True(mine.CanBeBuiltOn("Hill"));
        Assert.False(mine.CanBeBuiltOn("Plains"));
        var hill = Read<Terrain>("""{ "name": "Hill", "type": "TerrainFeature" }""");
        var forest = Read<Terrain>("""{ "name": "Forest", "type": "TerrainFeature", "uniques": ["Rough terrain"] }""");
        var plains = Read<Terrain>("""{ "name": "Plains", "type": "Land" }""");
        Assert.True(mine.CanBeBuiltOn(hill));
        Assert.True(mine.CanBeBuiltOn(forest));
        Assert.False(mine.CanBeBuiltOn(plains));

        var plantation = Read<TileImprovement>("""{ "name": "Plantation", "uniques": ["Does not need removal of [Forest]"] }""");
        Assert.False(plantation.CanBeBuiltOn(forest));
        Assert.True(plantation.IsAllowedOnFeature(forest));
        Assert.False(plantation.IsAllowedOnFeature(plains));
        Assert.True(mine.IsAllowedOnFeature(hill));
    }

    [Fact]
    public void StockpiledResourceRequirementsScaleWithGameSpeedTruncating()
    {
        var improvement = Read<TileImprovement>("""{ "name": "Mega", "uniques": ["Costs [5] [Oil]", "Costs [3] [Oil] <(modified by game speed)>", "Costs [7] [Coal]"] }""");
        var info = new FakeGameInfo();
        info.FakeSpeedData.Modifier = 1.5f;
        var counter = improvement.GetStockpiledResourceRequirements(new GameContext(gameInfo: info));
        Assert.Equal(9, counter["Oil"]); // 5 + (int)(3 * 1.5f)
        Assert.Equal(7, counter["Coal"]);
    }

    // region ResourceSupplyList

    [Fact]
    public void SupplyListMergesAndDropsZeroEntries()
    {
        var iron = Resource("Iron", ResourceType.Strategic);
        var list = new ResourceSupplyList();
        list.Add(iron, "Mine", 2);
        list.Add(iron, "Mine", 3);
        Assert.Single(list);
        Assert.Equal(5, list.Get(iron, "Mine")!.Amount);
        Assert.True(list.Add(new ResourceSupplyList.ResourceSupply(iron, "Mine", -5))); // entry cancelled out and removed
        Assert.Empty(list);
        Assert.False(list.Add(new ResourceSupplyList.ResourceSupply(iron, "Trade", 0)));
        Assert.Empty(list);
        Assert.True(list.Add(new ResourceSupplyList.ResourceSupply(iron, "Trade", 1)));
        Assert.False(list.Add(new ResourceSupplyList.ResourceSupply(iron, "Trade", 1)));
        Assert.Equal(2, list.Get(iron, "Trade")!.Amount);
    }

    [Fact]
    public void SupplyListKeepZeroAmountsRetainsEntries()
    {
        var iron = Resource("Iron");
        var list = new ResourceSupplyList(keepZeroAmounts: true);
        Assert.True(list.Add(new ResourceSupplyList.ResourceSupply(iron, "Mine", 0)));
        Assert.Single(list);
        list.Add(iron, "Mine", 1);
        list.Add(iron, "Mine", -1);
        Assert.Single(list);
        Assert.Equal(0, list[0].Amount);
    }

    [Fact]
    public void SupplyListKeysResourcesByNameAndKeepsInsertionOrder()
    {
        var iron = Resource("Iron");
        var ironCopy = Resource("Iron");
        var coal = Resource("Coal");
        var list = new ResourceSupplyList();
        list.Add(iron, "B", 1);
        list.Add(coal, "A", 2);
        list.Add(ironCopy, "B", 4);
        list.Add(iron, "A", 8);
        Assert.Equal(["5 Iron from B", "2 Coal from A", "8 Iron from A"], list.Select(it => it.ToString()));
        Assert.Equal(13, list.SumBy("Iron"));
        Assert.Equal(13, list.SumBy(iron)); // by reference: the entries hold the first instance
        Assert.Equal(0, list.SumBy(ironCopy));
        Assert.Equal(0, list.SumBy("Oil"));
        Assert.Equal(["B", "A"], list.Origins());
    }

    [Fact]
    public void SupplyListAggregations()
    {
        var iron = Resource("Iron");
        var coal = Resource("Coal");
        var list = new ResourceSupplyList();
        list.Add(iron, "A", 3);
        list.Add(iron, "B", -3);
        list.Add(coal, "A", 2);
        list.Add(coal, "B", -1);

        var summed = list.SumByResource("All");
        Assert.Equal(["1 Coal from All"], summed.Select(it => it.ToString())); // Iron cancels out and is dropped

        var positive = new ResourceSupplyList();
        positive.AddPositiveByResource(list, "Pos");
        Assert.Equal(["3 Iron from Pos", "2 Coal from Pos"], positive.Select(it => it.ToString()));

        var chained = new ResourceSupplyList().AddByResource(list, "X");
        Assert.Equal(["1 Coal from X"], chained.Select(it => it.ToString()));

        var copy = new ResourceSupplyList();
        copy.Add(list);
        Assert.Equal(4, copy.Count);

        Assert.Same(copy, copy.RemoveAll("A"));
        Assert.Equal(["-3 Iron from B", "-1 Coal from B"], copy.Select(it => it.ToString()));
    }

    [Fact]
    public void SupplyListApplyModifiersTruncatesTowardZero()
    {
        var iron = Resource("Iron");
        var coal = Resource("Coal");
        var list = new ResourceSupplyList();
        list.Add(iron, "A", 3);
        list.Add(coal, "A", -3);
        list.ApplyModifiers(new Dictionary<string, float>());
        Assert.Equal(3, list.SumBy("Iron"));
        list.ApplyModifiers(new Dictionary<string, float> { ["Iron"] = 1.5f, ["Coal"] = 1.5f, ["Oil"] = 9f });
        Assert.Equal(4, list.SumBy("Iron")); // 4.5 -> 4
        Assert.Equal(-4, list.SumBy("Coal")); // -4.5 -> -4
        list.ApplyModifiers(new Dictionary<string, float> { ["Iron"] = float.NaN });
        Assert.Equal(0, list.SumBy("Iron")); // NaN.toInt() == 0; the entry stays
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void SupplyListCityStateAndTradeOrigins()
    {
        var iron = Resource("Iron");
        Assert.True(new ResourceSupplyList.ResourceSupply(iron, "City-States", 1).IsCityStateOrTradeOrigin());
        Assert.True(new ResourceSupplyList.ResourceSupply(iron, "Trade", 1).IsCityStateOrTradeOrigin());
        Assert.False(new ResourceSupplyList.ResourceSupply(iron, "Trade", 0).IsCityStateOrTradeOrigin());
        Assert.False(new ResourceSupplyList.ResourceSupply(iron, "Mine", 1).IsCityStateOrTradeOrigin());
        Assert.Equal(new ResourceSupplyList.ResourceSupply(iron, "Mine", 1), new ResourceSupplyList.ResourceSupply(iron, "Mine", 1));
        Assert.NotEqual(new ResourceSupplyList.ResourceSupply(iron, "Mine", 1), new ResourceSupplyList.ResourceSupply(iron, "Mine", 2));
    }
}
