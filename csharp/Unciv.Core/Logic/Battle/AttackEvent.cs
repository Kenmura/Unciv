using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Utils;
using TileClass = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/AttackEvent.kt (T082). Purity annotations dropped.
// JSON names are the Kotlin field names (IsPartOfGameInfoSerialization).

public enum AttackResolution { Completed, Withdrawn }

/// <summary>
/// One unit attack and the endpoints each civilization observed when it happened.
/// Knowledge is captured during the attack and never expanded by later exploration.
/// </summary>
public class AttackEvent() : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("turn")]
    public int Turn { get; set; }
    [JsonPropertyName("sourceTile")]
    public HexCoord SourceTile { get; set; } = HexCoord.Zero;
    [JsonPropertyName("targetTile")]
    public HexCoord TargetTile { get; set; } = HexCoord.Zero;
    [JsonPropertyName("civIdsKnowingAttackSource")]
    public JavaHashSet<string> CivIdsKnowingAttackSource { get; set; } = new();
    [JsonPropertyName("civIdsKnowingAttackTarget")]
    public JavaHashSet<string> CivIdsKnowingAttackTarget { get; set; } = new();
    [JsonPropertyName("resolution")]
    public AttackResolution? Resolution { get; set; }
    [JsonPropertyName("attacker")]
    public AttackParticipant? Attacker { get; set; }
    /// <summary>The intended target and units affected by direct combat effects.</summary>
    [JsonPropertyName("targets")]
    public List<AttackParticipant> Targets { get; set; } = [];

    public AttackEvent(MapUnitCombatant attacker, TileClass targetTile) : this()
    {
        var attackingCiv = attacker.GetCivInfo();
        var gameInfo = attackingCiv.GameInfo;
        var sourceTile = attacker.GetTile();
        Turn = gameInfo.Turns;
        SourceTile = sourceTile.Position;
        TargetTile = targetTile.Position;
        Attacker = new AttackParticipant(attacker);

        foreach (var civ in gameInfo.Civilizations)
        {
            // An attacker knows its own origin and intended target, even if the target is unseen.
            if (civ == attackingCiv)
            {
                CivIdsKnowingAttackSource.Add(civ.CivId);
                CivIdsKnowingAttackTarget.Add(civ.CivId);
                continue;
            }
            if (attacker.IsVisibleTo(civ))
                CivIdsKnowingAttackSource.Add(civ.CivId);
            if (civ.ViewableTiles.Contains(targetTile) && targetTile.GetUnits().Any(it => it.IsVisibleTo(civ)))
                CivIdsKnowingAttackTarget.Add(civ.CivId);
        }
    }

    public AttackEvent Clone() => new()
    {
        Turn = Turn,
        SourceTile = SourceTile,
        TargetTile = TargetTile,
        CivIdsKnowingAttackSource = new JavaHashSet<string>(CivIdsKnowingAttackSource),
        CivIdsKnowingAttackTarget = new JavaHashSet<string>(CivIdsKnowingAttackTarget),
        Resolution = Resolution,
        Attacker = Attacker?.Clone(),
        Targets = Targets.Select(it => it.Clone()).ToList(),
    };
}
