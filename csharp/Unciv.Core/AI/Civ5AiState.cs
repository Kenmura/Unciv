using System.Text.Json.Serialization;
using Unciv.AI.Operations;
using Unciv.Json;
using Unciv.Logic.Map;
using GrandStrategyKind = Unciv.AI.GrandStrategy.GrandStrategy;

namespace Unciv.AI;

/// <summary>
/// T235: what the Civ 5 AI remembers between turns, saved with the civ (<c>Civilization.Civ5Ai</c>): the grand strategy with its 10-turn lock
/// counter (hysteresis) and the running operations. Plain data, so a reload, a clone and a lockstep peer all continue identically.
/// </summary>
public sealed class Civ5AiState : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("strategy")]
    public GrandStrategyKind CurrentStrategy { get; set; } = GrandStrategyKind.None;

    [JsonPropertyName("turnsActive")]
    public int TurnsActive { get; set; }

    [JsonPropertyName("nextOperationId")]
    public int NextOperationId { get; set; } = 1;

    [JsonPropertyName("operations")]
    public List<OperationRecord> Operations { get; set; } = [];

    /// <summary>Targets whose army could not find a way there; not tried again until the given turn.</summary>
    [JsonPropertyName("blockedTargets")]
    public List<BlockedTarget> BlockedTargets { get; set; } = [];

    /// <summary>
    /// T237: tolerant of a freshly deserialized state, whose lists and entries may be null (a save is untrusted; the sanitizer only runs on the next AI turn).
    /// Null lists clone as empty and null entries are skipped, which is what the sanitizer would make of them anyway.
    /// </summary>
    public Civ5AiState Clone() => new()
    {
        BlockedTargets = [.. (BlockedTargets ?? []).Where(it => it != null).Select(it => new BlockedTarget { Position = it.Position, UntilTurn = it.UntilTurn })],
        CurrentStrategy = CurrentStrategy,
        TurnsActive = TurnsActive,
        NextOperationId = NextOperationId,
        Operations = [.. (Operations ?? []).Where(it => it != null).Select(it => it.Clone())],
    };
}

/// <summary>One running operation as saved: everything <see cref="AIOperation"/> keeps between turns, plus the turn it began (for the driver's age limit).</summary>
public sealed class OperationRecord
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public OperationType Type { get; set; }

    [JsonPropertyName("target")]
    public HexCoord Target { get; set; }

    [JsonPropertyName("muster")]
    public HexCoord Muster { get; set; }

    [JsonPropertyName("slots")]
    public List<ArmyRole> Slots { get; set; } = [];

    /// <summary>The unit id in each slot, or null while the slot is empty.</summary>
    [JsonPropertyName("filled")]
    public List<int?> Filled { get; set; } = [];

    [JsonPropertyName("maxRecruitTurns")]
    public int MaxRecruitTurns { get; set; }

    [JsonPropertyName("turnsRecruiting")]
    public int TurnsRecruiting { get; set; }

    [JsonPropertyName("state")]
    public OperationState State { get; set; }

    [JsonPropertyName("startTurn")]
    public int StartTurn { get; set; }

    public OperationRecord Clone() => new()
    {
        Id = Id,
        Type = Type,
        Target = Target,
        Muster = Muster,
        Slots = [.. Slots ?? []],
        Filled = [.. Filled ?? []],
        MaxRecruitTurns = MaxRecruitTurns,
        TurnsRecruiting = TurnsRecruiting,
        State = State,
        StartTurn = StartTurn,
    };
}

/// <summary>An operation target that was found unreachable, skipped until <see cref="UntilTurn"/>.</summary>
public sealed class BlockedTarget
{
    [JsonPropertyName("position")]
    public HexCoord Position { get; set; }

    [JsonPropertyName("untilTurn")]
    public int UntilTurn { get; set; }
}
