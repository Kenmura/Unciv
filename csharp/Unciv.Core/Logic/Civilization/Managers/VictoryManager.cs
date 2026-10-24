using Unciv.Json;
using Unciv.Models;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Translations;
using Unciv.Utils;

namespace Unciv.Logic.Civilization.Managers;

// Ported from core/src/com/unciv/logic/civilization/managers/VictoryManager.kt (T057).
public class VictoryManager : IsPartOfGameInfoSerialization
{
    private Civilization? civInfo;
    /// <summary>Transient. Kotlin <c>lateinit var</c>: reading before assignment throws.</summary>
    public Civilization CivInfo
    {
        get => civInfo ?? throw new InvalidOperationException("lateinit property civInfo has not been initialized");
        set => civInfo = value;
    }

    // There is very likely a typo in this name (currents), but as its saved in save files,
    // fixing it is non-trivial
    public Counter<string> CurrentsSpaceshipParts { get; set; } = new();
    public bool HasEverWonDiplomaticVote { get; set; }

    public VictoryManager Clone()
    {
        var toReturn = new VictoryManager();
        toReturn.CurrentsSpaceshipParts.PutAll(CurrentsSpaceshipParts);
        toReturn.HasEverWonDiplomaticVote = HasEverWonDiplomaticVote;
        return toReturn;
    }

    private Counter<string> CalculateDiplomaticVotingResults(IEnumerable<KeyValuePair<string, string?>> votesCast)
    {
        var results = new Counter<string>();
        // UN Owner gets 2 votes in G&K
        var (_, civOwningUN) = GetUNBuildingAndOwnerNames();
        foreach (var (voter, votedFor) in votesCast)
        {
            if (votedFor == null) continue; // null means Abstained
            results.Add(votedFor, voter == civOwningUN ? 2 : 1);
        }
        return results;
    }

    private IEnumerable<Civilization> GetVotingCivs() => CivInfo.GameInfo.Civilizations
        .Where(it => !(it.IsBarbarian || it.IsSpectator() || it.IsDefeated()));

    /// <summary>
    /// Finds the Building and Owner of the United Nations (or whatever the Mod called it)
    /// - if it's built at all and only if the owner is alive
    /// </summary>
    /// <returns><c>Item1</c>: Building name, <c>Item2</c>: Owner civ name; both null if not found</returns>
    public (string?, string?) GetUNBuildingAndOwnerNames()
    {
        foreach (var civ in GetVotingCivs())
        {
            foreach (var city in civ.Cities)
            {
                foreach (var building in city.CityConstructions.GetBuiltBuildings())
                {
                    if (((IHasUniques)building).HasUnique(UniqueType.OneTimeTriggerVoting, GameContext.IgnoreConditionalsState))
                        return (building.Name, civ.CivId);
                }
            }
        }
        return (null, null);
    }

    private int VotesNeededForDiplomaticVictory()
    {
        // The original counts "teams ever alive", which excludes Observer and Barbarians.
        // The "ever alive" part sounds unfair - could make a Vote unwinnable?

        // So this is a slightly arbitrary decision: Apply original formula to count of available votes
        // - including catering for the possibility we're voting without a UN thanks to razing -
        var (_, civOwningUN) = GetUNBuildingAndOwnerNames();
        int voteCount = GetVotingCivs().Count() + (civOwningUN != null ? 1 : 0);

        // CvGame.cpp::DoUpdateDiploVictory() in the source code of the original - same integer math and rounding!
        // To verify run `(1..30).map { voteCount -> voteCount to voteCount * (67 - (1.1 * voteCount).toInt()) / 100 + 1 }`
        // ... and compare with: "4 votes needed to win in a game with 5 players, 7 with 13 and 11 with 28"
        if (voteCount > 28) return voteCount * 35 / 100;
        return voteCount * (67 - KotlinMath.DoubleToInt(1.1 * voteCount)) / 100 + 1;
    }

    public bool HasEnoughVotesForDiplomaticVictory()
    {
        var results = CalculateDiplomaticVotingResults(CivInfo.GameInfo.DiplomaticVictoryVotesCast);
        // maxByOrNull: the first entry with the maximal value
        KeyValuePair<string, int>? bestCiv = null;
        foreach (var entry in results)
            if (bestCiv == null || entry.Value > bestCiv.Value.Value)
                bestCiv = entry;
        if (bestCiv == null) return false;

        // If we don't have the highest score, we have not won anyway
        if (bestCiv.Value.Key != CivInfo.CivId) return false;

        // If we don't have enough votes, we haven't won
        if (bestCiv.Value.Value < VotesNeededForDiplomaticVictory()) return false;

        // If there's a tie, we haven't won either
        return !results.Any(it => !it.Equals(bestCiv.Value) && it.Value == bestCiv.Value.Value);
    }

    public record DiplomaticVictoryVoteBreakdown(Counter<string> Results, string WinnerText);

    public DiplomaticVictoryVoteBreakdown GetDiplomaticVictoryVoteBreakdown()
    {
        var results = CalculateDiplomaticVotingResults(CivInfo.GameInfo.DiplomaticVictoryVotesCast);
        var best = results
            .GroupBy(it => it.Value, it => it.Key)
            .OrderByDescending(it => it.Key) // key is vote count here
            .FirstOrDefault();
        if (best == null) return new DiplomaticVictoryVoteBreakdown(results, "No valid votes were cast.");
        int voteCount = best.Key;
        var winnerList = best.ToList();

        var lines = new List<string>();
        int minVotes = VotesNeededForDiplomaticVictory();
        if (voteCount < minVotes)
            lines.Add($"Minimum votes for electing a world leader: [{minVotes}]");
        if (winnerList.Count > 1)
            lines.Add($"Tied in first position: [{string.Join(", ", winnerList.Select(it => it.Tr()))}]"); // Yes with icons
        var winnerCiv = CivInfo.GameInfo.GetCivilization(winnerList[0]);
        if (lines.Count != 0) lines.Add("No world leader was elected.");
        else if (ReferenceEquals(winnerCiv, CivInfo)) lines.Add("You have been elected world leader!");
        else lines.Add($"{winnerCiv.Nation.GetLeaderDisplayName()} has been elected world leader!");
        return new DiplomaticVictoryVoteBreakdown(results, string.Join("\n", lines.Select(it => "{" + it + "}")));
    }

    /// <summary>
    /// The victories <see cref="CivInfo"/> can achieve in this game: enabled in the game parameters, and open to
    /// this civilization according to the victory's own uniques - see <see cref="IHasUniques.IsAvailable"/>.
    ///
    /// This is the single source that filters the game's victories for a civilization. Nothing else
    /// should iterate the ruleset's victories or the game parameters on a civilization's behalf.
    /// </summary>
    public List<Victory> GetAvailableVictories() => CivInfo.GameInfo.Ruleset.Victories.Values.Where(it =>
        it.Name != Constants.NeutralVictoryType
        && CivInfo.GameInfo.GameParameters.VictoryTypes.Contains(it.Name)
        && ((IHasUniques)it).IsAvailable(CivInfo.State)).ToList();

    /// <summary>
    /// <see cref="GetAvailableVictories"/> without the ones the ruleset keeps out of the victory screen.
    /// A <see cref="Victory.HiddenInVictoryScreen"/> victory can still be achieved - it is only not displayed.
    /// </summary>
    public List<Victory> GetVictoriesShownInVictoryScreen() =>
        GetAvailableVictories().Where(it => !it.HiddenInVictoryScreen).ToList();

    public string? GetVictoryTypeAchieved()
    {
        if (!CivInfo.IsMajorCiv()) return null;
        var victory = GetAvailableVictories().FirstOrDefault(it => GetNextMilestone(it) == null);
        if (victory != null) return victory.Name;
        if (CivInfo.HasUnique(UniqueType.TriggersVictory))
            return Constants.NeutralVictoryType;
        return null;
    }

    public Milestone? GetNextMilestone(Victory victory)
    {
        foreach (var milestone in victory.MilestoneObjects)
        {
            if (!milestone.HasBeenCompletedBy(CivInfo))
                return milestone;
        }
        return null;
    }

    public int AmountMilestonesCompleted(Victory victory)
    {
        int completed = 0;
        foreach (var milestone in victory.MilestoneObjects)
        {
            if (milestone.HasBeenCompletedBy(CivInfo))
                ++completed;
            else
                break;
        }
        return completed;
    }

    public bool HasWon() => GetVictoryTypeAchieved() != null;
}
