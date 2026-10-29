using System.Text.Json.Serialization;
using Unciv.Json;

namespace Unciv.Logic.GreatWorks;

// Brave New World (ticket B007, spec id B030, bnw-spec/SPEC.md section 4.2). Not part of the Kotlin source.

/// <summary>
/// A Great Work that exists in the game (spec 2.2 <c>GameInfo.GreatWorks</c>). Every named work exists at most once per game, so the
/// <see cref="Name"/> identifies it, and a destroyed work stays in the list: a work that was created never comes back.
/// </summary>
public class GreatWorkInstance : IsPartOfGameInfoSerialization
{
    /// <summary>Name of the <see cref="Unciv.Models.Ruleset.GreatWork"/> in the ruleset.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Civ id of the creator, empty when no civ made it (a work found by other means).</summary>
    [JsonPropertyName("creator")]
    public string Creator { get; set; } = "";

    /// <summary>Era name: the era of the work when its data names one, else the creator's era at the moment of creation.</summary>
    [JsonPropertyName("era")]
    public string Era { get; set; } = "";

    /// <summary>The game turn on which the work was created.</summary>
    [JsonPropertyName("turn")]
    public int Turn { get; set; }

    public GreatWorkInstance Clone() => new() { Name = Name, Creator = Creator, Era = Era, Turn = Turn };
}

/// <summary>
/// A civ's standing offer to swap one of its works (spec 4.2.3). A civ has at most one offer per class (Art, Artifact, Writing).
/// An offer is only honoured while the civ still holds the work in a city of its own.
/// </summary>
public class GreatWorkSwapOffer : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("civ")]
    public string Civ { get; set; } = "";

    [JsonPropertyName("work")]
    public string Work { get; set; } = "";

    public GreatWorkSwapOffer Clone() => new() { Civ = Civ, Work = Work };
}

/// <summary>The works held by the slots of one building of a city, by slot index. A null entry is an empty slot.</summary>
public class BuildingGreatWorks : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("building")]
    public string Building { get; set; } = "";

    [JsonPropertyName("works")]
    public List<string?> Works { get; set; } = [];

    public BuildingGreatWorks Clone() => new() { Building = Building, Works = [.. Works] };
}
