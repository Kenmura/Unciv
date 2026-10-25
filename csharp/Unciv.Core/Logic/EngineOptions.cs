using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Logic;

// core/src/com/unciv/utils/DebugUtils.kt (only the part the game rules read; the UI flags SHOW_TILE_COORDS, SHOW_TILE_IMAGE_LOCATIONS and
// SHOW_FPS are Won't-port, see T043 and T048)

/// <summary>
/// The upstream <c>DebugUtils</c> flags that change what the game rules compute, as an instance owned by the game instead of a process-wide
/// mutable static. Because they change results (extra yields, whole-map vision, turn simulation) they are game state: every peer must hold
/// the same values, they take part in the state hash, and they may only be changed through a command (never by a client setting a static).
/// Upstream writes two of them from engine code (<c>SimulateUntilTurn</c> is reset to 0 when the turn is reached, the experiment group is set
/// by the simulation), so the object is mutable.
/// </summary>
/// <remarks>
/// Users to port: <c>GameInfo</c> (<see cref="SimulateUntilTurn"/>: <c>isSimulation()</c> is <c>turns &lt; SimulateUntilTurn</c>, <c>nextTurn</c> resets it),
/// <c>CityStats</c> (<see cref="Supercharged"/>), <c>CivInfoTransientCache</c> and <c>Tile.isVisible</c> (<see cref="VisibleMap"/>),
/// <c>Simulation</c> (<see cref="CivIdsInExperimentGroup"/>), <c>CityLocationTileRanker</c> (<see cref="ShowSettlerScores"/>, <see cref="SettlerScores"/>).
/// The object belongs to the game (a property of GameInfo, copied by GameInfo.Clone), not to a static.
/// </remarks>
public sealed class EngineOptions : IStateHashable
{
    /// <summary>Debugging aid: every tile is visible. Remember to turn this off before a real game.</summary>
    public bool VisibleMap { get; set; }

    /// <summary>For when you need to test something in an advanced game and don't have time to faff around.</summary>
    public bool Supercharged { get; set; }

    /// <summary>Simulate until this turn on the first "Next turn". 0 disables it.</summary>
    public int SimulateUntilTurn { get; set; }

    /// <summary>For A/B testing against the unchanged AI: experimental code is gated by <c>civ.civID in CivIdsInExperimentGroup</c>.</summary>
    public LinkedHashSet<string> CivIdsInExperimentGroup { get; set; } = [];

    /// <summary>Diagnostics only, never read by rules and not part of the state hash: ask the settler ranking to keep its scores.</summary>
    public bool ShowSettlerScores { get; set; }

    /// <summary>Diagnostics only (output of <c>CityLocationTileRanker</c> when <see cref="ShowSettlerScores"/> is on), not part of the state hash.</summary>
    public IReadOnlyDictionary<HexCoord, float> SettlerScores { get; set; } = new Dictionary<HexCoord, float>();

    /// <summary>
    /// <c>settings.useAStarPathfinding</c> (default false; T183: a per-game rule fixed at game start, since it changes path results): installs the A* <see cref="Map.PathingMap.AStarPathing"/> into <see cref="UnitPathing"/>,
    /// <see cref="AttackPathing"/> and <see cref="RoadPathing"/>, or removes it. Those three can also be set separately (tests install stand-ins).
    /// </summary>
    public bool UseAStarPathfinding
    {
        get => UnitPathing != null || AttackPathing != null || RoadPathing != null;
        set
        {
            UnitPathing = value ? Map.PathingMap.AStarPathing.Instance : null;
            AttackPathing = value ? Map.PathingMap.AStarPathing.Instance : null;
            RoadPathing = value ? Map.PathingMap.AStarPathing.Instance : null;
        }
    }

    /// <summary>The A* unit pathing of <c>UnitMovement</c>. Null is Kotlin's <c>settings.useAStarPathfinding = false</c> (the default).</summary>
    public Map.MapUnits.IUnitPathingMapFactory? UnitPathing { get; set; }

    /// <summary>The A* attack pathing of <c>City.getLandAttackPath</c> / <c>getAmphibiousAttackPath</c>. Null is Kotlin's <c>settings.useAStarPathfinding = false</c> (the default).</summary>
    public Automation.Civilization.ICityAttackPathing? AttackPathing { get; set; }

    /// <summary>The A* road pathing of <c>City.getRoadPath</c> / <c>getRoadPathToAny</c> Null is Kotlin's <c>settings.useAStarPathfinding = false</c> (the default).</summary>
    public Automation.Unit.ICityRoadPathing? RoadPathing { get; set; }

    /// <summary>Upstream <c>GameInfo.isSimulation()</c>.</summary>
    public bool IsSimulation(int turns) => turns < SimulateUntilTurn;

    public EngineOptions Clone() => new()
    {
        VisibleMap = VisibleMap,
        Supercharged = Supercharged,
        SimulateUntilTurn = SimulateUntilTurn,
        CivIdsInExperimentGroup = new LinkedHashSet<string>(CivIdsInExperimentGroup),
        ShowSettlerScores = ShowSettlerScores,
        SettlerScores = SettlerScores,
        UnitPathing = UnitPathing,
        AttackPathing = AttackPathing,
        RoadPathing = RoadPathing,
    };

    /// <summary>Hashes the rule-affecting flags only (the experiment group as an unordered set).</summary>
    public void HashState(StateHasher hasher)
    {
        hasher.Add("EngineOptions");
        hasher.Add(VisibleMap);
        hasher.Add(Supercharged);
        hasher.Add(SimulateUntilTurn);
        hasher.AddUnorderedSet(CivIdsInExperimentGroup, civId => civId);
        if (UseAStarPathfinding) hasher.Add("UseAStarPathfinding"); // only when on, so the default hash is unchanged
    }
}
