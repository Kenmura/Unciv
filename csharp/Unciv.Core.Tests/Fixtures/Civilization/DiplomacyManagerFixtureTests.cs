using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map;
using Unciv.Models.Ruleset;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Core.Tests.Fixtures.CivilizationFixtures;

// T142: port of tests/src/com/unciv/logic/civilization/diplomacy/DiplomacyManagerTests.kt over the real TestGame.
// The three city-conquest cases (puppetCity/liberateCity reach Battle) are deferred to T149.
[Collection("RulesetCache")]
public sealed class DiplomacyManagerFixtureTests
{
    private readonly TestGame testGame = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly Civilization c;
    private readonly Civilization d;

    // We need to add units so they are not considered defeated, since defeated civs are filtered out of knowncivs
    public DiplomacyManagerFixtureTests()
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

    [Fact]
    public void GetCommonKnownCivsDoesNotIncludeEitherDiplomacyManagersCivs()
    {
        Meet(a, b);
        var commonKnownCivs = a.GetDiplomacyManager(b)!.GetCommonKnownCivs();

        Assert.DoesNotContain(a, commonKnownCivs);
        Assert.DoesNotContain(b, commonKnownCivs);
    }

    [Fact]
    public void GetCommonKnownCivsIncludesCivsMetByBothCivs()
    {
        Meet(a, b);
        Meet(b, c);
        Meet(c, a);
        Assert.Contains(c, a.GetDiplomacyManager(b)!.GetCommonKnownCivs());
    }

    [Fact]
    public void GetCommonKnownCivsDoesNotIncludeCivsMetByOnlyOneCiv()
    {
        Meet(a, b);
        Meet(a, c);
        Assert.DoesNotContain(c, a.GetDiplomacyManager(b)!.GetCommonKnownCivs());
    }

    [Fact]
    public void GetCommonKnownCivsIsEqualForMirroredDiplomacyManagers()
    {
        Meet(a, b);
        Meet(a, c);
        Meet(b, c);
        Meet(a, d);
        Meet(b, d);

        Assert.Equal(a.GetDiplomacyManager(b)!.GetCommonKnownCivs(), b.GetDiplomacyManager(a)!.GetCommonKnownCivs());
    }

    [Fact]
    public void ShouldHave0OpinionWhenJustMet()
    {
        Meet(a, b);
        Assert.Equal(0f, a.GetDiplomacyManager(b)!.OpinionOfOtherCiv());
    }

    [Fact]
    public void ShouldChangeOpinionWhenDenouncing()
    {
        Meet(a, b);
        a.GetDiplomacyManager(b)!.Denounce();

        var aOpinionOfB = a.GetDiplomacyManager(b)!.OpinionOfOtherCiv();
        var bOpinionOfA = b.GetDiplomacyManager(a)!.OpinionOfOtherCiv();

        // B is upset that A denounced them
        Assert.Equal(DiplomacyManager.InitialOpinionChangeWhenDenounced, bOpinionOfA);
        // A denounces B because A already dislikes B; denouncing simply reflects that - it does not change their opinion further
        Assert.Equal(0f, aOpinionOfB);
    }

    [Fact]
    public void ShouldMakeCityStateFriendWhenOverThreshold()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        Meet(a, cityState);

        cityState.GetDiplomacyManager(a)!.AddInfluence(31f);

        Assert.True(cityState.GetDiplomacyManager(a)!.IsRelationshipLevelEQ(RelationshipLevel.Friend));
    }

    [Fact]
    public void ShouldMakeCityStateAlliedWhenOverThresholdAndNoOtherCivAreAllied()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        Meet(a, cityState);

        cityState.GetDiplomacyManager(a)!.AddInfluence(61f);

        Assert.True(cityState.GetDiplomacyManager(a)!.IsRelationshipLevelEQ(RelationshipLevel.Ally));
    }

    [Fact]
    public void ShouldNotMakeCityStateAlliedWhenOverThresholdAndOtherCivHasMoreInfluence()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        Meet(a, cityState);
        Meet(b, cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(70f);

        cityState.GetDiplomacyManager(b)!.AddInfluence(61f);

        Assert.True(cityState.GetDiplomacyManager(a)!.IsRelationshipLevelEQ(RelationshipLevel.Ally));
        Assert.True(cityState.GetDiplomacyManager(b)!.IsRelationshipLevelEQ(RelationshipLevel.Friend));
    }

    [Fact]
    public void ShouldMakeCityStateAlliedWhenOverThresholdAndMostInfluencial()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        Meet(a, cityState);
        Meet(b, cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(61f);

        cityState.GetDiplomacyManager(b)!.AddInfluence(70f);

        Assert.True(cityState.GetDiplomacyManager(a)!.IsRelationshipLevelEQ(RelationshipLevel.Friend));
        Assert.True(cityState.GetDiplomacyManager(b)!.IsRelationshipLevelEQ(RelationshipLevel.Ally));
    }

    [Fact]
    public void ShouldMakeCityStateAngryWhenAtWarRegardlessOfPreviousInfluence()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        Meet(a, cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(61f);

        a.GetDiplomacyManager(cityState)!.DeclareWar();

        Assert.True(cityState.GetDiplomacyManager(a)!.IsRelationshipLevelEQ(RelationshipLevel.Unforgivable));
        Assert.Equal(-60f, cityState.GetDiplomacyManager(a)!.GetInfluence());
    }

    [Fact]
    public void ShouldGainPreviousInfluenceInCityStateAfterIndirectWar()
    {
        // making peace tries to move units around, so we need to initialize their positions
        var cityState = AddCiv("Militaristic", testGame.GetTile(HexCoord.Zero));
        var e = AddCiv(defaultUnitTile: testGame.GetTile(new HexCoord(1, 0)));
        Meet(e, cityState);
        // we cannot be allied and simoultaneously having a city state declare indirect war on us
        cityState.GetDiplomacyManager(e)!.AddInfluence(31f);
        cityState.GetDiplomacyManager(e)!.DeclareWar(new DeclareWarReason(WarType.DefensivePactWar, a));

        e.GetDiplomacyManager(cityState)!.MakePeace();

        Assert.True(cityState.GetDiplomacyManager(e)!.IsRelationshipLevelEQ(RelationshipLevel.Friend));
        Assert.Equal(31f, cityState.GetDiplomacyManager(e)!.GetInfluence());
    }

    [Fact]
    public void ShouldDegradeInfluenceInCityStateOnNextTurn()
    {
        var cityState = AddCiv(cityStateType: "Mercantile");
        cityState.CityStatePersonality = CityStatePersonality.Neutral;
        Meet(a, cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(30f);

        cityState.GetDiplomacyManager(a)!.NextTurn();

        Assert.Equal(29f, cityState.GetDiplomacyManager(a)!.GetInfluence());
    }

    [Fact]
    public void ShouldDegradeInfluenceInHostileCityStateOnNextTurn()
    {
        var cityState = AddCiv(cityStateType: "Militaristic");
        cityState.CityStatePersonality = CityStatePersonality.Hostile;
        Meet(a, cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(30f);

        cityState.GetDiplomacyManager(a)!.NextTurn();

        Assert.Equal(28.5f, cityState.GetDiplomacyManager(a)!.GetInfluence());
    }

    private void SetUpSharedReligionWithCityState(Civilization cityState)
    {
        // to spread religion, need cities
        testGame.AddCity(a, testGame.GetTile(HexCoord.Zero));
        var cityStateCapital = testGame.AddCity(cityState, testGame.GetTile(new HexCoord(1, 0)), initialPopulation: 2);

        var religion = testGame.AddReligion(a);
        var belief = testGame.CreateBelief(BeliefType.Founder, "[+1 Food] from every [Shrine]");
        religion.AddBeliefs([belief]);
        cityStateCapital.Religion.AddPressure(religion.Name, 1000);
    }

    [Fact]
    public void ShouldDegradeInfluenceInCityStateWhenSharingReligionOnNextTurn()
    {
        var cityState = AddCiv(cityStateType: "Mercantile");
        cityState.CityStatePersonality = CityStatePersonality.Neutral;
        Meet(a, cityState);
        SetUpSharedReligionWithCityState(cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(30f);

        cityState.GetDiplomacyManager(a)!.NextTurn();

        Assert.Equal(29.25f, cityState.GetDiplomacyManager(a)!.GetInfluence());
    }

    [Fact]
    public void ShouldIncreaseInfluenceInCityStateWhenUnderRestingPoints()
    {
        var cityState = AddCiv(cityStateType: "Mercantile");
        cityState.CityStatePersonality = CityStatePersonality.Neutral;
        Meet(a, cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(-30f);

        cityState.GetDiplomacyManager(a)!.NextTurn();

        Assert.Equal(-29f, cityState.GetDiplomacyManager(a)!.GetInfluence());
    }

    [Fact]
    public void ShouldIncreaseInfluenceInCityStateWhenUnderRestingPointsAndSharingReligion()
    {
        var cityState = AddCiv(cityStateType: "Mercantile");
        cityState.CityStatePersonality = CityStatePersonality.Neutral;
        Meet(a, cityState);
        SetUpSharedReligionWithCityState(cityState);
        cityState.GetDiplomacyManager(a)!.AddInfluence(-30f);

        cityState.GetDiplomacyManager(a)!.NextTurn();

        Assert.Equal(-28.5f, cityState.GetDiplomacyManager(a)!.GetInfluence());
    }

    [Fact]
    public void ShouldGiveScienceForResearchAgreement()
    {
        Meet(a, b);
        testGame.AddCity(a, testGame.GetTile(HexCoord.Zero), initialPopulation: 10);
        testGame.AddCity(b, testGame.GetTile(new HexCoord(1, 0)), initialPopulation: 20);

        const int expectedSciencePerTurnCivA = 13; // 10 pop, 3 palace. Smaller than 23 science per turn of civ B (20 pop, 3 palace)
        const int turns = 10;

        a.GetDiplomacyManager(b)!.SetFlag(DiplomacyFlags.ResearchAgreement, turns);
        b.GetDiplomacyManager(a)!.SetFlag(DiplomacyFlags.ResearchAgreement, turns);
        for (var i = 0; i < turns; i++)
        {
            a.GetDiplomacyManager(b)!.NextTurn();
            b.GetDiplomacyManager(a)!.NextTurn();
        }

        Assert.False(a.GetDiplomacyManager(b)!.HasFlag(DiplomacyFlags.ResearchAgreement));
        Assert.False(b.GetDiplomacyManager(a)!.HasFlag(DiplomacyFlags.ResearchAgreement));
        Assert.Equal(expectedSciencePerTurnCivA * turns, a.Tech.ScienceFromResearchAgreements);
        Assert.Equal(expectedSciencePerTurnCivA * turns, b.Tech.ScienceFromResearchAgreements);
    }

}
