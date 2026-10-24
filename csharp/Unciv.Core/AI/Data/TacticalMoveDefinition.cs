using System.Collections.Immutable;
using System.Globalization;

namespace Unciv.AI.Data;

/// <summary>
/// One row of data/tactical_moves.tsv: a tactical move type, its queue priority and the flavor weights that scale it.
/// A blank flavor weight is null (no weight). Rows keep file order, which is descending priority with ties as shipped.
/// </summary>
public sealed record TacticalMoveDefinition(
    string Type,
    int Priority,
    int? OffenseFlavorWeight,
    int? DefenseFlavorWeight,
    bool OperationsCanRecruit,
    bool DominanceZoneMove,
    bool InBase,
    bool InGk,
    bool InBnw)
{
    private static readonly ImmutableArray<string> Header =
        ["Type", "Priority", "OffenseFlavorWeight", "DefenseFlavorWeight", "OperationsCanRecruit", "DominanceZoneMove", "in_base", "in_gk", "in_bnw"];

    public bool IsInEdition(AIEdition edition) => edition switch
    {
        AIEdition.Base => InBase,
        AIEdition.GK => InGk,
        AIEdition.BNW => InBnw,
        _ => throw new ArgumentOutOfRangeException(nameof(edition)),
    };

    public static IReadOnlyList<TacticalMoveDefinition> Parse(string tsv)
    {
        var result = new List<TacticalMoveDefinition>();
        var seen = new HashSet<string>();   // membership only, never iterated
        foreach (string[] c in TsvTable.Rows(tsv, "tactical_moves", Header.AsSpan()))
        {
            if (!seen.Add(c[0])) throw new FormatException($"tactical_moves: duplicate move '{c[0]}'");
            result.Add(new TacticalMoveDefinition(
                c[0], ParseInt(c[0], c[1]), ParseOptionalInt(c[0], c[2]), ParseOptionalInt(c[0], c[3]),
                ParseBool(c[0], c[4]), ParseBool(c[0], c[5]), ParseBool(c[0], c[6]), ParseBool(c[0], c[7]), ParseBool(c[0], c[8])));
        }
        return result;
    }

    public static IReadOnlyList<TacticalMoveDefinition> LoadEmbedded() => Parse(TsvTable.ReadResource("tactical_moves.tsv"));

    private static int ParseInt(string move, string cell) =>
        int.TryParse(cell, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)
            ? v : throw new FormatException($"tactical_moves: '{move}' has non-integer value '{cell}'");

    private static int? ParseOptionalInt(string move, string cell) => cell.Length == 0 ? null : ParseInt(move, cell);

    // The extractor writes Python booleans ("True"/"False") and lower-case ones; a blank cell is false
    private static bool ParseBool(string move, string cell) => cell switch
    {
        "True" or "true" => true,
        "False" or "false" or "" => false,
        _ => throw new FormatException($"tactical_moves: '{move}' has non-boolean value '{cell}'"),
    };
}
