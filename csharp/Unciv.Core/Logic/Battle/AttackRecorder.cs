using Unciv.Logic.Civilization;
using Unciv.Utils;
using MapUnitClass = Unciv.Logic.Map.MapUnits.MapUnit;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/AttackRecorder.kt (T082).

/// <summary>
/// Builds a detached history record for combat between units. The caller explicitly <see cref="Finish"/>es
/// the record; recording never stores history or publishes notifications as a side effect.
/// Identity and visibility are captured before effects; final HP and outcomes are written once.
/// Callers report direct effects explicitly. This object does not execute or intercept unit mutations.
///
/// Never store this temporary object in game state, cached GameContexts, Views or deferred actions.
/// Its operations are engine-only; raw records must not be exposed through user-facing APIs.
/// </summary>
public sealed class AttackRecorder
{
    private IGameInfoState? gameInfo;
    private AttackEvent? @event;

    private sealed class ParticipantState(MapUnitClass unit, AttackParticipant record)
    {
        public MapUnitClass Unit { get; set; } = unit;
        public AttackParticipant Record { get; } = record;
        public AttackParticipantOutcome? ExplicitOutcome { get; set; }
    }

    private readonly LinkedHashMap<int, ParticipantState> participants = new();

    internal AttackRecorder(MapUnitCombatant attacker, TileClass targetTile)
    {
        gameInfo = attacker.GetCivInfo().GameInfo;
        if (!ReferenceEquals(targetTile.TileMap.GameInfo, gameInfo))
            throw new ArgumentException("An attack cannot target another game instance");
        var attack = new AttackEvent(attacker, targetTile);
        @event = attack;
        participants.Put(attacker.Unit.Id, new ParticipantState(attacker.Unit, attack.Attacker.NotNull()));
    }

    /// <summary>Validate once at the recorder boundary, before a unit mutation can take place.</summary>
    private AttackEvent Validate(IGameInfoState gameInfo)
    {
        var attack = CurrentEvent();
        if (!ReferenceEquals(this.gameInfo, gameInfo))
            throw new ArgumentException("An attack cannot record effects from another game instance");
        return attack;
    }

    /// <summary>Preserve the intended target even when the attack deals no damage.</summary>
    internal void SnapshotTarget(MapUnitCombatant combatant)
    {
        var attack = Validate(combatant.GetCivInfo().GameInfo);
        GetOrCreateParticipantState(combatant.Unit, attack);
    }

    /// <summary>Capture identity and initial health before healing or another direct effect changes a unit.</summary>
    internal void MarkUnitAffected(MapUnitClass unit)
    {
        var attack = Validate(unit.Civ.GameInfo);
        GetOrCreateParticipantState(unit, attack);
    }

    /// <summary>Capture and withdrawal must be reported explicitly when ordinary survival cannot describe them.</summary>
    internal void RecordOutcome(MapUnitCombatant combatant, AttackParticipantOutcome? outcome)
    {
        var attack = Validate(combatant.GetCivInfo().GameInfo);
        if (!(outcome == AttackParticipantOutcome.Captured || outcome == AttackParticipantOutcome.Withdrew))
            throw new ArgumentException("Failed requirement.");
        GetOrCreateParticipantState(combatant.Unit, attack).ExplicitOutcome = outcome;
    }

    /// <summary>Replacement can destroy an old unit object; survival is resolved by stable ID at finish.</summary>
    internal void RecordDestruction(MapUnitClass unit) => MarkUnitAffected(unit);

    /// <summary>Report clamped HP loss before applying it; healing never subtracts from accumulated damage.</summary>
    internal void RecordDamage(MapUnitClass unit, int actualDamage)
    {
        var attack = Validate(unit.Civ.GameInfo);
        if (actualDamage <= 0) return;
        var state = GetOrCreateParticipantState(unit, attack);
        state.Record.DamageReceived = unchecked(state.Record.DamageReceived + actualDamage);
    }

    private ParticipantState GetOrCreateParticipantState(MapUnitClass unit, AttackEvent attack)
    {
        if (!participants.TryGetValue(unit.Id, out var state))
        {
            var record = new AttackParticipant(new MapUnitCombatant(unit));
            attack.Targets.Add(record);
            state = new ParticipantState(unit, record);
            participants.Put(unit.Id, state);
        }
        state.Unit = unit;
        return state;
    }

    private AttackEvent CurrentEvent() => @event ?? throw new InvalidOperationException("This attack has already finished");

    /// <summary>Finalize exactly once. Storage and any later delivery are the caller's responsibility.</summary>
    internal AttackEvent Finish(AttackResolution? resolution)
    {
        if (resolution == null) throw new ArgumentException("Use finishIncomplete for a failed attack");
        return FinishRecording(resolution);
    }

    /// <summary>Preserve partial history on an explicit exception path; combat mutations are not rolled back.</summary>
    internal AttackEvent FinishIncomplete() => FinishRecording(null);

    private AttackEvent FinishRecording(AttackResolution? resolution)
    {
        var attack = CurrentEvent();
        try
        {
            foreach (var state in participants.Values) FinishParticipant(state);
            attack.Resolution = resolution;
            return attack;
        }
        finally
        {
            participants.Clear();
            @event = null;
            gameInfo = null;
        }
    }

    private static void FinishParticipant(ParticipantState state)
    {
        var unit = state.Unit;
        var record = state.Record;
        var survivingUnit = record.UnitID is { } id ? unit.Civ.Units.GetUnitById(id) : null;
        bool captured = unit.Civ.CivId != record.CivID && survivingUnit != null;
        record.Outcome = state.ExplicitOutcome ?? (captured ? AttackParticipantOutcome.Captured
            : survivingUnit == null ? AttackParticipantOutcome.Destroyed
            : AttackParticipantOutcome.Survived);
        record.HealthAfter = survivingUnit?.Health ?? unit.Health;
    }
}
