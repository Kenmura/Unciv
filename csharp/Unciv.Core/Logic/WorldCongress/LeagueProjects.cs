using Unciv.Commands.Events;
using Unciv.Logic.Civilization;
using Unciv.Models.Ruleset;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.WorldCongress;

// Brave New World (ticket B017, spec id B062, bnw-spec/SPEC.md section 4.7.7). Not part of the Kotlin source.
//
// League projects: World's Fair, International Games and the International Space Station. A resolution of the same name enables the project (LeagueEffects.EnabledProjects);
// cities of the members work on the process "{project} Project" (PerpetualConstruction.LeagueProcess) and their production of the turn goes into the project.
// Once per game turn (LeagueRules.ProcessTurn, after all civs) a project whose total reaches its cost is completed. Deliberate readings where the spec is open
// ([K]/[I], no decompile available):
// - Cost = CostPerPlayer x living members x the game speed's production cost modifier (rounded half up). The era scaling (Eras.LeaguePercent [I]) is not applied.
// - A project completes in the league turn step, not mid-turn, so the result does not depend on the order the civs act in; everything contributed that turn counts.
// - Tier thresholds use ModConstants.LeagueProjectRewardTier1Threshold / Tier2Threshold times (cost / living members). Tier 3 goes to the top contributor; civs
//   tied for the top all get it (when the top is above zero). The tiers stack: tier 3 includes the rewards of tiers 1 and 2. Only living members are rewarded.
// - Completing the project consumes the resolution that enabled it (every active resolution of that project is removed), so a new vote can start the project again.
//   A project whose resolution is repealed loses its progress.
// - Rewards: Golden Age points and free policies are added at once; the +3 Happiness is permanent (LeagueMember.ProjectHappiness); the +30 influence goes to
//   every city-state the civ knows; the +100% culture / tourism last 20 turns (CivCultureManager); the science is 4 turns of the civ's current science; the free
//   Great Scientist appears next to the capital; the International Space Station building is added to the capital (it is "Unbuildable" otherwise).
// - The AI only contributes with cities that would otherwise be idle (ConstructionAutomation).

public static class LeagueProjects
{
    public const string WorldsFair = "World's Fair";
    public const string InternationalGames = "International Games";
    public const string SpaceStation = "International Space Station";

    public const int FairGoldenAgePoints = 500;
    public const int FairCultureTurns = 20;
    public const int GamesHappiness = 3;
    public const int GamesInfluence = 30;
    public const int GamesTourismTurns = 20;
    public const int SpaceScienceTurns = 4;
    public const string GreatScientist = "Great Scientist";

    /// <summary>The percent of culture the temporary culture boost adds to every city.</summary>
    public const int CultureBoostPercent = 100;

    /// <summary>Is the project enabled by an enacted resolution?</summary>
    public static bool IsEnabled(GameInfo game, string project) => LeagueEffects.EnabledProjects(game).Contains(project);

    /// <summary>Can a city of a living league member put its production into the project now?</summary>
    public static bool CanContribute(CityObject city, string project) =>
        city.Civ.GameInfo is GameInfo { League: { } league } game && city.Civ.IsAlive() && league.GetMember(city.Civ.CivId) != null && IsEnabled(game, project);

    /// <summary>The production the project costs at the moment (the members alive now).</summary>
    public static int Cost(GameInfo game, League league, string project)
    {
        int perPlayer = LeagueData.Projects.First(it => it.Name == project).CostPerPlayer;
        int members = LeagueRules.LivingMembers(game, league).Count();
        return KotlinMath.RoundToInt(perPlayer * members * game.Speed.ProductionCostModifier);
    }

    public static LeagueProject? Find(League league, string project) => league.Projects.FirstOrDefault(it => it.Name == project);

    /// <summary>Adds the production of a city to the project of its civ.</summary>
    public static void Contribute(CityObject city, string project, int production)
    {
        if (production <= 0 || city.Civ.GameInfo is not GameInfo { League: { } league } || !CanContribute(city, project)) return;
        var state = Find(league, project);
        if (state == null)
        {
            state = new LeagueProject { Name = project };
            league.Projects.Add(state);
        }
        var entry = state.Contributions.FirstOrDefault(it => it.Civ == city.Civ.CivId);
        if (entry == null) state.Contributions.Add(new LeagueVote { Civ = city.Civ.CivId, Choice = project, Count = production });
        else entry.Count = unchecked(entry.Count + production);
    }

    /// <summary>The tier (0 to 3) of a contribution: 1 and 2 by the thresholds of the ruleset, 3 for the top contributor (<paramref name="isTop"/>). Tiers stack.</summary>
    public static int Tier(Ruleset ruleset, int contribution, int cost, int members, bool isTop)
    {
        if (isTop && contribution > 0) return 3;
        var constants = ruleset.ModOptions.Constants;
        float share = (float)cost / Math.Max(1, members);
        if (contribution >= constants.LeagueProjectRewardTier2Threshold * share) return 2;
        if (contribution >= constants.LeagueProjectRewardTier1Threshold * share) return 1;
        return 0;
    }

    /// <summary>The tier of every living member for a project with the given contributions, in member order.</summary>
    public static List<KeyValuePair<string, int>> Tiers(GameInfo game, League league, LeagueProject state)
    {
        var members = LeagueRules.LivingMembers(game, league).ToList();
        int cost = Cost(game, league, state.Name);
        int top = members.Select(it => state.ContributionOf(it.CivId)).DefaultIfEmpty(0).Max();
        return members.Select(it =>
        {
            int contribution = state.ContributionOf(it.CivId);
            return KeyValuePair.Create(it.CivId, Tier(game.Ruleset, contribution, cost, members.Count, contribution == top));
        }).ToList();
    }

    /// <summary>Once per game turn: drops the projects that are no longer enabled, completes those that reached their cost.</summary>
    public static void ProcessTurn(GameInfo game, League league)
    {
        league.Projects.RemoveAll(it => !IsEnabled(game, it.Name));
        foreach (var state in league.Projects.ToList())
            if (state.Total >= Cost(game, league, state.Name)) Complete(game, league, state);
    }

    private static void Complete(GameInfo game, League league, LeagueProject state)
    {
        var tiers = Tiers(game, league, state);
        league.Projects.Remove(state);
        league.Active.RemoveAll(it => LeagueData.Find(it.Resolution)?.Project == state.Name);
        foreach (var (civId, tier) in tiers)
            for (int level = 1; level <= tier; level++)
                Reward(game, league, game.GetCivilization(civId), state.Name, level);
        LeagueEffects.OnChanged(game, league);
        game.EventSink?.Emit(new ProjectCompleted(state.Name, tiers));
        foreach (var civ in LeagueRules.LivingMembers(game, league).Where(it => it.IsHuman()))
        {
            int tier = tiers.First(it => it.Key == civ.CivId).Value;
            civ.AddNotification($"The {state.Name} project is complete! Your reward tier: {tier}", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy);
        }
    }

    private static void Reward(GameInfo game, League league, CivilizationObject civ, string project, int tier)
    {
        switch (project, tier)
        {
            case (WorldsFair, 1): civ.GoldenAges.AddHappiness(FairGoldenAgePoints); break;
            case (WorldsFair, 2): civ.Policies.FreePolicies++; break;
            case (WorldsFair, 3): (civ.Culture ??= new()).AddCultureBonusTurns(FairCultureTurns); break;
            case (InternationalGames, 1): AddHappiness(league, civ); break;
            case (InternationalGames, 2):
                AddHappiness(league, civ);
                foreach (var cityState in civ.GetKnownCivs().Where(it => it.IsCityState && it.IsAlive()).ToList())
                    cityState.GetDiplomacyManager(civ)?.AddInfluence(GamesInfluence);
                break;
            case (InternationalGames, 3):
                civ.Policies.FreePolicies++;
                (civ.Culture ??= new()).AddTourismBonusTurns(GamesTourismTurns);
                break;
            case (SpaceStation, 1): civ.Tech.AddScience(KotlinMath.RoundToInt(civ.Stats.StatsForNextTurn.Science * SpaceScienceTurns)); break;
            case (SpaceStation, 2):
                if (civ.GetCapital() is { } scientistCity && game.Ruleset.Units.ContainsKey(GreatScientist))
                    civ.Units.PlaceUnitNearTile(scientistCity.Location, GreatScientist);
                break;
            case (SpaceStation, 3):
                if (civ.GetCapital() is { } capital && game.Ruleset.Buildings.ContainsKey(SpaceStation) && !capital.CityConstructions.IsBuilt(SpaceStation))
                    capital.CityConstructions.AddBuilding(SpaceStation);
                break;
        }
    }

    private static void AddHappiness(League league, CivilizationObject civ)
    {
        if (league.GetMember(civ.CivId) is { } member) member.ProjectHappiness = unchecked(member.ProjectHappiness + GamesHappiness);
    }

    /// <summary>The permanent happiness a civ earned from league projects.</summary>
    public static int Happiness(CivilizationObject civ) =>
        civ.GameInfo is GameInfo { League: { } league } ? league.GetMember(civ.CivId)?.ProjectHappiness ?? 0 : 0;
}
