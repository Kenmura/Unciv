using Unciv.Logic;
using Unciv.Logic.WorldCongress;
using Unciv.View;

namespace Unciv.Queries.Empire;

// CivId: The member
// Delegates: Delegates it has in the open session, or would have now between sessions
// Left: Delegates not yet committed in the open session; only the viewer's own row (ballots are secret until the session is decided)
public sealed record LeagueMemberRow(string CivId, int Delegates, int Left);

// Id: The id the repeal command takes
public sealed record LeagueActiveRow(int Id, string Resolution, string Choice, string Proposer, int Turn);

// RepealId: Non-zero for a proposal to repeal that active resolution
// Tally: Delegates per choice ("yes", "no" or a civ id), all members once decided; only the viewer's own while the session is open
// YourVotes: The viewer's own delegates per choice
public sealed record LeagueProposalRow(int Id, string Resolution, string Choice, string Proposer, int RepealId, IReadOnlyList<KeyValuePair<string, int>> Tally,
    IReadOnlyList<KeyValuePair<string, int>> YourVotes);

// Name: The project; Cost: The production it needs now; Total: What all members have put in; Yours: The viewer's own contribution
public sealed record LeagueProjectRow(string Name, int Cost, int Total, int Yours);

// SessionName: The special session level the league is at
// TurnsUntilSession: Rounds until the next session opens (0 while one is open)
// CanPropose: The viewer is a proposer with a proposal left and no session is open
// Proposable: Resolutions without a choice that the viewer can propose now (the ones that need a choice are checked by the command)
public sealed record LeagueResult(bool Founded, string Host, string SessionName, bool InSession, int TurnsUntilSession, bool CanPropose,
    IReadOnlyList<LeagueMemberRow> Members, IReadOnlyList<LeagueActiveRow> Active, IReadOnlyList<LeagueProposalRow> Proposals, IReadOnlyList<string> Proposable,
    IReadOnlyList<LeagueProjectRow>? Projects = null);

// B015 (bnw-spec 2.4): the World Congress as the viewer sees it. It is public, so nothing is redacted.
[QueryType("empire.League")]
public sealed record LeagueQuery : IQuery<LeagueResult>
{
    public string Type => "empire.League";

    public LeagueResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        if (civ.GameInfo is not GameInfo game || !LeagueRules.IsEnabled(game.Ruleset) || civ.IsSpectator())
            throw new QueryException(EmpireQueryCodes.NotAvailable, "There is no World Congress in this game");
        if (game.League is not { } league) return new LeagueResult(false, "", "", false, 0, false, [], [], [], []);

        var members = league.Members.Select(it =>
        {
            var member = game.GetCivilization(it.Civ);
            int delegates = !member.IsAlive() ? 0 : league.InSession ? it.Delegates : LeagueRules.StartingVotes(game, league, member);
            return new LeagueMemberRow(it.Civ, delegates, league.InSession && it.Civ == civ.CivId ? LeagueRules.RemainingVotes(league, it.Civ) : 0);
        }).ToList();
        var proposals = league.Proposals.Select(p => new LeagueProposalRow(p.Id, p.Resolution, p.Choice, p.Proposer, p.RepealId, league.InSession ? LeagueRules.Tally(new LeagueProposal { Votes = p.Votes.Where(v => v.Civ == civ.CivId).ToList() }) : LeagueRules.Tally(p),
            LeagueRules.Tally(new LeagueProposal { Votes = p.Votes.Where(v => v.Civ == civ.CivId).ToList() }))).ToList();
        var proposable = LeagueData.Resolutions
            .Where(it => it.Proposer == ProposerDecision.None && LeagueRules.EnactProblem(game, civ, it.Name, "") == null).Select(it => it.Name).ToList();
        return new LeagueResult(true, league.Host, LeagueData.Sessions[league.SessionIndex].Name, league.InSession, league.InSession ? 0 : league.TurnsUntilSession,
            LeagueRules.CanPropose(game, civ), members, league.Active.Select(it => new LeagueActiveRow(it.Id, it.Resolution, it.Choice, it.Proposer, it.Turn)).ToList(), proposals, proposable,
            LeagueEffects.EnabledProjects(game).Distinct().Select(name =>
                new LeagueProjectRow(name, LeagueProjects.Cost(game, league, name), LeagueProjects.Find(league, name)?.Total ?? 0, LeagueProjects.Find(league, name)?.ContributionOf(civ.CivId) ?? 0)).ToList());
    }
}
