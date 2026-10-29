using System.Text.Json.Serialization;
using Unciv.Logic.Map;

namespace Unciv.Logic;

/// <summary>What a route carries (bnw-spec 4.1.1). A domestic route is Food or Production.</summary>
public enum TradeRouteKind
{
    International = 0,
    Food = 1,
    Production = 2,
}

/// <summary>The domain of the trade unit that walks the route.</summary>
public enum TradeDomain
{
    Land = 0,
    Sea = 1,
}

/// <summary>Index of each stat in <see cref="TradeRoute.OriginYields"/> and <see cref="TradeRoute.DestYields"/>.</summary>
public static class TradeYield
{
    public const int Food = 0;
    public const int Production = 1;
    public const int Gold = 2;
    public const int Science = 3;
    public const int Culture = 4;
    public const int Faith = 5;
    public const int Count = 6;
}

/// <summary>
/// One active trade route (bnw-spec 4.1.1, B020). Not in Kotlin. Lives in <see cref="GameInfo.TradeRoutes"/>, in creation order.
/// <para>
/// Deliberate deviation (B020): the trade unit is consumed when the route is established and a new unit of the same name is created
/// in the origin city when the route completes, instead of keeping an invisible unit on the civ's unit list. <see cref="UnitId"/> is the
/// id the consumed unit had (informational), <see cref="UnitName"/> what comes back. The unit's position is
/// <c>Path[UnitPathIndex]</c>.
/// </para>
/// All yields are hundredths (Times100), in the order of <see cref="TradeYield"/>.
/// </summary>
public sealed class TradeRoute
{
    /// <summary>Monotonic, never reused.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("originCityId")]
    public string OriginCityId { get; set; } = "";

    [JsonPropertyName("destCityId")]
    public string DestCityId { get; set; } = "";

    [JsonPropertyName("origin")]
    public HexCoord Origin { get; set; }

    [JsonPropertyName("dest")]
    public HexCoord Dest { get; set; }

    [JsonPropertyName("originOwner")]
    public string OriginOwner { get; set; } = "";

    [JsonPropertyName("destOwner")]
    public string DestOwner { get; set; } = "";

    [JsonPropertyName("domain")]
    public TradeDomain Domain { get; set; }

    [JsonPropertyName("kind")]
    public TradeRouteKind Kind { get; set; }

    [JsonPropertyName("unitId")]
    public int UnitId { get; set; }

    [JsonPropertyName("unitName")]
    public string UnitName { get; set; } = "";

    /// <summary>From the origin city's center to the destination city's center, both included.</summary>
    [JsonPropertyName("path")]
    public List<HexCoord> Path { get; set; } = [];

    [JsonPropertyName("unitPathIndex")]
    public int UnitPathIndex { get; set; }

    [JsonPropertyName("movingForward")]
    public bool MovingForward { get; set; } = true;

    [JsonPropertyName("circuitsCompleted")]
    public int CircuitsCompleted { get; set; }

    [JsonPropertyName("circuitsToComplete")]
    public int CircuitsToComplete { get; set; }

    /// <summary>The game turn the route is due to end (informational: the circuits decide).</summary>
    [JsonPropertyName("turnRouteComplete")]
    public int TurnRouteComplete { get; set; }

    [JsonPropertyName("originYields")]
    public List<int> OriginYields { get; set; } = NoYields();

    [JsonPropertyName("destYields")]
    public List<int> DestYields { get; set; } = NoYields();

    [JsonIgnore]
    public bool IsInternational => Kind == TradeRouteKind.International;

    [JsonIgnore]
    public HexCoord CurrentPosition => Path.Count == 0 ? Origin : Path[Math.Clamp(UnitPathIndex, 0, Path.Count - 1)];

    public static List<int> NoYields() => [0, 0, 0, 0, 0, 0];

    public TradeRoute Clone() => new()
    {
        Id = Id,
        OriginCityId = OriginCityId,
        DestCityId = DestCityId,
        Origin = Origin,
        Dest = Dest,
        OriginOwner = OriginOwner,
        DestOwner = DestOwner,
        Domain = Domain,
        Kind = Kind,
        UnitId = UnitId,
        UnitName = UnitName,
        Path = [.. Path],
        UnitPathIndex = UnitPathIndex,
        MovingForward = MovingForward,
        CircuitsCompleted = CircuitsCompleted,
        CircuitsToComplete = CircuitsToComplete,
        TurnRouteComplete = TurnRouteComplete,
        OriginYields = [.. OriginYields],
        DestYields = [.. DestYields],
    };
}

/// <summary>
/// A route that ended (completed, plundered or broken). Civs remember them for <see cref="Trade.Routes.TradeRouteRules.RouteMemoryTurns"/> turns
/// (bnw-spec 4.1.6 item 5, @101f7e40); the AI scorer of a later ticket reads them.
/// </summary>
public sealed record EndedTradeRoute(
    [property: JsonPropertyName("originCityId")] string OriginCityId,
    [property: JsonPropertyName("destCityId")] string DestCityId,
    [property: JsonPropertyName("originOwner")] string OriginOwner,
    [property: JsonPropertyName("turn")] int Turn,
    [property: JsonPropertyName("plundered")] bool Plundered);
