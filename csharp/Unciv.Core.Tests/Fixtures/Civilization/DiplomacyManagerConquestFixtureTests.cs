using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T149: the three DiplomacyManagerTests city-conquest cases deferred from T142 (puppetCity/liberateCity reach Battle.DestroyIfDefeated).
[Collection("RulesetCache")]
public sealed class DiplomacyManagerConquestFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly Civilization c;
    private readonly Civilization d;

    // We need to add units so they are not considered defeated, since defeated civs are filtered out of knowncivs
    public DiplomacyManagerConquestFixtureTests()
    {
        a = AddCiv();
        b = AddCiv();
        c = AddCiv();
        d = AddCiv();
        testGame.MakeHexagonalMap(4);
    }

    private Civilization AddCiv(string? cityStateType = null, TileClass? defaultUnitTile = null)
    {
        var civ = testGame.AddCiv(cityStateType: cityStateType);
        testGame.AddUnit("Warrior", civ, defaultUnitTile);
        return civ;
    }

    private static void Meet(Civilization civilization, Civilization otherCivilization) =>
        civilization.DiplomacyFunctions.MakeCivilizationsMeet(otherCivilization);

    private void SetDummyCurrentPlayer()
    {
        // otherwise test crashes when puppetying city
        testGame.GameInfo.CurrentPlayerCiv = AddCiv();
        testGame.GameInfo.CurrentPlayer = testGame.GameInfo.CurrentPlayerCiv.CivId;
    }

    [Fact]
    public void ShouldChangeOpinionsWhenLiberatingCity()
    {
        Meet(a, b);
        Meet(a, c);
        Meet(c, b);
        Meet(a, d);
        Meet(b, d);
        Meet(c, d);

        SetDummyCurrentPlayer();

        var bCity = testGame.AddCity(b, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        testGame.AddCity(b, testGame.GetTile(1, 1)); // another city otherwise b is destroyed when bCity is captured
        bCity.PuppetCity(c);

        bCity.LiberateCity(a);

        var aOpinionOfB = a.GetDiplomacyManager(b)!.OpinionOfOtherCiv();
        var bOpinionOfA = b.GetDiplomacyManager(a)!.OpinionOfOtherCiv();
        var cOpinionOfA = c.GetDiplomacyManager(a)!.OpinionOfOtherCiv();
        var dOpinionOfA = d.GetDiplomacyManager(a)!.OpinionOfOtherCiv();

        Assert.Equal(0f, aOpinionOfB); // A shouldn't change its opinion of others
        Assert.Equal(66f, bOpinionOfA); // massive boost, liberated their city
        Assert.Equal(0f, cOpinionOfA); // city conquering counters liberated city
        Assert.Equal(6f, dOpinionOfA); // small boost, liberated another civ's city
    }

    [Fact]
    public void ShouldChangeOpinionsWhenConqueringCity()
    {
        Meet(a, b);
        Meet(a, c);
        Meet(c, b);

        SetDummyCurrentPlayer();

        var bCity = testGame.AddCity(b, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        testGame.AddCity(b, testGame.GetTile(1, 1)); // another city otherwise b is destroyed when bCity is captured

        bCity.PuppetCity(a);

        var aOpinionOfB = a.GetDiplomacyManager(b)!.OpinionOfOtherCiv();
        var bOpinionOfA = b.GetDiplomacyManager(a)!.OpinionOfOtherCiv();
        var cOpinionOfA = c.GetDiplomacyManager(a)!.OpinionOfOtherCiv();

        Assert.Equal(0f, aOpinionOfB); // A shouldn't change its opinion of others
        Assert.Equal(-85f, bOpinionOfA); // massive penality, conquered their city
        Assert.Equal(-8f, cOpinionOfA); // warmonging penalty
    }

    [Fact]
    public void LiberatingCityStateKeepsThirdPartyInfluenceAndAppliesCiv5LiberatorFormula()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        var liberator = AddCiv();
        var conqueror = AddCiv();
        var ally = AddCiv();
        var rival = AddCiv();

        Meet(liberator, cityState);
        Meet(conqueror, cityState);
        Meet(ally, cityState);
        Meet(rival, cityState);
        Meet(liberator, conqueror);
        Meet(liberator, ally);
        Meet(liberator, rival);

        SetDummyCurrentPlayer();

        var csCity = testGame.AddCity(cityState, testGame.GetTile(HexCoord.Zero), initialPopulation: 2);
        cityState.GetDiplomacyManager(ally)!.SetInfluence(93f);
        cityState.GetDiplomacyManager(rival)!.SetInfluence(-20f);
        cityState.GetDiplomacyManager(liberator)!.SetInfluence(19f);

        csCity.PuppetCity(conqueror);
        Assert.True(cityState.IsDefeated());
        Assert.Equal(93f, cityState.GetDiplomacyManager(ally)!.GetInfluence(), 0.01f);
        Assert.Equal(-20f, cityState.GetDiplomacyManager(rival)!.GetInfluence(), 0.01f);

        csCity.LiberateCity(liberator);

        // third parties unchanged (no full diplo wipe); liberator gets Civ5-style bonus
        Assert.False(cityState.IsDefeated());
        Assert.Equal(cityState, csCity.Civ);
        Assert.Equal(93f, cityState.GetDiplomacyManager(ally)!.GetInfluence(), 0.01f);
        Assert.Equal(-20f, cityState.GetDiplomacyManager(rival)!.GetInfluence(), 0.01f);
        // max(max(-60, 93, -20), 19) + 105 = 93 + 105 = 198
        Assert.Equal(198f, cityState.GetDiplomacyManager(liberator)!.GetInfluence(), 0.01f);
    }
}
