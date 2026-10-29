using Unciv.Core.Tests.Models.Ruleset.Unique;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;
using Unciv.Logic.Trade;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.Screens.VictoryScreen;
using CityStateTypeObject = Unciv.Models.Ruleset.Nation.CityStateType;
using EraObject = Unciv.Models.Ruleset.Tech.Era;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using PolicyObject = Unciv.Models.Ruleset.Policy;
using StatsObject = Unciv.Models.StatTypes.Stats;
using TechnologyObject = Unciv.Models.Ruleset.Tech.Technology;
using TileResourceObject = Unciv.Models.Ruleset.Tile.TileResource;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Logic;

// T055: Civilization, ExploredRegion, PopupAlert. The Kotlin fixtures for Civilization are TestGame-based (T035/T072); these are
// hand-derived expectations over the seam fakes in CivilizationTestFakes.cs.
public class CivilizationCoreTests
{
    // ---- PopupAlert

    [Fact]
    public void PopupAlert_KeepsTypeAndValue_AndLateinitThrowsBeforeAssignment()
    {
        var alert = new PopupAlert(AlertType.WonderBuilt, "Pyramids");
        Assert.Equal(AlertType.WonderBuilt, alert.Type);
        Assert.Equal("Pyramids", alert.Value);

        var empty = new PopupAlert();
        Assert.Throws<InvalidOperationException>(() => empty.Type);
        Assert.Throws<InvalidOperationException>(() => empty.Value);
    }

    // ---- ExploredRegion

    private static ExploredRegion Region(bool worldWrap = false, int radius = 10)
    {
        var region = new ExploredRegion();
        region.SetMapParameters(new MapParameters { Shape = MapShape.Hexagonal, MapSize = new MapSize(radius), WorldWrap = worldWrap });
        return region;
    }

    [Fact]
    public void ExploredRegion_FirstTile_FillsBothCornersAndStaysEmptyUntilThen()
    {
        var region = Region();
        Assert.True(region.IsEmpty());
        // x=3, y=1 -> longitude 2, latitude 4
        region.CheckTilePosition(HexCoord.Of(3, 1), null);
        Assert.False(region.IsEmpty());
        Assert.True(region.IsPositionInRegion(HexCoord.Of(3, 1)));
        Assert.False(region.IsPositionInRegion(HexCoord.Of(4, 1)));
        // Kotlin quirk: a single tile has topLeft.x == bottomRight.x, which takes the "crossed the map edge" branch: 2 * mapRadius + 1
        Assert.Equal(21, region.GetWidth());
        Assert.Equal(1, region.GetHeight());
        Assert.True(region.ShouldRecalculateCoords()); // nothing recalculated yet
    }

    [Fact]
    public void ExploredRegion_Expands_AndFlagsRecalculation()
    {
        var region = Region();
        region.CheckTilePosition(HexCoord.Of(3, 1), null); // long 2, lat 4
        region.CalculateStageCoords(1000f, 1000f);
        Assert.False(region.ShouldRecalculateCoords());

        region.CheckTilePosition(HexCoord.Of(3, 1), null); // inside the region: no change
        Assert.False(region.ShouldRecalculateCoords());

        region.CheckTilePosition(HexCoord.Of(6, 0), null); // long 6, lat 6: grows to the east and north
        Assert.True(region.ShouldRecalculateCoords());
        Assert.True(region.ShouldUpdateMinimap());
        Assert.Equal(5, region.GetWidth()); // 6 - 2 + 1
        Assert.Equal(3, region.GetHeight()); // 6 - 4 + 1
        Assert.True(region.IsPositionInRegion(HexCoord.Of(5, 1))); // long 4, lat 6
        Assert.False(region.IsPositionInRegion(HexCoord.Of(1, 0))); // long 1, lat 1

        region.CheckTilePosition(HexCoord.Of(0, 3), null); // long -3, lat 3: grows to the west and south
        Assert.Equal(10, region.GetWidth()); // 6 - (-3) + 1
        Assert.Equal(4, region.GetHeight()); // 6 - 3 + 1
    }

    [Fact]
    public void ExploredRegion_GetMinimapLeft_ClearsTheMinimapFlag()
    {
        var region = Region();
        region.CheckTilePosition(HexCoord.Of(3, 1), null); // long 2
        Assert.True(region.ShouldUpdateMinimap());
        Assert.Equal((2f + 1f) * 10f * -0.75f, region.GetMinimapLeft(10f));
        Assert.False(region.ShouldUpdateMinimap());
    }

    [Fact]
    public void ExploredRegion_Clone_CopiesTheCorners()
    {
        var region = Region();
        region.CheckTilePosition(HexCoord.Of(3, 1), null);
        region.CheckTilePosition(HexCoord.Of(6, 0), null);
        var clone = region.Clone();
        Assert.False(clone.IsEmpty());
        Assert.Equal(region.GetWidth(), clone.GetWidth());
        Assert.Equal(region.GetHeight(), clone.GetHeight());
        Assert.True(Region().Clone().IsEmpty());
    }

    [Fact]
    public void ExploredRegion_WorldWrap_EastEdgeWrapsToTheWestEdge()
    {
        var region = Region(worldWrap: true, radius: 10);
        region.CheckTilePosition(HexCoord.Of(8, 0), null); // long 8, lat 8
        region.CheckTilePosition(HexCoord.Of(10, 0), null); // long 10 == mapRadius: wraps to -10
        // topLeft.x = -10 < bottomRight.x = 8 -> the vectors are now swapped along the x-axis
        Assert.True(region.IsPositionInRegion(HexCoord.Of(10, 0)) || region.IsPositionInRegion(HexCoord.Of(-9, 0)));
        // width of a crossed region is mapRadius * 2 - (bottomRight.x - topLeft.x) = 20 - 18, plus one
        Assert.Equal(3, region.GetWidth());
    }

    [Fact]
    public void ExploredRegion_CalculateStageCoords_ComputesTheRectangle()
    {
        var region = Region();
        region.CheckTilePosition(HexCoord.Of(0, 0), null); // long 0, lat 0
        region.CalculateStageCoords(1000f, 800f);
        // tileRadius = 50 * 0.8 = 40; world of (0,0) is (-0, 0), centre x = 500, centre y = 400 (hexagonal map, odd width)
        Assert.Equal(500f, region.GetLeftX());
        Assert.Equal(500f, region.GetRightX());
        Assert.Equal(400f, region.GetTopY());
        Assert.Equal(400f, region.GetBottomY());
        Assert.True(region.ShouldRestrictX()); // right - left != 1
        float yOffset = 40f * (float)System.Math.Sqrt(3f) * 0.5f;
        var rectangle = region.GetRectangle();
        Assert.Equal(500f - 40f, rectangle.X);
        Assert.Equal(800f - 400f - yOffset * 0.5f, rectangle.Y);
        Assert.Equal(21 * 40f * 1.5f, rectangle.Width); // see the width quirk above
        Assert.Equal(1 * yOffset, rectangle.Height);
    }

    // ---- Civilization

    private static (Civilization Civ, FakeGameInfoState Info) NewCiv(string name = "Rome", PlayerType playerType = PlayerType.Human)
    {
        var info = new FakeGameInfoState();
        info.Map = new TileMap();
        var civ = new Civilization(new NationObject { Name = name }) { GameInfo = info, PlayerType = playerType };
        civ.Tech.CivInfo = civ;
        civ.Policies.CivInfo = civ;
        civ.GoldenAges.CivInfo = civ;
        civ.GreatPeople.CivInfo = civ;
        civ.CityStateFunctions = new FakeCityStateFunctions();
        info.Civs.Add(civ);
        return (civ, info);
    }

    [Fact]
    public void CityStateType_RetriesAfterAFailedFirstRead()
    {
        var civ = new Civilization(new NationObject { Name = "Geneva", CityStateType = "Cultured" });
        Assert.Throws<InvalidOperationException>(() => civ.CityStateType); // gameInfo not set yet
        var info = new FakeGameInfoState();
        var type = new CityStateTypeObject { Name = "Cultured" };
        info.RulesetData.CityStateTypes.Put("Cultured", type);
        civ.GameInfo = info;
        Assert.Same(type, civ.CityStateType);
    }

    [Fact]
    public void Constructors_SetNameAndId()
    {
        var civ = new Civilization("Rome");
        Assert.Equal("Rome", civ.CivName);
        Assert.Equal("Rome", civ.CivId);
        var civ2 = new Civilization("Rome", "id-1");
        Assert.Equal("Rome", civ2.CivName);
        Assert.Equal("id-1", civ2.CivId);
        var civ3 = new Civilization(new NationObject { Name = "Greece" }, "g1");
        Assert.Equal("Greece", civ3.CivName);
        Assert.Equal("g1", civ3.CivId);
        Assert.Equal("Greece", civ3.Nation.Name);
    }

    [Fact]
    public void LateinitMembers_ThrowBeforeAssignment()
    {
        var civ = new Civilization("Rome");
        Assert.Throws<InvalidOperationException>(() => civ.GameInfo);
        Assert.Throws<InvalidOperationException>(() => civ.Nation);
        Assert.Throws<InvalidOperationException>(() => civ.Tech.CivInfo);
        Assert.NotNull(civ.Units);
        Assert.Same(civ, civ.Units.CivInfo);
        Assert.Same(GameContext.IgnoreConditionalsState, civ.GetGameContextForStartBias());
        civ.GameInfo = new FakeGameInfoState();
        Assert.Same(civ.GameInfo, civ.GetGameContextForStartBias().GameInfo);
    }

    [Theory]
    [InlineData(100, 50, 150)]
    [InlineData(int.MaxValue - 5, 10, int.MaxValue)]
    [InlineData(int.MaxValue, 1, int.MaxValue)]
    [InlineData(int.MinValue + 5, -10, int.MinValue)]
    [InlineData(-5, -7, -12)]
    [InlineData(0, int.MaxValue, int.MaxValue)]
    public void AddGold_ClampsAtTheIntLimits(int start, int delta, int expected)
    {
        var (civ, _) = NewCiv();
        civ.AddGold(start);
        civ.AddGold(delta);
        // a start beyond what one call can add: AddGold(int.MinValue + 5) is exact, so the sum is start + delta clamped
        Assert.Equal(expected, civ.Gold);
    }

    [Fact]
    public void AddNotification_IsIgnoredForAI_AndRecordedForHumans()
    {
        var (human, _) = NewCiv("Rome", PlayerType.Human);
        human.AddNotification("Hello", NotificationCategory.Trade, NotificationIcon.Gold);
        human.AddNotification("There", HexCoord.Of(1, 2), NotificationCategory.War);
        human.AddNotification("Two", [new LocationAction(HexCoord.Of(3, 4)), new LocationAction(HexCoord.Of(5, 6))], NotificationCategory.General);
        Assert.Equal(3, human.Notifications.Count);
        Assert.Equal("Hello", human.Notifications[0].Text);
        Assert.Equal(NotificationCategory.Trade, human.Notifications[0].Category);
        Assert.Equal([NotificationIcon.Gold], human.Notifications[0].Icons);
        Assert.Empty(human.Notifications[0].Actions);
        Assert.Single(human.Notifications[1].Actions);
        Assert.Equal(2, human.Notifications[2].Actions.Count);

        var (ai, _) = NewCiv("Egypt", PlayerType.AI);
        ai.AddNotification("Ignored", NotificationCategory.General);
        Assert.Empty(ai.Notifications);
    }

    [Fact]
    public void Flags_AreStoredByName()
    {
        var (civ, _) = NewCiv();
        Assert.False(civ.HasFlag("x"));
        Assert.Null(civ.GetTurnsTillNextDiplomaticVote());
        civ.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 7);
        civ.AddFlag(CivFlags.RecentlyBullied.ToString(), 3);
        Assert.Equal(7, civ.GetTurnsTillNextDiplomaticVote());
        Assert.Equal(3, civ.GetRecentBullyingCountdown());
        Assert.Null(civ.GetTurnsTillCallForBarbHelp());
        civ.RemoveFlag(CivFlags.RecentlyBullied.ToString());
        Assert.Null(civ.GetRecentBullyingCountdown());
        civ.AddFlag(CivFlags.TurnsTillNextDiplomaticVote.ToString(), 0);
        Assert.Equal(0, civ.GetTurnsTillNextDiplomaticVote());
    }

    [Fact]
    public void GetTurnsBetweenDiplomaticVotes_TruncatesTheSpeedModifier()
    {
        var (civ, info) = NewCiv();
        info.SpeedData.Modifier = 1.5f;
        Assert.Equal(22, civ.GetTurnsBetweenDiplomaticVotes()); // 22.5 -> 22
        info.SpeedData.Modifier = 0.5f;
        Assert.Equal(7, civ.GetTurnsBetweenDiplomaticVotes());
    }

    [Fact]
    public void Proximity_DefaultsToNone()
    {
        var (civ, _) = NewCiv();
        Assert.Equal(Proximity.None, civ.GetProximity("Egypt"));
        civ.Proximity["Egypt"] = Proximity.Close;
        Assert.Equal(Proximity.Close, civ.GetProximity("Egypt"));
    }

    [Fact]
    public void DiplomacyLookups_KnowsAndGetKnownCivs()
    {
        var (rome, info) = NewCiv("Rome");
        var egypt = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = info };
        egypt.Units = new TestUnitManager(egypt) { Count = 1 };
        var spectator = new Civilization(new NationObject { Name = "Spectator" }) { GameInfo = info };
        spectator.Units = new TestUnitManager(spectator) { Count = 1 };
        var dead = new Civilization(new NationObject { Name = "Greece" }) { GameInfo = info };
        dead.Units = new TestUnitManager(dead) { Count = 0 };
        rome.Units = new TestUnitManager(rome) { Count = 1 };
        foreach (var other in new[] { egypt, spectator, dead })
            rome.Diplomacy[other.CivId] = new DiplomacyManager(rome, other);

        Assert.True(rome.Knows(egypt));
        Assert.True(rome.Knows("Greece"));
        Assert.False(rome.Knows("Persia"));
        Assert.Null(rome.GetDiplomacyManager("Persia"));
        Assert.Same(rome.Diplomacy["Egypt"], rome.GetDiplomacyManager(egypt));
        // defeated civs (no units) and, by default, spectators are not listed
        Assert.Equal(["Egypt"], rome.GetKnownCivs().Select(it => it.CivId));
        // java.util.HashMap order of the keys: "Greece" (bucket 0), "Spectator" (6), "Egypt" (7)
        Assert.Equal(["Spectator", "Egypt"], rome.GetKnownCivsWithSpectators().Select(it => it.CivId));
        var visited = new List<string>();
        rome.ForEachKnownCiv(true, it => visited.Add(it.CivId));
        Assert.Equal(["Spectator", "Egypt"], visited);
        visited.Clear();
        rome.ForEachKnownCiv(it => visited.Add(it.CivId));
        Assert.Equal(["Egypt"], visited);
    }

    [Fact]
    public void GetDiplomacyManagerOrMeet_MeetsOnlyWhenUnknown()
    {
        var (rome, info) = NewCiv("Rome");
        var egypt = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = info };
        Assert.Null(rome.GetDiplomacyManager(egypt));
        var manager = rome.GetDiplomacyManagerOrMeet(egypt);
        Assert.Same(manager, rome.GetDiplomacyManagerOrMeet(egypt));
        Assert.Equal(DiplomaticStatus.Peace, manager.DiplomaticStatus);
        Assert.Equal(DiplomaticStatus.Peace, egypt.GetDiplomacyManager(rome)!.DiplomaticStatus);
        // meeting happened once, from both sides
        Assert.Equal(AlertType.FirstContact, Assert.Single(egypt.PopupAlerts).Type);
        Assert.Equal("Rome", egypt.PopupAlerts[0].Value);
        Assert.Equal("Egypt", Assert.Single(rome.PopupAlerts).Value);
    }

    [Fact]
    public void MatchesSingleFilter_PlayerTypesAndRelations()
    {
        var (rome, info) = NewCiv("Rome", PlayerType.Human);
        var egypt = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = info, PlayerType = PlayerType.AI };

        Assert.True(rome.MatchesSingleFilter(Constants.HumanPlayer, null));
        Assert.False(rome.MatchesSingleFilter(Constants.AiPlayer, null));
        Assert.True(egypt.MatchesSingleFilter(Constants.AiPlayer, null));

        // Relations are read from the *other* civ (state.civInfo), and are false without one
        var stateOfEgypt = GameContext.ForCiv(egypt);
        Assert.False(rome.MatchesSingleFilter("Open Borders", null));
        Assert.False(rome.MatchesSingleFilter("Friendly", null));
        Assert.False(rome.MatchesSingleFilter("Hostile", null));
        Assert.False(rome.MatchesSingleFilter("Known", null));

        Assert.False(rome.MatchesSingleFilter("Open Borders", stateOfEgypt));
        var egyptSeesRome = new DiplomacyManager(egypt, rome) { HasOpenBorders = true, DiplomaticStatus = DiplomaticStatus.Peace };
        egyptSeesRome.SetModifier(DiplomaticModifiers.DeclarationOfFriendship, 40f); // opinion 40 of a human: Friend
        egypt.Diplomacy["Rome"] = egyptSeesRome;
        Assert.True(rome.MatchesSingleFilter("Open Borders", stateOfEgypt));
        Assert.True(rome.MatchesSingleFilter("Friendly", stateOfEgypt));
        egyptSeesRome.SetModifier(DiplomaticModifiers.DeclarationOfFriendship, 39f);
        Assert.False(rome.MatchesSingleFilter("Friendly", stateOfEgypt));
        Assert.True(rome.MatchesSingleFilter("Friendly", GameContext.ForCiv(rome))); // a civ is friendly to itself
        Assert.True(rome.MatchesSingleFilter("Known", GameContext.ForCiv(rome)));
        Assert.False(rome.MatchesSingleFilter("Known", stateOfEgypt));
        var romeSeesEgypt = new DiplomacyManager(rome, egypt) { DiplomaticStatus = DiplomaticStatus.Peace };
        rome.Diplomacy["Egypt"] = romeSeesEgypt;
        Assert.True(rome.MatchesSingleFilter("Known", stateOfEgypt));
        Assert.False(rome.MatchesSingleFilter("Hostile", stateOfEgypt));
        romeSeesEgypt.DiplomaticStatus = DiplomaticStatus.War;
        Assert.True(rome.MatchesSingleFilter("Hostile", stateOfEgypt));
    }

    [Fact]
    public void MatchesFilter_UsesMultiFilterAndNationFilters()
    {
        var (rome, _) = NewCiv("Rome", PlayerType.Human);
        var state = GameContext.ForCiv(rome);
        Assert.True(rome.MatchesFilter(Constants.HumanPlayer));
        Assert.False(rome.MatchesFilter(Constants.AiPlayer));
        Assert.True(rome.MatchesFilter("non-[" + Constants.AiPlayer + "]", state));
        Assert.True(rome.MatchesFilter("{" + Constants.HumanPlayer + "} {Rome}", state));
        Assert.False(rome.MatchesFilter("{" + Constants.HumanPlayer + "} {Egypt}", state));
        // multiFilter = false hands the whole text to the single filter
        Assert.False(rome.MatchesFilter("non-[" + Constants.AiPlayer + "]", state, false));
    }

    [Fact]
    public void AddStat_RoutesToTheManagers()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.Technologies.Put("Writing", new TechnologyObject { Name = "Writing", Cost = 50 });
        var policies = civ.Policies;
        var tech = civ.Tech;
        var religion = new ReligionManager();
        civ.ReligionManager = religion;
        civ.Stats.Happiness = 9;

        civ.AddStat(Stat.Culture, 5);
        civ.AddStat(Stat.Culture, -2);
        civ.AddStat(Stat.Science, 7);
        civ.AddStat(Stat.Gold, 11);
        civ.AddStat(Stat.Faith, 4);
        civ.AddStat(Stat.Faith, -1);
        civ.AddStat(Stat.Food, 100); // ignored
        civ.AddStat(Stat.Happiness, 100); // ignored

        Assert.Equal(3, policies.StoredCulture);
        Assert.Equal(5, civ.TotalCultureForContests); // only positive amounts count
        Assert.Equal(7, tech.GetOverflowScience()); // no current technology: stored as overflow
        Assert.Equal(11, civ.Gold);
        Assert.Equal(3, religion.StoredFaith);
        Assert.Equal(4, civ.TotalFaithForContests);

        Assert.Equal(3, civ.GetStatReserve(Stat.Culture));
        Assert.Equal(11, civ.GetStatReserve(Stat.Gold));
        Assert.Equal(3, civ.GetStatReserve(Stat.Faith));
        Assert.Equal(9, civ.GetStatReserve(Stat.Happiness));
        Assert.Equal(0, civ.GetStatReserve(Stat.Food));
        Assert.Equal(0, civ.GetStatReserve(Stat.Science)); // no current technology
        tech.TechsToResearch.Add("Writing");
        tech.TechsInProgress["Writing"] = 42;
        Assert.Equal(42, civ.GetStatReserve(Stat.Science));
    }

    [Fact]
    public void AddStats_TruncatesFractionalAmounts()
    {
        var (civ, _) = NewCiv();
        civ.AddStats(new StatsObject(gold: 2.9f, culture: -0.5f));
        Assert.Equal(2, civ.Gold);
    }

    [Fact]
    public void AddGameResource_And_GetGameResource_CoverStatsSubStatsAndResources()
    {
        var (civ, _) = NewCiv();
        civ.AddGameResource(Stat.Gold, 8);
        civ.AddGameResource(SubStat.GoldenAgePoints, 5);
        civ.AddGameResource(Stat.Culture, 3);
        Assert.Equal(8, civ.GetGameResource(Stat.Gold));
        Assert.Equal(5, civ.GetGameResource(SubStat.GoldenAgePoints));
        Assert.Equal(3, civ.GetGameResource(Stat.Culture));
        Assert.Throws<Exception>(() => civ.GetGameResource(SubStat.StoredFood));

        var stockpiled = new TileResourceObject { Name = "Oil", Uniques = [UniqueType.Stockpiled.Text()] };
        var cityWide = new TileResourceObject { Name = "Coal", Uniques = [UniqueType.Stockpiled.Text(), UniqueType.CityResource.Text()] };
        civ.AddGameResource(stockpiled, 4);
        civ.AddGameResource(cityWide, 6); // city-wide resources are not stockpiled civ-wide
        Assert.Equal(4, civ.ResourceStockpiles["Oil"]);
        Assert.Equal(0, civ.ResourceStockpiles["Coal"]);
        Assert.Equal(4, civ.GetResourceAmount("Oil"));
        Assert.Equal(4, civ.GetResourceAmount(stockpiled));
        Assert.Equal(4, civ.GetGameResource(stockpiled));
        Assert.True(civ.HasResource("Oil"));
        Assert.False(civ.HasResource("Iron"));
    }

    [Fact]
    public void TileResourceMatchesFilter_ByNameTagAndContext()
    {
        var (civ, _) = NewCiv();
        var oil = new TileResourceObject { Name = "Oil", Uniques = [UniqueType.Stockpiled.Text()] };
        Assert.True(oil.MatchesFilter("Oil"));
        Assert.False(oil.MatchesFilter("Iron"));
        Assert.False(oil.MatchesFilter("Iron", new GameContext(civ)));
        Assert.True(oil.MatchesFilter("Oil", new GameContext(civ)));
        Assert.True(oil.MatchesFilter("{Oil} {Oil}"));
        Assert.False(oil.MatchesFilter("{Iron} {Oil}", new GameContext(civ)));
    }

    [Fact]
    public void GetResourceAmount_FallsBackToTheSupplyList()
    {
        var (civ, _) = NewCiv();
        var iron = new TileResourceObject { Name = "Iron" };
        civ.SummarizedCivResourceSupply.Add(iron, "Tiles", 3);
        Assert.Equal(3, civ.GetResourceAmount("Iron"));
        Assert.Equal(3, civ.GetResourceAmount(iron));
        Assert.Equal(3, ((IGameContextCivilization)civ).GetCivResourceSupplyAmount("Iron"));
        civ.ResourceStockpiles["Iron"] = 10; // a non-zero stockpile wins by name
        Assert.Equal(10, civ.GetResourceAmount("Iron"));
    }

    [Fact]
    public void GetCivResourcesByName_ListsEveryRulesetResource()
    {
        var (civ, info) = NewCiv();
        var iron = new TileResourceObject { Name = "Iron" };
        var oil = new TileResourceObject { Name = "Oil" };
        info.RulesetData.TileResources.Put("Iron", iron);
        info.RulesetData.TileResources.Put("Oil", oil);
        civ.SummarizedCivResourceSupply.Add(iron, "Tiles", 2);
        civ.ResourceStockpiles["Oil"] = 5;
        var byName = civ.GetCivResourcesByName();
        Assert.Equal(2, byName.Count);
        Assert.Equal(2, byName["Iron"]);
        Assert.Equal(5, byName["Oil"]);
    }

    [Fact]
    public void IsDefeated_FollowsTheCapitalAndUnitRules()
    {
        var (civ, _) = NewCiv();
        civ.Units = new TestUnitManager(civ) { Count = 0 };
        Assert.True(civ.IsDefeated()); // never owned an original capital: alive while it has units
        Assert.False(civ.IsAlive());
        civ.Units = new TestUnitManager(civ) { Count = 2 };
        Assert.False(civ.IsDefeated());
        civ.HasEverOwnedOriginalCapital = true;
        Assert.True(civ.IsDefeated()); // ... otherwise while it has cities
        civ.Cities = [new FakeCityState("c1", new FakeCiv("Rome"))];
        Assert.False(civ.IsDefeated());

        var barbarians = new Civilization(new NationObject { Name = Constants.Barbarians });
        Assert.False(barbarians.IsDefeated());
        var spectator = new Civilization(new NationObject { Name = Constants.Spectator });
        Assert.False(spectator.IsDefeated());
    }

    [Fact]
    public void GetCapital_PrefersTheCapitalElseTheFirstCity()
    {
        var (civ, _) = NewCiv();
        var first = new FakeCityState("a", new FakeCiv("Rome"));
        var second = new FakeCityState("b", new FakeCiv("Rome"));
        civ.Cities = [first, second];
        Assert.Null(civ.GetCapital(false));
        Assert.Same(first.City, civ.GetCapital());
        second.Capital = true;
        Assert.Same(second.City, civ.GetCapital(false));
        Assert.Same(second.City, ((IGameContextCivilization)civ).GetCapital());
    }

    [Fact]
    public void GetMatchingUniques_ListsTheSourcesInKotlinOrder()
    {
        var (civ, info) = NewCiv();
        UniqueObject Make(string tag) => new("Cannot be traded", UniqueTarget.Global, tag);
        var timed = new UniqueObject("Cannot be traded <for [3] turns>", UniqueTarget.Global, "temp");
        var nation = civ.Nation;
        nation.Uniques = ["Cannot be traded"];
        var city = new FakeCityState("c", new FakeCiv("Rome"));
        city.BuiltUniques.AddUnique(Make("city"));
        civ.Cities = [city];
        civ.Policies.PolicyUniques.AddUnique(Make("policy"));
        civ.Tech.TechUniques.AddUnique(Make("tech"));
        civ.Tech.Era = new EraObject { Name = "Ancient era", EraNumber = 0, Uniques = ["Cannot be traded"] };
        civ.TemporaryUniques.Add(new TemporaryUnique(timed, 3));
        var cityStates = new FakeCityStateFunctions();
        cityStates.Provided.Add(Make("cityState"));
        civ.CityStateFunctions = cityStates;
        info.RulesetData.Beliefs["founder"] = new Belief(BeliefType.Founder) { Name = "founder", Uniques = ["Cannot be traded"] };
        var founderReligion = new Religion("Faith", info, civ, 1);
        founderReligion.AddBelief("founder");
        civ.ReligionManager = new ReligionManager { Religion = founderReligion };
        civ.CivResourcesUniqueMap.AddUnique(Make("resource"));
        info.Globals.Uniques = ["Cannot be traded"];

        // Civilization.GetMatchingUniques is a lazy sequence in the order nation, cities, policies, tech, temporary, era, city-states, religion, resources, global
        var sources = civ.GetMatchingUniques(UniqueType.CannotBeTraded).Select(it => it.SourceObjectName ?? "?").ToList();
        Assert.Equal(10, sources.Count);
        Assert.Equal(["Rome", "city", "policy", "tech"], sources.Take(4));
        Assert.Equal(["temp", "Ancient era", "cityState", "founder", "resource"], sources.Skip(4).Take(5));
        Assert.Equal(info.Globals.Name, sources[9]);
        Assert.True(civ.HasUnique(UniqueType.CannotBeTraded));
        Assert.False(civ.HasUnique(UniqueType.Stockpiled));

        var visited = new List<string>();
        civ.ForEachMatchingUnique(UniqueType.CannotBeTraded, unique => visited.Add(unique.SourceObjectName ?? "?"));
        Assert.Equal(sources, visited);
    }

    [Fact]
    public void GetTriggeredUniques_FiltersByTheTriggerConditionals()
    {
        var (civ, _) = NewCiv();
        civ.Nation.Uniques =
        [
            "Cannot be traded <upon discovering [Writing] technology>",
            "Cannot be traded <upon discovering [Currency] technology>",
            "Cannot be traded",
        ];
        var city = new FakeCityState("c", new FakeCiv("Rome"));
        city.BuiltUniques.AddUnique(new UniqueObject("Cannot be traded <upon discovering [Writing] technology>", UniqueTarget.Building, "Library"));
        civ.Cities = [city];

        Assert.Equal(3, civ.GetTriggeredUniques(UniqueType.TriggerUponResearch, GameContext.EmptyState).Count());
        Assert.Equal(2, civ.GetTriggeredUniques(UniqueType.TriggerUponResearch, GameContext.EmptyState, it => it.Params[0] == "Writing").Count());
        // ignoreCities leaves out the built buildings' uniques
        Assert.Equal(2, civ.GetTriggeredUniques(UniqueType.TriggerUponResearch, GameContext.EmptyState, _ => true, true).Count());
        Assert.Empty(civ.GetTriggeredUniques(UniqueType.TriggerUponResearch, GameContext.EmptyState, _ => false));

        var visited = new List<string>();
        civ.ForEachTriggeredUnique(UniqueType.TriggerUponResearch, GameContext.EmptyState, it => it.Params[0] == "Writing", unique => visited.Add(unique.SourceObjectName ?? "?"));
        Assert.Equal(["Rome", "Library"], visited);
        visited.Clear();
        civ.ForEachTriggeredUnique(UniqueType.TriggerUponResearch, GameContext.EmptyState, it => it.Params[0] == "Writing", true, unique => visited.Add(unique.SourceObjectName ?? "?"));
        Assert.Equal(["Rome"], visited);
    }

    [Fact]
    public void GetStatForRanking_ReadsTheManagersAndZeroForDefeated()
    {
        var (civ, info) = NewCiv();
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        civ.Stats.Happiness = 12;
        civ.Stats.StatsForNextTurn = new StatsObject(food: 2.5f, production: 3.4f);
        civ.AddGold(77);
        civ.Tech.ResearchedTechnologies = [new TechnologyObject { Name = "a" }, new TechnologyObject { Name = "b" }];
        civ.Policies.AdoptedPolicies.Add("Liberty");
        civ.Policies.AdoptedPolicies.Add("Liberty Complete");
        civ.Policies.AdoptedPolicies.Add("Tradition");
        var city = new FakeCityState("c", new FakeCiv("Rome")) { PopulationData = 4 };
        city.TileLocations.AddRange([HexCoord.Of(0, 0), HexCoord.Of(1, 0)]);
        civ.Cities = [city, new FakeCityState("d", new FakeCiv("Rome")) { PopulationData = 6 }];
        _ = info;

        Assert.Equal(77, civ.GetStatForRanking(RankingType.Gold));
        Assert.Equal(12, civ.GetStatForRanking(RankingType.Happiness));
        Assert.Equal(3, civ.GetStatForRanking(RankingType.Growth)); // 2.5 rounds half up
        Assert.Equal(3, civ.GetStatForRanking(RankingType.Production));
        Assert.Equal(10, civ.GetStatForRanking(RankingType.Population));
        Assert.Equal(2, civ.GetStatForRanking(RankingType.Territory));
        Assert.Equal(2, civ.GetStatForRanking(RankingType.Technologies));
        Assert.Equal(2, civ.GetStatForRanking(RankingType.Culture)); // the completed branch does not count

        civ.Units = new TestUnitManager(civ) { Count = 0 };
        Assert.Equal(0, civ.GetStatForRanking(RankingType.Gold)); // defeated
    }

    [Fact]
    public void MilitaryMight_IsCached_AndScalesWithGold()
    {
        var (civ, _) = NewCiv();
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        Assert.Equal(1, civ.GetStatForRanking(RankingType.Force)); // 1 * (sqrt(0) + 1) = 1
        civ.AddGold(10000);
        Assert.Equal(1, civ.GetStatForRanking(RankingType.Force)); // cached
        civ.ResetMilitaryMightCache();
        Assert.Equal(2, civ.GetStatForRanking(RankingType.Force)); // sqrt(10000) = 100 -> +100% -> 2
        civ.AddGold(int.MaxValue);
        civ.ResetMilitaryMightCache();
        Assert.Equal(2, civ.GetStatForRanking(RankingType.Force)); // the bonus is capped at 2
    }

    [Fact]
    public void CalculateScoreBreakdown_ScalesWithTheMapSize()
    {
        var (civ, info) = NewCiv();
        info.Map = new TileMap { MapParameters = { MapSize = new MapSize(10) } };
        // MapParameters.numberOfTiles of a radius 10 hexagon: 1 + 3 * 10 * 9 = 271 tiles -> modifier 1276 / 271, then (m - 1) / 3 + 1
        double modifier = 1276 / 271.0;
        modifier = (modifier - 1) / 3 + 1;
        civ.Tech.RepeatingTechsResearched = 2;
        foreach (var name in new[] { "a", "b", "c" }) civ.Tech.TechsResearched.Add(name);
        var city = new FakeCityState("c", new FakeCiv("Rome")) { PopulationData = 5 };
        civ.Cities = [city, new FakeCityState("d", new FakeCiv("Rome")) { PopulationData = 2 }];

        var breakdown = civ.CalculateScoreBreakdown();
        Assert.Equal(6, breakdown.Count);
        Assert.Equal(2 * 10 * modifier, breakdown["Cities"]);
        Assert.Equal(7 * 3 * modifier, breakdown["Population"]); // modConstants.scoreFromPopulation = 3
        Assert.Equal(0.0, breakdown["Tiles"]);
        Assert.Equal(0.0, breakdown["Wonders"]);
        Assert.Equal(12.0, breakdown["Technologies"]);
        Assert.Equal(20.0, breakdown["Future Tech"]);
        double expectedTotal = 0.0;
        foreach (var value in breakdown.Values) expectedTotal += value;
        Assert.Equal(expectedTotal, civ.CalculateTotalScore());
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        Assert.Equal((int)expectedTotal, civ.GetStatForRanking(RankingType.Score));
    }

    [Fact]
    public void AllyCiv_StoresTheCivIdAndResolvesItByName()
    {
        var (rome, info) = NewCiv("Rome");
        var egypt = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = info };
        info.Civs.Add(egypt);
        Assert.Null(rome.AllyCiv);
        rome.AllyCiv = egypt;
        Assert.Same(egypt, rome.AllyCiv);
        var clone = new Civilization(new NationObject { Name = "Rome" }) { GameInfo = info };
        // the copy resolves it through gameInfo.getCivilization(allyCivName)
        typeof(Civilization).GetField("allyCivName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(clone, "Egypt");
        Assert.Same(egypt, clone.AllyCiv);
        rome.AllyCiv = null;
        Assert.Null(rome.AllyCiv);
    }

    [Fact]
    public void LastSeenImprovement_IgnoredForAIAndSpectators()
    {
        var (human, _) = NewCiv("Rome", PlayerType.Human);
        var position = HexCoord.Of(2, 3);
        Assert.Null(human.GetLastSeenImprovement(position));
        human.SetLastSeenImprovement(position, "Farm");
        Assert.Equal("Farm", human.GetLastSeenImprovement(position));
        human.SetLastSeenImprovement(position, null);
        Assert.Null(human.GetLastSeenImprovement(position));

        var (ai, _) = NewCiv("Egypt", PlayerType.AI);
        ai.SetLastSeenImprovement(position, "Farm");
        Assert.Null(ai.GetLastSeenImprovement(position));
        Assert.Equal(0, ai.LastSeenImprovement.Count);
    }

    [Fact]
    public void PreferredVictoryTypes_UsesTheSingleAvailableVictoryElseNationAndPersonality()
    {
        var (civ, info) = NewCiv("Rome", PlayerType.Human);
        civ.Nation.PreferredVictoryType = "Scientific";
        foreach (var name in new[] { "Scientific", "Cultural", "Domination" })
            info.RulesetData.Victories[name] = new Victory { Name = name };
        civ.VictoryManager.CivInfo = civ;
        info.GameParameters.VictoryTypes = ["Scientific", "Cultural"];
        Assert.Equal(["Scientific"], civ.GetPreferredVictoryTypes()); // human: neutral personality (preferredVictoryType Neutral)
        info.GameParameters.VictoryTypes = ["Cultural"];
        Assert.Equal(["Cultural"], civ.GetPreferredVictoryTypes());
        info.GameParameters.VictoryTypes = ["Domination", "Cultural"];
        Assert.Equal([Constants.NeutralVictoryType], civ.GetPreferredVictoryTypes());
    }

    [Fact]
    public void Destroy_NotifiesLiveHumansAndClearsTrades()
    {
        var (rome, info) = NewCiv("Rome", PlayerType.Human);
        var egypt = new Civilization(new NationObject { Name = "Egypt" }) { GameInfo = info, PlayerType = PlayerType.Human };
        egypt.Units = new TestUnitManager(egypt) { Count = 1 };
        info.Civs.Add(egypt);
        rome.Units = new TestUnitManager(rome) { Count = 0 };
        info.GameParameters.IsOnlineMultiplayer = true; // no map reveal
        var romeSeesEgypt = new DiplomacyManager(rome, egypt);
        var egyptSeesRome = new DiplomacyManager(egypt, rome);
        romeSeesEgypt.Trades.Add(new Trade());
        egyptSeesRome.Trades.Add(new Trade());
        rome.Diplomacy["Egypt"] = romeSeesEgypt;
        egypt.Diplomacy["Rome"] = egyptSeesRome;
        rome.TradeRequests.Add(new TradeRequest("Egypt", new Trade()));
        egypt.TradeRequests.Add(new TradeRequest("Rome", new Trade()));
        egypt.TradeRequests.Add(new TradeRequest("Persia", new Trade()));

        rome.Destroy();

        Assert.Empty(rome.Notifications); // Rome is defeated: skipped
        var notification = Assert.Single(egypt.Notifications);
        Assert.Equal("The civilization of [Rome] has been destroyed!", notification.Text);
        Assert.Equal(["Rome", NotificationIcon.Death], notification.Icons);
        Assert.Empty(notification.Actions); // no location was given
        Assert.Empty(rome.TradeRequests);
        Assert.Equal("Persia", Assert.Single(egypt.TradeRequests).RequestingCiv);
        Assert.Empty(romeSeesEgypt.Trades);
        Assert.Empty(egyptSeesRome.Trades);
    }

    [Fact]
    public void Preview_CopiesTheLoadPreviewFields()
    {
        var (civ, _) = NewCiv("Rome", PlayerType.Human);
        civ.PlayerId = "player";
        civ.TotalTurnTimeSeconds = 5;
        civ.TurnsPlayedAsHuman = 2;
        civ.Units = new TestUnitManager(civ) { Count = 1 };
        var preview = civ.AsPreview();
        Assert.Equal("Rome", preview.CivName);
        Assert.Equal("player", preview.PlayerId);
        Assert.True(preview.IsPlayerCivilization());
        Assert.Equal(5, preview.TotalTurnTimeSeconds);
        Assert.Equal(2, preview.TurnsPlayedAsHuman);
        Assert.True(preview.IsAlive);
        Assert.Equal(60 * 24 * 3, new CivilizationInfoPreview().PlayerMinutesBeforeForceResign);
    }

    [Fact]
    public void GameProgress_And_SeamMembersDelegateToTheManagers()
    {
        var (civ, info) = NewCiv();
        info.RulesetData.Technologies.Put("Writing", new TechnologyObject { Name = "Writing" });
        info.RulesetData.Policies.Put("Liberty", new PolicyObject { Name = "Liberty" });
        civ.Tech.TechsResearched.Add("Writing");
        civ.Policies.AdoptedPolicies.Add("Liberty");
        civ.Stats.Happiness = 4;

        var progress = (IGameProgressCivilization)civ;
        Assert.Equal(0, progress.ResearchedTechnologiesCount); // the technologies list is empty in the fake
        Assert.Equal(1, progress.RulesetTechnologiesCount);
        Assert.Equal(1, progress.AdoptedPoliciesCount);
        Assert.Equal(1, progress.RulesetPoliciesCount);

        var seam = (IGameContextCivilization)civ;
        Assert.Same(info, seam.GameInfo);
        Assert.Null(new Civilization("x").GameInfoForSeam());
        Assert.True(seam.IsTechResearched("Writing"));
        Assert.True(seam.IsPolicyAdopted("Liberty"));
        Assert.Equal(4, seam.Happiness);
        Assert.Equal("Rome", seam.CivId);
        Assert.Empty(civ.NeutralRoads);
        Assert.Same(civ.NeutralRoads, seam.NeutralRoads);
        Assert.Equal(PlayerType.Human, seam.PlayerType);
    }

    [Fact]
    public void Clone_CopiesTheDataFields()
    {
        var (civ, _) = NewCiv("Rome", PlayerType.Human);
        civ.AddGold(123);
        civ.PlayerId = "pid";
        civ.LeaderTitle = "King";
        civ.CitiesCreated = 3;
        civ.NaturalWonders.Add("Everest");
        civ.AddFlag("flag", 4);
        civ.Proximity["Egypt"] = Proximity.Far;
        civ.PassableImpassables.Add("Mountain");
        civ.DisabledCityConstructions.Add("Barracks");
        civ.ResourceStockpiles["Oil"] = 8;
        civ.AttacksSinceTurnStart.Add(new Civilization.HistoricalAttackMemory("Archer", HexCoord.Of(1, 1), HexCoord.Of(2, 2)));
        civ.NotificationCountAtStartTurn = 5;
        civ.TemporaryUniques.Add(new TemporaryUnique(new UniqueObject("Cannot be traded <for [3] turns>", UniqueTarget.Global, "x"), 3));
        civ.CityStatePersonality = Unciv.Logic.Civilization.Diplomacy.CityStatePersonality.Hostile;
        civ.LastSeenImprovement[HexCoord.Of(0, 0)] = "Farm";
        civ.Policies.StoredCulture = 7;
        civ.Policies.AdoptedPolicies.Add("Liberty");
        civ.Tech.FreeTechs = 2;
        civ.Tech.TechsResearched.Add("Writing");
        civ.GoldenAges.StoredHappiness = 9;
        civ.GreatPeople.FreeGreatPeople = 3;

        var clone = civ.Clone();
        Assert.NotSame(civ, clone);
        Assert.Equal(123, clone.Gold);
        Assert.Equal("Rome", clone.CivName);
        Assert.Equal("pid", clone.PlayerId);
        Assert.Equal("King", clone.LeaderTitle);
        Assert.Equal(3, clone.CitiesCreated);
        Assert.Equal(["Everest"], clone.NaturalWonders);
        Assert.Equal(4, clone.FlagsCountdown["flag"]);
        Assert.Equal(Proximity.Far, clone.GetProximity("Egypt"));
        Assert.True(clone.PassableImpassables.Contains("Mountain"));
        Assert.True(clone.DisabledCityConstructions.Contains("Barracks"));
        Assert.Equal(8, clone.ResourceStockpiles["Oil"]);
        Assert.Equal(5, clone.NotificationCountAtStartTurn);
        Assert.Equal(CityStatePersonalityHostile, clone.CityStatePersonality);
        Assert.Equal("Farm", clone.LastSeenImprovement[HexCoord.Of(0, 0)]);
        Assert.NotSame(civ.NeutralRoads, clone.NeutralRoads); // T219 deviation: Kotlin shares the reference
        var attack = Assert.Single(clone.AttacksSinceTurnStart);
        Assert.NotSame(civ.AttacksSinceTurnStart[0], attack);
        Assert.Equal("Archer", attack.AttackingUnit);
        Assert.Equal(HexCoord.Of(2, 2), attack.Target);
        var temporary = Assert.Single(clone.TemporaryUniques);
        Assert.NotSame(civ.TemporaryUniques[0], temporary);
        Assert.Equal(3, temporary.TurnsLeft);
        Assert.NotSame(civ.Policies, clone.Policies);
        Assert.Equal(7, clone.Policies.StoredCulture);
        Assert.True(clone.Policies.IsAdopted("Liberty"));
        Assert.NotSame(civ.Tech, clone.Tech);
        Assert.Equal(2, clone.Tech.FreeTechs);
        Assert.True(clone.Tech.IsResearched("Writing"));
        Assert.Equal(9, clone.GoldenAges.StoredHappiness);
        Assert.Equal(3, clone.GreatPeople.FreeGreatPeople);
        Assert.Same(clone, clone.CivConstructions.CivInfo); // setTransients(clone)
    }

    private const Unciv.Logic.Civilization.Diplomacy.CityStatePersonality CityStatePersonalityHostile = Unciv.Logic.Civilization.Diplomacy.CityStatePersonality.Hostile;


}

internal static class CivilizationTestExtensions
{
    /// <summary>The seam view of <c>GameInfo</c>, which is <c>null</c> while it is not set.</summary>
    public static IGameContextGameInfo? GameInfoForSeam(this Civilization civ) => ((IGameContextCivilization)civ).GameInfo;
}
