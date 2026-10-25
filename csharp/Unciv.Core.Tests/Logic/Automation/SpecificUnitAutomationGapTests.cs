using Unciv.Core.Tests.Support;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Automation.Unit;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;

namespace Unciv.Core.Tests.Logic.Automation;

// T211: coverage for the SpecificUnitAutomation settler and add-in-capital branches left uncovered by T091/T161.
// Not coverable: the multi-settler filter (needs another same-type settler on a tile this one can move to, which stacking forbids)
// and the "City within distance" throw (the ranker and CanBeSettled agree on every tile it returns).
[Collection("RulesetCache")]
public class SpecificUnitAutomationGapTests
{
    private readonly TestGame testGame = new();
    private readonly CivilizationObject civ;
    private readonly AutomationCache cache = new();

    public SpecificUnitAutomationGapTests()
    {
        testGame.GameInfo.EngineOptions.VisibleMap = true;
        testGame.MakeHexagonalMap(12, Constants.Grassland);
        civ = testGame.AddCiv();
    }

    private MapUnitView ViewOf(MapUnitClass unit) => new GameView(testGame.GameInfo, unit.Civ).GetMapUnitView(unit);

    [Fact]
    public void CityStateWithoutCitiesFoundsItsFirstCityInPlace()
    {
        testGame.GameInfo.Turns = 5; // not the turn-zero special case
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var settler = testGame.AddUnit("Settler", cityState, testGame.GetTile(0, 0));

        SpecificUnitAutomation.AutomateSettlerActions(settler, [], cache);

        Assert.Single(cityState.Cities);
        Assert.Equal(testGame.GetTile(0, 0), cityState.Cities[0].GetCenterTile());
    }

    [Fact]
    public void CityStateSettlerWithoutMovementCannotFoundInPlaceAndFallsThrough()
    {
        testGame.GameInfo.Turns = 5;
        var cityState = testGame.AddCiv(cityStateType: "Cultured");
        var settler = testGame.AddUnit("Settler", cityState, testGame.GetTile(0, 0));
        settler.CurrentMovement = 0f;

        SpecificUnitAutomation.AutomateSettlerActions(settler, [], cache);

        Assert.Empty(cityState.Cities);
    }

    [Fact]
    public void SettlerWalksTowardsABestTileThatIsFartherThanThreeStepsAway()
    {
        testGame.GameInfo.Ruleset.ModOptions.Constants.MinimumCityLocationTileValue = 1f;
        testGame.GameInfo.Ruleset.ModOptions.Constants.MinimalCityDistance = 7; // only the tile 4 steps away from the settler is far enough from our city
        testGame.AddCity(civ, testGame.GetTile(0, 4));
        var slowSettler = testGame.CreateBaseUnit(null, "Founds a new city");
        slowSettler.Movement = 1; // shortest paths count turns, and the best tile has to be at least four of them away
        var settler = testGame.AddUnit(slowSettler.Name, civ, testGame.GetTile(0, 0));
        var target = testGame.GetTile(-4, -4);
        var start = settler.CurrentTile;
        SpecificUnitAutomation.AutomateSettlerActions(settler, [], cache);

        Assert.Single(civ.Cities);
        Assert.True(settler.CurrentTile.AerialDistanceTo(target) < start.AerialDistanceTo(target));
    }

    [Fact]
    public void SettlerWithoutMovementLeftDoesNothingWhenItCannotFound()
    {
        testGame.AddCity(civ, testGame.GetTile(0, 4));
        var settler = testGame.AddUnit("Settler", civ, testGame.GetTile(0, 0));
        settler.CurrentMovement = 0f;
        var start = settler.CurrentTile;

        SpecificUnitAutomation.AutomateSettlerActions(settler, [], cache);

        Assert.Equal(start, settler.CurrentTile);
        Assert.Single(civ.Cities);
    }

    [Fact]
    public void AddInCapitalUnitSwapsPlacesWithACivilianInTheCapital()
    {
        var capital = testGame.AddCity(civ, testGame.GetTile(0, 0));
        var blocker = testGame.AddUnit("Worker", civ, capital.GetCenterTile());
        var addInCapital = testGame.CreateBaseUnit(null, "Can be added to [comment] in the Capital");
        addInCapital.Movement = 3;
        var unit = testGame.AddUnit(addInCapital.Name, civ, testGame.GetTile(2, 0));

        SpecificUnitAutomation.AutomateAddInCapital(ViewOf(unit));

        Assert.NotEqual(capital.GetCenterTile(), blocker.CurrentTile);
    }
}
