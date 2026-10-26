using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>
/// An incoming trade or peace proposal ready to draw. <see cref="Buttons"/> are Accept, Decline and Counter ("How about something else": the owner submits the
/// dismiss command and then opens the trade screen with the offer staged as a counter proposal); the popup may always be closed without answering, the proposal then
/// stays pending and the HUD offers to review it.
/// </summary>
public sealed record TradeProposal(IncomingTrade Trade, string Title, string Body, IReadOnlyList<AlertButton> Buttons)
{
    public string FromId => Trade.FromId;

    /// <summary>Identifies this proposal (who and what) for "decide later" bookkeeping; a changed proposal from the same civ is a new one.</summary>
    public string Key => Trade.FromId + ":" + Body;
}

/// <summary>
/// C039: the popup for a trade or peace proposal another civ (an AI or, in multiplayer, another human) sent the viewer, as plain data so it is unit-tested without
/// Godot. The proposals come from the viewer's own snapshot (<see cref="TradeScreenModel.Incoming"/>); the answers are the engine's commands, which validate again when sent.
/// </summary>
public static class TradeProposalModel
{
    public const string AcceptId = "Accept";
    public const string DeclineId = "Decline";
    public const string CounterId = "Counter";

    /// <summary>The popup for one proposal.</summary>
    public static TradeProposal Describe(IncomingTrade trade)
    {
        string title = trade.IsPeace ? $"{trade.FromName} proposes peace" : $"{trade.FromName} proposes a trade";
        string body = $"They give: {Join(trade.Gives)}\nThey ask: {Join(trade.Asks)}";
        AlertButton[] buttons =
        [
            new(AcceptId, "Sounds good!", trade.Accept),
            new(DeclineId, "Not this time.", trade.Decline),
            new(CounterId, "How about something else...", trade.Counter),
        ];
        return new TradeProposal(trade, title, body, buttons);
    }

    /// <summary>The first proposal that was not put off (<paramref name="deferred"/>, by <see cref="TradeProposal.Key"/>), in the snapshot's order, or null.</summary>
    public static TradeProposal? Next(ClientSnapshot snapshot, IReadOnlySet<string> deferred, Func<string, string, string?>? nameOf = null) =>
        TradeScreenModel.Incoming(snapshot, nameOf).Select(Describe).FirstOrDefault(it => !deferred.Contains(it.Key));

    /// <summary>How many proposals were put off and are still pending.</summary>
    public static int WaitingCount(ClientSnapshot snapshot, IReadOnlySet<string> deferred, Func<string, string, string?>? nameOf = null) =>
        TradeScreenModel.Incoming(snapshot, nameOf).Count(it => deferred.Contains(Describe(it).Key));

    private static string Join(IReadOnlyList<string> offers) => offers.Count == 0 ? "nothing" : string.Join(", ", offers);
}
