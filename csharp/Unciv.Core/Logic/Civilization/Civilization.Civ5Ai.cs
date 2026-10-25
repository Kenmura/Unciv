using System.Text.Json.Serialization;
using Unciv.AI;

namespace Unciv.Logic.Civilization;

public partial class Civilization
{
    /// <summary>T235: the Civ 5 AI's memory (grand strategy lock, running operations); null until the AI first acts, so other games are saved unchanged.</summary>
    [JsonPropertyName("civ5Ai")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Civ5AiState? Civ5Ai { get; set; }

    private Civilization CopyCiv5AiTo(Civilization clone)
    {
        clone.Civ5Ai = Civ5Ai?.Clone();
        return clone;
    }
}
