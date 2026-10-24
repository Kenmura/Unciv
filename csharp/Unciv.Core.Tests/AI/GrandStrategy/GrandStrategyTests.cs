using Unciv.AI.GrandStrategy;
using Unciv.Utils;

namespace Unciv.Core.Tests.AI;

public class GrandStrategyTests
{
    [Fact]
    public void StrategyEnumAndStringRoundTrip()
    {
        Assert.Equal(4, GrandStrategyExtensions.All.Count);
        Assert.Equal(GrandStrategy.Conquest, GrandStrategyExtensions.All[0]);
        Assert.Equal(GrandStrategy.Culture, GrandStrategyExtensions.All[1]);
        Assert.Equal(GrandStrategy.UnitedNations, GrandStrategyExtensions.All[2]);
        Assert.Equal(GrandStrategy.Spaceship, GrandStrategyExtensions.All[3]);

        foreach (var s in GrandStrategyExtensions.All)
        {
            string name = s.ToStrategyName();
            Assert.StartsWith("AIGRANDSTRATEGY_", name, StringComparison.Ordinal);
            Assert.Equal(s, GrandStrategyExtensions.ParseStrategy(name));
            Assert.True(GrandStrategyExtensions.TryParseStrategy(name, out var parsed));
            Assert.Equal(s, parsed);
        }

        Assert.Equal("NONE", GrandStrategy.None.ToStrategyName());
        Assert.Equal(GrandStrategy.None, GrandStrategyExtensions.ParseStrategy("NONE"));
        Assert.True(GrandStrategyExtensions.TryParseStrategy("NONE", out var noneStrategy));
        Assert.Equal(GrandStrategy.None, noneStrategy);

        Assert.False(GrandStrategyExtensions.TryParseStrategy("UNKNOWN_STRATEGY", out _));
        Assert.Throws<ArgumentException>(() => GrandStrategyExtensions.ParseStrategy("INVALID"));
    }

    [Fact]
    public void PersonalityFlavorsDefaultAndClamping()
    {
        var personality = new LeaderPersonalityFlavors();
        Assert.Equal(LeaderPersonalityFlavors.DefaultFlavorValue, personality["FLAVOR_OFFENSE"]);
        Assert.False(personality.HasFlavor("FLAVOR_OFFENSE"));

        personality["FLAVOR_OFFENSE"] = 10;
        Assert.Equal(10, personality["FLAVOR_OFFENSE"]);
        Assert.True(personality.HasFlavor("FLAVOR_OFFENSE"));

        // Clamping to [0..20]
        personality["FLAVOR_DEFENSE"] = -5;
        Assert.Equal(0, personality["FLAVOR_DEFENSE"]);

        personality["FLAVOR_CULTURE"] = 25;
        Assert.Equal(20, personality["FLAVOR_CULTURE"]);

        var clone = personality.Clone();
        Assert.Equal(10, clone["FLAVOR_OFFENSE"]);
        Assert.Equal(0, clone["FLAVOR_DEFENSE"]);
        Assert.Equal(20, clone["FLAVOR_CULTURE"]);

        // Mutating clone does not affect original
        clone["FLAVOR_OFFENSE"] = 15;
        Assert.Equal(10, personality["FLAVOR_OFFENSE"]);
    }

    [Fact]
    public void PersonalityFlavorsRandomizationIsDeterministicAndBounded()
    {
        var p1 = new LeaderPersonalityFlavors(
        [
            new("FLAVOR_OFFENSE", 10),
            new("FLAVOR_DEFENSE", 1),
            new("FLAVOR_CULTURE", 19),
        ]);
        var p2 = p1.Clone();

        var rng1 = new GameRandom(42);
        var rng2 = new GameRandom(42);

        p1.Randomize(rng1, range: 2);
        p2.Randomize(rng2, range: 2);

        // Deterministic across identical seeds
        Assert.Equal(p1["FLAVOR_OFFENSE"], p2["FLAVOR_OFFENSE"]);
        Assert.Equal(p1["FLAVOR_DEFENSE"], p2["FLAVOR_DEFENSE"]);
        Assert.Equal(p1["FLAVOR_CULTURE"], p2["FLAVOR_CULTURE"]);

        // Values shifted within [-2..+2] and clamped to [0..20]
        Assert.InRange(p1["FLAVOR_OFFENSE"], 8, 12);
        Assert.InRange(p1["FLAVOR_DEFENSE"], 0, 3);
        Assert.InRange(p1["FLAVOR_CULTURE"], 17, 20);
    }

    [Fact]
    public void PersonalityFlavorsGrandStrategyModifiersMatchSpec()
    {
        var personality = new LeaderPersonalityFlavors(
        [
            new("FLAVOR_OFFENSE", 8),
            new("FLAVOR_EXPANSION", 6),
            new("FLAVOR_PRODUCTION", 5),
            new("FLAVOR_CULTURE", 7),
            new("FLAVOR_DIPLOMACY", 5),
            new("FLAVOR_SPACESHIP", 6),
            new("FLAVOR_RELIGION", 6),
        ]);

        // When Conquest is active: OFFENSE +2, EXPANSION +1, PRODUCTION +1
        Assert.Equal(10, personality.GetModifiedFlavor("FLAVOR_OFFENSE", GrandStrategy.Conquest));
        Assert.Equal(7, personality.GetModifiedFlavor("FLAVOR_EXPANSION", GrandStrategy.Conquest));
        Assert.Equal(6, personality.GetModifiedFlavor("FLAVOR_PRODUCTION", GrandStrategy.Conquest));
        Assert.Equal(7, personality.GetModifiedFlavor("FLAVOR_CULTURE", GrandStrategy.Conquest)); // unchanged

        // When Culture is active: CULTURE +2, EXPANSION -2, SPACESHIP -2
        Assert.Equal(9, personality.GetModifiedFlavor("FLAVOR_CULTURE", GrandStrategy.Culture));
        Assert.Equal(4, personality.GetModifiedFlavor("FLAVOR_EXPANSION", GrandStrategy.Culture));
        Assert.Equal(4, personality.GetModifiedFlavor("FLAVOR_SPACESHIP", GrandStrategy.Culture));

        // When Spaceship is active: RELIGION -5, SPACESHIP +2, SCIENCE +2
        Assert.Equal(1, personality.GetModifiedFlavor("FLAVOR_RELIGION", GrandStrategy.Spaceship));
        Assert.Equal(8, personality.GetModifiedFlavor("FLAVOR_SPACESHIP", GrandStrategy.Spaceship));

        // When None is active: base values
        Assert.Equal(8, personality.GetModifiedFlavor("FLAVOR_OFFENSE", GrandStrategy.None));
    }

    [Fact]
    public void DotProductCalculationMatchesCiv5Weights()
    {
        var evaluator = new GrandStrategyEvaluator();

        var personality = new LeaderPersonalityFlavors(
        [
            new("FLAVOR_OFFENSE", 9),
            new("FLAVOR_CULTURE", 8),
            new("FLAVOR_DIPLOMACY", 7),
            new("FLAVOR_SPACESHIP", 6),
        ]);

        // Weights from AIGrandStrategy_Flavors:
        // CONQUEST: OFFENSE = 9 -> 9 * 9 = 81
        // CULTURE: CULTURE = 10 -> 8 * 10 = 80
        // UNITED_NATIONS: DIPLOMACY = 10 -> 7 * 10 = 70
        // SPACESHIP: SPACESHIP = 13 -> 6 * 13 = 78
        Assert.Equal(81, evaluator.CalculateDotProduct(GrandStrategy.Conquest, personality));
        Assert.Equal(80, evaluator.CalculateDotProduct(GrandStrategy.Culture, personality));
        Assert.Equal(70, evaluator.CalculateDotProduct(GrandStrategy.UnitedNations, personality));
        Assert.Equal(78, evaluator.CalculateDotProduct(GrandStrategy.Spaceship, personality));
    }

    [Fact]
    public void InitialEvaluationSelectsHighestScoringStrategy()
    {
        var evaluator = new GrandStrategyEvaluator();
        var player = new GrandStrategyPlayerState
        {
            CurrentStrategy = GrandStrategy.None,
            TurnsActive = 0,
            PersonalityFlavors = new LeaderPersonalityFlavors(
            [
                new("FLAVOR_OFFENSE", 4),
                new("FLAVOR_CULTURE", 12), // 12 * 10 = 120
                new("FLAVOR_DIPLOMACY", 5),
                new("FLAVOR_SPACESHIP", 5),
            ]),
        };

        var result = evaluator.Evaluate(player);

        Assert.Equal(GrandStrategy.Culture, result.ChosenStrategy);
        Assert.Equal(GrandStrategy.Culture, player.CurrentStrategy);
        Assert.True(result.StrategyChanged);
        Assert.Equal(0, player.TurnsActive); // reset to 0 on new strategy adoption
        Assert.Equal(120, result.FinalScores[GrandStrategy.Culture]);
    }

    [Fact]
    public void TieResolvesToFirstInCanonicalOrder()
    {
        // When all flavors produce the exact same dot product (e.g. 0), strict '>' picks Conquest (index 0)
        var evaluator = new GrandStrategyEvaluator();
        var player = new GrandStrategyPlayerState
        {
            CurrentStrategy = GrandStrategy.None,
            TurnsActive = 0,
            PersonalityFlavors = new LeaderPersonalityFlavors(
            [
                new("FLAVOR_OFFENSE", 0),
                new("FLAVOR_CULTURE", 0),
                new("FLAVOR_DIPLOMACY", 0),
                new("FLAVOR_SPACESHIP", 0),
            ]),
        };

        var result = evaluator.Evaluate(player);

        Assert.Equal(GrandStrategy.Conquest, result.ChosenStrategy);
        Assert.Equal(GrandStrategy.Conquest, player.CurrentStrategy);
    }

    [Fact]
    public void TenTurnLockPreventsMidLockStrategyChanges()
    {
        var evaluator = new GrandStrategyEvaluator();
        var player = new GrandStrategyPlayerState
        {
            CurrentStrategy = GrandStrategy.Culture,
            TurnsActive = 0,
            PersonalityFlavors = new LeaderPersonalityFlavors(
            [
                new("FLAVOR_OFFENSE", 20), // 20 * 9 = 180 (far higher than culture)
                new("FLAVOR_CULTURE", 5),  // 5 * 10 + 50 stick = 100
                new("FLAVOR_DIPLOMACY", 0),
                new("FLAVOR_SPACESHIP", 0),
            ]),
        };

        // Turns 1 through 9: locked, cannot change despite Conquest scoring higher
        for (int turn = 1; turn <= 9; turn++)
        {
            var result = evaluator.Evaluate(player);
            Assert.Equal(GrandStrategy.Culture, result.ChosenStrategy);
            Assert.Equal(GrandStrategy.Culture, player.CurrentStrategy);
            Assert.False(result.StrategyChanged);
            Assert.Equal(turn, player.TurnsActive);
        }

        // Turn 10: lock expires (TurnsActive reaches 10 >= 10), switches to Conquest!
        var turn10Result = evaluator.Evaluate(player);
        Assert.Equal(GrandStrategy.Conquest, turn10Result.ChosenStrategy);
        Assert.Equal(GrandStrategy.Conquest, player.CurrentStrategy);
        Assert.True(turn10Result.StrategyChanged);
        Assert.Equal(0, player.TurnsActive); // reset on change
    }

    [Fact]
    public void HysteresisStickBonusProtectsCurrentStrategy()
    {
        var evaluator = new GrandStrategyEvaluator();

        // Culture dot = 80, Spaceship dot = 104
        // Difference = 24 (less than stick weight 50)
        var player = new GrandStrategyPlayerState
        {
            CurrentStrategy = GrandStrategy.Culture,
            TurnsActive = 10, // lock already expired
            PersonalityFlavors = new LeaderPersonalityFlavors(
            [
                new("FLAVOR_OFFENSE", 0),
                new("FLAVOR_CULTURE", 8),   // 8 * 10 + 50 stick = 130
                new("FLAVOR_DIPLOMACY", 0),
                new("FLAVOR_SPACESHIP", 8),  // 8 * 13 = 104
            ]),
        };

        var result = evaluator.Evaluate(player);

        // Culture wins because of the +50 stick bonus (130 > 104)
        Assert.Equal(GrandStrategy.Culture, result.ChosenStrategy);
        Assert.Equal(130, result.FinalScores[GrandStrategy.Culture]);
        Assert.Equal(104, result.FinalScores[GrandStrategy.Spaceship]);
        Assert.False(result.StrategyChanged);
        Assert.Equal(11, player.TurnsActive);
    }

    [Fact]
    public void RivalChasingPenaltyDiscouragesContestedStrategies()
    {
        var evaluator = new GrandStrategyEvaluator();

        // Base score for Conquest: 10 * 9 = 90
        var player = new GrandStrategyPlayerState
        {
            CurrentStrategy = GrandStrategy.None,
            TurnsActive = 0,
            MetMajorCivsCount = 4, // self + 3 others
            PersonalityFlavors = new LeaderPersonalityFlavors(
            [
                new("FLAVOR_OFFENSE", 10),
                new("FLAVOR_CULTURE", 8),
                new("FLAVOR_DIPLOMACY", 0),
                new("FLAVOR_SPACESHIP", 0),
            ]),
        };

        // 2 rivals guessed to be pursuing Conquest
        player.GuessedStrategiesOfOthers.Add(GrandStrategy.Conquest);
        player.GuessedStrategiesOfOthers.Add(GrandStrategy.Conquest);

        // Conquest: score = 90.
        // n = 2, multiplier = 50, met = 4
        // delta = -(((2 * 90 * 50) / 4) / 100) = -((9000 / 4) / 100) = -(2250 / 100) = -22
        // final Conquest score = 90 - 22 = 68
        // Culture: score = 80 (n = 0, delta = 0).
        var result = evaluator.Evaluate(player);

        var conquestBreakdown = result.Breakdowns[GrandStrategy.Conquest];
        Assert.Equal(90, conquestBreakdown.BaseScore);
        Assert.Equal(-22, conquestBreakdown.RivalPenalty);
        Assert.Equal(68, conquestBreakdown.TotalScore);
        Assert.Equal(68, result.FinalScores[GrandStrategy.Conquest]);

        // Culture wins (80 > 68) because Conquest was penalized by rivals!
        Assert.Equal(GrandStrategy.Culture, result.ChosenStrategy);
    }

    [Fact]
    public void DisabledVictoryHeavilyPenalizesStrategy()
    {
        var evaluator = new GrandStrategyEvaluator();
        var player = new GrandStrategyPlayerState
        {
            CurrentStrategy = GrandStrategy.None,
            TurnsActive = 0,
            PersonalityFlavors = new LeaderPersonalityFlavors(
            [
                new("FLAVOR_OFFENSE", 10), // base 90
                new("FLAVOR_CULTURE", 5),  // base 50
                new("FLAVOR_DIPLOMACY", 0),
                new("FLAVOR_SPACESHIP", 0),
            ]),
        };

        // Domination victory is disabled -> returns -100
        int Scorer(GrandStrategy s) => s == GrandStrategy.Conquest ? GrandStrategyEvaluator.DisabledVictoryScore : 0;

        var result = evaluator.Evaluate(player, victoryScorer: Scorer);

        // Conquest: 90 - 100 = -10
        // Culture: 50
        Assert.Equal(-10, result.FinalScores[GrandStrategy.Conquest]);
        Assert.Equal(50, result.FinalScores[GrandStrategy.Culture]);
        Assert.Equal(GrandStrategy.Culture, result.ChosenStrategy);
    }

    [Fact]
    public void RandomNoiseIsDeterministicWithSeededRng()
    {
        var evaluator = new GrandStrategyEvaluator();
        var player1 = new GrandStrategyPlayerState
        {
            PersonalityFlavors = new LeaderPersonalityFlavors([new("FLAVOR_OFFENSE", 10)]),
        };
        var player2 = new GrandStrategyPlayerState
        {
            PersonalityFlavors = new LeaderPersonalityFlavors([new("FLAVOR_OFFENSE", 10)]),
        };

        var rng1 = new GameRandom(12345);
        var rng2 = new GameRandom(12345);

        var res1 = evaluator.Evaluate(player1, random: rng1);
        var res2 = evaluator.Evaluate(player2, random: rng2);

        foreach (var s in GrandStrategyExtensions.All)
        {
            Assert.Equal(res1.Breakdowns[s].Noise, res2.Breakdowns[s].Noise);
            Assert.InRange(res1.Breakdowns[s].Noise, 0, 49);
            Assert.Equal(res1.FinalScores[s], res2.FinalScores[s]);
        }
    }
}
