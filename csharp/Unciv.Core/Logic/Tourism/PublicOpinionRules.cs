using System.Collections.Immutable;
using Unciv.Logic.WorldCongress;
using Unciv.Models.Ruleset;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Tourism;

// Brave New World (ticket B014, spec id B052, bnw-spec/SPEC.md section 4.6.4 and 4.6.5). Not part of the Kotlin source.
//
// Public opinion of a civ that follows an ideology: the cultural pressure of other civs' ideologies, the unhappiness it causes, the ideology it
// would rather follow. State is derived (nothing is saved): it only reads the ideologies and the influence levels, which change once per turn.
// Deliberate deviations and unresolved spec items (no decompiler output is available, so [K]/[?] items keep the spec value):
// - The World Ideology resolution pressure (step 1, @1024be50) is <see cref="LeagueEffects.IdeologyPressure"/> (B016).
// - Ties between two other ideologies follow the observed comparison order Freedom, Autocracy, Order ([?] in the spec); an ideology outside that list
//   (a mod) comes after them in ruleset order. A strictly larger pressure wins.
// - The D thresholds (below 3, 3 to 4, 5 and more) and the unhappiness table (per city, population divisor) are the spec's hard-coded numbers.
// - The unhappiness uses the total population of all cities (puppets included) and every city counts.

/// <summary>How badly the people want another ideology (spec 4.6.4 step 3).</summary>
public enum PublicOpinionLevel
{
    Content = 0,
    Dissidents = 1,
    CivilResistance = 2,
    RevolutionaryWave = 3,
}

/// <summary>A civ that pressures us: its ideology and the excess of its influence level over ours (always positive).</summary>
public sealed record IdeologyPressure(string Civ, string Ideology, int Amount);

/// <summary>The public opinion of one civ (spec 4.6.4). <see cref="Dissatisfaction"/> is D, <see cref="Preferred"/> the ideology the people favour.</summary>
public sealed record PublicOpinion(PublicOpinionLevel Level, int Dissatisfaction, string? Preferred, int Unhappiness, IReadOnlyList<IdeologyPressure> Pressures)
{
    public static PublicOpinion Content { get; } = new(PublicOpinionLevel.Content, 0, null, 0, []);
}

public static class PublicOpinionRules
{
    /// <summary>The comparison order of ties between two other ideologies (spec 4.6.4 step 3 [?]).</summary>
    private static readonly ImmutableArray<string> TieOrder = ["Freedom", "Autocracy", "Order"];

    /// <summary>The civ's public opinion: <see cref="PublicOpinion.Content"/> unless it follows an ideology in a ruleset with tourism and ideologies.</summary>
    public static PublicOpinion Evaluate(CivilizationObject civ)
    {
        var mine = civ.Policies.Ideology;
        if (mine == null || !TourismRules.IsEnabled(civ) || !civ.IsMajorCiv() || !civ.IsAlive()) return PublicOpinion.Content;

        var ideologies = Ordered(civ.Policies.IdeologyBranches);
        var pressure = ideologies.ToDictionary(it => it.Name, it => LeagueEffects.IdeologyPressure(civ, it.Name));
        var pressures = new List<IdeologyPressure>();
        foreach (var other in civ.GameInfo.Civilizations)
        {
            var theirs = other.Policies.Ideology;
            if (other == civ || theirs == null || !other.IsMajorCiv() || !other.IsAlive() || !pressure.ContainsKey(theirs)) continue;
            int d = (int)InfluenceRules.Level(other, civ) - (int)InfluenceRules.Level(civ, other);
            if (d <= 0) continue;
            pressure[theirs] = unchecked(pressure[theirs] + d);
            pressures.Add(new IdeologyPressure(other.CivId, theirs, d));
        }

        int own = pressure.GetValueOrDefault(mine);
        int others = 0;
        foreach (var (name, amount) in pressure) if (name != mine) others = unchecked(others + amount);
        if (others <= own) return PublicOpinion.Content with { Pressures = pressures };

        string? preferred = null;
        foreach (var branch in ideologies)
        {
            if (branch.Name == mine) continue;
            if (preferred == null || pressure[branch.Name] > pressure[preferred]) preferred = branch.Name;
        }
        int dissatisfaction = others - own;
        var level = dissatisfaction < 3 ? PublicOpinionLevel.Dissidents : dissatisfaction < 5 ? PublicOpinionLevel.CivilResistance : PublicOpinionLevel.RevolutionaryWave;
        return new PublicOpinion(level, dissatisfaction, preferred, OpinionUnhappiness(civ, level), pressures);
    }

    /// <summary>Opinion unhappiness (@1006c5d0): <c>max(totalPopulation / popDiv, cities * perCity)</c> with (1, 10), (2, 5), (4, 3) for the three levels; 0 when content.</summary>
    public static int OpinionUnhappiness(CivilizationObject civ, PublicOpinionLevel level)
    {
        var (perCity, popDiv) = level switch
        {
            PublicOpinionLevel.Dissidents => (1, 10),
            PublicOpinionLevel.CivilResistance => (2, 5),
            PublicOpinionLevel.RevolutionaryWave => (4, 3),
            _ => (0, 1),
        };
        if (perCity == 0) return 0;
        int population = 0;
        foreach (var city in civ.Cities) population = unchecked(population + city.Population.Population);
        return Math.Max(population / popDiv, unchecked(civ.Cities.Count * perCity));
    }

    /// <summary>The unhappiness added to the happiness breakdown (spec 4.6.4 step 4), 0 without an ideology.</summary>
    public static int Unhappiness(CivilizationObject civ) => civ.Policies.Ideology == null ? 0 : Evaluate(civ).Unhappiness;

    private static List<PolicyBranch> Ordered(IEnumerable<PolicyBranch> branches)
    {
        var all = branches.ToList();
        var known = TieOrder.SelectMany(name => all.Where(it => it.Name == name));
        return known.Concat(all.Where(it => !TieOrder.Contains(it.Name))).ToList();
    }
}
