using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.View;
using CityClass = Unciv.Logic.City.City;

namespace Unciv.Core.Tests.View;

// T085: the Views B classes (cities, constructions, units, combatants, attackable tiles) on a TestGame hex map with a player civ, its city,
// a foreign civ with a city and units. The Kotlin ViewPurityTest (shared with T084) is checked structurally in ViewTests.
[Collection("RulesetCache")]
public sealed class ViewCityUnitTests
{
    private readonly TestGame game = new();
    private readonly Civilization player;
    private readonly Civilization foreign;
    private readonly CityClass city;
    private readonly CityClass foreignCity;
    private readonly GameView view;

    public ViewCityUnitTests()
    {
        game.MakeHexagonalMap(6, Constants.Grassland);
        player = game.AddCiv(isPlayer: true);
        foreign = game.AddCiv();
        game.GameInfo.CurrentPlayerCiv = player;
        city = game.AddCity(player, game.GetTile(new HexCoord(0, 0)), initialPopulation: 3);
        foreignCity = game.AddCity(foreign, game.GetTile(new HexCoord(4, 0)));
        foreach (var civ in new[] { player, foreign }) civ.Cache.UpdateState();
        player.DiplomacyFunctions.MakeCivilizationsMeet(foreign);
        view = new GameView(game.GameInfo, player);
        foreach (var tile in game.TileMap.Values) tile.SetExplored(player, true);
    }

    private MapUnit AddUnit(string name, Civilization civ, int x, int y) => game.AddUnit(name, civ, game.GetTile(new HexCoord(x, y)));

    // ---- ForeignCityView ----

    [Fact]
    public void ForeignCityView_ExposesPublicCityData()
    {
        var own = view.GetForeignCityView(city);
        var other = view.GetForeignCityView(foreignCity);
        Assert.Equal(city.Name, own.Name);
        Assert.Equal(player, own.OwningCivView.GetCiv());
        Assert.Equal(foreign.CivName, other.OwningCiv().CivName);
        Assert.True(own.IsOwnedByViewer());
        Assert.False(other.IsOwnedByViewer());
        Assert.Equal(city.Health, own.GetHealth());
        Assert.Equal(city.GetMaxHealth(), own.GetMaxHealth());
        Assert.Equal(3, own.GetPopulationCount());
        Assert.True(own.IsCapital());
        Assert.False(own.IsPuppet());
        Assert.False(own.IsBeingRazed());
        Assert.False(own.IsInResistance());
        Assert.False(own.IsWeLoveTheKingDayActive());
        Assert.False(own.IsBlockaded());
        Assert.True(own.IsConnectedToCapital());
        Assert.NotNull(own.GetProductionMarkup());
        Assert.Equal(city.GetCenterTile().Position, own.GetCenterTile().Position());
        Assert.Equal(own.GetCenterTile().Position(), own.Location());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(city).GetDefendingStrength(), own.GetDefendingStrength());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(city).GetAttackingStrength(), own.GetAttackingStrength());
        Assert.Equal(city.CanBombard(), own.CanBombard());
        Assert.Equal(city.GetBombardRange(), own.GetBombardRange());
        Assert.False(own.IsCityState());
        Assert.Equal(foreign.Nation.GetOuterColor(), other.GetNationOuterColor());
        Assert.Equal(foreign.Nation.GetInnerColor(), other.GetCivInnerColor());
        var cityState = game.AddCiv(cityStateType: "Cultured");
        var csCity = game.AddCity(cityState, game.GetTile(new HexCoord(-4, 0)));
        Assert.Equal("Cultured", view.GetForeignCityView(csCity).GetCityStateTypeName());
        Assert.True(view.GetForeignCityView(csCity).IsCityState());
    }

    [Fact]
    public void ForeignCityView_NeighboursDiplomacyAndReligion()
    {
        var own = view.GetForeignCityView(city);
        Assert.Contains(foreignCity, own.GetNeighboringCities().Select(it => it.Unwrap()));
        Assert.DoesNotContain(foreignCity, own.GetNeighboringCities(2).Select(it => it.Unwrap()));
        Assert.DoesNotContain(city, own.GetNeighboringCities().Select(it => it.Unwrap()));
        Assert.False(own.IsAtWarWith(foreign));
        Assert.True(own.CivKnows(foreign));
        Assert.True(own.IsKnownTo(foreign));
        Assert.Same(player.GetDiplomacyManager(foreign), own.GetDiplomacyManagerWith(foreign));
        Assert.Equal(game.GameInfo.IsEspionageEnabled(), own.IsEspionageEnabled());
        Assert.Equal(game.GameInfo.IsReligionEnabled(), own.IsReligionEnabled());
        Assert.False(own.SpyIsSetUpAtCity(foreign));
        Assert.Null(own.GetMajorityReligion());
        Assert.Empty(own.GetNumberOfFollowers());
        Assert.Empty(own.GetPressuresFromSurroundingCities());
        Assert.Null(own.GetReligionThisIsTheHolyCityOf());
        Assert.False(own.IsBlockedHolyCity());
        Assert.Null(own.GetReligion(null));
        Assert.Null(own.GetReligion("Nope"));
        var religion = game.AddReligion(player);
        Assert.Same(religion, own.GetReligion(religion.Name));
    }

    [Fact]
    public void TryGetCityView_NeedsOwnershipSpectatorSpyOrDebugVisibility()
    {
        Assert.NotNull(view.GetForeignCityView(city).TryGetCityView());
        Assert.Null(view.GetForeignCityView(foreignCity).TryGetCityView());
        game.GameInfo.EngineOptions.VisibleMap = true;
        Assert.NotNull(view.GetForeignCityView(foreignCity).TryGetCityView());
        game.GameInfo.EngineOptions.VisibleMap = false;
        var spectator = game.AddCiv(new Unciv.Models.Ruleset.Nation.Nation { Name = Constants.Spectator });
        var specView = new GameView(game.GameInfo, spectator);
        Assert.True(spectator.IsSpectator());
        Assert.NotNull(specView.GetForeignCityView(foreignCity).TryGetCityView());
    }

    [Fact]
    public void ForeignCityView_AsCombatant_WrapsTheCity()
    {
        var combatant = view.GetForeignCityView(foreignCity).AsCombatant();
        Assert.True(combatant.IsCity());
        Assert.True(combatant.IsRanged());
        Assert.False(combatant.IsDefeated());
        Assert.Equal(foreignCity.GetCenterTile(), combatant.GetTile().Unwrap());
        Assert.Equal(foreign, combatant.GetCivInfo().GetCiv());
        Assert.Equal(foreignCity.Name, combatant.GetCombatantName());
        Assert.Equal(foreignCity.Health, combatant.GetHealth());
        Assert.Equal(foreignCity.GetMaxHealth(), combatant.GetMaxHealth());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(foreignCity).GetUnitType(), combatant.GetUnitType());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(foreignCity).GetAttackingStrength(), combatant.GetAttackingStrength());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(foreignCity).GetDefendingStrength(), combatant.GetDefendingStrength());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(foreignCity).CanAttack(), combatant.CanAttack());
        Assert.Equal(new Unciv.Logic.Battle.CityCombatant(foreignCity).GetAttackSound(), combatant.GetAttackSound());
        Assert.Same(view.GetForeignCityView(foreignCity).Unwrap(), ((CityCombatantView)combatant).GetCityView().Unwrap());
    }

    // ---- CityView ----

    private CityView CityView() => view.GetCityView(city);

    [Fact]
    public void CityView_NavigationAndTiles()
    {
        var cv = CityView();
        Assert.Same(view.CivView.Unwrap(), cv.ViewingCiv().Unwrap());
        Assert.Equal([city], cv.GetViewableCities().Select(it => it.Unwrap()));
        var centre = cv.GetCenterTile();
        Assert.True(cv.IsInRange(centre));
        Assert.Equal(city.GetWorkRange(), cv.GetWorkRange());
        Assert.Equal(city.GetTiles().Count(), cv.GetTiles().Count());
        Assert.True(cv.IsOwnedTile(centre));
        var far = view.GetTile(game.GetTile(new HexCoord(-5, 0)));
        Assert.False(cv.IsOwnedTile(far));
        Assert.False(cv.IsInRange(far));
        Assert.Equal(city.IsWorked(centre.Unwrap()), cv.IsWorked(centre));
        Assert.Equal(city.Expansion.CanBuyTile(far.Unwrap()), cv.CanBuyTile(far));
        Assert.Equal(city.Expansion.GetGoldCostOfTile(far.Unwrap(), 2), cv.GetGoldCostOfTile(far, 2));
        Assert.Equal(city.Expansion.GetChoosableTiles().Any(), cv.HasChoosableTiles());
        Assert.Equal(city.Expansion.GetCultureToNextTile(), cv.GetCultureToNextTile());
        Assert.Equal(city.Expansion.CultureStored, cv.GetCultureStored());
        Assert.Equal(city.GetExpandRange(), cv.GetExpandRange());
        Assert.Equal(city.Expansion.ChooseNewTileToOwn() == null, cv.ChooseNewTileToOwn() == null);
        Assert.NotNull(cv.GetStatDiffForImprovement(centre, game.Ruleset.TileImprovements["Farm"]));
    }

    [Fact]
    public void CityView_PopulationStatsAndState()
    {
        var cv = CityView();
        Assert.Equal(city.Population.GetFreePopulation(), cv.GetFreePopulation());
        Assert.Equal(city.Population.FoodStored, cv.GetFoodStored());
        Assert.Equal(city.Population.GetFoodToNextPopulation(), cv.GetFoodToNextPopulation());
        Assert.Equal(city.Population.GetMaxSpecialists().Count, cv.GetMaxSpecialists().Count);
        Assert.Equal(city.Population.GetNewSpecialists().Count, cv.GetNewSpecialists().Count);
        Assert.Equal(city.ManualSpecialists, cv.ManualSpecialists);
        Assert.Equal(city.Population.GetNumTurnsToStarvation(), cv.GetNumTurnsToStarvation());
        Assert.Equal(city.Population.GetNumTurnsToNewPopulation(), cv.GetNumTurnsToNewPopulation());
        Assert.Equal(city.FoodForNextTurn(), cv.FoodForNextTurn());
        Assert.Equal(cv.FoodForNextTurn() < 0, cv.IsStarving());
        Assert.Equal(cv.FoodForNextTurn() > 0, cv.IsGrowing());
        city.SetFlag(Unciv.Logic.City.CityFlags.Resistance, 3);
        Assert.Equal(city.DemandedResource, cv.DemandedResource);
        Assert.Equal(3, cv.GetFlag(Unciv.Logic.City.CityFlags.Resistance));
        Assert.Equal(city.GetCityFocus(), cv.GetCityFocus());
        Assert.Equal(city.AvoidGrowth, cv.AvoidGrowth);
        Assert.Same(city.State, cv.GetState());
        Assert.Same(city.CityStats.CurrentCityStats, cv.GetCurrentCityStats());
        Assert.Same(city.CityStats.HappinessList, cv.GetHappinessList());
        Assert.Same(city.CityStats.BaseStatTree, cv.GetBaseStatTree());
        Assert.Same(city.CityStats.StatPercentBonusTree, cv.GetStatPercentBonusTree());
        Assert.Same(city.CityStats.FinalStatList, cv.GetFinalStatList());
        Assert.NotNull(cv.GetStatsOfSpecialist(game.CreateSpecialist()));
        Assert.Equal(city.GetStatReserve(Stat.Gold), cv.GetStatReserve(Stat.Gold));
        Assert.Same(game.Ruleset, cv.GetRuleset());
        Assert.Same(city.ResourceStockpiles, cv.GetResourceStockpiles());
        Assert.NotNull(cv.GetCityResourcesAvailableToCity());
        Assert.NotNull(cv.GetGreatPersonPointsBreakdown());
        Assert.Equal(city.HasSoldBuildingThisTurn, cv.HasSoldBuildingThisTurn());
        Assert.False(cv.IsGodModeEnabled());
        Assert.Equal(city.CanBeDestroyed(), cv.CanBeDestroyed());
        Assert.Same(city.DisabledConstructions, cv.GetDisabledConstructions());
        Assert.Equal(city.CityConstructions.GetBuiltBuildings().Count(), cv.GetBuiltBuildings().Count());
        Assert.Equal(city.GetMatchingUniques(UniqueType.IndicatesCapital).Any(), cv.HasMatchingUnique(UniqueType.IndicatesCapital));
        Assert.Equal(city.GetGoldForSellingBuilding("Palace"), cv.GetGoldForSellingBuilding("Palace"));
    }

    [Fact]
    public void CityView_ConstructionQueries()
    {
        var cv = CityView();
        var monument = game.Ruleset.Buildings["Monument"];
        var warrior = game.Ruleset.Units["Warrior"];
        var constructions = cv.Constructions;
        Assert.Equal(city.CityConstructions.CurrentConstructionName(), constructions.CurrentConstructionName());
        Assert.Equal(constructions.CurrentConstructionName(), cv.CurrentConstructionName());
        Assert.Same(constructions.GetConstruction(constructions.CurrentConstructionName()), constructions.GetCurrentConstruction());
        Assert.Equal(city.CityConstructions.ConstructionQueue, constructions.ConstructionQueue);
        Assert.Equal(constructions.ConstructionQueue.Contains("Monument"), constructions.IsBeingConstructedOrEnqueued("Monument"));
        Assert.Equal(city.CityConstructions.IsBuilt("Palace"), constructions.IsBuilt("Palace"));
        Assert.Equal(city.CityConstructions.GetWorkDone("Monument"), constructions.GetWorkDone("Monument"));
        Assert.Equal(city.CityConstructions.TurnsToConstruction("Monument", true), constructions.TurnsToConstruction("Monument"));
        Assert.Equal(city.CityConstructions.TurnsToConstruction("Monument", false), constructions.TurnsToConstruction("Monument", false));
        Assert.Equal(monument.IsBuildable(city.CityConstructions), constructions.IsBuildable(monument));
        Assert.Equal(monument.ShouldBeDisplayed(city.CityConstructions), constructions.ShouldBeDisplayed(monument));
        Assert.Equal(monument.GetRejectionReasons(city.CityConstructions).Count(), constructions.GetRejectionReasons(monument).Count());
        Assert.Equal(city.CityConstructions.IsQueueFull(), constructions.IsQueueFull());
        Assert.Equal(city.CityConstructions.CanAddToQueue(monument), constructions.CanAddToQueue(monument));
        Assert.Equal(monument.GetStatBuyCost(city, Stat.Gold), constructions.GetStatBuyCost(monument, Stat.Gold));
        Assert.Equal(city.CityConstructions.IsConstructionPurchaseAllowed(monument, Stat.Gold, 10), constructions.IsConstructionPurchaseAllowed(monument, Stat.Gold, 10));
        Assert.Equal(city.CityConstructions.IsConstructionPurchaseBlockedByUnit(warrior), constructions.IsConstructionPurchaseBlockedByUnit(warrior));
        Assert.Equal(city.CityConstructions.IsFirstConstructionOfItsKind(0, "Palace"), constructions.IsFirstConstructionOfItsKind(0, "Palace"));
        Assert.Null(constructions.GetTileForImprovement("Nonexistent"));
        var farm = game.Ruleset.TileImprovements["Farm"];
        Assert.Equal(city.CityConstructions.CanPlaceCreateOneImprovementOn(farm, city.GetCenterTile()), constructions.CanPlaceCreateOneImprovementOn(farm, cv.GetCenterTile()));
        Assert.Equal(game.Ruleset.Buildings["Library"].GetStats(city).ToString(), cv.GetBuildingStats(game.Ruleset.Buildings["Library"]).ToString());
        Assert.Equal(monument.GetProductionCost(player, city), cv.GetConstructionProductionCost(monument));
        Assert.Equal(warrior.GetDescription(city), cv.GetUnitDescription(warrior));
        Assert.Equal(monument.GetDescription(city, true), cv.GetBuildingDescription(monument));
        Assert.Equal(monument.IsStatRelated(Stat.Culture, city), cv.IsStatRelated(Stat.Culture, monument));
        Assert.Equal(((INonPerpetualConstruction)monument).CanBePurchasedWithAnyStat(city), cv.CanBePurchasedWithAnyStat(monument));
        Assert.Equal(((INonPerpetualConstruction)monument).CanBePurchasedWithStat(city, Stat.Gold), cv.CanBePurchasedWithStat(monument, Stat.Gold));
        Assert.Equal(monument.GetImprovementToCreate(game.Ruleset, player), cv.GetImprovementToCreate(monument));
        Assert.Equal(player.CivConstructions.HasFreeBuilding(city, monument), cv.HasFreeBuilding(monument));
        Assert.Equal(warrior.GetResourceRequirementsPerTurn(player.State).Count, cv.GetResourceRequirementsPerTurn(warrior).Count);
        Assert.Equal(((IConstruction)monument).GetResourceRequirementsPerTurn(city.State).Count, cv.GetResourceRequirementsPerTurn(monument).Count);
        Assert.Equal(((IConstruction)monument).GetStockpiledResourceRequirements(city.State).Count, cv.GetStockpiledResourceRequirements(monument).Count);
        Assert.Null(cv.GetUnitShouldUseSavedPromotion("Warrior"));
        city.UnitShouldUseSavedPromotion["Warrior"] = true;
        Assert.True(cv.GetUnitShouldUseSavedPromotion("Warrior"));
    }

    [Fact]
    public void CityView_ConstructionActions_AndPurchase()
    {
        var cv = CityView();
        Assert.True(cv.TryAddToQueue("Monument"));
        Assert.Contains("Monument", cv.Constructions.ConstructionQueue);
        Assert.True(cv.TryAddToQueueConstruction(game.Ruleset.Units["Warrior"], addToTop: true));
        Assert.Equal("Warrior", cv.Constructions.ConstructionQueue[0]);
        Assert.True(cv.TryAddToQueueWithTile(game.Ruleset.Buildings["Library"], cv.GetCenterTile()));
        var index = cv.Constructions.ConstructionQueue.Count - 1;
        Assert.Equal(index - 1, cv.TryRaisePriority(index));
        Assert.Equal(index, cv.TryLowerPriority(index - 1));
        Assert.True(cv.TryMoveEntryToTop(index));
        Assert.True(cv.TryMoveEntryToEnd(0));
        Assert.True(cv.TryRemoveFromQueue(0, false));
        Assert.True(cv.TryRemoveAllByName("Monument"));
        Assert.DoesNotContain("Monument", cv.Constructions.ConstructionQueue);
        Assert.True(cv.TryDisableConstruction("Library"));
        Assert.Contains("Library", city.DisabledConstructions);
        Assert.True(cv.TryEnableConstruction("Library"));
        Assert.DoesNotContain("Library", city.DisabledConstructions);
        Assert.True(cv.TrySetUnitShouldUseSavedPromotion("Warrior", true));
        Assert.True(city.UnitShouldUseSavedPromotion["Warrior"]);
        player.AddGold(1000);
        var warrior = game.Ruleset.Units["Warrior"];
        var cost = cv.Constructions.GetStatBuyCost(warrior, Stat.Gold);
        Assert.NotNull(cost);
        var before = player.Gold;
        Assert.True(cv.Constructions.PurchaseConstruction(warrior, 0, Stat.Gold, null));
        Assert.True(player.Gold < before);
    }

    [Fact]
    public void CityView_Actions_AreRefusedUnlessTheViewerIsTheCurrentOwner()
    {
        var foreignView = new GameView(game.GameInfo, foreign).GetCityView(city);
        var centre = foreignView.GetCenterTile();
        Assert.False(foreignView.TryLockTile(centre));
        Assert.False(foreignView.TryUnlockTile(centre));
        Assert.False(foreignView.TryBuyTile(centre));
        Assert.False(foreignView.TryWorkTile(centre));
        Assert.False(foreignView.TryStopWorkingTile(centre));
        Assert.False(foreignView.TryAddToQueue("Monument"));
        Assert.False(foreignView.TryRemoveFromQueue(0, false));
        Assert.Null(foreignView.TryRaisePriority(0));
        Assert.Null(foreignView.TryLowerPriority(0));
        Assert.False(foreignView.TryRenameCity("X"));
        Assert.False(foreignView.TryAnnexCity());
        Assert.False(foreignView.TrySetRazing(true));
        Assert.False(foreignView.TryAddToQueueWithTile(game.Ruleset.Buildings["Library"], centre));
        Assert.False(foreignView.TrySetUnitShouldUseSavedPromotion("Warrior", true));
        Assert.False(foreignView.TrySellBuilding(game.Ruleset.Buildings["Palace"]));
        Assert.False(foreignView.TryMoveEntryToTop(0));
        Assert.False(foreignView.TryMoveEntryToEnd(0));
        Assert.False(foreignView.TryAddToQueueConstruction(game.Ruleset.Units["Warrior"]));
        Assert.False(foreignView.TryRemoveAllByName("Monument"));
        Assert.False(foreignView.TryDisableConstruction("Library"));
        Assert.False(foreignView.TryEnableConstruction("Library"));
        Assert.False(foreignView.TryReassignPopulation());
        Assert.False(foreignView.TryToggleAvoidGrowth());
        Assert.False(foreignView.TryEnableManualSpecialists());
        Assert.False(foreignView.TryDisableManualSpecialists());
        Assert.False(foreignView.TryAssignSpecialist("x"));
        Assert.False(foreignView.TryUnassignSpecialist("x"));
        Assert.False(foreignView.TrySetCityFocus(Unciv.Logic.City.CityFocus.NoFocus));
        Assert.NotEqual("X", city.Name);
        // Not the current player: the owner is refused too
        game.GameInfo.CurrentPlayerCiv = foreign;
        Assert.False(CityView().TryRenameCity("X"));
        Assert.NotEqual("X", city.Name);
    }

    [Fact]
    public void CityView_Actions_ChangeTheCity()
    {
        var cv = CityView();
        Assert.True(cv.TryRenameCity("Renamed"));
        Assert.Equal("Renamed", city.Name);
        Assert.True(cv.TrySetRazing(true));
        Assert.True(city.IsBeingRazed);
        Assert.True(cv.TrySetRazing(false));
        Assert.True(cv.TryToggleAvoidGrowth());
        Assert.True(city.AvoidGrowth);
        Assert.True(cv.TryToggleAvoidGrowth());
        Assert.False(city.AvoidGrowth);
        Assert.True(cv.TryEnableManualSpecialists());
        Assert.True(city.ManualSpecialists);
        Assert.True(cv.TryDisableManualSpecialists());
        Assert.False(city.ManualSpecialists);
        Assert.True(cv.TryReassignPopulation(true));
        Assert.True(cv.TrySetCityFocus(Unciv.Logic.City.CityFocus.GoldFocus));
        Assert.Equal(Unciv.Logic.City.CityFocus.GoldFocus, city.GetCityFocus());
        Assert.True(cv.UpdateTileStats());
        Assert.True(cv.UpdateCityStats());

        var specialist = game.CreateSpecialist();
        Assert.True(cv.TryAssignSpecialist(specialist));
        Assert.Equal(1, city.Population.SpecialistAllocations[specialist]);
        Assert.True(city.ManualSpecialists);
        Assert.True(cv.TryUnassignSpecialist(specialist));
        Assert.Equal(0, city.Population.SpecialistAllocations[specialist]);

        var workable = cv.GetTiles().First(it => !it.IsCityCenter());
        cv.TryStopWorkingTile(workable);
        Assert.False(cv.IsWorked(workable));
        Assert.True(cv.TryWorkTile(workable));
        Assert.True(cv.IsWorked(workable));
        Assert.True(cv.TryLockTile(workable));
        Assert.True(city.LockedTiles.Contains(workable.Position()));
        Assert.True(cv.TryUnlockTile(workable));
        Assert.False(cv.TryLockTile(view.GetTile(game.GetTile(new HexCoord(-5, 0))))); // not worked

        var far = game.TileMap.Values.Select(it => view.GetTile(it)).First(it => cv.CanBuyTile(it));
        player.AddGold(100000);
        Assert.True(cv.TryBuyTile(far));
        Assert.True(cv.IsOwnedTile(far));
        Assert.False(cv.TryBuyTile(far));

        var library = game.Ruleset.Buildings["Library"];
        city.CityConstructions.AddBuilding(library);
        Assert.True(cv.TrySellBuilding(library));
        Assert.False(city.CityConstructions.IsBuilt("Library"));
    }

    [Fact]
    public void CityView_Garrison_AndViewableCitiesWhileSpying()
    {
        var cv = CityView();
        Assert.Null(cv.GetGarrison());
        var unit = AddUnit("Warrior", player, 0, 0);
        Assert.Equal(unit, cv.GetGarrison()?.Unwrap());
        Assert.False(cv.Constructions.IsBuilt("Nope"));
    }

    // ---- ForeignMapUnitView / MapUnitView ----

    [Fact]
    public void ForeignMapUnitView_ExposesUnitData()
    {
        var own = AddUnit("Warrior", player, 1, 0);
        var enemy = AddUnit("Warrior", foreign, 2, 0);
        var fv = view.GetForeignMapUnitView(enemy);
        Assert.Equal("Warrior", fv.Name);
        Assert.Equal(foreign.CivName, fv.CivName);
        Assert.Equal(enemy.Health, fv.UnitHealth);
        Assert.Equal(enemy.ReligiousStrengthLost, fv.ReligiousStrengthLost);
        Assert.Equal(enemy.Religion, fv.Religion);
        Assert.Equal(enemy.Id, fv.Id);
        Assert.Equal(enemy.InstanceName, fv.InstanceName);
        Assert.Equal(enemy.CurrentMovement, fv.CurrentMovement);
        Assert.Equal(enemy.AttacksThisTurn, fv.AttacksThisTurn);
        Assert.False(fv.IsOwnedByViewer());
        Assert.Equal(foreign, fv.Civ().GetCiv());
        Assert.Null(fv.TryGetMapUnitView());
        Assert.Equal(own, view.GetForeignMapUnitView(own).TryGetMapUnitView()?.Unwrap());
        Assert.Equal(enemy.GetTile(), fv.GetTile().Unwrap());
        Assert.Equal(enemy.CanAttack(), fv.CanAttack());
        Assert.Equal(enemy.IsEmbarked(), fv.IsEmbarked());
        Assert.Equal(enemy.DisplayName(), fv.DisplayName());
        Assert.False(fv.IsTransportTypeOf(view.GetForeignMapUnitView(own)));
        Assert.Same(enemy.BaseUnit, fv.GetBaseUnit());
        Assert.Equal(enemy.GetRange(), fv.GetRange());
        Assert.Equal(enemy.GetMaxMovement(), fv.GetMaxMovement());
        Assert.Equal(enemy.GetInterceptionRange(), fv.GetInterceptionRange());
        Assert.Same(enemy.StatusMap, fv.GetStatusMap());
        Assert.Same(enemy.MovementMemories, fv.GetMovementMemories());
        Assert.Equal(enemy.MostRecentMoveType, fv.GetMostRecentMoveType());
        Assert.Equal(enemy.HasUnique(UniqueType.NuclearWeapon), fv.HasUnique(UniqueType.NuclearWeapon));
        Assert.Equal(enemy.GetMatchingUniques(UniqueType.NuclearWeapon).Count(), fv.GetMatchingUniques(UniqueType.NuclearWeapon).Count());
        Assert.False(fv.HasReachedMaxXPFromBarbarians());
        Assert.Equal(enemy.IsIdle(), fv.IsIdle());
        Assert.False(fv.IsFortified());
        Assert.False(fv.IsGuarding());
        Assert.False(fv.IsAirUnit());
        Assert.False(fv.IsCivilian());
        Assert.True(fv.IsMilitary());
        Assert.True(fv.HasMovement());
        enemy.CurrentMovement = 0;
        Assert.False(fv.HasMovement());
    }

    [Fact]
    public void ForeignMapUnitView_AsCombatant_AndTileViewCombatants()
    {
        var enemy = AddUnit("Warrior", foreign, 2, 0);
        var worker = AddUnit("Worker", foreign, 2, 0);
        var combatant = view.GetForeignMapUnitView(enemy).AsCombatant();
        Assert.False(combatant.IsCity());
        Assert.False(combatant.IsRanged());
        Assert.Equal("Warrior", combatant.GetCombatantName());
        Assert.Equal(enemy.Health, combatant.GetHealth());
        Assert.Equal(enemy.HasUnique(UniqueType.NuclearWeapon), ((MapUnitCombatantView)combatant).HasUnique(UniqueType.NuclearWeapon));
        Assert.Equal("Warrior", ((MapUnitCombatantView)combatant).GetUnitView().Name);

        var tileView = view.GetTile(enemy.GetTile());
        Assert.Equal("Warrior", ((MapUnitCombatantView)tileView.GetCombatant()!).GetUnitView().Name);
        Assert.Equal(enemy, tileView.MilitaryUnit?.Unwrap());
        Assert.Equal(worker, tileView.CivilianUnit?.Unwrap());
        Assert.Equal(2, tileView.GetVisibleUnits().Count);
        Assert.Equal(foreignCity, ((CityCombatantView)view.GetTile(foreignCity.GetCenterTile()).GetCombatant()!).GetCityView().Unwrap());
        Assert.Null(view.GetTile(game.GetTile(new HexCoord(0, 3))).GetCombatant());
        Assert.Equal(foreignCity, view.GetTile(foreignCity.GetCenterTile()).GetCity()?.Unwrap());
        Assert.Equal(foreignCity, view.GetTile(foreignCity.GetCenterTile()).OwningCity()?.Unwrap());
        Assert.Null(view.GetTile(game.GetTile(new HexCoord(0, 3))).GetCity());
        // Only a worker stands here: the civilian is the combatant
        var lonely = AddUnit("Worker", foreign, 2, 1);
        Assert.Equal(lonely, ((MapUnitCombatantView)view.GetTile(lonely.GetTile()).GetCombatant()!).GetUnitView().Unwrap());
        // Unexplored tiles reveal nothing
        lonely.GetTile().SetExplored(player, false);
        Assert.Null(view.GetTile(lonely.GetTile()).GetCombatant());
        // No viewer, no units
        var noViewer = new TileMapView(game.TileMap, null).GetTile(enemy.GetTile());
        Assert.Empty(noViewer.GetVisibleUnits());
        Assert.Null(noViewer.GetCombatant());
        Assert.Null(noViewer.MilitaryUnit);
        Assert.Null(noViewer.GetCity());
    }

    [Fact]
    public void TileView_UnitsHiddenFromTheViewerAreNotVisible()
    {
        var submarineTile = game.GetTile(new HexCoord(3, 0));
        var enemy = AddUnit("Warrior", foreign, 3, 0);
        var tileView = view.GetTile(enemy.GetTile());
        Assert.Equal(enemy.IsVisibleTo(player), tileView.MilitaryUnit != null);
        Assert.Equal(submarineTile, enemy.GetTile());
    }

    private MapUnitView OwnUnit(string name = "Warrior", int x = 1, int y = 0) => view.GetMapUnitView(AddUnit(name, player, x, y));

    [Fact]
    public void MapUnitView_DataRetrieval()
    {
        var uv = OwnUnit();
        var unit = uv.Unwrap();
        Assert.Equal(unit.Due, uv.Due);
        Assert.Equal(unit.Promotions.XP, uv.Xp);
        Assert.False(uv.IsDestroyed());
        Assert.Equal(unit.Promotions.XpForNextPromotion(), uv.XpForNextPromotion());
        Assert.Equal(unit.Promotions.CanBePromoted(), uv.CanBePromoted());
        Assert.Same(unit.Promotions.Promotions, uv.GetPromotionNames());
        Assert.Equal(unit.Promotions.GetPromotions(true).Count(), uv.GetPromotions().Count());
        Assert.Equal(unit.Upgrade.GetUpgradesOfKind(false).Count, uv.GetPossibleUpgrades().Count);
        Assert.Equal(unit.Cache.HasUniqueToBuildImprovements, uv.HasUniqueToBuildImprovements());
        Assert.Equal(unit.Cache.HasUniqueToCreateWaterImprovements, uv.HasUniqueToCreateWaterImprovements());
        Assert.Equal(unit.Cache.HasStrengthBonusInRadiusUnique, uv.HasStrengthBonusInRadiusUnique());
        Assert.Equal(unit.Cache.HasCitadelPlacementUnique, uv.HasCitadelPlacementUnique());
        Assert.Equal(unit.Promotions.GetPromotionTreeCandidates().Count, uv.GetAvailablePromotions().Count);
        Assert.Equal(unit.Promotions.CanAffordPromotions(1), uv.CanAffordPromotions(1));
        Assert.True(uv.CanSaveDefaultPromotions());
        Assert.Equal(player, uv.Civ().GetCiv());
        Assert.IsType<CivView>(uv.Civ());
        Assert.IsType<CivView>(((ForeignMapUnitView)uv).Civ()); // Kotlin `override fun civ()`: virtual dispatch through the base type
        Assert.Null(uv.GetOtherEscortUnit());
        Assert.Equal(unit.Action, uv.Action);
        Assert.False(uv.IsEscorting());
        Assert.False(uv.IsAutomated());
        Assert.Equal(unit.Movement.GetDistanceToTiles().Count, uv.GetDistanceToTiles().Count);
        Assert.False(uv.HasDisappeared());
        Assert.Equal(unit.MaxAttacksPerTurn(), uv.MaxAttacksPerTurn());
        Assert.Equal(unit.Cache.CannotMove, uv.CannotMove());
        Assert.Equal(unit.GetVisibilityRange(), uv.GetVisibilityRange());
        Assert.Equal(unit.ViewableTiles.Count, uv.GetViewableTiles().Count);
        Assert.Equal(unit.Movement.GetReachableTilesInCurrentTurn().Count(), uv.GetReachableTilesInCurrentTurn().Count);
        Assert.Equal(unit.Movement.GetUnitSwappableTiles().Count(), uv.GetUnitSwappableTiles().Count);
        Assert.Equal(unit.GetRange() == 0 ? 0 : 6, uv.GetTilesInAttackRange().Count);
        Assert.Null(uv.GetFutureAutomatedRoadConnectionTiles());
        Assert.NotNull(uv.GetAirSweepAttackModifiers());
        Assert.False(uv.IsNuclearWeapon());
        Assert.Equal(2, uv.GetNukeBlastRadius());
        Assert.Null(uv.GetAirUnitMovementRange());
        Assert.False(uv.IsMoving());
        Assert.False(uv.IsExploring());
        Assert.False(uv.IsAutomatingRoadConnection());
        Assert.False(uv.IsPreparingParadrop());
        Assert.False(uv.IsPreparingAirSweep());
        Assert.Null(uv.GetImprovementInProgress());
        Assert.False(uv.CanBuildCurrentImprovement());
    }

    [Fact]
    public void MapUnitView_MovementQueries()
    {
        var uv = OwnUnit();
        var unit = uv.Unwrap();
        var next = view.GetTile(game.GetTile(new HexCoord(2, 0)));
        var distances = uv.GetDistanceToTiles();
        Assert.True(distances.ContainsKey(next));
        Assert.Equal(unit.Movement.GetDistanceToTiles()[next.Unwrap()].TotalMovement, distances[next]);
        Assert.Equal(unit.Movement.CanReach(next.Unwrap()), uv.CanReach(next));
        Assert.Equal(unit.Movement.CanMoveTo(next.Unwrap()), uv.CanMoveTo(next));
        Assert.Equal(unit.Movement.CanUnitSwapTo(next.Unwrap()), uv.CanSwapTo(next));
        Assert.Equal(unit.Movement.IsUnknownTileWeShouldAssumeToBePassable(next.Unwrap()), uv.IsUnknownTileWeShouldAssumeToBePassable(next));
        Assert.Equal(unit.GetDamageFromTerrain(next.Unwrap()), uv.GetDamageFromTerrain(next));
        Assert.Equal(next.Unwrap().GetDefensiveBonus(true, unit), uv.GetDefensiveBonusOn(next));
        Assert.Equal(next.Unwrap().GetDefensiveBonus(false, unit), uv.GetDefensiveBonusOn(next, false));
        Assert.Equal(unit.HealingAmountForTile(next.Unwrap(), true), uv.HealingAmountForTile(next, true));
        Assert.Equal(unit.Movement.GetShortestPath(next.Unwrap()).Count, uv.GetShortestPath(next).Count);
        Assert.Equal(unit.Movement.GetDistanceToTiles().GetPathToTile(next.Unwrap()).Count, uv.GetPathToTile(next).Count);
        Assert.Equal(unit.Movement.GetRoadPath(next.Unwrap())?.Count, uv.GetRoadPath(next)?.Count);
        Assert.Equal(next.Unwrap(), uv.GetTileToMoveToThisTurn(next).Unwrap());
        Assert.Equal(unit.Civ.GameInfo.TileMap.TileList.Count(it => Unciv.Logic.Map.MapPathing.IsValidRoadPathTile(player, it)), uv.GetValidRoadConnectionTiles().Count);
    }

    [Fact]
    public void MapUnitView_Actions_ChangeTheUnit()
    {
        var uv = OwnUnit();
        var unit = uv.Unwrap();
        Assert.True(uv.SetDue(false));
        Assert.False(unit.Due);
        Assert.True(uv.SetDue(true));
        Assert.True(unit.Due);
        Assert.True(uv.TrySetInstanceName("Bob"));
        Assert.Equal("Bob", unit.InstanceName);
        Assert.True(uv.TrySetInstanceName(null));
        Assert.True(uv.TryFortifyIfCan());
        Assert.Equal(unit.IsFortified(), uv.IsFortified());
        Assert.True(uv.TryResetAction());
        Assert.Null(unit.Action);
        var target = view.GetTile(game.GetTile(new HexCoord(2, 0)));
        Assert.True(uv.TrySetMoveToAction(target));
        Assert.Equal("moveTo 2,0", unit.Action);
        Assert.True(uv.IsMoving());
        Assert.Equal(target.Unwrap(), uv.GetMovementDestination().Unwrap());
        Assert.True(uv.TryResetAction());
        Assert.True(uv.TryMoveToTile(target));
        Assert.Equal(target.Unwrap(), unit.GetTile());
        Assert.False(uv.CanPillage(target)); // nothing to pillage there (T079: UnitActionsPillage.canPillage)
        Assert.True(uv.TryHeadTowards(view.GetTile(game.GetTile(new HexCoord(3, 0)))));
        unit.CurrentMovement = unit.GetMaxMovement();
        Assert.True(uv.TryStopWorkingOnImprovement());
        Assert.True(uv.TryAddPromotion("Shock I"));
        Assert.Contains("Shock I", unit.Promotions.Promotions);
        Assert.True(uv.TryDisband());
        Assert.True(uv.IsDestroyed());
        Assert.True(uv.HasDisappeared());
    }

    [Fact]
    public void MapUnitView_TrySwapMoveToTile_SwapsPlaces()
    {
        var a = OwnUnit("Warrior", 1, 0);
        var b = OwnUnit("Warrior", 2, 0);
        var aTile = a.Unwrap().GetTile();
        var bTile = b.Unwrap().GetTile();
        Assert.True(a.CanSwapTo(b.GetTile()));
        Assert.True(a.TrySwapMoveToTile(b.GetTile()));
        Assert.Equal(bTile, a.Unwrap().GetTile());
        Assert.Equal(aTile, b.Unwrap().GetTile());
    }

    [Fact]
    public void MapUnitView_Upgrade_AndDefaultPromotions()
    {
        var uv = OwnUnit();
        var unit = uv.Unwrap();
        var upgrade = game.Ruleset.Units["Swordsman"];
        Assert.Equal(unit.Upgrade.GetUpgradesOfKind(false).Any(it => it.UpgradedUnit == upgrade && unit.Upgrade.CanPerformUpgrade(it, false)), uv.CanUpgradeTo(upgrade));
        Assert.False(uv.CanUpgradeTo(game.Ruleset.Units["Worker"]));
        Assert.False(uv.TryUpgradeTo(game.Ruleset.Units["Worker"]));
        var inCity = view.GetMapUnitView(AddUnit("Warrior", player, 0, 0));
        Assert.True(inCity.CanSaveDefaultPromotions());
        Assert.True(inCity.TrySaveDefaultPromotions());
        Assert.True(city.UnitShouldUseSavedPromotion["Warrior"]);
        Assert.Same(inCity.Unwrap().Promotions, city.UnitToPromotions["Warrior"]);
        // Anywhere inside the city's territory counts (Tile.getCity() is the owning city)
        Assert.True(uv.CanSaveDefaultPromotions());
        var outside = OwnUnit("Warrior", -5, 0);
        Assert.False(outside.CanSaveDefaultPromotions());
        Assert.False(outside.TrySaveDefaultPromotions());
        city.IsPuppet = true;
        Assert.False(inCity.CanSaveDefaultPromotions());
    }

    [Fact]
    public void MapUnitView_Workers_ImprovementsAndEscorts()
    {
        var worker = OwnUnit("Worker", 1, 1);
        var farm = game.Ruleset.TileImprovements["Farm"];
        Assert.Equal(worker.Unwrap().CanBuildImprovement(farm), worker.CanBuildImprovement(farm));
        Assert.Equal(farm.GetStockpiledResourceRequirements(worker.Unwrap().Cache.State).Count, worker.GetStockpiledResourceRequirements(farm).Count);
        Assert.True(worker.HasUniqueToBuildImprovements());
        var tile = worker.GetTile();
        tile.Unwrap().QueueImprovement("Farm", 3);
        Assert.Equal("Farm", worker.GetImprovementInProgress());
        Assert.Equal(worker.CanBuildImprovement(farm), worker.CanBuildCurrentImprovement());
        Assert.Equal(tile.Unwrap().ImprovementFunctions.GetImprovementBuildingProblems(farm, worker.Unwrap().Cache.State).ToHashSet(),
            tile.GetImprovementBuildingProblems(farm, worker));
        // Pretending the top terrain is gone checks a copy; the real tile keeps its terrain
        var terrains = tile.Unwrap().AllTerrains.Count();
        tile.GetImprovementBuildingProblems(farm, worker, pretendLastTerrainRemoved: true);
        Assert.Equal(terrains, tile.Unwrap().AllTerrains.Count());
    }

    // T230: the view's seed uses the host secret too (internal to the view layer), so an AI unit's choices are not predictable either.
    [Fact]
    public void MapUnitView_StateBasedRandom_DependsOnTheHostSecret()
    {
        var uv = OwnUnit();
        var plain = uv.StateBasedRandom("caller").NextInt(1000000);
        ((Unciv.Logic.GameInfo)uv.Unwrap().Civ.GameInfo).RandomSalt = 0x5EC0DEL;
        var salted = uv.StateBasedRandom("caller").NextInt(1000000);
        Assert.NotEqual(plain, salted);
        Assert.Equal(salted, uv.StateBasedRandom("caller").NextInt(1000000));
        ((Unciv.Logic.GameInfo)uv.Unwrap().Civ.GameInfo).RandomSalt = 0;
        Assert.Equal(plain, uv.StateBasedRandom("caller").NextInt(1000000));
    }

    [Fact]
    public void MapUnitView_StateBasedRandom_IsDeterministicPerState()
    {
        var uv = OwnUnit();
        var a = uv.StateBasedRandom("caller").NextInt(1000000);
        var b = uv.StateBasedRandom("caller").NextInt(1000000);
        Assert.Equal(a, b);
        Assert.NotEqual(uv.StateBasedRandom("caller", 1).NextInt(1000000), uv.StateBasedRandom("caller", 2).NextInt(1000000));
        uv.Unwrap().CurrentMovement = 0.5f;
        Assert.NotEqual(a, uv.StateBasedRandom("caller").NextInt(1000000));
    }

    [Fact]
    public void MapUnitView_AirUnitRange_AndNukeRadius()
    {
        var fighter = OwnUnit("Fighter", 0, 0);
        Assert.Equal(fighter.GetRange() * 2, fighter.GetAirUnitMovementRange());
        Assert.True(fighter.IsAirUnit());
        Assert.Equal(2, fighter.GetNukeBlastRadius());
    }

    // ---- CivView / GameView / TileView additions ----

    [Fact]
    public void CivView_CitiesUnitsAndHelpers()
    {
        var cv = view.CivView;
        Assert.Equal([city], cv.Cities().Select(it => it.Unwrap()));
        Assert.Same(city, cv.GetCapital()?.Unwrap());
        Assert.False(cv.IsHolyCityBlocked());
        var religion = game.AddReligion(player);
        Assert.False(cv.IsHolyCityBlocked());
        city.Religion.ReligionThisIsTheHolyCityOf = religion.Name;
        city.Religion.IsBlockedHolyCity = true;
        Assert.True(cv.IsHolyCityBlocked());
        var warrior = OwnUnit();
        Assert.Equal([warrior.Unwrap()], cv.GetUnits().Select(it => it.Unwrap()));
        Assert.Equal(1, cv.IdleUnitsCount(true) + cv.IdleUnitsCount(false));
        Assert.Equal(warrior.IsIdle() && warrior.Due ? 1 : 0, cv.DueUnitsCount());
        // A foreign civ only sees the cities whose centre it has explored
        var foreignCiv = view.GetForeignCivView(foreign);
        Assert.Equal([foreignCity], foreignCiv.Cities().Select(it => it.Unwrap()));
        foreignCity.GetCenterTile().SetExplored(player, false);
        Assert.Empty(foreignCiv.Cities());
    }

    [Fact]
    public void GameView_CityAndUnitFactories_AndMovementHistory()
    {
        Assert.Equal(city, view.GetCityView(city).Unwrap());
        Assert.Equal(city, view.GetForeignCityView(city).Unwrap());
        var unit = AddUnit("Warrior", player, 1, 0);
        Assert.Equal(unit, view.GetMapUnitView(unit).Unwrap());
        Assert.Equal(unit, view.GetForeignMapUnitView(unit).Unwrap());
        Assert.Equal(view.GetMapUnitView(unit), view.GetForeignMapUnitView(unit));
        Assert.Equal(game.GameInfo.Civilizations.SelectMany(it => it.Units.GetCivUnits()).Count(it => new MapVisualization(game.GameInfo, player).IsUnitPastVisible(it)),
            view.GetUnitsWithVisibleMovementHistory().Count());
    }

    [Fact]
    public void SpyView_CityNavigation()
    {
        game.GameInfo.GameParameters.EspionageEnabled = true;
        player.EspionageManager.AddSpy();
        var spy = view.GetSpyView(player.EspionageManager.SpyList[0]);
        Assert.Null(spy.GetCityViewOrNull());
        var foreignView = view.GetForeignCityView(foreignCity);
        Assert.Equal(spy.Unwrap().CanMoveTo(foreignCity), spy.CanMoveTo(foreignView));
        Assert.True(spy.TryMoveTo(foreignView));
        Assert.Equal(foreignCity, spy.GetCityViewOrNull()?.Unwrap());
        Assert.True(spy.TryMoveTo(null));
        Assert.Null(spy.GetCityViewOrNull());
    }

    [Fact]
    public void TileView_TileStatsWithExplicitCity_AndBuildingProblems()
    {
        var tile = view.GetCityView(city).GetTiles().First(it => !it.IsCityCenter());
        Assert.Equal(tile.Unwrap().Stats.GetTileStats(city, player).ToString(), tile.GetTileStats(view.CivView, view.GetCityView(city)).ToString());
        Assert.Equal(tile.Unwrap().Stats.GetTileStatsBreakdown(city, player).Count, tile.GetTileStatsBreakdown(view.CivView, view.GetCityView(city)).Count);
        Assert.Equal(city, tile.GetCity()?.Unwrap()); // Tile.getCity() is the owning city
        Assert.Equal(city, tile.OwningCity()?.Unwrap());
        Assert.Equal(tile.Unwrap().GetWorkingCity() == null, tile.GetWorkingCity() == null);
    }

    // ---- CombatantView battle math ----

    [Fact]
    public void CombatantView_BattleMath_MirrorsBattleDamage()
    {
        var attacker = AddUnit("Warrior", player, 1, 0);
        var defender = AddUnit("Warrior", foreign, 2, 0);
        var av = view.GetMapUnitView(attacker).AsCombatant();
        var dv = view.GetForeignMapUnitView(defender).AsCombatant();
        var from = view.GetTile(attacker.GetTile());
        var ac = new Unciv.Logic.Battle.MapUnitCombatant(attacker);
        var dc = new Unciv.Logic.Battle.MapUnitCombatant(defender);
        Assert.Equal(Unciv.Logic.Battle.BattleDamage.GetAttackModifiers(ac, dc, from.Unwrap()).Count, av.GetAttackModifiers(dv, from).Count);
        Assert.Equal(Unciv.Logic.Battle.BattleDamage.GetDefenceModifiers(ac, dc, from.Unwrap()).Count, dv.GetDefenceModifiers(av, from).Count);
        Assert.Equal(Unciv.Logic.Battle.BattleDamage.GetAttackingStrength(ac, dc, from.Unwrap()), av.GetFinalAttackingStrength(dv, from));
        Assert.Equal(Unciv.Logic.Battle.BattleDamage.GetDefendingStrength(ac, dc, from.Unwrap()), dv.GetFinalDefendingStrength(av, from));
        Assert.Equal(Unciv.Logic.Battle.BattleDamage.CalculateDamageToDefender(ac, dc, from.Unwrap(), 0.5f), av.CalculateDamageToDefender(dv, from, 0.5f));
        Assert.Equal(Unciv.Logic.Battle.BattleDamage.CalculateDamageToAttacker(ac, dc, from.Unwrap(), 0.5f), av.CalculateDamageToAttacker(dv, from, 0.5f));
        Assert.Equal(ac.GetAttackingStrength(dc), av.GetAttackingStrength(dv));
        Assert.Equal(dc.GetDefendingStrength(ac), dv.GetDefendingStrength(av));
        Assert.Equal(ac.GetUnitType(), av.GetUnitType());
    }

    // ---- AttackableTileView ----

    [Fact]
    public void AttackableTileView_WrapsTilesAndCombatant()
    {
        var attacker = AddUnit("Warrior", player, 1, 0);
        var defender = AddUnit("Warrior", foreign, 2, 0);
        var from = attacker.GetTile();
        var to = defender.GetTile();
        var unitTile = new Unciv.Logic.Battle.AttackableTile(from, to, 1.5f, new Unciv.Logic.Battle.MapUnitCombatant(defender));
        var atv = new AttackableTileView(unitTile, player, false, view);
        Assert.Equal(from, atv.GetTileToAttackFrom().Unwrap());
        Assert.Equal(to, atv.GetTileToAttack().Unwrap());
        Assert.Equal(1.5f, atv.GetMovementLeftAfterMovingToAttackTile());
        Assert.Equal(defender, ((MapUnitCombatantView)atv.GetCombatant()!).GetUnitView().Unwrap());
        var cityTile = new AttackableTileView(new Unciv.Logic.Battle.AttackableTile(from, foreignCity.GetCenterTile(), 0f, new Unciv.Logic.Battle.CityCombatant(foreignCity)), player, false, view);
        Assert.Equal(foreignCity, ((CityCombatantView)cityTile.GetCombatant()!).GetCityView().Unwrap());
        Assert.Null(new AttackableTileView(new Unciv.Logic.Battle.AttackableTile(from, to, 0f, null), player, false, view).GetCombatant());
        // Bombarding builds the attackable tile from the city centre without any movement
        var bombard = ((CityCombatantView)view.GetForeignCityView(foreignCity).AsCombatant())
            .BuildBombardAttackableTile(view.GetTile(to), view.GetForeignMapUnitView(defender).AsCombatant(), player, false, view);
        Assert.Equal(foreignCity.GetCenterTile(), bombard.GetTileToAttackFrom().Unwrap());
        Assert.Equal(to, bombard.GetTileToAttack().Unwrap());
        Assert.Equal(0f, bombard.GetMovementLeftAfterMovingToAttackTile());
        Assert.Equal(defender, ((MapUnitCombatantView)bombard.GetCombatant()!).GetUnitView().Unwrap());
    }

    [Fact]
    public void MapUnitView_AttackMembersCallBattleAndNuke()
    {
        var uv = OwnUnit("Warrior", 0, 0);
        var defender = AddUnit("Warrior", foreign, 2, 0);
        var from = game.GetTile(new HexCoord(1, 0));
        var atv = new AttackableTileView(new Unciv.Logic.Battle.AttackableTile(from, defender.GetTile(), 1f, new Unciv.Logic.Battle.MapUnitCombatant(defender)), player, false, view);

        Assert.True(uv.TryMovePreparingAttack(atv));
        Assert.Equal(from, uv.Unwrap().GetTile());
        var damage = uv.AttackOrNuke(atv);
        Assert.True(damage.AttackerDealt > 0);
        Assert.True(defender.Health < 100);
        Assert.False(uv.MayUseNuke(view.GetTile(uv.Unwrap().GetTile()))); // never the tile the nuke is on
        Assert.True(uv.TryNuke(view.GetTile(defender.GetTile()))); // not a nuclear weapon: nothing happens
    }

    [Fact]
    public void CityView_TryBombardAttacksTheTile()
    {
        var defender = AddUnit("Warrior", foreign, 2, 0);
        var bombard = new AttackableTileView(new Unciv.Logic.Battle.AttackableTile(city.GetCenterTile(), defender.GetTile(), 0f, new Unciv.Logic.Battle.MapUnitCombatant(defender)), player, false, view);

        var damage = CityView().TryBombard(bombard);

        Assert.True(damage.AttackerDealt > 0);
        Assert.True(defender.Health < 100);
        Assert.True(city.AttackedThisTurn);
    }

    [Fact]
    public void MapUnitView_MovementString_FormatsWithOneFractionDigitAndNoGrouping()
    {
        var uv = OwnUnit("Warrior", 0, 0);
        uv.Unwrap().CurrentMovement = 1f;
        Assert.Equal("1/2", uv.GetMovementString());
        uv.Unwrap().CurrentMovement = 1.25f; // HALF_EVEN on the exactly representable tie
        Assert.Equal("1.2/2", uv.GetMovementString());
        uv.Unwrap().CurrentMovement = 0.35f; // 0.3499999940395355 as a double
        Assert.Equal("0.3/2", uv.GetMovementString());
        uv.Unwrap().CurrentMovement = 0f;
        Assert.Equal("0/2", uv.GetMovementString());
    }

    [Fact]
    public void MapUnitView_TurnsToBuild_AndStartWorkingOnImprovement()
    {
        var worker = OwnUnit("Worker", 1, 1);
        var farm = game.Ruleset.TileImprovements["Farm"];
        var road = game.Ruleset.TileImprovements["Road"];
        Assert.Equal(farm.GetTurnsToBuild(player, worker.Unwrap()), worker.GetTurnsToBuild(farm));
        Assert.True(worker.GetTurnsToBuild(farm) >= 1);

        var tile = worker.GetTile().Unwrap();
        tile.QueueImprovement("Fort", 9); // replaced by starting new work
        Assert.True(worker.TryStartWorkingOnImprovement(farm, road));
        Assert.Equal(["Farm", "Road"], tile.ImprovementQueue.Select(it => it.Improvement));
        Assert.Equal(worker.GetTurnsToBuild(farm), tile.ImprovementQueue[0].TurnsToImprovement);

        game.GameInfo.GameParameters.GodMode = true;
        Assert.True(worker.TryStartWorkingOnImprovement(road));
        Assert.Equal(1, tile.ImprovementQueue.Single().TurnsToImprovement);
    }

    [Fact]
    public void MapUnitView_AttackableEnemies_AndAirSweep()
    {
        var uv = OwnUnit("Warrior", 0, 0);
        var defender = AddUnit("Warrior", foreign, 2, 0);
        player.GetDiplomacyManager(foreign)!.DeclareWar();
        Assert.True(uv.ContainsAttackableEnemy(view.GetTile(defender.GetTile())));
        Assert.False(uv.ContainsAttackableEnemy(view.GetTile(game.GetTile(new HexCoord(0, 1)))));
        var attackable = uv.GetAttackableEnemies();
        Assert.Contains(attackable, it => it.GetTileToAttack().Unwrap() == defender.GetTile());

        var fighter = OwnUnit("Fighter", 0, 0);
        Assert.True(fighter.TryAirSweep(view.GetTile(defender.GetTile())));
    }

    [Fact]
    public void ForeignCityView_BombardableTiles_ListTilesWithEnemiesInRange()
    {
        var own = view.GetForeignCityView(city);
        Assert.Empty(own.GetBombardableTiles());
        var defender = AddUnit("Warrior", foreign, 2, 0);
        player.GetDiplomacyManager(foreign)!.DeclareWar();
        Assert.Contains(own.GetBombardableTiles(), it => it.Unwrap() == defender.GetTile());
    }
}
