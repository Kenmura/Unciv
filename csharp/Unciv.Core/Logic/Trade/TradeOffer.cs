using Unciv.Json;
using Unciv.Models.Ruleset;

namespace Unciv.Logic.Trade;

// core/src/com/unciv/logic/trade/TradeOffer.kt
// Kotlin data class: Equals(object)/GetHashCode are memberwise (name, type, amount, duration) like the generated equals;
// the extra overload Equals(TradeOffer) is Kotlin's `fun equals(offer: TradeOffer)` (it ignores the duration), which Kotlin
// picks statically for a TradeOffer argument - as C# does here. Deliberately NOT IEquatable, so List.Remove/Contains keep the
// data-class equality of the generated equals(Any?).
// Not ported: getOfferText (translation and Fonts, UI-side text; it also reads UncivGame.Current.gameInfo).

public class TradeOffer : IsPartOfGameInfoSerialization
{
    public string Name { get; init; }

    public TradeOfferType Type { get; init; }

    public int Amount { get; set; }

    public int Duration { get; set; }

    public TradeOffer(string name, TradeOfferType type, int amount = 1, int duration = -1)
    {
        Name = name;
        Type = type;
        Amount = amount;
        Duration = duration;
    }

    /// <summary>The offer's duration is derived from the game speed: <c>-1</c> for immediate offers, the peace or deal duration otherwise.</summary>
    public TradeOffer(string name, TradeOfferType type, int amount, Speed speed)
        : this(name, type, amount, -1)
    {
        Duration =
            type.IsImmediate() ? -1 // -1 for offers that are immediate (e.g. gold transfer)
            : name == Constants.PeaceTreaty || type == TradeOfferType.PeaceProposal ? speed.PeaceDealDuration
            : speed.DealDuration;
    }

    /// <summary>Kotlin <c>TradeOffer(name, type, speed = speed)</c>, whose <c>amount</c> keeps its default of 1.</summary>
    public TradeOffer(string name, TradeOfferType type, Speed speed) : this(name, type, 1, speed)
    {
    }

    /// <summary>So that the json deserializer can work.</summary>
    public TradeOffer() : this("", TradeOfferType.Gold, 1, -1)
    {
    }

    /// <summary>Kotlin <c>copy(name, type, amount, duration)</c>: omitted arguments keep this offer's values.</summary>
    public TradeOffer Copy(string? name = null, TradeOfferType? type = null, int? amount = null, int? duration = null) =>
        new(name ?? Name, type ?? Type, amount ?? Amount, duration ?? Duration);

    public bool Equals(TradeOffer offer) => offer.Name == Name && offer.Type == Type && offer.Amount == Amount;

    public bool IsTradable() => Amount > 0;

    public override bool Equals(object? obj) =>
        obj is TradeOffer other && other.Name == Name && other.Type == Type && other.Amount == Amount && other.Duration == Duration;

    public override int GetHashCode() => HashCode.Combine(Name, Type, Amount, Duration);

    public override string ToString() => $"TradeOffer(name={Name}, type={Type}, amount={Amount}, duration={Duration})";
}
