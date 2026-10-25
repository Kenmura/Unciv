using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;
using NationObject = Unciv.Models.Ruleset.Nation.Nation;
using RulesetObject = Unciv.Models.Ruleset.Ruleset;

namespace Unciv.Models.Metadata;

// core/src/com/unciv/models/metadata/Player.kt
//
public class Player : IIsPartOfGameInfoSerialization
{
    [JsonPropertyName("chosenCiv")]
    public string ChosenCiv { get; set; } = Constants.Random;

    [JsonPropertyName("playerType")]
    public PlayerType PlayerType { get; set; } = PlayerType.AI;

    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = "";

    private NationObject? chosenNation;

    /// <summary>Kotlin <c>@Transient lateinit var</c>: reading before assignment throws.</summary>
    [JsonIgnore]
    public NationObject ChosenNation
    {
        get => chosenNation ?? throw new InvalidOperationException("lateinit property chosenNation has not been initialized");
        set => chosenNation = value;
    }

    public Player()
    {
    }

    public Player(NationObject chosenNation, PlayerType playerType = PlayerType.AI, string playerId = "")
        : this(chosenNation.Name, playerType, playerId)
    {
        ChosenNation = chosenNation;
    }

    /// <summary>T219: an independent copy (GameParameters.Clone used to share the Player objects with its source).</summary>
    public Player Clone() => new(ChosenCiv, PlayerType, PlayerId) { chosenNation = chosenNation };

    public void SetNationTransient(RulesetObject ruleset) => ChosenNation = ruleset.Nations[ChosenCiv];

    public Player(string chosenCiv = Constants.Random, PlayerType playerType = PlayerType.AI, string playerId = "")
    {
        ChosenCiv = chosenCiv;
        PlayerType = playerType;
        PlayerId = playerId;
    }
}
