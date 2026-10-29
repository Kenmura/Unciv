using Unciv.Commands.Events;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Transients;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CityObject = Unciv.Logic.City.City;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.WorldCongress;

// Brave New World (ticket B015, spec id B060, bnw-spec/SPEC.md section 4.7). Not part of the Kotlin source.
//
// Founding, special sessions, delegates, proposals and voting of the World Congress. The effects of the resolutions are B016, the league
// projects B017, the diplomatic victory B018. Deliberate deviations and unresolved spec items (no decompiler output or SDK source is available,
// so the [K]/[?] items of 4.7.4 keep the spec value or the reading below):
// - Session shape [K]: a session opens at the end of a round (the AI members vote at once, the proposals of the proposers are fixed) and is
//   decided at the end of the next round, so every human has one round to commit delegates. A member that does nothing abstains.
//   After the decision the next session is TurnsBetweenSessions of the current special session x the game speed (Speed.Modifier as a percent;
//   Speed.LeaguePercent is not in the ruleset and equals it for all four speeds) rounds away. A special session holds the automatic proposals
//   (Change League Host, in the UN also the Diplomatic Victory) and the proposals already made.
// - Delegates are spent on proposals one by one (a civ backs one choice per proposal) and the pool is the delegate count when the session opened.
// - A choice decision takes the plurality; a tie or no vote fails [K]. Yes/No passes with Yes > No.
// - The proposers are the host and the member with the most delegates (the next one if that is the host), ties broken by civ order. A host that
//   died is replaced by the member with the most delegates.
// - Members are the major civs alive at founding. A civ that dies keeps its seat but has no delegates and cannot vote or propose.
// - The AI is minimal (LeagueAi): it proposes a resolution without a choice, backs its own and its friends' proposals and opposes those of its
//   enemies. The AI tickets refine it.
// - World Ideology needs a living member with an ideology; a ruleset without the tech of a resolution can never propose it.

public static class LeagueRules
{
    public const string Yes = "yes";
    public const string No = "no";

    /// <summary>Does the ruleset turn the World Congress on (ModOptions <c>Enables the World Congress</c>)?</summary>
    public static bool IsEnabled(Ruleset ruleset) => ((IHasUniques)ruleset.ModOptions).HasUnique(UniqueType.EnablesWorldCongress);

    // ---- members and eras

    /// <summary>Major civs that are alive, in civ order.</summary>
    public static List<CivilizationObject> LivingMajors(GameInfo game) =>
        game.Civilizations.Where(it => it.IsMajorCiv() && !it.IsSpectator() && it.IsAlive()).ToList();

    /// <summary>The highest era among the living major civs (@1024d960 [I]).</summary>
    public static int GameEraNumber(GameInfo game) => LivingMajors(game).Select(it => it.GetEraNumber()).DefaultIfEmpty(0).Max();

    private static int EraNumberOf(Ruleset ruleset, string era) => ruleset.Eras.TryGetValue(era, out var found) ? found.EraNumber : int.MaxValue;

    /// <summary>The index of the special session for an era: the last one whose trigger is not above it, at least the first.</summary>
    public static int SessionIndexForEra(Ruleset ruleset, int eraNumber)
    {
        int index = 0;
        for (int i = 0; i < LeagueData.Sessions.Count; i++)
            if (EraNumberOf(ruleset, LeagueData.Sessions[i].Era) <= eraNumber) index = i;
        return index;
    }

    /// <summary>The rounds between sessions at the given special session level (spec 4.7.1).</summary>
    public static int SessionInterval(GameInfo game, int sessionIndex)
    {
        int percent = (int)(game.Speed.Modifier * 100f + 0.5f);
        int interval = LeagueData.Sessions[sessionIndex].TurnsBetweenSessions * percent / 100;
        return interval > 0 ? interval : Math.Max(1, game.Ruleset.ModOptions.Constants.LeagueSessionIntervalBaseTurns);
    }

    public static IEnumerable<CivilizationObject> LivingMembers(GameInfo game, League league) =>
        league.Members.Select(it => game.GetCivilization(it.Civ)).Where(it => it.IsAlive());

    // ---- founding (4.7.1)

    /// <summary>Does the civ satisfy the Congress condition: it knows the tech that allows it and has met every other living major civ?</summary>
    public static bool MeetsFoundingCondition(GameInfo game, CivilizationObject civ) =>
        civ.HasUnique(UniqueType.AllowsWorldCongress) && LivingMajors(game).All(it => it == civ || civ.Knows(it));

    // ---- the turn (2.3 step 4)

    /// <summary>Once per game turn, after all civs: found the league, start a special session, count down, open or decide a session.</summary>
    public static void ProcessTurn(GameInfo game)
    {
        if (!IsEnabled(game.Ruleset)) return;
        var league = game.League;
        if (league == null)
        {
            TryFound(game);
            return;
        }
        EnsureHost(game, league);
        if (league.InSession)
        {
            EndSession(game, league);
            return;
        }
        int era = SessionIndexForEra(game.Ruleset, GameEraNumber(game));
        if (era > league.SessionIndex)
        {
            league.SessionIndex = era;
            StartSession(game, league, special: true);
            return;
        }
        league.TurnsUntilSession--;
        if (league.TurnsUntilSession == game.Ruleset.ModOptions.Constants.LeagueSessionSoonWarningTurns && league.TurnsUntilSession > 0)
            foreach (var civ in LivingMembers(game, league).Where(it => it.IsHuman()))
                civ.AddNotification($"The World Congress meets in {league.TurnsUntilSession} turns", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy);
        if (league.TurnsUntilSession <= 0) StartSession(game, league, special: false);
    }

    private static void TryFound(GameInfo game)
    {
        var majors = LivingMajors(game);
        if (majors.Count < 2) return;
        var host = majors.FirstOrDefault(it => MeetsFoundingCondition(game, it));
        if (host == null) return;
        var league = new League
        {
            Host = host.CivId,
            SessionIndex = SessionIndexForEra(game.Ruleset, GameEraNumber(game)),
            Members = majors.Select(it => new LeagueMember { Civ = it.CivId }).ToList(),
        };
        game.League = league;
        game.EventSink?.Emit(new LeagueFounded(host.CivId));
        foreach (var civ in majors.Where(it => it.IsHuman()))
            civ.AddNotification($"{host.CivName} has founded the World Congress!", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy);
        StartSession(game, league, special: true);
    }

    private static void EnsureHost(GameInfo game, League league)
    {
        if (league.Members.Count == 0 || game.GetCivilization(league.Host).IsAlive()) return;
        var top = ByDelegates(game, league).FirstOrDefault();
        if (top != null) league.Host = top.CivId;
    }

    // ---- sessions

    private static void StartSession(GameInfo game, League league, bool special)
    {
        var session = LeagueData.Sessions[league.SessionIndex];
        if (!special) LeagueAi.Propose(game, league);
        league.InSession = true;
        foreach (var member in league.Members)
            member.Delegates = game.GetCivilization(member.Civ).IsAlive() ? StartingVotes(game, league, game.GetCivilization(member.Civ)) : 0;
        if (special)
        {
            foreach (var name in new[] { session.Immediate, session.Recurring }.Where(it => it.Length > 0))
                league.Proposals.Add(new LeagueProposal { Id = league.NextId++, Resolution = name });
            game.EventSink?.Emit(new SpecialSessionStarted(session.Name, league.Host));
        }
        foreach (var civ in LivingMembers(game, league))
        {
            if (civ.IsHuman()) civ.AddNotification($"{session.Name}: the World Congress is in session", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy);
            else LeagueAi.Vote(game, league, civ);
        }
    }

    private static void EndSession(GameInfo game, League league)
    {
        foreach (var proposal in league.Proposals) Decide(game, league, proposal);
        league.Proposals.Clear();
        foreach (var member in league.Members)
        {
            member.Delegates = 0;
            member.ProposalsUsed = 0;
        }
        league.InSession = false;
        league.TurnsUntilSession = SessionInterval(game, league.SessionIndex);
    }

    /// <summary>The total of the votes for each choice of a proposal, in order of first vote.</summary>
    public static List<KeyValuePair<string, int>> Tally(LeagueProposal proposal)
    {
        var result = new List<KeyValuePair<string, int>>();
        foreach (var vote in proposal.Votes)
        {
            int index = result.FindIndex(it => it.Key == vote.Choice);
            if (index < 0) result.Add(new KeyValuePair<string, int>(vote.Choice, vote.Count));
            else result[index] = new KeyValuePair<string, int>(vote.Choice, unchecked(result[index].Value + vote.Count));
        }
        return result;
    }

    private static void Decide(GameInfo game, League league, LeagueProposal proposal)
    {
        var resolution = LeagueData.Find(proposal.Resolution).NotNull();
        var tally = Tally(proposal);
        int Count(string choice) => tally.FirstOrDefault(it => it.Key == choice).Value;
        string winner = "";
        bool passed;
        if (resolution.Voter == VoterDecision.YesNo)
            passed = Count(Yes) > Count(No);
        else
        {
            int best = tally.Select(it => it.Value).DefaultIfEmpty(0).Max();
            var leaders = tally.Where(it => it.Value == best).ToList();
            passed = best > 0 && leaders.Count == 1;
            if (passed) winner = leaders[0].Key;
        }

        // Seam (B018): a Diplomatic Victory vote needs the votes needed (4.7.6), ends the game and gives the failed vote's leaders LeadersVoteBonusOnFail delegates (AddExtraVotes)
        if (resolution.DiplomaticVictory) passed = false;
        else if (passed && proposal.IsRepeal) passed = league.Active.RemoveAll(it => it.Id == proposal.RepealId) > 0;
        else if (passed && resolution.ChangeLeagueHost) league.Host = winner;
        else if (passed && !proposal.IsRepeal)
            league.Active.Add(new ActiveResolution { Id = league.NextId++, Resolution = proposal.Resolution, Choice = proposal.Choice, Proposer = proposal.Proposer, Turn = game.Turns });

        string shownChoice = winner.Length > 0 ? winner : proposal.Choice;
        EngineEvent result = !passed ? new ResolutionFailed(proposal.Resolution, shownChoice)
            : proposal.IsRepeal ? new ResolutionRepealed(proposal.Resolution, proposal.RepealId)
            : new ResolutionEnacted(proposal.Resolution, shownChoice);
        game.EventSink?.Emit(result);
        foreach (var civ in LivingMembers(game, league).Where(it => it.IsHuman()))
            civ.AddNotification($"{proposal.Resolution}: {(passed ? (proposal.IsRepeal ? "repealed" : "passed") : "failed")}", NotificationCategory.Diplomacy, NotificationIcon.Diplomacy);
    }

    /// <summary>Gives a member extra delegates for the following sessions (the spec's member field <c>extra</c>).</summary>
    public static void AddExtraVotes(League league, string civId, int votes)
    {
        if (league.GetMember(civId) is { } member) member.ExtraVotes = unchecked(member.ExtraVotes + votes);
    }

    // ---- delegates (4.7.3)

    /// <summary>The delegates a civ gets in the current session (@1024e260).</summary>
    public static int StartingVotes(GameInfo game, League league, CivilizationObject civ)
    {
        var session = LeagueData.Sessions[league.SessionIndex];
        int votes = league.GetMember(civ.CivId)?.ExtraVotes ?? 0;
        votes = unchecked(votes + session.CivDelegates);
        if (league.Host == civ.CivId) votes = unchecked(votes + session.HostDelegates);
        int allies = game.Civilizations.Count(it => it.IsCityState && it.IsAlive() && it.AllyCiv == civ);
        votes = unchecked(votes + allies * session.CityStateDelegates);
        foreach (var active in league.Active)
        {
            var resolution = LeagueData.Find(active.Resolution).NotNull();
            if (resolution.VotesForFollowingReligion > 0 && game.Religions.TryGetValue(active.Choice, out var religion) && religion.FoundingCivName == civ.CivId)
                votes = unchecked(votes + resolution.VotesForFollowingReligion);
            if (resolution.VotesForFollowingIdeology > 0 && civ.Policies.Ideology == active.Choice)
                votes = unchecked(votes + resolution.VotesForFollowingIdeology);
        }
        votes = unchecked(votes + civ.GetMatchingUniques(UniqueType.WorldCongressDelegates).Sum(it => it.Params[0].ToInt()));
        // Seam (B020): + votes per diplomat (WorldCongressVotesPerDiplomat) for each of the civ's diplomats in a major capital
        return votes;
    }

    /// <summary>Members by delegates, most first, ties by civ order.</summary>
    private static List<CivilizationObject> ByDelegates(GameInfo game, League league) =>
        LivingMembers(game, league).Select((civ, order) => (civ, order, votes: StartingVotes(game, league, civ)))
            .OrderByDescending(it => it.votes).ThenBy(it => it.order).Select(it => it.civ).ToList();

    /// <summary>The members who may propose: the host and the member with the most delegates (the next one if that is the host).</summary>
    public static List<CivilizationObject> Proposers(GameInfo game, League league)
    {
        var result = new List<CivilizationObject>();
        var host = game.GetCivilization(league.Host);
        if (host.IsAlive()) result.Add(host);
        var top = ByDelegates(game, league).FirstOrDefault(it => it != host);
        if (top != null) result.Add(top);
        return result;
    }

    /// <summary>Delegates the civ can still commit in the open session.</summary>
    public static int RemainingVotes(League league, string civId)
    {
        int spent = league.Proposals.Sum(p => p.Votes.Where(v => v.Civ == civId).Sum(v => v.Count));
        return (league.GetMember(civId)?.Delegates ?? 0) - spent;
    }

    // ---- proposals (4.7.4 step 1)

    /// <summary>Why the civ cannot propose enacting <paramref name="resolutionName"/> with <paramref name="choice"/> now, or null.</summary>
    public static string? EnactProblem(GameInfo game, CivilizationObject civ, string resolutionName, string choice)
    {
        if (ProposerProblem(game, civ) is { } problem) return problem;
        var league = game.League.NotNull();
        var resolution = LeagueData.Find(resolutionName);
        if (resolution == null || resolution.NoProposalByPlayer) return $"{resolutionName} cannot be proposed";
        if (resolution.TechPrereq.Length > 0 && !LivingMembers(game, league).Any(it => it.Tech.IsResearched(resolution.TechPrereq)))
            return $"No member knows {resolution.TechPrereq} yet";
        if (ChoiceProblem(game, league, civ, resolution, choice) is { } bad) return bad;
        if (league.Active.Any(it => it.Resolution == resolutionName && it.Choice == choice)) return $"{resolutionName} is already in force";
        if (league.Proposals.Any(it => !it.IsRepeal && it.Resolution == resolutionName && it.Choice == choice)) return $"{resolutionName} is already proposed";
        return null;
    }

    /// <summary>Why the civ cannot propose repealing the active resolution <paramref name="activeId"/> now, or null.</summary>
    public static string? RepealProblem(GameInfo game, CivilizationObject civ, int activeId)
    {
        if (ProposerProblem(game, civ) is { } problem) return problem;
        var league = game.League.NotNull();
        if (league.Active.All(it => it.Id != activeId)) return "There is no such resolution in force";
        if (league.Proposals.Any(it => it.RepealId == activeId)) return "Its repeal is already proposed";
        return null;
    }

    /// <summary>May the civ make a proposal now (a proposer with a proposal left, no session open)?</summary>
    public static bool CanPropose(GameInfo game, CivilizationObject civ) => ProposerProblem(game, civ) == null;

    private static string? ProposerProblem(GameInfo game, CivilizationObject civ)
    {
        if (!IsEnabled(game.Ruleset) || game.League is not { } league) return "There is no World Congress";
        if (league.InSession) return "The World Congress is in session";
        if (!civ.IsAlive() || !Proposers(game, league).Contains(civ)) return "Only the host and the member with the most delegates can propose";
        return league.GetMember(civ.CivId).NotNull().ProposalsUsed >= game.Ruleset.ModOptions.Constants.LeagueMemberProposalsBase ? "You have no proposal left" : null;
    }

    private static string? ChoiceProblem(GameInfo game, League league, CivilizationObject proposer, LeagueResolution resolution, string choice)
    {
        switch (resolution.Proposer)
        {
            case ProposerDecision.None:
                return choice.Length == 0 ? null : $"{resolution.Name} takes no choice";
            case ProposerDecision.OtherMajorCiv:
                return LivingMembers(game, league).Any(it => it.CivId == choice && it != proposer) ? null : "Choose another civilization of the World Congress";
            case ProposerDecision.Luxury:
                return game.Ruleset.TileResources.TryGetValue(choice, out var resource) && resource.ResourceType == ResourceType.Luxury ? null : "Choose a luxury resource";
            case ProposerDecision.Religion:
                return game.Religions.TryGetValue(choice, out var religion) && religion.IsMajorReligion() ? null : "Choose a religion";
            default:
                return proposer.Policies.IdeologyBranches.Any(it => it.Name == choice) && LivingMembers(game, league).Any(it => it.Policies.Ideology != null)
                    ? null : "Choose an ideology that a member follows or that exists in the game";
        }
    }

    /// <summary>Adds a proposal to enact. The caller has checked <see cref="EnactProblem"/>.</summary>
    public static LeagueProposal Propose(GameInfo game, CivilizationObject civ, string resolutionName, string choice)
    {
        var league = game.League.NotNull();
        var proposal = new LeagueProposal { Id = league.NextId++, Resolution = resolutionName, Choice = choice, Proposer = civ.CivId };
        league.Proposals.Add(proposal);
        league.GetMember(civ.CivId).NotNull().ProposalsUsed++;
        game.EventSink?.Emit(new ResolutionProposed(proposal.Id, resolutionName, choice, civ.CivId, 0));
        return proposal;
    }

    /// <summary>Adds a proposal to repeal. The caller has checked <see cref="RepealProblem"/>.</summary>
    public static LeagueProposal ProposeRepeal(GameInfo game, CivilizationObject civ, int activeId)
    {
        var league = game.League.NotNull();
        var active = league.Active.First(it => it.Id == activeId);
        var proposal = new LeagueProposal { Id = league.NextId++, Resolution = active.Resolution, Choice = active.Choice, Proposer = civ.CivId, RepealId = activeId };
        league.Proposals.Add(proposal);
        league.GetMember(civ.CivId).NotNull().ProposalsUsed++;
        game.EventSink?.Emit(new ResolutionProposed(proposal.Id, proposal.Resolution, proposal.Choice, civ.CivId, activeId));
        return proposal;
    }

    // ---- voting (4.7.4 step 3)

    /// <summary>Why the civ cannot put <paramref name="count"/> delegates on <paramref name="choice"/> of proposal <paramref name="proposalId"/>, or null.</summary>
    public static string? VoteProblem(GameInfo game, CivilizationObject civ, int proposalId, string choice, int count)
    {
        if (!IsEnabled(game.Ruleset) || game.League is not { } league) return "There is no World Congress";
        if (!league.InSession) return "The World Congress is not in session";
        if (league.GetMember(civ.CivId) == null || !civ.IsAlive()) return "You are not a member of the World Congress";
        var proposal = league.Proposals.FirstOrDefault(it => it.Id == proposalId);
        if (proposal == null) return "There is no such proposal";
        if (count <= 0) return "Commit at least one delegate";
        if (count > RemainingVotes(league, civ.CivId)) return "You do not have that many delegates left";
        if (proposal.Votes.Any(it => it.Civ == civ.CivId && it.Choice != choice)) return "You already backed another choice of this proposal";
        var resolution = LeagueData.Find(proposal.Resolution).NotNull();
        bool valid = resolution.Voter == VoterDecision.YesNo ? choice is Yes or No : LivingMembers(game, league).Any(it => it.CivId == choice);
        return valid ? null : "That is not a choice of this proposal";
    }

    /// <summary>Commits delegates. The caller has checked <see cref="VoteProblem"/>.</summary>
    public static void CommitVotes(League league, string civId, int proposalId, string choice, int count)
    {
        var proposal = league.Proposals.First(it => it.Id == proposalId);
        var vote = proposal.Votes.FirstOrDefault(it => it.Civ == civId && it.Choice == choice);
        if (vote == null) proposal.Votes.Add(new LeagueVote { Civ = civId, Choice = choice, Count = count });
        else vote.Count += count;
    }

    // ---- effects other systems read

    /// <summary>
    /// The tourism percent a city gets from enacted resolutions (4.3.1, @1024bd30): World Religion gives the holy city of the chosen religion +50%.
    /// </summary>
    public static int HolyCityTourismModifier(CityObject city)
    {
        if (!city.IsHolyCity() || city.Civ.GameInfo is not GameInfo { League: { } league }) return 0;
        return league.Active.Where(it => city.IsHolyCityOf(it.Choice)).Sum(it => LeagueData.Find(it.Resolution).NotNull().HolyCityTourism);
    }
}

/// <summary>The (minimal) AI of the World Congress: deterministic from state, no randomness.</summary>
internal static class LeagueAi
{
    /// <summary>The AI proposers use their proposal on a resolution without a choice, picked by turn and civ index.</summary>
    public static void Propose(GameInfo game, League league)
    {
        foreach (var civ in LeagueRules.Proposers(game, league).Where(it => !it.IsHuman()))
        {
            var candidates = LeagueData.Resolutions
                .Where(it => it.Proposer == ProposerDecision.None && LeagueRules.EnactProblem(game, civ, it.Name, "") == null).ToList();
            if (candidates.Count == 0) continue;
            LeagueRules.Propose(game, civ, candidates[(game.Turns + game.Civilizations.IndexOf(civ)) % candidates.Count].Name, "");
        }
    }

    /// <summary>The AI backs its own proposals and those of friends, opposes those of enemies, and votes for itself in a choice of civs.</summary>
    public static void Vote(GameInfo game, League league, CivilizationObject civ)
    {
        var opinions = new List<(LeagueProposal Proposal, string Choice)>();
        foreach (var proposal in league.Proposals)
        {
            var resolution = LeagueData.Find(proposal.Resolution).NotNull();
            if (resolution.Voter == VoterDecision.MajorCiv) { opinions.Add((proposal, civ.CivId)); continue; }
            if (proposal.Proposer.Length == 0) continue;
            var proposer = game.GetCivilization(proposal.Proposer);
            if (proposer == civ || civ.GetDiplomacyManager(proposer)?.HasFlag(DiplomacyFlags.DeclarationOfFriendship) == true) opinions.Add((proposal, LeagueRules.Yes));
            else if (civ.IsAtWarWith(proposer)) opinions.Add((proposal, LeagueRules.No));
        }
        int pool = LeagueRules.RemainingVotes(league, civ.CivId);
        if (opinions.Count == 0 || pool <= 0) return;
        for (int i = 0; i < opinions.Count; i++)
        {
            int share = pool / opinions.Count + (i < pool % opinions.Count ? 1 : 0);
            if (share > 0) LeagueRules.CommitVotes(league, civ.CivId, opinions[i].Proposal.Id, opinions[i].Choice, share);
        }
    }
}
