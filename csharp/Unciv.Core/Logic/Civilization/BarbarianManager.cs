using System.Text.Json.Serialization;
using Unciv.Json;
using Unciv.Logic.Map;
using Unciv.Logic.Map.MapUnits;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset.Unique;
using Unciv.Models.Ruleset.Unit;
using Unciv.Utils;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;

namespace Unciv.Logic.Civilization;

// Ported from core/src/com/unciv/logic/automation/civilization/BarbarianManager.kt (T078).
// Stripped: the purity annotations.
// Deviation: placeBarbarianEncampment seeds its RNG with encampments.hashCode(), and BarbarianEncampment has no hashCode in Kotlin, so the JVM
// uses identity hashes and the stream differs from run to run. Here the list hash is java.util.ArrayList.hashCode with each element's hash
// being the hash of its position, which makes the draw reproducible (an empty list hashes to 1 in both).

public class BarbarianManager : IsPartOfGameInfoSerialization
{
    [JsonPropertyName("encampments")]
    public List<BarbarianEncampment> Encampments { get; set; } = [];

    private IGameInfoState? gameInfo;
    /// <summary>Kotlin <c>@Transient lateinit var</c>, set by <see cref="SetTransients"/>: reading before assignment throws.</summary>
    [JsonIgnore]
    public IGameInfoState GameInfo
    {
        get => gameInfo ?? throw new InvalidOperationException("lateinit property gameInfo has not been initialized");
        set => gameInfo = value;
    }

    private TileMap? tileMap;
    [JsonIgnore]
    public TileMap TileMap
    {
        get => tileMap ?? throw new InvalidOperationException("lateinit property tileMap has not been initialized");
        set => tileMap = value;
    }

    public BarbarianManager Clone()
    {
        var toReturn = new BarbarianManager();
        toReturn.Encampments.AddRange(Encampments.Select(it => it.Clone()));
        return toReturn;
    }

    public void SetTransients(IGameInfoState gameInfo)
    {
        GameInfo = gameInfo;
        TileMap = gameInfo.TileMap;

        // Add any preexisting camps as Encampment objects

        var existingEncampmentLocations = Encampments.Select(it => it.Position).ToHashSet();

        foreach (var tile in TileMap.Values)
        {
            if (tile.IsBarbarianEncampment() && !existingEncampmentLocations.Contains(tile.Position))
                Encampments.Add(new BarbarianEncampment(tile.Position));
        }

        foreach (var camp in Encampments)
            camp.GameInfo = gameInfo;
    }

    public void UpdateEncampments()
    {
        // Check if camps were destroyed
        foreach (var encampment in Encampments.ToList()) // tolist to avoid concurrent modification
        {
            if (!TileMap[encampment.Position].IsBarbarianEncampment())
                encampment.WasDestroyed();
            // Check if the ghosts are ready to depart
            if (encampment.Destroyed && encampment.Countdown == 0)
                Encampments.Remove(encampment);
        }

        // Possibly place a new encampment
        PlaceBarbarianEncampment();

        foreach (var encampment in Encampments) encampment.Update();
    }

    /// <summary>Called when an encampment was attacked, will speed up time to next spawn</summary>
    public void CampAttacked(HexCoord position) =>
        Encampments.FirstOrDefault(it => it.Position == position)?.WasAttacked();

    /// <summary>java.util.ArrayList.hashCode with the deterministic element hash described in the file header.</summary>
    private int EncampmentsHashCode()
    {
        var hash = 1;
        foreach (var encampment in Encampments) hash = unchecked(31 * hash + encampment.Position.GetHashCode());
        return hash;
    }

    public void PlaceBarbarianEncampment(bool forTesting = false)
    {
        var rng = GameInfo.GetBarbarianCivilization().State.StateBasedRandom("BarbarianManager.placeBarbarianEncampent", EncampmentsHashCode());
        // Before we do the expensive stuff, do a roll to see if we will place a camp at all
        if (!forTesting && GameInfo.Turns > 1 && rng.NextBoolean())
            return;

        // Barbarians will only spawn in places that no one can see
        var allViewableTiles = GameInfo.Civilizations.Where(it => !it.IsBarbarian && !it.IsSpectator())
            .SelectMany(it => it.ViewableTiles).ToHashSet();
        var fogTiles = TileMap.Values.Where(it => it.IsLand && !allViewableTiles.Contains(it)).ToList();

        var fogTilesPerCamp = KotlinMath.FloatToInt((float)Math.Pow(TileMap.Values.Count, 0.4f)); // Approximately

        // Check if we have more room
        var campsToAdd = (fogTiles.Count / fogTilesPerCamp) - Encampments.Count(it => !it.Destroyed);

        // First turn of the game add 1/3 of all possible camps
        if (GameInfo.Turns == 1)
        {
            campsToAdd /= 3;
            campsToAdd = Math.Max(campsToAdd, 1); // At least 1 on first turn
        }
        else if (campsToAdd > 0)
            campsToAdd = 1;

        if (campsToAdd <= 0) return;

        // Camps can't spawn within 7 tiles of each other or within 4 tiles of major civ capitals
        var tooCloseToCapitals = GameInfo.Civilizations
            .Where(it => !it.IsBarbarian && !it.IsSpectator() && it.Cities.Count != 0 && !it.IsCityState && it.GetCapital() != null)
            .SelectMany(it => it.GetCapital().NotNull().GetCenterTile().GetTilesInDistance(4)).ToHashSet();
        var tooCloseToCamps = Encampments
            .SelectMany(it => TileMap[it.Position].GetTilesInDistance(it.Destroyed ? 4 : 7)).ToHashSet();

        var viableTiles = fogTiles.Where(it =>
            !it.IsImpassible()
            && it.Resource == null
            && it.TerrainFeatureObjects.All(feature => !((IHasUniques)feature).HasUnique(UniqueType.RestrictedBuildableImprovements))
            && it.Neighbors.Any(neighbor => neighbor.IsLand)
            && !tooCloseToCapitals.Contains(it)
            && !tooCloseToCamps.Contains(it)).ToList();

        var addedCamps = 0;
        var biasCoast = rng.NextInt(6) == 0;

        // Add the camps
        while (addedCamps < campsToAdd)
        {
            if (viableTiles.Count == 0)
                break;

            // If we're biasing for coast, get a coast tile if possible
            Tile? tile;
            if (biasCoast)
            {
                tile = viableTiles.Where(it => it.IsAdjacentToCoast()).ToList().RandomOrNull(rng);
                tile ??= viableTiles.Random(rng);
            }
            else
                tile = viableTiles.Random(rng);

            CreateNewCamp(tile, rng);
            NotifyCivsOfBarbarianEncampment(tile);
            addedCamps++;

            // Still more camps to add?
            if (addedCamps < campsToAdd)
            {
                // Remove some newly non-viable tiles
                var tooClose = tile.GetTilesInDistance(7).ToHashSet();
                viableTiles.RemoveAll(it => tooClose.Contains(it));
                // Reroll bias
                biasCoast = rng.NextInt(6) == 0;
            }
        }
    }

    /// <summary>Adds a notification to every civilization that has adopted Honor policy and has explored the <paramref name="tile"/> where the Barbarian Encampment has spawned.</summary>
    private void NotifyCivsOfBarbarianEncampment(Tile tile)
    {
        foreach (var civ in GameInfo.Civilizations)
        {
            if (!civ.HasExplored(tile)) continue;
            if (!civ.HasUnique(UniqueType.NotifiedOfBarbarianEncampments)) continue;
            civ.AddNotification("A new barbarian encampment has spawned!", tile.Position, NotificationCategory.War, NotificationIcon.War);
            civ.SetLastSeenImprovement(tile.Position, tile.Improvement);
        }
    }

    /// <summary>Creates a new camp without any checks - Does not affect notifications</summary>
    public void CreateNewCamp(Tile tile, GameRandom? rng = null)
    {
        rng ??= tile.StateThisTile.StateBasedRandom("createNewCamp");
        var candidates = GameInfo.Ruleset.TileImprovements.Values
            .Where(it => it.IsBarbarianCampEquivalent(tile.StateThisTile)).ToList();
        var improvement = candidates.RandomOrNull(rng)?.Name ?? Constants.BarbarianEncampment;
        tile.SetImprovement(improvement);
        var newCamp = new BarbarianEncampment(tile.Position) { GameInfo = GameInfo };
        Encampments.Add(newCamp);
    }

    /// <summary>
    /// Attempts to spawn a random barbarian <see cref="MapUnit"/> at or near the provided <see cref="Tile"/>.
    /// </summary>
    /// <param name="tile">where to spawn (the encampment tile, or the tile whose neighbours are used)</param>
    /// <param name="allegiance">Which <see cref="Civilization"/> should the unit belong to? Defaults to the barbarian civilization.
    /// If non-barbarian, there are fewer restrictions on whether a unit can be spawned or not.</param>
    /// <returns>The newly spawned <see cref="MapUnit"/>, or null if unsuccessful.</returns>
    public virtual MapUnit? SpawnBarbarian(Tile tile, Civilization? allegiance = null)
    {
        allegiance ??= GameInfo.GetBarbarianCivilization();
        if (allegiance.IsBarbarian)
        {
            // Empty camp - spawn a defender
            if (tile.MilitaryUnit == null)
                // Try spawning a unit on this tile, return null if unsuccessful
                return SpawnUnit(tile.Position, false);

            // Don't spawn wandering barbs too early
            if (GameInfo.Turns < 10)
                return null;

            var nearbyBarbarians = 0;
            tile.ForEachTileInDistance(4, it => { if (it.MilitaryUnit?.Civ.IsBarbarian == true) nearbyBarbarians++; });
            // Too many barbarians around already?
            if (nearbyBarbarians > 2)
                return null;
        }

        var canSpawnNaval = GameInfo.Turns > 30;
        var validTiles = tile.Neighbors.Where(it =>
            !(it.IsImpassible()
              || it.IsCityCenter()
              || it.GetFirstUnit() != null
              || (it.IsWater && !canSpawnNaval)
              || (it.TerrainHasUnique(UniqueType.FreshWater) && it.IsWater))) // No Lakes
            .ToList();
        if (validTiles.Count == 0)
            return null;

        var rng = GameInfo.GetBarbarianCivilization().State.StateBasedRandom("BarbarianManager.spawnBarbarian");
        // Attempt to spawn a barbarian on a valid tile
        return SpawnUnit(tile.Position, validTiles.Random(rng).IsWater, allegiance);
    }

    /// <summary>Attempts to spawn a barbarian on <paramref name="position"/>, returns the unit if successful and null if unsuccessful.</summary>
    private MapUnit? SpawnUnit(HexCoord position, bool naval, Civilization? allegiance = null)
    {
        allegiance ??= GameInfo.GetBarbarianCivilization();
        UpdateBarbarianTech();
        var unitToSpawn = ChooseBarbarianUnit(naval);
        if (unitToSpawn == null) return null; // return null if we didn't find a unit
        return GameInfo.TileMap.PlaceUnitNearTile(position, unitToSpawn, allegiance) as MapUnit;
    }

    private void UpdateBarbarianTech()
    {
        var barbarianCiv = GameInfo.GetBarbarianCivilization();
        var allResearchedTechs = GameInfo.Ruleset.Technologies.Keys.ToList();
        foreach (var civ in GameInfo.Civilizations.Where(it => !it.IsBarbarian && !it.IsDefeated()))
        {
            var researched = civ.Tech.TechsResearched;
            allResearchedTechs.RemoveAll(it => !researched.Contains(it));
        }
        barbarianCiv.Tech.TechsResearched = new JavaHashSet<string>(allResearchedTechs);
    }

    private BaseUnit? ChooseBarbarianUnit(bool naval)
    {
        // if we don't make this into a separate list then the retain() will happen on the Tech keys,
        // which effectively removes those techs from the game and causes all sorts of problems
        var barbarianCiv = GameInfo.GetBarbarianCivilization();
        var unitList = GameInfo.Ruleset.Units.Values
            .Where(it => it.IsMilitary
                && !(((IHasUniques)it).HasUnique(UniqueType.CannotAttack) || ((IHasUniques)it).HasUnique(UniqueType.CannotBeBarbarian))
                && (naval ? it.IsWaterUnit : it.IsLandUnit)
                && it.IsBuildable(barbarianCiv)).ToList();

        if (unitList.Count == 0) return null; // No naval tech yet? Mad modders?

        // Civ V weights its list by FAST_ATTACK or ATTACK_SEA AI types, we'll do it a bit differently
        // getForceEvaluation is already conveniently biased towards fast units and against ranged naval
        var weightings = unitList.Select(it => (float)it.GetForceEvaluation()).ToList();
        var rng = new GameContext(gameInfo: GameInfo).StateBasedRandom("BarbarianManager.chooseBarbarianUnit");

        return unitList.RandomWeighted(weightings, rng);
    }

    /// <summary>Return a _mutable_ List of encampment tiles, allowing in-place sort</summary>
    public List<Tile> GetEncampmentTiles() => Encampments.Select(it => GameInfo.TileMap[it.Position]).ToList();
}
