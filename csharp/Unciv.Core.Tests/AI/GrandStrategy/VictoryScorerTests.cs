using Unciv.AI.Data;
using Unciv.AI.GrandStrategy.Scorers;

namespace Unciv.Core.Tests.AI;

public class VictoryScorerTests
{
    private static readonly GlobalAIDefines Defines = GlobalAIDefines.LoadEmbedded();

    private static ConquestInputs Conquest(
        bool enabled = true, int baseTerm = 0, int turn = 0, bool met = true, int mine = 0, int reference = 0,
        bool war = false, bool cramped = false, bool tail = false) =>
        new(enabled, baseTerm, turn, met, mine, reference, war, cramped, tail);

    [Fact]
    public void ConquestDisabledReturnsMinus100()
    {
        Assert.Equal(-100, new ConquestScorer(Defines).Score(Conquest(enabled: false, baseTerm: 500)));
    }

    [Fact]
    public void ConquestNobodyMetOnlyAfterTurn20()
    {
        var scorer = new ConquestScorer(Defines);
        Assert.Equal(0, scorer.Score(Conquest(turn: 19, met: false)));
        Assert.Equal(-50, scorer.Score(Conquest(turn: 20, met: false)));
        Assert.Equal(0, scorer.Score(Conquest(turn: 20, met: true)));
    }

    [Fact]
    public void ConquestPowerRatioHalvesOnlyUpside()
    {
        var scorer = new ConquestScorer(Defines);
        Assert.Equal(0, scorer.Score(Conquest(turn: 59, mine: 300, reference: 100)));
        Assert.Equal(100, scorer.Score(Conquest(turn: 60, mine: 300, reference: 100))); // 200 / 2
        Assert.Equal(-50, scorer.Score(Conquest(turn: 60, mine: 50, reference: 100)));
        Assert.Equal(0, scorer.Score(Conquest(turn: 60, mine: 50, reference: 0)));
        Assert.Equal(-33, scorer.Score(Conquest(turn: 60, mine: 67, reference: 100))); // truncates toward zero
    }

    [Fact]
    public void ConquestWarCrampedAndTailTerms()
    {
        var scorer = new ConquestScorer(Defines);
        Assert.Equal(40, scorer.Score(Conquest(baseTerm: 10, war: true, cramped: true)));
        Assert.Equal(-40, scorer.Score(Conquest(baseTerm: 10, tail: true)));
    }

    private static CultureInputs Culture(
        bool enabled = true, int flavor = 5, int x = 0, int culture = 100, int tourism = 100,
        RivalCultureStats[]? rivals = null, int influential = 0) =>
        new(enabled, flavor, x, culture, tourism, rivals ?? [], influential);

    [Fact]
    public void CultureDisabledReturnsMinus100()
    {
        Assert.Equal(-100, new CultureScorer(Defines).Score(Culture(enabled: false)));
    }

    [Fact]
    public void CultureBaseTermAndInfluentialBonus()
    {
        var scorer = new CultureScorer(Defines);
        Assert.Equal(100, scorer.Score(Culture(flavor: 5, x: 0))); // 10 * 5 * 200 / 100
        Assert.Equal(0, scorer.Score(Culture(flavor: 5, x: 10)));
        Assert.Equal(150, scorer.Score(Culture(flavor: 5, x: 0, influential: 1)));
    }

    [Fact]
    public void CultureAheadAndTourismAheadWeights()
    {
        var scorer = new CultureScorer(Defines);
        var rivals = new RivalCultureStats[] { new(50, 200), new(60, 10), new(70, 20) };
        // culture: 3 behind me, 0 not -> 3 * 50 / 3; tourism: 2 behind, 1 not -> 1 * 100 / 3
        Assert.Equal(50 + 33, scorer.Score(Culture(flavor: 0, rivals: rivals)));
    }

    [Fact]
    public void CultureTieCountsAsNotAheadAndNoBonusWhenBehind()
    {
        var scorer = new CultureScorer(Defines);
        Assert.Equal(0, scorer.Score(Culture(flavor: 0, rivals: [new(100, 100)])));
        Assert.Equal(0, scorer.Score(Culture(flavor: 0, rivals: [new(500, 500), new(100, 100), new(10, 10)])));
    }

    private static DiplomaticInputs Diplo(
        bool enabled = true, int minors = 0, bool league = true, int flavor = 5, int x = 0, int mine = 0, int rival = 0,
        int unknown = 0, int needed = 20, int c1 = 0, int c2 = 0, int c3 = 0) =>
        new(enabled, minors, league, flavor, x, mine, rival, unknown, needed, c1, c2, c3);

    [Fact]
    public void DiplomaticDisabledReturnsMinus100()
    {
        Assert.Equal(-100, new DiplomaticScorer(Defines).Score(Diplo(enabled: false)));
    }

    [Fact]
    public void DiplomaticNoLeagueUsesFlavorAndMinorPenalty()
    {
        var scorer = new DiplomaticScorer(Defines);
        Assert.Equal(75, scorer.Score(Diplo(league: false, flavor: 5))); // 10 * 5 * 150 / 100
        Assert.Equal(45, scorer.Score(Diplo(league: false, flavor: 5, minors: 1)));
    }

    [Fact]
    public void DiplomaticEnoughVotesWins()
    {
        var scorer = new DiplomaticScorer(Defines);
        Assert.Equal(1000, scorer.Score(Diplo(mine: 20, minors: 3)));
        Assert.NotEqual(1000, scorer.Score(Diplo(mine: 19)));
    }

    [Fact]
    public void DiplomaticThreeQuartersBonusAndLeadClamp()
    {
        var scorer = new DiplomaticScorer(Defines);
        Assert.Equal(40 + 40, scorer.Score(Diplo(mine: 15, rival: 0))); // lead 15 -> 75 clamps to 40
        Assert.Equal(15, scorer.Score(Diplo(mine: 5, rival: 2))); // lead 3 -> 15, below 75%
        Assert.Equal(0, scorer.Score(Diplo(mine: 5, rival: 5)));
    }

    [Fact]
    public void DiplomaticUncertainAndTrailingBranches()
    {
        var scorer = new DiplomaticScorer(Defines);
        Assert.Equal(10, scorer.Score(Diplo(mine: 1, rival: 3, unknown: 2))); // adjusted 2 -> 10
        Assert.Equal(20, scorer.Score(Diplo(mine: 1, rival: 20, unknown: 12))); // adjusted 5 -> 25 clamps to 20
        Assert.Equal(0, scorer.Score(Diplo(mine: 1, rival: 3, unknown: 1))); // adjusted 0
        Assert.Equal(-10, scorer.Score(Diplo(mine: 1, rival: 5, unknown: 1))); // adjusted -2
        Assert.Equal(-40, scorer.Score(Diplo(mine: 0, rival: 19)));
    }

    [Fact]
    public void DiplomaticModifiersAndNonPositiveVotesNeeded()
    {
        var scorer = new DiplomaticScorer(Defines);
        Assert.Equal(7 - 3 + 2, scorer.Score(Diplo(c1: 7, c2: 2, c3: 3)));
        Assert.Equal(0, scorer.Score(Diplo(needed: 0, mine: 5)));
    }

    [Fact]
    public void SpaceshipScoring()
    {
        var scorer = new SpaceshipScorer(Defines);
        Assert.Equal(-100, scorer.Score(new SpaceshipInputs(false, 9, true)));
        Assert.Equal(20, scorer.Score(new SpaceshipInputs(true, 4, false)));
        Assert.Equal(170, scorer.Score(new SpaceshipInputs(true, 4, true)));
    }
}
