using System.Text.Json.Serialization;
using Unciv.Models.StatTypes;

namespace Unciv.Models.Ruleset;

// Brave New World (ticket B007, spec id B030, bnw-spec/SPEC.md section 4.2). Not part of the Kotlin source.

/// <summary>The class of a Great Work (BNW <c>GreatWorkClasses</c>). Literature is named <c>Writing</c>, as the building slot is.</summary>
public enum GreatWorkClass
{
    Art,
    Artifact,
    Writing,
    Music,
}

/// <summary>The kind of Great Work a building slot takes (BNW <c>GreatWorkSlots</c>). Art and Artifact share one slot type.</summary>
public enum GreatWorkSlotType
{
    [JsonStringEnumMemberName("Art/Artifact")]
    ArtArtifact,
    Writing,
    Music,
}

public static class GreatWorkClassExtensions
{
    /// <summary>The slot type that takes works of this class (BNW <c>GreatWorkClasses.SlotType</c>).</summary>
    public static GreatWorkSlotType SlotType(this GreatWorkClass greatWorkClass) => greatWorkClass switch
    {
        GreatWorkClass.Art or GreatWorkClass.Artifact => GreatWorkSlotType.ArtArtifact,
        GreatWorkClass.Writing => GreatWorkSlotType.Writing,
        GreatWorkClass.Music => GreatWorkSlotType.Music,
        _ => throw new ArgumentOutOfRangeException(nameof(greatWorkClass)),
    };
}

/// <summary>
/// A named Great Work of the ruleset (<c>GreatWorks.json</c>; BNW table <c>GreatWorks</c>). Each named work exists once per game.
/// An empty <see cref="Era"/> means "the era of the civilization that creates it" (BNW leaves it empty for the generic artifacts).
/// </summary>
public class GreatWork : INamed
{
    public string Name { get; set; } = "";

    public GreatWorkClass GreatWorkClass { get; set; } = GreatWorkClass.Art;

    /// <summary>Name of an era of the ruleset, or empty.</summary>
    public string Era { get; set; } = "";

    /// <summary>Artifact class (BNW <c>GreatWorkArtifactClasses</c>, for archaeology, ticket B040). Free text until then.</summary>
    public string ArtifactClass { get; set; } = "";

    /// <summary>The hidden writing finds: only archaeology creates them, never a Great Person.</summary>
    public bool ArchaeologyOnly { get; set; }
}

/// <summary>
/// One row of a building's <c>themingBonuses</c> (BNW <c>Building_ThemingBonuses</c>, spec 4.2.4). A building that has all slots filled
/// gets the <see cref="Bonus"/> of the first row (in data order) whose conditions all hold.
/// </summary>
public class ThemingBonus
{
    public int Bonus { get; set; }

    /// <summary>All works are from one era.</summary>
    public bool SameEra { get; set; }

    /// <summary>All works are from different eras.</summary>
    public bool UniqueEras { get; set; }

    public bool MustBeArt { get; set; }
    public bool MustBeArtifact { get; set; }

    /// <summary>Half of the works are art and half are artifacts.</summary>
    public bool MustBeEqualArtArtifact { get; set; }

    /// <summary>All works were created by the city's owner.</summary>
    public bool RequiresOwner { get; set; }

    /// <summary>No work was created by the city's owner.</summary>
    public bool RequiresAnyButOwner { get; set; }

    /// <summary>All works were created by one civ.</summary>
    public bool RequiresSamePlayer { get; set; }

    /// <summary>Each work was created by a different civ.</summary>
    public bool RequiresUniquePlayers { get; set; }
}
