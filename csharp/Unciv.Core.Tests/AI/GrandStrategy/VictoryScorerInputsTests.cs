using Unciv.AI.GrandStrategy.Scorers;
using Unciv.Core.Tests.Support;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map;
using Unciv.UI.Screens.VictoryScreen;

namespace Unciv.Core.Tests.AI;

[Collection("RulesetCache")]
public sealed class VictoryScorerInputsTests
{
    private readonly TestGame game = new();
    private readonly Civilization a;
    private readonly Civilization b;
    private readonly Civilization c;

    public VictoryScorerInputsTests()
    {
        game.MakeHexagonalMap(4);
        a = AddCiv();
        b = AddCiv();
        c = AddCiv();
        game.GameInfo.CurrentPlayerCiv = a;
        game.GameInfo.CurrentPlayer = a.CivId;
    }

    private Civilization AddCiv()
    {
        var civ = game.AddCiv();
        game.AddUnit("Warrior", civ, null);
        return civ;
    }

    private static void Meet(Civilization x, Civilization y) => x.DiplomacyFunctions.MakeCivilizationsMeet(y);

    private void EnableAll() =>
        game.GameInfo.GameParameters.VictoryTypes = ["Domination", "Cultural", "Diplomatic", "Scientific"];

    [Fact]
    public void DisabledVictoriesAreReportedDisabled()
    {
        Assert.False(VictoryScorerInputsFactory.Conquest(a).VictoryEnabled);
        Assert.False(VictoryScorerInputsFactory.Culture(a, 5).VictoryEnabled);
        Assert.False(VictoryScorerInputsFactory.Diplomatic(a, 5).VictoryEnabled);
        Assert.False(VictoryScorerInputsFactory.Spaceship(a).VictoryEnabled);
    }

    [Fact]
    public void EnabledVictoriesAreReportedEnabled()
    {
        EnableAll();
        Assert.True(VictoryScorerInputsFactory.Conquest(a).VictoryEnabled);
        Assert.True(VictoryScorerInputsFactory.Culture(a, 5).VictoryEnabled);
        Assert.True(VictoryScorerInputsFactory.Diplomatic(a, 5).VictoryEnabled);
        Assert.True(VictoryScorerInputsFactory.Spaceship(a).VictoryEnabled);
    }

    [Fact]
    public void ConquestNobodyMetAndPassThroughs()
    {
        game.GameInfo.Turns = 42;
        var input = VictoryScorerInputsFactory.Conquest(a, baseTerm: 7);
        Assert.Equal(42, input.Turn);
        Assert.Equal(7, input.BaseTerm);
        Assert.False(input.HasMetMajor);
        Assert.False(input.AtWar);
        Assert.False(input.Cramped);
        Assert.Equal(0, input.ReferenceStrength);
        Assert.False(input.MetMajorTriggersTail);
    }

    [Fact]
    public void ConquestMetMajorWarAndStrength()
    {
        Meet(a, b);
        game.AddUnit("Warrior", b, null);
        var before = VictoryScorerInputsFactory.Conquest(a);
        Assert.True(before.HasMetMajor);
        Assert.False(before.AtWar);
        Assert.True(before.ReferenceStrength > 0);
        a.GetDiplomacyManager(b)!.DeclareWar();
        Assert.True(VictoryScorerInputsFactory.Conquest(a).AtWar);
        Assert.Equal(a.GetStatForRanking(RankingType.Force), before.MyStrength);
    }

    [Fact]
    public void ConquestCrampedWhenSharingContinent()
    {
        Meet(a, b);
        game.AddCity(a, game.GetTile(HexCoord.Zero));
        game.AddCity(b, game.GetTile(2, 0));
        Assert.True(VictoryScorerInputsFactory.Conquest(a).Cramped);
        Assert.False(VictoryScorerInputsFactory.Conquest(c).Cramped);
    }

    [Fact]
    public void ConquestNotCrampedOnDifferentContinents()
    {
        foreach (var tile in game.TileMap.Values.ToList()) game.SetTileTerrain(tile.Position, Constants.Ocean);
        var t1 = game.SetTileTerrain(HexCoord.Zero, Constants.Desert);
        var t2 = game.SetTileTerrain(new HexCoord(3, 0), Constants.Desert);
        game.TileMap.AssignContinents(TileMap.AssignContinentsMode.Reassign);
        Meet(a, b);
        game.AddCity(a, t1);
        game.AddCity(b, t2);
        Assert.NotEqual(t1.GetContinent(), t2.GetContinent());
        Assert.False(VictoryScorerInputsFactory.Conquest(a).Cramped);
    }

    [Fact]
    public void CultureCountsOnlyKnownMajorRivals()
    {
        Meet(a, b);
        a.TotalCultureForContests = 10;
        b.TotalCultureForContests = 3;
        c.TotalCultureForContests = 99;
        var input = VictoryScorerInputsFactory.Culture(a, 8, unknownX: 2);
        Assert.Equal(10, input.MyCulture);
        Assert.Equal(8, input.CultureFlavor);
        Assert.Equal(2, input.UnknownX);
        Assert.Equal(0, input.MyTourism);
        Assert.Equal(0, input.InfluentialCivCount);
        Assert.Equal([new RivalCultureStats(3, 0)], input.Rivals);
    }

    [Fact]
    public void DiplomaticCountsVotesAndUnknownRivals()
    {
        Meet(a, b);
        game.GameInfo.DiplomaticVictoryVotesCast[a.CivId] = a.CivId;
        game.GameInfo.DiplomaticVictoryVotesCast[b.CivId] = b.CivId;
        game.GameInfo.DiplomaticVictoryVotesCast[c.CivId] = b.CivId;
        var input = VictoryScorerInputsFactory.Diplomatic(a, 6, 1, 2, 3, 4);
        Assert.Equal(1, input.MyVotes);
        Assert.Equal(2, input.BestRivalVotes);
        Assert.Equal(1, input.UnknownRivals); // c is not met
        Assert.False(input.LeagueExists);
        Assert.Equal(0, input.MinorsAttacked);
        Assert.Equal((6, 1, 2, 3, 4), (input.DiplomacyFlavor, input.UnknownX, input.Modifier1, input.Modifier2, input.Modifier3));
        Assert.True(input.VotesNeeded > 0);
    }

    [Fact]
    public void SpaceshipProgressPassThroughAndNoApollo()
    {
        var input = VictoryScorerInputsFactory.Spaceship(a, 4);
        Assert.Equal(4, input.ProgressCount);
        Assert.False(input.HasApolloProgram);
    }

    [Fact]
    public void FactoryRejectsNullCiv()
    {
        Assert.Throws<ArgumentNullException>(() => VictoryScorerInputsFactory.Conquest(null!));
        Assert.Throws<ArgumentNullException>(() => VictoryScorerInputsFactory.Culture(null!, 0));
        Assert.Throws<ArgumentNullException>(() => VictoryScorerInputsFactory.Diplomatic(null!, 0));
        Assert.Throws<ArgumentNullException>(() => VictoryScorerInputsFactory.Spaceship(null!));
    }
}
