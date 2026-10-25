using Unciv.AI.Data;
using Unciv.AI.GrandStrategy;
using Unciv.AI.GrandStrategy.Scorers;
using Unciv.AI.Homeland;
using Unciv.AI.Tactical;
using Unciv.Logic.Civilization;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using GrandStrategyKind = Unciv.AI.GrandStrategy.GrandStrategy;

namespace Unciv.AI;

/// <summary>
/// T227: the turn-loop wiring of the Civ 5 AI (A001-A008). With <see cref="Models.Metadata.GameParameters.UseCiv5Ai"/> on, an AI civ's unit pass is
/// driven from here: the grand strategy is evaluated, its flavors rank the tactical move queue and the attacks that queue recruits are carried out,
/// then the homeland move queue decides in which order the non-combat and idle units act. What a unit then does is still the ported Kotlin unit automation
/// (workers, exploring, movement towards the enemy, ...), passed in as <c>automate</c>.
/// Nothing is kept between turns: the strategy is re-derived from the civ's state with a state-based seed each turn (no hysteresis, lock or rival
/// guesses; <see cref="GrandStrategyPlayerState"/> is rebuilt), so a saved game, its reload and a lockstep peer all see the same decisions.
/// Operations (<see cref="Operations.OperationManager"/>) keep state between turns and are not wired yet (see the ticket notes).
/// </summary>
public static class Civ5AiDriver
{
    private static readonly KotlinLazy<Data3> Data = new(() => new Data3());

    // Immutable tables, loaded once per process.
    private sealed class Data3
    {
        public readonly GlobalAIDefines Defines = GlobalAIDefines.LoadEmbedded();
        public readonly GrandStrategyFlavors Flavors = GrandStrategyFlavors.LoadEmbedded();
        public readonly IReadOnlyList<TacticalMoveDefinition> TacticalMoves = TacticalMoveDefinition.LoadEmbedded();
    }

    private const AIEdition Edition = AIEdition.BNW;
    private const string OffenseFlavor = "FLAVOR_OFFENSE";
    private const string DefenseFlavor = "FLAVOR_DEFENSE";
    private const string CultureFlavor = "FLAVOR_CULTURE";
    private const string DiplomacyFlavor = "FLAVOR_DIPLOMACY";

    /// <summary>Whether this civ's unit pass is run by the Civ 5 AI: the game option is on and the civ is a (non-barbarian) AI.</summary>
    public static bool IsActive(Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.GameInfo.GameParameters.UseCiv5Ai && civ.IsAI() && !civ.IsBarbarian;
    }

    /// <summary>The civ's grand strategy this turn, from its flavors and the four victory scorers (SPEC.md sections 3 and 4).</summary>
    public static GrandStrategyEvaluationResult EvaluateGrandStrategy(Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var data = Data.Value;
        var personality = new LeaderPersonalityFlavors();
        var conquest = new ConquestScorer(data.Defines, Edition);
        var culture = new CultureScorer(data.Defines, Edition);
        var diplomatic = new DiplomaticScorer(data.Defines, Edition);
        var spaceship = new SpaceshipScorer(data.Defines, Edition);
        int Victory(GrandStrategyKind s) => s switch
        {
            GrandStrategyKind.Conquest => conquest.Score(VictoryScorerInputsFactory.Conquest(civ)),
            GrandStrategyKind.Culture => culture.Score(VictoryScorerInputsFactory.Culture(civ, personality[CultureFlavor])),
            GrandStrategyKind.UnitedNations => diplomatic.Score(VictoryScorerInputsFactory.Diplomatic(civ, personality[DiplomacyFlavor])),
            GrandStrategyKind.Spaceship => spaceship.Score(VictoryScorerInputsFactory.Spaceship(civ)),
            _ => 0,
        };
        var state = new GrandStrategyPlayerState
        {
            PersonalityFlavors = personality,
            MetMajorCivsCount = 1 + civ.GetKnownCivs().Count(it => it.IsMajorCiv() && !it.IsDefeated()),
        };
        var evaluator = new GrandStrategyEvaluator(data.Defines, data.Flavors, Edition);
        return evaluator.Evaluate(state, Victory, civ.State.StateBasedRandom("Civ5AiDriver.grandStrategy"));
    }

    /// <summary>Offense and defense flavors of the leader with the strategy's modifiers applied.</summary>
    public static TacticalFlavors FlavorsFor(GrandStrategyKind strategy)
    {
        var personality = new LeaderPersonalityFlavors();
        return new TacticalFlavors(
            personality.GetModifiedFlavor(OffenseFlavor, strategy, Data.Value.Flavors),
            personality.GetModifiedFlavor(DefenseFlavor, strategy, Data.Value.Flavors));
    }

    /// <summary>Evaluates the grand strategy, then runs the tactical queue: returns the number of attacks made.</summary>
    public static int RunTactical(Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var data = Data.Value;
        var strategy = EvaluateGrandStrategy(civ).ChosenStrategy;
        var queue = new TacticalMoveQueue(data.TacticalMoves, data.Defines, Edition);
        var recruiter = TacticalRecruiter.FromDefines(data.Defines, Edition);
        var entries = queue.Build(FlavorsFor(strategy), civ.State.StateBasedRandom("Civ5AiDriver.tacticalQueue"));
        return TacticalAdapter.DoTurn(civ, recruiter, entries);
    }

    /// <summary>The homeland move queue of this turn, highest priority first (jitter from a state-based seed).</summary>
    public static IReadOnlyList<HomelandQueueEntry> BuildHomelandQueue(Civilization civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var queue = new HomelandMoveQueue(Data.Value.Defines, Edition);
        return queue.Build(civ.State.StateBasedRandom("Civ5AiDriver.homelandQueue"));
    }

    /// <summary>
    /// Which homeland move claims a unit, or null when none of the modelled ones does (the unit then acts in the closing pass).
    /// Only the moves that map onto a unit's role are recognised: spaceship parts, settlers, workers and other civilians,
    /// garrisons in a city and badly hurt military units.
    /// </summary>
    public static HomelandMovePriority? ClaimedBy(MapUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (unit.HasUnique(UniqueType.SpaceshipPart)) return HomelandMovePriority.SpaceshipPart;
        if (unit.IsCivilian() && unit.HasUnique(UniqueType.FoundCity)) return HomelandMovePriority.Settle;
        if (unit.IsCivilian() && !unit.IsGreatPersonOfType("War")) return HomelandMovePriority.Worker;
        if (!unit.IsMilitary()) return null;
        if (unit.Health <= HealHealthLimit) return HomelandMovePriority.Heal;
        return unit.CurrentTile.IsCityCenter() && unit.CurrentTile.GetCity()?.Civ == unit.Civ ? HomelandMovePriority.Garrison : null;
    }

    private const int HealHealthLimit = 50;

    /// <summary>
    /// Runs the homeland queue: for each move in queue order, every unit it claims (in the order given) is passed to <paramref name="automate"/>,
    /// once. Returns the units that were handled, so the caller can run the rest afterwards.
    /// </summary>
    public static HashSet<MapUnit> RunHomeland(Civilization civ, IReadOnlyList<MapUnit> unitsInOrder, Action<MapUnit> automate)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(unitsInOrder);
        ArgumentNullException.ThrowIfNull(automate);
        var handled = new HashSet<MapUnit>();   // membership only, never iterated
        var claims = unitsInOrder.Select(u => (Unit: u, Move: ClaimedBy(u))).ToList();
        HomelandMoveQueue.Run(BuildHomelandQueue(civ), move =>
        {
            foreach (var (unit, claim) in claims)
            {
                if (claim != move || unit.IsDestroyed || unit.Civ != civ || !handled.Add(unit)) continue;
                automate(unit);
            }
        });
        return handled;
    }
}
