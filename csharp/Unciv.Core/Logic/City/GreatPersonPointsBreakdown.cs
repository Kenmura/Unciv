using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.WorldCongress;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Logic.City;

// Ported from core/src/com/unciv/logic/city/GreatPersonPointsBreakdown.kt (T062). Purity annotations are stripped.

/// <summary>Manages calculating Great Person Points per City for nextTurn. See public constructor(city) below for details.</summary>
public class GreatPersonPointsBreakdown
{
    // ruleset kept as class field for reuse in sum(), for the "Remove all gpp values that are not valid units" step.
    // I am unsure why that existed before this class was written, what the UX for invalid mods should be.
    // Refactoring through allNames so that ruleset is only needed in init should be easy if all UI should behave as if any invalid definition did not exist at all.
    // As is, the "lost" points can still display.
    private readonly Ruleset ruleset;

    /// <summary>Return type component of the <see cref="GetPercentagesApplyingToAllGP"/> helper</summary>
    private sealed class AllGPPercentageEntry(string source, string? pediaLink, int bonus)
    {
        public string Source { get; } = source;
        public string? PediaLink { get; } = pediaLink;
        public int Bonus { get; } = bonus;
    }

    /// <summary>Represents any source of Great Person Points or GPP percentage bonuses</summary>
    public class Entry(string source, string? pediaLink = null, bool isAllGP = false, Counter<string>? counter = null)
    {
        /// <summary>Simple label for the source of these points</summary>
        public string Source { get; } = source;
        /// <summary>In case we want to show the breakdown with decorations and/or Civilopedia linking</summary>
        public string? PediaLink { get; } = pediaLink;
        /// <summary>For display only - this entry affects all Great Persons and can be displayed as simple percentage without listing all GP keys</summary>
        public bool IsAllGP { get; } = isAllGP;
        /// <summary>Reference to the points, <b>do not mutate</b></summary>
        public Counter<string> Counter { get; } = counter ?? new Counter<string>();
    }

    /// <summary>Using fixed-point(n.3) math in sum() to avoid surprises by rounding while still leveraging the Counter class</summary>
    public const int FixedPointFactor = 1000;

    private static string GetUniqueSourceName(UniqueObject unique) => unique.SourceObjectName ?? "Bonus";

    private static string? GuessPediaLink(UniqueObject unique)
    {
        if (unique.SourceObjectName == null) return null;
        return unique.SourceObjectType.NotNull() + "/" + unique.SourceObjectName;
    }

    /// <summary>
    /// List all percentage bonuses that apply to all GPP.
    /// This is used internally from the public constructor to include them in the brakdown,
    /// and exposed to autoAssignPopulation via <see cref="GetGreatPersonPercentageBonus"/>
    /// </summary>
    private static IEnumerable<AllGPPercentageEntry> GetPercentagesApplyingToAllGP(City city)
    {
        // Now add boni for GreatPersonPointPercentage
        foreach (var unique in city.GetMatchingUniques(UniqueType.GreatPersonPointPercentage))
        {
            if (!city.MatchesFilter(unique.Params[1])) continue;
            yield return new AllGPPercentageEntry(GetUniqueSourceName(unique), GuessPediaLink(unique), unique.Params[0].ToInt());
        }

        // Now add boni for GreatPersonBoostWithFriendship (Sweden UP)
        var civ = city.Civ;
        foreach (var otherCiv in civ.GetKnownCivs())
        {
            if (!civ.GetDiplomacyManager(otherCiv).NotNull().HasFlag(DiplomacyFlags.DeclarationOfFriendship))
                continue;
            var boostUniques = civ.GetMatchingUniques(UniqueType.GreatPersonBoostWithFriendship)
                .Concat(otherCiv.GetMatchingUniques(UniqueType.GreatPersonBoostWithFriendship));
            foreach (var unique in boostUniques)
                yield return new AllGPPercentageEntry("Declaration of Friendship", null, unique.Params[0].ToInt());
        }
    }

    /// <summary>
    /// Aggregate all percentage bonuses that apply to all GPP.
    /// For use by <see cref="City.GetGreatPersonPercentageBonus"/> (which in turn is only used by autoAssignPopulation)
    /// </summary>
    public static int GetGreatPersonPercentageBonus(City city)
    {
        int sum = 0;
        foreach (var entry in GetPercentagesApplyingToAllGP(city)) sum = unchecked(sum + entry.Bonus);
        return sum;
    }

    /// <summary>Collects all GPP names that have base points</summary>
    public HashSet<string> AllNames { get; } = []; // Kotlin mutableSetOf(): insertion ordered, only iterated in that order below

    public List<Entry> BasePoints { get; } = [];
    public List<Entry> PercentBonuses { get; } = [];

    /// <summary>
    /// Manages calculating Great Person Points per City for nextTurn.
    /// Keeps flat points and percentage boni as separate items.
    /// See <see cref="Sum"/> to calculate the aggregate, use <see cref="BasePoints"/> and <see cref="PercentBonuses"/> to list as breakdown
    /// </summary>
    public GreatPersonPointsBreakdown(City city)
    {
        ruleset = city.GetRuleset();

        // Collect points from Specialists
        var specialists = new Entry("Specialists"); // "Tutorial/Great People" as link doesn't quite fit
        foreach (var (specialistName, amount) in city.Population.GetNewSpecialists())
            if (ruleset.Specialists.ContainsKey(specialistName)) // To solve problems in total remake mods
            {
                var specialist = ruleset.Specialists[specialistName];
                specialists.Counter.Add(specialist.GreatPersonPoints * amount);
            }
        BasePoints.Add(specialists);
        AllNames.UnionWith(specialists.Counter.Keys);

        // Collect points from buildings - duplicates listed individually (should not happen in vanilla Unciv)
        foreach (var building in city.CityConstructions.GetBuiltBuildings())
        {
            if (building.GreatPersonPoints.IsEmpty()) continue;
            BasePoints.Add(new Entry(building.Name, building.MakeLink(), counter: building.GreatPersonPoints));
            AllNames.UnionWith(building.GreatPersonPoints.Keys);
        }

        // Translate bonuses applying to all GP equally
        foreach (var item in GetPercentagesApplyingToAllGP(city))
        {
            var bonusEntry = new Entry(item.Source, item.PediaLink, isAllGP: true);
            foreach (var name in AllNames)
                bonusEntry.Counter.Add(name, item.Bonus);
            PercentBonuses.Add(bonusEntry);
        }

        // B016: Arts and Sciences Funding
        foreach (var (name, percent) in LeagueEffects.For(city.Civ).GreatPersonRates())
        {
            if (!AllNames.Contains(name)) continue;
            var leagueEntry = new Entry("World Congress");
            leagueEntry.Counter.Add(name, percent);
            PercentBonuses.Add(leagueEntry);
        }

        // And last, the GPP-type-specific GreatPersonEarnedFaster Unique
        var stateForConditionals = city.State;
        city.Civ.ForEachMatchingUnique(UniqueType.GreatPersonEarnedFaster, stateForConditionals, unique =>
        {
            var gppName = unique.Params[0];
            if (AllNames.Contains(gppName)) // No sense applying a percentage without base points
            {
                var bonusEntry = new Entry(GetUniqueSourceName(unique), GuessPediaLink(unique));
                bonusEntry.Counter.Add(gppName, unique.Params[1].ToInt());
                PercentBonuses.Add(bonusEntry);
            }
        });
    }

    /// <summary>Aggregate over sources, applying percentage boni using fixed-point math to avoid rounding surprises</summary>
    public Counter<string> Sum()
    {
        // Accumulate base points as fake "fixed-point"
        var result = new Counter<string>();
        foreach (var entry in BasePoints)
            result.Add(entry.Counter * FixedPointFactor);

        // Accumulate percentage bonuses additively not multiplicatively
        var bonuses = new Counter<string>();
        foreach (var entry in PercentBonuses)
        {
            bonuses.Add(entry.Counter);
        }

        // Apply percent bonuses
        foreach (var key in result.Keys.Where(it => bonuses.ContainsKey(it)).ToList())
        {
            result.Add(key, unchecked(result[key] * bonuses[key]) / 100);
        }

        // Round fixed-point to integers, toSet() because a result of 0 will remove the entry (-99% bonus in a certain Mod)
        foreach (var key in result.Keys.ToList())
            result[key] = unchecked(result[key] + FixedPointFactor / 2) / FixedPointFactor;

        // Remove all "gpp" values that are not valid units
        foreach (var key in result.Keys.ToList())
            if (!ruleset.Units.ContainsKey(key))
                result.Remove(key);

        return result;
    }
}
