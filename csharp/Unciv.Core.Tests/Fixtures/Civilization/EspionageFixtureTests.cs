using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/EspionageTests.kt over the real TestGame.
[Collection("RulesetCache")]
public sealed class EspionageFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization civA;
    private readonly Civilization civB;

    public EspionageFixtureTests()
    {
        civA = testGame.AddCiv();
        civB = testGame.AddCiv();
        testGame.GameInfo.GameParameters.EspionageEnabled = true;
        civA.DiplomacyFunctions.MakeCivilizationsMeet(civB);
        testGame.MakeHexagonalMap(3);
    }

    [Fact]
    public void EspionageManagerAddSpy()
    {
        var espionageManagerA = civA.EspionageManager;
        Assert.Empty(espionageManagerA.SpyList);
        espionageManagerA.AddSpy();
        Assert.Single(espionageManagerA.SpyList);
    }

    [Fact]
    public void EspionageCheckSpyEffectivenessReductionUnique()
    {
        var spy = civA.EspionageManager.AddSpy();
        var city = civB.AddCity(new HexCoord(1, 1));
        spy.MoveTo(city);
        Assert.Equal(1.0, spy.GetEfficiencyModifier(), 0.1);
        city.CityConstructions.AddBuilding("Constabulary");
        Assert.Equal(0.75, spy.GetEfficiencyModifier(), 0.1);
    }

    [Fact]
    public void SpyEffectivenessCantGoBelowZero()
    {
        var spy = civA.EspionageManager.AddSpy();
        var city = civB.AddCity(new HexCoord(1, 1));
        spy.MoveTo(city);
        city.CityConstructions.AddBuilding("Constabulary");
        city.CityConstructions.AddBuilding("Police Station");
        city.CityConstructions.AddBuilding("National Intelligence Agency");
        city.CityConstructions.AddBuilding("Great Firewall");
        Assert.True(spy.GetEfficiencyModifier() >= 0);
    }
}
