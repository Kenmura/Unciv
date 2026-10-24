using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Logic.Battle;

// Ported from core/src/com/unciv/logic/battle/AttackParticipant.kt (T082). Purity annotations dropped.
// JSON names are the Kotlin field names (IsPartOfGameInfoSerialization).

public enum AttackParticipantOutcome
{
    Survived, Destroyed, Captured, Withdrew
}

/// <summary>A participant's identity at attack time, independent of later movement, renaming or capture.</summary>
public class AttackParticipant() : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("unitID")]
    public int? UnitID { get; set; }
    [JsonPropertyName("civID")]
    public string CivID { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("instanceName")]
    public string? InstanceName { get; set; }
    [JsonPropertyName("position")]
    public HexCoord Position { get; set; } = HexCoord.Zero;
    [JsonPropertyName("healthBefore")]
    public int HealthBefore { get; set; }
    [JsonPropertyName("healthAfter")]
    public int? HealthAfter { get; set; }
    /// <summary>
    /// Actual HP loss recorded by direct combat effects; indirect effect chains are excluded.
    /// Destruction or capture without HP loss is represented by <see cref="Outcome"/>.
    /// </summary>
    [JsonPropertyName("damageReceived")]
    public int DamageReceived { get; set; }
    [JsonPropertyName("outcome")]
    public AttackParticipantOutcome? Outcome { get; set; }
    /// <summary>
    /// Civilizations that identified this particular participant when the attack happened.
    /// A combat report may disclose its unit type without identifying the unit, owner or origin.
    /// </summary>
    [JsonPropertyName("civIdsThatKnowMe")]
    public JavaHashSet<string> CivIdsThatKnowMe { get; set; } = new();

    public AttackParticipant(MapUnitCombatant combatant) : this()
    {
        var owner = combatant.GetCivInfo();
        CivID = owner.CivId;
        Name = combatant.GetName();
        Position = combatant.GetTile().Position;
        HealthBefore = combatant.GetHealth();
        UnitID = combatant.Unit.Id;
        InstanceName = combatant.Unit.InstanceName;
        foreach (var civ in owner.GameInfo.Civilizations)
        {
            if (combatant.IsVisibleTo(civ))
                CivIdsThatKnowMe.Add(civ.CivId);
        }
    }

    public AttackParticipant Clone() => new()
    {
        UnitID = UnitID,
        CivID = CivID,
        Name = Name,
        InstanceName = InstanceName,
        Position = Position,
        HealthBefore = HealthBefore,
        HealthAfter = HealthAfter,
        DamageReceived = DamageReceived,
        Outcome = Outcome,
        CivIdsThatKnowMe = new JavaHashSet<string>(CivIdsThatKnowMe),
    };
}
