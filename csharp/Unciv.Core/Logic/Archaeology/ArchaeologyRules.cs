using Unciv.Logic.Civilization;
using Unciv.Logic.GreatWorks;
using Unciv.Logic.Map.MapUnits;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Models.Ruleset.Tile;
using Unciv.Models.Ruleset.Unique;
using Unciv.Utils;
using CivilizationObject = Unciv.Logic.Civilization.Civilization;
using NotificationCategory = Unciv.Logic.Civilization.Notification.NotificationCategory;
using TileObject = Unciv.Logic.Map.Tiles.Tile;

namespace Unciv.Logic.Archaeology;

// Brave New World (ticket B010, spec id B040, bnw-spec/SPEC.md section 4.4). Not part of the Kotlin source.
//
// Deliberate deviations and unresolved spec items (no decompiler output or SDK source is available, so [K]/[?] items keep the spec value
// where it gives one and are otherwise settled here, all deterministic):
// - Era of a record [K]: the game era at the event, taken as the average era number of the living major civs (Civ V's current era).
// - Players of a record [K]: a kill or clear has winner = attacker, loser = defender (a barbarian camp: the barbarians); a popped ruin has
//   only Player1; a razed city has Player1 = the owner that razed it and Player2 = the civ that founded it.
// - Site weight (@100d4080, constants after rand(10) are [?]): zero on a tile with a resource, an improvement, a city, water, impassable
//   terrain, a natural wonder or a feature nobody can remove; else (raw + 10 + rand(10)) * (owned ? 8 : 9) with raw = (10 - recordEra) *
//   (recordClass + 1), and 11 without a record. The era table of the original is dropped (the record era is already in raw). The sites
//   within 3 tiles scale it by 4 / (4 + n).
// - Random record of a tile without one [?]: class rand(6) + 1, era rand(current era + 1), two different random major civs.
// - Which artifact a dig yields [?]: the first unused artifact of the record's class and era, else the first unused era-less one of the class.
//   A writing site yields its own hidden writing. The two players are stored on the work (creator = Player1, creator2 = Player2); theming
//   reads Player1 only.
// - Landmark culture [K]: "[stats] per era since discovery" counts the eras between the record era and the Landmark owner's era.
// - Not done: the diplomacy penalty of digging on foreign land and the AI promise not to dig (AI opinion belongs to the A track).

/// <summary>The archaeology rules: historical records, antiquity sites, digs.</summary>
public static class ArchaeologyRules
{
    public const string AntiquitySite = "Antiquity Site";
    public const string HiddenAntiquitySite = "Hidden Antiquity Site";

    // Defines (bnw-spec/data/Defines.tsv). Not ModConstants: they are archaeology-only and have no G&K value, like the spec's own constants.
    private const int MinSitesPerMajorCiv = 5;
    private const int MaxSitesPerMajorCiv = 8;
    private const int PercentSitesHidden = 30;
    private const int PercentHiddenSitesWriting = 30;
    private const int SiteNeighbourhood = 3;

    private static GameInfo GameOf(CivilizationObject civ) => (GameInfo)civ.GameInfo;

    /// <summary>Does the ruleset turn archaeology on (ModOptions <c>Enables archaeology</c>)?</summary>
    public static bool IsEnabled(Ruleset ruleset) => ((IHasUniques)ruleset.ModOptions).HasUnique(UniqueType.EnablesArchaeology);

    private static bool IsSite(TileObject tile) => tile.Resource is AntiquitySite or HiddenAntiquitySite;

    /// <summary>The ruleset improvement that is the dig (it has the unique <c>Is an Archaeological Dig</c>), or null.</summary>
    public static TileImprovement? DigImprovement(Ruleset ruleset) =>
        ruleset.TileImprovements.Values.FirstOrDefault(it => ((IHasUniques)it).HasUnique(UniqueType.IsArchaeologicalDig));

    /// <summary>The ruleset improvement that is the Landmark (it has the unique <c>Is a Landmark</c>), or null.</summary>
    public static TileImprovement? LandmarkImprovement(Ruleset ruleset) =>
        ruleset.TileImprovements.Values.FirstOrDefault(it => ((IHasUniques)it).HasUnique(UniqueType.IsLandmark));

    private static int EraNumber(Ruleset ruleset, string era) => ruleset.Eras.TryGetValue(era, out var found) ? found.EraNumber : 0;

    /// <summary>The era name with this number (the last era when the number is past the end).</summary>
    private static string EraName(Ruleset ruleset, int number) =>
        (ruleset.Eras.Values.FirstOrDefault(it => it.EraNumber == number) ?? ruleset.Eras.Values.OrderBy(it => it.EraNumber).Last()).Name;

    /// <summary>The game era (spec 4.4.1 [K]): the average era number of the living major civs, rounded down.</summary>
    public static int GameEraNumber(GameInfo game)
    {
        var majors = game.GetAliveMajorCivs();
        return majors.Count == 0 ? 0 : majors.Sum(it => it.GetEraNumber()) / majors.Count;
    }

    // ---- Historical records (spec 4.4.1)

    /// <summary>Writes a record on <paramref name="tile"/> (a newer event overwrites an older one). A no-op when archaeology is off.</summary>
    public static void RecordEvent(TileObject tile, ArtifactClass artifactClass, string player1, string player2)
    {
        var ruleset = tile.Ruleset;
        if (!IsEnabled(ruleset) || IsSite(tile) || IsLandmarkOf(tile)) return; // a site keeps its record, and a Landmark keeps the discovery era its yield counts from
        if (tile.TileMap.GameInfo is not GameInfo game) return;
        tile.ArchaeologyRecord = new ArchaeologyRecord
        {
            ArtifactClass = artifactClass,
            Era = EraName(ruleset, GameEraNumber(game)),
            Player1 = player1,
            Player2 = player2,
        };
    }

    /// <summary>A unit died in a fight on <paramref name="tile"/> (<paramref name="ranged"/>: the attacker shot).</summary>
    public static void RecordCombatDeath(TileObject tile, bool ranged, CivilizationObject winner, CivilizationObject loser) =>
        RecordEvent(tile, ranged ? ArtifactClass.BattleRanged : ArtifactClass.BattleMelee, winner.CivId, loser.CivId);

    /// <summary>A barbarian camp was cleared by <paramref name="clearer"/>.</summary>
    public static void RecordCampCleared(TileObject tile, CivilizationObject clearer) =>
        RecordEvent(tile, ArtifactClass.BarbarianCamp, clearer.CivId, Constants.Barbarians);

    /// <summary>An ancient ruin was entered by <paramref name="finder"/>.</summary>
    public static void RecordRuinPopped(TileObject tile, CivilizationObject finder) =>
        RecordEvent(tile, ArtifactClass.AncientRuin, finder.CivId, "");

    /// <summary>A city was razed. Call before the city is removed.</summary>
    public static void RecordCityRazed(TileObject tile, CivilizationObject owner, string founder) =>
        RecordEvent(tile, ArtifactClass.RazedCity, owner.CivId, founder == owner.CivId ? "" : founder);

    // ---- Spawning sites (spec 4.4.2)

    /// <summary>A civ learned a tech: the first time any civ learns one that triggers archaeological sites, the sites are created.</summary>
    public static void OnTechResearched(CivilizationObject civ, Technology tech)
    {
        if (civ.GameInfo is not GameInfo game || game.ArchaeologySitesSpawned || !IsEnabled(game.Ruleset)) return;
        if (!((IHasUniques)tech).HasUnique(UniqueType.TriggersArchaeologicalSites)) return;
        game.ArchaeologySitesSpawned = true;
        int created = SpawnSites(game);
        if (created > 0)
            civ.AddNotification("Archaeological sites have appeared on the map!", NotificationCategory.General, NotificationIcon.Culture);
    }

    /// <summary>Creates the antiquity sites of the game. Returns how many were placed.</summary>
    public static int SpawnSites(GameInfo game)
    {
        var ruleset = game.Ruleset;
        if (!ruleset.TileResources.ContainsKey(AntiquitySite) || !ruleset.TileResources.ContainsKey(HiddenAntiquitySite)) return 0;
        var rng = new Models.Ruleset.Unique.GameContext(gameInfo: game).StateBasedRandom("ArchaeologyRules.SpawnSites", game.Turns);

        int majors = game.Civilizations.Count(it => it.IsMajorCiv());
        int count = MinSitesPerMajorCiv * majors;
        int spread = (MaxSitesPerMajorCiv * majors - MinSitesPerMajorCiv * majors) / 2;
        if (spread > 0) count += rng.NextInt(spread) + rng.NextInt(spread);
        int writingsLeft = count * PercentSitesHidden / 100 * PercentHiddenSitesWriting / 100;
        var writingPool = ruleset.GreatWorks.Values.Where(it => it.ArchaeologyOnly && !game.IsGreatWorkUsed(it.Name)).ToList();

        var tiles = game.TileMap.TileList;
        var indexOf = new Dictionary<TileObject, int>(tiles.Count);
        for (int i = 0; i < tiles.Count; i++) indexOf[tiles[i]] = i;
        var weights = new int[tiles.Count];
        for (int i = 0; i < tiles.Count; i++) weights[i] = SiteWeight(tiles[i], rng);

        int placed = 0;
        for (int n = 0; n < count; n++)
        {
            int best = -1, bestWeight = 0;
            for (int i = 0; i < weights.Length; i++)
                if (weights[i] > bestWeight) (best, bestWeight) = (i, weights[i]);
            if (best < 0) break;
            var tile = tiles[best];

            bool hidden = rng.NextInt(100) < PercentSitesHidden;
            tile.SetTileResource(hidden ? HiddenAntiquitySite : AntiquitySite);
            tile.ResourceAmount = 0;
            tile.ArchaeologyRecord ??= RandomRecord(game, rng);
            if (hidden && writingsLeft > 0 && writingPool.Count > 0)
            {
                var writing = writingPool[rng.NextInt(writingPool.Count)];
                writingPool.Remove(writing);
                writingsLeft--;
                var record = tile.ArchaeologyRecord;
                record.ArtifactClass = ArtifactClass.Writing;
                record.GreatWork = writing.Name;
                if (writing.Era.Length != 0) record.Era = writing.Era;
            }
            placed++;
            foreach (var near in tile.GetTilesInDistance(SiteNeighbourhood))
                weights[indexOf[near]] = SiteWeight(near, rng);
        }
        return placed;
    }

    private static ArchaeologyRecord RandomRecord(GameInfo game, GameRandom rng)
    {
        var record = new ArchaeologyRecord
        {
            ArtifactClass = (ArtifactClass)(rng.NextInt(6) + 1),
            Era = EraName(game.Ruleset, rng.NextInt(GameEraNumber(game) + 1)),
        };
        var civs = game.Civilizations.Where(it => it.IsMajorCiv()).ToList();
        if (civs.Count == 0) return record;
        int first = rng.NextInt(civs.Count);
        record.Player1 = civs[first].CivId;
        if (civs.Count > 1)
        {
            int second = rng.NextInt(civs.Count - 1);
            record.Player2 = civs[second >= first ? second + 1 : second].CivId;
        }
        return record;
    }

    private static bool RemovableFeature(Ruleset ruleset, string feature) => ruleset.TileImprovements.ContainsKey(Constants.Remove + feature);

    /// <summary>The weight of a tile as a new site (0 = not allowed). Draws one random number when the tile is allowed.</summary>
    public static int SiteWeight(TileObject tile, GameRandom rng)
    {
        if (tile.Resource != null || tile.Improvement != null || tile.IsWater || tile.IsImpassible() || tile.IsCityCenter() || tile.IsNaturalWonder())
            return 0;
        var ruleset = tile.Ruleset;
        if (tile.TerrainFeatures.Any(it => !RemovableFeature(ruleset, it))) return 0;

        var record = tile.ArchaeologyRecord;
        int raw = record == null ? 11 : (10 - EraNumber(ruleset, record.Era)) * ((int)record.ArtifactClass + 1);
        int weight = (Math.Max(raw, 0) + 10 + rng.NextInt(10)) * (tile.GetOwner() != null ? 8 : 9);
        int near = tile.GetTilesInDistance(SiteNeighbourhood).Count(IsSite);
        return weight * 4 / (4 + near);
    }

    // ---- Digs (spec 4.4.3)

    /// <summary>
    /// A unit finished building <paramref name="tile"/>'s improvement. When that is the dig, the unit is used up and its owner chooses what
    /// the site becomes (the AI chooses at once).
    /// </summary>
    public static void OnImprovementBuilt(TileObject tile, MapUnit builder)
    {
        if (tile.TileImprovement is not { } improvement || !((IHasUniques)improvement).HasUnique(UniqueType.IsArchaeologicalDig)) return;
        var civ = builder.Civ;
        builder.Consume();
        var game = GameOf(civ);
        if (!civ.IsHuman())
        {
            AutoResolve(civ, tile);
            return;
        }
        game.PendingDigs?.RemoveAll(it => game.TileMap.GetOrNull(it.X, it.Y) is not { } other || !IsDigOf(other) || game.GetCivilization(it.Civ).IsDefeated()); // drop digs that no longer exist
        (game.PendingDigs ??= []).Add(new PendingDig { X = tile.Position.x, Y = tile.Position.y, Civ = civ.CivId });
        civ.AddNotification("Our archaeologist has finished the dig! Choose what to do with the site.", tile.Position,
            NotificationCategory.General, NotificationIcon.Culture);
    }

    private static void AutoResolve(CivilizationObject civ, TileObject tile)
    {
        if (FindArtifact(civ, tile) is { } found) TakeArtifact(civ, tile, found.Work, found.Location);
        else BuildLandmark(civ, tile);
    }

    /// <summary>The finished dig of <paramref name="civ"/> at this position, or null.</summary>
    public static PendingDig? FindPending(GameInfo game, CivilizationObject civ, int x, int y) =>
        PendingOf(game, civ).FirstOrDefault(it => it.X == x && it.Y == y);

    /// <summary>The finished digs of the civ whose site is still a dig, in completion order.</summary>
    public static IEnumerable<PendingDig> PendingOf(GameInfo game, CivilizationObject civ) =>
        (game.PendingDigs ?? []).Where(it => it.Civ == civ.CivId && game.TileMap.GetOrNull(it.X, it.Y) is { } tile && IsDigOf(tile));

    private static bool IsDigOf(TileObject tile) =>
        !tile.ImprovementIsPillaged && tile.TileImprovement is { } improvement && ((IHasUniques)improvement).HasUnique(UniqueType.IsArchaeologicalDig);

    private static bool IsLandmarkOf(TileObject tile) =>
        tile.TileImprovement is { } improvement && ((IHasUniques)improvement).HasUnique(UniqueType.IsLandmark);

    public static void RemovePending(GameInfo game, PendingDig pending)
    {
        game.PendingDigs?.Remove(pending);
        if (game.PendingDigs?.Count == 0) game.PendingDigs = null;
    }

    /// <summary>The work a dig on this site yields, or null when none is left to find.</summary>
    public static GreatWork? ChooseArtifact(GameInfo game, ArchaeologyRecord record)
    {
        var works = game.Ruleset.GreatWorks.Values.Where(it => !game.IsGreatWorkUsed(it.Name)).ToList();
        if (record.ArtifactClass == ArtifactClass.Writing)
            return works.FirstOrDefault(it => it.Name == record.GreatWork)
                ?? works.FirstOrDefault(it => it.ArchaeologyOnly && it.GreatWorkClass == GreatWorkClass.Writing);
        string className = record.ArtifactClass.GreatWorkClassName();
        var ofClass = works.Where(it => it.GreatWorkClass == GreatWorkClass.Artifact && it.ArtifactClass == className).ToList();
        return ofClass.FirstOrDefault(it => it.Era == record.Era) ?? ofClass.FirstOrDefault(it => it.Era.Length == 0);
    }

    /// <summary>The artifact of the site and the free slot it would go to for <paramref name="civ"/>, or null (no record, no work left, no free slot).</summary>
    public static (GreatWork Work, GreatWorkLocation Location)? FindArtifact(CivilizationObject civ, TileObject tile)
    {
        if (tile.ArchaeologyRecord is not { } record) return null;
        if (ChooseArtifact(GameOf(civ), record) is not { } found) return null;
        if (GreatWorkRules.FindClosestFreeSlot(civ, tile, found.GreatWorkClass) is not { } slot) return null;
        return (found, slot);
    }

    /// <summary>The site becomes the artifact: it goes into <paramref name="location"/> and the tile is cleared.</summary>
    public static void TakeArtifact(CivilizationObject civ, TileObject tile, GreatWork work, GreatWorkLocation location)
    {
        var game = GameOf(civ);
        var record = tile.ArchaeologyRecord.NotNull();
        var instance = new GreatWorkInstance
        {
            Name = work.Name,
            Creator = record.Player1,
            Creator2 = record.Player2,
            Era = work.Era.Length != 0 ? work.Era : record.Era,
            Turn = game.Turns,
        };
        (game.GreatWorkInstances ??= []).Add(instance);
        location.City.SetGreatWorkIn(location.Building.Name, location.Slot, work.Name);
        ClearSite(tile);
        location.City.CityStats.Update();
        civ.UpdateStatsForNextTurn();
    }

    /// <summary>The site becomes a Landmark of the tile's owner (spec 4.4.3). On city-state land the digging civ gains influence.</summary>
    public static void BuildLandmark(CivilizationObject civ, TileObject tile)
    {
        var landmark = LandmarkImprovement(tile.Ruleset).NotNull();
        tile.SetTileResource((string?)null);
        tile.ResourceAmount = 0;
        tile.SetImprovement(landmark.Name, civ);
        if (tile.GetOwner() is CivilizationObject { IsCityState: true } cityState && !ReferenceEquals(cityState, civ))
            cityState.GetDiplomacyManagerOrMeet(civ).AddInfluence(tile.Ruleset.ModOptions.Constants.LandmarkMinorFriendshipChange);
        foreach (var city in civ.Cities) city.CityStats.Update();
        if (tile.GetOwner() is CivilizationObject owner && !ReferenceEquals(owner, civ))
            foreach (var city in owner.Cities) city.CityStats.Update();
        civ.UpdateStatsForNextTurn();
    }

    private static void ClearSite(TileObject tile)
    {
        tile.RemoveImprovement();
        tile.SetTileResource((string?)null);
        tile.ResourceAmount = 0;
        tile.ArchaeologyRecord = null;
    }

    /// <summary>The number of eras between a tile's record and its owner's era: the factor of <c>[stats] per era since discovery</c>.</summary>
    public static int ErasSinceDiscovery(TileObject tile)
    {
        if (tile.ArchaeologyRecord is not { } record || tile.GetOwner() is not CivilizationObject owner) return 0;
        return Math.Max(0, owner.GetEraNumber() - EraNumber(tile.Ruleset, record.Era));
    }
}
