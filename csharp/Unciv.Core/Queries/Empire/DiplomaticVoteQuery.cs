using Unciv.View;

namespace Unciv.Queries.Empire;

// CanVote: VoteDiplomatic would accept a vote now
// Candidates: The civs that can be voted for (the known civs, as DiplomaticVotePickerScreen lists them); not voting (abstaining) is always possible
public sealed record DiplomaticVoteResult(bool CanVote, IReadOnlyList<string> Candidates);

// T220: the World Congress vote picker.
[QueryType("empire.DiplomaticVote")]
public sealed record DiplomaticVoteQuery : IQuery<DiplomaticVoteResult>
{
    public string Type => "empire.DiplomaticVote";

    public DiplomaticVoteResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        if (!civ.MayVoteForDiplomaticVictory()) return new DiplomaticVoteResult(false, []);
        return new DiplomaticVoteResult(true, civ.DiplomacyFunctions.GetKnownCivsSorted(false).Select(it => it.CivId).ToList());
    }
}
