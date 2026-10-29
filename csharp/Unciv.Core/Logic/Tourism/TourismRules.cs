using Unciv.Logic.Civilization.Transients;
using Unciv.Logic.GreatWorks;
using Unciv.Logic.Trade.Routes;
using Unciv.Models;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Tourism;

// Brave New World (ticket B008, spec id B031, bnw-spec/SPEC.md section 4.3.1 and 4.3.2). Not part of the Kotlin source.
//
// Tourism of a city and of a civ, and the percent modifier of a civ toward another. Integer arithmetic with truncating division, as in Civ V.
// Deliberate deviations and unresolved spec items (no decompiler output or SDK source is available, so [K]/[?] items keep the spec value):
// - "[x]% Tourism" is one unique (TourismDuringGoldenAge). A city counts every one whose conditionals hold (the player tourism modifier and the
//   Brazil Golden Age modifier of 4.3.1); toward a civ (4.3.2 step 7) the ones that carry the "during a Golden Age" conditional count once more,
//   as the spec lists both places.
// - The league/holy-city tourism modifier of 4.3.1 (World Religion resolution) is not applied: the league does not exist yet (B015).
// - Culture from "Wonders, Landmarks and Natural Wonders": the Culture of the built wonders of the city (their own stats), plus the Culture
//   of the city's tiles that hold a Landmark improvement (unique "Landmark") or a natural wonder. Spec [I].
// - Diplomats (step 3, B020) and ideologies (steps 3 and 6, B013) do not exist yet: no civ has a diplomat, and the ideology is a plain field on its PolicyManager.
// - The modifier of a civ is never below the sum of its parts but the resulting influence per turn is clamped at 0 (a -34% modifier cannot
//   make influence negative, and a modifier below -100% would).

/// <summary>Tourism and the tourism modifier toward other civs (spec 4.3.1, 4.3.2).</summary>
public static class TourismRules
{
    /// <summary>Does the ruleset turn Great Works and tourism on (ModOptions <c>Enables Great Works and tourism</c>)?</summary>
    public static bool IsEnabled(CivilizationObject civ) => GreatWorkRules.IsEnabled(civ.GameInfo.Ruleset);

    private static ModConstants Constants(CivilizationObject civ) => civ.GameInfo.Ruleset.ModOptions.Constants;

    // ---- city tourism (4.3.1)

    /// <summary>Tourism of a city before the percent modifier: Great Works, theming, wonder culture, faith and building tourism.</summary>
    public static int CityTourismBeforeModifiers(CityObject city)
    {
        if (!IsEnabled(city.Civ) || city.IsBeingRazed || city.IsInResistance()) return 0;
        int works = unchecked(city.GetNumberOfGreatWorks() * Constants(city.Civ).BaseTourismPerGreatWork);
        int greatWorkPercent = city.GetMatchingUniques(UniqueType.TourismFromGreatWorks)
            .Where(it => city.MatchesFilter(it.Params[1])).Sum(it => it.Params[0].ToInt());
        int before = unchecked(works + city.GetThemingBonuses() + works * greatWorkPercent / 100);

        int landmarkPercent = city.GetMatchingUniques(UniqueType.LandmarksCultureAsTourism).Sum(it => it.Params[0].ToInt());
        if (landmarkPercent > 0) before = unchecked(before + LandmarkCulture(city) * landmarkPercent / 100);

        var built = city.CityConstructions.GetBuiltBuildings().ToList();
        foreach (var unique in city.GetMatchingUniques(UniqueType.TourismFromBuildingsFlat))
        {
            if (!city.MatchesFilter(unique.Params[2])) continue;
            int count = built.Count(it => it.MatchesFilter(unique.Params[1], city.State));
            before = unchecked(before + unique.Params[0].ToInt() * count);
        }
        // Tech-enhanced tourism of single buildings (Eiffel Tower): the unique sits on the building, its conditional names the tech.
        foreach (var unique in city.GetMatchingUniques(UniqueType.TourismAmount)) before = unchecked(before + unique.Params[0].ToInt());
        return before;
    }

    // The culture of the wonders, landmark tiles and natural wonders of a city (whole points, truncated once like a Culture stat).
    private static int LandmarkCulture(CityObject city)
    {
        float culture = 0f;
        foreach (var building in city.CityConstructions.GetBuiltBuildings().Where(it => it.IsWonder)) culture += building.GetStats(city).Culture;
        foreach (var tile in city.GetTiles())
        {
            bool landmark = tile.GetUnpillagedImprovement() is { } name
                && city.GetRuleset().TileImprovements.TryGetValue(name, out var improvement)
                && ((IHasUniques)improvement).HasUnique(UniqueType.IsLandmark);
            if (landmark || tile.IsNaturalWonder()) culture += tile.Stats.GetTileStats(city, city.Civ).Culture;
        }
        return KotlinMath.FloatToInt(culture);
    }

    /// <summary>The percent modifier of a city's tourism (spec 4.3.1 <c>mod</c>): never negative.</summary>
    public static int CityTourismModifier(CityObject city)
    {
        var civ = city.Civ;
        int percent = Math.Max(0, civ.GetMatchingUniques(UniqueType.TourismDuringGoldenAge).Sum(it => it.Params[0].ToInt()));
        if ((civ.Culture?.TourismBonusTurns ?? 0) > 0) percent = unchecked(percent + Constants(civ).TemporaryTourismBoostMod);
        var built = city.CityConstructions.GetBuiltBuildings().ToList();
        foreach (var unique in city.GetMatchingUniques(UniqueType.TourismFromBuildings))
            if (built.Any(it => it.MatchesFilter(unique.Params[1], city.State))) percent = unchecked(percent + unique.Params[0].ToInt());
        return percent;
    }

    /// <summary>Tourism a city produces per turn.</summary>
    public static int CityTourism(CityObject city)
    {
        int before = CityTourismBeforeModifiers(city);
        int modifier = CityTourismModifier(city);
        return modifier > 0 ? unchecked(before * (100 + modifier) / 100) : before;
    }

    /// <summary>Sum of the tourism of all cities of the civ (@10073fa0).</summary>
    public static int PlayerTourism(CivilizationObject civ) => (int)Math.Clamp(civ.Cities.Sum(it => (long)CityTourism(it)), int.MinValue, int.MaxValue);

    // ---- modifier toward a civ (4.3.2)

    /// <summary>The ideology a civ follows, or null (<see cref="Civilization.Managers.PolicyManager.Ideology"/>, chosen by B013).</summary>
    public static string? IdeologyOf(CivilizationObject civ) => civ.Policies.Ideology;

    /// <summary>The additive percent by which <paramref name="me"/>'s tourism counts toward <paramref name="target"/>.</summary>
    public static int ModifierWith(CivilizationObject me, CivilizationObject target) =>
        ModifierWith(me, target, diplomatInTargetCapital: false); // Seam (B020): a diplomat of me in the capital of target

    /// <summary><see cref="ModifierWith(CivilizationObject, CivilizationObject)"/> with the diplomat answer given.</summary>
    public static int ModifierWith(CivilizationObject me, CivilizationObject target, bool diplomatInTargetCapital)
    {
        var constants = Constants(me);
        int modifier = 0;
        int Sum(UniqueType type) => me.GetMatchingUniques(type).Sum(it => it.Params[0].ToInt());

        // 1. target allows my civ to enter its borders
        if (me.GetDiplomacyManager(target)?.HasOpenBorders == true)
            modifier = unchecked(modifier + constants.TourismModifierOpenBorders + Sum(UniqueType.TourismOpenBorders));
        // 2. a trade route connects us (either direction)
        var game = me.GameInfo;
        if (TradeRouteRules.Routes(game).Any(it => (it.OriginOwner == me.CivId && it.DestOwner == target.CivId)
                                                   || (it.OriginOwner == target.CivId && it.DestOwner == me.CivId)))
            modifier = unchecked(modifier + constants.TourismModifierTradeRoute + Sum(UniqueType.TourismTradeRoute));
        // 3. different ideologies; the diplomat bonus applies only then
        string? myIdeology = IdeologyOf(me);
        string? targetIdeology = IdeologyOf(target);
        if (myIdeology != null && targetIdeology != null && myIdeology != targetIdeology)
        {
            modifier = unchecked(modifier + constants.TourismModifierDifferentIdeologies);
            if (diplomatInTargetCapital) modifier = unchecked(modifier + constants.TourismModifierDiplomat);
        }
        // 4. I am happier
        if (me.GetHappiness() > target.GetHappiness()) modifier = unchecked(modifier + Sum(UniqueType.TourismLessHappy));
        // 5. once per third major civ at war with both of us
        int commonFoes = game.Civilizations.Count(it => it.IsMajorCiv() && it.IsAlive() && it != me && it != target
            && me.IsAtWarWith(it) && target.IsAtWarWith(it));
        if (commonFoes > 0) modifier = unchecked(modifier + Sum(UniqueType.TourismCommonFoe) * commonFoes);
        // 6. same ideology
        if (myIdeology != null && myIdeology == targetIdeology) modifier = unchecked(modifier + Sum(UniqueType.TourismSharedIdeology));
        // 7. my golden age (the uniques that name it, see the header)
        if (me.GoldenAges.IsGoldenAge())
            modifier = unchecked(modifier + me.GetMatchingUniques(UniqueType.TourismDuringGoldenAge)
                .Where(it => it.HasModifier(UniqueType.ConditionalGoldenAge)).Sum(it => it.Params[0].ToInt()));
        // 8. most of the target's cities follow the religion I founded
        if (FollowsFoundedReligionOf(target, me))
            modifier = unchecked(modifier + constants.TourismModifierSharedReligion + Sum(UniqueType.TourismSharedReligion));
        return modifier;
    }

    // "Most cities": the religion that is the majority religion of the most cities of the civ wins (first in city order on a tie).
    private static bool FollowsFoundedReligionOf(CivilizationObject follower, CivilizationObject founder)
    {
        string? founded = founder.ReligionManager.Religion?.Name;
        if (founded == null) return false;
        string? best = null;
        int bestCount = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var city in follower.Cities)
        {
            string? name = city.Religion.GetMajorityReligionName();
            if (name == null) continue;
            int count = counts[name] = counts.GetValueOrDefault(name) + 1;
            if (count > bestCount) { best = name; bestCount = count; }
        }
        return best == founded;
    }
}
