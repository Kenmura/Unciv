using Unciv.AI.Data;
using Unciv.Logic.Map;

namespace Unciv.AI.Operations;

/// <summary>The operation kinds of SPEC.md section 8 (CvAIOperation* RTTI names), in spec order.</summary>
public enum OperationType
{
    BasicCityAttack,
    SneakCityAttack,
    QuickSneakCityAttack,
    SmallCityAttack,
    CityStateAttack,
    DestroyBarbarianCamp,
    PillageEnemy,
    FoundCity,
    QuickColonize,
    NotSoQuickColonize,
    MerchantDelegation,
    NavalBombardment,
    NavalSuperiority,
    CityCloseDefense,
    RapidResponse,
    NavalAttack,
    NavalSneakAttack,
    NavalCityStateAttack,
    NukeAttack,
}

/// <summary>What a slot of an army wants: the role decides which candidates may fill it.</summary>
public enum ArmyRole { Melee, Ranged, Support }

/// <summary>A unit as the operations layer sees it: where it is, how healthy, what role it can play.</summary>
public sealed record OperationUnit(int Id, HexCoord Position, int HealthPercent, ArmyRole Role);

/// <summary>Where an operation currently stands.</summary>
public enum OperationState
{
    /// <summary>Slots are still being filled.</summary>
    Recruiting,
    /// <summary>All slots are filled; the army walks to the muster tile.</summary>
    Gathering,
    /// <summary>The army is assembled and walks to the target.</summary>
    Moving,
    /// <summary>The army reached the deploy range of the target; tactical moves take over until the target is gone.</summary>
    AtTarget,
    /// <summary>The target was taken or destroyed after the army arrived.</summary>
    Succeeded,
    /// <summary>The operation was given up (target vanished early, recruiting timed out, army lost).</summary>
    Aborted,
}

/// <summary>An instruction produced by an operation for one of its units this turn.</summary>
public sealed record OperationOrder(int OperationId, int UnitId, HexCoord Destination);

/// <summary>The tunables of SPEC.md section 8 that one operation needs.</summary>
public sealed record OperationTunables(
    int MaxRecruitTurnsDefault,
    int MaxRecruitTurnsEnemyTerritory,
    int PercentHealthForOperation,
    int PercentDangerForForwardMuster,
    int CityAttackDeployRange,
    int BarbarianCampDeployRange,
    int PillageEnemyDeployRange,
    int NavalBombardmentDeployRange)
{
    public static OperationTunables FromDefines(GlobalAIDefines defines, AIEdition edition)
    {
        ArgumentNullException.ThrowIfNull(defines);
        return new OperationTunables(
            defines.GetInt("AI_OPERATIONAL_MAX_RECRUIT_TURNS_DEFAULT", edition),
            defines.GetInt("AI_OPERATIONAL_MAX_RECRUIT_TURNS_ENEMY_TERRITORY", edition),
            defines.GetInt("AI_OPERATIONAL_PERCENT_HEALTH_FOR_OPERATION", edition),
            defines.GetInt("AI_OPERATIONAL_PERCENT_DANGER_FOR_FORWARD_MUSTER", edition),
            defines.GetInt("AI_OPERATIONAL_CITY_ATTACK_DEPLOY_RANGE", edition),
            defines.GetInt("AI_OPERATIONAL_BARBARIAN_CAMP_DEPLOY_RANGE", edition),
            defines.GetInt("AI_OPERATIONAL_PILLAGE_ENEMY_DEPLOY_RANGE", edition),
            defines.GetInt("AI_OPERATIONAL_NAVAL_BOMBARDMENT_DEPLOY_RANGE", edition));
    }

    /// <summary>
    /// How close the army must get before it counts as deployed. City attacks, naval attacks and the rest use the
    /// city-attack range [I]: the spec lists only four ranges, so every kind not named below shares that one.
    /// </summary>
    public int DeployRange(OperationType type) => type switch
    {
        OperationType.DestroyBarbarianCamp => BarbarianCampDeployRange,
        OperationType.PillageEnemy => PillageEnemyDeployRange,
        OperationType.NavalBombardment => NavalBombardmentDeployRange,
        _ => CityAttackDeployRange,
    };
}

/// <summary>
/// One planned multi-unit effort (SPEC.md section 8): recruit an army, muster it, march it to a target.
/// A unit belongs to at most one operation (the recruitment lock); <see cref="Update"/> is called once per turn with
/// the current state of the civilization's units and returns the orders for this turn. Only the recruiting phase has a
/// timeout; the engine side must abort an army that cannot make progress (see <see cref="OperationManager.Abort"/>). Engine-agnostic: the engine
/// supplies unit snapshots and target validity, and carries out the orders.
/// </summary>
public sealed class AIOperation
{
    private const int MusterRadius = 1;

    private readonly OperationTunables tunables;
    private readonly List<ArmyRole> slots;
    private readonly List<int?> filled;

    public AIOperation(int id, OperationType type, HexCoord target, HexCoord muster, IEnumerable<ArmyRole> slots, bool enemyTerritory, OperationTunables tunables)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(tunables);
        this.slots = [.. slots];
        if (this.slots.Count == 0) throw new ArgumentException("An operation needs at least one army slot", nameof(slots));
        filled = [.. this.slots.Select(_ => (int?)null)];
        this.tunables = tunables;
        Id = id;
        Type = type;
        Target = target;
        Muster = muster;
        MaxRecruitTurns = enemyTerritory ? tunables.MaxRecruitTurnsEnemyTerritory : tunables.MaxRecruitTurnsDefault;
    }

    /// <summary>T235: rebuilds an operation from its saved form. A record that does not describe a valid operation (no slots, mismatched slot lists) is rejected.</summary>
    internal static AIOperation Restore(OperationRecord record, OperationTunables tunables)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Filled.Count != record.Slots.Count) throw new ArgumentException("Saved operation has mismatched slot lists", nameof(record));
        var op = new AIOperation(record.Id, record.Type, record.Target, record.Muster, record.Slots, false, tunables)
        {
            MaxRecruitTurns = record.MaxRecruitTurns,
            TurnsRecruiting = record.TurnsRecruiting,
            State = record.State,
            StartTurn = record.StartTurn,
        };
        for (int i = 0; i < record.Filled.Count; i++) op.filled[i] = record.Filled[i];
        return op;
    }

    /// <summary>T235: the saved form.</summary>
    internal OperationRecord ToRecord() => new()
    {
        Id = Id,
        Type = Type,
        Target = Target,
        Muster = Muster,
        Slots = [.. slots],
        Filled = [.. filled],
        MaxRecruitTurns = MaxRecruitTurns,
        TurnsRecruiting = TurnsRecruiting,
        State = State,
        StartTurn = StartTurn,
    };

    public int Id { get; }
    /// <summary>The game turn this operation began (set by the driver; it ages out operations that cannot make progress).</summary>
    public int StartTurn { get; internal set; }
    public OperationType Type { get; }
    public HexCoord Target { get; }
    public HexCoord Muster { get; }
    public int MaxRecruitTurns { get; private set; }
    public int TurnsRecruiting { get; private set; }
    public OperationState State { get; private set; } = OperationState.Recruiting;
    public bool IsFinished => State is OperationState.Succeeded or OperationState.Aborted;

    /// <summary>The unit ids currently held by this operation, in slot order.</summary>
    public IEnumerable<int> UnitIds => filled.OfType<int>();

    /// <summary>
    /// Picks the muster tile: the forward tile when the danger there is at most PERCENT_DANGER_FOR_FORWARD_MUSTER,
    /// otherwise the safe staging tile [I]. Only meaningful before the army has gathered.
    /// </summary>
    public static HexCoord ChooseMuster(HexCoord staging, HexCoord forward, int forwardDangerPercent, OperationTunables tunables)
    {
        ArgumentNullException.ThrowIfNull(tunables);
        return forwardDangerPercent <= tunables.PercentDangerForForwardMuster ? forward : staging;
    }

    /// <summary>
    /// Advances the operation one turn. <paramref name="units"/> are all the civilization's units (a held unit that is
    /// missing is dead); <paramref name="locked"/> holds the ids held by any operation, which are never recruited again;
    /// <paramref name="targetValid"/> says whether the target still exists and is still wanted.
    /// </summary>
    public IReadOnlyList<OperationOrder> Update(IReadOnlyList<OperationUnit> units, ISet<int> locked, bool targetValid)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(locked);
        if (IsFinished) return [];

        if (!targetValid)
        {
            Finish(State == OperationState.AtTarget ? OperationState.Succeeded : OperationState.Aborted, locked);
            return [];
        }

        var byId = new Dictionary<int, OperationUnit>();   // lookup only, never iterated
        foreach (var u in units)
        {
            if (!byId.TryAdd(u.Id, u)) throw new ArgumentException($"Duplicate unit id {u.Id}", nameof(units));
        }
        for (int i = 0; i < filled.Count; i++)
        {
            if (filled[i] is not int uid) continue;
            // A dead unit always leaves its slot; a wounded one leaves while the army is not yet marching.
            bool alive = byId.TryGetValue(uid, out var unit);
            bool wounded = alive && State is OperationState.Recruiting or OperationState.Gathering && unit?.HealthPercent < tunables.PercentHealthForOperation;
            if (!alive || wounded)
            {
                locked.Remove(uid);
                filled[i] = null;
            }
        }

        if (State is OperationState.Gathering && filled.Any(u => u == null))
        {
            State = OperationState.Recruiting;
            TurnsRecruiting = 0;   // the recruit timeout is a budget per recruiting attempt
        }
        if (State is OperationState.Moving or OperationState.AtTarget && !filled.Any(u => u.HasValue))
        {
            Finish(OperationState.Aborted, locked);
            return [];
        }

        if (State == OperationState.Recruiting)
        {
            Recruit(units, locked);
            if (filled.All(u => u.HasValue)) State = OperationState.Gathering;
            else if (++TurnsRecruiting > MaxRecruitTurns)
            {
                Finish(OperationState.Aborted, locked);
                return [];
            }
        }

        if (State == OperationState.Gathering && AllWithin(byId, Muster, MusterRadius)) State = OperationState.Moving;
        if (State == OperationState.Moving && AllWithin(byId, Target, tunables.DeployRange(Type))) State = OperationState.AtTarget;

        return State switch
        {
            OperationState.Gathering => OrdersTo(Muster),
            OperationState.Moving => OrdersTo(Target),
            _ => [],   // Recruiting: units keep doing their normal jobs; AtTarget: tactical moves drive them
        };
    }

    /// <summary>Gives up the operation, releasing every held unit. Engine code goes through <see cref="OperationManager.Abort"/>.</summary>
    internal void Abort(ISet<int> locked)
    {
        ArgumentNullException.ThrowIfNull(locked);
        if (!IsFinished) Finish(OperationState.Aborted, locked);
    }

    private void Recruit(IReadOnlyList<OperationUnit> units, ISet<int> locked)
    {
        for (int i = 0; i < filled.Count; i++)
        {
            if (filled[i].HasValue) continue;
            var pick = units
                .Where(u => u.Role == slots[i] && u.HealthPercent >= tunables.PercentHealthForOperation && !locked.Contains(u.Id))
                .OrderBy(u => HexMath.GetDistance(u.Position, Muster))
                .ThenBy(u => u.Id)
                .FirstOrDefault();
            if (pick == null) continue;
            filled[i] = pick.Id;
            locked.Add(pick.Id);
        }
    }

    // Empty slots only matter while the army is still being assembled; a marching army that lost a unit goes on with the rest.
    private bool AllWithin(Dictionary<int, OperationUnit> byId, HexCoord point, int range) =>
        filled.OfType<int>().All(uid => byId.TryGetValue(uid, out var unit) && HexMath.GetDistance(unit.Position, point) <= range);

    private List<OperationOrder> OrdersTo(HexCoord destination) =>
        [.. UnitIds.Select(id => new OperationOrder(Id, id, destination))];

    private void Finish(OperationState end, ISet<int> locked)
    {
        foreach (int id in UnitIds) locked.Remove(id);
        for (int i = 0; i < filled.Count; i++) filled[i] = null;
        State = end;
    }
}
