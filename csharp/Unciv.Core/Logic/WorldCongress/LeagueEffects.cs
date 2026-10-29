using System.Collections.Immutable;
using Unciv.Logic.Civilization;
using Unciv.Logic.Trade.Routes;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.WorldCongress;

// Brave New World (ticket B016, spec id B061, bnw-spec/SPEC.md section 4.7.5). Not part of the Kotlin source.
//
// The effects of the enacted resolutions on a league member, aggregated once per query (the spec's League.GetEffects(civ)). Every system that a
// resolution touches asks here; nothing else reads the resolution table, so a repeal restores the state by itself. The effects apply to the
// members of the league (the major civs alive at founding); without a league, or for a non-member, the result is <see cref="None"/>.
// Deliberate readings where the spec is open ([K], no decompile available):
// - Embargoes: a route between two civs is refused (and an existing one ends) when one side is the embargoed civ, or when one side is a city-state and
//   All City-States Embargo is in force. Trade deals are not covered (no source for it); only trade routes are.
// - Ban Luxury removes the luxury from every happiness line (own, city-state ally and traded).
// - Standing Army Tax multiplies the unit maintenance gold by 1 + percent / 100 (float, before the Int conversion).
// - Scholars in Residence: a tech some other living member has researched costs percent less (multiplicative with the other tech cost modifiers).
// - Cultural / Natural Heritage Sites: culture in the city holding the World Wonder / owning the Natural Wonder tile. Historical Landmarks: culture on
//   the worked tiles with an unpillaged Great Person improvement or Landmark.
// - World Religion spread strength multiplies the pressure a city spreads and the pressure of the Missionaries and Prophets of that religion.
// - World Ideology: OtherIdeologyRebellionMod is the public opinion pressure toward the chosen ideology for every member (4.6.4 step 1).
// - Arts and Sciences Funding add their percents to the Great Person points rate of the Writer, Artist, Musician and Scientist, Engineer, Merchant.

/// <summary>The effects of the enacted resolutions on one civ (see the file header).</summary>
public sealed record LeagueEffects(
    bool EmbargoCityStates,
    IReadOnlyList<string> EmbargoedCivs,
    IReadOnlyList<string> BannedLuxuries,
    int UnitMaintenancePercent,
    int MemberTechDiscountPercent,
    int CulturePerWorldWonder,
    int CulturePerNaturalWonder,
    bool NoNuclearWeapons,
    int ArtsyRatePercent,
    int ScienceyRatePercent,
    int GreatPersonImprovementCulture,
    int LandmarkCulture)
{
    public static readonly LeagueEffects None = new(false, [], [], 0, 0, 0, 0, false, 0, 0, 0, 0);

    private static readonly ImmutableArray<string> Artsy = ["Great Writer", "Great Artist", "Great Musician"];
    private static readonly ImmutableArray<string> Sciencey = ["Great Scientist", "Great Engineer", "Great Merchant"];

    private static IEnumerable<(ActiveResolution Active, LeagueResolution Resolution)> ActiveResolutions(League league) =>
        league.Active.SelectMany(it => LeagueData.Find(it.Resolution) is { } resolution ? [(it, resolution)] : Array.Empty<(ActiveResolution, LeagueResolution)>());

    /// <summary>The effects on <paramref name="civ"/>: <see cref="None"/> unless a league exists, the civ is one of its members and a resolution is in force.</summary>
    public static LeagueEffects For(CivilizationObject civ)
    {
        if (civ.GameInfo is not GameInfo { League: { Active.Count: > 0 } league } || league.GetMember(civ.CivId) == null) return None;
        bool cityStates = false;
        var embargoed = new List<string>();
        var banned = new List<string>();
        int maintenance = 0, techs = 0, wonders = 0, natural = 0, artsy = 0, sciencey = 0, improvement = 0, landmark = 0;
        bool nukes = false;
        foreach (var (active, resolution) in ActiveResolutions(league))
        {
            cityStates |= resolution.EmbargoCityStates;
            if (resolution.EmbargoPlayer && active.Choice.Length > 0) embargoed.Add(active.Choice);
            if (resolution.NoResourceHappiness && active.Choice.Length > 0) banned.Add(active.Choice);
            maintenance = unchecked(maintenance + resolution.UnitMaintenanceGoldPercent);
            techs = unchecked(techs + resolution.MemberDiscoveredTechMod);
            wonders = unchecked(wonders + resolution.CulturePerWonder);
            natural = unchecked(natural + resolution.CulturePerNaturalWonder);
            nukes |= resolution.NoTrainingNuclearWeapons;
            artsy = unchecked(artsy + resolution.ArtsyGreatPersonRateMod);
            sciencey = unchecked(sciencey + resolution.ScienceyGreatPersonRateMod);
            improvement = unchecked(improvement + resolution.GreatPersonTileImprovementCulture);
            landmark = unchecked(landmark + resolution.LandmarkCulture);
        }
        return new LeagueEffects(cityStates, embargoed, banned, maintenance, techs, wonders, natural, nukes, artsy, sciencey, improvement, landmark);
    }

    // ---- trade routes (4.7.5, 4.1.6)

    /// <summary>Is trade between the two civs embargoed for the league (see the file header)? Order does not matter; a civ never embargoes itself.</summary>
    public static bool IsEmbargoed(IGameInfoState game, CivilizationObject one, CivilizationObject two)
    {
        if (one == two || game is not GameInfo { League: { Active.Count: > 0 } league }) return false;
        foreach (var (active, resolution) in ActiveResolutions(league))
        {
            if (resolution.EmbargoCityStates && (one.IsCityState || two.IsCityState)) return true;
            if (resolution.EmbargoPlayer && (active.Choice == one.CivId || active.Choice == two.CivId)) return true;
        }
        return false;
    }

    // ---- other systems

    public bool IsLuxuryBanned(string resourceName) => BannedLuxuries.Contains(resourceName);

    /// <summary>The percent by which a tech costs less for <paramref name="civ"/>: <see cref="MemberTechDiscountPercent"/> when another living member has researched it.</summary>
    public static int TechDiscountPercent(CivilizationObject civ, string techName)
    {
        var effects = For(civ);
        if (effects.MemberTechDiscountPercent == 0) return 0;
        if (civ.GameInfo is not GameInfo { League: { } league } game) return 0;
        bool known = LeagueRules.LivingMembers(game, league).Any(it => it != civ && it.Tech.IsResearched(techName));
        return known ? effects.MemberTechDiscountPercent : 0;
    }

    /// <summary>The percent added to the spread strength of a religion by the enacted resolutions that name it (World Religion).</summary>
    public static int ReligionSpreadPercent(IGameInfoState game, string? religion)
    {
        if (religion == null || game is not GameInfo { League: { Active.Count: > 0 } league }) return 0;
        return ActiveResolutions(league).Where(it => it.Active.Choice == religion).Sum(it => it.Resolution.ReligionSpreadStrengthMod);
    }

    /// <summary>The public opinion pressure the enacted World Ideology resolutions put on <paramref name="ideology"/> for <paramref name="civ"/> (spec 4.6.4 step 1).</summary>
    public static int IdeologyPressure(CivilizationObject civ, string ideology)
    {
        if (civ.GameInfo is not GameInfo { League: { Active.Count: > 0 } league } || league.GetMember(civ.CivId) == null) return 0;
        return ActiveResolutions(league).Where(it => it.Active.Choice == ideology).Sum(it => it.Resolution.OtherIdeologyRebellionMod);
    }

    /// <summary>The Great Person rate percent per Great Person name (Arts and Sciences Funding), zero percents left out.</summary>
    public IEnumerable<(string Name, int Percent)> GreatPersonRates()
    {
        foreach (var name in Artsy) if (ArtsyRatePercent != 0) yield return (name, ArtsyRatePercent);
        foreach (var name in Sciencey) if (ScienceyRatePercent != 0) yield return (name, ScienceyRatePercent);
    }

    /// <summary>The names of the league projects the enacted resolutions enable (B017 builds the projects).</summary>
    public static IEnumerable<string> EnabledProjects(GameInfo game) =>
        game.League == null ? [] : ActiveResolutions(game.League).Where(it => it.Resolution.Project.Length > 0).Select(it => it.Resolution.Project);

    /// <summary>The culture a city gets from Cultural / Natural Heritage Sites and Historical Landmarks.</summary>
    public static Stats CityStats(CityObject city)
    {
        var effects = For(city.Civ);
        var stats = new Stats();
        if (effects.CulturePerWorldWonder != 0)
        {
            int wonders = city.CityConstructions.GetBuiltBuildings().Count(it => it.IsWonder);
            stats.Culture = unchecked(stats.Culture + effects.CulturePerWorldWonder * wonders);
        }
        if (effects.CulturePerNaturalWonder != 0)
        {
            int natural = city.GetTiles().Count(it => it.IsNaturalWonder());
            stats.Culture = unchecked(stats.Culture + effects.CulturePerNaturalWonder * natural);
        }
        if (effects.GreatPersonImprovementCulture != 0 || effects.LandmarkCulture != 0)
        {
            foreach (var tile in city.GetWorkedTiles())
            {
                var improvement = tile.GetUnpillagedTileImprovement();
                if (improvement == null) continue;
                if (((IHasUniques)improvement).HasUnique(UniqueType.IsLandmark)) stats.Culture = unchecked(stats.Culture + effects.LandmarkCulture);
                else if (improvement.IsGreatImprovement()) stats.Culture = unchecked(stats.Culture + effects.GreatPersonImprovementCulture);
            }
        }
        return stats;
    }

    /// <summary>After a resolution was enacted or repealed: ends the trade routes it forbids and refreshes the stats of every member.</summary>
    public static void OnChanged(GameInfo game, League league)
    {
        league.Projects.RemoveAll(it => !LeagueProjects.IsEnabled(game, it.Name)); // B017: a repealed project loses its progress
        TradeRouteTurns.BreakInvalid(game);
        foreach (var civ in LeagueRules.LivingMembers(game, league))
        {
            foreach (var city in civ.Cities) city.CityStats.Update(updateCivStats: false);
            civ.UpdateStatsForNextTurn();
        }
    }
}
