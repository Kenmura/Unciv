using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Speed.kt
// Not ported yet: the Civilopedia members (getCivilopediaTextLines, getSortGroup...), they need Ruleset (T034) and the Civilopedia text types.
public class Speed : RulesetObject, IGameContextSpeed
{
    public const string Default = "Quick";
    public const string DefaultForSimulation = "Standard";

    /// <summary>One <c>turns</c> row after parsing: <c>yearInterval</c> years per turn until turn <c>untilTurn</c>.</summary>
    public readonly record struct YearsPerTurnRow(float YearInterval, int UntilTurn);

    private readonly Lazy<List<YearsPerTurnRow>> yearsPerTurn;
    private readonly Lazy<Dictionary<Stat, float>> statCostModifiers;

    public Speed()
    {
        // Kotlin default values refer to `modifier` at construction time; a later json assignment to `modifier` does not propagate
        GoldCostModifier = Modifier;
        ProductionCostModifier = Modifier;
        ScienceCostModifier = Modifier;
        CultureCostModifier = Modifier;
        FaithCostModifier = Modifier;
        GoldGiftModifier = Modifier;
        BarbarianModifier = Modifier;
        ImprovementBuildLengthModifier = Modifier;
        GoldenAgeLengthModifier = Modifier;
        yearsPerTurn = new Lazy<List<YearsPerTurnRow>>(() =>
            Turns.Select(row => new YearsPerTurnRow(row["yearsPerTurn"], (int)row["untilTurn"])).ToList());
        statCostModifiers = new Lazy<Dictionary<Stat, float>>(() =>
        {
            var map = new Dictionary<Stat, float>();
            foreach (var stat in Enum.GetValues<Stat>())
            {
                map[stat] = stat switch
                {
                    Stat.Production => ProductionCostModifier,
                    Stat.Gold => GoldCostModifier,
                    Stat.Science => ScienceCostModifier,
                    Stat.Faith => FaithCostModifier,
                    Stat.Culture => CultureCostModifier,
                    _ => 1f,
                };
            }
            return map;
        });
    }

    public float Modifier { get; set; } = 1f;
    public float GoldCostModifier { get; set; }
    public float ProductionCostModifier { get; set; }
    public float ScienceCostModifier { get; set; }
    public float CultureCostModifier { get; set; }
    public float FaithCostModifier { get; set; }
    public float GoldGiftModifier { get; set; }
    public float CityStateTributeScalingInterval { get; set; } = 6.5f;
    public float BarbarianModifier { get; set; }
    public float ImprovementBuildLengthModifier { get; set; }
    public float GoldenAgeLengthModifier { get; set; }
    public int ReligiousPressureAdjacentCity { get; set; } = 6;
    public int PeaceDealDuration { get; set; } = 10;
    public int DealDuration { get; set; } = 30;
    public float StartYear { get; set; } = -4000f;
    public List<Dictionary<string, float>> Turns { get; set; } = [];

    /// <summary>These could be private but for RulesetValidator checking it</summary>
    [JsonIgnore]
    public List<YearsPerTurnRow> YearsPerTurn => yearsPerTurn.Value;

    [JsonIgnore]
    public Dictionary<Stat, float> StatCostModifiers => statCostModifiers.Value;

    public float GetStatCostModifier(Stat stat) => StatCostModifiers[stat];

    /// <summary>End of defined turn range, used for starting Era's <c>startPercent</c> calculation</summary>
    public int NumTotalTurns() => YearsPerTurn.Last().UntilTurn;

    /// <summary>
    /// Calculate a Year from a turn number.
    ///
    /// Note that years can have fractional parts and the integer part of the year for two consecutive turns _can_ be equal.
    /// </summary>
    /// <param name="turn">The logical turn number, any offset from starting in an advanced Era already added in</param>
    public float TurnToYear(int turn)
    {
        float year = StartYear;
        int intervalStartTurn = 0;
        int lastIntervalEndTurn = NumTotalTurns();
        foreach (var (turnLength, intervalEndTurn) in YearsPerTurn)
        {
            if (intervalStartTurn >= turn) break; // ensure year isn't projected backwards for negative `turn`
            if (turn <= intervalEndTurn || intervalEndTurn == lastIntervalEndTurn)
            {
                // We can interpolate linearly within this interval and are done
                year += (turn - intervalStartTurn) * turnLength;
                break;
            }
            // Accumulate total length in years of this interval and move on to the following intervals.
            year += (intervalEndTurn - intervalStartTurn) * turnLength;
            intervalStartTurn = intervalEndTurn;
        }
        return year;
    }

    // Note: Speed uniques will be treated as part of GlobalUniques
    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Speed;

    public string MakeLink() => $"Speed/{Name}";
}
