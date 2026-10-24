using Unciv.Core.Tests.Logic.Map;
using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Nation;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic;

// T078: BarbarianManager / BarbarianEncampment. There are no Kotlin tests; the expected values follow from the Kotlin source (the RNG streams are
// covered by the GameRandom/Randomness tests, so the tests pin the branches and the arithmetic around the draws).
public class BarbarianManagerTests
{
    private sealed record World(FakeGameInfoState Info, TileMap Map, Civilization Barbarians, Civilization Rome, BarbarianManager Manager);

    private static World NewWorld(int width = 5, int height = 5, Action<Ruleset>? configure = null)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        ruleset.Terrains.Put("Coast", new Terrain { Name = "Coast", Type = TerrainType.Water });
        ruleset.TileImprovements.Put(Constants.BarbarianEncampment, new TileImprovement { Name = Constants.BarbarianEncampment });
        ruleset.Difficulties.Put("Prince", new Difficulty { Name = "Prince", BarbarianSpawnDelay = 0 });
        MapUnitFactory.AddUnitTypes(ruleset);
        configure?.Invoke(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset, Turns = 2 };
        var map = new TileMap(width * height) { GameInfo = info };
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = "Grassland" });
        }
        map.SetTransients(ruleset);
        info.Map = map;
        var barbarians = MapUnitFactory.NewCiv(Constants.Barbarians, info);
        var rome = MapUnitFactory.NewCiv("Rome", info);
        var manager = new BarbarianManager();
        info.RealBarbarians = manager;
        manager.SetTransients(info);
        return new World(info, map, barbarians, rome, manager);
    }

    private static void MakeCamp(World w, int x, int y)
    {
        w.Map[HexCoord.Of(x, y)].SetImprovementBasic(Constants.BarbarianEncampment);
        w.Manager.SetTransients(w.Info); // picks up the camp
    }

    // ---- BarbarianEncampment

    [Fact]
    public void Encampment_DefaultsAndClone()
    {
        var camp = new BarbarianEncampment(HexCoord.Of(3, 4)) { Countdown = 5, SpawnedUnits = 2, Destroyed = true };
        var clone = camp.Clone();
        Assert.NotSame(camp, clone);
        Assert.Equal((HexCoord.Of(3, 4), 5, 2, true), (clone.Position, clone.Countdown, clone.SpawnedUnits, clone.Destroyed));
        var fresh = new BarbarianEncampment();
        Assert.Equal((HexCoord.Of(0, 0), 0, -1, false), (fresh.Position, fresh.Countdown, fresh.SpawnedUnits, fresh.Destroyed));
    }

    [Fact]
    public void Encampment_WasAttackedHalvesTheCountdown_OnlyWhileAlive()
    {
        var camp = new BarbarianEncampment { Countdown = 9 };
        camp.WasAttacked();
        Assert.Equal(4, camp.Countdown);
        camp.Destroyed = true;
        camp.WasAttacked();
        Assert.Equal(4, camp.Countdown);
    }

    [Fact]
    public void Encampment_WasDestroyedHaunts15Turns_AndIsIdempotent()
    {
        var camp = new BarbarianEncampment { Countdown = 3 };
        camp.WasDestroyed();
        Assert.True(camp.Destroyed);
        Assert.Equal(15, camp.Countdown);
        camp.Countdown = 7;
        camp.WasDestroyed();
        Assert.Equal(7, camp.Countdown);
    }

    [Fact]
    public void Encampment_UpdateCountsDownBeforeSpawning()
    {
        var w = NewWorld();
        MakeCamp(w, 2, 2);
        var camp = w.Manager.Encampments.Single();
        camp.Countdown = 2;
        camp.Update();
        Assert.Equal(1, camp.Countdown);
        Assert.Equal(-1, camp.SpawnedUnits);
    }

    [Fact]
    public void Encampment_UpdateOfADestroyedCampNeverSpawns()
    {
        var w = NewWorld();
        MakeCamp(w, 2, 2);
        var camp = w.Manager.Encampments.Single();
        camp.Destroyed = true;
        camp.Update();
        Assert.Equal((0, -1), (camp.Countdown, camp.SpawnedUnits));
    }

    [Fact]
    public void Encampment_UpdateWithAFailedSpawnKeepsTheCountdownAtZero()
    {
        var w = NewWorld(); // no units in the ruleset: nothing can be chosen
        MakeCamp(w, 2, 2);
        var camp = w.Manager.Encampments.Single();
        camp.Update();
        Assert.Equal((0, -1), (camp.Countdown, camp.SpawnedUnits));
    }

    [Theory]
    [InlineData(false, 0, 1.0f, -1, 8, 12)]
    [InlineData(true, 0, 1.0f, -1, 4, 6)]
    [InlineData(false, 0, 1.0f, 4, 5, 9)] // spawnedUnits becomes 5, reduced by min(5, 3)
    [InlineData(false, 0, 0.5f, -1, 4, 6)]
    [InlineData(false, 2, 1.0f, -1, 10, 14)]
    public void Encampment_ResetCountdownRange(bool raging, int delay, float modifier, int startSpawned, int min, int max)
    {
        var w = NewWorld(configure: ruleset =>
        {
            ruleset.Difficulties["Prince"].BarbarianSpawnDelay = delay;
            MapUnitFactory.NewBaseUnit(ruleset, "Brute");
        });
        w.Info.SpeedData.BarbarianModifier = modifier;
        w.Info.GameParameters.RagingBarbarians = raging;
        MakeCamp(w, 2, 2);
        var camp = w.Manager.Encampments.Single();
        camp.SpawnedUnits = startSpawned;
        camp.Update();
        Assert.Equal(startSpawned + 1, camp.SpawnedUnits);
        Assert.InRange(camp.Countdown, min, max);
    }

    [Fact]
    public void Encampment_SerializesLikeKotlin()
    {
        var camp = new BarbarianEncampment(HexCoord.Of(1, 2)) { Countdown = 4, SpawnedUnits = 1, Destroyed = true };
        var json = UncivJson.Serialize(camp);
        Assert.Contains("\"spawnedUnits\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"destroyed\":true", json, StringComparison.Ordinal);
        var back = UncivJson.Deserialize<BarbarianEncampment>(json)!;
        Assert.Equal((HexCoord.Of(1, 2), 4, 1, true), (back.Position, back.Countdown, back.SpawnedUnits, back.Destroyed));
    }

    // ---- BarbarianManager

    [Fact]
    public void SetTransients_AddsPreexistingCampsOnce()
    {
        var w = NewWorld();
        MakeCamp(w, 1, 1);
        MakeCamp(w, 3, 3);
        w.Manager.SetTransients(w.Info);
        w.Manager.SetTransients(w.Info);
        Assert.Equal([HexCoord.Of(1, 1), HexCoord.Of(3, 3)], w.Manager.Encampments.Select(it => it.Position).OrderBy(it => it.x).ThenBy(it => it.y).ToList());
        Assert.All(w.Manager.Encampments, it => Assert.Same(w.Info, it.GameInfo));
    }

    [Fact]
    public void Clone_CopiesTheEncampments()
    {
        var w = NewWorld();
        w.Manager.Encampments.Add(new BarbarianEncampment(HexCoord.Of(1, 1)) { Countdown = 6 });
        var clone = w.Manager.Clone();
        Assert.NotSame(w.Manager.Encampments[0], clone.Encampments[0]);
        Assert.Equal(6, clone.Encampments[0].Countdown);
    }

    [Fact]
    public void UpdateEncampments_MarksDestroyedCampsAndRemovesThemWhenTheGhostsLeave()
    {
        var w = NewWorld();
        w.Info.Turns = 5;
        MakeCamp(w, 1, 1);
        var camp = w.Manager.Encampments.Single();
        camp.Countdown = 4;
        w.Map[HexCoord.Of(1, 1)].RemoveImprovement();

        w.Manager.UpdateEncampments();
        Assert.True(camp.Destroyed);
        Assert.Equal(14, camp.Countdown); // WasDestroyed sets 15, then Update() counts down once (a destroyed camp never spawns)
        Assert.Contains(camp, w.Manager.Encampments);

        camp.Countdown = 0;
        w.Manager.UpdateEncampments();
        Assert.DoesNotContain(camp, w.Manager.Encampments);
    }

    [Fact]
    public void CampAttacked_OnlyAffectsTheCampAtThatPosition()
    {
        var w = NewWorld();
        w.Manager.Encampments.Add(new BarbarianEncampment(HexCoord.Of(1, 1)) { Countdown = 8 });
        w.Manager.Encampments.Add(new BarbarianEncampment(HexCoord.Of(2, 2)) { Countdown = 8 });
        w.Manager.CampAttacked(HexCoord.Of(2, 2));
        w.Manager.CampAttacked(HexCoord.Of(4, 4));
        Assert.Equal([8, 4], w.Manager.Encampments.Select(it => it.Countdown).ToList());
    }

    [Fact]
    public void PlaceBarbarianEncampment_FirstTurnPlacesACampAndNotifiesNobody()
    {
        var w = NewWorld();
        w.Info.Turns = 1;
        w.Manager.PlaceBarbarianEncampment();
        // 25 fog land tiles / 3 per camp = 8 camps, a third of that = 2; after the first the rest of this small map is within 7 tiles
        var camp = Assert.Single(w.Manager.Encampments);
        Assert.Equal(Constants.BarbarianEncampment, w.Map[camp.Position].Improvement);
        Assert.Equal(-1, camp.SpawnedUnits);
        Assert.Same(w.Info, camp.GameInfo);
    }

    [Fact]
    public void PlaceBarbarianEncampment_NeverPlacesInViewOfACiv()
    {
        var w = NewWorld(15, 15);
        w.Info.Turns = 3;
        bool Hidden(TileClass t) => t.Position.x < 4 && t.Position.y < 4; // 16 fog tiles, 8 per camp: room for a camp
        w.Rome.ViewableTiles = w.Map.Values.Where(it => !Hidden(it)).ToHashSet();
        w.Manager.PlaceBarbarianEncampment(forTesting: true);
        Assert.True(Hidden(w.Map[Assert.Single(w.Manager.Encampments).Position]));
    }

    [Fact]
    public void PlaceBarbarianEncampment_DoesNothingWhenTheMapIsFullyVisible()
    {
        var w = NewWorld();
        w.Rome.ViewableTiles = w.Map.Values.ToHashSet();
        w.Manager.PlaceBarbarianEncampment(forTesting: true);
        Assert.Empty(w.Manager.Encampments);
    }

    [Fact]
    public void PlaceBarbarianEncampment_KeepsAwayFromExistingCamps()
    {
        var w = NewWorld(15, 15);
        w.Info.Turns = 3;
        MakeCamp(w, 7, 7);
        w.Manager.PlaceBarbarianEncampment(forTesting: true);
        Assert.Equal(2, w.Manager.Encampments.Count);
        var added = w.Manager.Encampments.Single(it => it.Position != HexCoord.Of(7, 7));
        Assert.True(w.Map[HexCoord.Of(7, 7)].GetTilesInDistance(7).All(it => it.Position != added.Position));
    }

    [Fact]
    public void PlaceBarbarianEncampment_CountsDestroyedCampsAsBlockingOnlyWithin4Tiles()
    {
        var w = NewWorld(15, 15);
        w.Info.Turns = 3;
        MakeCamp(w, 7, 7);
        w.Manager.Encampments.Single().Destroyed = true;
        w.Manager.PlaceBarbarianEncampment(forTesting: true);
        var added = w.Manager.Encampments.Single(it => it.Position != HexCoord.Of(7, 7));
        Assert.True(w.Map[HexCoord.Of(7, 7)].GetTilesInDistance(4).All(it => it.Position != added.Position));
    }

    [Fact]
    public void PlaceBarbarianEncampment_NotifiesCivsWithTheHonorUniqueThatExploredTheTile()
    {
        var w = NewWorld();
        w.Info.Turns = 3;
        var honor = MapUnitFactory.NewCiv("Honorable", w.Info, "Notified of new Barbarian encampments");
        honor.PlayerType = PlayerType.Human; // AI civs keep no notifications
        w.Rome.PlayerType = PlayerType.Human;
        foreach (var tile in w.Map.Values) tile.SetExplored(honor, true);
        w.Rome.ViewableTiles = new HashSet<TileClass>(); // Rome has the tiles in neither fog nor view of interest: it has no unique
        w.Manager.PlaceBarbarianEncampment(forTesting: true);
        var camp = Assert.Single(w.Manager.Encampments);
        var notification = Assert.Single(honor.Notifications);
        Assert.Equal("A new barbarian encampment has spawned!", notification.Text);
        Assert.Empty(w.Rome.Notifications);
        Assert.Equal(Constants.BarbarianEncampment, honor.GetLastSeenImprovement(camp.Position));
    }

    [Fact]
    public void CreateNewCamp_UsesTheEquivalentImprovementsOfTheRuleset()
    {
        var w = NewWorld(configure: ruleset =>
        {
            ruleset.TileImprovements.Clear();
            ruleset.TileImprovements.Put("Goblin den", new TileImprovement { Name = "Goblin den", Uniques = ["Marks a barbarian camp"] });
        });
        var tile = w.Map[HexCoord.Of(2, 2)];
        w.Manager.CreateNewCamp(tile);
        Assert.Equal("Goblin den", tile.Improvement);
        Assert.Equal(tile.Position, Assert.Single(w.Manager.Encampments).Position);
    }

    [Fact]
    public void CreateNewCamp_FallsBackToTheBarbarianEncampment()
    {
        var w = NewWorld(configure: ruleset => ruleset.TileImprovements.Clear());
        w.Info.RulesetData.TileImprovements.Put(Constants.BarbarianEncampment, new TileImprovement { Name = Constants.BarbarianEncampment });
        var tile = w.Map[HexCoord.Of(1, 1)];
        w.Manager.CreateNewCamp(tile);
        Assert.Equal(Constants.BarbarianEncampment, tile.Improvement);
    }

    [Fact]
    public void GetEncampmentTiles_IsAMutableListInEncampmentOrder()
    {
        var w = NewWorld();
        w.Manager.Encampments.Add(new BarbarianEncampment(HexCoord.Of(3, 3)));
        w.Manager.Encampments.Add(new BarbarianEncampment(HexCoord.Of(1, 1)));
        var tiles = w.Manager.GetEncampmentTiles();
        Assert.Equal([HexCoord.Of(3, 3), HexCoord.Of(1, 1)], tiles.Select(it => it.Position).ToList());
        tiles.Reverse();
        Assert.Equal(HexCoord.Of(3, 3), w.Manager.Encampments[0].Position);
    }

    [Fact]
    public void SpawnBarbarian_WanderingBarbariansWaitUntilTurn10()
    {
        var w = NewWorld();
        w.Info.Turns = 9;
        var tile = w.Map[HexCoord.Of(2, 2)];
        var guard = MapUnitFactory.NewUnit(w.Barbarians, "Brute", "Land", tile);
        tile.MilitaryUnit = guard;
        Assert.Null(w.Manager.SpawnBarbarian(tile));
    }

    [Fact]
    public void SpawnBarbarian_TooManyBarbariansNearby()
    {
        var w = NewWorld();
        w.Info.Turns = 20;
        foreach (var (x, y) in new[] { (2, 2), (1, 1), (3, 3), (1, 2) })
        {
            var t = w.Map[HexCoord.Of(x, y)];
            t.MilitaryUnit = MapUnitFactory.NewUnit(w.Barbarians, "Brute", "Land", t);
        }
        Assert.Null(w.Manager.SpawnBarbarian(w.Map[HexCoord.Of(2, 2)]));
    }

    [Fact]
    public void SpawnBarbarian_NoSurroundingTileIsFreeForAWanderer()
    {
        var w = NewWorld();
        w.Info.Turns = 20;
        var centre = w.Map[HexCoord.Of(2, 2)];
        centre.MilitaryUnit = MapUnitFactory.NewUnit(w.Barbarians, "Brute", "Land", centre);
        foreach (var neighbor in centre.Neighbors)
            neighbor.MilitaryUnit = MapUnitFactory.NewUnit(w.Rome, "Warrior", "Land", neighbor);
        // allegiance is not barbarian: the nearby-barbarian checks are skipped, but there is no valid tile either
        Assert.Null(w.Manager.SpawnBarbarian(centre, w.Rome));
    }

    [Fact]
    public void SpawnBarbarian_WithoutAnyBuildableUnitSpawnsNothing()
    {
        var w = NewWorld();
        Assert.Null(w.Manager.SpawnBarbarian(w.Map[HexCoord.Of(2, 2)]));
    }

    [Fact]
    public void SpawnBarbarian_SetsTheBarbarianTechToWhatEveryoneKnows()
    {
        var w = NewWorld(configure: ruleset =>
        {
            foreach (var tech in new[] { "A", "B", "C" }) ruleset.Technologies.Put(tech, new Technology { Name = tech });
        });
        w.Rome.Tech.TechsResearched.Add("A");
        w.Rome.Tech.TechsResearched.Add("B");
        w.Rome.Units = new TestUnitManager(w.Rome) { Count = 1 }; // alive
        var other = MapUnitFactory.NewCiv("Egypt", w.Info);
        other.Units = new TestUnitManager(other) { Count = 1 };
        other.Tech.TechsResearched.Add("B");
        other.Tech.TechsResearched.Add("C");
        w.Manager.SpawnBarbarian(w.Map[HexCoord.Of(2, 2)]);
        Assert.Equal(["B"], w.Barbarians.Tech.TechsResearched.ToList());
    }
}
