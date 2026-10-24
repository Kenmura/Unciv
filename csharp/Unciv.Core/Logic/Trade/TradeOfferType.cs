namespace Unciv.Logic.Trade;

// core/src/com/unciv/logic/trade/TradeOfferType.kt

/// <summary>How the value number of a trade offer is formatted (<c>TradeOfferType.TradeTypeNumberType</c>).</summary>
public enum TradeTypeNumberType
{
    None,
    Simple,
    Gold,
}

/// <summary>Enum that classifies Trade Types. The names are serialized, so the underscores stay.</summary>
#pragma warning disable CA1707 // JUSTIFY: T059 the Kotlin enum entries (serialized by name in save files) contain underscores
public enum TradeOfferType
{
    Embassy,
    Gold,
    Gold_Per_Turn,
    /// <summary>Treaties are shared by both sides - like peace treaty and defensive pact</summary>
    Treaty,
    /// <summary>Agreements are one-sided, like open borders</summary>
    Agreement,
    Luxury_Resource,
    Strategic_Resource,
    Stockpiled_Resource,
    Technology,
    Introduction,
    WarDeclaration,
    PeaceProposal,
    City,
}
#pragma warning restore CA1707

/// <summary>The two constructor parameters of Kotlin's <c>TradeOfferType</c> enum entries.</summary>
public static class TradeOfferTypeExtensions
{
    /// <summary>How the value number is formatted - None, Simple number or with a Gold symbol.</summary>
    public static TradeTypeNumberType NumberType(this TradeOfferType type) => type switch
    {
        TradeOfferType.Embassy => TradeTypeNumberType.None,
        TradeOfferType.Gold => TradeTypeNumberType.Gold,
        TradeOfferType.Gold_Per_Turn => TradeTypeNumberType.Gold,
        TradeOfferType.Treaty => TradeTypeNumberType.None,
        TradeOfferType.Agreement => TradeTypeNumberType.None,
        TradeOfferType.Luxury_Resource => TradeTypeNumberType.Simple,
        TradeOfferType.Strategic_Resource => TradeTypeNumberType.Simple,
        TradeOfferType.Stockpiled_Resource => TradeTypeNumberType.Simple,
        TradeOfferType.Technology => TradeTypeNumberType.None,
        TradeOfferType.Introduction => TradeTypeNumberType.None,
        TradeOfferType.WarDeclaration => TradeTypeNumberType.None,
        TradeOfferType.PeaceProposal => TradeTypeNumberType.None,
        TradeOfferType.City => TradeTypeNumberType.None,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Trade is a one-time effect without duration.</summary>
    public static bool IsImmediate(this TradeOfferType type) => type switch
    {
        TradeOfferType.Embassy => true,
        TradeOfferType.Gold => true,
        TradeOfferType.Gold_Per_Turn => false,
        TradeOfferType.Treaty => false,
        TradeOfferType.Agreement => false,
        TradeOfferType.Luxury_Resource => false,
        TradeOfferType.Strategic_Resource => false,
        TradeOfferType.Stockpiled_Resource => true,
        TradeOfferType.Technology => true,
        TradeOfferType.Introduction => true,
        TradeOfferType.WarDeclaration => true,
        TradeOfferType.PeaceProposal => true,
        TradeOfferType.City => true,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
