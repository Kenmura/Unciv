using Unciv.Net.Visibility;

namespace Unciv.Assets;

/// <summary>
/// C039: what became of the offers the viewer sent and of the proposals it received, as plain data. Each <see cref="Update"/> compares the snapshot with the previous
/// one: an offer of ours that is gone was accepted or declined (the engine tells the proposer in a notification), taken back by us, or expired; a proposal to us that is
/// gone without us answering was withdrawn. The lines are the offers still waiting for an answer followed by the latest outcomes, which fade after a turn.
/// </summary>
public sealed class OfferTracker
{
    private const int KeptOutcomes = 3;
    private const int OutcomeTurns = 2;

    private readonly record struct Outcome(int Turn, string Text, string? UnknownFor = null, string? UnknownName = null);

    private readonly List<(string Id, string Text)> outgoing = [];
    private readonly List<(string Id, string Name)> incoming = [];
    private readonly List<Outcome> outcomes = [];
    private readonly HashSet<string> answered = [];
    private readonly HashSet<string> retracted = [];

    /// <summary>The viewer answered or countered the proposal from this civ: its disappearing is not a withdrawal.</summary>
    public void NoteAnswered(string fromId) => answered.Add(fromId);

    /// <summary>The engine refused the viewer's answer: the proposal is still pending, so its later disappearing is a withdrawal after all.</summary>
    public void NoteRefused(string fromId) => answered.Remove(fromId);

    /// <summary>The viewer took back its offer to this civ.</summary>
    public void NoteRetracted(string toId)
    {
        retracted.Add(toId);
        // the event may arrive after the snapshot that no longer holds the offer: the outcome it was given then was a guess
        int guess = outcomes.FindLastIndex(it => it.UnknownFor == toId);
        if (guess < 0) return;
        outcomes[guess] = new Outcome(outcomes[guess].Turn, $"You took back your offer to {outcomes[guess].UnknownName}.");
        retracted.Remove(toId);
    }

    /// <summary>Forgets everything (a new game).</summary>
    public void Reset()
    {
        outgoing.Clear();
        incoming.Clear();
        outcomes.Clear();
        answered.Clear();
        retracted.Clear();
    }

    /// <summary>Compares the snapshot with the previous one and returns the lines to show: waiting offers first, then recent outcomes.</summary>
    public IReadOnlyList<string> Update(ClientSnapshot snapshot, Func<string, string, string?>? nameOf = null)
    {
        var names = snapshot.Civs.GroupBy(it => it.CivId, StringComparer.Ordinal).ToDictionary(it => it.Key, it => it.First().Name, StringComparer.Ordinal);
        var nowIncoming = TradeScreenModel.Incoming(snapshot, nameOf).Select(it => (it.FromId, it.FromName)).ToList();
        var nowOutgoing = snapshot.PendingTrades.Where(it => it.From == snapshot.You).Select(it => it.To).Distinct(StringComparer.Ordinal)
            .Select(it => (Id: it, Trade: TradeScreenModel.Outgoing(snapshot, it, nameOf)!)).ToList();

        foreach (var (id, text) in outgoing)
        {
            if (nowOutgoing.Any(it => it.Id == id)) continue;
            string name = names.GetValueOrDefault(id, id);
            bool unknown = false;
            string outcome = retracted.Contains(id) ? $"You took back your offer to {name}."
                : snapshot.Notifications.Any(it => NotificationsModel.Plain(it.Text) == $"{name} has accepted your trade request") ? $"{name} accepted your offer."
                : snapshot.Notifications.Any(it => NotificationsModel.Plain(it.Text) == $"{name} has denied your trade request") ? $"{name} declined your offer."
                : Unknown(name, out unknown);
            outcomes.Add(new Outcome(snapshot.Turn, outcome, unknown ? id : null, name));
        }
        foreach (var (id, name) in incoming)
            if (!nowIncoming.Any(it => it.FromId == id) && !answered.Contains(id))
                outcomes.Add(new Outcome(snapshot.Turn, $"{name} withdrew their offer."));

        retracted.RemoveWhere(id => nowOutgoing.All(it => it.Id != id));
        answered.RemoveWhere(id => nowIncoming.All(it => it.FromId != id));
        outgoing.Clear();
        outgoing.AddRange(nowOutgoing.Select(it => (it.Id, $"Waiting for {it.Trade.FromName}: you give {Join(it.Trade.Gives)}; you ask {Join(it.Trade.Asks)}")));
        incoming.Clear();
        incoming.AddRange(nowIncoming.Select(it => (it.FromId, it.FromName)));
        outcomes.RemoveAll(it => snapshot.Turn - it.Turn >= OutcomeTurns);
        if (outcomes.Count > KeptOutcomes) outcomes.RemoveRange(0, outcomes.Count - KeptOutcomes);
        return outgoing.Select(it => it.Text).Concat(outcomes.Select(it => it.Text)).ToList();
    }

    private static string Unknown(string name, out bool unknown)
    {
        unknown = true;
        return $"Your offer to {name} is no longer pending.";
    }

    private static string Join(IReadOnlyList<string> offers) => offers.Count == 0 ? "nothing" : string.Join(", ", offers);
}
