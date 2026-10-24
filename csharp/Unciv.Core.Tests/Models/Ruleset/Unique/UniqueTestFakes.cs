using Unciv.Logic.Map;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.StatTypes;
using UniqueObject = Unciv.Models.Ruleset.Unique.Unique;

namespace Unciv.Core.Tests.Models.Ruleset.Unique;

/// <summary>Minimal implementations of the game-state interfaces <see cref="GameContext"/> reads, standing in for the not yet ported classes.</summary>
internal sealed class FakeGameInfo : IGameContextGameInfo
{
    public int Turns { get; init; }
    public string? GameId { get; init; }
    public long? MapSeed { get; init; }
}

internal sealed class FakeCiv(string civId, IGameContextGameInfo? gameInfo = null) : IGameContextCivilization, ILocalUniqueCacheCivilization
{
    public IGameContextGameInfo? GameInfo { get; } = gameInfo;
    public string CivId { get; } = civId;
    public Dictionary<string, int> Resources { get; } = new();
    public Dictionary<Stat, int> StatReserves { get; } = new();
    public int GetResourceAmount(string resourceName) => Resources.GetValueOrDefault(resourceName);
    public int GetStatReserve(Stat stat) => StatReserves.GetValueOrDefault(stat);

    // ILocalUniqueCacheCivilization
    public string CivName => CivId;
    public GameContext State => GameContext.ForCiv(this);
    public int GetMatchingUniquesCalls { get; private set; } // evaluations
    public List<UniqueObject> CivUniques { get; } = new();
    public IEnumerable<UniqueObject> GetMatchingUniques(UniqueType uniqueType, GameContext gameContext)
    {
        GetMatchingUniquesCalls++; // counted when the sequence is iterated, like the work Kotlin's lazy sequence does
        foreach (var unique in CivUniques.Where(it => it.Type == uniqueType)) yield return unique;
    }
}

internal sealed class FakeTile(HexCoord position) : IGameContextTile
{
    public HexCoord Position { get; } = position;
    public IGameContextGameInfo? GameInfo { get; init; }
    public IGameContextCity? City { get; set; }
    public List<FakeTile> NeighborTiles { get; } = new();
    public HashSet<string> Matching { get; } = new();
    public IGameContextCity? GetCity() => City;
    public IEnumerable<IGameContextTile> Neighbors => NeighborTiles;
    public bool MatchesFilter(string filter) => Matching.Contains(filter);
}

internal sealed class FakeCity(string id, FakeCiv civ, FakeTile? center = null) : IGameContextCity, ILocalUniqueCacheCity
{
    public string Id { get; } = id;
    public IGameContextCivilization Civ { get; } = civ;
    public Dictionary<string, int> Resources { get; } = new();
    public Dictionary<Stat, int> StatReserves { get; } = new();
    public IGameContextTile? GetCenterTileOrNull() => center;
    public int GetAvailableResourceAmount(string resourceName) => Resources.GetValueOrDefault(resourceName);
    public int GetStatReserve(Stat stat) => StatReserves.GetValueOrDefault(stat);

    // ILocalUniqueCacheCity
    ILocalUniqueCacheCivilization ILocalUniqueCacheCity.Civ => civ;
    public GameContext State => GameContext.ForCity(this);
    public int GetLocalMatchingUniquesCalls { get; private set; }
    public List<UniqueObject> LocalUniques { get; } = new();
    public IEnumerable<UniqueObject> GetLocalMatchingUniques(UniqueType uniqueType, GameContext gameContext)
    {
        GetLocalMatchingUniquesCalls++; // counted when the sequence is iterated, like the work Kotlin's lazy sequence does
        foreach (var unique in LocalUniques.Where(it => it.Type == uniqueType)) yield return unique;
    }
}

internal sealed class FakeUnit(string name, float currentMovement, FakeCiv civ, FakeTile? tile = null) : IGameContextUnit
{
    public string Name { get; } = name;
    public IGameContextCivilization Civ { get; } = civ;
    public float CurrentMovement { get; } = currentMovement;
    public bool HasTile() => tile != null;
    public IGameContextTile GetTile() => tile ?? throw new InvalidOperationException("lateinit property currentTile has not been initialized");
}

/// <summary>A MapUnitCombatant (<see cref="Unit"/> set) or a CityCombatant (<see cref="City"/> set).</summary>
internal sealed class FakeCombatant(FakeCiv civ, FakeTile tile, string name, FakeUnit? unit = null, FakeCity? city = null) : IGameContextCombatant
{
    public IGameContextCivilization GetCivInfo() => civ;
    public IGameContextTile GetTile() => tile;
    public string GetName() => name;
    public IGameContextUnit? Unit { get; } = unit;
    public IGameContextCity? City { get; } = city;
}
