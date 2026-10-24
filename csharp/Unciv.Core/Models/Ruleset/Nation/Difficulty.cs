using Unciv.Models.Ruleset.Unique;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/Difficulty.kt
// Not ported yet: makeLink, getSortGroup and getCivilopediaTextLines (Civilopedia text types and Ruleset, T034).
// Kotlin's `override lateinit var name` is the inherited Name here (default empty instead of throwing when unset).
public class Difficulty : RulesetObject
{
    public int BaseHappiness { get; set; }
    public float ExtraHappinessPerLuxury { get; set; }
    public float ResearchCostModifier { get; set; } = 1f;
    public float UnitCostModifier { get; set; } = 1f;
    public int UnitSupplyBase { get; set; } = 5;
    public int UnitSupplyPerCity { get; set; } = 2;
    public float BuildingCostModifier { get; set; } = 1f;
    public float PolicyCostModifier { get; set; } = 1f;
    public float UnhappinessModifier { get; set; } = 1f;
    public float BarbarianBonus { get; set; }
    public int BarbarianSpawnDelay { get; set; }
    public List<string> PlayerBonusStartingUnits { get; set; } = [];

    public string? AiDifficultyLevel { get; set; }
    public float AiCityGrowthModifier { get; set; } = 1f;
    public float AiUnitCostModifier { get; set; } = 1f;
    public float AiBuildingCostModifier { get; set; } = 1f;
    public float AiWonderCostModifier { get; set; } = 1f;
    public float AiBuildingMaintenanceModifier { get; set; } = 1f;
    public float AiUnitMaintenanceModifier { get; set; } = 1f;
    public float AiUnitSupplyModifier { get; set; }
    public List<string> AiFreeTechs { get; set; } = [];
    public List<string> AiMajorCivBonusStartingUnits { get; set; } = [];
    public List<string> AiCityStateBonusStartingUnits { get; set; } = [];
    public float AiUnhappinessModifier { get; set; } = 1f;
    public int TurnBarbariansCanEnterPlayerTiles { get; set; }
    public int ClearBarbarianCampReward { get; set; } = 25;

    /// <summary>Difficulty uniques will be treated as part of GlobalUniques</summary>
    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Difficulty;
}
