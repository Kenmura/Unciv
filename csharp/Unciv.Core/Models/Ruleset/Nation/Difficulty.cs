using Unciv.Models.Ruleset.Unique;
using Unciv.UI.Components;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/Difficulty.kt
// Kotlin's `override lateinit var name` is the inherited Name here (default empty instead of throwing when unset).
public class Difficulty : RulesetObject, ICivilopediaText
{
    public override string MakeLink() => $"Difficulty/{Name}";

    public int GetSortGroup(Ruleset ruleset) => ruleset.Difficulties.Keys.ToList().IndexOf(Name);

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) =>
        FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            static string Percent(float value) => KotlinNumberText.Int(KotlinNumberText.FloatToInt(value * 100));
            static string I(int value) => KotlinNumberText.Int(value);

            b.Add("Player settings", header: 3, indent: 0);
            b.Add($"{{Base happiness}}: {I(BaseHappiness)} {Fonts.Happiness}");
            b.Add($"{{Extra happiness per luxury}}: {I(KotlinNumberText.FloatToInt(ExtraHappinessPerLuxury))} {Fonts.Happiness}");
            b.Add($"{{Research cost modifier}}: {Percent(ResearchCostModifier)}% {Fonts.Science}");
            b.Add($"{{Unit cost modifier}}: {Percent(UnitCostModifier)}% {Fonts.Production}");
            b.Add($"{{Building cost modifier}}: {Percent(BuildingCostModifier)}% {Fonts.Production}");
            b.Add($"{{Policy cost modifier}}: {Percent(PolicyCostModifier)}% {Fonts.Culture}");
            b.Add($"{{Unhappiness modifier}}: {Percent(UnhappinessModifier)}%");
            b.Add($"{{Bonus vs. Barbarians}}: {Percent(BarbarianBonus)}% {Fonts.Strength}");
            b.Add($"{{Barbarian spawning delay}}: {I(BarbarianSpawnDelay)}");

            void AddUnitCounts(List<string> units)
            {
                // groupBy keeps first-occurrence order of the keys
                foreach (var group in units.GroupBy(it => it))
                {
                    // Through a virtual Unique was the simplest way to prevent white icons showing for stuff like eraSpecificUnit
                    var count = group.Count();
                    b.Add(new UniqueObject(count == 1 ? $"[{group.Key}]" : $"{I(count)} [{group.Key}]"), indent: 2);
                }
            }

            if (PlayerBonusStartingUnits.Count != 0)
            {
                b.Space();
                b.Add("{Bonus starting units}:");
                AddUnitCounts(PlayerBonusStartingUnits);
            }

            b.Space();
            b.Add("AI settings", header: 3, indent: 0);
            b.Add($"{{AI difficulty level}}: {{{AiDifficultyLevel ?? "null"}}}");
            b.Add($"{{AI city growth modifier}}: {Percent(AiCityGrowthModifier)}% {Fonts.Food}");
            b.Add($"{{AI unit cost modifier}}: {Percent(AiUnitCostModifier)}% {Fonts.Production}");
            b.Add($"{{AI building cost modifier}}: {Percent(AiBuildingCostModifier)}% {Fonts.Production}");
            b.Add($"{{AI wonder cost modifier}}: {Percent(AiWonderCostModifier)}% {Fonts.Production}");
            b.Add($"{{AI building maintenance modifier}}: {Percent(AiBuildingMaintenanceModifier)}% {Fonts.Gold}");
            b.Add($"{{AI unit maintenance modifier}}: {Percent(AiUnitMaintenanceModifier)}% {Fonts.Gold}");
            b.Add($"{{AI unhappiness modifier}}: {Percent(AiUnhappinessModifier)}%");

            if (AiFreeTechs.Count != 0)
            {
                b.Space();
                b.Add("{AI free techs}:");
                foreach (var tech in AiFreeTechs)
                    b.Add(tech, link: $"Technology/{tech}", indent: 2);
            }
            if (AiMajorCivBonusStartingUnits.Count != 0)
            {
                b.Space();
                b.Add("{Major AI civilization bonus starting units}:");
                AddUnitCounts(AiMajorCivBonusStartingUnits);
            }
            if (AiCityStateBonusStartingUnits.Count != 0)
            {
                b.Space();
                b.Add("{City state bonus starting units}:");
                AddUnitCounts(AiCityStateBonusStartingUnits);
            }

            b.Defaults(new FormattedLine());
            b.Space();
            b.Add($"{{Turns until barbarians enter player tiles}}: {I(TurnBarbariansCanEnterPlayerTiles)} {Fonts.Turn}");
            b.Add($"{{Gold reward for clearing barbarian camps}}: {I(ClearBarbarianCampReward)} {Fonts.Gold}");

            b.AddUniques(this);
        }, defaults: new FormattedLine(indent: 1));

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

    /// <summary>B023 (bnw-spec 4.12, HandicapInfos.AIFreeXP): flat XP each military unit an AI major civ constructs starts with.</summary>
    public int AiFreeXp { get; set; }

    /// <summary>B023 (bnw-spec 4.12, HandicapInfos.AIFreeXPPercent): extra percent on the combat XP an AI major civ's units earn.</summary>
    public int AiFreeXpPercent { get; set; }
    public List<string> AiMajorCivBonusStartingUnits { get; set; } = [];
    public List<string> AiCityStateBonusStartingUnits { get; set; } = [];
    public float AiUnhappinessModifier { get; set; } = 1f;
    public int TurnBarbariansCanEnterPlayerTiles { get; set; }
    public int ClearBarbarianCampReward { get; set; } = 25;

    /// <summary>Difficulty uniques will be treated as part of GlobalUniques</summary>
    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Difficulty;
}
