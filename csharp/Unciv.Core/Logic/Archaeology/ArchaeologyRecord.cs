using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.Archaeology;

// Brave New World (ticket B010, spec id B040, bnw-spec/SPEC.md section 4.4). Not part of the Kotlin source.

/// <summary>The kind of event a historical record remembers (BNW <c>GreatWorkArtifactClasses</c>; the numbers are the data values).</summary>
public enum ArtifactClass
{
    BattleRanged = 1,
    BattleMelee = 2,
    BarbarianCamp = 3,
    AncientRuin = 4,
    RazedCity = 5,
    Writing = 6,
}

public static class ArtifactClassExtensions
{
    /// <summary>The <c>artifactClass</c> text of the artifacts of this class in <c>GreatWorks.json</c> (the generator title-cases the data type).</summary>
    public static string GreatWorkClassName(this ArtifactClass artifactClass) => artifactClass switch
    {
        ArtifactClass.BattleRanged => "Battle Ranged",
        ArtifactClass.BattleMelee => "Battle Melee",
        ArtifactClass.BarbarianCamp => "Barbarian Camp",
        ArtifactClass.AncientRuin => "Ancient Ruin",
        ArtifactClass.RazedCity => "Razed City",
        ArtifactClass.Writing => "Writing",
        _ => throw new ArgumentOutOfRangeException(nameof(artifactClass)),
    };
}

/// <summary>
/// What happened on a tile (spec 4.4.1 <c>{ArtifactClass, Era, Player1, Player2, GreatWork}</c>). A newer event overwrites an older one. A site
/// that hides a writing carries the name of that work in <see cref="GreatWork"/>.
/// </summary>
public class ArchaeologyRecord : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("artifactClass")]
    public ArtifactClass ArtifactClass { get; set; } = ArtifactClass.BattleMelee;

    /// <summary>Name of the era in which it happened.</summary>
    [JsonPropertyName("era")]
    public string Era { get; set; } = "";

    /// <summary>Civ id of the winner (killer, clearer, razer), empty when there is none.</summary>
    [JsonPropertyName("player1")]
    public string Player1 { get; set; } = "";

    /// <summary>Civ id of the loser, empty when there is none.</summary>
    [JsonPropertyName("player2")]
    public string Player2 { get; set; } = "";

    /// <summary>The hidden writing of a <see cref="ArtifactClass.Writing"/> site, else empty.</summary>
    [JsonPropertyName("greatWork")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string GreatWork { get; set; } = "";

    public ArchaeologyRecord Clone() => new() { ArtifactClass = ArtifactClass, Era = Era, Player1 = Player1, Player2 = Player2, GreatWork = GreatWork };
}

/// <summary>A finished dig of a human civ whose owner has not chosen yet (spec 4.4.3): the tile still holds the dig.</summary>
public class PendingDig : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("civ")]
    public string Civ { get; set; } = "";

    public PendingDig Clone() => new() { X = X, Y = Y, Civ = Civ };
}
