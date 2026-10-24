using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using TileClass = Unciv.Logic.Map.Tiles.Tile;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic.Map;

/// <summary>
/// T054: TileStatFunctions, TileImprovementFunctions, TileNormalizer and ImprovementFunctions, against a real TileMap.
/// The Kotlin fixture TileImprovementConstructionTests needs TestGame (T035/T072) and is ported there.
/// </summary>
public class TileStatTests
{
    private static Ruleset MakeRuleset()
    {
        var ruleset = new Ruleset();
        void Add(string name, TerrainType type, Action<Terrain>? init = null)
        {
            var terrain = new Terrain { Name = name, Type = type };
            init?.Invoke(terrain);
            ruleset.Terrains.Put(name, terrain);
        }
        Add("Grassland", TerrainType.Land, it => it.Food = 2f);
        Add("Plains", TerrainType.Land, it => { it.Food = 1f; it.Production = 1f; });
        Add("Hill", TerrainType.Land, it => it.Production = 2f);
        Add("Mountain", TerrainType.Land, it => it.Impassable = true);
        Add("Coast", TerrainType.Water, it => it.Food = 1f);
        Add("Forest", TerrainType.TerrainFeature, it => { it.Production = 1f; it.OccursOn = ["Grassland", "Plains"]; });
        Add("Jungle", TerrainType.TerrainFeature, it => { it.OverrideStats = true; it.Food = 2f; it.OccursOn = ["Grassland"]; });
        Add("Ice", TerrainType.TerrainFeature, it => { it.Uniques = ["Nullifies all other stats this tile provides"]; it.Unbuildable = true; });
        Add("Fallout", TerrainType.TerrainFeature, it => it.Uniques = ["[+2 Production] from [Farm] tiles"]);
        Add("Oasis", TerrainType.TerrainFeature, it => { it.TurnsInto = null; it.Uniques = ["[+1 Gold]"]; });
        ruleset.TileResources.Put("Wheat", new TileResource
        {
            Name = "Wheat",
            ResourceType = ResourceType.Bonus,
            Food = 1f,
            ImprovementStats = new Stats(food: 1f),
            Improvement = "Farm",
            TerrainsCanBeFoundOn = ["Grassland", "Plains"],
        });
        ruleset.TileResources.Put("Iron", new TileResource { Name = "Iron", ResourceType = ResourceType.Strategic, TerrainsCanBeFoundOn = ["Hill"] });
        foreach (var resource in ruleset.TileResources.Values) resource.SetTransients(ruleset);
        void AddImp(string name, Action<TileImprovement>? init = null)
        {
            var improvement = new TileImprovement { Name = name };
            init?.Invoke(improvement);
            ruleset.TileImprovements.Put(name, improvement);
        }
        AddImp("Farm", it => { it.Food = 1f; it.TerrainsCanBeBuiltOn = ["Grassland", "Plains"]; });
        AddImp("Mine", it => { it.Production = 2f; it.TerrainsCanBeBuiltOn = ["Hill"]; it.TechRequired = "Mining"; });
        AddImp("Lumber mill", it => { it.Production = 1f; it.TerrainsCanBeBuiltOn = ["Forest"]; });
        AddImp("Road");
        AddImp("Railroad");
        AddImp("Remove Road");
        AddImp("Remove Railroad");
        AddImp("Remove Forest");
        AddImp("Cancel improvement order");
        AddImp("City center", it => it.Uniques = ["Ensures a minimum tile yield of [+3 Gold]", "Irremovable"]);
        AddImp("Fort", it => { it.TerrainsCanBeBuiltOn = ["Land"]; it.UniqueTo = "Rome"; });
        AddImp("Platform", it => it.TerrainsCanBeBuiltOn = ["Water"]);
        AddImp("Clear", it => { it.TerrainsCanBeBuiltOn = ["Land"]; it.Uniques = ["Removes removable features when built"]; });
        return ruleset;
    }

    private static TileMap MapOf(Ruleset ruleset, string baseTerrain = "Grassland")
    {
        var map = new TileMap(25) { GameInfo = new FakeGameInfo() };
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = baseTerrain });
        }
        map.SetTransients(ruleset);
        return map;
    }

    private static TileClass CenterTile(TileMap map) => map[HexCoord.Of(2, 2)];

    private static void SetBase(TileClass tile, string baseTerrain)
    {
        tile.BaseTerrain = baseTerrain;
        tile.SetTerrainTransients();
    }

    private sealed class StatCiv(string id, FakeGameInfo? gameInfo = null) : FakeCiv(id, gameInfo)
    {
        public override bool CanSeeResource(TileResource? resource) => resource != null;
        public override bool MatchesFilter(string filter, GameContext? state, bool multiFilter) => filter == CivId;
        public override bool IsTileViewable(TileClass tile) => false;
        public override void UpdateCivResources() { }
        public List<TileImprovement> Replacements { get; } = [];
        public override IEnumerable<TileImprovement> UniqueImprovements => Replacements;
    }

    private sealed class StatCity(string id, StatCiv civ, TileClass center) : FakeCity(id, civ)
    {
        public override string Name => "Rome";
        public override HexCoord Location => center.Position;
        public override TileClass GetCenterTile() => center;
        public override void ReassignPopulationDeferred() { }
    }

    private static Stats Yield(TileClass tile, StatCiv? civ = null, StatCity? city = null) => tile.Stats.GetTileStats(city, civ);

    // ---- TileStatFunctions ----

    [Fact]
    public void BaseTerrainStats()
    {
        var map = MapOf(MakeRuleset());
        var stats = Yield(CenterTile(map));
        Assert.Equal(2f, stats.Food);
        Assert.Equal(0f, stats.Production);
    }

    [Fact]
    public void FeatureStatsAreAddedToTheBaseTerrain()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Forest");
        var stats = Yield(tile);
        Assert.Equal(2f, stats.Food);
        Assert.Equal(1f, stats.Production);
    }

    [Fact]
    public void OverrideStatsReplacesEarlierTerrains()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Jungle");
        var breakdown = tile.Stats.GetTerrainStatsBreakdown();
        Assert.Equal(["Jungle"], breakdown.Select(it => it.Cause).ToList());
    }

    [Fact]
    public void NullifyYieldsTerrainShortCircuitsEverythingElse()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Ice");
        tile.SetImprovementBasic("Farm");
        var stats = Yield(tile, new StatCiv("Rome", new FakeGameInfo()));
        Assert.True(stats.IsEmpty());
    }

    [Fact]
    public void StatsUniqueOnTerrainIsListedWithItsDisplayText()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Oasis");
        var breakdown = tile.Stats.GetTerrainStatsBreakdown();
        Assert.Equal(["Grassland", "Oasis", "Oasis: [+1 Gold]"], breakdown.Select(it => it.Cause).ToList());
        Assert.Equal(1f, Yield(tile).Gold);
    }

    [Fact]
    public void BreakdownDoesNotExposeTheTerrainsOwnStats()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var stats = Yield(tile);
        stats.Food += 10f;
        Assert.Equal(2f, Yield(tile).Food);
        Assert.Equal(2f, map.Ruleset!.Terrains["Grassland"].Food);
    }

    [Fact]
    public void ImprovementStatsNeedAnObservingCiv()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.SetImprovementBasic("Farm");
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        // The improvement's own stats are cloned in even without a civ, only the extras need one
        Assert.Equal(3f, Yield(tile).Food);
        Assert.Equal(3f, Yield(tile, civ).Food);
    }

    [Fact]
    public void PillagedImprovementDoesNotCount()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.SetImprovementBasic("Farm");
        tile.ImprovementIsPillaged = true;
        Assert.Equal(2f, Yield(tile).Food);
    }

    [Fact]
    public void ResourceAndImprovedResourceStatsAreSeenByTheCiv()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.SetTileResource("Wheat");
        tile.SetImprovementBasic("Farm");
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        // 2 terrain + 1 resource + 1 farm + 1 improvedResource bonus
        Assert.Equal(5f, Yield(tile, civ).Food);
        // Without a civ the resource is not added at all
        Assert.Equal(3f, Yield(tile).Food);
    }

    [Fact]
    public void CityCenterGetsTheDefaultMinimum()
    {
        var map = MapOf(MakeRuleset(), "Mountain");
        var tile = CenterTile(map);
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        var city = new StatCity("c", civ, tile);
        tile.SetOwningCity(city);
        var stats = Yield(tile, civ, city);
        Assert.Equal(2f, stats.Food);
        Assert.Equal(1f, stats.Production);
    }

    [Fact]
    public void EnsureMinimumStatsUniqueOverridesTheDefaultMinimum()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        var city = new StatCity("c", civ, tile);
        tile.SetOwningCity(city);
        tile.SetImprovementBasic("City center");
        var stats = Yield(tile, civ, city);
        Assert.Equal(2f, stats.Food);
        Assert.Equal(3f, stats.Gold);
        Assert.Equal(0f, stats.Production);
    }

    [Fact]
    public void GoldenAgeAddsOneGoldOnlyToTilesAlreadyProducingGold()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo) { GoldenAge = true };
        Assert.Equal(0f, Yield(tile, civ).Gold);
        tile.AddTerrainFeature("Oasis");
        Assert.Equal(2f, Yield(tile, civ).Gold);
    }

    [Fact]
    public void RiverGivesOneGoldWhenTheRulesetHasNoRiverTerrain()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.SetConnectedByRiver(map[HexCoord.Of(3, 2)], true);
        Assert.Equal(1f, Yield(tile).Gold);
    }

    [Fact]
    public void StatsFromTilesUniqueOfTheCityIsAddedToMatchingTerrain()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        var city = new StatCity("c", civ, map[HexCoord.Of(0, 0)]);
        city.LocalUniques.Add(new UniqueObject("[+1 Culture] from [Grassland] tiles [in this city]"));
        city.CityFilters.Add("in this city");
        var stats = Yield(tile, civ, city);
        Assert.Equal(1f, stats.Culture);
        var names = tile.Stats.GetTileStatsBreakdown(city, civ).Select(it => it.Cause).ToList();
        Assert.Contains("{null} ({[+1 Culture] from [Grassland] tiles [in this city]})", names);
    }

    [Fact]
    public void PercentBonusesApplyPerCategory()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.SetImprovementBasic("Farm");
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        civ.CivUniques.Add(new UniqueObject("[+50]% [Food] from every [Farm]"));
        civ.CivUniques.Add(new UniqueObject("[+100]% [Food] from every [Grassland]"));
        // Terrain 2 food doubled, farm 1 food +50%
        Assert.Equal(5.5f, Yield(tile, civ).Food);
        var percent = tile.Stats.GetTilePercentageStats(civ, null);
        Assert.Equal(50f, percent[TileStatFunctions.TilePercentageCategory.Improvement].Food);
        Assert.Equal(100f, percent[TileStatFunctions.TilePercentageCategory.Terrain].Food);
        Assert.Equal(0f, percent[TileStatFunctions.TilePercentageCategory.Road].Food);
    }

    [Fact]
    public void StatDiffForImprovementIsFutureMinusCurrent()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var civ = new StatCiv("Rome", map.GameInfo as FakeGameInfo);
        var diff = tile.Stats.GetStatDiffForImprovement(map.Ruleset!.TileImprovements["Farm"], civ, null);
        Assert.Equal(1f, diff.Food);
        Assert.Null(tile.Improvement);
    }

    [Fact]
    public void TileStartScoreCountsCloseTilesAndTheNeighborsTwice()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        // 19 tiles within distance 2, each 2 food; the 6 neighbors count twice; no coast, river or hills
        var withoutMinimum = tile.Stats.GetTileStartScore(Stats.ZERO);
        Assert.Equal(19 * 2f + 6 * 2f, withoutMinimum);
        // The center is topped up to the minimum 2 food + 1 production
        Assert.Equal(withoutMinimum + 1f, tile.Stats.GetTileStartScore(Stats.DefaultCityCenterMinimum));
    }

    [Fact]
    public void TileStartScoreRewardsCoastAndPenalizesHills()
    {
        var baseline = CenterTile(MapOf(MakeRuleset())).Stats.GetTileStartScore(Stats.ZERO);
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        SetBase(map[HexCoord.Of(3, 2)], "Coast");
        Assert.True(tile.IsAdjacentToCoast());
        // The coast tile now yields 1 food, not 2, and counts twice: -2; +3 for the coast
        Assert.Equal(baseline - 2f + 3f, tile.Stats.GetTileStartScore(Stats.ZERO));
    }

    // ---- TileImprovementFunctions ----

    [Fact]
    public void CanImprovementBeBuiltHere_BasicRules()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var ruleset = map.Ruleset!;
        var functions = tile.ImprovementFunctions;
        Assert.True(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Farm"]));
        Assert.False(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Mine"]));
        Assert.True(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Road"]));
        Assert.False(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Platform"]));
        Assert.False(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Cancel improvement order"]));
        Assert.False(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Remove Road"]));
        tile.SetImprovementBasic("Farm");
        Assert.False(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Farm"]));
        Assert.True(functions.CanImprovementBeBuiltHere(ruleset.TileImprovements["Farm"], isNormalizeCheck: true));
    }

    [Fact]
    public void CanImprovementBeBuiltHere_RoadsAndRemovals()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var improvements = map.Ruleset!.TileImprovements;
        var functions = tile.ImprovementFunctions;
        tile.SetRoadStatus(RoadStatus.Road, null);
        Assert.False(functions.CanImprovementBeBuiltHere(improvements["Road"]));
        Assert.True(functions.CanImprovementBeBuiltHere(improvements["Railroad"]));
        Assert.True(functions.CanImprovementBeBuiltHere(improvements["Remove Road"]));
        Assert.False(functions.CanImprovementBeBuiltHere(improvements["Remove Railroad"]));
        Assert.False(functions.CanImprovementBeBuiltHere(improvements["Remove Forest"]));
        tile.AddTerrainFeature("Forest");
        Assert.True(functions.CanImprovementBeBuiltHere(improvements["Remove Forest"]));
        tile.QueueImprovement("Farm", 3);
        Assert.True(functions.CanImprovementBeBuiltHere(improvements["Cancel improvement order"]));
    }

    [Fact]
    public void CanImprovementBeBuiltHere_UnbuildableTerrainNeedsPermission()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var improvements = map.Ruleset!.TileImprovements;
        tile.AddTerrainFeature("Ice");
        Assert.False(tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvements["Farm"]));
        Assert.False(tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvements["Clear"]));
    }

    [Fact]
    public void CanImprovementBeBuiltHere_ResourceOnlyImprovementsAndDomain()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var improvements = map.Ruleset!.TileImprovements;
        SetBase(tile, "Hill");
        tile.SetTileResource("Iron");
        // Mine is allowed on Hill; Iron is not improved by it, so only the terrain counts
        Assert.True(tile.ImprovementFunctions.CanImprovementBeBuiltHere(improvements["Mine"]));
        Assert.True(tile.ImprovementFunctions.ExtendedDomainCheck(improvements["Mine"]));
        Assert.False(tile.ImprovementFunctions.ExtendedDomainCheck(improvements["Platform"]));
        Assert.True(tile.ImprovementFunctions.ExtendedDomainCheck(improvements["Road"]));
    }

    [Fact]
    public void SetImprovement_SetsTheImprovementAndRemovesFeaturesItReplaces()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Forest");
        tile.SetImprovement("Clear");
        Assert.Equal("Clear", tile.Improvement);
        Assert.Empty(tile.TerrainFeatures);
        tile.SetImprovement("Lumber mill");
        Assert.Equal("Lumber mill", tile.Improvement);
    }

    [Fact]
    public void SetImprovement_RoadsGoThroughRoadStatus()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.SetImprovement("Road");
        Assert.Equal(RoadStatus.Road, tile.RoadStatus);
        Assert.Null(tile.Improvement);
        tile.SetImprovement("Railroad");
        Assert.Equal(RoadStatus.Railroad, tile.RoadStatus);
        tile.SetImprovement("Remove Railroad");
        Assert.Equal(RoadStatus.None, tile.RoadStatus);
    }

    [Fact]
    public void SetImprovement_RemovalOfFeatureAndRemoveImprovement()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Forest");
        tile.SetImprovementBasic("Lumber mill");
        tile.SetImprovement("Remove Forest");
        Assert.Empty(tile.TerrainFeatures);
        // The lumber mill needs the forest, so it went with it
        Assert.Null(tile.Improvement);
        tile.SetImprovementBasic("Farm");
        tile.ImprovementIsPillaged = true;
        tile.RemoveImprovement();
        Assert.Null(tile.Improvement);
        Assert.False(tile.ImprovementIsPillaged);
    }

    [Fact]
    public void SetImprovement_UpdatesLastSeenImprovementOfCivsThatSeeTheTile()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var info = (FakeGameInfo)map.GameInfo;
        var civ = new StatCiv("Rome", info);
        var other = new StatCiv("Greece", info);
        info.FakeCivs.Add(civ);
        info.FakeCivs.Add(other);
        tile.SetImprovement(map.Ruleset!.TileImprovements["Farm"], civ);
        Assert.Equal("Farm", civ.LastSeenImprovements[tile.Position]);
        Assert.Empty(other.LastSeenImprovements);
    }

    [Fact]
    public void SetImprovement_BroaderEffectsUpdateTheOwningCity()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var civ = new StatCiv("Rome", (FakeGameInfo)map.GameInfo);
        var city = new StatCity("c", civ, map[HexCoord.Of(0, 0)]);
        tile.SetOwningCity(city);
        tile.SetImprovement("Farm");
        Assert.Equal(0, city.CityStatsUpdates);
        tile.SetImprovement("Farm", civ);
        Assert.Equal(1, city.CityStatsUpdates);
    }

    [Fact]
    public void SetPillaged_NeedsRepairActionToKeepTheImprovement()
    {
        var ruleset = MakeRuleset();
        var map = MapOf(ruleset);
        var tile = CenterTile(map);
        tile.SetImprovementBasic("Farm");
        tile.SetPillaged();
        Assert.Null(tile.Improvement); // no "Repair" improvement: destroyed
        ruleset.TileImprovements.Put("Repair", new TileImprovement { Name = "Repair" });
        tile.SetImprovementBasic("Farm");
        tile.SetPillaged();
        Assert.True(tile.ImprovementIsPillaged);
        Assert.Equal("Farm", tile.Improvement);
    }

    [Fact]
    public void ClearingBarbarianCampNotifiesQuestsOnlyWithGameInfo()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.RemoveImprovement();
        Assert.Equal(0, ((FakeGameInfo)map.GameInfo).ObsoleteQuestCalls);
    }

    // ---- ImprovementFunctions ----

    [Fact]
    public void BuildingProblems_WithoutCivIsWrongCiv()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var farm = map.Ruleset!.TileImprovements["Farm"];
        Assert.Equal([ImprovementBuildingProblem.WrongCiv], tile.ImprovementFunctions.GetImprovementBuildingProblems(farm, new GameContext()).ToList());
        Assert.False(tile.ImprovementFunctions.CanBuildImprovement(farm, new GameContext()));
    }

    [Fact]
    public void BuildingProblems_ListsEveryReasonInOrder()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var civ = new StatCiv("Greece", (FakeGameInfo)map.GameInfo);
        var mine = map.Ruleset!.TileImprovements["Mine"];
        var problems = ImprovementFunctions.GetImprovementBuildingProblems(mine, new GameContext(civInfo: civ), tile).ToList();
        // Mine is not buildable on Grassland (Other) and the tile has no owner (OutsideBorders)
        Assert.Equal([ImprovementBuildingProblem.MissingTech, ImprovementBuildingProblem.OutsideBorders, ImprovementBuildingProblem.Other], problems);
    }

    [Fact]
    public void BuildingProblems_UniqueToAndReplaced()
    {
        var map = MapOf(MakeRuleset());
        var ruleset = map.Ruleset!;
        var civ = new StatCiv("Greece", (FakeGameInfo)map.GameInfo);
        var fort = ruleset.TileImprovements["Fort"];
        Assert.Equal([ImprovementBuildingProblem.WrongCiv],
            ImprovementFunctions.GetImprovementBuildingProblems(fort, new GameContext(civInfo: civ)).ToList());
        civ.Replacements.Add(new TileImprovement { Name = "Better fort", Replaces = "Fort" });
        Assert.Equal([ImprovementBuildingProblem.WrongCiv, ImprovementBuildingProblem.Replaced],
            ImprovementFunctions.GetImprovementBuildingProblems(fort, new GameContext(civInfo: civ)).ToList());
    }

    [Fact]
    public void BuildingProblems_UnbuildableAndPermanence()
    {
        var map = MapOf(MakeRuleset());
        var ruleset = map.Ruleset!;
        ruleset.TileImprovements.Put("Wonder", new TileImprovement { Name = "Wonder", Uniques = ["Unbuildable"] });
        ruleset.TileImprovements.Put("Maybe", new TileImprovement { Name = "Maybe", Uniques = ["Unbuildable <for [Rome] Civilizations>"] });
        var civ = new StatCiv("Rome", (FakeGameInfo)map.GameInfo);
        Assert.Contains(ImprovementBuildingProblem.Unbuildable,
            ImprovementFunctions.GetImprovementBuildingProblems(ruleset.TileImprovements["Wonder"], new GameContext(civInfo: civ)).ToList());
        Assert.True(ImprovementBuildingProblem.Unbuildable.IsPermanent());
        Assert.False(ImprovementBuildingProblem.ConditionallyUnbuildable.IsPermanent());
        Assert.True(ImprovementBuildingProblem.MissingTech.IsReportable());
        Assert.False(ImprovementBuildingProblem.Other.IsReportable());
    }

    // ---- TileNormalizer ----

    [Fact]
    public void NormalizeToRuleset_FixesTerrainFeaturesResourcesAndImprovements()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        var ruleset = map.Ruleset!;
        SetBase(tile, "Hill");
        tile.AddTerrainFeature("Jungle"); // does not occur on Hill
        tile.SetTileResource("Wheat"); // cannot be found on Hill
        tile.QueueImprovement("Mine", 2);
        tile.QueueImprovement("Farm", 2);
        TileNormalizer.NormalizeToRuleset(tile, ruleset);
        Assert.Empty(tile.TerrainFeatures);
        Assert.Null(tile.Resource);
        // Only the queue entries that are still buildable stay
        Assert.Equal(["Mine"], tile.ImprovementQueue.Select(it => it.Improvement).ToList());
        tile.SetImprovementBasic("Farm"); // cannot be built on Hill
        TileNormalizer.NormalizeToRuleset(tile, ruleset);
        Assert.Null(tile.Improvement);
        Assert.Empty(tile.ImprovementQueue); // clearing the improvement also stops the work in progress
    }

    [Fact]
    public void NormalizeToRuleset_UnknownBaseTerrainFallsBackToFirstPassableLand()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.BaseTerrain = "Atlantis";
        TileNormalizer.NormalizeToRuleset(tile, map.Ruleset!);
        Assert.Equal("Grassland", tile.BaseTerrain);
    }

    [Fact]
    public void NormalizeToRuleset_WaterAndImpassableTilesLoseTheirRoad()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        SetBase(tile, "Mountain");
        tile.SetRoadStatus(RoadStatus.Road, null);
        TileNormalizer.NormalizeToRuleset(tile, map.Ruleset!);
        Assert.Equal(RoadStatus.None, tile.RoadStatus);
    }

    [Fact]
    public void SetBaseTerrain_NormalizesAndRefreshesTransients()
    {
        var map = MapOf(MakeRuleset());
        var tile = CenterTile(map);
        tile.AddTerrainFeature("Forest");
        tile.SetBaseTerrain(map.Ruleset!.Terrains["Hill"]); // Forest does not occur on Hill
        Assert.Equal("Hill", tile.BaseTerrain);
        Assert.Empty(tile.TerrainFeatures);
        Assert.Equal("Hill", tile.LastTerrain.Name);
    }
}
