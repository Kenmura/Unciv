using Unciv.AI.Data;
using Unciv.Utils;

namespace Unciv.AI.GrandStrategy;

/// <summary>
/// State tracking grand strategy progression for an AI civilization (§3.2 in SPEC.md).
/// </summary>
public sealed class GrandStrategyPlayerState
{
    public GrandStrategy CurrentStrategy { get; set; } = GrandStrategy.None;
    public int TurnsActive { get; set; } = 0;
    public LeaderPersonalityFlavors PersonalityFlavors { get; set; } = new();
    public List<GrandStrategy> GuessedStrategiesOfOthers { get; } = [];
    public int MetMajorCivsCount { get; set; } = 1;
}

/// <summary>
/// Detailed breakdown of score components for one grand strategy (§3.2 in SPEC.md).
/// </summary>
public sealed record GrandStrategyScoreBreakdown(
    int BaseScore,
    int VictoryScore,
    int Noise,
    int Hysteresis,
    int RivalPenalty,
    int TotalScore
);

/// <summary>
/// Result of evaluating grand strategies for an AI player on a turn (§3.2 in SPEC.md).
/// </summary>
public sealed class GrandStrategyEvaluationResult
{
    public GrandStrategy ChosenStrategy { get; }
    public bool StrategyChanged { get; }
    public IReadOnlyDictionary<GrandStrategy, int> FinalScores { get; }
    public IReadOnlyDictionary<GrandStrategy, GrandStrategyScoreBreakdown> Breakdowns { get; }

    public GrandStrategyEvaluationResult(
        GrandStrategy chosenStrategy,
        bool strategyChanged,
        IReadOnlyDictionary<GrandStrategy, int> finalScores,
        IReadOnlyDictionary<GrandStrategy, GrandStrategyScoreBreakdown> breakdowns)
    {
        ChosenStrategy = chosenStrategy;
        StrategyChanged = strategyChanged;
        FinalScores = finalScores;
        Breakdowns = breakdowns;
    }
}

/// <summary>
/// Evaluates and selects an AI player's Grand Strategy (§3.2 in SPEC.md).
/// Implements the Civ 5 per-turn algorithm: dot-product flavor scoring,
/// victory scorer integration, RNG noise, current-strategy hysteresis (+50),
/// rival-chasing penalty, and 10-turn lock.
/// </summary>
public sealed class GrandStrategyEvaluator
{
    public const int DisabledVictoryScore = -100;

    private readonly GlobalAIDefines defines;
    private readonly GrandStrategyFlavors flavorsData;

    public AIEdition Edition { get; }

    private readonly int randRoll;
    private readonly int currentStrategyWeight;
    private readonly int turnsMustBeActive;
    private readonly int otherPlayersMultiplier;

    public GrandStrategyEvaluator(
        GlobalAIDefines? defines = null,
        GrandStrategyFlavors? flavors = null,
        AIEdition edition = AIEdition.BNW)
    {
        this.defines = defines ?? GlobalAIDefines.LoadEmbedded();
        this.flavorsData = flavors ?? GrandStrategyFlavors.LoadEmbedded();
        this.Edition = edition;

        randRoll = this.defines.IsDefined("AI_GS_RAND_ROLL", edition)
            ? this.defines.GetInt("AI_GS_RAND_ROLL", edition)
            : 50;

        currentStrategyWeight = this.defines.IsDefined("AI_GRAND_STRATEGY_CURRENT_STRATEGY_WEIGHT", edition)
            ? this.defines.GetInt("AI_GRAND_STRATEGY_CURRENT_STRATEGY_WEIGHT", edition)
            : 50;

        turnsMustBeActive = this.defines.IsDefined("AI_GRAND_STRATEGY_NUM_TURNS_STRATEGY_MUST_BE_ACTIVE", edition)
            ? this.defines.GetInt("AI_GRAND_STRATEGY_NUM_TURNS_STRATEGY_MUST_BE_ACTIVE", edition)
            : 10;

        otherPlayersMultiplier = this.defines.IsDefined("AI_GRAND_STRATEGY_OTHER_PLAYERS_GS_MULTIPLIER", edition)
            ? this.defines.GetInt("AI_GRAND_STRATEGY_OTHER_PLAYERS_GS_MULTIPLIER", edition)
            : 50;
    }

    /// <summary>
    /// Computes the dot product between strategy flavor weights and leader personality flavors:
    /// Σ_f W[s][f] * myFlavor[f] (§3.2 in SPEC.md).
    /// </summary>
    public int CalculateDotProduct(GrandStrategy strategy, LeaderPersonalityFlavors personality)
    {
        ArgumentNullException.ThrowIfNull(personality);
        if (strategy == GrandStrategy.None) return 0;

        int sum = 0;
        string strategyName = strategy.ToStrategyName();
        foreach (var entry in flavorsData.Entries(GrandStrategyFlavors.FlavorsTable, strategyName))
        {
            sum += entry.Value * personality.GetFlavor(entry.Key);
        }
        return sum;
    }

    /// <summary>
    /// Executes the full per-turn grand strategy evaluation algorithm (§3.2 in SPEC.md).
    /// </summary>
    public GrandStrategyEvaluationResult Evaluate(
        GrandStrategyPlayerState player,
        Func<GrandStrategy, int>? victoryScorer = null,
        GameRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        var scores = new OrderedDictionary<GrandStrategy, int>();
        var breakdowns = new OrderedDictionary<GrandStrategy, GrandStrategyScoreBreakdown>();

        // 1) Score every strategy
        foreach (var s in GrandStrategyExtensions.All)
        {
            int baseScore = CalculateDotProduct(s, player.PersonalityFlavors);
            int victoryScore = victoryScorer?.Invoke(s) ?? 0;
            int noise = (random != null && randRoll > 0) ? random.NextInt(randRoll) : 0;
            int stick = (s == player.CurrentStrategy) ? currentStrategyWeight : 0;

            int score = baseScore + victoryScore + noise + stick;
            scores[s] = score;

            breakdowns[s] = new GrandStrategyScoreBreakdown(
                baseScore,
                victoryScore,
                noise,
                stick,
                0,
                score
            );
        }

        // 2) Discourage chasing what rivals already chase
        // met = number of alive major civs I have met (includes me) [I]
        int met = Math.Max(1, player.MetMajorCivsCount);
        foreach (var s in GrandStrategyExtensions.All)
        {
            int n = player.GuessedStrategiesOfOthers.Count(g => g == s);
            // delta = -(((n * score[s] * AI_GRAND_STRATEGY_OTHER_PLAYERS_GS_MULTIPLIER) / met) / 100)
            int delta = -(((n * scores[s] * otherPlayersMultiplier) / met) / 100);
            if (delta != 0)
            {
                scores[s] += delta;
            }

            var b = breakdowns[s];
            breakdowns[s] = b with
            {
                RivalPenalty = delta,
                TotalScore = scores[s]
            };
        }

        // 3) Commit only after the lock expires
        player.TurnsActive++;
        bool lockExpired = player.CurrentStrategy == GrandStrategy.None ||
                           player.TurnsActive >= turnsMustBeActive;

        GrandStrategy chosen = player.CurrentStrategy;
        bool changed = false;

        if (lockExpired)
        {
            // best = argmax(score) - strict '>' so ties resolve to the FIRST index [O]
            GrandStrategy best = GrandStrategyExtensions.All[0];
            int bestScore = scores[best];

            for (int i = 1; i < GrandStrategyExtensions.All.Count; i++)
            {
                var candidate = GrandStrategyExtensions.All[i];
                int candidateScore = scores[candidate];
                if (candidateScore > bestScore)
                {
                    best = candidate;
                    bestScore = candidateScore;
                }
            }

            if (best != player.CurrentStrategy)
            {
                player.CurrentStrategy = best;
                player.TurnsActive = 0;
                changed = true;
            }
            chosen = player.CurrentStrategy;
        }

        return new GrandStrategyEvaluationResult(chosen, changed, scores, breakdowns);
    }
}
