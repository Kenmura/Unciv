using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Utils;

namespace Unciv.Logic.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/BarbarianEncampment.kt (T078).

public class BarbarianEncampment() : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("position")]
    public HexCoord Position { get; set; } = new();
    [JsonPropertyName("countdown")]
    public int Countdown { get; set; }
    [JsonPropertyName("spawnedUnits")]
    public int SpawnedUnits { get; set; } = -1;
    /// <summary>destroyed encampments haunt the vicinity for 15 turns preventing new spawns</summary>
    [JsonPropertyName("destroyed")]
    public bool Destroyed { get; set; }

    private IGameInfoState? gameInfo;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <see cref="BarbarianManager.SetTransients"/>: reading before assignment throws.</summary>
    [JsonIgnore]
    public IGameInfoState GameInfo
    {
        get => gameInfo ?? throw new InvalidOperationException("lateinit property gameInfo has not been initialized");
        set => gameInfo = value;
    }

    public BarbarianEncampment(HexCoord position) : this() => Position = position;

    public BarbarianEncampment Clone() => new(Position)
    {
        Countdown = Countdown,
        SpawnedUnits = SpawnedUnits,
        Destroyed = Destroyed,
    };

    public void Update()
    {
        if (Countdown > 0) // Not yet
            Countdown--;
        // Countdown at 0, try to spawn a barbarian
        else if (!Destroyed && GameInfo.Barbarians.SpawnBarbarian(GameInfo.TileMap[Position]) != null)
        {
            // Successful
            SpawnedUnits++;
            ResetCountdown();
        }
    }

    public void WasAttacked()
    {
        if (!Destroyed)
            Countdown /= 2;
    }

    public void WasDestroyed()
    {
        if (!Destroyed)
        {
            Countdown = 15;
            Destroyed = true;
        }
    }

    /// <summary>When a barbarian is spawned, seed the counter for next spawn</summary>
    private void ResetCountdown()
    {
        var rng = GameInfo.GetBarbarianCivilization().State.StateBasedRandom("BarbarianManager.resetCooldown");
        // Base 8-12 turns
        Countdown = 8 + rng.NextInt(5);
        // Quicker on Raging Barbarians
        if (GameInfo.GameParameters.RagingBarbarians)
            Countdown /= 2;
        // Higher on low difficulties
        Countdown += GameInfo.Ruleset.Difficulties[GameInfo.GameParameters.Difficulty].BarbarianSpawnDelay;
        // Quicker if this camp has already spawned units
        Countdown -= Math.Min(SpawnedUnits, 3);

        Countdown = KotlinMath.FloatToInt(Countdown * GameInfo.Speed.BarbarianModifier);
    }
}
