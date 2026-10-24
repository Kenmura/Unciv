using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnit.Actions;
using Unciv.Models;
using Unciv.Utils;
using Unciv.View;

namespace Unciv.Core.Tests.Logic.Map;

// T080: UnitActions, UnitActionsFromUniques, UnitActionsSeams, and MapUnitView unit action methods.
[Collection("RulesetCache")]
public class UnitActionsBTests
{
    // ---- UnitActions: InvokeUnitAction / GetUnitActions

    [Fact]
    public void InvokeUnitAction_ExecutesFirstEnabledAction_AndReturnsWhetherAnyActionWasInvoked()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));

        // Fortify is available and enabled
        Assert.False(unit.IsFortified());
        bool invoked = UnitActions.InvokeUnitAction(unit, UnitActionType.Fortify);
        Assert.True(invoked);
        Assert.True(unit.IsFortified());

        // Invoking Fortify again when already fortified returns false (action is null/disabled)
        bool reInvoked = UnitActions.InvokeUnitAction(unit, UnitActionType.Fortify);
        Assert.False(reInvoked);

        // Invoking an action type the unit doesn't have returns false
        Assert.False(UnitActions.InvokeUnitAction(unit, UnitActionType.FoundCity));
    }

    [Fact]
    public void GetUnitActions_EnumeratesAllPossibleActions()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));

        var allActions = UnitActions.GetUnitActions(unit).ToList();
        Assert.Contains(allActions, a => a.Type == UnitActionType.Skip);
        Assert.Contains(allActions, a => a.Type == UnitActionType.Fortify);
        Assert.Contains(allActions, a => a.Type == UnitActionType.Automate);
        Assert.Contains(allActions, a => a.Type == UnitActionType.Explore);
    }

    [Fact]
    public void GetUnitActions_WithType_ReturnsFilteredActions()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));

        var skipActions = UnitActions.GetUnitActions(unit, UnitActionType.Skip).ToList();
        Assert.Single(skipActions);
        Assert.Equal(UnitActionType.Skip, skipActions[0].Type);

        var fortifyActions = UnitActions.GetUnitActions(unit, UnitActionType.Fortify).ToList();
        Assert.Single(fortifyActions);
        Assert.Equal(UnitActionType.Fortify, fortifyActions[0].Type);

        var none = UnitActions.GetUnitActions(unit, UnitActionType.FoundReligion).ToList();
        Assert.Empty(none);
    }

    // ---- Unmapped General Actions

    [Fact]
    public void SkipAction_TogglesDueStatus()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));
        unit.Due = true;

        var skipAction = UnitActions.GetUnitActions(unit, UnitActionType.Skip).First();
        Assert.False(skipAction.IsCurrentAction);
        Assert.NotNull(skipAction.Action);

        skipAction.Action!.Invoke();
        Assert.False(unit.Due);

        // Now isCurrentAction should be true (!unit.Due)
        var skipAction2 = UnitActions.GetUnitActions(unit, UnitActionType.Skip).First();
        Assert.True(skipAction2.IsCurrentAction);

        // Out of movement disables skip action
        unit.CurrentMovement = 0f;
        var disabledSkip = UnitActions.GetUnitActions(unit, UnitActionType.Skip).First();
        Assert.Null(disabledSkip.Action);
    }

    [Fact]
    public void StopActions_StopMovement_StopExploration_StopAutomation()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));

        // Movement
        unit.Action = "moveTo 3,3";
        var stopMove = UnitActions.GetUnitActions(unit, UnitActionType.StopMovement).FirstOrDefault();
        Assert.NotNull(stopMove?.Action);
        stopMove.Action!.Invoke();
        Assert.Null(unit.Action);

        // Exploration
        unit.Action = UnitActionType.Explore.Value();
        var stopExplore = UnitActions.GetUnitActions(unit, UnitActionType.StopExploration).FirstOrDefault();
        Assert.NotNull(stopExplore?.Action);
        stopExplore.Action!.Invoke();
        Assert.Null(unit.Action);

        // Automation
        unit.Action = "automate";
        unit.Automated = true;
        var stopAuto = UnitActions.GetUnitActions(unit, UnitActionType.StopAutomation).FirstOrDefault();
        Assert.NotNull(stopAuto?.Action);
        stopAuto.Action!.Invoke();
        Assert.Null(unit.Action);
        Assert.False(unit.Automated);
    }

    [Fact]
    public void AutomateAndExploreActions_SetTheUnitAutomated()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));

        // Automate
        var autoAction = UnitActions.GetUnitActions(unit, UnitActionType.Automate).FirstOrDefault();
        Assert.NotNull(autoAction?.Action);
        autoAction.Action!.Invoke();
        Assert.True(unit.Automated);

        // Explore
        unit.Automated = false;
        var exploreAction = UnitActions.GetUnitActions(unit, UnitActionType.Explore).FirstOrDefault();
        Assert.NotNull(exploreAction?.Action);
        exploreAction.Action!.Invoke();
        Assert.Equal(UnitActionType.Explore.Value(), unit.Action);
    }

    [Fact]
    public void FortifyAndFortifyUntilHealedActions()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(HexCoord.Zero));
        unit.Health = 50;

        var actions = UnitActions.GetUnitActions(unit).Where(a => a.Type is UnitActionType.Fortify or UnitActionType.FortifyUntilHealed).ToList();
        Assert.Equal(2, actions.Count);

        var fortifyHeal = actions.First(a => a.Type == UnitActionType.FortifyUntilHealed);
        Assert.NotNull(fortifyHeal.Action);
        fortifyHeal.Action!.Invoke();
        Assert.True(unit.IsFortifyingUntilHealed());

        // When already fortified, shows fortification turn percentage and action is null
        var currentFortify = UnitActions.GetUnitActions(unit).First(a => a.Type == UnitActionType.FortifyUntilHealed);
        Assert.True(currentFortify.IsCurrentAction);
        Assert.Contains("Fortification", currentFortify.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void SleepActions_WhenUnitCannotFortify()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var baseUnit = game.CreateBaseUnit(uniques: ["Cannot fortify"]);
        var scout = game.AddUnit(baseUnit.Name, civ, game.GetTile(HexCoord.Zero));
        scout.Health = 60;

        var sleepActions = UnitActions.GetUnitActions(scout).Where(a => a.Type is UnitActionType.Sleep or UnitActionType.SleepUntilHealed).ToList();
        Assert.Equal(2, sleepActions.Count);

        var sleepHeal = sleepActions.First(a => a.Type == UnitActionType.SleepUntilHealed);
        Assert.NotNull(sleepHeal.Action);
        sleepHeal.Action!.Invoke();
        Assert.True(scout.IsSleepingUntilHealed());
    }

    [Fact]
    public void EscortActions_FormAndStopFormation()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var military = game.AddUnit("Warrior", civ, tile);
        var civilian = game.AddUnit("Worker", civ, tile);

        // Neither is escorting yet, other unit is present on tile
        var escortAction = UnitActions.GetUnitActions(military, UnitActionType.EscortFormation).FirstOrDefault();
        Assert.NotNull(escortAction?.Action);

        escortAction.Action!.Invoke();
        Assert.True(military.IsEscorting());
        Assert.True(civilian.IsEscorting());

        // Now StopEscortFormation is available
        var stopEscortAction = UnitActions.GetUnitActions(military, UnitActionType.StopEscortFormation).FirstOrDefault();
        Assert.NotNull(stopEscortAction?.Action);

        stopEscortAction.Action!.Invoke();
        Assert.False(military.IsEscorting());
        Assert.False(civilian.IsEscorting());
    }

    [Fact]
    public void GiftUnit_ToCityStateAndMajorCiv()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var cs = game.AddCiv(cityStateType: "Cultured");
        civ.GetDiplomacyManagerOrMeet(cs);
        cs.GetDiplomacyManagerOrMeet(civ);

        var tile = game.GetTile(HexCoord.Of(1, 0));
        var csCity = game.AddCity(cs, tile);
        tile.SetOwningCity(csCity);

        var unit = game.AddUnit("Warrior", civ, null);
        unit.CurrentTile = tile;
        unit.CurrentMovement = unit.GetMaxMovement();
        tile.MilitaryUnit = unit;

        // CS receives military unit gift
        var giftActions = UnitActions.GetUnitActions(unit, UnitActionType.GiftUnit).ToList();
        Assert.Single(giftActions);
        Assert.NotNull(giftActions[0].Action);

        giftActions[0].Action!.Invoke();
        // Unit gifted to CS
        Assert.Equal(cs, unit.Civ);
        Assert.Equal(5f, cs.GetDiplomacyManager(civ).NotNull().GetInfluence());
    }

    // ---- UnitActionsFromUniques: FoundCity

    [Fact]
    public void FoundCity_SettlesCityAndDestroysUnit()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        civ.PlayerType = PlayerType.Human;
        var tile = game.GetTile(HexCoord.Zero);
        var settler = game.AddUnit("Settler", civ, tile);

        var action = UnitActionsFromUniques.GetFoundCityAction(settler, tile);
        Assert.NotNull(action);
        Assert.Equal(UnitActionType.FoundCity, action.Type);
        Assert.NotNull(action.Action);

        action.Action!.Invoke();
        Assert.True(settler.IsDestroyed);
        Assert.True(tile.IsCityCenter());
        Assert.Single(civ.Cities);
    }

    [Fact]
    public void FoundPuppetCity_SettlesCityAsPuppet()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var baseUnit = game.CreateBaseUnit(uniques: ["Founds a new puppet city"]);
        var tile = game.GetTile(HexCoord.Zero);
        var settler = game.AddUnit(baseUnit.Name, civ, tile);

        var action = UnitActionsFromUniques.GetFoundCityAction(settler, tile);
        Assert.NotNull(action?.Action);

        action.Action!.Invoke();
        Assert.True(settler.IsDestroyed);
        Assert.True(civ.Cities[0].IsPuppet);
    }

    [Fact]
    public void FoundCity_RejectsWaterImpassibleOrOneCityChallenge()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        civ.PlayerType = PlayerType.Human;
        var tile = game.GetTile(HexCoord.Zero);
        var settler = game.AddUnit("Settler", civ, tile);

        // Water tile
        tile.BaseTerrain = Constants.Coast;
        tile.SetTerrainTransients();
        Assert.Null(UnitActionsFromUniques.GetFoundCityAction(settler, tile));

        // Impassible tile
        tile.BaseTerrain = Constants.Mountain;
        tile.SetTerrainTransients();
        Assert.Null(UnitActionsFromUniques.GetFoundCityAction(settler, tile));

        // OCC
        tile.BaseTerrain = Constants.Grassland;
        tile.SetTerrainTransients();
        game.GameInfo.GameParameters.OneCityChallenge = true;
        civ.HasEverOwnedOriginalCapital = true;
        Assert.Null(UnitActionsFromUniques.GetFoundCityAction(settler, tile));
    }

    [Fact]
    public void FoundCity_BreakingPromise_AddsConfirmationForHuman()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        civ.PlayerType = PlayerType.Human;
        var otherCiv = game.AddCiv();

        // Meet each other and set agreement flag
        civ.GetDiplomacyManagerOrMeet(otherCiv);
        otherCiv.GetDiplomacyManagerOrMeet(civ).SetFlag(DiplomacyFlags.AgreedToNotSettleNearUs, 10);

        // Place a city for otherCiv nearby
        var otherCityTile = game.GetTile(HexCoord.Zero);
        var otherCity = game.AddCity(otherCiv, otherCityTile);
        otherCity.IsOriginalCapital = true;
        otherCiv.HasEverOwnedOriginalCapital = true;
        otherCiv.HasExplored(otherCityTile);

        // Settle tile at distance 4 (> MinimalCityDistance, <= 6) from otherCity
        var settlerTile = game.GetTile(HexCoord.Of(4, 0));
        var settler = game.AddUnit("Settler", civ, settlerTile);

        var action = UnitActionsFromUniques.GetFoundCityAction(settler, settlerTile);
        Assert.NotNull(action);
        Assert.NotNull(action.Confirmation);
        Assert.Contains(otherCiv.CivId, action.Confirmation.Question, StringComparison.Ordinal);
    }

    // ---- UnitActionsFromUniques: SetUp, Paradrop, AirSweep, Guard

    [Fact]
    public void SetUp_SiegeUnit()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var catapult = game.AddUnit("Catapult", civ, tile);

        var actions = UnitActionsFromUniques.GetSetupActions(catapult, tile).ToList();
        Assert.Single(actions);
        Assert.False(actions[0].IsCurrentAction);
        Assert.NotNull(actions[0].Action);

        actions[0].Action!.Invoke();
        Assert.True(catapult.IsSetUpForSiege());
        Assert.Equal(catapult.GetMaxMovement() - 1f, catapult.CurrentMovement);

        // When already set up, action is null and isCurrentAction is true
        var setupAgain = UnitActionsFromUniques.GetSetupActions(catapult, tile).ToList();
        Assert.True(setupAgain[0].IsCurrentAction);
        Assert.Null(setupAgain[0].Action);
    }

    [Fact]
    public void Paradrop_TogglesPreparingParadrop()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        game.AddCity(civ, tile);
        var paratrooper = game.AddUnit("Paratrooper", civ, tile);

        var actions = UnitActionsFromUniques.GetParadropActions(paratrooper, tile).ToList();
        Assert.Single(actions);

        actions[0].Action!.Invoke();
        Assert.True(paratrooper.IsPreparingParadrop());

        // Action toggles it off
        actions = UnitActionsFromUniques.GetParadropActions(paratrooper, tile).ToList();
        actions[0].Action!.Invoke();
        Assert.False(paratrooper.IsPreparingParadrop());
    }

    [Fact]
    public void AirSweep_TogglesPreparingAirSweep()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        game.AddCity(civ, tile);
        var fighter = game.AddUnit("Triplane", civ, tile);

        var actions = UnitActionsFromUniques.GetAirSweepActions(fighter, tile).ToList();
        Assert.Single(actions);
        Assert.NotNull(actions[0].Action);

        actions[0].Action!.Invoke();
        Assert.True(fighter.IsPreparingAirSweep());

        actions = UnitActionsFromUniques.GetAirSweepActions(fighter, tile).ToList();
        actions[0].Action!.Invoke();
        Assert.False(fighter.IsPreparingAirSweep());
    }

    [Fact]
    public void Guard_StandsGroundInsteadOfWithdrawing()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var baseUnit = game.CreateBaseUnit(uniques: ["Withdraws before melee combat"]);
        var immortal = game.AddUnit(baseUnit.Name, civ, tile);

        var actions = UnitActionsFromUniques.GetGuardActions(immortal, tile).ToList();
        Assert.Single(actions);
        Assert.NotNull(actions[0].Action);

        actions[0].Action!.Invoke();
        Assert.True(immortal.IsGuarding());

        // When guarding, action is null and isCurrentAction is true
        var guarding = UnitActionsFromUniques.GetGuardActions(immortal, tile).ToList();
        Assert.True(guarding[0].IsCurrentAction);
        Assert.Null(guarding[0].Action);
    }

    // ---- UnitActionsFromUniques: AddInCapital

    [Fact]
    public void AddInCapital_AddsSpaceshipPartToVictoryManager()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var booster = game.AddUnit("SS Cockpit", civ, tile);

        // Not in city center -> action is null
        var notInCity = UnitActionsFromUniques.GetAddInCapitalActions(booster, tile).ToList();
        Assert.Null(notInCity[0].Action);

        // Found capital at tile
        var capital = game.AddCity(civ, tile);
        capital.IsOriginalCapital = true;

        var inCapital = UnitActionsFromUniques.GetAddInCapitalActions(booster, tile).ToList();
        Assert.NotNull(inCapital[0].Action);

        inCapital[0].Action!.Invoke();
        Assert.True(booster.IsDestroyed);
        Assert.Equal(1, civ.VictoryManager.CurrentsSpaceshipParts["SS Cockpit"]);
    }

    // ---- UnitActionsFromUniques: TriggerUnique

    [Fact]
    public void TriggerUnique_OneTimeEnterGoldenAgeTurns()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var artist = game.AddUnit("Great Artist", civ, tile);

        var actions = UnitActionsFromUniques.GetTriggerUniqueActions(artist, tile).ToList();
        Assert.Single(actions);
        Assert.NotNull(actions[0].Action);

        actions[0].Action!.Invoke();
        Assert.True(artist.IsDestroyed);
        Assert.True(civ.GoldenAges.IsGoldenAge());
    }

    // ---- UnitActionsFromUniques: Improvement Creation & Repair

    [Fact]
    public void ImprovementCreation_WaterImprovement()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        civ.Tech.AddTechnology("Sailing");
        var capital = game.AddCity(civ, game.GetTile(HexCoord.Of(1, 0)));
        var tile = game.GetTile(HexCoord.Zero);
        game.AddTileToCity(capital, tile);
        tile.SetOwningCity(capital);
        tile.BaseTerrain = Constants.Coast;
        tile.SetTerrainTransients();
        tile.SetTileResource("Whales");

        var boat = game.AddUnit("Work Boats", civ, tile);
        var actions = UnitActionsFromUniques.GetImprovementCreationActions(boat, tile).ToList();
        Assert.Single(actions);
        Assert.Equal("Create [Fishing Boats]", actions[0].Title);

        actions[0].Action!.Invoke();
        Assert.True(boat.IsDestroyed);
        Assert.Equal("Fishing Boats", tile.Improvement);
    }

    [Fact]
    public void RepairAction_CalculatesTurnsAndQueuesRepair()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var capital = game.AddCity(civ, game.GetTile(HexCoord.Zero));
        var tile = game.GetTile(HexCoord.Of(1, 0));
        game.AddTileToCity(capital, tile);
        tile.SetOwningCity(capital);
        tile.BaseTerrain = Constants.Grassland;
        tile.SetTerrainTransients();
        tile.SetImprovementBasic("Farm");
        tile.SetPillaged();

        var worker = game.AddUnit("Worker", civ, tile);
        var action = UnitActionsFromUniques.GetRepairAction(worker);
        Assert.NotNull(action);
        Assert.Equal(UnitActionType.Repair, action.Type);
        Assert.NotNull(action.Action);

        action.Action!.Invoke();
        Assert.Equal(Constants.Repair, tile.ImprovementInProgress);
    }

    // ---- UnitActionsFromUniques: Transform

    [Fact]
    public void TransformAction_ConvertsUnitToEquivalentType()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var target = game.CreateBaseUnit("Sword");
        target.Movement = 2;
        target.Strength = 10;
        var source = game.CreateBaseUnit("Sword", uniques: [$"Can transform to [{target.Name}]"]);
        source.Movement = 2;
        source.Strength = 10;
        var unit = game.AddUnit(source.Name, civ, tile);

        var actions = UnitActionsFromUniques.GetTransformActions(unit, tile).ToList();
        Assert.Single(actions);
        Assert.Contains($"Transform to [{target.Name}]", actions[0].Title, StringComparison.Ordinal);

        actions[0].Action!.Invoke();
        Assert.True(unit.IsDestroyed);

        var newUnit = tile.MilitaryUnit;
        Assert.NotNull(newUnit);
        Assert.Equal(target.Name, newUnit.Name);
    }

    // ---- MapUnitView Unit Action Integration

    [Fact]
    public void MapUnitView_CanInvokeAndTryInvokeUnitAction()
    {
        var game = new TestGame();
        game.MakeHexagonalMap(5);
        var civ = game.AddCiv();
        var tile = game.GetTile(HexCoord.Zero);
        var warrior = game.AddUnit("Warrior", civ, tile);
        var gameView = new GameView(game.GameInfo, civ);
        var unitView = gameView.GetMapUnitView(warrior);

        Assert.True(unitView.CanInvokeUnitAction(UnitActionType.Fortify));
        Assert.False(unitView.CanInvokeUnitAction(UnitActionType.FoundCity));

        bool invoked = unitView.TryInvokeUnitAction(UnitActionType.Fortify);
        Assert.True(invoked);
        Assert.True(warrior.IsFortified());
        Assert.False(unitView.CanInvokeUnitAction(UnitActionType.Fortify));
    }
}
