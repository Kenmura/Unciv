using Unciv.Logic.Automation.Unit;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.UI.Screens.VictoryScreen;
using Unciv.Utils;
using Unciv.View;
using CityObject = Unciv.Logic.City.City;
using CivilizationClass = Unciv.Logic.Civilization.Civilization;

namespace Unciv.Logic.Automation.Civilization;

// NextTurnAutomation.kt (T094), research, policy, city and settler members.
public static partial class NextTurnAutomation
{
    private static void ChooseTechToResearch(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var rng = civInfo.State.StateBasedRandom("NextTurnAutomation.chooseTechToResearch");

        List<List<Technology>> GetGroupedResearchableTechs()
        {
            // groupBy keeps first-seen key order, toSortedMap sorts by key: OrderBy over the groups is the same
            return civInfo.GameInfo.Ruleset.Technologies.Values
                .Where(it => civInfo.Tech.CanBeResearched(it.Name))
                .GroupBy(it => it.Cost)
                .OrderBy(it => it.Key)
                .Select(it => it.ToList())
                .ToList();
        }

        var stateForConditionals = civInfo.State;
        while (civInfo.Tech.FreeTechs > 0)
        {
            var costs = GetGroupedResearchableTechs();
            if (costs.Count == 0) return;

            // Ignore rows where all techs have 0 weight
            var mostExpensiveTechs = costs.LastOrDefault(row => row.Any(it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals) > 0))
                ?? costs[^1];
            var chosenTech = mostExpensiveTechs.RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals));
            civInfo.Tech.GetFreeTechnology(chosenTech.Name);
        }
        if (civInfo.Tech.TechsToResearch.Count == 0)
        {
            var costs = GetGroupedResearchableTechs();
            if (costs.Count == 0) return;

            // Ignore rows where all techs have 0 weight
            var cheapestTechs = costs.FirstOrDefault(row => row.Any(it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals) > 0))
                ?? costs[0];
            //Do not consider advanced techs if only one tech left in cheapest group
            Technology techToResearch;
            if (cheapestTechs.Count == 1 || costs.Count == 1)
            {
                techToResearch = cheapestTechs.RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals));
            }
            else
            {
                //Choose randomly between cheapest and second cheapest group
                var techsAdvanced = costs[1];
                techToResearch = cheapestTechs.Concat(techsAdvanced).ToList()
                    .RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(stateForConditionals));
            }

            civInfo.Tech.TechsToResearch.Add(techToResearch.Name);
        }
    }

    private static void AdoptPolicy(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var rng = civInfo.State.StateBasedRandom("NextTurnAutomation.adoptPolicy");
        /*
        # Branch-based policy-to-adopt decision
        Basically the AI prioritizes finishing incomplete branches before moving on, \
        unless a new branch with higher priority is adoptable.

        - If incomplete branches have higher priorities than any newly adoptable branch,
            - Candidates are the unfinished branches.
        - Else if newly adoptable branches have higher priorities than any incomplete branch,
            - Candidates are the new branches.
        - Choose a random candidate closest to completion.
        - Pick a random child policy of a chosen branch and adopt it.
        */
        var policies = civInfo.Policies;
        if (policies.CanChooseIdeology()) ChooseIdeology(civInfo, rng);
        while (civView.CanAdoptPolicy())
        {
            // B013: tenets first (free ones, or paid with culture); social branches only when their own pool (a free policy, or culture) can pay
            if (AdoptTenet(civInfo, rng)) continue;
            if (policies.FreePolicies == 0 && policies.StoredCulture < policies.GetCultureNeededForNextPolicy()) return;

            var incompleteBranches = civInfo.Policies.IncompleteBranches;
            var adoptableBranches = civInfo.Policies.AdoptableBranches;

            // Skip the whole thing if all branches are completed
            if (incompleteBranches.Count == 0 && adoptableBranches.Count == 0) return;

            var priorityMap = civInfo.Policies.PriorityMap;
            int? maxIncompletePriority = civInfo.Policies.GetMaxPriority(incompleteBranches);
            int? maxAdoptablePriority = civInfo.Policies.GetMaxPriority(adoptableBranches);

            // This here is a (probably dirty) code to bypass NoSuchElementException error
            //  when one of the priority variables is null
            maxIncompletePriority ??= maxAdoptablePriority.NotNull() - 1;
            maxAdoptablePriority ??= maxIncompletePriority.Value - 1;

            // Candidate branches to adopt
            HashSet<PolicyBranch> candidates;
            // If incomplete branches have higher priorities than any newly adoptable branch,
            if (maxAdoptablePriority <= maxIncompletePriority)
            {
                // Prioritize finishing one of the unfinished branches
                candidates = incompleteBranches.Where(it => priorityMap[it] == maxIncompletePriority).ToHashSet();
            }
            // If newly adoptable branches have higher priorities than any incomplete branch,
            else
            {
                // Prioritize adopting one of the new branches
                candidates = adoptableBranches.Where(it => priorityMap[it] == maxAdoptablePriority).ToHashSet();
            }

            // branchCompletionMap but keys are only candidates
            var candidateCompletionMap = civInfo.Policies.BranchCompletionMap.Where(it => candidates.Contains(it.Key)).ToList();

            // Choose the branch with the LEAST REMAINING policies, not the MOST ADOPTED ones
            var targetBranch = candidateCompletionMap
                .GroupBy(it => it.Key.Policies.Count - it.Value)
                .OrderBy(it => it.Key)
                .First().ToList().Random(rng).Key;

            Policy policyToAdopt = civInfo.Policies.IsAdoptable(targetBranch)
                ? targetBranch
                : targetBranch.Policies.Where(it => civInfo.Policies.IsAdoptable(it)).ToList()
                    .RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(civInfo.State));

            civInfo.Policies.Adopt(policyToAdopt);
        }
    }

    /// <summary>
    /// B013 (not in Kotlin, bnw-spec 4.6.2): the AI picks the ideology branch with the highest <c>PolicyManager.PriorityMap</c> value (the same branch
    /// priorities it uses for social branches), a random one among equals.
    /// </summary>
    private static void ChooseIdeology(CivilizationClass civInfo, GameRandom rng)
    {
        var priorityMap = civInfo.Policies.PriorityMap;
        var options = civInfo.Policies.IdeologyBranches.Where(it => civInfo.Policies.IsAdoptable(it)).ToList();
        if (options.Count == 0) return;
        var best = options.Max(it => priorityMap[it]);
        civInfo.Policies.Adopt(options.Where(it => priorityMap[it] == best).ToList().Random(rng));
    }

    /// <summary>B013: adopt one tenet of the civ's ideology (random, weighted like policies) when one is adoptable and affordable.</summary>
    private static bool AdoptTenet(CivilizationClass civInfo, GameRandom rng)
    {
        var policies = civInfo.Policies;
        if (policies.Ideology == null || !civInfo.GameInfo.Ruleset.PolicyBranches.TryGetValue(policies.Ideology, out var branch)) return false;
        var candidates = branch.Policies.Where(it => policies.CanAdoptPolicy(it)).ToList();
        if (candidates.Count == 0) return false;
        policies.Adopt(candidates.RandomWeighted(rng, it => ((IHasUniques)it).GetWeightForAiDecision(civInfo.State)));
        return true;
    }

    public static void ChooseGreatPerson(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var greatPeople = civInfo.GreatPeople.GetFreeGreatPersonOptions();
        if (greatPeople.Count == 0) return;
        var rng = civInfo.State.StateBasedRandom("NextTurnAutomation.chooseGreatPerson");
        var greatPerson = greatPeople.Random(rng);
        var scienceGP = greatPeople.FirstOrDefault(it => it.Uniques.Contains("Great Person - [Science]"));
        if (scienceGP != null) greatPerson = scienceGP;
        // Humans would pick a prophet or engineer, but it'd require more sophistication on part of the AI - a scientist is the safest option for now

        civInfo.GreatPeople.ChooseFreeGreatPerson(greatPerson.Name);
    }

    /// <summary>If we are able to build a spaceship but have already spent our resources, try disbanding
    ///  a unit and selling a building to make room. Can happen due to trades etc</summary>
    private static void FreeUpSpaceResources(CivView civView)
    {
        var civInfo = civView.GetCiv();
        // No need to build spaceship parts just yet
        if (civView.GetAvailableVictories()
                .All(it => civInfo.VictoryManager.GetNextMilestone(it)?.Type != MilestoneType.AddedSSPartsInCapital))
            return;

        foreach (var resource in civInfo.GameInfo.SpaceResources)
        {
            // Have enough resources already
            if (civView.GetResourceAmount(resource) >= 2) continue;

            // minByOrNull: the first minimum
            var unitToDisband = civInfo.Units.GetCivUnits()
                .Where(it => it.RequiresResource(resource) && !it.HasUnique(UniqueType.SpaceshipPart))
                .Aggregate((Unciv.Logic.Map.MapUnits.MapUnit?)null, (best, it) => best == null || it.GetForceEvaluation() < best.GetForceEvaluation() ? it : best);
            unitToDisband?.Disband();
            SellBuildingForResource(civView, resource);
        }
    }

    private static void SellBuildingForResource(CivView civView, string resource)
    {
        var civInfo = civView.GetCiv();
        foreach (var city in civInfo.Cities)
        {
            if (city.HasSoldBuildingThisTurn) continue;
            var rng = city.State.StateBasedRandom("NextTurnAutomation.freeUpSpaceResources");
            var buildingToSell = civView.Ruleset.Buildings.Values
                .Where(it => city.CityConstructions.IsBuilt(it.Name)
                    && ((IConstruction)it).RequiredResources(city.State).Contains(resource)
                    && it.IsSellable()
                    && !civInfo.CivConstructions.HasFreeBuilding(city, it))
                .ToList()
                .RandomOrNull(rng);
            if (buildingToSell == null) continue;
            city.SellBuilding(buildingToSell);
            return;
        }
    }

    public static void AutomateCityBombardment(CivView civView)
    {
        var civInfo = civView.GetCiv();
        foreach (var city in civInfo.Cities) UnitAutomation.TryBombardEnemy(city);
    }

    private static bool ShouldAnnexCity(CivView civView, CityObject city)
    {
        var civInfo = civView.GetCiv();
        return city.IsPuppet
            && city.Population.Population > 9
            && !city.IsInResistance()
            && !civView.HasUnique(UniqueType.MayNotAnnexCities)
            && civView.GetHappiness() >= 5 // live happiness - statsForNextTurn is stale mid-turn, so a multi-city conquest wave would otherwise annex several puppets against a pre-conquest reading
            && civInfo.Stats.StatsForNextTurn.Happiness > city.Population.Population * 2 - 8; // don't go below -10 happiness due to annexing
    }

    public static void AutomateCities(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var ownMilitaryStrength = civInfo.GetStatForRanking(RankingType.Force);
        var sumOfEnemiesMilitaryStrength = 0;
        foreach (var it in civInfo.GameInfo.Civilizations.Where(it => it != civInfo && !it.IsBarbarian && civInfo.IsAtWarWith(it)))
            sumOfEnemiesMilitaryStrength = unchecked(sumOfEnemiesMilitaryStrength + it.GetStatForRanking(RankingType.Force));
        var civHasSignificantlyWeakerMilitaryThanEnemies =
            ownMilitaryStrength < sumOfEnemiesMilitaryStrength * 0.66f;
        foreach (var city in civInfo.Cities)
        {
            if (ShouldAnnexCity(civView, city)) city.AnnexCity();

            // Don't pull a city off a spaceship part it's already building for the military push (only if actually attacked)
            var onSpaceshipPart = civInfo.GameInfo.SpaceResources.Contains(city.CityConstructions.GetCurrentConstruction().Name);
            if (city.Health < city.GetMaxHealth() || (civHasSignificantlyWeakerMilitaryThanEnemies && !onSpaceshipPart))
            {
                Automation.TryTrainMilitaryUnit(city); // need defenses if city is under attack
                if (city.CityConstructions.ConstructionQueue.Count != 0)
                    continue; // found a unit to build so move on
            }

            city.CityConstructions.ChooseNextConstruction();
        }
    }

    private static void TrainSettler(CivView civView)
    {
        var civInfo = civView.GetCiv();
        var personality = civInfo.GetPersonality();
        if (civView.IsCityState) return;
        if (civInfo.IsOneCityChallenger()) return;
        if (civView.Cities().Count == 0) return;
        if (civView.GetHappiness() <= civView.Cities().Count) return;
        if (CivilianUnitAutomation.IsLateGame(civView))
        {
            // all suitable land may be occupied already
            // 10 tiles away and 6 "options" are heuristics based on nothing, feel free to change
            var unoccupiedNearishTiles = 0;
            foreach (var city in civInfo.Cities)
                city.GetCenterTile().ForEachTileInDistance(10, it =>
                {
                    if (it.OwningCity == null && it.Neighbors.All(neighbor => neighbor.OwningCity == null)) unoccupiedNearishTiles++;
                });
            if (unoccupiedNearishTiles < 6) return;
        }

        // This is a tough one - if we don't ignore conditionals we could have units that can found only on certain tiles that are ignored
        // If we DO ignore conditionals we could get a unit that can only found if there's a certain tech, or something
        // Allow up to 2 settlers in flight (built or in production) so early expansion isn't serialized to
        // one settler at a time - the stock happiness/military guards below still bound how many we build.
        const int maxSettlersInFlight = 2;
        var settlersInFlight = civInfo.Units.GetCivUnits().Count(it => it.HasUnique(UniqueType.FoundCity, GameContext.IgnoreConditionalsState)) +
            civInfo.Cities.Count(it => it.CityConstructions.GetCurrentConstruction() is BaseUnit currentConstruction && currentConstruction.IsCityFounder());
        if (settlersInFlight >= maxSettlersInFlight) return;
        var settlerUnits = civView.Ruleset.Units.Values
            .Where(it => it.IsCityFounder() && it.IsBuildable(civInfo)
                && !((IHasUniques)personality).GetMatchingUniques(UniqueType.WillNotBuild, civInfo.State)
                    .Any(unique => it.MatchesFilter(unique.Params[0], civInfo.State)))
            .ToList();
        if (settlerUnits.Count == 0) return;

        // The "defend them first" gate permanently latches peaceful, happy civs at ~4 cities. Bypass it when
        // we clearly have room to expand safely: at peace, ample happiness, still early, and in a game with
        // other major civs whose presence deters an opportunistic attack (this is a bad idea in a 1v1 duel).
        var hasSlackToExpandSafely = !civView.IsAtWar() && civView.GetHappiness() >= 8 && civInfo.GameInfo.Turns < 150
            && civInfo.GameInfo.Civilizations.Count(it => it.IsMajorCiv() && it.IsAlive()) >= 3;
        if (civView.GetUnits().Count(it => it.IsMilitary()) < civView.Cities().Count && !hasSlackToExpandSafely)
            return; // We need someone to defend them first

        // maxByOrNull: the first maximum
        CityObject? bestCity = null;
        foreach (var city in civInfo.Cities
                     .Where(it => !(it.IsPuppet || it.Population.Population < 3))
                     // don't re-target a city that is already producing a settler (reachable now that 2 may be in flight)
                     .Where(it => !((it.CityConstructions.GetCurrentConstruction() as BaseUnit)?.IsCityFounder() == true)))
        {
            if (bestCity == null || city.CityStats.CurrentCityStats.Production > bestCity.CityStats.CurrentCityStats.Production)
                bestCity = city;
        }
        if (bestCity == null) return;
        if (bestCity.CityConstructions.GetBuiltBuildings().Count() > 1) // 2 buildings or more, otherwise focus on self first
        {
            // minByOrNull: the first minimum
            var cheapest = settlerUnits[0];
            foreach (var unit in settlerUnits)
                if (unit.Cost < cheapest.Cost) cheapest = unit;
            bestCity.CityConstructions.SetCurrentConstruction(cheapest.Name);
        }
    }

    // Technically, this function should also check for civs that have liberated one or more cities
    // However, that can be added in another update, this PR is large enough as it is.
    private static void TryVoteForDiplomaticVictory(CivView civView)
    {
        var civ = civView.GetCiv();
        if (!civView.MayVoteForDiplomaticVictory()) return;
        var rng = civ.State.StateBasedRandom("NextTurnAutomation.tryVoteForDiplomaticVictory");

        CivilizationClass? chosenCiv;
        if (civ.IsMajorCiv())
        {
            var knownMajorCivs = civ.GetKnownCivs().Where(it => it.IsMajorCiv()).ToList();
            float? highestOpinion = null;
            foreach (var it in knownMajorCivs)
            {
                var opinion = civ.GetDiplomacyManager(it).NotNull().OpinionOfOtherCiv();
                if (highestOpinion == null || opinion > highestOpinion) highestOpinion = opinion;
            }

            if (highestOpinion == null) chosenCiv = null; // Abstain if we know nobody
            else if (highestOpinion < -80 || highestOpinion < -40 && highestOpinion + rng.NextInt(40) < -40)
                chosenCiv = null; // Abstain if we hate everybody (proportional chance in the RelationshipLevel.Enemy range - lesser evil)
            else
                chosenCiv = knownMajorCivs
                    .Where(it => civ.GetDiplomacyManager(it).NotNull().OpinionOfOtherCiv() == highestOpinion)
                    .ToList().Random(rng);
        }
        else
        {
            chosenCiv = civ.AllyCiv;
        }

        civ.DiplomaticVoteForCiv(chosenCiv?.CivId);
    }
}
