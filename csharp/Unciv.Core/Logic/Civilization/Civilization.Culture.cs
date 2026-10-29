using System.Text.Json.Serialization;
using Unciv.Logic.Civilization.Managers;

namespace Unciv.Logic.Civilization;

// Brave New World (ticket B008, bnw-spec/SPEC.md 2.2). Not part of the Kotlin source.
public partial class Civilization
{
    /// <summary>Influence over other civs and the tourism boost. Null until the first turn of a game with tourism, so G&amp;K saves are unchanged.</summary>
    [JsonPropertyName("culture")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CivCultureManager? Culture { get; set; }

    private Civilization CopyCultureTo(Civilization toReturn)
    {
        toReturn.Culture = Culture?.Clone();
        return toReturn;
    }
}
