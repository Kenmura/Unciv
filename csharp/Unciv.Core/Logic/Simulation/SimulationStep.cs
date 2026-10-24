using Unciv.Utils;

namespace Unciv.Logic.Simulation;

// Ported from core/src/com/unciv/logic/simulation/SimulationStep.kt (T101).
//
// Deviation: the [civ][turn] maps are insertion-ordered LinkedHashMaps (Kotlin mutableMapOf is a LinkedHashMap).

/// <summary>The statistics of one simulated game: per major civ and sampled turn, the population, production and city count.</summary>
public sealed class SimulationStep
{
    private readonly List<string> civilizationNames;

    public int Turns { get; private set; }
    public string? VictoryType { get; private set; }
    public string? Winner { get; set; }
    public string CurrentPlayer { get; private set; }

    /// <summary>[civ][turn]; turn -1 is the end of the game.</summary>
    public LinkedHashMap<string, LinkedHashMap<int, MutableInt>> TurnStatsPop { get; } = new();
    public LinkedHashMap<string, LinkedHashMap<int, MutableInt>> TurnStatsProd { get; } = new();
    public LinkedHashMap<string, LinkedHashMap<int, MutableInt>> TurnStatsCities { get; } = new();

    public SimulationStep(GameInfo gameInfo, IReadOnlyList<int>? statTurns = null)
    {
        statTurns ??= [];
        civilizationNames = gameInfo.Civilizations.Where(it => !it.IsSpectator() && it.IsMajorCiv()).Select(it => it.CivId).ToList();
        Turns = gameInfo.Turns;
        VictoryType = gameInfo.GetCurrentPlayerCivilization().VictoryManager.GetVictoryTypeAchieved();
        CurrentPlayer = gameInfo.CurrentPlayer;

        foreach (var civ in civilizationNames)
        {
            foreach (var turn in statTurns)
            {
                GetOrPut(TurnStatsPop, civ)[turn] = new MutableInt(-1);
                GetOrPut(TurnStatsProd, civ)[turn] = new MutableInt(-1);
                GetOrPut(TurnStatsCities, civ)[turn] = new MutableInt(-1);
            }
            GetOrPut(TurnStatsPop, civ)[-1] = new MutableInt(-1); // end of game
            GetOrPut(TurnStatsProd, civ)[-1] = new MutableInt(-1); // end of game
            GetOrPut(TurnStatsCities, civ)[-1] = new MutableInt(-1); // end of game
        }
    }

    private static LinkedHashMap<int, MutableInt> GetOrPut(LinkedHashMap<string, LinkedHashMap<int, MutableInt>> map, string civ)
    {
        if (map.TryGetValue(civ, out var existing)) return existing;
        var created = new LinkedHashMap<int, MutableInt>();
        map[civ] = created;
        return created;
    }

    public void SaveTurnStats(GameInfo gameInfo)
    {
        VictoryType = gameInfo.GetCurrentPlayerCivilization().VictoryManager.GetVictoryTypeAchieved();
        var turn = VictoryType != null ? -1 : gameInfo.Turns;
        foreach (var civ in gameInfo.Civilizations.Where(it => !it.IsSpectator() && it.IsMajorCiv()).ToList())
        {
            // Kotlin sumOf over Int wraps on overflow; LINQ Sum would throw, hence the unchecked Aggregate
            var popsum = civ.Cities.Aggregate(0, (sum, it) => unchecked(sum + it.Population.Population));
            TurnStatsPop[civ.CivId][turn].Set(popsum);
            var prodsum = civ.Cities.Aggregate(0, (sum, it) => unchecked(sum + KotlinMath.RoundToInt(it.CityStats.CurrentCityStats.Production)));
            TurnStatsProd[civ.CivId][turn].Set(prodsum);
            var cityCnt = civ.Cities.Count;
            TurnStatsCities[civ.CivId][turn].Set(cityCnt);
        }
    }

    public void Update(GameInfo gameInfo)
    {
        Turns = gameInfo.Turns;
        VictoryType = gameInfo.GetCurrentPlayerCivilization().VictoryManager.GetVictoryTypeAchieved();
        CurrentPlayer = gameInfo.CurrentPlayer;
    }
}
