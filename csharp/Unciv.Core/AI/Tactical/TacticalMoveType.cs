namespace Unciv.AI.Tactical;

/// <summary>
/// Every tactical move of data/tactical_moves.tsv (SPEC.md section 6.2/6.4), in file order.
/// The TSV name is TACTICAL_ plus the upper-snake form of the member name ("1_TURN" is spelled OneTurn).
/// </summary>
public enum TacticalMoveType
{
    EmergencyPurchases,
    CaptureCity,
    DestroyHighUnit,
    AirSweep,
    AirIntercept,
    AttackVeryHighPriorityCivilian,
    PillageCitadel,
    DestroyMediumUnit,
    AttackEmbarkedMilitaryUnit,
    DestroyLowUnit,
    OffensivePostureMoves,
    PostureShoreBombardment,
    PlunderTradeUnit,
    AttackHighPriorityCivilian,
    PostureWithdraw,
    MoveOperations,
    AttackEmbarkedCivilian,
    SafeBombards,
    PostureHedgehog,
    CloseOnTarget,
    DefensiveAirlift,
    Pillage,
    AttackMediumPriorityCivilian,
    PillageResource,
    AttackLowPriorityCivilian,
    AncientRuins,
    PillageCitadelNextTurn,
    GarrisonToAllowBombard,
    AttritHighUnit,
    AttritMediumUnit,
    DamageCity,
    PillageResourceNextTurn,
    AttritLowUnit,
    ToSafety,
    BarbarianCamp,
    EscortEmbarkedUnit,
    PillageNextTurn,
    ParkOnTradeRoute,
    Heal,
    BastionAlreadyThere,
    GarrisonAlreadyThere,
    BastionOneTurn,
    GarrisonOneTurn,
    GuardImprovementAlreadyThere,
    GuardImprovementOneTurn,
    AirRebase,
    Reposition,
    MoveNoncombatantsToSafety,
    PostureCounterattack,
    PostureSteamroll,
    PostureSitAndBombard,
    Unassigned,
    PostureSurgicalCityStrike,
    PostureAttritFromRange,
    PostureExploitFlanks,
}

public static class TacticalMoveTypeExtensions
{
    private static readonly System.Collections.Frozen.FrozenDictionary<string, TacticalMoveType> ByName = BuildByName();

    private static System.Collections.Frozen.FrozenDictionary<string, TacticalMoveType> BuildByName()
    {
        var map = new Dictionary<string, TacticalMoveType>(StringComparer.Ordinal);   // lookup only, never iterated
        foreach (var t in Enum.GetValues<TacticalMoveType>())
            map[ToTsvName(t)] = t;
        return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(map, StringComparer.Ordinal);
    }

    /// <summary>The data-file name, e.g. TACTICAL_CAPTURE_CITY.</summary>
    public static string ToTsvName(this TacticalMoveType type)
    {
        var sb = new System.Text.StringBuilder("TACTICAL");
        string name = type.ToString();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) sb.Append('_');
            if (i == 0) sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString().Replace("_ONE_TURN", "_1_TURN", StringComparison.Ordinal);
    }

    public static TacticalMoveType Parse(string tsvName) =>
        ByName.TryGetValue(tsvName, out var t) ? t : throw new ArgumentException($"Unknown tactical move '{tsvName}'", nameof(tsvName));

    public static bool TryParse(string tsvName, out TacticalMoveType type) => ByName.TryGetValue(tsvName, out type);
}
