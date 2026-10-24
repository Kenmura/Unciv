using Unciv.Json;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using CityObject = Unciv.Logic.City.City;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Logic.Map;

// T065: MapUnit and MapUnitCache over a real Civilization / TileMap with fake movement / promotions sub-objects (T066/T067 own the
// real ones). UnitFormationTests.kt is TestGame-based and is ported with T072.
public class MapUnitCoreTests
{
    private sealed record World(Civilization Civ, FakeGameInfoState Info, TileMap Map, Ruleset Ruleset)
    {
        public TileClass TileAt(int x, int y) => Map[HexCoord.Of(x, y)];

        public MapUnitClass Unit(string name = "Warrior", string type = "Land", int x = 2, int y = 2, Civilization? civ = null, params string[] uniques)
        {
            var owner = civ ?? Civ;
            var baseUnit = Ruleset.Units.TryGetValue(name, out var existing) ? existing : MapUnitFactory.NewBaseUnit(Ruleset, name, type, uniques: uniques);
            return MapUnitFactory.NewUnit(owner, baseUnit, TileAt(x, y));
        }
    }

    private static World NewWorld(Action<Ruleset>? configure = null, params string[] civUniques)
    {
        var ruleset = new Ruleset();
        ruleset.Terrains.Put("Grassland", new Terrain { Name = "Grassland", Type = TerrainType.Land });
        ruleset.Terrains.Put("Hill", new Terrain { Name = "Hill", Type = TerrainType.Land, Uniques = ["Occurs in groups around high elevations"] });
        ruleset.Terrains.Put("Forest", new Terrain { Name = "Forest", Type = TerrainType.TerrainFeature });
        ruleset.Terrains.Put("Coast", new Terrain { Name = "Coast", Type = TerrainType.Water });
        ruleset.Terrains.Put("Lava", new Terrain { Name = "Lava", Type = TerrainType.TerrainFeature, DamagePerTurn = 7 });
        ruleset.UnitPromotions.Put("Shock", new Promotion { Name = "Shock", Uniques = ["[+1] Movement"] });
        MapUnitFactory.AddUnitTypes(ruleset);
        configure?.Invoke(ruleset);
        var info = new FakeGameInfoState { RulesetData = ruleset };
        var map = new TileMap(25) { GameInfo = info };
        for (var x = 0; x < 5; x++)
        {
            for (var y = 0; y < 5; y++)
                map.TileList.Add(new TileClass { Position = HexCoord.Of(x, y), BaseTerrain = "Grassland" });
        }
        map.SetTransients(ruleset);
        info.Map = map;
        var civ = MapUnitFactory.NewCiv("Rome", info, civUniques);
        return new World(civ, info, map, ruleset);
    }

    // ---- MapUnitCache

    [Fact]
    public void Cache_ReadsTheMovementUniques()
    {
        var w = NewWorld();
        var unit = w.Unit("Hovercraft", "Land", 2, 2, null,
            "All tiles cost 1 movement", "Can pass through impassable tiles", "Ignores terrain cost", "Ignores Zone of Control",
            "Rough terrain penalty", "May travel on Water tiles without embarking", "Cannot move");
        var cache = unit.Cache;
        Assert.True(cache.AllTilesCosts1);
        Assert.True(cache.CanPassThroughImpassableTiles);
        Assert.True(cache.IgnoresTerrainCost);
        Assert.True(cache.IgnoresZoneOfControl);
        Assert.True(cache.RoughTerrainPenalty);
        Assert.True(cache.CanMoveOnWater);
        Assert.True(cache.CannotMove);
        Assert.False(cache.NoTerrainMovementUniques); // roughTerrainPenalty
        Assert.False(cache.CanEnterIceTiles);
        Assert.False(cache.CannotEmbark);
        Assert.Same(unit.Cache.State, ((IGameContextUnit)unit).CacheState);
    }

    [Fact]
    public void Cache_CannotMoveIsAlsoTrueForZeroBaseMovement()
    {
        var w = NewWorld();
        var baseUnit = MapUnitFactory.NewBaseUnit(w.Ruleset, "Fort", movement: 0);
        Assert.True(MapUnitFactory.NewUnit(w.Civ, baseUnit).Cache.CannotMove);
        Assert.False(w.Unit("Warrior").Cache.CannotMove);
    }

    [Fact]
    public void Cache_ClassifiesTheDoubleMovementTargets()
    {
        var w = NewWorld();
        var unit = w.Unit("Skier", "Land", 2, 2, null,
            "Double movement in [Forest]", "Double movement in [Grassland]", "Double movement in [Hill]", "Double movement in [Rubble]");
        var map = unit.Cache.DoubleMovementInTerrain;
        Assert.Equal(MapUnitCache.DoubleMovementTerrainTarget.Feature, map["Forest"].TerrainTarget);
        Assert.Equal(MapUnitCache.DoubleMovementTerrainTarget.Base, map["Grassland"].TerrainTarget);
        Assert.Equal(MapUnitCache.DoubleMovementTerrainTarget.Hill, map["Hill"].TerrainTarget);
        Assert.Equal(MapUnitCache.DoubleMovementTerrainTarget.Filter, map["Rubble"].TerrainTarget); // not a terrain: a filter
        Assert.Equal("Double movement in [Forest]", map["Forest"].Unique.Text);
        Assert.False(unit.Cache.NoTerrainMovementUniques);
        Assert.False(unit.Cache.NoBaseTerrainOrHillDoubleMovementUniques);
        Assert.False(unit.Cache.NoFilteredDoubleMovementUniques);
    }

    [Fact]
    public void Cache_ShortcutFlags_WithOnlyFeatureDoubleMovement()
    {
        var w = NewWorld();
        var unit = w.Unit("Skier", "Land", 2, 2, null, "Double movement in [Forest]");
        Assert.False(unit.Cache.NoTerrainMovementUniques);
        Assert.True(unit.Cache.NoBaseTerrainOrHillDoubleMovementUniques);
        Assert.True(unit.Cache.NoFilteredDoubleMovementUniques);
        var plain = w.Unit("Warrior");
        Assert.True(plain.Cache.NoTerrainMovementUniques);
        Assert.True(plain.Cache.NoBaseTerrainOrHillDoubleMovementUniques);
        Assert.True(plain.Cache.NoFilteredDoubleMovementUniques);
    }

    [Fact]
    public void Cache_ShortcutFlags_NationIgnoringHillMovementCost()
    {
        var w = NewWorld();
        w.Civ.Nation.IgnoreHillMovementCost = true;
        Assert.False(w.Unit("Warrior").Cache.NoTerrainMovementUniques);
    }

    [Fact]
    public void Cache_EmbarkCostsAreTheMinimumOfTheUniques()
    {
        var w = NewWorld();
        var unit = w.Unit("Marine", "Land", 2, 2, null,
            "[1.5] Movement point cost to disembark", "[0.5] Movement point cost to disembark", "[2] Movement point cost to embark");
        Assert.Equal(0.5f, unit.Cache.CostToDisembark);
        Assert.Equal(2f, unit.Cache.CostToEmbark);
        Assert.Null(w.Unit("Warrior").Cache.CostToDisembark);
        Assert.Null(w.Unit("Warrior").Cache.CostToEmbark);
    }

    [Fact]
    public void Cache_CitadelPlacement_NeedsAnImprovementWithTheTakeOverUnique()
    {
        var w = NewWorld(ruleset =>
        {
            ruleset.TileImprovements.Put("Citadel", new TileImprovement { Name = "Citadel", Uniques = ["Gain control over [All] tiles in a [1]-tile radius"] });
            ruleset.TileImprovements.Put("Road", new TileImprovement { Name = "Road" });
        });
        Assert.True(w.Unit("GreatGeneral", "Land", 2, 2, null, "Can instantly construct a [Citadel] improvement").Cache.HasCitadelPlacementUnique);
        Assert.False(w.Unit("Engineer", "Land", 2, 2, null, "Can instantly construct a [Road] improvement").Cache.HasCitadelPlacementUnique);
        Assert.False(w.Unit("Missing", "Land", 2, 2, null, "Can instantly construct a [Nope] improvement").Cache.HasCitadelPlacementUnique);
    }

    [Fact]
    public void Cache_UnitMovementUniques_ListsTheNineteenCachedTypes()
    {
        Assert.Equal(19, MapUnitCache.UnitMovementUniques.Count);
        Assert.Contains(UniqueType.EnemyUnitsSpendExtraMovement, MapUnitCache.UnitMovementUniques);
        Assert.DoesNotContain(UniqueType.Sight, MapUnitCache.UnitMovementUniques);
    }

    // ---- names, clone, toString

    [Fact]
    public void Names_ToStringAndDisplayName()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.Equal("Warrior - Rome", unit.ToString());
        Assert.Equal("[Warrior]", unit.DisplayName());
        Assert.Equal("[Warrior]", unit.ShortDisplayName());
        unit.InstanceName = "Bob";
        Assert.Equal("Bob ([Warrior])", unit.DisplayName());
        Assert.Equal("[Bob]", unit.ShortDisplayName());
        unit.Religion = "Zeus";
        w.Info.ReligionMap["Zeus"] = new Religion { Name = "Zeus", DisplayName = "Cult of Zeus" };
        Assert.Equal("Cult of Zeus", unit.GetReligionDisplayName());
        Assert.Equal("Bob ([Warrior]) ([Cult of Zeus])", unit.DisplayName());
        unit.Religion = null;
        Assert.Null(unit.GetReligionDisplayName());
    }

    [Fact]
    public void LateinitMembers_ThrowBeforeAssignment()
    {
        var unit = new MapUnitClass();
        Assert.Throws<InvalidOperationException>(() => unit.Owner);
        Assert.Throws<InvalidOperationException>(() => unit.Name);
        Assert.Throws<InvalidOperationException>(() => unit.Civ);
        Assert.Throws<InvalidOperationException>(() => unit.BaseUnit);
        Assert.Throws<InvalidOperationException>(() => unit.CurrentTile);
        Assert.Throws<InvalidOperationException>(() => unit.Movement);
        Assert.Throws<InvalidOperationException>(() => unit.Promotions);
        Assert.False(unit.HasTile());
        Assert.Null(unit.GetOtherEscortUnit());
        Assert.Equal(100, unit.Health);
        Assert.Equal(Constants.NoId, unit.Id);
        Assert.True(unit.Due);
    }

    [Fact]
    public void Clone_CopiesTheStateAndDeepCopiesTheMutableParts()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Id = 7;
        unit.InstanceName = "Bob";
        unit.CurrentMovement = 1.5f;
        unit.Health = 60;
        unit.Action = "Fortify";
        unit.Automated = true;
        unit.AttacksThisTurn = 1;
        unit.TurnsFortified = 3;
        unit.IsTransported = true;
        unit.Religion = "Zeus";
        unit.ReligiousStrengthLost = 4;
        unit.OriginalOwner = "Egypt";
        unit.AutomatedRoadConnectionDestination = HexCoord.Of(1, 1);
        unit.MostRecentMoveType = UnitMovementMemoryType.UnitWithdrew;
        unit.AbilityToTimesUsed["Build"] = 2;
        unit.Promo().Promotions.Add("Shock");
        unit.MovementMemories.Add(new MapUnitClass.UnitMovementMemory(HexCoord.Of(2, 2), UnitMovementMemoryType.UnitMoved));
        unit.AttacksSinceTurnStart.Add(HexCoord.Of(3, 3));
        unit.StatusMap["Frozen"] = new MapUnitClass.UnitStatus("Frozen", 2);

        var clone = unit.Clone();
        Assert.NotSame(unit, clone);
        Assert.Equal(("Warrior", "Rome", 7, "Bob"), (clone.Name, clone.Owner, clone.Id, clone.InstanceName));
        Assert.Equal((1.5f, 60, "Fortify", true, 1, 3), (clone.CurrentMovement, clone.Health, clone.Action, clone.Automated, clone.AttacksThisTurn, clone.TurnsFortified));
        Assert.Equal(("Zeus", 4, "Egypt", true), (clone.Religion, clone.ReligiousStrengthLost, clone.OriginalOwner, clone.IsTransported));
        Assert.Equal(HexCoord.Of(1, 1), clone.AutomatedRoadConnectionDestination);
        Assert.Equal(UnitMovementMemoryType.UnitWithdrew, clone.MostRecentMoveType);
        Assert.Same(unit.Civ, clone.Civ);
        Assert.Same(unit.BaseUnit, clone.BaseUnit);
        Assert.False(clone.HasTile()); // the tile is not cloned
        Assert.Equal(2, clone.AbilityToTimesUsed["Build"]);
        Assert.NotSame(unit.Promotions, clone.Promotions);
        Assert.True(clone.Promotions.Promotions.Contains("Shock"));
        Assert.NotSame(unit.MovementMemories[0], clone.MovementMemories[0]);
        Assert.Equal(unit.MovementMemories[0].Position, clone.MovementMemories[0].Position);
        Assert.NotSame(unit.StatusMap["Frozen"], clone.StatusMap["Frozen"]);
        Assert.Equal(2, clone.StatusMap["Frozen"].TurnsLeft);
        clone.AttacksSinceTurnStart.Clear();
        clone.AbilityToTimesUsed["Build"] = 9;
        clone.StatusMap["Frozen"].TurnsLeft = 5;
        Assert.Single(unit.AttacksSinceTurnStart);
        Assert.Equal(2, unit.AbilityToTimesUsed["Build"]);
        Assert.Equal(2, unit.StatusMap["Frozen"].TurnsLeft);
    }

    [Fact]
    public void Clone_KeepsTheEscortingFlag()
    {
        var w = NewWorld();
        var military = w.Unit("Warrior");
        var civilian = w.Unit("Worker", "Land", 2, 2);
        w.TileAt(2, 2).MilitaryUnit = military;
        w.TileAt(2, 2).CivilianUnit = civilian;
        military.StartEscorting();
        var clone = military.Clone();
        clone.CurrentTile = w.TileAt(2, 2);
        Assert.True(clone.IsEscorting());
    }

    // ---- action predicates

    [Fact]
    public void ActionPredicates_FollowTheActionStrings()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.False(unit.IsFortified());
        unit.Action = "Fortify";
        Assert.True(unit.IsFortified());
        Assert.False(unit.IsFortifyingUntilHealed());
        unit.Action = "Fortify until healed";
        Assert.True(unit.IsFortifyingUntilHealed());
        Assert.True(unit.IsActionUntilHealed());
        unit.TurnsFortified = 4;
        Assert.Equal(4, unit.GetFortificationTurns());
        unit.Action = "Guard";
        Assert.True(unit.IsGuarding());
        Assert.Equal(4, unit.GetFortificationTurns());
        unit.Action = "Sleep until healed";
        Assert.True(unit.IsSleeping());
        Assert.True(unit.IsSleepingUntilHealed());
        Assert.Equal(0, unit.GetFortificationTurns());
        unit.Action = "Sleep";
        Assert.False(unit.IsSleepingUntilHealed());
        unit.Action = "moveTo 1,2";
        Assert.True(unit.IsMoving());
        unit.Action = "Explore";
        Assert.True(unit.IsExploring());
        unit.Action = "Paradrop";
        Assert.True(unit.IsPreparingParadrop());
        unit.Action = "Air Sweep";
        Assert.True(unit.IsPreparingAirSweep());
        unit.Action = "Set up";
        Assert.True(unit.IsSetUpForSiege());
        unit.Action = Constants.ConnectRoadAction;
        Assert.True(unit.IsAutomatingRoadConnection());
        unit.Automated = true;
        Assert.True(unit.IsAutomated());
    }

    [Fact]
    public void GetMovementDestination_ParsesTheActionCoordinates()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Action = "moveTo 3.0,1.0";
        Assert.Same(w.TileAt(3, 1), unit.GetMovementDestination());
        unit.Action = "moveTo 4,4,";
        Assert.Same(w.TileAt(4, 4), unit.GetMovementDestination());
    }

    [Fact]
    public void FortifyHelpers_SetTheAction_AndCanFortifyRespectsTheUnitKind()
    {
        var w = NewWorld();
        var warrior = w.Unit();
        Assert.True(warrior.CanFortify());
        warrior.FortifyIfCan();
        Assert.Equal("Fortify", warrior.Action);
        Assert.False(warrior.CanFortify());
        Assert.True(warrior.CanFortify(ignoreAlreadyFortified: true));
        warrior.FortifyUntilHealed();
        Assert.Equal("Fortify until healed", warrior.Action);
        var civilian = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0), w.TileAt(0, 0));
        Assert.False(civilian.CanFortify());
        Assert.False(w.Unit("Trireme", "Water", 0, 1).CanFortify());
        Assert.False(w.Unit("Fighter", "Air", 0, 2).CanFortify());
        Assert.False(w.Unit("Knight", "Land", 0, 3, null, "No defensive terrain bonus").CanFortify());
    }

    [Fact]
    public void FortifyIfCan_DoesNothingForACivilian()
    {
        var w = NewWorld();
        var civilian = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0), w.TileAt(0, 0));
        civilian.FortifyIfCan();
        Assert.Null(civilian.Action);
    }

    // ---- movement

    [Fact]
    public void GetMaxMovement_BaseUniquesMinimumAndEmbarked()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.Equal(2, unit.GetMaxMovement());
        var fast = w.Unit("Knight", "Land", 1, 1, null, "[+1] Movement", "[+2] Movement");
        Assert.Equal(5, fast.GetMaxMovement());
        var slow = w.Unit("Heavy", "Land", 1, 2, null, "[-5] Movement");
        Assert.Equal(1, slow.GetMaxMovement()); // never below 1
        w.TileAt(0, 0).BaseTerrain = "Coast";
        w.TileAt(0, 0).SetTerrainTransients();
        var swimmer = w.Unit("Swimmer", "Land", 0, 0);
        Assert.True(swimmer.IsEmbarked());
        Assert.Equal(2, swimmer.GetMaxMovement());
    }

    [Fact]
    public void GetMaxMovement_CivUniquesApplyOnlyWhenAsked_AndPromotionsAreIncluded()
    {
        var w = NewWorld(null, "[+1] Movement");
        var unit = w.Unit();
        // the unit's own getMatchingUniques does not see civ uniques, getMaxMovement asks for them
        Assert.Equal(3, unit.GetMaxMovement());
        Assert.Empty(unit.GetMatchingUniques(UniqueType.Movement));
        Assert.Single(unit.GetMatchingUniques(UniqueType.Movement, checkCivInfoUniques: true));
        unit.Promo().PromotionObjects.Add(w.Ruleset.UnitPromotions["Shock"]);
        unit.UpdateUniques();
        Assert.Equal(4, unit.GetMaxMovement());
    }

    [Fact]
    public void GetMaxMovement_CohabitantCanBoostTheSpeed()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var knight = w.Unit("Hakkapeliitta", "Land", 2, 2, null, "Transfer Movement to [Worker]");
        var worker = w.Unit("Worker", "Land", 2, 2);
        tile.MilitaryUnit = knight;
        tile.CivilianUnit = worker;
        Assert.Equal(2, worker.GetMaxMovement());
        var fastKnight = w.Unit("Hakkapeliitta", "Land", 2, 2);
        Assert.Same(knight.BaseUnit, fastKnight.BaseUnit);
        // Raise the knight's own speed: the worker takes the larger value, but not the other way round
        knight.Promo().PromotionObjects.Add(new Promotion { Name = "Fast", Uniques = ["[+2] Movement"] });
        knight.UpdateUniques();
        Assert.Equal(4, knight.GetMaxMovement());
        Assert.Equal(4, worker.GetMaxMovement());
        Assert.Equal(2, worker.GetMaxMovement(ignoreOtherUnit: true));
    }

    [Fact]
    public void UseMovementPoints_ResetsFortificationAndClampsBothWays()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.CurrentMovement = 2f;
        unit.TurnsFortified = 3;
        unit.UseMovementPoints(0.5f);
        Assert.Equal(1.5f, unit.CurrentMovement);
        Assert.Equal(0, unit.TurnsFortified);
        unit.UseMovementPoints(5f);
        Assert.Equal(0f, unit.CurrentMovement);
        Assert.False(unit.HasMovement());
        unit.UseMovementPoints(-10f); // gain, clamped to the maximum
        Assert.Equal(2f, unit.CurrentMovement);
        Assert.True(unit.HasMovement());
    }

    [Fact]
    public void HasUnitMovedThisTurn_ComparesAgainstTheMaximumMinusOneUlp()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.CurrentMovement = 2f;
        Assert.False(unit.HasUnitMovedThisTurn());
        unit.CurrentMovement = 1.9999999f;
        Assert.False(unit.HasUnitMovedThisTurn()); // within one ulp of 2
        unit.CurrentMovement = 1.99f;
        Assert.True(unit.HasUnitMovedThisTurn());
    }

    // ---- uniques, ranges, attacks

    [Fact]
    public void RangesAttacksAndVisibility()
    {
        var w = NewWorld();
        var melee = w.Unit();
        Assert.Equal(1, melee.GetRange());
        Assert.Equal(2, melee.GetVisibilityRange());
        Assert.Equal(1, melee.MaxAttacksPerTurn());
        melee.CurrentMovement = 1f;
        Assert.True(melee.CanAttack());
        melee.AttacksThisTurn = 1;
        Assert.False(melee.CanAttack());
        var archer = w.Unit("Archer", "Land", 1, 1, null, "[+1] Range", "[+1] Sight", "[+2] additional attacks per turn");
        var archerBase = archer.BaseUnit;
        archerBase.RangedStrength = 5;
        Assert.Equal(3, archer.GetRange()); // base range 2 + 1
        Assert.Equal(3, archer.GetVisibilityRange());
        Assert.Equal(3, archer.MaxAttacksPerTurn());
        Assert.Equal(6, archer.GetAirUnitMovementRange());
        var blind = w.Unit("Blind", "Land", 0, 1, null, "[-5] Sight");
        Assert.Equal(1, blind.GetVisibilityRange());
        var settler = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0), w.TileAt(0, 0));
        settler.CurrentMovement = 2f;
        Assert.False(settler.CanAttack());
    }

    [Fact]
    public void VisibilityRange_AddsTheTileSightUniques()
    {
        var w = NewWorld(ruleset => ruleset.Terrains["Hill"].Uniques = ["Occurs in groups around high elevations", "[+1] Sight"]);
        w.TileAt(2, 2).BaseTerrain = "Hill";
        w.TileAt(2, 2).SetTerrainTransients();
        Assert.Equal(3, w.Unit().GetVisibilityRange());
    }

    [Fact]
    public void ResourceRequirements_PerTurnAndCosts()
    {
        var w = NewWorld();
        var baseUnit = MapUnitFactory.NewBaseUnit(w.Ruleset, "Horseman", uniques: ["Consumes [2] [Horses]", "Costs [3] [Gold]"]);
        baseUnit.RequiredResource = "Iron";
        var unit = MapUnitFactory.NewUnit(w.Civ, baseUnit, w.TileAt(1, 1));
        var requirements = unit.GetResourceRequirementsPerTurn();
        Assert.Equal(1, requirements["Iron"]);
        Assert.Equal(2, requirements["Horses"]);
        Assert.True(unit.RequiresResource("Iron"));
        Assert.True(unit.RequiresResource("Horses"));
        Assert.False(unit.RequiresResource("Wheat"));
        Assert.True(((IGameContextUnit)unit).HasResourceRequirementsPerTurn());
        Assert.False(((IGameContextUnit)w.Unit("Warrior")).HasResourceRequirementsPerTurn());
    }

    // ---- filters

    [Fact]
    public void MatchesFilter_SingleFilters()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.True(unit.MatchesFilter("Non-City"));
        Assert.True(unit.MatchesFilter("Warrior")); // base unit name
        Assert.True(unit.MatchesFilter("Land"));
        Assert.True(unit.MatchesFilter("Rome")); // civ filter
        Assert.False(unit.MatchesFilter("Water"));
        Assert.False(unit.MatchesFilter(Constants.Wounded));
        unit.Health = 50;
        Assert.True(unit.MatchesFilter(Constants.Wounded));
        Assert.True(unit.MatchesFilter("wounded units"));
        Assert.False(unit.MatchesFilter(Constants.Barbarians));
        Assert.False(unit.MatchesFilter("City-State"));
        Assert.False(unit.MatchesFilter(Constants.Embarked));
        unit.Promo().Promotions.Add("Shock");
        Assert.True(unit.MatchesFilter("Shock"));
        unit.StatusMap["Frozen"] = new MapUnitClass.UnitStatus("Frozen", 1);
        Assert.True(unit.MatchesFilter("Frozen"));
        Assert.True(((IGameContextUnit)unit).HasPromotionOrStatus("Frozen"));
        Assert.True(((IGameContextUnit)unit).HasPromotionOrStatus("Shock"));
        Assert.False(((IGameContextUnit)unit).HasPromotionOrStatus("Nope"));
    }

    [Fact]
    public void MatchesFilter_MultiFilterAndTheOtherFilter()
    {
        var w = NewWorld();
        var unit = w.Unit();
        var other = w.Unit("Archer", "Land", 1, 1);
        Assert.True(unit.MatchesFilter("{Land} {Rome}"));
        Assert.False(unit.MatchesFilter("{Land} {Water}"));
        Assert.True(unit.MatchesFilter("non-[Water]"));
        Assert.False(unit.MatchesFilter("non-[Land]"));
        // multiFilter = false hands the whole text to the single filter
        Assert.False(unit.MatchesFilter("non-[Water]", multiFilter: false));
        // "other" is false for the unit in the state, true for any other
        var state = GameContext.ForUnit(unit);
        Assert.False(unit.MatchesFilter("other", true, state));
        Assert.True(other.MatchesFilter("other", true, state));
        Assert.True(unit.MatchesFilter("other")); // EmptyState has no unit
    }

    [Fact]
    public void MatchesFilter_BarbarianAndCityStateCivs()
    {
        var w = NewWorld();
        var barbarians = MapUnitFactory.NewCiv(Constants.Barbarians, w.Info);
        Assert.True(w.Unit("Brute", "Land", 1, 1, barbarians).MatchesFilter(Constants.Barbarians));
        Assert.True(w.Unit("Brute", "Land", 1, 1, barbarians).MatchesFilter("Barbarian"));
        var geneva = MapUnitFactory.NewCiv("Geneva", w.Info);
        geneva.Nation.CityStateType = "Cultured";
        Assert.True(w.Unit("Guard", "Land", 1, 2, geneva).MatchesFilter("City-State"));
    }

    [Fact]
    public void MatchesFilter_EmbarkedLandUnitsOnWater()
    {
        var w = NewWorld();
        w.TileAt(0, 0).BaseTerrain = "Coast";
        w.TileAt(0, 0).SetTerrainTransients();
        Assert.True(w.Unit("Swimmer", "Land", 0, 0).MatchesFilter(Constants.Embarked));
        Assert.False(w.Unit("Boat", "Water", 0, 0).IsEmbarked());
        Assert.False(w.Unit("Amphibious", "Land", 0, 0, null, "May travel on Water tiles without embarking").IsEmbarked());
    }

    [Fact]
    public void MatchesFilter_UsesTheNonUnitUniqueMapForUniqueTags()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.False(unit.MatchesFilter("Brave"));
        unit.Promo().PromotionObjects.Add(new Promotion { Name = "Valor", Uniques = ["Brave"] });
        unit.UpdateUniques();
        Assert.True(unit.MatchesFilter("Brave"));
        // the base unit's own uniques are not in the non-unit map
        var tagged = w.Unit("Tagged", "Land", 1, 1, null, "Bold");
        Assert.False(tagged.MatchesFilter("Bold") && !tagged.BaseUnit.MatchesFilter("Bold", tagged.Cache.State, false));
    }

    // ---- health, healing

    [Fact]
    public void HealBy_CapsAtOneHundredAndDoublesWithTheUnique()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Health = 50;
        unit.HealBy(20);
        Assert.Equal(70, unit.Health);
        unit.HealBy(50);
        Assert.Equal(100, unit.Health);
        var doubled = NewWorld(null, "All healing effects doubled");
        var healer = doubled.Unit();
        healer.Health = 10;
        healer.HealBy(20);
        Assert.Equal(50, healer.Health);
    }

    [Fact]
    public void TakeDamage_ClampsAndDestroysAtZero()
    {
        var w = NewWorld();
        var unit = w.Unit();
        w.TileAt(2, 2).MilitaryUnit = unit;
        unit.TakeDamage(30);
        Assert.Equal(70, unit.Health);
        unit.TakeDamage(-50); // cheating modder: negative damage clamps at 100
        Assert.Equal(100, unit.Health);
        Assert.False(unit.IsDestroyed);
        unit.TakeDamage(500);
        Assert.Equal(0, unit.Health);
        Assert.True(unit.IsDestroyed);
        Assert.Null(w.TileAt(2, 2).MilitaryUnit);
    }

    [Fact]
    public void HealingAmountForTile_ByTerritoryAndTerrain()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Health = 50;
        // neutral land
        Assert.Equal(10, unit.GetHealAmountForCurrentTile());
        Assert.True(unit.CanHealInCurrentTile());
        Assert.Equal(10, unit.HealingAmountForTile(w.TileAt(2, 2)));
        // water: a land unit cannot heal there
        w.TileAt(3, 3).BaseTerrain = "Coast";
        w.TileAt(3, 3).SetTerrainTransients();
        Assert.Equal(0, unit.HealingAmountForTile(w.TileAt(3, 3)));
        // terrain damage is subtracted unless noTerrainDamage
        w.TileAt(1, 1).AddTerrainFeature("Lava");
        Assert.Equal(3, unit.HealingAmountForTile(w.TileAt(1, 1)));
        Assert.Equal(10, unit.HealingAmountForTile(w.TileAt(1, 1), noTerrainDamage: true));
        Assert.Equal(7, unit.GetDamageFromTerrain(w.TileAt(1, 1)));
        Assert.Equal(0, unit.GetDamageFromTerrain());
        // Uniques add to the healing
        var healer = w.Unit("Medic", "Land", 0, 0, null, "[5] HP when healing");
        healer.Health = 10;
        Assert.Equal(15, healer.GetHealAmountForCurrentTile());
        // full health: nothing to heal
        unit.Health = 100;
        Assert.Equal(0, unit.GetHealAmountForCurrentTile());
        Assert.False(unit.CanHealInCurrentTile());
    }

    [Fact]
    public void HealingAmount_EmbarkedAndHealOnlyByPillaging()
    {
        var w = NewWorld();
        w.TileAt(0, 0).BaseTerrain = "Coast";
        w.TileAt(0, 0).SetTerrainTransients();
        var swimmer = w.Unit("Swimmer", "Land", 0, 0);
        swimmer.Health = 10;
        Assert.Equal(0, swimmer.GetHealAmountForCurrentTile());
        var pillager = w.Unit("Pirate", "Land", 3, 3, null, "Can only heal by pillaging");
        pillager.Health = 10;
        Assert.Equal(0, pillager.GetHealAmountForCurrentTile());
    }

    [Fact]
    public void HealingAmount_AdjacentUnitsHealTheBestBonus()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.Health = 50;
        var neighbor = w.TileAt(2, 2).Neighbors[0];
        var other = w.TileAt(2, 2).Neighbors[1];
        neighbor.MilitaryUnit = w.Unit("Medic", "Land", neighbor.Position.x, neighbor.Position.y, null, "All adjacent units heal [3] HP when healing");
        other.MilitaryUnit = w.Unit("Surgeon", "Land", other.Position.x, other.Position.y, null, "All adjacent units heal [8] HP when healing");
        var foreign = MapUnitFactory.NewCiv("Egypt", w.Info);
        w.TileAt(2, 2).Neighbors[2].MilitaryUnit = w.Unit("Enemy", "Land", w.TileAt(2, 2).Neighbors[2].Position.x, w.TileAt(2, 2).Neighbors[2].Position.y, foreign, "All adjacent units heal [50] HP when healing");
        Assert.Equal(18, unit.HealingAmountForTile(w.TileAt(2, 2))); // 10 + max(3, 8); other civs do not count
    }

    // ---- garrison, escort, interception, transport

    [Fact]
    public void KindPredicates()
    {
        var w = NewWorld();
        var warrior = w.Unit();
        Assert.True(warrior.IsMilitary());
        Assert.False(warrior.IsCivilian());
        Assert.True(warrior.CanGarrison());
        Assert.False(w.Unit("Trireme", "Water", 0, 1).CanGarrison());
        Assert.False(MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0)).CanGarrison());
        Assert.False(warrior.IsGreatPerson());
        Assert.True(w.Unit("Sage", "Land", 1, 1, null, "Great Person - [Scientist]").IsGreatPerson());
        Assert.True(w.Unit("Sage2", "Land", 1, 2, null, "Great Person - [Scientist]").IsGreatPersonOfType("Scientist"));
        Assert.False(warrior.IsNuclearWeapon());
        Assert.True(w.Unit("Bomb", "Air", 0, 0, null, "Nuclear weapon of Strength [3]").IsNuclearWeapon());
        Assert.Equal(2, warrior.GetNukeBlastRadius());
        Assert.Equal(5, w.Unit("Bomb2", "Air", 0, 1, null, "Blast radius [5]").GetNukeBlastRadius());
    }

    [Fact]
    public void GetClosestCity_PicksTheFirstOfTheNearest()
    {
        var w = NewWorld();
        var unit = w.Unit();
        Assert.Null(unit.GetClosestCity());
        var far = MapUnitFactory.NewCity(w.Civ, "far", HexCoord.Of(0, 0));
        var near = MapUnitFactory.NewCity(w.Civ, "near", HexCoord.Of(2, 3));
        var alsoNear = MapUnitFactory.NewCity(w.Civ, "alsoNear", HexCoord.Of(3, 2));
        w.Civ.Cities = [far, near, alsoNear];
        Assert.Same(near, unit.GetClosestCity());
    }

    [Fact]
    public void Escorting_NeedsTheOtherUnitOnTheTile()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var military = w.Unit("Warrior");
        var civilian = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0), tile);
        tile.MilitaryUnit = military;
        Assert.Null(military.GetOtherEscortUnit());
        tile.CivilianUnit = civilian;
        Assert.Same(civilian, military.GetOtherEscortUnit());
        Assert.Same(military, civilian.GetOtherEscortUnit());
        military.StartEscorting();
        Assert.True(military.IsEscorting());
        Assert.True(civilian.IsEscorting());
        Assert.Equal(1, military.Move().PathfindingCacheCleared);
        // the partner leaves: the flag resets lazily
        tile.CivilianUnit = null;
        Assert.False(military.IsEscorting());
        tile.CivilianUnit = civilian;
        Assert.False(military.IsEscorting());
        military.StartEscorting();
        military.StopEscorting();
        Assert.False(military.IsEscorting());
        Assert.False(civilian.IsEscorting());
        // starting without a partner clears
        tile.CivilianUnit = null;
        military.StartEscorting();
        Assert.False(military.IsEscorting());
    }

    [Fact]
    public void IsIdle_ChecksMovementActionsAndTheEscortPartner()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var military = w.Unit("Warrior");
        var civilian = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0), tile);
        tile.MilitaryUnit = military;
        tile.CivilianUnit = civilian;
        Assert.False(military.IsIdle()); // no movement
        military.CurrentMovement = 2f;
        civilian.CurrentMovement = 2f;
        Assert.True(military.IsIdle());
        military.Action = "Sleep";
        Assert.False(military.IsIdle());
        military.Action = null;
        military.Automated = true;
        Assert.False(military.IsIdle());
        military.Automated = false;
        military.StartEscorting();
        civilian.Action = "Fortify";
        Assert.False(military.IsIdle()); // the partner is not idle
        Assert.True(military.IsIdle(includeOtherEscortUnit: false));
    }

    [Fact]
    public void Interception_RangeChanceAndLimits()
    {
        var w = NewWorld();
        var fighter = w.Unit("Fighter", "Air", 2, 2, null, "[50]% chance to intercept air attacks", "[2] Air Interception Range");
        fighter.BaseUnit.InterceptRange = 1;
        fighter.CurrentMovement = 8f;
        Assert.Equal(50, fighter.InterceptChance());
        Assert.Equal(3, fighter.GetInterceptionRange());
        Assert.True(fighter.CanIntercept());
        Assert.True(fighter.CanIntercept(w.TileAt(2, 4)));
        Assert.False(fighter.CanIntercept(w.TileAt(0, 4)));
        fighter.AttacksThisTurn = 1;
        Assert.False(fighter.CanIntercept());
        fighter.AttacksThisTurn = 0;
        fighter.CurrentMovement = 0f;
        Assert.False(fighter.CanIntercept()); // air units need movement left
        Assert.False(w.Unit("Warrior").CanIntercept());
    }

    [Fact]
    public void InterceptDamageModifiers()
    {
        var w = NewWorld();
        var unit = w.Unit("Fighter", "Air", 2, 2, null, "[30]% Damage when intercepting", "Damage taken from interception reduced by [50]%", "Damage taken from interception reduced by [50]%");
        Assert.Equal(30, unit.InterceptDamagePercentBonus());
        Assert.Equal(0.25f, unit.ReceivedInterceptDamageFactor());
        var heavy = w.Unit("Bomber", "Air", 2, 3, null, "Damage taken from interception reduced by [150]%");
        Assert.Equal(0f, heavy.ReceivedInterceptDamageFactor()); // never negative
        Assert.Equal(1f, w.Unit("Warrior").ReceivedInterceptDamageFactor());
    }

    [Fact]
    public void Transport_CapacityMatchingAndTheOwnerCheck()
    {
        var w = NewWorld();
        w.TileAt(2, 2).BaseTerrain = "Coast";
        w.TileAt(2, 2).SetTerrainTransients();
        var carrier = w.Unit("Carrier", "Water", 2, 2, null, "Can carry [1] [Fighter] units");
        var fighter = w.Unit("Fighter", "Air", 2, 2);
        var bomber = w.Unit("Bomber", "Air", 2, 2);
        Assert.True(carrier.IsTransportTypeOf(fighter));
        Assert.False(carrier.IsTransportTypeOf(carrier)); // only air units can be carried
        Assert.False(carrier.IsTransportTypeOf(bomber)); // filter does not match
        Assert.Equal(1, carrier.CheckCarryCapacity(fighter));
        Assert.True(carrier.CanTransport(fighter));
        fighter.IsTransported = true;
        w.TileAt(2, 2).AirUnits.Add(fighter);
        var second = w.Unit("Fighter", "Air", 2, 2);
        Assert.Equal(0, carrier.CheckCarryCapacity(second));
        Assert.False(carrier.CanTransport(second));
        // another owner cannot be carried
        var foreign = MapUnitFactory.NewCiv("Egypt", w.Info);
        Assert.False(carrier.CanTransport(w.Unit("Fighter", "Air", 2, 2, foreign)));
        Assert.Equal(0, w.Unit("Warrior").CheckCarryCapacity(fighter)); // no slots at all
    }

    [Fact]
    public void Transport_CannotBeCarriedByAndExtraSlots()
    {
        var w = NewWorld();
        var carrier = w.Unit("Carrier", "Water", 2, 2, null, "Can carry [1] [Air] units", "Can carry [1] [Fighter] units");
        var picky = w.Unit("Picky", "Air", 2, 2, null, "Cannot be carried by [Carrier] units");
        Assert.False(carrier.CanTransport(picky));
        Assert.Equal(2, carrier.CheckCarryCapacity(w.Unit("Fighter", "Air", 2, 2))); // both slots can take a Fighter
    }

    // ---- placement

    [Fact]
    public void PutInTile_PlacesByKind_AndMovesThroughTheTile()
    {
        var w = NewWorld();
        var military = MapUnitFactory.NewUnit(w.Civ, "Warrior");
        var civilian = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0));
        var air = MapUnitFactory.NewUnit(w.Civ, "Fighter", "Air");
        var tile = w.TileAt(1, 1);
        military.PutInTile(tile);
        civilian.PutInTile(tile);
        air.PutInTile(tile);
        Assert.Same(military, tile.MilitaryUnit);
        Assert.Same(civilian, tile.CivilianUnit);
        Assert.Equal([air], tile.AirUnits);
        Assert.Same(tile, military.CurrentTile);
        Assert.True(air.IsTransported); // not in a city: an air unit is carried
        Assert.Equal(GameContext.ForUnit(military), military.Cache.State);
        Assert.Equal(tile.Position, military.ViewableTiles.First(it => it == tile).Position);
    }

    [Fact]
    public void PutInTile_ThrowsWithTheMovementReason()
    {
        var w = NewWorld();
        var unit = MapUnitFactory.NewUnit(w.Civ, "Warrior");
        unit.Move().CanMoveToResult = false;
        unit.Move().CannotMoveReason = "Impassable";
        var ex = Assert.Throws<InvalidOperationException>(() => unit.PutInTile(w.TileAt(1, 1)));
        Assert.StartsWith("Unit Warrior of Rome at  can't be put in tile Tile @HexCoord(x=1, y=1)", ex.Message);
        Assert.EndsWith("reason: Impassable", ex.Message);
        Assert.Null(w.TileAt(1, 1).MilitaryUnit);
    }

    [Fact]
    public void MoveThroughTile_GrantsPromotionsFromAdjacentTerrainsToMatchingUnits()
    {
        var w = NewWorld(ruleset => ruleset.Terrains["Forest"].Uniques = ["Grants [Woodsman] ([from the forest]) to adjacent [Land] units for the rest of the game"]);
        var unit = MapUnitFactory.NewUnit(w.Civ, "Warrior");
        var sailor = MapUnitFactory.NewUnit(w.Civ, "Boat", "Water");
        w.TileAt(1, 1).Neighbors[0].AddTerrainFeature("Forest");
        Assert.Contains("Forest", w.TileAt(1, 1).Neighbors[0].AllTerrains.Select(it => it.Name));
        Assert.Single(((IHasUniques)w.TileAt(1, 1).Neighbors[0].AllTerrains[^1]).GetMatchingUniques(UniqueType.TerrainGrantsPromotion));
        unit.MoveThroughTile(w.TileAt(1, 1));
        Assert.Equal([("Woodsman", true)], unit.Promo().Added);
        sailor.MoveThroughTile(w.TileAt(1, 1));
        Assert.Empty(sailor.Promo().Added);
    }

    [Fact]
    public void MoveThroughTile_UpdatesTheViewableTiles()
    {
        var w = NewWorld();
        var unit = w.Unit("Scout", "Land", 2, 2);
        unit.UpdateVisibleTiles(updateCivViewableTiles: false);
        Assert.Contains(w.TileAt(2, 2), unit.ViewableTiles);
        Assert.Contains(w.TileAt(2, 4), unit.ViewableTiles);
        Assert.DoesNotContain(w.TileAt(0, 4), unit.ViewableTiles);
        var blind = w.Unit("Blind", "Land", 2, 2, null, "No Sight");
        blind.UpdateVisibleTiles(false);
        Assert.Equal([w.TileAt(2, 2)], blind.ViewableTiles);
        var eagle = w.Unit("Eagle", "Land", 2, 2, null, "Can see over obstacles");
        eagle.UpdateVisibleTiles(false);
        Assert.True(w.TileAt(2, 2).GetTilesInDistance(2).ToHashSet().SetEquals(eagle.ViewableTiles));
    }

    // ---- destroy, gift, disband, ownership

    [Fact]
    public void Destroy_RemovesTheUnitFromCivAndTile_AndRecordsItsAttacks()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var unit = w.Unit();
        tile.MilitaryUnit = unit;
        unit.CurrentMovement = 2f;
        unit.AttacksSinceTurnStart.Add(HexCoord.Of(3, 3));
        unit.Destroy();
        Assert.True(unit.IsDestroyed);
        Assert.Equal(0f, unit.CurrentMovement);
        Assert.Null(tile.MilitaryUnit);
        var removed = Assert.Single(((FakeUnitManager)w.Civ.Units).RemovedUnits);
        Assert.Same(unit, removed.Unit);
        Assert.True(removed.UpdateCivInfo);
        var memory = Assert.Single(w.Civ.AttacksSinceTurnStart);
        Assert.Equal(("Warrior", HexCoord.Of(2, 2), HexCoord.Of(3, 3)), (memory.AttackingUnit, memory.Source, memory.Target));
    }

    [Fact]
    public void Destroy_OfAnUnplacedUnitOnlyDetachesItFromTheCiv()
    {
        var w = NewWorld();
        var unit = MapUnitFactory.NewUnit(w.Civ, "Warrior");
        unit.Destroy();
        Assert.True(unit.IsDestroyed);
        Assert.Empty(w.Civ.AttacksSinceTurnStart);
    }

    [Fact]
    public void Destroy_TakesTheCarriedUnitsWithIt_UnlessAsked()
    {
        var w = NewWorld();
        w.TileAt(2, 2).BaseTerrain = "Coast";
        w.TileAt(2, 2).SetTerrainTransients();
        var tile = w.TileAt(2, 2);
        var carrier = w.Unit("Carrier", "Water", 2, 2, null, "Can carry [2] [Air] units");
        var fighter = w.Unit("Fighter", "Air", 2, 2);
        fighter.IsTransported = true;
        tile.MilitaryUnit = carrier;
        tile.AirUnits.Add(fighter);
        carrier.Destroy(destroyTransportedUnit: false);
        Assert.False(fighter.IsDestroyed);
        var second = w.Unit("Ferry", "Water", 2, 2);
        tile.MilitaryUnit = second;
        second.Destroy();
        Assert.False(fighter.IsDestroyed); // second carries nothing
        var carrier3 = w.Unit("Carrier", "Water", 2, 2);
        tile.MilitaryUnit = carrier3;
        Assert.True(carrier3.IsTransportTypeOf(fighter));
        carrier3.Destroy();
        Assert.True(fighter.IsDestroyed);
        Assert.Empty(tile.AirUnits);
    }

    [Fact]
    public void Destroy_StopsEscorting()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var military = w.Unit("Warrior");
        var civilian = MapUnitFactory.NewUnit(w.Civ, MapUnitFactory.NewBaseUnit(w.Ruleset, "Settler", strength: 0), tile);
        tile.MilitaryUnit = military;
        tile.CivilianUnit = civilian;
        military.StartEscorting();
        military.Destroy();
        Assert.False(civilian.IsEscorting());
    }

    [Fact]
    public void AssignOwner_SetsTheOwnerAndRegistersWithTheUnitManager()
    {
        var w = NewWorld();
        var egypt = MapUnitFactory.NewCiv("Egypt", w.Info);
        var unit = new MapUnitClass { Name = "Warrior" };
        unit.AssignOwner(egypt, updateCivInfo: false);
        Assert.Equal("Egypt", unit.Owner);
        Assert.Same(egypt, unit.Civ);
        var assigned = Assert.Single(((FakeUnitManager)egypt.Units).AssignedUnits);
        Assert.Same(unit, assigned.Unit);
        Assert.False(assigned.UpdateCivInfo);
        // the base unit is not set yet: no cache update, so no failure
        var seam = (IGameContextUnit)new MapUnitClass { Name = "Warrior" };
        seam.AssignOwner(egypt, true);
        Assert.Equal("Egypt", seam.Owner);
        Assert.Equal(2, ((FakeUnitManager)egypt.Units).AssignedUnits.Count);
    }

    [Fact]
    public void AssignOwner_RefreshesTheCacheOnceTheBaseUnitIsKnown()
    {
        var w = NewWorld();
        var unit = w.Unit("Hovercraft", "Land", 2, 2, null, "All tiles cost 1 movement");
        var egypt = MapUnitFactory.NewCiv("Egypt", w.Info);
        unit.Cache.State = GameContext.EmptyState;
        unit.AssignOwner(egypt);
        Assert.Equal(GameContext.ForUnit(unit), unit.Cache.State);
        Assert.Same(egypt, unit.Cache.State.CivInfo);
    }

    [Fact]
    public void Gift_ReassignsTheUnitToTheRecipient()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var unit = w.Unit();
        tile.MilitaryUnit = unit;
        var egypt = MapUnitFactory.NewCiv("Egypt", w.Info);
        unit.Gift(egypt);
        Assert.Equal("Egypt", unit.Owner);
        Assert.Same(egypt, unit.Civ);
        Assert.Contains(((FakeUnitManager)w.Civ.Units).RemovedUnits, it => ReferenceEquals(it.Unit, unit));
        Assert.Contains(((FakeUnitManager)egypt.Units).AssignedUnits, it => ReferenceEquals(it.Unit, unit));
    }

    [Fact]
    public void CapturedBy_TeleportsAUnitThatCannotStayOnItsTile()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var unit = w.Unit();
        tile.MilitaryUnit = unit;
        unit.CurrentMovement = 2f;
        var egypt = MapUnitFactory.NewCiv("Egypt", w.Info);
        unit.Move().CanPassThroughResult = false;
        unit.CapturedBy(egypt);
        Assert.Equal("Egypt", unit.Owner);
        Assert.Equal(0f, unit.CurrentMovement);
        Assert.Equal(1, unit.Move().Teleports);
        unit.Move().CanPassThroughResult = true;
        unit.CapturedBy(w.Civ);
        Assert.Equal(1, unit.Move().Teleports);
    }

    [Fact]
    public void Disband_DestroysTheUnit_AndIsIdempotent()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var unit = w.Unit();
        tile.MilitaryUnit = unit;
        unit.Disband();
        Assert.True(unit.IsDestroyed);
        Assert.Null(tile.MilitaryUnit);
        var removals = ((FakeUnitManager)w.Civ.Units).RemovedUnits.Count;
        unit.Disband();
        Assert.Equal(removals, ((FakeUnitManager)w.Civ.Units).RemovedUnits.Count);
    }

    [Fact]
    public void Disband_EvacuatesCarriedUnitsInACity()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var city = MapUnitFactory.NewCity(w.Civ, "Rome", tile.Position);
        w.Civ.Cities = [city];
        tile.SetOwningCity(city);
        var carrier = w.Unit("Carrier", "Water", 2, 2, null, "Can carry [2] [Air] units");
        var fighter = w.Unit("Fighter", "Air", 2, 2);
        fighter.IsTransported = true;
        tile.MilitaryUnit = carrier;
        tile.AirUnits.Add(fighter);
        carrier.Disband();
        Assert.True(carrier.IsDestroyed);
        Assert.False(fighter.IsDestroyed);
        Assert.False(fighter.IsTransported); // still in the city, no longer carried
    }

    [Fact]
    public void Disband_CarriedUnitsMoveToAnotherTile_OrAreDisbanded()
    {
        var w = NewWorld();
        var tile = w.TileAt(2, 2);
        var carrier = w.Unit("Carrier", "Water", 2, 2, null, "Can carry [2] [Air] units");
        var rescued = w.Unit("Fighter", "Air", 2, 2);
        rescued.CurrentMovement = 8f;
        rescued.IsTransported = true;
        tile.MilitaryUnit = carrier;
        tile.AirUnits.Add(rescued);
        rescued.Move().OnMoveTo = target =>
        {
            tile.AirUnits.Remove(rescued);
            rescued.CurrentTile = target;
        };
        carrier.Disband();
        Assert.Single(rescued.Move().MovedTo);
        Assert.NotSame(tile, rescued.Move().MovedTo[0]);
        Assert.False(rescued.IsDestroyed);
        // No fuel and nowhere to go: the unit is disbanded too
        var w2 = NewWorld();
        var tile2 = w2.TileAt(2, 2);
        var carrier2 = w2.Unit("Carrier", "Water", 2, 2, null, "Can carry [2] [Air] units");
        var doomed = w2.Unit("Fighter", "Air", 2, 2);
        doomed.CurrentMovement = 0f;
        doomed.IsTransported = true;
        doomed.Move().CanMoveToResult = false;
        tile2.MilitaryUnit = carrier2;
        tile2.AirUnits.Add(doomed);
        carrier2.Disband();
        Assert.True(doomed.IsDestroyed);
    }

    // ---- statuses, memories, promotions

    [Fact]
    public void SetStatus_AddsTheStatusAndItsUniques_AndKeepsTheLongerDuration()
    {
        var w = NewWorld(ruleset => ruleset.UnitPromotions.Put("Frozen", new Promotion { Name = "Frozen", Uniques = ["[+3] Movement"] }));
        var unit = w.Unit();
        Assert.Equal(2, unit.GetMaxMovement());
        unit.SetStatus("Frozen", 2);
        Assert.True(unit.HasStatus("Frozen"));
        Assert.Equal(2, unit.GetStatus("Frozen")!.TurnsLeft);
        Assert.Equal(5, unit.GetMaxMovement()); // updateUniques ran
        unit.SetStatus("Frozen", 1);
        Assert.Equal(2, unit.GetStatus("Frozen")!.TurnsLeft);
        unit.SetStatus("Frozen", 4);
        Assert.Equal(4, unit.GetStatus("Frozen")!.TurnsLeft);
        unit.RemoveStatus("Frozen");
        Assert.False(unit.HasStatus("Frozen"));
        Assert.Equal(2, unit.GetMaxMovement());
        unit.RemoveStatus("Frozen"); // removing a missing status is a no-op
        Assert.Null(unit.GetStatus("Frozen"));
    }

    [Fact]
    public void UnitStatus_UniquesComeFromThePromotionOfTheSameName()
    {
        var w = NewWorld();
        var unit = w.Unit();
        var known = new MapUnitClass.UnitStatus("Shock", 3);
        known.SetTransients(unit);
        Assert.Single(known.Uniques);
        var unknown = new MapUnitClass.UnitStatus("Mystery", 1);
        unknown.SetTransients(unit);
        Assert.Empty(unknown.Uniques);
        Assert.Throws<InvalidOperationException>(() => new MapUnitClass.UnitStatus("X", 1).Uniques);
        var parameterless = new MapUnitClass.UnitStatus();
        Assert.Equal(("", 1), (parameterless.Name, parameterless.TurnsLeft));
        Assert.Equal(("Shock", 3), (known.Clone().Name, known.Clone().TurnsLeft));
    }

    [Fact]
    public void AddMovementMemory_KeepsAtMostTwoEntries()
    {
        var w = NewWorld();
        var unit = w.Unit();
        unit.AddMovementMemory();
        unit.MostRecentMoveType = UnitMovementMemoryType.UnitAttacked;
        unit.AddMovementMemory();
        unit.MostRecentMoveType = UnitMovementMemoryType.UnitTeleported;
        unit.AddMovementMemory();
        Assert.Equal(2, unit.MovementMemories.Count);
        Assert.Equal([UnitMovementMemoryType.UnitAttacked, UnitMovementMemoryType.UnitTeleported], unit.MovementMemories.Select(it => it.Type));
        Assert.Equal([HexCoord.Of(2, 2), HexCoord.Of(2, 2)], ((IGameContextUnit)unit).MovementMemoryPositions);
        Assert.Equal("UnitMovementMemory(HexCoord(x=2, y=2), UnitAttacked)", unit.MovementMemories[0].ToString());
    }

    [Fact]
    public void GetForceEvaluation_ScalesWithPromotionsAndHealth()
    {
        var w = NewWorld();
        var unit = w.Unit();
        var baseForce = unit.BaseUnit.GetForceEvaluation();
        Assert.Equal(baseForce, unit.GetForceEvaluation()); // (0+1)^0.3 = 1, health 100
        unit.Health = 50;
        Assert.Equal(baseForce * 50 / 100, unit.GetForceEvaluation());
        unit.Health = 100;
        unit.Promo().Promotions.Add("A");
        unit.Promo().Promotions.Add("B");
        unit.Promo().Promotions.Add("C");
        var expected = (int)(baseForce * (float)System.Math.Pow(4f, 0.3f)) * 100 / 100;
        Assert.Equal(expected, unit.GetForceEvaluation());
    }

    [Fact]
    public void CopyStatisticsTo_CopiesStatsAndMissingPromotions()
    {
        var w = NewWorld();
        var source = w.Unit("Warrior", "Land", 1, 1);
        source.Health = 40;
        source.InstanceName = "Old";
        source.CurrentMovement = 1f;
        source.AttacksThisTurn = 1;
        source.IsTransported = true;
        source.Automated = true;
        source.Action = "Explore";
        var target = w.Unit("Swordsman", "Land", 2, 2);
        target.Promo().Promotions.Add("Shock");
        source.CopyStatisticsTo(target);
        Assert.Equal((40, "Old", 1f, 1, true, true, "Explore"), (target.Health, target.InstanceName, target.CurrentMovement, target.AttacksThisTurn, target.IsTransported, target.Automated, target.Action));
        Assert.Equal([("Shock", true)], source.Promo().Added); // the source gains the promotion the new unit has
        Assert.True(target.Promotions.Promotions.Contains("Shock"));
    }

    // ---- SetTransients

    [Fact]
    public void SetTransients_ResolvesTheBaseUnit_AndConvertsTheAutomateAction()
    {
        var w = NewWorld();
        MapUnitFactory.NewBaseUnit(w.Ruleset, "Worker", movement: 2, strength: 0);
        var unit = new MapUnitClass
        {
            Name = "Worker",
            Civ = w.Civ,
            Owner = "Rome",
            Action = "Automate",
            Promotions = new FakeUnitPromotions(),
            Movement = new FakeUnitMovement(),
        };
        unit.StatusMap["Shock"] = new MapUnitClass.UnitStatus("Shock", 1);
        unit.CurrentTile = w.TileAt(2, 2);
        unit.SetTransients(w.Ruleset);
        Assert.Same(w.Ruleset.Units["Worker"], unit.BaseUnit);
        Assert.Same(unit, unit.Promo().TransientsSetFor);
        Assert.True(unit.Automated);
        Assert.Null(unit.Action);
        Assert.Single(unit.StatusMap["Shock"].Uniques);
        Assert.Equal(3, unit.GetMaxMovement()); // the status unique "[+1] Movement" is part of the unique map
    }

    [Fact]
    public void SetTransients_ThrowsForAnUnknownUnit()
    {
        var w = NewWorld();
        var unit = new MapUnitClass { Name = "Ghost", Civ = w.Civ, Promotions = new FakeUnitPromotions() };
        var ex = Assert.Throws<InvalidOperationException>(() => unit.SetTransients(w.Ruleset));
        Assert.Equal("Unit Ghost is not found!", ex.Message);
    }

    // ---- visibility, threats

    [Fact]
    public void IsVisibleTo_ChecksFogAndInvisibility()
    {
        var w = NewWorld();
        var egypt = MapUnitFactory.NewCiv("Egypt", w.Info);
        var tile = w.TileAt(2, 2);
        var unit = w.Unit();
        Assert.True(unit.IsVisibleTo(w.Civ)); // own units are always visible
        Assert.False(unit.IsVisibleTo(egypt)); // not in view
        egypt.ViewableTiles = new HashSet<TileClass> { tile };
        Assert.True(unit.IsVisibleTo(egypt));
        Assert.True(((IGameContextUnit)unit).IsVisibleTo(egypt));
        var ghost = w.Unit("Ghost", "Land", 2, 2, null, "Invisible to others");
        Assert.True(ghost.HasActiveInvisibilityUnique(egypt));
        Assert.False(ghost.IsVisibleTo(egypt));
        egypt.ViewableInvisibleUnitsTiles = new Dictionary<TileClass, IReadOnlySet<string>> { [tile] = new HashSet<string> { "Ghost" } };
        Assert.True(ghost.IsVisibleTo(egypt));
        var sneak = w.Unit("Sneak", "Land", 2, 2, null, "Invisible to non-adjacent units");
        Assert.True(sneak.HasActiveInvisibilityUnique(egypt));
        w.TileAt(2, 2).Neighbors[0].MilitaryUnit = w.Unit("Spy", "Land", w.TileAt(2, 2).Neighbors[0].Position.x, w.TileAt(2, 2).Neighbors[0].Position.y, egypt);
        Assert.False(sneak.HasActiveInvisibilityUnique(egypt));
    }

    [Fact]
    public void ThreatensCiv_WhenOnOrNextToItsTerritory()
    {
        var w = NewWorld();
        var egypt = MapUnitFactory.NewCiv("Egypt", w.Info);
        var unit = w.Unit();
        Assert.False(unit.ThreatensCiv(egypt));
        var city = new CityObject { Civ = egypt };
        unit.GetTile().Neighbors[0].SetOwningCity(city);
        Assert.True(unit.ThreatensCiv(egypt));
        unit.GetTile().Neighbors[0].SetOwningCity(null);
        unit.GetTile().SetOwningCity(city);
        Assert.True(unit.ThreatensCiv(egypt));
        Assert.False(unit.ThreatensCiv(w.Civ));
    }

    // ---- seam surface

    [Fact]
    public void SeamMembers_DelegateToTheUnit()
    {
        var w = NewWorld();
        var unit = w.Unit();
        IGameContextUnit seam = unit;
        IMapUnitState state = unit;
        Assert.Equal("Warrior", seam.Name);
        Assert.Same(w.Civ, seam.Civ);
        Assert.Same(w.Civ, state.Civ);
        Assert.Same(unit.GetTile(), seam.GetTile());
        Assert.True(seam.HasTile());
        Assert.True(seam.AbilityToTimesUsedIsEmpty);
        unit.AbilityToTimesUsed["Build"] = 1;
        Assert.False(seam.AbilityToTimesUsedIsEmpty);
        Assert.True(seam.MatchesFilter("Land"));
        Assert.False(seam.CanMoveOnWater);
        Assert.Same(unit.BaseUnit, seam.BaseUnit);
        Assert.True(seam.CanMoveTo(w.TileAt(0, 0)));
        Assert.True(seam.CanPassThrough(w.TileAt(0, 0)));
        seam.SetCurrentMovement(1.25f);
        Assert.Equal(1.25f, seam.CurrentMovement);
        seam.SetOriginalOwner("Egypt");
        Assert.Equal("Egypt", unit.OriginalOwner);
        seam.SetCacheState(GameContext.EmptyState);
        Assert.Same(GameContext.EmptyState, seam.CacheState);
        seam.AddPromotion("Shock", true);
        Assert.Equal([("Shock", true)], unit.Promo().Added);
        seam.ClearPathfindingCache();
        Assert.Equal(1, unit.Move().PathfindingCacheCleared);
        state.TeleportToClosestMoveableTile();
        Assert.Equal(1, unit.Move().Teleports);
        Assert.Equal(unit.Promo().Promotions.Count, unit.Promotions.NumberOfPromotions);
        Assert.True(unit.ViewableTiles.SetEquals(state.ViewableTiles));
        Assert.False(state.HasUnique(UniqueType.NoSight));
        Assert.Empty(state.GetMatchingUniques(UniqueType.Sight));
        Assert.Equal(unit.GetForceEvaluation(), state.GetForceEvaluation());
        Assert.Equal(2, seam.GetMaxMovement());
        Assert.Same(unit.GetTile(), state.CurrentTile);
        Assert.Empty(seam.GetTriggeredUniques(UniqueType.TriggerUponEnteringTile, GameContext.EmptyState));
        Assert.NotSame(unit, seam.Clone());
        state.Destroy();
        Assert.True(unit.IsDestroyed);
        Assert.Null(unit.Religion);
    }

    [Fact]
    public void GetUniquesAndTriggeredUniques()
    {
        var w = NewWorld();
        var unit = w.Unit("Hero", "Land", 2, 2, null, "[+1] Movement", "Cannot move", "[+2] Sight <upon entering a [Hill] tile>");
        Assert.Equal(["Cannot move", "[+1] Movement", "[+2] Sight <upon entering a [Hill] tile>"], unit.GetUniques().Select(it => it.Text).Order(StringComparer.Ordinal));
        var triggered = unit.GetTriggeredUniques(UniqueType.TriggerUponEnteringTile).ToList();
        Assert.Equal(["[+2] Sight <upon entering a [Hill] tile>"], triggered.Select(it => it.Text));
        Assert.Empty(unit.GetTriggeredUniques(UniqueType.TriggerUponEnteringTile, null, it => it.Params[0] == "Forest"));
        Assert.Single(((IGameContextUnit)unit).GetTriggeredUniques(UniqueType.TriggerUponEnteringTile, GameContext.IgnoreConditionalsState));
    }

    [Fact]
    public void MoveThroughTile_TriggersUniquesOfTheEnteredTileFilter()
    {
        // UniqueTriggerActivation is the T052 stub: this only checks that the call site selects the right uniques without failing
        var w = NewWorld();
        var unit = w.Unit("Hero", "Land", 2, 2, null, "[+2] Sight <upon entering a [Hill] tile>");
        unit.MoveThroughTile(w.TileAt(1, 1));
        Assert.Same(w.TileAt(1, 1), unit.CurrentTile);
    }

    [Fact]
    public void MoveThroughTile_ClearsABarbarianEncampment_AndPaysTheCiv()
    {
        var w = NewWorld(ruleset => ruleset.TileImprovements.Put(Constants.BarbarianEncampment, new TileImprovement { Name = Constants.BarbarianEncampment }));
        w.Civ.PlayerType = PlayerType.Human;
        w.Info.SpeedData.GoldCostModifier = 1.5f;
        var geneva = MapUnitFactory.NewCiv("Geneva", w.Info);
        geneva.Nation.CityStateType = "Cultured";
        ((FakeUnitManager)geneva.Units).Count = 1; // alive
        var quests = new FakeQuestManager();
        geneva.QuestManager = quests;
        var camp = w.TileAt(1, 1);
        camp.SetImprovementBasic(Constants.BarbarianEncampment);
        var unit = MapUnitFactory.NewUnit(w.Civ, "Warrior");
        unit.MoveThroughTile(camp);
        Assert.Null(camp.Improvement);
        Assert.Equal([("Rome", camp.Position)], quests.ClearedCamps);
        Assert.Equal(37, w.Civ.Gold); // 25 * 1.5, truncated
        Assert.Contains(w.Civ.Notifications, it => it.Text.Contains("recovered [37] gold", StringComparison.Ordinal));
    }

    [Fact]
    public void MoveThroughTile_TheGermanUniqueRecruitsABarbarian()
    {
        var w = NewWorld(ruleset => ruleset.TileImprovements.Put(Constants.BarbarianEncampment, new TileImprovement { Name = Constants.BarbarianEncampment }),
            "When conquering an encampment, earn [10] Gold and recruit a Barbarian unit");
        w.Civ.PlayerType = PlayerType.Human;
        w.Info.SpeedData.GoldCostModifier = 1f;
        var recruit = MapUnitFactory.NewUnit(MapUnitFactory.NewCiv(Constants.Barbarians, w.Info), "Brute", "Land", w.TileAt(1, 1));
        recruit.Health = 20;
        recruit.CurrentMovement = 2f;
        w.Info.BarbarianFake.SpawnResult = recruit;
        var camp = w.TileAt(1, 1);
        camp.SetImprovementBasic(Constants.BarbarianEncampment);
        MapUnitFactory.NewUnit(w.Civ, "Warrior").MoveThroughTile(camp);
        Assert.Equal(100, recruit.Health);
        Assert.Equal(0f, recruit.CurrentMovement);
    }

    // ---- sub-objects of the serialized state

    [Fact]
    public void UnitStatusAndMovementMemory_RoundTripThroughJson()
    {
        var status = new MapUnitClass.UnitStatus("Frozen", 3);
        var json = UncivJson.Serialize(status);
        var back = UncivJson.Deserialize<MapUnitClass.UnitStatus>(json);
        Assert.Equal(("Frozen", 3), (back!.Name, back.TurnsLeft));
        var memory = new MapUnitClass.UnitMovementMemory(HexCoord.Of(1, 2), UnitMovementMemoryType.UnitWithdrew);
        var memoryBack = UncivJson.Deserialize<MapUnitClass.UnitMovementMemory>(UncivJson.Serialize(memory));
        Assert.Equal((HexCoord.Of(1, 2), UnitMovementMemoryType.UnitWithdrew), (memoryBack!.Position, memoryBack.Type));
    }
}
