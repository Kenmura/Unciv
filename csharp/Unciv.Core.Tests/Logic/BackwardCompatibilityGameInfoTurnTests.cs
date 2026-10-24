using Unciv.Core.Tests.Support;
using Unciv.Logic;
using Unciv.Logic.Civilization;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Map.Tiles;
using Unciv.Models.Ruleset;

namespace Unciv.Core.Tests.Logic;

// T071: BackwardCompatibility.kt on TestGame games (the Kotlin object has no tests of its own).
[Collection("RulesetCache")]
public sealed class BackwardCompatibilityGameInfoTurnTests
{
    private readonly TestGame game = new();
    private GameInfo Info => game.GameInfo;
    private Ruleset Rules => game.Ruleset;

    private Civilization Civ()
    {
        game.MakeHexagonalMap(3);
        return game.AddCiv(isPlayer: true);
    }

    [Fact]
    public void RemoveMissingModReferences_DropsUnitsPromotionsAndGreatPersonPointsOfRemovedObjects()
    {
        var civ = Civ();
        var warrior = game.AddUnit("Warrior", civ, game.GetTile(0, 0));
        var archer = game.AddUnit("Archer", civ, game.GetTile(1, 0));
        warrior.Promotions.Promotions.Add("Shock I");
        warrior.Promotions.Promotions.Add("Gone promotion");
        Rules.Units.Remove("Archer");
        civ.GreatPeople.GreatGeneralPointsCounter["Great General"] = 5;
        civ.GreatPeople.GreatGeneralPointsCounter["Gone general"] = 7;
        civ.GreatPeople.GreatPersonPointsCounter["Great Scientist"] = 3;
        civ.GreatPeople.GreatPersonPointsCounter["Gone person"] = 4;
        civ.GreatPeople.PointsForNextGreatGeneralCounter["Gone pool"] = 9;

        BackwardCompatibility.RemoveMissingModReferences(Info);

        Assert.Same(warrior, game.GetTile(0, 0).MilitaryUnit);
        Assert.Null(archer.GetTile().MilitaryUnit);
        Assert.Empty(game.GetTile(1, 0).GetUnits());
        Assert.Equal(["Shock I"], warrior.Promotions.Promotions.ToList());
        Assert.Equal(["Great General"], civ.GreatPeople.GreatGeneralPointsCounter.Keys.ToList());
        Assert.Equal(["Great Scientist"], civ.GreatPeople.GreatPersonPointsCounter.Keys.ToList());
        Assert.Equal(9, civ.GreatPeople.PointsForNextGreatGeneralCounter["Gone pool"]); // the 'points to next' counters stay
    }

    [Fact]
    public void RemoveMissingModReferences_CleansCityConstructions()
    {
        var civ = Civ();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        var constructions = city.CityConstructions;
        constructions.BuiltBuildings.Add("Gone building");
        constructions.EditQueue(queue =>
        {
            queue.Clear();
            queue.AddRange(["Monument", "Gone building", "Warrior", "Gone unit", PerpetualConstruction.Science.Name]);
        });
        constructions.InProgressConstructions["Gone building"] = 10;
        constructions.InProgressConstructions["Monument"] = 20;
        Rules.Buildings.Remove("Gone building"); // never existed: stays absent

        BackwardCompatibility.RemoveMissingModReferences(Info);

        Assert.DoesNotContain("Gone building", constructions.BuiltBuildings);
        Assert.Contains("Palace", constructions.BuiltBuildings);
        Assert.Equal(["Monument", "Warrior", PerpetualConstruction.Science.Name], constructions.ConstructionQueue.ToList());
        Assert.False(constructions.InProgressConstructions.ContainsKey("Gone building"));
        Assert.Equal(20, constructions.InProgressConstructions["Monument"]);
    }

    [Fact]
    public void RemoveMissingModReferences_DropsUnknownTechsPoliciesAndLastSeenImprovements()
    {
        var civ = Civ();
        civ.Tech.TechsResearched.Add("Gone tech");
        civ.Tech.TechsToResearch.Add("Gone tech");
        civ.Tech.TechsToResearch.Add("Writing");
        civ.Policies.AdoptedPolicies.Add("Gone policy");
        civ.LastSeenImprovement[game.GetTile(0, 0).Position] = "Gone improvement";
        civ.LastSeenImprovement[game.GetTile(1, 0).Position] = "Farm";

        BackwardCompatibility.RemoveMissingModReferences(Info);

        Assert.DoesNotContain("Gone tech", civ.Tech.TechsResearched);
        Assert.Equal(["Writing"], civ.Tech.TechsToResearch);
        Assert.DoesNotContain("Gone policy", civ.Policies.AdoptedPolicies);
        Assert.False(civ.LastSeenImprovement.ContainsKey(game.GetTile(0, 0).Position));
        Assert.Equal("Farm", civ.LastSeenImprovement[game.GetTile(1, 0).Position]);
    }

    [Fact]
    public void RemoveMissingModReferences_RemovesPillagedImprovementsOnlyWhenRepairIsGone()
    {
        Civ();
        var tile = game.GetTile(0, 0);
        tile.Improvement = "Farm";
        tile.ImprovementIsPillaged = true;
        var roadTile = game.GetTile(1, 0);
        roadTile.SetRoadStatus(RoadStatus.Road, null);
        roadTile.RoadIsPillaged = true;

        Assert.True(Rules.TileImprovements.ContainsKey(Constants.Repair));
        BackwardCompatibility.RemoveMissingModReferences(Info);
        Assert.True(tile.ImprovementIsPillaged);
        Assert.True(roadTile.RoadIsPillaged);

        Rules.TileImprovements.Remove(Constants.Repair);
        BackwardCompatibility.RemoveMissingModReferences(Info);
        Assert.Null(tile.Improvement);
        Assert.False(tile.ImprovementIsPillaged);
        Assert.Equal(RoadStatus.None, roadTile.RoadStatus);
        Assert.False(roadTile.RoadIsPillaged);
    }

    [Fact]
    public void RemoveMissingModReferences_FixesAMissingStartingEraAndEmptyCivIds()
    {
        var civ = Civ();
        Info.GameParameters.StartingEra = "Gone era";
        civ.CivId = "";

        BackwardCompatibility.RemoveMissingModReferences(Info);

        Assert.Equal(Rules.Eras.Keys.First(), Info.GameParameters.StartingEra);
        Assert.Equal(civ.CivName, civ.CivId);
    }

    [Fact]
    public void MigrateCivId_AlsoWorksOnThePreview()
    {
        var civ = Civ();
        civ.CivId = "";
        var preview = Info.AsPreview();
        Assert.Equal("", preview.Civilizations[0].CivId);

        BackwardCompatibility.MigrateCivId(preview);

        Assert.Equal(civ.CivName, preview.Civilizations[0].CivId);
        Assert.Equal("", civ.CivId); // the live civ is untouched
        preview.Civilizations[0].CivId = "kept";
        BackwardCompatibility.MigrateCivId(preview);
        Assert.Equal("kept", preview.Civilizations[0].CivId);
    }

    [Fact]
    public void MigrateGreatGeneralPools_TakesTheLargerOfPointsAndPoolCounter()
    {
        var civ = Civ();
        civ.GreatPeople.PointsForNextGreatGeneral = 300;
        civ.GreatPeople.PointsForNextGreatGeneralCounter["Great General"] = 200;
        BackwardCompatibility.MigrateGreatGeneralPools(Info);
        Assert.Equal(300, civ.GreatPeople.PointsForNextGreatGeneralCounter["Great General"]);
        Assert.Equal(300, civ.GreatPeople.PointsForNextGreatGeneral);

        civ.GreatPeople.PointsForNextGreatGeneralCounter["Great General"] = 450;
        BackwardCompatibility.MigrateGreatGeneralPools(Info);
        Assert.Equal(450, civ.GreatPeople.PointsForNextGreatGeneral);
        Assert.Equal(450, civ.GreatPeople.PointsForNextGreatGeneralCounter["Great General"]);
    }

    [Theory]
    [InlineData("Fortify 5", 5, "Fortify")]
    [InlineData("Fortify   12 until healed", 12, "Fortify until healed")]
    [InlineData("Fortify 3 until healed", 3, "Fortify until healed")]
    public void ConvertFortify_SplitsTheTurnCountOffTheAction(string action, int turns, string expected)
    {
        var civ = Civ();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(0, 0));
        unit.Action = action;

        BackwardCompatibility.ConvertFortify(Info);

        Assert.Equal(turns, unit.TurnsFortified);
        Assert.Equal(expected, unit.Action);
    }

    [Theory]
    [InlineData("Fortify")]
    [InlineData("Fortify 5!")]
    [InlineData("Fortified 5")]
    [InlineData("Sleep")]
    public void ConvertFortify_LeavesOtherActionsAlone(string action)
    {
        var civ = Civ();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(0, 0));
        unit.Action = action;
        BackwardCompatibility.ConvertFortify(Info);
        Assert.Equal(action, unit.Action);
        Assert.Equal(0, unit.TurnsFortified);
    }

    [Fact]
    public void ConvertFortify_IgnoresUnitsWithoutAnAction()
    {
        var civ = Civ();
        var unit = game.AddUnit("Warrior", civ, game.GetTile(0, 0));
        unit.Action = null;
        BackwardCompatibility.ConvertFortify(Info);
        Assert.Null(unit.Action);
    }

    [Fact]
    public void MigrateToTileHistory_RecordsOwnershipOnceAndStoresTheStartTurn()
    {
        var civ = Civ();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        Info.Turns = 12;
        Info.HistoryStartTurn = -1;

        BackwardCompatibility.MigrateToTileHistory(Info);

        Assert.Equal(12, Info.HistoryStartTurn);
        Assert.Equal(civ.CivName, city.GetCenterTile().History.GetState(12).OwningCivName);

        Info.Turns = 20;
        BackwardCompatibility.MigrateToTileHistory(Info); // already migrated
        Assert.Equal(12, Info.HistoryStartTurn);
    }

    [Fact]
    public void EnsureUnitIds_GivesIdsToUnitsWithoutOne()
    {
        var civ = Civ();
        var withId = game.AddUnit("Warrior", civ, game.GetTile(0, 0));
        var without = game.AddUnit("Archer", civ, game.GetTile(1, 0));
        withId.Id = 5;
        without.Id = Constants.NoId;

        BackwardCompatibility.EnsureUnitIds(Info);

        Assert.Equal(5, withId.Id);
        Assert.NotEqual(Constants.NoId, without.Id);
        Assert.NotEqual(5, without.Id);
    }

    [Fact]
    public void ReplaceDiplomacyFlag_MovesTheFlagValue()
    {
        var civ = Civ();
        var other = game.AddCiv();
        civ.DiplomacyFunctions.MakeCivilizationsMeet(other);
        var manager = civ.GetDiplomacyManager(other)!;
        manager.SetFlag(DiplomacyFlags.WaryOf, 17);

        BackwardCompatibility.ReplaceDiplomacyFlag(Info, DiplomacyFlags.WaryOf, DiplomacyFlags.Denunciation);

        Assert.False(manager.HasFlag(DiplomacyFlags.WaryOf));
        Assert.Equal(17, manager.GetFlag(DiplomacyFlags.Denunciation));
        BackwardCompatibility.ReplaceDiplomacyFlag(Info, DiplomacyFlags.WaryOf, DiplomacyFlags.Denunciation); // nothing left to move
        Assert.Equal(17, manager.GetFlag(DiplomacyFlags.Denunciation));
    }

    [Fact]
    public void ReplaceUpdatedTechName_RenamesInEveryCollection()
    {
        var civ = Civ();
        civ.Tech.TechsResearched.Add("Old tech");
        civ.Tech.TechsToResearch.AddRange(["Pottery", "Old tech"]);
        civ.Tech.TechsInProgress["Old tech"] = 33;

        BackwardCompatibility.ReplaceUpdatedTechName(civ.Tech, "Old tech", "New tech");

        Assert.DoesNotContain("Old tech", civ.Tech.TechsResearched);
        Assert.Contains("New tech", civ.Tech.TechsResearched);
        Assert.Equal(["Pottery", "New tech"], civ.Tech.TechsToResearch.TakeLast(2));
        Assert.Equal(33, civ.Tech.TechsInProgress["New tech"]);
        Assert.False(civ.Tech.TechsInProgress.ContainsKey("Old tech"));

        BackwardCompatibility.ReplaceUpdatedTechName(civ.Tech, "Missing", "Whatever"); // nothing to replace
        Assert.DoesNotContain("Whatever", civ.Tech.TechsResearched);
    }

    [Fact]
    public void ChangeBuildingNameIfNotInRuleset_ReplacesQueuedAndInProgressEntries()
    {
        var civ = Civ();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        var constructions = city.CityConstructions;
        constructions.EditQueue(queue =>
        {
            queue.Clear();
            queue.AddRange(["Monument", "Old building"]);
        });
        constructions.InProgressConstructions["Old building"] = 40;

        BackwardCompatibility.ChangeBuildingNameIfNotInRuleset(Rules, constructions, "Old building", "Library");

        Assert.False(constructions.IsBuilt("Library"));
        Assert.Equal(["Monument", "Library"], constructions.ConstructionQueue.ToList());
        Assert.Equal(40, constructions.InProgressConstructions["Library"]);
        Assert.False(constructions.InProgressConstructions.ContainsKey("Old building"));
    }

    [Fact]
    public void ChangeBuildingNameIfNotInRuleset_BuildsTheNewNameForABuiltOldOne()
    {
        var civ = Civ();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        var constructions = city.CityConstructions;
        constructions.BuiltBuildings.Add("Old building");
        constructions.EditQueue(queue =>
        {
            queue.Clear();
            queue.AddRange(["Monument", "Old building"]);
        });

        BackwardCompatibility.ChangeBuildingNameIfNotInRuleset(Rules, constructions, "Old building", "Library");

        // Kotlin quirk kept: removeBuilding(name) is a no-op for a name the ruleset does not know, so the old name stays in the built set
        Assert.True(constructions.IsBuilt("Old building"));
        Assert.True(constructions.IsBuilt("Library"));
        Assert.Equal(["Monument"], constructions.ConstructionQueue.ToList()); // the new one is built now, so the old queue entry is dropped
    }

    [Fact]
    public void ChangeBuildingNameIfNotInRuleset_DropsTheOldEntriesWhenTheNewOneIsBuiltOrQueued()
    {
        var civ = Civ();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        var constructions = city.CityConstructions;
        constructions.BuiltBuildings.Add("Library");
        constructions.EditQueue(queue =>
        {
            queue.Clear();
            queue.AddRange(["Old building", "Monument"]);
        });
        constructions.InProgressConstructions["Old building"] = 40;

        BackwardCompatibility.ChangeBuildingNameIfNotInRuleset(Rules, constructions, "Old building", "Library");

        Assert.Equal(["Monument"], constructions.ConstructionQueue.ToList());
        Assert.False(constructions.InProgressConstructions.ContainsKey("Library")); // already built: progress is discarded
        Assert.False(constructions.InProgressConstructions.ContainsKey("Old building"));
    }

    [Fact]
    public void ChangeBuildingNameIfNotInRuleset_DoesNothingForBuildingsOfTheRuleset()
    {
        var civ = Civ();
        var city = game.AddCity(civ, game.GetTile(0, 0));
        city.CityConstructions.BuiltBuildings.Add("Monument");
        BackwardCompatibility.ChangeBuildingNameIfNotInRuleset(Rules, city.CityConstructions, "Monument", "Library");
        Assert.True(city.CityConstructions.IsBuilt("Monument"));
        Assert.False(city.CityConstructions.IsBuilt("Library"));
    }
}
