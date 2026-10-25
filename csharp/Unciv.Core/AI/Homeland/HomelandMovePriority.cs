namespace Unciv.AI.Homeland;

/// <summary>
/// Every homeland (non-combat and idle unit) move that has an AI_HOMELAND_MOVE_PRIORITY_* define (SPEC.md section 7),
/// in data-file order. The define name is AI_HOMELAND_MOVE_PRIORITY_ plus the upper-snake form of the member name.
/// </summary>
public enum HomelandMovePriority
{
    AddSpaceshipPart,
    AdmiralGarrison,
    AircraftToTheFront,
    Airlift,
    AncientRuins,
    Archaeologist,
    Artist,
    ArtistGoldenAge,
    EngineerHurry,
    Explore,
    ExploreSea,
    Garrison,
    GarrisonCityState,
    GeneralGarrison,
    Heal,
    Inquisitor,
    Missionary,
    MobileReserve,
    Musician,
    Patrol,
    ProphetReligion,
    ScientistFreeTech,
    Sentry,
    Settle,
    SpaceshipPart,
    ToSafety,
    TradeUnit,
    Treasure,
    Upgrade,
    Worker,
    WorkerSea,
    Writer,
}

public static class HomelandMovePriorityExtensions
{
    private const string Prefix = "AI_HOMELAND_MOVE_PRIORITY_";

    /// <summary>The define name, e.g. AI_HOMELAND_MOVE_PRIORITY_TRADE_UNIT.</summary>
    public static string ToDefineName(this HomelandMovePriority move)
    {
        var sb = new System.Text.StringBuilder(Prefix);
        string name = move.ToString();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString();
    }
}
