using System.Text.RegularExpressions;
using Unciv.Logic.City;
using Unciv.Logic.Civilization.Diplomacy;
using Unciv.Logic.Civilization.Managers;
using Unciv.Models.Ruleset;

namespace Unciv.Logic;

// Ported from core/src/com/unciv/logic/BackwardCompatibility.kt (T071).
//
// Kotlin extension functions of GameInfo / GameInfoPreview / ModOptions / TechManager are static methods taking the receiver as first
// parameter. Like the Kotlin object they are called only from GameInfo.SetTransients.
// Deviations:
// - The Kotlin loops that copy a collection with toList() before removing keep a snapshot (ToList) for the same reason.
// - ConvertFortify: Kotlin's Regex.matches needs the whole text to match, so the pattern is anchored with \z; the character classes are
//   spelled out (java \d, \w, \s are ASCII only, .NET's are Unicode). `turns.toInt()` throwing NumberFormatException on overflow
//   becomes int.Parse's OverflowException.
// - ModOptions.updateDeprecations is an empty template upstream (and has no caller): not ported.

/// <summary>Container for all temporarily used code managing transitions from deprecated elements to their replacements.</summary>
public static class BackwardCompatibility
{
    /// <summary>
    /// Mods can change, leading to things on the map that are no longer defined in the mod.
    /// This function removes them so the game doesn't crash when it tries to access them.
    /// </summary>
    public static void RemoveMissingModReferences(GameInfo gameInfo)
    {
        gameInfo.TileMap.RemoveMissingTerrainModReferences(gameInfo.Ruleset);

        RemoveUnitsAndPromotions(gameInfo);
        RemoveMissingGreatPersonPoints(gameInfo);

        // Mod decided you can't repair things anymore - get rid of old pillaged improvements
        RemoveOldPillagedImprovements(gameInfo);
        RemoveMissingLastSeenImprovements(gameInfo);

        HandleMissingReferencesForEachCity(gameInfo);

        RemoveTechAndPolicies(gameInfo);
        UpdateMissingStartingEra(gameInfo);
        MigrateCivId(gameInfo);
    }

    public static void MigrateCivId(GameInfo gameInfo)
    {
        foreach (var civ in gameInfo.Civilizations)
            if (civ.CivId == "") civ.CivId = civ.CivName;
    }

    public static void MigrateCivId(GameInfoPreview gameInfo)
    {
        foreach (var civ in gameInfo.Civilizations)
            if (civ.CivId == "") civ.CivId = civ.CivName;
    }

    private static void UpdateMissingStartingEra(GameInfo gameInfo)
    {
        if (gameInfo.Ruleset.Eras.ContainsKey(gameInfo.GameParameters.StartingEra)) return;
        gameInfo.GameParameters.StartingEra = gameInfo.Ruleset.Eras.Keys.First();
    }

    public static void MigrateGreatGeneralPools(GameInfo gameInfo)
    {
        foreach (var civ in gameInfo.Civilizations)
        {
            var greatPeople = civ.GreatPeople;
            if (greatPeople.PointsForNextGreatGeneral >= greatPeople.PointsForNextGreatGeneralCounter["Great General"])
                greatPeople.PointsForNextGreatGeneralCounter["Great General"] = greatPeople.PointsForNextGreatGeneral;
            else greatPeople.PointsForNextGreatGeneral = greatPeople.PointsForNextGreatGeneralCounter["Great General"];
        }
    }

    private static void RemoveUnitsAndPromotions(GameInfo gameInfo)
    {
        var ruleset = gameInfo.Ruleset;
        foreach (var tile in gameInfo.TileMap.Values)
        {
            foreach (var unit in tile.GetUnits().ToList())
            {
                if (!ruleset.Units.ContainsKey(unit.Name)) tile.RemoveUnit(unit);

                foreach (var promotion in unit.Promotions.Promotions.ToList())
                    if (!ruleset.UnitPromotions.ContainsKey(promotion))
                        unit.Promotions.Promotions.Remove(promotion);
            }
        }
    }

    private static void RemoveMissingGreatPersonPoints(GameInfo gameInfo)
    {
        var ruleset = gameInfo.Ruleset;
        foreach (var civ in gameInfo.Civilizations)
        {
            // Don't remove the 'points to next' counters, since pools do not necessarily correspond to unit names
            foreach (var key in civ.GreatPeople.GreatGeneralPointsCounter.Keys.ToList())
                if (!ruleset.Units.ContainsKey(key))
                    civ.GreatPeople.GreatGeneralPointsCounter.Remove(key);
            foreach (var key in civ.GreatPeople.GreatPersonPointsCounter.Keys.ToList())
                if (!ruleset.Units.ContainsKey(key))
                    civ.GreatPeople.GreatPersonPointsCounter.Remove(key);
        }
    }

    private static void RemoveOldPillagedImprovements(GameInfo gameInfo)
    {
        if (gameInfo.Ruleset.TileImprovements.ContainsKey(Constants.Repair)) return;
        foreach (var tile in gameInfo.TileMap.Values)
        {
            if (tile.RoadIsPillaged)
                tile.RemoveRoad();
            if (tile.ImprovementIsPillaged)
            {
                tile.Improvement = null;
                tile.ImprovementIsPillaged = false;
            }
        }
    }

    private static void RemoveMissingLastSeenImprovements(GameInfo gameInfo)
    {
        foreach (var civ in gameInfo.Civilizations)
            foreach (var (vector, improvementName) in civ.LastSeenImprovement.ToList())
                if (!gameInfo.Ruleset.TileImprovements.ContainsKey(improvementName))
                    civ.LastSeenImprovement.Remove(vector);
    }

    private static void HandleMissingReferencesForEachCity(GameInfo gameInfo)
    {
        var ruleset = gameInfo.Ruleset;
        bool IsInvalidConstruction(string construction) =>
            !ruleset.Buildings.ContainsKey(construction)
            && !ruleset.Units.ContainsKey(construction)
            && !PerpetualConstruction.IsNamePerpetual(construction);

        foreach (var city in gameInfo.Civilizations.SelectMany(it => it.Cities))
        {
            foreach (var building in city.CityConstructions.BuiltBuildings.ToList())
            {
                if (!ruleset.Buildings.ContainsKey(building))
                    city.CityConstructions.BuiltBuildings.Remove(building);
            }

            // Remove invalid buildings or units from the queue - don't just check buildings and units because it might be a special construction as well
            city.CityConstructions.EditQueue(queue => queue.RemoveAll(it => IsInvalidConstruction(it)));
            // And from being in progress
            foreach (var construction in city.CityConstructions.InProgressConstructions.Keys.ToList())
                if (IsInvalidConstruction(construction))
                    city.CityConstructions.InProgressConstructions.Remove(construction);
        }
    }

    private static void RemoveTechAndPolicies(GameInfo gameInfo)
    {
        var ruleset = gameInfo.Ruleset;
        foreach (var civInfo in gameInfo.Civilizations)
        {
            foreach (var tech in civInfo.Tech.TechsResearched.ToList())
                if (!ruleset.Technologies.ContainsKey(tech))
                    civInfo.Tech.TechsResearched.Remove(tech);

            foreach (var tech in civInfo.Tech.TechsToResearch.ToList())
                if (!ruleset.Technologies.ContainsKey(tech))
                    civInfo.Tech.TechsToResearch.Remove(tech);

            foreach (var policy in civInfo.Policies.AdoptedPolicies.ToList())
                if (!ruleset.Policies.ContainsKey(policy))
                    civInfo.Policies.AdoptedPolicies.Remove(policy);
        }
    }

    /// <summary>
    /// Replaces all occurrences of <paramref name="oldBuildingName"/> in <paramref name="cityConstructions"/> with <paramref name="newBuildingName"/>
    /// if the former is not contained in the ruleset.
    /// </summary>
    internal static void ChangeBuildingNameIfNotInRuleset(Ruleset ruleSet, CityConstructions cityConstructions, string oldBuildingName, string newBuildingName)
    {
        if (ruleSet.Buildings.ContainsKey(oldBuildingName))
            return;
        // Replace in built buildings
        if (cityConstructions.IsBuilt(oldBuildingName))
        {
            cityConstructions.RemoveBuilding(oldBuildingName);
            cityConstructions.AddBuilding(newBuildingName);
        }
        // Replace in construction queue
        if (!cityConstructions.IsBuilt(newBuildingName) && !cityConstructions.ConstructionQueue.Contains(newBuildingName))
            cityConstructions.EditQueue(queue =>
            {
                for (var i = 0; i < queue.Count; i++)
                    if (queue[i] == oldBuildingName) queue[i] = newBuildingName;
            });
        else
            cityConstructions.EditQueue(queue => queue.RemoveAll(it => it == oldBuildingName));
        // Replace in in-progress constructions
        if (cityConstructions.InProgressConstructions.ContainsKey(oldBuildingName))
        {
            if (!cityConstructions.IsBuilt(newBuildingName) && !cityConstructions.InProgressConstructions.ContainsKey(newBuildingName))
                cityConstructions.InProgressConstructions[newBuildingName] = cityConstructions.InProgressConstructions[oldBuildingName];
            cityConstructions.InProgressConstructions.Remove(oldBuildingName);
        }
    }

    /// <summary>Replace a changed tech name</summary>
    internal static void ReplaceUpdatedTechName(TechManager tech, string oldTechName, string newTechName)
    {
        if (tech.TechsResearched.Contains(oldTechName))
        {
            tech.TechsResearched.Remove(oldTechName);
            tech.TechsResearched.Add(newTechName);
        }
        var index = tech.TechsToResearch.IndexOf(oldTechName);
        if (index >= 0)
            tech.TechsToResearch[index] = newTechName;
        if (tech.TechsInProgress.ContainsKey(oldTechName))
        {
            tech.TechsInProgress[newTechName] = tech.ResearchOfTech(oldTechName);
            tech.TechsInProgress.Remove(oldTechName);
        }
    }

    /// <summary>Replace a deprecated DiplomacyFlags instance</summary>
    public static void ReplaceDiplomacyFlag(GameInfo gameInfo, DiplomacyFlags oldFlag, DiplomacyFlags newFlag)
    {
        foreach (var manager in gameInfo.Civilizations.SelectMany(civ => civ.Diplomacy.Values).ToList())
        {
            if (!manager.HasFlag(oldFlag)) continue;
            var value = manager.GetFlag(oldFlag);
            manager.RemoveFlag(oldFlag);
            manager.SetFlag(newFlag, value);
        }
    }

    private static readonly Regex FortifyRegex = new(@"^Fortify[ \t\n\x0B\f\r]+([0-9]+)([A-Za-z0-9_ \t\n\x0B\f\r]*)\z", RegexOptions.CultureInvariant);

    /// <summary>Convert from Fortify X to Fortify and save off X</summary>
    public static void ConvertFortify(GameInfo gameInfo)
    {
        foreach (var civInfo in gameInfo.Civilizations)
        {
            foreach (var unit in civInfo.Units.GetCivUnits())
            {
                if (unit.Action == null) continue;
                var match = FortifyRegex.Match(unit.Action);
                if (!match.Success) continue;
                unit.TurnsFortified = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                unit.Action = "Fortify" + match.Groups[2].Value;
            }
        }
    }

    public static void MigrateToTileHistory(GameInfo gameInfo)
    {
        if (gameInfo.HistoryStartTurn >= 0) return;
        foreach (var tile in gameInfo.GetCities().SelectMany(it => it.GetTiles()).ToList())
            tile.History.RecordTakeOwnership(tile);
        gameInfo.HistoryStartTurn = gameInfo.Turns;
    }

    public static void EnsureUnitIds(GameInfo gameInfo)
    {
        gameInfo.EnsureLastUnitId();
        foreach (var unit in gameInfo.TileMap.Values.SelectMany(it => it.GetUnits()).ToList())
            if (unit.Id == Constants.NoId) unit.Id = gameInfo.GetNextUnitId();
    }
}
