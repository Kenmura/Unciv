using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Managers;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/managers/GoldenAgeManagerTest.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class GoldenAgeManagerFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civ;
    private readonly GoldenAgeManager goldenAgeManager = new();

    public GoldenAgeManagerFixtureTests()
    {
        civ = testGame.AddCiv();
        goldenAgeManager.CivInfo = civ;
    }

    [Fact]
    public void ShouldGainGoldenAgePointsWhenHappy()
    {
        goldenAgeManager.EndTurn(10);
        Assert.Equal(10, goldenAgeManager.StoredHappiness);
    }

    [Fact]
    public void ShouldLoseGoldenAgePointsWhenUnhappy()
    {
        goldenAgeManager.StoredHappiness = 100;
        goldenAgeManager.EndTurn(-10);
        Assert.Equal(90, goldenAgeManager.StoredHappiness);
    }

    [Fact]
    public void ShouldNotGoIntoNegativeGoldenAgePoints()
    {
        goldenAgeManager.StoredHappiness = 5;
        goldenAgeManager.EndTurn(-10);
        Assert.Equal(0, goldenAgeManager.StoredHappiness);
    }

    [Fact]
    public void ShouldNotStoreExcessHappinessWhenAlreadyInGoldenAge()
    {
        goldenAgeManager.StoredHappiness = 5;
        goldenAgeManager.EnterGoldenAge();
        goldenAgeManager.EndTurn(10);
        Assert.Equal(5, goldenAgeManager.StoredHappiness);
    }

    [Fact]
    public void ShouldDecreaseGoldenAgeDurationOnNextTurn()
    {
        goldenAgeManager.EnterGoldenAge(10);
        goldenAgeManager.EndTurn(0);
        Assert.Equal(9, goldenAgeManager.TurnsLeftForCurrentGoldenAge);
    }

    [Fact]
    public void ShouldGoInGoldenAgeWithEnoughHappiness()
    {
        goldenAgeManager.StoredHappiness = 700;
        goldenAgeManager.EndTurn(0);
        Assert.True(goldenAgeManager.IsGoldenAge());
    }

    [Fact]
    public void ShouldIncreaseGoldenAgeCostEachTimeIsTriggeredByHappiness()
    {
        var first = goldenAgeManager.HappinessRequiredForNextGoldenAge();
        goldenAgeManager.StoredHappiness = first;
        goldenAgeManager.EndTurn(10);
        Assert.True(goldenAgeManager.HappinessRequiredForNextGoldenAge() > first);
    }

    [Fact]
    public void ShouldNotIncreaseGoldenAgeCostWhenTriggeredByOutsideFactors()
    {
        var first = goldenAgeManager.HappinessRequiredForNextGoldenAge();
        goldenAgeManager.EnterGoldenAge(10);
        goldenAgeManager.EndTurn(10);
        Assert.Equal(first, goldenAgeManager.HappinessRequiredForNextGoldenAge());
    }

    [Fact]
    public void ShouldIncreaseGoldenAgeCostWithMoreCities()
    {
        testGame.MakeHexagonalMap(1);
        testGame.AddCity(civ, testGame.GetTile(HexCoord.Zero), initialPopulation: 10);
        var oneCity = goldenAgeManager.HappinessRequiredForNextGoldenAge();

        testGame.AddCity(civ, testGame.GetTile(1, 0), initialPopulation: 10);

        Assert.True(oneCity < goldenAgeManager.HappinessRequiredForNextGoldenAge());
    }

    [Fact]
    public void ShouldIncreaseGoldenAgeLengthDueToUniques()
    {
        var uniqueCiv = testGame.AddCiv(["[+50]% Golden Age length"]);
        goldenAgeManager.CivInfo = uniqueCiv;
        goldenAgeManager.EnterGoldenAge(10);
        Assert.Equal(15, goldenAgeManager.TurnsLeftForCurrentGoldenAge);
    }
}
