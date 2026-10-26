using Unciv.Queries.Empire;

namespace Unciv.Assets;

/// <summary>
/// One civ's line of the World Congress result. <see cref="Received"/> is the vote count a major civ got ("" for a city-state), <see cref="Weight"/> the translation key
/// of what the civ's own vote is worth, <see cref="UnBuilding"/> the United Nations building (only for its owner), <see cref="Choice"/> "Abstained" or "Voted for" and
/// <see cref="Target"/> the civ it voted for (null when abstained, or when the engine does not name it to this viewer).
/// </summary>
public sealed record VoteResultRow(string Civ, string Received, string Weight, string? UnBuilding, string Choice, string? Target);

/// <summary>What the screen shows: <see cref="Verdict"/> is the engine's winner text (translated with <c>Tr()</c>, it has its own braces).</summary>
public sealed record VoteResultView(string Title, string Verdict, IReadOnlyList<VoteResultRow> Rows, string ContinueLabel);

/// <summary>
/// C053: the World Congress vote result (Kotlin <c>DiplomaticVoteResultScreen</c>) as plain data, Godot-free so it is unit-tested. Everything comes from the engine's
/// <see cref="DiplomaticVoteBreakdownResult"/> (viewer-filtered there); this class only words it. The Continue button sends <c>AckDiplomaticVoteResult</c>.
/// </summary>
public static class VoteResultModel
{
    public const string Title = "World Congress";

    public static VoteResultView? Build(DiplomaticVoteBreakdownResult? result)
    {
        if (result is not { Show: true }) return null;
        var rows = result.Rows.Select(it => new VoteResultRow(
            it.CivName,
            it.IsMajor ? it.Votes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
            it.Weight == 1 ? "[1] vote" : $"[{it.Weight}] votes",
            it.CivName == result.UnOwner ? result.UnBuilding : null,
            it.Abstained ? "Abstained" : "Voted for",
            it.Abstained ? null : it.VotedFor ?? DiplomaticVoteResultQuery.UnknownCiv)).ToList();
        return new VoteResultView(Title, result.WinnerText, rows, "Continue");
    }
}
