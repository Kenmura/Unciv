using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.Models.Translations;
using Unciv.UI.Components;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;
using Unciv.Utils;

namespace Unciv.Models.Ruleset;

// core/src/com/unciv/models/ruleset/Speed.kt
public class Speed : RulesetObject, IGameContextSpeed, ICivilopediaText
{
    public const string Default = "Quick";
    public const string DefaultForSimulation = "Standard";

    /// <summary>One <c>turns</c> row after parsing: <c>yearInterval</c> years per turn until turn <c>untilTurn</c>.</summary>
    public readonly record struct YearsPerTurnRow(float YearInterval, int UntilTurn);

    private readonly KotlinLazy<List<YearsPerTurnRow>> yearsPerTurn;
    private readonly KotlinLazy<Dictionary<Stat, float>> statCostModifiers;

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
        yearsPerTurn = new KotlinLazy<List<YearsPerTurnRow>>(() =>
            Turns.Select(row => new YearsPerTurnRow(row["yearsPerTurn"], (int)row["untilTurn"])).ToList());
        statCostModifiers = new KotlinLazy<Dictionary<Stat, float>>(() =>
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

    public override string MakeLink() => $"Speed/{Name}";

    public FormattedLine? GetCivilopediaTextHeader() => new(Name, header: 2);

    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) =>
        FormattedLineListBuilder.BuildCivilopediaText(context, b =>
        {
            static string F(float value) => KotlinNumberText.FloatToString(value);
            b.Add($"General speed modifier: [{F(Modifier * 100)}]%{Fonts.Turn}");
            b.Add($"Production cost modifier: [{F(ProductionCostModifier * 100)}]%{Fonts.Production}");
            b.Add($"Gold cost modifier: [{F(GoldCostModifier * 100)}]%{Fonts.Gold}");
            b.Add($"Science cost modifier: [{F(ScienceCostModifier * 100)}]%{Fonts.Science}");
            b.Add($"Culture cost modifier: [{F(CultureCostModifier * 100)}]%{Fonts.Culture}");
            b.Add($"Faith cost modifier: [{F(FaithCostModifier * 100)}]%{Fonts.Faith}");
            b.Add($"Improvement build length modifier: [{F(ImprovementBuildLengthModifier * 100)}]%{Fonts.Turn}");
            b.Add($"Diplomatic deal duration: [{KotlinNumberText.Int(DealDuration)}] turns{Fonts.Turn}");
            b.Add($"Gold gift influence gain modifier: [{F(GoldGiftModifier * 100)}]%{Fonts.Gold}");
            b.Add($"City-state tribute scaling interval: [{F(CityStateTributeScalingInterval)}] turns{Fonts.Turn}");
            b.Add($"Barbarian spawn modifier: [{F(BarbarianModifier * 100)}]%{Fonts.Strength}");
            b.Add($"Golden age length modifier: [{F(GoldenAgeLengthModifier * 100)}]%{Fonts.Happiness}");
            b.Add($"Adjacent city religious pressure: [{KotlinNumberText.Int(ReligiousPressureAdjacentCity)}]{Fonts.Faith}");
            b.Add($"Peace deal duration: [{KotlinNumberText.Int(PeaceDealDuration)}] turns{Fonts.Turn}");
            b.Add("Start year: [" + $"{{[{KotlinNumberText.Int(KotlinNumberText.FloatToInt(Math.Abs(StartYear)))}] {(StartYear < 0 ? "BC" : "AD")}}}]".Tr());
            b.AddUniques(this);
        });

    public int GetSortGroup(Ruleset ruleset) => KotlinNumberText.FloatToInt(Modifier * 1000);
}
