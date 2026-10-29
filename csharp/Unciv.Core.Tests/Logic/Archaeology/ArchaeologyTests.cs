using Unciv.Logic.Archaeology;
using Unciv.Logic.Files;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;
using Unciv.Models.Ruleset.Tech;
using Unciv.Utils;

namespace Unciv.Core.Tests.Logic.Archaeology;

// B010 (bnw-spec/SPEC.md 4.4): records, site spawning, the dig, artifact and Landmark.
[Collection("RulesetCache")]
public sealed class ArchaeologyTests
{
    private readonly ArchaeologyWorld a = new();
    private Unciv.Core.Tests.Logic.GreatWorks.GreatWorkWorld W => a.W;

    // --- records

    [Fact]
    public void AnEventWritesARecordAndANewerOneOverwritesIt()
    {
        var tile = W.Game.GetTile(0, 5);
        ArchaeologyRules.RecordCampCleared(tile, W.A);
        var record = tile.ArchaeologyRecord!;
        Assert.Equal((ArtifactClass.BarbarianCamp, W.A.CivId, Constants.Barbarians, "Ancient era"), (record.ArtifactClass, record.Player1, record.Player2, record.Era));
        ArchaeologyRules.RecordCombatDeath(tile, true, W.B, W.A);
        Assert.Equal((ArtifactClass.BattleRanged, W.B.CivId, W.A.CivId), (tile.ArchaeologyRecord!.ArtifactClass, tile.ArchaeologyRecord.Player1, tile.ArchaeologyRecord.Player2));
        ArchaeologyRules.RecordCombatDeath(tile, false, W.A, W.B);
        Assert.Equal(ArtifactClass.BattleMelee, tile.ArchaeologyRecord!.ArtifactClass);
        ArchaeologyRules.RecordRuinPopped(tile, W.A);
        Assert.Equal((ArtifactClass.AncientRuin, ""), (tile.ArchaeologyRecord!.ArtifactClass, tile.ArchaeologyRecord.Player2));
    }

    [Fact]
    public void NothingIsRecordedWithoutTheGateOrOnASite()
    {
        var off = new ArchaeologyWorld(enabled: false);
        var tile = off.W.Game.GetTile(0, 5);
        ArchaeologyRules.RecordRuinPopped(tile, off.W.A);
        Assert.Null(tile.ArchaeologyRecord);

        var site = a.Site(ArtifactClass.RazedCity);
        ArchaeologyRules.RecordRuinPopped(site, W.A);
        Assert.Equal(ArtifactClass.RazedCity, site.ArchaeologyRecord!.ArtifactClass);
    }

    [Fact]
    public void RazingACityRecordsTheOwnerAndTheFounder()
    {
        var tile = W.CityB.GetCenterTile();
        W.CityB.DestroyCity(true);
        var record = tile.ArchaeologyRecord!;
        Assert.Equal((ArtifactClass.RazedCity, W.B.CivId, ""), (record.ArtifactClass, record.Player1, record.Player2));
    }

    [Fact]
    public void TheGameEraIsTheAverageOfTheMajorCivs()
    {
        Assert.Equal(0, ArchaeologyRules.GameEraNumber(W.Game.GameInfo));
    }

    // --- spawning

    [Fact]
    public void SpawningPlacesBetweenFiveAndEightSitesPerMajorCivOnFreeLand()
    {
        int placed = ArchaeologyRules.SpawnSites(W.Game.GameInfo);
        Assert.InRange(placed, 10, 16);
        var sites = W.Game.TileMap.TileList.Where(it => it.Resource is ArchaeologyRules.AntiquitySite or ArchaeologyRules.HiddenAntiquitySite).ToList();
        Assert.Equal(placed, sites.Count);
        foreach (var site in sites)
        {
            Assert.NotNull(site.ArchaeologyRecord);
            Assert.False(site.IsCityCenter());
            Assert.False(site.IsWater);
        }
        Assert.All(sites.Where(it => it.ArchaeologyRecord!.ArtifactClass == ArtifactClass.Writing), it => Assert.Equal("HiddenWriting", it.ArchaeologyRecord!.GreatWork));
        Assert.True(sites.Count(it => it.ArchaeologyRecord!.ArtifactClass == ArtifactClass.Writing) <= 1);
    }

    [Fact]
    public void SpawningIsDeterministicAndKeepsExistingRecords()
    {
        var other = new ArchaeologyWorld();
        W.Game.GetTile(3, 3).ArchaeologyRecord = new ArchaeologyRecord { ArtifactClass = ArtifactClass.RazedCity, Era = "Ancient era", Player1 = "X" };
        other.W.Game.GetTile(3, 3).ArchaeologyRecord = new ArchaeologyRecord { ArtifactClass = ArtifactClass.RazedCity, Era = "Ancient era", Player1 = "X" };
        ArchaeologyRules.SpawnSites(W.Game.GameInfo);
        ArchaeologyRules.SpawnSites(other.W.Game.GameInfo);
        string Dump(Unciv.Core.Tests.Logic.GreatWorks.GreatWorkWorld w) => string.Join(";", w.Game.TileMap.TileList
            .Where(it => it.Resource != null).Select(it => $"{it.Position.x},{it.Position.y},{it.Resource},{it.ArchaeologyRecord!.ArtifactClass},{it.ArchaeologyRecord.Era}"));
        Assert.Equal(Dump(other.W), Dump(W));
        Assert.NotEqual("", Dump(W));
        var kept = W.Game.GetTile(3, 3);
        if (kept.Resource != null) Assert.Equal((ArtifactClass.RazedCity, "X"), (kept.ArchaeologyRecord!.ArtifactClass, kept.ArchaeologyRecord.Player1));
    }

    [Fact]
    public void SiteWeightIsZeroOnForbiddenTilesAndBoundedOtherwise()
    {
        var rng = new GameRandom(1);
        var free = W.Game.GetTile(0, 8);
        for (int i = 0; i < 50; i++) Assert.InRange(ArchaeologyRules.SiteWeight(free, rng), 21 * 9, 30 * 9); // (11 + 10 + rand(10)) * 9, no site near
        Assert.Equal(0, ArchaeologyRules.SiteWeight(W.CapA.GetCenterTile(), rng));
        free.SetTileResource(ArchaeologyRules.AntiquitySite);
        Assert.Equal(0, ArchaeologyRules.SiteWeight(free, rng));
        var improved = W.Game.GetTile(1, 8);
        improved.SetImprovement("Farm");
        Assert.Equal(0, ArchaeologyRules.SiteWeight(improved, rng));
        var feature = W.Game.GetTile(2, 8);
        feature.AddTerrainFeature("Ice");
        feature.SetTerrainTransients();
        Assert.Equal(0, ArchaeologyRules.SiteWeight(feature, rng));
    }

    [Fact]
    public void ANearbySiteAndAnOwnedTileLowerTheWeightAndAnOlderRecordRaisesIt()
    {
        var rng = new GameRandom(5);
        var tile = W.Game.GetTile(0, 8);
        int baseline = Enumerable.Range(0, 30).Min(_ => ArchaeologyRules.SiteWeight(tile, rng));
        W.Game.GetTile(1, 8).SetTileResource(ArchaeologyRules.AntiquitySite);
        int crowded = Enumerable.Range(0, 30).Max(_ => ArchaeologyRules.SiteWeight(tile, rng));
        Assert.True(crowded < baseline * 30 / 21 && crowded <= 30 * 9 * 4 / 5);
        // a record of the oldest era and the highest class: (10 - 0) * (6 + 1) = 70
        var other = W.Game.GetTile(0, -8);
        other.ArchaeologyRecord = new ArchaeologyRecord { ArtifactClass = ArtifactClass.Writing, Era = "Ancient era" };
        Assert.InRange(ArchaeologyRules.SiteWeight(other, rng), (70 + 10) * 9, (70 + 19) * 9);
    }

    [Fact]
    public void LearningTheTriggerTechSpawnsTheSitesOnlyOnce()
    {
        var tech = new Technology { Name = "Digging Lore", Uniques = ["Triggers the creation of archaeological sites"], Column = new TechColumn { ColumnNumber = 0, Era = "Ancient era" } };
        W.Game.Ruleset.Technologies.Put(tech.Name, tech);
        var other = new Technology { Name = "Plain Lore", Column = new TechColumn { ColumnNumber = 0, Era = "Ancient era" } };
        W.Game.Ruleset.Technologies.Put(other.Name, other);
        W.A.Tech.AddTechnology("Plain Lore");
        Assert.False(W.Game.GameInfo.ArchaeologySitesSpawned);
        W.A.Tech.AddTechnology("Digging Lore");
        Assert.True(W.Game.GameInfo.ArchaeologySitesSpawned);
        int sites = W.Game.TileMap.TileList.Count(it => it.Resource != null);
        Assert.InRange(sites, 10, 16);
        W.B.Tech.AddTechnology("Digging Lore");
        Assert.Equal(sites, W.Game.TileMap.TileList.Count(it => it.Resource != null));
    }

    [Fact]
    public void WithoutTheGateOrTheResourcesNothingSpawns()
    {
        var off = new ArchaeologyWorld(enabled: false);
        var tech = new Technology { Name = "Digging Lore", Uniques = ["Triggers the creation of archaeological sites"], Column = new TechColumn { ColumnNumber = 0, Era = "Ancient era" } };
        off.W.Game.Ruleset.Technologies.Put(tech.Name, tech);
        off.W.A.Tech.AddTechnology("Digging Lore");
        Assert.False(off.W.Game.GameInfo.ArchaeologySitesSpawned);
        Assert.DoesNotContain(off.W.Game.TileMap.TileList, it => it.Resource != null);

        W.Game.Ruleset.TileResources.Remove(ArchaeologyRules.AntiquitySite);
        Assert.Equal(0, ArchaeologyRules.SpawnSites(W.Game.GameInfo));
    }

    // --- artifacts

    [Fact]
    public void ChooseArtifactPrefersTheRecordEraThenTheEralessWork()
    {
        var game = W.Game.GameInfo;
        var record = new ArchaeologyRecord { ArtifactClass = ArtifactClass.BattleMelee, Era = GreatWorkWorld_Classical };
        Assert.Equal("FindClassical", ArchaeologyRules.ChooseArtifact(game, record)!.Name);
        game.GreatWorkInstances = [new() { Name = "FindClassical" }];
        Assert.Equal("FindOther", ArchaeologyRules.ChooseArtifact(game, record)!.Name);
        game.GreatWorkInstances.Add(new() { Name = "FindOther" });
        Assert.Null(ArchaeologyRules.ChooseArtifact(game, record));
        Assert.Null(ArchaeologyRules.ChooseArtifact(game, new ArchaeologyRecord { ArtifactClass = ArtifactClass.BarbarianCamp, Era = "Ancient era" }));
    }

    private const string GreatWorkWorld_Classical = Unciv.Core.Tests.Logic.GreatWorks.GreatWorkWorld.Classical;

    [Fact]
    public void AWritingSiteYieldsItsHiddenWritingEvenWithAnotherEra()
    {
        var game = W.Game.GameInfo;
        var record = new ArchaeologyRecord { ArtifactClass = ArtifactClass.Writing, Era = "Medieval era", GreatWork = "HiddenWriting" };
        Assert.Equal("HiddenWriting", ArchaeologyRules.ChooseArtifact(game, record)!.Name);
        Assert.Equal(GreatWorkClass.Writing, ArchaeologyRules.ChooseArtifact(game, record)!.GreatWorkClass);
        record.GreatWork = "";
        Assert.Equal("HiddenWriting", ArchaeologyRules.ChooseArtifact(game, record)!.Name); // falls back to the first unused hidden writing
    }

    // --- dig

    [Fact]
    public void AFinishedDigUsesUpTheBuilderAndWaitsForAHumanChoice()
    {
        var tile = a.Site();
        var unit = a.FinishDig(tile);
        Assert.True(unit.IsDestroyed);
        var pending = Assert.Single(W.Game.GameInfo.PendingDigs!);
        Assert.Equal((W.A.CivId, tile.Position.x, tile.Position.y), (pending.Civ, pending.X, pending.Y));
        Assert.Equal(a.Dig.Name, tile.Improvement);
        Assert.Single(ArchaeologyRules.PendingOf(W.Game.GameInfo, W.A));
        Assert.Empty(ArchaeologyRules.PendingOf(W.Game.GameInfo, W.B));
    }

    [Fact]
    public void AnotherImprovementIsNotADig()
    {
        var tile = a.Site();
        var unit = W.Game.AddUnit("Worker", W.A, tile);
        tile.SetImprovement("Farm", W.A, unit);
        ArchaeologyRules.OnImprovementBuilt(tile, unit);
        Assert.False(unit.IsDestroyed);
        Assert.Null(W.Game.GameInfo.PendingDigs);
    }

    [Fact]
    public void AnAIChoosesTheArtifactWhenItFitsElseTheLandmark()
    {
        var ai = W.Game.AddCiv();
        var city = W.Game.AddCity(ai, W.Game.GetTile(0, -9));
        var gallery = W.Build(city, 1, GreatWorkSlotType.ArtArtifact);
        var tile = W.Game.GetTile(0, -8);
        tile.SetOwningCity(city);
        tile.SetTileResource(ArchaeologyRules.AntiquitySite);
        tile.ArchaeologyRecord = new ArchaeologyRecord { ArtifactClass = ArtifactClass.BattleMelee, Era = "Ancient era", Player1 = "P1" };
        a.FinishDig(tile, ai);
        Assert.Null(W.Game.GameInfo.PendingDigs);
        Assert.Equal("FindAncient", city.GetGreatWorkIn(gallery.Name, 0));
        Assert.Null(tile.Improvement);

        var second = W.Game.GetTile(1, -8);
        second.SetOwningCity(city);
        second.SetTileResource(ArchaeologyRules.AntiquitySite);
        second.ArchaeologyRecord = new ArchaeologyRecord { ArtifactClass = ArtifactClass.BattleMelee, Era = "Ancient era" };
        a.FinishDig(second, ai); // the gallery is full now
        Assert.Equal(a.Landmark.Name, second.Improvement);
    }

    [Fact]
    public void ALandmarkGrowsWithTheErasSinceTheRecord()
    {
        var tile = a.Site(era: "Ancient era");
        a.FinishDig(tile);
        ArchaeologyRules.BuildLandmark(W.A, tile);
        Assert.Null(tile.Resource);
        Assert.Equal(a.Landmark.Name, tile.Improvement);
        Assert.Equal(0, ArchaeologyRules.ErasSinceDiscovery(tile)); // owner era 0, record era 0
        var stats = tile.Stats.GetTileStats(W.CapA, W.A);
        float baseline = stats.Culture;
        tile.ArchaeologyRecord!.Era = "Ancient era";
        W.A.Tech.AddTechnology(W.Game.Ruleset.Technologies.Values.First(it => it.Era() == "Classical era").Name);
        Assert.Equal(1, ArchaeologyRules.ErasSinceDiscovery(tile));
        Assert.Equal(baseline + 1f, tile.Stats.GetTileStats(W.CapA, W.A).Culture);
    }

    [Fact]
    public void ALandmarkOnCityStateLandGivesInfluence()
    {
        W.Game.Ruleset.ModOptions.Constants.LandmarkMinorFriendshipChange = 50;
        var cs = W.Game.AddCiv(cityStateType: "Cultured");
        var city = W.Game.AddCity(cs, W.Game.GetTile(0, -9));
        var tile = W.Game.GetTile(0, -8);
        tile.SetOwningCity(city);
        tile.SetTileResource(ArchaeologyRules.AntiquitySite);
        float before = cs.GetDiplomacyManagerOrMeet(W.A).Influence;
        ArchaeologyRules.BuildLandmark(W.A, tile);
        Assert.Equal(before + 50f, cs.GetDiplomacyManager(W.A)!.Influence);
    }

    [Fact]
    public void OnlyAUnitThatNamesTheDigCanBuildIt()
    {
        var tile = a.Site();
        var worker = W.Game.AddUnit("Worker", W.A, tile);
        Assert.False(worker.CanBuildImprovement(a.Dig, tile)); // "Can build [Land] improvements" is not enough
        var digger = W.Game.CreateBaseUnit("Civilian", $"Can build [{a.Dig.Name}] improvements on tiles");
        var unit = W.Game.AddUnit(digger.Name, W.A, W.Game.GetTile(0, -3));
        Assert.True(unit.CanBuildImprovement(a.Dig, tile));
    }

    [Fact]
    public void ArchaeologyStateSurvivesSaveLoadAndClone()
    {
        var tile = a.Site(ArtifactClass.Writing);
        tile.ArchaeologyRecord!.GreatWork = "HiddenWriting";
        W.Game.GameInfo.ArchaeologySitesSpawned = true;
        a.FinishDig(a.Site(ArtifactClass.RazedCity));
        var game = W.Game.GameInfo;
        var clone = game.Clone();
        Assert.True(clone.ArchaeologySitesSpawned);
        Assert.Equal(game.PendingDigs!.Count, clone.PendingDigs!.Count);
        var cloned = clone.TileMap.TileList.First(it => it.Position == tile.Position);
        Assert.Equal("HiddenWriting", cloned.ArchaeologyRecord!.GreatWork);
        Assert.NotSame(tile.ArchaeologyRecord, cloned.ArchaeologyRecord);

        string json = UncivFiles.GameInfoToString(game, forceZip: false);
        Assert.Contains("\"archaeology\"", json);
        Assert.Contains("\"pendingDigs\"", json);
        // the nations of the synthetic test game are not loadable by name, so the record is round-tripped on its tile
        var tileJson = System.Text.Json.JsonSerializer.Serialize(tile);
        var loadedTile = System.Text.Json.JsonSerializer.Deserialize<Tile>(tileJson)!;
        Assert.Equal(ArtifactClass.Writing, loadedTile.ArchaeologyRecord!.ArtifactClass);
        Assert.Equal("HiddenWriting", loadedTile.ArchaeologyRecord.GreatWork);
    }

    [Fact]
    public void ArtifactClassesNameTheirGreatWorkClass()
    {
        Assert.Equal(["Battle Ranged", "Battle Melee", "Barbarian Camp", "Ancient Ruin", "Razed City", "Writing"],
            Enum.GetValues<ArtifactClass>().Select(it => it.GreatWorkClassName()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ArtifactClass)99).GreatWorkClassName());
    }

    [Fact]
    public void APendingDigCloneIsIndependent()
    {
        var dig = new PendingDig { X = 1, Y = -2, Civ = "A" };
        var clone = dig.Clone();
        clone.X = 5;
        Assert.Equal((1, -2, "A"), (dig.X, dig.Y, dig.Civ));
        Assert.Equal((5, -2, "A"), (clone.X, clone.Y, clone.Civ));
    }

    [Fact]
    public void AResourceCanBeRevealedByAPolicy()
    {
        var ruleset = W.Game.Ruleset;
        var policy = ruleset.Policies.Values.First();
        var resource = ruleset.TileResources[ArchaeologyRules.HiddenAntiquitySite];
        resource.RevealedBy = policy.Name;
        Assert.False(W.A.Tech.IsRevealed(resource));
        W.A.Policies.AdoptedPolicies.Add(policy.Name);
        Assert.True(W.A.Tech.IsRevealed(resource));
    }

    [Fact]
    public void AGameWithoutArchaeologySerialisesWithoutItsFields()
    {
        var off = new ArchaeologyWorld(enabled: false);
        string json = UncivFiles.GameInfoToString(off.W.Game.GameInfo, forceZip: false);
        Assert.DoesNotContain("archaeolog", json);
        Assert.DoesNotContain("pendingDigs", json);
    }
}
