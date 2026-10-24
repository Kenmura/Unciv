using Unciv.Models.Metadata;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Simulation;

// Ported from core/src/com/unciv/logic/simulation/Simulation.kt (T101).
//
// Deviations from the Kotlin source:
// - Determinism. Kotlin runs threadsNumber coroutines on Dispatchers.Default, each playing simulationsPerThread games, with map seed 0
//   (= clock seed) and unseeded shuffles. Here the games run one after the other (threadsNumber * simulationsPerThread of them, Non-goals #4;
//   the @Synchronized members need no lock), and everything random derives from the `seed` argument: game i draws its GameStarter seed and
//   its clock reading (map seed 0 and CurrentTurnStartTime/NextTurn's nowMillis) from one GameRandom(seed), in game order. One seed gives
//   one set of games and one report.
// - No wall clock. startTime/totalTurns/avgSpeed/avgDuration/totalDuration and the Timers.singleton start/end are dropped, and so are the three trailing
//   "Average speed", "Average game duration" and "Total time" lines of text(): they were the only wall-clock dependent output.
// - println goes to the `output` callback (null discards). UncivGame.Current.gameInfo!!.ruleset is newGameInfo.Ruleset (the same game in
//   Kotlin's callers). The experiment group is the per-game EngineOptions.CivIdsInExperimentGroup (Kotlin: static DebugUtils), set on each
//   started game right after GameStarter returns; GameStarter itself does not read it.
// - The summary maps are only looked up, never iterated, so they stay Dictionaries; numWins is an insertion-ordered LinkedHashMap.
// - binomialTest/erf use Math.Sqrt/Math.Exp where Kotlin uses kotlin.math (JVM StrictMath-compatible); the p-value is report text only
//   and may differ in the last digit.
// - `String.format("%.1f")` goes through JavaFormat; float/double interpolation uses Java's Float/Double.toString rules (JavaFormat.ToJavaString).

/// <summary>Plays a series of headless AI-only games and tallies wins and population/production/city statistics per major civ.</summary>
public sealed class Simulation
{
    private readonly GameInfo newGameInfo;
    private readonly int threadsNumber;
    private readonly int maxTurns;
    private readonly IReadOnlyList<int> statTurns;
    private readonly ISet<string> civIdsInExperimentGroup;
    private readonly long seed;
    private readonly Action<string>? output;
    private readonly Action<CivilizationObject>? onCivilizationCreated;
    private readonly int maxSimulations;
    private readonly List<string> majorCivs;
    private readonly int numMajorCivs;
    private int talliedGames;
    private int stepCounter;

    public int SimulationsPerThread { get; }
    public LinkedHashMap<string, MutableInt> NumWins { get; } = new();

    private readonly Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summaryStatsPop = new(); // [civ][turn][stat]=value
    private readonly Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summaryStatsProd = new();
    private readonly Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summaryStatsCities = new();
    private readonly Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summaryStatsAvgPop = new();
    private readonly Dictionary<string, Dictionary<string, MutableInt>> winRateByVictory = new();
    private readonly Dictionary<string, Dictionary<string, MutableInt>> winTurnByVictory = new();

    public enum Stat
    {
        SUM,
        NUM,
    }

    // print flags
    private static readonly bool PrintPop = true;
    private static readonly bool PrintProd = false;
    private static readonly bool PrintCityCnt = false;
    private static readonly bool PrintAvgCityPop = false;

    /// <param name="newGameInfo">The game to take the civilizations, setup and ruleset from; each simulated game is started anew from its setup.</param>
    /// <param name="seed">Seeds every game (see the file header).</param>
    /// <param name="simulationsPerThread">Games per (formerly parallel) thread.</param>
    /// <param name="threadsNumber">Number of formerly parallel threads; the games now run sequentially.</param>
    /// <param name="maxTurns">The turn limit of a game that is not won earlier.</param>
    /// <param name="statTurns">The turns whose statistics are sampled (besides the end of the game).</param>
    /// <param name="civIdsInExperimentGroup">The civs that run the experimental AI code, for A/B reporting.</param>
    /// <param name="output">Receives each line Kotlin printed.</param>
    /// <param name="onCivilizationCreated">Passed on to <see cref="GameStarter.StartNewGame"/>.</param>
    public Simulation(GameInfo newGameInfo, long seed, int simulationsPerThread = 1, int threadsNumber = 1, int maxTurns = 500,
        IReadOnlyList<int>? statTurns = null, ISet<string>? civIdsInExperimentGroup = null,
        Action<string>? output = null, Action<CivilizationObject>? onCivilizationCreated = null)
    {
        this.newGameInfo = newGameInfo;
        this.seed = seed;
        SimulationsPerThread = simulationsPerThread;
        this.threadsNumber = threadsNumber;
        this.maxTurns = maxTurns;
        this.statTurns = statTurns ?? [];
        this.civIdsInExperimentGroup = civIdsInExperimentGroup ?? new HashSet<string>();
        this.output = output;
        this.onCivilizationCreated = onCivilizationCreated;
        maxSimulations = threadsNumber * simulationsPerThread;
        majorCivs = newGameInfo.Civilizations.Where(it => !it.IsSpectator() && it.IsMajorCiv()).Select(it => it.CivId).ToList();
        numMajorCivs = newGameInfo.Civilizations.Count(it => !it.IsSpectator() && it.IsMajorCiv());

        var victories = newGameInfo.Ruleset.Victories.Keys.ToList();
        foreach (var civ in majorCivs)
        {
            NumWins[civ] = new MutableInt(0);
            winRateByVictory[civ] = new Dictionary<string, MutableInt>();
            foreach (var victory in victories)
                winRateByVictory[civ][victory] = new MutableInt(0);
            winTurnByVictory[civ] = new Dictionary<string, MutableInt>();
            foreach (var victory in victories)
                winTurnByVictory[civ][victory] = new MutableInt(0);
        }
        InitHash(summaryStatsPop);
        InitHash(summaryStatsProd);
        InitHash(summaryStatsCities);
        InitHash(summaryStatsAvgPop);
    }

    // Need to initialize the values
    // Later will iterate with flatMap
    private void InitHash(Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summary)
    {
        foreach (var civ in majorCivs)
        {
            foreach (var turn in statTurns)
                InitTurn(summary, civ, turn);
            InitTurn(summary, civ, -1); // end of game
        }
    }

    private static void InitTurn(Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summary, string civ, int turn)
    {
        if (!summary.TryGetValue(civ, out var byTurn)) summary[civ] = byTurn = new();
        if (!byTurn.TryGetValue(turn, out var byStat)) byTurn[turn] = byStat = new();
        byStat[Stat.SUM] = new MutableInt(0);
        byStat[Stat.NUM] = new MutableInt(0);
    }

    private void Println(string line) => output?.Invoke(line);

    public void Start()
    {
        Println(
            "Starting new game with major civs: " +
            string.Join(", ", newGameInfo.Civilizations.Where(it => it.IsMajorCiv()).Select(it => it.CivId)) +
            " and minor civs: " +
            string.Join(", ", newGameInfo.Civilizations.Where(it => it.IsCityState).Select(it => it.CivId)));

        newGameInfo.GameParameters.ShufflePlayerOrder = true;

        var seeds = new GameRandom(seed);
        for (var threadId = 1; threadId <= threadsNumber; threadId++)
        {
            for (var run = 0; run < SimulationsPerThread; run++)
            {
                var gameSeed = seeds.NextLong();
                var clockMillis = seeds.NextLong(1L, 1L << 40);
                var step = new SimulationStep(newGameInfo, statTurns);
                var gameSetupInfo = new GameSetupInfo(newGameInfo);
                gameSetupInfo.MapParameters.Seed = 0;
                var gameInfo = GameStarter.StartNewGame(gameSetupInfo, gameSeed, clockMillis, onCivilizationCreated: onCivilizationCreated);
                gameInfo.EngineOptions.CivIdsInExperimentGroup = new LinkedHashSet<string>(civIdsInExperimentGroup);

                gameInfo.SimulateUntilWin = true;

                foreach (var turn in statTurns)
                {
                    gameInfo.SimulateMaxTurns = turn;
                    gameInfo.NextTurn(clockMillis);
                    step.Update(gameInfo);
                    if (step.VictoryType != null) break;
                    step.SaveTurnStats(gameInfo);
                }

                step.Update(gameInfo);

                if (step.VictoryType == null)
                {
                    gameInfo.SimulateMaxTurns = maxTurns;
                    gameInfo.NextTurn(clockMillis);
                    step.Update(gameInfo);
                }

                if (step.VictoryType != null)
                {
                    step.SaveTurnStats(gameInfo);
                    step.Winner = step.CurrentPlayer;
                    Println($"{step.Winner} won {step.VictoryType} victory on turn {step.Turns}");
                }
                else if (gameInfo.GetAliveMajorCivs().Count == 0)
                {
                    Println($"No major civ left on turn {step.Turns}: Draw");
                }
                else
                {
                    Println($"Max simulation {step.Turns} turns reached: Draw");
                }

                UpdateCounter();
                Add(step);
                Print();
            }
        }
    }

    public void Add(SimulationStep step)
    {
        talliedGames++;
        if (step.Winner != null)
        {
            NumWins[step.Winner].Inc();
            winRateByVictory[step.Winner][step.VictoryType.NotNull()].Inc();
            winTurnByVictory[step.Winner][step.VictoryType.NotNull()].Add(step.Turns);
        }
        foreach (var civ in majorCivs)
        {
            foreach (var turn in statTurns)
            {
                SummaryStatSet(summaryStatsPop, civ, turn, step.TurnStatsPop);
                SummaryStatSet(summaryStatsProd, civ, turn, step.TurnStatsProd);
                SummaryStatSet(summaryStatsCities, civ, turn, step.TurnStatsCities);
                if (step.TurnStatsPop[civ][turn].Value != -1 && step.TurnStatsCities[civ][turn].Value != -1)
                {
                    if (step.TurnStatsCities[civ][turn].Value != 0) // if no cities, avgpop=0
                        summaryStatsAvgPop[civ][turn][Stat.SUM].Add(step.TurnStatsPop[civ][turn].Value / step.TurnStatsCities[civ][turn].Value);
                    summaryStatsAvgPop[civ][turn][Stat.NUM].Inc();
                }
            }
            const int endTurn = -1; // end of game
            SummaryStatSet(summaryStatsPop, civ, endTurn, step.TurnStatsPop);
            SummaryStatSet(summaryStatsProd, civ, endTurn, step.TurnStatsProd);
            SummaryStatSet(summaryStatsCities, civ, endTurn, step.TurnStatsCities);
            if (step.TurnStatsCities[civ][endTurn].Value != 0) // if no cities, avgpop=0
                summaryStatsAvgPop[civ][endTurn][Stat.SUM].Add(step.TurnStatsPop[civ][endTurn].Value / step.TurnStatsCities[civ][endTurn].Value);
            summaryStatsAvgPop[civ][endTurn][Stat.NUM].Inc();
        }
    }

    public void UpdateCounter()
    {
        stepCounter++;
        Println($"Simulation step ({stepCounter}/{maxSimulations})");
    }

    public void Print() => Println(Text());

    private static void SummaryStatSet(Dictionary<string, Dictionary<int, Dictionary<Stat, MutableInt>>> summaryHash,
        string civ, int turn, LinkedHashMap<string, LinkedHashMap<int, MutableInt>> stat)
    {
        if (stat[civ][turn].Value != -1)
        {
            summaryHash[civ][turn][Stat.SUM].Add(stat[civ][turn].Value);
            summaryHash[civ][turn][Stat.NUM].Inc();
        }
    }

    // Helper text formatter
    private static string SummaryStatsText(Dictionary<Stat, MutableInt> summaryStats, int turn, string statStr)
    {
        var turnStr = turn == -1 ? "END" : turn.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var avg = (float)summaryStats[Stat.SUM].Value / (float)summaryStats[Stat.NUM].Value;
        return $"@{turnStr}: {statStr} avg={JavaFormat.ToJavaString(avg)} cnt={summaryStats[Stat.NUM].Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n";
    }

    private static string Fmt1(float value) => JavaFormat.Format("%.1f", [value]);

    /// <summary>Win rate and p-value for the group as a whole</summary>
    private string GroupText(string groupName, List<string> groupCivs)
    {
        if (groupCivs.Count == 0) return "";
        var numSteps = Math.Max(talliedGames, 1);
        var groupWins = groupCivs.Aggregate(0, (sum, it) => unchecked(sum + NumWins[it].Value));
        var expWinRate = (float)groupCivs.Count / numMajorCivs;
        var winRate = Fmt1(groupWins * 100f / numSteps);
        var expected = Fmt1(expWinRate * 100f);

        var outString = $"\n{groupName} ({string.Join(", ", groupCivs)}):\n";
        outString += $"{winRate}% total win rate (expected {expected}% if no effect)\n";
        if (numSteps * expWinRate >= 10 && numSteps * (1 - expWinRate) >= 10)
        {
            var pval = BinomialTest(groupWins, numSteps, expWinRate, "greater");
            outString += $"one-tail binomial pval = {JavaFormat.ToJavaString(pval)}\n";
        }
        foreach (var victory in newGameInfo.Ruleset.Victories.Keys)
        {
            var winsVictory = unchecked(groupCivs.Aggregate(0, (sum, it) => unchecked(sum + winRateByVictory[it][victory].Value)) * 100) / Math.Max(groupWins, 1);
            outString += $"{victory}: {winsVictory}%    ";
        }
        outString += "\n";
        return outString;
    }

    public string Text()
    {
        var outString = "";
        if (civIdsInExperimentGroup.Count != 0)
        {
            outString += GroupText("Experiment group", majorCivs.Where(civIdsInExperimentGroup.Contains).ToList());
            outString += GroupText("Control group", majorCivs.Where(it => !civIdsInExperimentGroup.Contains(it)).ToList());
        }
        foreach (var civ in majorCivs)
        {
            var numSteps = Math.Max(talliedGames, 1);
            var expWinRate = 1f / numMajorCivs;
            if (NumWins[civ].Value == 0) continue;
            var winRate = Fmt1(NumWins[civ].Value * 100f / numSteps);

            outString += $"\n{civ}:\n";
            outString += $"{winRate}% total win rate \n";
            if (numSteps * expWinRate >= 10 && numSteps * (1 - expWinRate) >= 10)
            {
                // large enough sample size, binomial distribution approximates the Normal Curve
                var pval = BinomialTest(NumWins[civ].Value, numSteps, expWinRate, "greater");
                outString += $"one-tail binomial pval = {JavaFormat.ToJavaString(pval)}\n";
            }
            foreach (var victory in newGameInfo.Ruleset.Victories.Keys)
            {
                var winsVictory = unchecked(winRateByVictory[civ][victory].Value * 100) / Math.Max(NumWins[civ].Value, 1);
                outString += $"{victory}: {winsVictory}%    ";
            }
            outString += "\n";
            foreach (var victory in newGameInfo.Ruleset.Victories.Keys)
            {
                var winsTurns = winTurnByVictory[civ][victory].Value / Math.Max(winRateByVictory[civ][victory].Value, 1);
                outString += $"{victory}: {winsTurns}    ";
            }
            outString += "avg turns\n";
            foreach (var turn in statTurns)
                outString += TurnText(civ, turn);
            outString += TurnText(civ, -1); // end of match
        }
        return outString;
    }

    private string TurnText(string civ, int turn)
    {
        var text = "";
        if (PrintPop)
            text += SummaryStatsText(summaryStatsPop[civ][turn], turn, "popSum");
        if (PrintProd)
            text += SummaryStatsText(summaryStatsProd[civ][turn], turn, "prodSum");
        if (PrintCityCnt)
            text += SummaryStatsText(summaryStatsCities[civ][turn], turn, "cityCount");
        if (PrintAvgCityPop)
            text += SummaryStatsText(summaryStatsAvgPop[civ][turn], turn, "avgCityPop");
        return text;
    }

    private static double BinomialTest(double successes, double trials, double p, string alternative)
    {
        var q = 1 - p;
        var mean = trials * p;
        var variance = trials * p * q;
        var stdDev = Math.Sqrt(variance);
        var z = (successes - mean) / stdDev;
        var pValue = 1 - NormalCdf(z);
        return alternative switch
        {
            "greater" => pValue,
            "less" => 1 - pValue,
            _ => throw new ArgumentException("Alternative must be 'greater' or 'less'"),
        };
    }

    private static double NormalCdf(double z) => 0.5 * (1 + Erf(z / Math.Sqrt(2.0)));

    // Approximation of the error function
    private static double Erf(double x)
    {
        var t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
        var tau =
            t * Math.Exp(
                -x * x - 1.26551223 +
                t * (1.00002368 + t * (0.37409196 + t * (0.09678418
                + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
                t * (1.48851587 + t * (-0.82215223 + t * 0.17087277))))))))
            );
        return x >= 0 ? 1 - tau : tau - 1;
    }
}
