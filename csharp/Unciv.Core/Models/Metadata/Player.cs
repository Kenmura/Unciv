using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Civilization;

namespace Unciv.Models.Metadata;

// core/src/com/unciv/models/metadata/Player.kt
//
// Not ported yet: the transient chosenNation and setNationTransient(ruleset), and the Nation constructor
// (TODO(T104): Nation and Ruleset are ported, the Player wiring is left to the final sweep or GameStarter T099).
public class Player : IIsPartOfGameInfoSerialization
{
    [JsonPropertyName("chosenCiv")]
    public string ChosenCiv { get; set; } = Constants.Random;

    [JsonPropertyName("playerType")]
    public PlayerType PlayerType { get; set; } = PlayerType.AI;

    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = "";

    public Player()
    {
    }

    public Player(string chosenCiv = Constants.Random, PlayerType playerType = PlayerType.AI, string playerId = "")
    {
        ChosenCiv = chosenCiv;
        PlayerType = playerType;
        PlayerId = playerId;
    }
}
