using System.Reflection;
using Unciv.Core.Tests.Logic;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models.Metadata;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.View;
using GeneratorClass = Unciv.Logic.Map.MapGenerator.MapGenerator;
using RulesetClass = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Core.Tests.View;

// T084: the Views A classes on a small real game (vanilla ruleset, generated Tiny map), the way GameInfoTests builds one.
// GameViewMovementHistoryTest (TestGame, T103) needs the unit views (T085); its attack cases are ported here. ViewPurityTest relies on
// the yairm210.purity annotations, which are stripped; its intent (only owned views mutate, and they report a Boolean) is checked structurally.
[Collection("RulesetCache")]
public sealed class ViewTests : IDisposable
{
    private readonly RulesetClass ruleset;
    private readonly GameInfo game;
    private readonly Civilization babylon;
    private readonly Civilization greece;
    private readonly Civilization geneva;

    public ViewTests()
    {
        RulesetCache.Clear();
        var jsons = JsonsPath();
        RulesetCache.LoadRulesets(new DirectoryRulesetFileSource(jsons, Path.Combine(jsons, "no mods here")), noMods: true);
        ruleset = RulesetCache.GetVanillaRuleset();
        game = new GameInfo
        {
            GameParameters = new GameParameters { BaseRuleset = ruleset.Name },
            TileMap = new GeneratorClass(ruleset).GenerateMap(
                new MapParameters { Type = MapType.Pangaea, Shape = MapShape.Rectangular, Seed = 3, MapSize = MapSize.Tiny }),
            Difficulty = "Prince",
            GameId = "test-game",
        };
        foreach (var (nation, type) in new[] { ("Babylon", PlayerType.Human), ("Greece", PlayerType.AI), ("Geneva", PlayerType.AI), (Constants.Barbarians, PlayerType.AI) })
            game.Civilizations.Add(new Civilization(nation) { PlayerType = type });
        game.TileMap.GameInfo = game;
        foreach (var civ in game.Civilizations)
        {
            civ.CityStateFunctions = new FakeCityStateFunctions();
        }
        game.SetTransients();
        // Each real civ owns a settler so none counts as defeated
        var land = game.TileMap.TileList.Where(it => it.IsLand && !it.IsImpassible()).ToList();
        for (var i = 0; i < 3; i++)
            Assert.NotNull(game.TileMap.PlaceUnitNearTile(land[i * 7].Position, "Settler", game.Civilizations[i]));
        babylon = game.GetCivilization("Babylon");
        greece = game.GetCivilization("Greece");
        geneva = game.GetCivilization("Geneva");
    }

    public void Dispose() => RulesetCache.Clear();

    private static string JsonsPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "android", "assets", "jsons");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("android/assets/jsons not found above " + AppContext.BaseDirectory);
    }

    private GameView ViewAs(Civilization civ, bool spectatorMode = false) => new(game, civ, spectatorMode);

    private Unciv.Logic.Map.Tiles.Tile Tile(int index) => game.TileMap.TileList[index];

    // ---- View / GameBasedView / OwnedView ----

    [Fact]
    public void Views_AreEqualWhenTheyWrapTheSameObject()
    {
        var view = ViewAs(babylon);
        var other = ViewAs(greece);
        Assert.Equal(view.GetCivView(greece), other.CivView);
        Assert.Equal(view.GetCivView(greece).GetHashCode(), other.CivView.GetHashCode());
        Assert.NotEqual(view.GetCivView(greece), view.CivView);
        // A CivView and a ForeignCivView of the same civ wrap the same object, as in Kotlin (`other is View<*> && wrapped == other.wrapped`)
        Assert.Equal(view.GetCivView(greece), view.GetForeignCivView(greece));
        Assert.False(view.CivView.Equals(null));
        Assert.False(view.CivView.Equals("Babylon"));
        var set = new HashSet<ForeignCivView> { view.GetForeignCivView(greece), other.GetForeignCivView(greece) };
        Assert.Single(set);
    }

    [Fact]
    public void IsOwnedByViewer_ComparesTheOwnerByReference()
    {
        var view = ViewAs(babylon);
        Assert.True(view.CivView.IsOwnedByViewer());
        Assert.False(view.GetForeignCivView(greece).IsOwnedByViewer());
        Assert.True(view.GetForeignCivView(babylon).IsOwnedByViewer());
    }

    [Fact]
    public void SpectatorMode_IsCarriedToEveryNavigatedView()
    {
        var view = ViewAs(babylon, spectatorMode: true);
        Assert.True(view.SpectatorMode);
        Assert.True(view.CivView.SpectatorMode);
        Assert.True(view.TileMapView.SpectatorMode);
        Assert.True(view.GetForeignCivView(greece).SpectatorMode);
        Assert.True(view.GetTile(Tile(0)).SpectatorMode);
        Assert.Same(view, view.GetForeignCivView(greece).GameView);
        Assert.False(ViewAs(babylon).SpectatorMode);
    }

    // ---- GameView ----

    [Fact]
    public void GameView_Turn_CivView_AndNavigation()
    {
        game.Turns = 42;
        var view = ViewAs(babylon);
        Assert.Equal(42, view.GetTurn());
        Assert.Equal("Babylon", view.CivView.CivName);
        Assert.Equal("Greece", view.GetCivView(greece).CivName);
        Assert.Equal(greece.CivId, view.GetForeignCivView(greece).CivId);
    }

    [Fact]
    public void GameView_GetTile_CachesPerTile_AndResolvesViewsAcrossPerspectives()
    {
        var view = ViewAs(babylon);
        var spectator = ViewAs(greece, spectatorMode: true);
        var tile = Tile(5);
        Assert.Same(view.GetTile(tile), view.GetTile(tile));
        Assert.Same(view.TileMapView.GetTile(tile), view.GetTile(tile));
        Assert.NotSame(view.GetTile(tile), spectator.GetTile(tile));
        Assert.Same(spectator.GetTile(tile), spectator.GetTile(view.GetTile(tile)));
    }

    private (Unciv.Logic.Map.Tiles.Tile Source, Unciv.Logic.Map.Tiles.Tile Target) NeighbourPair(int sourceIndex = 20) =>
        (Tile(sourceIndex), Tile(sourceIndex).Neighbors[0]);

    [Fact]
    public void GetVisibleAttacks_ForeignRecordsNeedAnEndpointInSight_OwnRecordsAlwaysShow()
    {
        var (source, target) = NeighbourPair(20);
        var (storedSource, storedTarget) = NeighbourPair(60);
        greece.AttacksSinceTurnStart.Add(new Civilization.HistoricalAttackMemory(null, storedSource.Position, storedTarget.Position));
        babylon.AttacksSinceTurnStart.Add(new Civilization.HistoricalAttackMemory("Archer", source.Position, target.Position));
        var view = ViewAs(babylon);

        babylon.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile>();
        Assert.Equal([(source.Position, target.Position)], view.GetVisibleAttacks().ToList());

        foreach (var visible in new[] { storedSource, storedTarget })
        {
            babylon.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { visible };
            Assert.Equal(
                new HashSet<(HexCoord, HexCoord)> { (source.Position, target.Position), (storedSource.Position, storedTarget.Position) },
                view.GetVisibleAttacks().ToHashSet());
        }
    }

    // ---- TileMapView / TileView ----

    [Fact]
    public void TileMapView_GetTileByPosition_ReturnsNullUnlessExplored()
    {
        var view = ViewAs(babylon);
        var tile = Tile(30);
        Assert.Null(view.TileMapView.GetTile(tile.Position));
        tile.SetExplored(babylon, true);
        var tileView = view.TileMapView.GetTile(tile.Position);
        Assert.NotNull(tileView);
        Assert.Equal(tile.Position, tileView.Position());
        // The map editor has no viewer: every tile exists
        Assert.NotNull(new TileMapView(game.TileMap, null).GetTile(Tile(31).Position));
    }

    [Fact]
    public void TileView_ExploredAndVisible_FollowTheViewer()
    {
        var view = ViewAs(babylon);
        var tile = Tile(30);
        var tileView = view.GetTile(tile);
        Assert.False(tileView.IsExplored());
        Assert.False(tileView.IsVisible());
        Assert.False(tileView.IsForceVisible());
        Assert.False(view.CivView.CanSeeTile(tileView));
        Assert.False(view.CivView.HasExplored(tileView));

        tile.SetExplored(babylon, true);
        babylon.ViewableTiles = new HashSet<Unciv.Logic.Map.Tiles.Tile> { tile };
        Assert.True(tileView.IsExplored());
        Assert.True(tileView.IsVisible());
        Assert.True(view.CivView.CanSeeTile(tileView));
        Assert.True(view.CivView.HasExplored(tileView));
        Assert.Equal(tile.IsLand, tileView.IsLand);
        Assert.Equal(tile.BaseTerrain, tileView.BaseTerrain);
        Assert.Equal(tile.IsCityCenter(), tileView.IsCityCenter());
        Assert.Equal(tile.GetDefensiveBonus(), tileView.GetDefensiveBonus());
    }

    [Fact]
    public void TileView_ForceVisible_ForDebugVisibleMap_AndForRevealedDefeatedGame()
    {
        var tileView = ViewAs(babylon).GetTile(Tile(30));
        Assert.False(tileView.IsForceVisible());
        game.EngineOptions.VisibleMap = true;
        Assert.True(tileView.IsForceVisible());
        game.EngineOptions.VisibleMap = false;

        // No viewer (civilopedia / map editor previews): everything is explored, visible and forced visible
        var single = TileView.ForSingleTile(new Unciv.Logic.Map.Tiles.Tile()); // a standalone tile has index 0, the only slot of its one-tile map
        Assert.True(single.IsExplored());
        Assert.True(single.IsVisible());
        Assert.True(single.IsForceVisible());
        Assert.Null(single.GetOwner());
        Assert.Null(single.GetCivView());
        Assert.False(single.IsFriendlyTerritory());
    }

    [Fact]
    public void TileView_ExploredNeighbours_AndUnexploredEstimate()
    {
        var view = ViewAs(babylon);
        var tile = Tile(40);
        var tileView = view.GetTile(tile);
        Assert.Empty(tileView.GetVisibleNeighbors());
        Assert.Equal(6, tileView.EstimateUnexploredTilesAtDistance(1));
        Assert.Equal(12, tileView.EstimateUnexploredTilesAtDistance(2));

        foreach (var neighbor in tile.Neighbors.Take(2)) neighbor.SetExplored(babylon, true);
        Assert.Equal(2, tileView.GetVisibleNeighbors().Count());
        Assert.Equal(2, tileView.GetExploredTilesAtDistance(1).Count());
        Assert.Equal(4, tileView.EstimateUnexploredTilesAtDistance(1));
        Assert.Equal(2, tileView.GetExploredTilesInDistance(1).Count());
        // Never negative, even when more tiles are explored than the estimated ring holds
        foreach (var neighbor in tile.GetTilesInDistance(3)) neighbor.SetExplored(babylon, true);
        Assert.Equal(0, tileView.EstimateUnexploredTilesAtDistance(0));
        Assert.Equal(System.Math.Max(0, 6 - tile.GetTilesAtDistance(1).Count()), tileView.EstimateUnexploredTilesAtDistance(1));
    }

    [Fact]
    public void TileView_ClockPositionAndSharedNeighbours()
    {
        var view = ViewAs(babylon);
        var tile = Tile(40);
        var neighbor = tile.Neighbors[0];
        foreach (var around in tile.GetTilesInDistance(2)) around.SetExplored(babylon, true);
        var tileView = view.GetTile(tile);
        var neighborView = view.GetTile(neighbor);
        var clock = game.TileMap.GetNeighborTileClockPosition(tile, neighbor);
        Assert.Equal(clock, tileView.GetNeighborClockPosition(neighborView));

        var left = view.TileMapView.GetLeftSharedNeighbor(tileView, neighborView);
        var right = view.TileMapView.GetRightSharedNeighbor(tileView, neighborView);
        Assert.Equal(game.TileMap.GetClockPositionNeighborTile(tile, (clock - 2) % 12), left?.Unwrap());
        Assert.Equal(game.TileMap.GetClockPositionNeighborTile(tile, (clock + 2) % 12), right?.Unwrap());
        Assert.Equal(game.TileMap.GetNeighborTilePositionAsWorldCoords(tile, neighbor),
            view.TileMapView.GetNeighborTilePositionAsWorldCoords(tileView, neighborView));
        // Not neighbours: clock position is -1, (-1 - 2) % 12 = -3 is no valid clock position
        var far = view.GetTile(Tile(100));
        Assert.Null(view.TileMapView.GetLeftSharedNeighbor(tileView, far));
    }

    [Fact]
    public void TileMapView_RelativeWorldPosition_AndUnwrappedPosition()
    {
        var view = ViewAs(babylon);
        var from = view.GetTile(Tile(40));
        var to = view.GetTile(Tile(41));
        var offset = view.TileMapView.GetRelativeWorldPosition(from, to);
        var expected = HexMath.Hex2WorldCoords(Tile(41).Position);
        var origin = HexMath.Hex2WorldCoords(Tile(40).Position);
        Assert.Equal(expected.x - origin.x, offset.x);
        Assert.Equal(expected.y - origin.y, offset.y);
        Assert.Equal(game.TileMap.GetUnwrappedPosition(Tile(41).Position), view.TileMapView.GetUnwrappedPosition(Tile(41).Position));

        // World wrap picks the nearest copy of the target, so the offset across the seam stays short
        game.TileMap.MapParameters.WorldWrap = true;
        var wrapped = view.TileMapView.GetRelativeWorldPosition(from, to);
        Assert.True(System.Math.Abs(wrapped.x) <= System.Math.Abs(offset.x) + 0.0001f || System.Math.Abs(wrapped.y) <= System.Math.Abs(offset.y) + 0.0001f);
    }

    [Fact]
    public void TileMapView_StartingLocationNationNames()
    {
        var view = ViewAs(babylon);
        var tile = Tile(10);
        game.TileMap.StartingLocationsByNation["Babylon"] = [tile];
        game.TileMap.StartingLocationsByNation["Greece"] = [Tile(11), tile];
        game.TileMap.StartingLocationsByNation["Rome"] = [Tile(11)];
        Assert.Equal(["Babylon", "Greece"], view.TileMapView.GetStartingLocationNationNames(view.GetTile(tile)));
        Assert.Equal(["Greece", "Rome"], view.TileMapView.GetStartingLocationNationNames(view.GetTile(Tile(11))));
        Assert.Empty(view.TileMapView.GetStartingLocationNationNames(view.GetTile(Tile(12))));
    }

    [Fact]
    public void TileView_ViewableResource_RespectsTheViewingCivsTech()
    {
        var view = ViewAs(babylon);
        var tile = Tile(30);
        var iron = ruleset.TileResources["Iron"];
        tile.SetTileResource(iron, true, new Unciv.Utils.GameRandom(1));
        var tileView = view.GetTile(tile);
        Assert.Equal("Iron", tileView.Resource);
        Assert.Same(iron, tileView.GetViewableResource(null));
        Assert.Equal(babylon.CanSeeResource(iron), view.CivView.CanSeeResource(iron));
        Assert.Same(babylon.CanSeeResource(iron) ? iron : null, tileView.GetViewableResource(view.CivView));
        Assert.Null(view.GetTile(Tile(31)).GetViewableResource(view.CivView)); // no resource
    }

    [Fact]
    public void TileView_Stats_AndTerrains_AndMaintenance()
    {
        var view = ViewAs(babylon);
        var tile = Tile(30);
        var tileView = view.GetTile(tile);
        Assert.Equal(tile.Stats.GetTileStats(tile.GetCity(), babylon).ToString(), tileView.GetTileStats(view.CivView).ToString());
        Assert.Equal(tile.Stats.GetTileStatsBreakdown(tile.GetCity(), babylon).Count, tileView.GetTileStatsBreakdown(view.CivView).Count);
        Assert.Equal(tile.AllTerrains.Select(it => it.Name), tileView.GetAllTerrains().Select(it => it.Name));
        Assert.Equal(tile.BaseTerrain, tileView.GetBaseTerrain().Name);
        Assert.Same(game.Ruleset, tileView.GetRuleset());
        var farm = ruleset.TileImprovements["Farm"];
        Assert.Equal(tile.Stats.GetStatDiffForImprovement(farm, babylon, tile.GetCity()).ToString(),
            tileView.GetStatDiffForImprovement(farm, view.CivView).ToString());
        // Farms cost no upkeep and the tile has no owner
        Assert.Equal(new Unciv.Models.StatTypes.Stats().ToString(), tileView.GetImprovementMaintenance(farm, view.CivView).ToString());
        Assert.Equal(tile.ProvidesResources(babylon), tileView.ProvidesResources(view.CivView));
    }

    // ---- ForeignCivView / CivView ----

    [Fact]
    public void ForeignCivView_ExposesCivData_AndDiplomacy()
    {
        var view = ViewAs(babylon);
        var foreign = view.GetForeignCivView(greece);
        Assert.Equal("Greece", foreign.CivName);
        Assert.Equal(greece.Gold, foreign.Gold);
        Assert.False(foreign.IsCityState);
        Assert.True(view.GetForeignCivView(geneva).IsCityState);
        Assert.Same(game.Ruleset, foreign.Ruleset);
        Assert.Same(greece, foreign.GetCiv());
        Assert.Same(greece.Nation, foreign.GetNation());
        Assert.Equal(greece.Nation.Style, foreign.GetStyle());
        Assert.Equal(greece.Nation.GetStyleOrCivName(), foreign.GetStyleOrCivName());
        Assert.Equal(greece.Nation.GetOuterColor(), foreign.GetOuterColor());
        Assert.Equal(greece.Nation.GetInnerColor(), foreign.GetInnerColor());
        Assert.Same(greece.GetEra(), foreign.GetEra());
        Assert.False(foreign.IsBarbarian());
        Assert.True(foreign.IsMajorCiv());
        Assert.Equal(greece.IsDefeated(), foreign.IsDefeated());
        Assert.Equal(0, foreign.GetGoldPerTurn());
        greece.Stats.StatsForNextTurn.Gold = -7.9f;
        Assert.Equal(-7, foreign.GetGoldPerTurn()); // Kotlin Float.toInt() truncates toward zero
        Assert.Same(greece.GetPerTurnResourcesWithOriginsForTrade().GetType(), foreign.GetPerTurnResourcesWithOriginsForTrade().GetType());

        Assert.Equal(babylon.Knows(greece), view.CivView.Knows(foreign));
        Assert.Equal(babylon.GetKnownCivs().Select(it => it.CivName), view.CivView.GetKnownCivs().Select(it => it.CivName));
        Assert.Equal(greece.Knows(babylon), foreign.Knows(view.CivView));
    }

    [Fact]
    public void ForeignCivView_AfterMeeting_KnowsAndWarQueries()
    {
        var view = ViewAs(babylon);
        var foreign = view.GetForeignCivView(greece);
        Assert.True(view.CivView.Knows(foreign));
        Assert.True(foreign.Knows(view.CivView));
        Assert.Contains("Greece", view.CivView.GetKnownCivs().Select(it => it.CivName));
        var manager = view.CivView.GetDiplomacyManagerWith(foreign);
        Assert.NotNull(manager);
        Assert.Equal(babylon.GetDiplomacyManager(greece)!.CanDeclareWar(), manager.CanDeclareWar());
        Assert.Same(view, manager.GameView);
        Assert.Equal(babylon.IsAtWarWith(greece), view.CivView.IsAtWarWith(foreign));

        babylon.GetDiplomacyManager(greece)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        greece.GetDiplomacyManager(babylon)!.DiplomaticStatus = Unciv.Logic.Civilization.Diplomacy.DiplomaticStatus.War;
        Assert.True(view.CivView.IsAtWarWith(foreign));
        Assert.Contains("Greece", view.CivView.GetCivsAtWarWith().Select(it => it.CivName));
        Assert.True(view.CivView.IsAtWar());
        Assert.Equal(babylon.DiplomacyFunctions.GetResearchAgreementCost(greece), view.CivView.GetResearchAgreementCost(foreign));
    }

    [Fact]
    public void CivView_DataRetrieval_MirrorsTheCivilization()
    {
        var view = ViewAs(babylon);
        var civView = view.CivView;
        Assert.Equal(babylon.IsHuman(), civView.IsHuman());
        Assert.Equal(babylon.IsSpectator(), civView.IsSpectator());
        Assert.Equal(babylon.IsCurrentPlayer(), civView.IsCurrentPlayer());
        Assert.Equal(babylon.GetHappiness(), civView.GetHappiness());
        Assert.Equal(game.IsReligionEnabled(), civView.IsReligionEnabled());
        Assert.Equal(babylon.GetResourceAmount("Iron"), civView.GetResourceAmount("Iron"));
        Assert.Equal(babylon.HasStatToBuy(Unciv.Models.StatTypes.Stat.Gold, 1), civView.HasStatToBuy(Unciv.Models.StatTypes.Stat.Gold, 1));
        Assert.Equal(babylon.ShouldOpenTechPicker(), civView.ShouldOpenTechPicker());
        Assert.Equal(babylon.MayVoteForDiplomaticVictory(), civView.MayVoteForDiplomaticVictory());
        Assert.Equal(babylon.Tech.CanResearchTech(), civView.CanResearchTech());
        Assert.Equal(babylon.Tech.CurrentTechnologyName(), civView.CurrentTechnologyName());
        Assert.Equal(babylon.Tech.ResearchedTechnologies.Count != 0, civView.HasResearchedAnyTech());
        Assert.False(civView.HasAdoptedPolicies());
        Assert.Equal(babylon.Policies.CanAdoptPolicy(), civView.CanAdoptPolicy());
        Assert.Equal(babylon.EspionageManager.ShouldShowMoveSpies(), civView.ShouldShowMoveSpies());
        Assert.Equal(babylon.VictoryManager.GetAvailableVictories().Count, civView.GetAvailableVictories().Count);
        Assert.Equal(babylon.VictoryManager.GetVictoriesShownInVictoryScreen().Count, civView.GetVictoriesShownInVictoryScreen().Count);
        Assert.Same(babylon.GreatPeople.GreatPersonPointsCounter, civView.GetGreatPersonPointsCounter());
        Assert.Same(babylon.GreatPeople.GreatGeneralPointsCounter, civView.GetGreatGeneralPointsCounter());
        Assert.Same(babylon.GreatPeople.PointsForNextGreatGeneralCounter, civView.GetPointsForNextGreatGeneralCounter());
        Assert.Equal(0, civView.GetGreatPersonPoints("Great Scientist"));
        Assert.Equal(babylon.GreatPeople.GetPointsRequiredForGreatPerson("Great Scientist"), civView.GetPointsRequiredForGreatPerson("Great Scientist"));
        Assert.NotNull(civView.GetGreatPersonPointsForNextTurn());
        Assert.NotNull(civView.GetStatMapForNextTurn());
        Assert.NotNull(civView.GetHappinessBreakdown());
        Assert.NotNull(civView.CalculateScoreBreakdown());
        Assert.NotNull(civView.GetCivResourcesByName());
        Assert.Equal(babylon.Tech.GoldPercentConvertedToScience, civView.GetGoldPercentConvertedToScience());
        Assert.Equal(babylon.HasUnique(Unciv.Models.Ruleset.Unique.UniqueType.OneTimeFreeUnit), civView.HasUnique(Unciv.Models.Ruleset.Unique.UniqueType.OneTimeFreeUnit));
        Assert.Equal(babylon.GetMatchingUniques(Unciv.Models.Ruleset.Unique.UniqueType.OneTimeFreeUnit).Count(),
            civView.GetMatchingUniques(Unciv.Models.Ruleset.Unique.UniqueType.OneTimeFreeUnit).Count());
        Assert.Same(ruleset.Technologies["Pottery"], civView.TechnologyByName("Pottery"));
        Assert.Null(civView.TechnologyByName("No such tech"));
        Assert.Null(civView.TechnologyByName(null));
        Assert.False(civView.IsResearched("Pottery"));
        Assert.NotNull(civView.TurnsToTech("Pottery"));
        Assert.Equal(babylon.GetPersonality()[PersonalityValue.Production], civView.GetPersonalityValue(PersonalityValue.Production));
    }

    [Fact]
    public void CivView_Religion_ReflectsReligionState()
    {
        var civView = ViewAs(babylon).CivView;
        Assert.Null(civView.GetReligion());
        Assert.False(civView.IsFoundingReligion());
        Assert.False(civView.IsEnhancingReligion());
        Assert.False(civView.HasFreeBeliefs());
        Assert.Equal(babylon.ReligionManager.CanFoundOrExpandPantheon(), civView.CanFoundPantheon()); // state None, not Pantheon
        Assert.False(civView.CanExpandPantheon());
        Assert.NotNull(civView.GetBeliefsToChooseAtFounding());
        Assert.NotNull(civView.GetBeliefsToChooseAtEnhancing());
        Assert.NotNull(civView.FreeBeliefsAsEnums());
    }

    [Fact]
    public void CivView_IsMapRevealed_NeedsADefeatedCurrentPlayerInOfflineGames()
    {
        var civView = ViewAs(babylon).CivView;
        Assert.False(civView.IsMapRevealed()); // alive
        foreach (var unit in babylon.Units.GetCivUnits().ToList()) unit.Destroy();
        Assert.True(babylon.IsDefeated());
        Assert.True(civView.IsMapRevealed());
        Assert.False(ViewAs(babylon).GetForeignCivView(greece).IsDefeated());
        game.GameParameters.IsOnlineMultiplayer = true;
        Assert.False(civView.IsMapRevealed());
        Assert.True(ViewAs(babylon).GetTile(Tile(30)).IsForceVisible() == false);
    }

    [Fact]
    public void CivView_Actions_MutateTheCivilization_AndReportSuccess()
    {
        var civView = ViewAs(babylon).CivView;
        Assert.False(civView.IsCivConstructionDisabled("Granary"));
        Assert.True(civView.TryDisableCivConstruction("Granary"));
        Assert.True(civView.IsCivConstructionDisabled("Granary"));
        Assert.Contains("Granary", babylon.DisabledCityConstructions);
        Assert.True(civView.TryEnableCivConstruction("Granary"));
        Assert.False(civView.IsCivConstructionDisabled("Granary"));
        Assert.True(civView.TryEnableCivConstruction("Never disabled"));

        Assert.True(civView.TrySetGoldPercentConvertedToScience(0.25f));
        Assert.Equal(0.25f, babylon.Tech.GoldPercentConvertedToScience);
        Assert.Equal(0.25f, civView.GetGoldPercentConvertedToScience());

        babylon.Policies.ShouldOpenPolicyPicker = true;
        Assert.True(civView.TryDismissPolicyPicker());
        Assert.False(babylon.Policies.ShouldOpenPolicyPicker);

        Assert.True(civView.TryDismissMoveSpies());
        Assert.True(babylon.EspionageManager.DismissedShouldMoveSpies);
        Assert.False(civView.ShouldShowMoveSpies());

        Assert.False(civView.HasMovedAutomatedUnitsThisTurn());
        Assert.True(civView.TryMarkMovedAutomatedUnits());
        Assert.True(civView.HasMovedAutomatedUnitsThisTurn());

        Assert.True(civView.TryUpdateAllCityStats()); // no cities: nothing to update
        Assert.True(civView.TryAutomateAllUnits()); // no units: nothing to act
    }

    [Fact]
    public void SpyView_ExposesNameRank_AndOwner()
    {
        var spy = babylon.EspionageManager.AddSpy();
        var view = ViewAs(babylon);
        var spyView = view.GetSpyView(spy);
        Assert.Equal(spy.Name, spyView.Name);
        Assert.Equal(spy.GetEffectiveRank(), spyView.GetEffectiveRank());
        Assert.True(spyView.IsOwnedByViewer());
        Assert.False(ViewAs(greece).GetSpyView(spy).IsOwnedByViewer());
        Assert.Same(view, spyView.GameView);
    }

    [Fact]
    public void TileView_SimpleGetters_MirrorTheTile()
    {
        var view = ViewAs(babylon);
        var tile = Tile(30);
        var v = view.GetTile(tile);
        Assert.Same(v, v.GetTileMap().GetTile(tile));
        Assert.Equal(tile.IsWorked(), v.IsWorked());
        Assert.Equal(tile.IsBlockaded(), v.IsBlockaded());
        Assert.Equal(tile.ProvidesYield(), v.ProvidesYield());
        Assert.Equal(tile.IsLocked(), v.IsLocked());
        Assert.Equal(tile.IsImpassible(), v.IsImpassible());
        Assert.Equal(tile.CanPillageTileImprovement(), v.CanPillageTileImprovement());
        Assert.Equal(tile.CanPillageRoad(), v.CanPillageRoad());
        Assert.Equal(tile.IsBarbarianEncampment(), v.IsBarbarianEncampment());
        Assert.Equal(tile.IsFriendlyTerritory(babylon), v.IsFriendlyTerritory());
        Assert.Equal(tile.IsAdjacentTo("Coast"), v.IsAdjacentTo("Coast"));
        Assert.Equal(tile.AerialDistanceTo(Tile(31)), v.AerialDistanceTo(view.GetTile(Tile(31))));
        Assert.Equal(tile.GetShownImprovement(babylon), v.GetShownImprovement());
        Assert.Equal(tile.TerrainFeatures, v.TerrainFeatures);
        Assert.Equal(tile.NaturalWonder, v.NaturalWonder);
        Assert.Equal(tile.RoadStatus, v.RoadStatus);
        Assert.Equal(tile.RoadIsPillaged, v.RoadIsPillaged);
        Assert.Equal(tile.ImprovementIsPillaged, v.ImprovementIsPillaged);
        Assert.Equal(tile.ImprovementInProgress, v.ImprovementInProgress);
        Assert.Equal(tile.Improvement, v.Improvement);
        Assert.Equal(tile.TurnsToImprovement, v.TurnsToImprovement);
        Assert.Equal(tile.TileHeight, v.TileHeight);
        Assert.Equal(tile.ResourceAmount, v.ResourceAmount);
        Assert.Equal(tile.IsMarkedForCreatesOneImprovement(), v.IsMarkedForCreatesOneImprovement());
        Assert.Equal(tile.HasBottomRightRiver, v.HasBottomRightRiver);
        Assert.Equal(tile.HasBottomRiver, v.HasBottomRiver);
        Assert.Equal(tile.HasBottomLeftRiver, v.HasBottomLeftRiver);
        Assert.Equal(tile.CachedTerrainData.TerrainNameSet.OrderBy(it => it, StringComparer.Ordinal), v.GetCachedTerrainNameSet().OrderBy(it => it, StringComparer.Ordinal));
        Assert.Null(v.GetOwner());
        Assert.Null(v.GetRoadOwner());
        Assert.Same(view.CivView, v.GetCivView());
    }

    [Fact]
    public void CivView_ImprovementQueries_MirrorTheTileFunctions()
    {
        var view = ViewAs(babylon);
        var tile = game.TileMap.TileList.First(it => it.IsLand && !it.IsImpassible());
        var tileView = view.GetTile(tile);
        var farm = ruleset.TileImprovements["Farm"];
        Assert.Equal(tile.ImprovementFunctions.CanBuildImprovement(farm, babylon.State), view.CivView.CanBuildImprovementOn(farm, tileView));
        Assert.Equal(tile.ImprovementFunctions.GetImprovementBuildingProblems(farm, babylon.State).ToList(),
            view.CivView.GetImprovementBuildingProblems(farm, tileView).ToList());
    }

    // ---- TradeView (T138) ----

    private TradeView TradeViewOf(Civilization civ, Civilization other) =>
        ViewAs(civ) is var gv ? gv.GetCivView(civ).GetTradeView(gv.GetForeignCivView(other)) : throw new InvalidOperationException();

    [Fact]
    public void TradeView_StagesProposesAndRetractsOffers()
    {
        var view = TradeViewOf(babylon, greece);
        Assert.True(view.IsOwnedByViewer());
        Assert.False(view.HasPendingOfferFromUs());
        Assert.False(view.TryProposeStagedTrade()); // nothing staged
        Assert.False(view.TryLoadOurPendingOffer());
        Assert.False(view.TryRetractOffer());

        var staged = new Trade();
        staged.OurOffers.Add(new TradeOffer(Constants.PeaceTreaty, TradeOfferType.Treaty, 1));
        Assert.True(view.SetStagedTrade(staged));
        Assert.Single(view.OurStagedOffers());
        Assert.Empty(view.TheirStagedOffers());

        Assert.True(view.TryProposeStagedTrade());
        Assert.True(view.HasPendingOfferFromUs());
        var request = Assert.Single(greece.TradeRequests);
        Assert.Equal(babylon.CivId, request.RequestingCiv);
        Assert.Empty(request.Trade.OurOffers); // reversed for the receiver
        Assert.Single(request.Trade.TheirOffers);

        // A fresh view restores the pending offer, in our orientation
        var fresh = TradeViewOf(babylon, greece);
        Assert.Empty(fresh.OurStagedOffers());
        Assert.True(fresh.TryLoadOurPendingOffer());
        Assert.Single(fresh.OurStagedOffers());
        Assert.Empty(fresh.TheirStagedOffers());

        Assert.True(fresh.TryRetractOffer());
        Assert.Empty(greece.TradeRequests);
        Assert.False(fresh.HasPendingOfferFromUs());
        Assert.False(fresh.TryRetractOffer());
    }

    [Fact]
    public void TradeView_AvailableOffersAreLiveListsOfTheLogic()
    {
        var view = TradeViewOf(babylon, greece);
        Assert.Same(view.OurAvailableOffers(), view.OurAvailableOffers());
        Assert.Same(view.TheirAvailableOffers(), view.TheirAvailableOffers());
        Assert.NotSame(view.OurAvailableOffers(), view.TheirAvailableOffers());
        view.OurStagedOffers().Add(new TradeOffer("x", TradeOfferType.Gold, 5));
        Assert.Single(view.OurStagedOffers()); // the same live instance
    }

    // ---- Structural purity (ViewPurityTest) ----

    private static IEnumerable<Type> ViewTypes() =>
        typeof(GameView).Assembly.GetTypes()
            .Where(it => it.Namespace == "Unciv.View" && it is { IsAbstract: false, IsClass: true } && !it.Name.Contains('<')
                && IsViewType(it));

    private static bool IsViewType(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(View<>)) return true;
        return false;
    }

    private static bool IsOwnedViewType(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(OwnedView<>)) return true;
        return false;
    }

    // Kotlin's ViewPurityTest `non-Readonly functions in owned Views always return Boolean` is @Ignore'd for exactly these
    // (CityView.tryRaisePriority/tryLowerPriority return Int?; tryBombard returns Battle.DamageDealt).
    private static readonly HashSet<string> KnownNonBooleanTryMembers = ["CityView.TryRaisePriority", "CityView.TryLowerPriority", "CityView.TryBombard"];

    // `TryGet...` navigation members (TryGetCityView, TryGetMapUnitView) are @Readonly in Kotlin: they only look something up.
    private static bool IsStateChanging(MethodInfo method) =>
        method.Name.StartsWith("Try", StringComparison.Ordinal) && !method.Name.StartsWith("TryGet", StringComparison.Ordinal);

    private static IEnumerable<MethodInfo> DeclaredMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(it => !it.IsSpecialName && it.Name is not ("Equals" or "GetHashCode" or "ToString"));

    [Fact]
    public void ViewTypes_AreDiscovered()
    {
        var names = ViewTypes().Select(it => it.Name).ToHashSet();
        foreach (var expected in new[] { "GameView", "CivView", "ForeignCivView", "TileView", "TileMapView", "DiplomacyManagerView", "SpyView", "TradeView",
            "ForeignCityView", "CityView", "CityConstructionsView", "ForeignMapUnitView", "MapUnitView", "MapUnitCombatantView", "CityCombatantView", "AttackableTileView" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void NonOwnedAndForeignViews_DeclareNoStateChangingMembers()
    {
        var violations = new List<string>();
        foreach (var type in ViewTypes().Where(it => !IsOwnedViewType(it) || it.Name.StartsWith("Foreign", StringComparison.Ordinal)))
        {
            violations.AddRange(DeclaredMethods(type).Where(it => IsStateChanging(it))
                .Select(it => $"{type.Name}.{it.Name} is a state-changing member of a read-only view"));
            violations.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(it => it.SetMethod is { IsPublic: true })
                .Select(it => $"{type.Name}.{it.Name} has a public setter"));
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void OwnedViews_TryMembersReturnBoolean_AndNoViewHasPublicSetters()
    {
        var violations = new List<string>();
        foreach (var type in ViewTypes())
        {
            if (IsOwnedViewType(type))
                violations.AddRange(DeclaredMethods(type).Where(it => IsStateChanging(it) && it.ReturnType != typeof(bool) && !KnownNonBooleanTryMembers.Contains($"{type.Name}.{it.Name}"))
                    .Select(it => $"{type.Name}.{it.Name} returns {it.ReturnType.Name} instead of bool"));
            violations.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(it => it.SetMethod is { IsPublic: true })
                .Select(it => $"{type.Name}.{it.Name} has a public setter"));
        }
        Assert.Empty(violations);
    }
}
