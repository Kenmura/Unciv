using System.Text.Json.Serialization;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using Unciv.UI.ObjectDescriptions;
using Unciv.UI.Screens.Civilopedia;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/Personality.kt
/// <summary>
/// A Leader Personality as defined in Personalities.json. Biases can be indexed with a <see cref="PersonalityValue"/> as key.
/// </summary>
public class Personality : RulesetObject, ICivilopediaText
{
    public override string MakeLink() => $"Personality/{Name}";
    public string GetIconName() => "";
    public FormattedLine? GetCivilopediaTextHeader() => this.GetCivilopediaTextHeaderImpl();
    public List<FormattedLine> GetCivilopediaTextLines(CivilopediaContext context) => this.GetPersonalityCivilopediaTextLines(context);

    private static readonly Lazy<Personality> NeutralPersonalityLazy = new(() => new Personality { IsNeutralPersonality = true });

    public static Personality NeutralPersonality => NeutralPersonalityLazy.Value;

    public float Production { get; set; } = 5f;
    public float Food { get; set; } = 5f;
    public float Gold { get; set; } = 5f;
    public float Science { get; set; } = 5f;
    public float Culture { get; set; } = 5f;
    public float Happiness { get; set; } = 5f;
    public float Faith { get; set; } = 5f;

    public float Military { get; set; } = 5f;
    public float Aggressive { get; set; } = 5f;
    public float DeclareWar { get; set; } = 5f;
    public float Commerce { get; set; } = 5f;
    public float Diplomacy { get; set; } = 5f;
    public float Loyal { get; set; } = 5f;
    public float Expansion { get; set; } = 5f;
    public float DenounceWillingness { get; set; } = 5f;

    public Dictionary<string, int> Priorities { get; set; } = [];
    public string PreferredVictoryType { get; set; } = Constants.NeutralVictoryType;
    public bool IsNeutralPersonality { get; set; }

    public override UniqueTarget GetUniqueTarget() => UniqueTarget.Personality;

    /// <summary>Scales the value to a more meaningful range, where 10 is 2, and 5 is 1, and 0 is 0</summary>
    public float ScaledFocus(PersonalityValue value) => this[value] / 5;

    /// <summary>Inverse scales the value to a more meaningful range, where 0 is 2, and 5 is 1 and 10 is 0</summary>
    public float InverseScaledFocus(PersonalityValue value) => (10 - this[value]) / 5;

    /// <param name="value">the personality value</param>
    /// <param name="weight">a value between 0 and 1 that determines how much the modifier deviates from 1</param>
    /// <returns>a modifier between 0 and 2 centered around 1 based off of the personality value and the weight given</returns>
    public float ModifierFocus(PersonalityValue value, float weight) => 1f + (ScaledFocus(value) - 1) * weight;

    /// <summary>An inverted version of <see cref="ModifierFocus"/>, a personality value of 0 becomes a 10, 8 becomes a 2, etc.</summary>
    public float InverseModifierFocus(PersonalityValue value, float weight) => 1f + (InverseScaledFocus(value) - 1) * weight;

    /// <summary>Scales the stats based on the personality and the weight given</summary>
    /// <param name="stats">the stats, modified in place</param>
    /// <param name="weight">a positive value that determines how much the personality should impact the stats given</param>
    public Stats ScaleStats(Stats stats, float weight)
    {
        foreach (var stat in Enum.GetValues<Stat>())
            stats[stat] *= ModifierFocus(PersonalityValueExtensions.FromStat(stat), weight);
        return stats;
    }

    [JsonIgnore]
    public float this[PersonalityValue personalityValue]
    {
        get => personalityValue switch
        {
            PersonalityValue.Production => Production,
            PersonalityValue.Food => Food,
            PersonalityValue.Gold => Gold,
            PersonalityValue.Science => Science,
            PersonalityValue.Culture => Culture,
            PersonalityValue.Happiness => Happiness,
            PersonalityValue.Faith => Faith,
            PersonalityValue.Military => Military,
            PersonalityValue.Aggressive => Aggressive,
            PersonalityValue.DeclareWar => DeclareWar,
            PersonalityValue.Commerce => Commerce,
            PersonalityValue.Diplomacy => Diplomacy,
            PersonalityValue.Loyal => Loyal,
            PersonalityValue.Expansion => Expansion,
            PersonalityValue.DenounceWillingness => DenounceWillingness,
            _ => throw new ArgumentOutOfRangeException(nameof(personalityValue)),
        };
        set
        {
            switch (personalityValue)
            {
                case PersonalityValue.Production: Production = value; break;
                case PersonalityValue.Food: Food = value; break;
                case PersonalityValue.Gold: Gold = value; break;
                case PersonalityValue.Science: Science = value; break;
                case PersonalityValue.Culture: Culture = value; break;
                case PersonalityValue.Happiness: Happiness = value; break;
                case PersonalityValue.Faith: Faith = value; break;
                case PersonalityValue.Military: Military = value; break;
                case PersonalityValue.Aggressive: Aggressive = value; break;
                case PersonalityValue.DeclareWar: DeclareWar = value; break;
                case PersonalityValue.Commerce: Commerce = value; break;
                case PersonalityValue.Diplomacy: Diplomacy = value; break;
                case PersonalityValue.Loyal: Loyal = value; break;
                case PersonalityValue.Expansion: Expansion = value; break;
                case PersonalityValue.DenounceWillingness: DenounceWillingness = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(personalityValue));
            }
        }
    }

    /// <summary>Used in Nation Civilopedia UI: Kotlin <c>NationDescriptions.getShortDescription</c></summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (PreferredVictoryType.Length != 0 && PreferredVictoryType != Constants.NeutralVictoryType)
            parts.Add(PreferredVictoryType);
        var all = Enum.GetValues<PersonalityValue>();
        var maxFocus = all.Max(it => this[it]);
        foreach (var focus in all)
            if (this[focus] == maxFocus) parts.Add(focus.GetDescription());
        return string.Join(", ", parts.Distinct().Select(it => $"[{it}]"));
    }
}
