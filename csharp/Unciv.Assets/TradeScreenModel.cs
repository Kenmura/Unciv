using System.Globalization;
using Unciv.Commands.Diplomacy;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Net.Visibility;
using Unciv.Queries.Empire;

namespace Unciv.Assets;

/// <summary>Which column of the offer builder an offer is in: what the viewer gives, or what the viewer asks for.</summary>
public enum TradeSide
{
    Give,
    Ask,
}

/// <summary>One offer on a column: the engine's entry (<see cref="TradeOptionsResult"/>), whether it is staged now, and the amount staged.</summary>
public sealed record TradeChoice(TradeSide Side, TradeOfferEntry Entry, bool Staged, int StagedAmount)
{
    /// <summary>Stable node name part: side, offer type and offer name.</summary>
    public string Id => TradeScreenModel.KeyOf(Side, Entry.Type, Entry.Name);

    public bool Quantity => TradeScreenModel.IsQuantity(Entry.Type);

    public string Label => TradeScreenModel.ChoiceLabel(Entry, Staged, StagedAmount);
}

/// <summary>A titled run of choices (all gold offers, all luxuries, ...).</summary>
public sealed record TradeGroup(string Title, IReadOnlyList<TradeChoice> Choices);

/// <summary>A trade another civ sent the viewer (from the snapshot): what it gives and what it asks.</summary>
public sealed record IncomingTrade(string FromId, string FromName, IReadOnlyList<string> Gives, IReadOnlyList<string> Asks)
{
    public string Title => $"{FromName} proposes a trade";

    public RespondToTradeRequest Accept => new() { CivId = FromId, Accept = true };

    public RespondToTradeRequest Decline => new() { CivId = FromId, Accept = false };

    /// <summary>"How about something else": drops the request; the screen then stages it as a counter proposal.</summary>
    public DismissTradeRequest Counter => new() { CivId = FromId };
}

/// <summary>A demand another civ made of the viewer (an alert of the engine's pending decisions).</summary>
public sealed record IncomingDemand(string FromId, string FromName, string Demand, string Text)
{
    public string Title => $"{FromName} demands: {Text}";

    public RespondToDemand Accept => new() { CivId = FromId, Demand = Demand, Accept = true };

    public RespondToDemand Refuse => new() { CivId = FromId, Demand = Demand, Accept = false };
}

/// <summary>
/// C022: the offer builder behind the trade screen, as plain data so it is unit-tested without Godot. It holds the engine's list of what each side could offer
/// (<see cref="TradeOptionsResult"/>, the same legality <see cref="ProposeTrade"/> validates with) and the viewer's staged choice; it decides no legality of
/// its own: only an offer the engine listed as tradable can be staged, amounts stay within what the engine says is available, and the command it builds is
/// validated by the engine again when sent.
/// </summary>
public sealed class TradeBuilder
{
    private readonly record struct Staged(TradeSide Side, string Name, string Type, int Amount);

    private readonly List<Staged> staged = [];

    public TradeBuilder(string partnerId, TradeOptionsResult options)
    {
        PartnerId = partnerId;
        Options = options;
    }

    public string PartnerId { get; }

    public TradeOptionsResult Options { get; }

    public bool IsEmpty => staged.Count == 0;

    private IReadOnlyList<TradeOfferEntry> ListOf(TradeSide side) => side == TradeSide.Give ? Options.Ours : Options.Theirs;

    private TradeOfferEntry? Find(TradeSide side, string type, string name) => ListOf(side).FirstOrDefault(it => it.Type == type && it.Name == name);

    private int IndexOf(TradeSide side, string type, string name) => staged.FindIndex(it => it.Side == side && it.Type == type && it.Name == name);

    /// <summary>Every offer of both columns grouped by kind in <see cref="TradeScreenModel.GroupOrder"/>, each kept in the engine's order; empty groups are left out.</summary>
    public IReadOnlyList<TradeGroup> Groups(TradeSide side)
    {
        var groups = new List<TradeGroup>();
        foreach (var (title, types) in TradeScreenModel.GroupOrder)
        {
            var choices = ListOf(side).Where(it => types.Contains(it.Type)).Select(it => ChoiceOf(side, it)).ToList();
            if (choices.Count > 0) groups.Add(new TradeGroup(title, choices));
        }
        return groups;
    }

    private TradeChoice ChoiceOf(TradeSide side, TradeOfferEntry entry)
    {
        int at = IndexOf(side, entry.Type, entry.Name);
        return new TradeChoice(side, entry, at >= 0, at >= 0 ? staged[at].Amount : 0);
    }

    /// <summary>The staged offers of one column, in the order they were staged.</summary>
    public IReadOnlyList<TradeChoice> StagedChoices(TradeSide side) =>
        staged.Where(it => it.Side == side).Select(it => ChoiceOf(side, Find(side, it.Type, it.Name)!)).ToList();

    /// <summary>
    /// Stages the offer, or takes it off when it is staged already. Refused (false) for an offer the engine lists as not tradable, or one that is not on the column.
    /// A quantity starts at <see cref="TradeScreenModel.StepOf"/> (at most what is available); a treaty sits on one column only (the engine mirrors it to both sides when the proposal is sent).
    /// </summary>
    public bool Toggle(TradeSide side, string type, string name)
    {
        var entry = Find(side, type, name);
        if (entry == null || !entry.Tradable) return false;
        int at = IndexOf(side, type, name);
        if (at >= 0)
        {
            staged.RemoveAt(at);
            return true;
        }
        if (type == nameof(Unciv.Logic.Trade.TradeOfferType.Treaty))
        {
            var other = side == TradeSide.Give ? TradeSide.Ask : TradeSide.Give;
            staged.RemoveAll(it => it.Side == other && it.Type == type && it.Name == name);
        }
        staged.Add(new Staged(side, name, type, TradeScreenModel.IsQuantity(type) ? Math.Min(entry.Amount, TradeScreenModel.StepOf(type)) : entry.Amount));
        return true;
    }

    /// <summary>Raises or lowers a staged quantity by <paramref name="steps"/> steps, kept between 1 and what the engine says is available. False when it is not a staged quantity.</summary>
    public bool Adjust(TradeSide side, string type, string name, int steps)
    {
        int at = IndexOf(side, type, name);
        var entry = Find(side, type, name);
        if (at < 0 || entry == null || !TradeScreenModel.IsQuantity(type)) return false;
        long wanted = staged[at].Amount + (long)steps * TradeScreenModel.StepOf(type);
        staged[at] = staged[at] with { Amount = (int)Math.Clamp(wanted, 1L, Math.Max(1, entry.Amount)) };
        return true;
    }

    /// <summary>Takes every staged offer off.</summary>
    public void Clear() => staged.Clear();

    /// <summary>
    /// Stages the viewer's side of a trade another civ proposed, the counter proposal of "How about something else": what the partner asked of us goes on our column,
    /// what it offered on theirs. Offers the engine no longer lists as tradable are left out and counted.
    /// </summary>
    /// <returns>How many of the offers could not be staged.</returns>
    public int StageRequest(PendingTradeDto request)
    {
        Clear();
        int skipped = 0;
        void Add(TradeSide side, TradeOfferDto offer)
        {
            var entry = Find(side, offer.Type, offer.Name);
            if (entry == null || !entry.Tradable) { skipped++; return; }
            if (IndexOf(side, offer.Type, offer.Name) >= 0) return;
            if (offer.Type == nameof(Unciv.Logic.Trade.TradeOfferType.Treaty) && IndexOf(side == TradeSide.Give ? TradeSide.Ask : TradeSide.Give, offer.Type, offer.Name) >= 0) return;
            int amount = TradeScreenModel.IsQuantity(offer.Type) ? Math.Clamp(offer.Amount, 1, Math.Max(1, entry.Amount)) : entry.Amount;
            staged.Add(new Staged(side, offer.Name, offer.Type, amount));
        }
        foreach (var offer in request.Requested) Add(TradeSide.Give, offer);
        foreach (var offer in request.Offered) Add(TradeSide.Ask, offer);
        return skipped;
    }

    /// <summary>The propose command for what is staged , or null when nothing is staged.</summary>
    public ProposeTrade? Command()
    {
        if (staged.Count == 0) return null;
        IReadOnlyList<TradeOfferSpec> Specs(TradeSide side) =>
            staged.Where(it => it.Side == side)
                .Select(it => new TradeOfferSpec(it.Name, it.Type, it.Amount)).ToList();
        return new ProposeTrade { CivId = PartnerId, OurOffers = Specs(TradeSide.Give), TheirOffers = Specs(TradeSide.Ask) };
    }

    /// <summary>The hint query's offers for one column (the same staged offers and amounts the command would send).</summary>
    public IReadOnlyList<TradeOfferRequest> HintRequests(TradeSide side) =>
        staged.Where(it => it.Side == side).Select(it => new TradeOfferRequest(it.Name, it.Type, it.Amount)).ToList();

    /// <summary>The staged trade in words, one line per column.</summary>
    public IReadOnlyList<string> Summary()
    {
        string Side(TradeSide side)
        {
            var parts = StagedChoices(side).Select(it => TradeScreenModel.OfferText(it.Entry, it.StagedAmount)).ToList();
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
        return [$"You give: {Side(TradeSide.Give)}", $"You ask: {Side(TradeSide.Ask)}"];
    }
}

/// <summary>
/// C022: the trade screen's words and routing as plain data. The partner list is the major civs the viewer has met (a city-state has no trade table), incoming
/// trade proposals come from the viewer's own snapshot, demands from the engine's pending decisions, and the AI's likely answer from <see cref="TradeHintQuery"/>
/// (which the engine only answers when the game enables hints).
/// </summary>
public static class TradeScreenModel
{
    /// <summary>The offer types in column order, each group with its heading.</summary>
    public static readonly IReadOnlyList<(string Title, string[] Types)> GroupOrder =
    [
        ("Gold", ["Gold"]),
        ("Gold per turn", ["Gold_Per_Turn"]),
        ("Luxury resources", ["Luxury_Resource"]),
        ("Strategic resources", ["Strategic_Resource", "Stockpiled_Resource"]),
        ("Technologies", ["Technology"]),
        ("Cities", ["City"]),
        ("Treaties and agreements", ["Treaty", "Agreement", "Embassy"]),
        ("Introductions", ["Introduction"]),
        ("War declarations", ["WarDeclaration"]),
        ("Peace proposals", ["PeaceProposal"]),
    ];

    /// <summary>Quantities have an amount the player sets; every other offer is all or nothing.</summary>
    public static bool IsQuantity(string type) => type is "Gold" or "Gold_Per_Turn" or "Luxury_Resource" or "Strategic_Resource" or "Stockpiled_Resource";

    /// <summary>How much one press of plus or minus moves a quantity: gold in tens, anything else by one.</summary>
    public static int StepOf(string type) => type == "Gold" ? 10 : 1;

    public static string KeyOf(TradeSide side, string type, string name)
    {
        var chars = (side + "_" + type + "_" + name).Select(it => char.IsLetterOrDigit(it) ? it : '_');
        return string.Concat(chars);
    }

    /// <summary>The civs one can trade with: major civs the viewer has met.</summary>
    public static IReadOnlyList<DiplomacyRow> Partners(ClientSnapshot snapshot) => DiplomacyScreenModel.Rows(snapshot).Where(it => !it.IsCityState).ToList();

    public static string NameOf(TradeOfferEntry entry) => entry.DisplayName ?? entry.Name;

    /// <summary>"Gold 50", "Iron x2", "Open Borders"; a duration is added for offers that last.</summary>
    public static string OfferText(TradeOfferEntry entry, int amount) => OfferText(NameOf(entry), entry.Type, amount, entry.Duration);

    public static string OfferText(string name, string type, int amount, int duration)
    {
        string text = type switch
        {
            "Gold" => $"{amount.ToString(CultureInfo.InvariantCulture)} gold",
            "Gold_Per_Turn" => $"{amount.ToString(CultureInfo.InvariantCulture)} gold per turn",
            "Luxury_Resource" or "Strategic_Resource" or "Stockpiled_Resource" => $"{name} x{amount.ToString(CultureInfo.InvariantCulture)}",
            "Introduction" => "Introduction to " + name,
            "WarDeclaration" => "War on " + name,
            "PeaceProposal" => "Peace with " + name,
            "Technology" => "Technology: " + name,
            "City" => "City: " + name,
            _ => name,
        };
        return duration > 0 && type != "Gold" ? $"{text} ({duration.ToString(CultureInfo.InvariantCulture)} turns)" : text;
    }

    /// <summary>The label of one offer button: the offer, what is available, and a check mark when staged.</summary>
    public static string ChoiceLabel(TradeOfferEntry entry, bool staged, int stagedAmount)
    {
        if (!entry.Tradable) return OfferText(NameOf(entry), entry.Type, 0, -1) + " (not possible)";
        string text = OfferText(entry, entry.Amount);
        return staged ? $"[x] {OfferText(entry, stagedAmount)}" : text;
    }

    /// <summary>The incoming trade proposals addressed to the viewer, in snapshot order. <paramref name="nameOf"/> turns an offer's id into a name when it knows one.</summary>
    public static IReadOnlyList<IncomingTrade> Incoming(ClientSnapshot snapshot, Func<string, string, string?>? nameOf = null)
    {
        var names = snapshot.Civs.GroupBy(it => it.CivId, StringComparer.Ordinal).ToDictionary(it => it.Key, it => it.First().Name, StringComparer.Ordinal);
        string Text(TradeOfferDto offer) => OfferText(nameOf?.Invoke(offer.Type, offer.Name) ?? NameIfCiv(names, offer), offer.Type, offer.Amount, offer.Duration);
        return snapshot.PendingTrades.Where(it => it.To == snapshot.You)
            .Select(it => new IncomingTrade(it.From, names.GetValueOrDefault(it.From, it.From), it.Offered.Select(Text).ToList(), it.Requested.Select(Text).ToList()))
            .ToList();
    }

    private static string NameIfCiv(Dictionary<string, string> names, TradeOfferDto offer) =>
        offer.Type is "Introduction" or "WarDeclaration" or "PeaceProposal" ? names.GetValueOrDefault(offer.Name, offer.Name) : offer.Name;

    /// <summary>The trade proposal the viewer sent to this civ and the engine has not answered yet, in words; null when there is none.</summary>
    public static IncomingTrade? Outgoing(ClientSnapshot snapshot, string partnerId, Func<string, string, string?>? nameOf = null)
    {
        var names = snapshot.Civs.GroupBy(it => it.CivId, StringComparer.Ordinal).ToDictionary(it => it.Key, it => it.First().Name, StringComparer.Ordinal);
        string Text(TradeOfferDto offer) => OfferText(nameOf?.Invoke(offer.Type, offer.Name) ?? NameIfCiv(names, offer), offer.Type, offer.Amount, offer.Duration);
        var pending = snapshot.PendingTrades.FirstOrDefault(it => it.From == snapshot.You && it.To == partnerId);
        // the proposal is stored from the receiver's side: Offered is what we give, Requested what we ask
        return pending == null ? null : new IncomingTrade(partnerId, names.GetValueOrDefault(partnerId, partnerId), pending.Offered.Select(Text).ToList(), pending.Requested.Select(Text).ToList());
    }

    /// <summary>The demands made of the viewer that it can still answer, from the pending decisions.</summary>
    public static IReadOnlyList<IncomingDemand> Demands(PendingDecisionsResult pending, ClientSnapshot snapshot)
    {
        var result = new List<IncomingDemand>();
        foreach (var decision in pending.Decisions)
        {
            if (decision.Alert is not { AckOnly: false } alert) continue;
            if (!Enum.TryParse<AlertType>(alert.Type, out var type)) continue;
            var demand = Demand.Values.FirstOrDefault(it => it.DemandAlert == type);
            if (demand == null) continue;
            string from = snapshot.Civs.FirstOrDefault(it => it.CivId == alert.Value)?.Name ?? alert.Value;
            result.Add(new IncomingDemand(alert.Value, from, demand.Name, DemandWords(demand.Name)));
        }
        return result;
    }

    private static string DemandWords(string demand) => demand switch
    {
        "DontSpyOnUs" => "stop spying on us",
        "DoNotSpreadReligion" => "stop spreading your religion to us",
        "DoNotSettleNearUs" => "stop settling cities near us",
        "DoNotAttackUs" => "stop attacking us",
        _ => demand,
    };

    /// <summary>What the AI is likely to answer, from <see cref="TradeHintResult.Hint"/>; empty when the engine gave no hint.</summary>
    public static string HintText(TradeHintResult? hint) => hint?.Hint switch
    {
        "accept" => "They would likely accept.",
        "counter-needed" => "They want more; adding gold from your treasury could do.",
        "refuse" => "They would refuse.",
        _ => "",
    };
}
