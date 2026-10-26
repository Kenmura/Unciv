using Unciv.Logic.Civilization;
using Unciv.View;

namespace Unciv.Queries.Empire;

// CivName: The voter
// IsMajor: A major civ (city-states are listed without a vote count)
// Votes: The votes this civ received (counting the United Nations owner's vote twice), for a major civ
// Weight: How many votes this civ casts (2 for the owner of the United Nations, else 1)
// Abstained: It cast no valid vote (it abstained, or voted for a civ that is defeated)
// VotedFor: The civ it voted for, when that is the viewer or a civ the viewer has met
// VotedForUnknown: It voted for a civ the viewer has not met (not named)
public sealed record VoteRow(string CivName, bool IsMajor, int Votes, int Weight, bool Abstained, string? VotedFor, bool VotedForUnknown);

// Show: DiplomaticVoteResultScreen is due (Civilization.ShouldShowDiplomaticVotingResults); when false, everything else is empty
// UnBuilding, UnOwner: The United Nations (or the mod's equivalent) and its owner, only when the owner is the viewer or a civ the viewer has met
// WinnerText: The engine's verdict (translatable, in braces per line), with civs the viewer has not met left unnamed
// Rows: The viewer first, then the civs the viewer has met, in the order GameInfo.GetCivsSorted gives
public sealed record DiplomaticVoteBreakdownResult(bool Show, string? UnBuilding, string? UnOwner, string WinnerText, IReadOnlyList<VoteRow> Rows);

// C053: the World Congress result screen (DiplomaticVoteResultScreen). Kotlin lists every civ; here a civ the viewer has not met is not listed and is not
// named as a vote target or as the winner, so unmet civs do not leak through the votes (vote counts for listed civs are as in Kotlin).
[QueryType("empire.DiplomaticVoteResult")]
public sealed record DiplomaticVoteResultQuery : IQuery<DiplomaticVoteBreakdownResult>
{
    public const string UnknownCiv = "an unknown civilization";

    public string Type => "empire.DiplomaticVoteResult";

    public DiplomaticVoteBreakdownResult Run(GameView view)
    {
        var civ = view.CivView.Unwrap();
        if (!civ.ShouldShowDiplomaticVotingResults()) return new DiplomaticVoteBreakdownResult(false, null, null, "", []);
        var game = civ.GameInfo;
        bool Visible(Civilization other) => other == civ || civ.Knows(other);

        var (unBuilding, unOwnerId) = civ.VictoryManager.GetUNBuildingAndOwnerNames();
        var owner = unBuilding == null || unOwnerId == null ? null : game.GetCivilizationOrNull(unOwnerId);
        if (owner != null && !Visible(owner)) owner = null;
        var (results, winnerText) = civ.VictoryManager.GetDiplomaticVictoryVoteBreakdown();
        var votesCast = game.DiplomaticVictoryVotesCast;

        var rows = new List<VoteRow>();
        foreach (var other in game.GetCivsSorted(civToSortFirst: civ).Where(Visible))
        {
            int weight = other == owner ? 2 : 1;
            var votedFor = votesCast.TryGetValue(other.CivId, out var votedId) && votedId != null ? game.GetCivilization(votedId) : null;
            bool abstained = votedFor == null || votedFor.IsDefeated();
            bool known = !abstained && Visible(votedFor!);
            rows.Add(new VoteRow(other.CivName, other.IsMajorCiv(), other.IsMajorCiv() ? results[other.CivId] : 0, weight, abstained,
                known ? votedFor!.CivName : null, !abstained && !known));
        }
        return new DiplomaticVoteBreakdownResult(true, owner == null ? null : unBuilding, owner?.CivName, Anonymize(civ, winnerText), rows);
    }

    private static string Anonymize(Civilization viewer, string text)
    {
        foreach (var other in viewer.GameInfo.Civilizations)
        {
            if (other == viewer || viewer.Knows(other) || other.IsBarbarian || other.IsSpectator()) continue;
            text = text.Replace(other.Nation.GetLeaderDisplayName(), UnknownCiv, StringComparison.Ordinal)
                .Replace(other.CivName, UnknownCiv, StringComparison.Ordinal);
        }
        return text;
    }
}
