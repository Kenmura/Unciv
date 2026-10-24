using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Automation.Civilization;
using Unciv.Logic.Map;
using Unciv.Models.Metadata;
using Unciv.View;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T157: the automation cases of tests/src/com/unciv/logic/civilization/GreatPersonSelectionTest.kt (split from T152).
// Kotlin's GameView(gameInfo, civ).civView wrapper is reproduced with the engine's GameView.
[Collection("RulesetCache")]
public sealed class GreatPersonSelectionAutomationFixtureTests
{
    private static CivView CivViewOf(CivilizationObject civ) => new GameView((GameInfo)civ.GameInfo, civ).CivView;

    [Fact]
    public void AutomationCommitsItsPreferredAvailableGreatPerson()
    {
        var scenario = GreatPersonSelectionFixtureTests.CreateScenario(BaseRuleset.Civ_V_GnK, "Rome");
        var manager = scenario.Civ.GreatPeople;
        manager.FreeGreatPeople = 1;

        NextTurnAutomation.ChooseGreatPerson(CivViewOf(scenario.Civ));

        Assert.Equal(0, manager.FreeGreatPeople);
        Assert.Contains(scenario.Civ.Units.GetCivGreatPeople(), it => it.Name == "Great Scientist");
    }

    [Fact]
    public void AutomationRetainsAPickWhenItsChosenUnitCannotBePlaced()
    {
        var game = new TestGame();
        var civ = game.AddCiv();
        var city = game.AddCity(civ, game.GetTile(HexCoord.Zero));
        var blocker = game.AddUnit("Worker", civ, city.GetCenterTile());
        civ.GreatPeople.FreeGreatPeople = 1;
        var originalUnits = civ.Units.GetCivUnits().ToList();

        NextTurnAutomation.ChooseGreatPerson(CivViewOf(civ));

        Assert.Equal(1, civ.GreatPeople.FreeGreatPeople);
        Assert.Equal(originalUnits, civ.Units.GetCivUnits().ToList());
        Assert.Same(blocker, city.GetCenterTile().CivilianUnit);
    }
}
