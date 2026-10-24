using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset.Nation;

// core/src/com/unciv/models/ruleset/nation/PersonalityValue.kt
/// <summary>
/// Type of Personality focus.
/// Associated values in <see cref="Personality"/> typically ranges from 0 (no focus) to 10 (double focus). <c>5</c> is neutral.
/// </summary>
public enum PersonalityValue
{
    // Stat focused personalities
    Production,
    Food,
    Gold,
    Science,
    Culture,
    Happiness,
    Faith,
    // Behaviour focused personalities
    /// <summary>Building a military but not nessesarily using it</summary>
    Military,
    /// <summary>How they use units agressively or defensively in wars, or their priority on war related buildings</summary>
    Aggressive,
    /// <summary>Likelihood of declaring war and acceptance of warmongering, a zero means they won't declare war at all</summary>
    DeclareWar,
    /// <summary>Trading frequency, open borders and liberating city-states, less negative diplomacy impact</summary>
    Commerce,
    /// <summary>Likelihood of signing friendship, defensive pact, peace treaty and other diplomatic actions</summary>
    Diplomacy,
    /// <summary>Likelihood to make a long-lasting aliance with another civ and join wars with them</summary>
    Loyal,
    /// <summary>Founding/capturing new cities, opposite of a cultural victory</summary>
    Expansion,
    /// <summary>Eagerness to denounce other civs</summary>
    DenounceWillingness,
}

public static class PersonalityValueExtensions
{
    /// <summary>Kotlin <c>PersonalityValue.description</c>: used for Civilopedia display</summary>
    public static string GetDescription(this PersonalityValue value) => value switch
    {
        PersonalityValue.Production => "Productive",
        PersonalityValue.Food => "Prolific",
        PersonalityValue.Gold => "Greedy",
        PersonalityValue.Science => "Rational",
        PersonalityValue.Culture => "Cultured",
        PersonalityValue.Happiness => "Hedonistic",
        PersonalityValue.Faith => "Religious",
        PersonalityValue.Military => "Militaristic",
        PersonalityValue.Aggressive => "Pushy",
        PersonalityValue.DeclareWar => "Warmonger",
        PersonalityValue.Commerce => "Trader",
        PersonalityValue.Diplomacy => "Negotiator",
        PersonalityValue.Loyal => "Stalwart",
        PersonalityValue.Expansion => "Expansionistic",
        PersonalityValue.DenounceWillingness => "Denouncer",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    /// <summary>Kotlin <c>PersonalityValue[stat]</c>: translate a <see cref="Stat"/> into a <see cref="PersonalityValue"/></summary>
    public static PersonalityValue FromStat(Stat stat) => stat switch
    {
        Stat.Production => PersonalityValue.Production,
        Stat.Food => PersonalityValue.Food,
        Stat.Gold => PersonalityValue.Gold,
        Stat.Science => PersonalityValue.Science,
        Stat.Culture => PersonalityValue.Culture,
        Stat.Happiness => PersonalityValue.Happiness,
        Stat.Faith => PersonalityValue.Faith,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
}
